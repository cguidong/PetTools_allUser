using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace OffWorkWidget.WinUI;

// Alpha-preserving capture for transparent underlying windows. Never requests capture consent.
internal static class CompositorWindowCapture
{
    internal sealed class Frame
    {
        internal int Width { get; }
        internal int Height { get; }
        // Premultiplied BGRA8. Consumers must treat this published snapshot as read-only.
        internal byte[] PixelsBGRA { get; }
        internal DateTime CaptureStartedUtc { get; }
        internal DateTime CapturedAtUtc { get; }
        internal Frame(int width, int height, byte[] pixels, DateTime started)
        { Width = width; Height = height; PixelsBGRA = pixels; CaptureStartedUtc = started; CapturedAtUtc = DateTime.UtcNow; }
    }

    const long PixelBudget = 12_000_000;
    static readonly object gate = new object();
    static readonly Dictionary<IntPtr, Source> sources = new Dictionary<IntPtr, Source>();
    static readonly Timer reaper = new Timer(_ => Reap(), null, 1000, 1000);
    static long cachedPixels;
    static string? lastError;
    internal static string? LastError => Volatile.Read(ref lastError);

    // First use starts capture on a worker and returns immediately, without waiting for a frame.
    internal static bool TryGetFrame(IntPtr hwnd, out Frame? frame)
    {
        frame = null;
        if (hwnd == IntPtr.Zero) return false;
        Source source;
        lock (gate)
        {
            if (sources.TryGetValue(hwnd, out var expired) && expired.IsExpired)
            { sources.Remove(hwnd); _ = Task.Run(expired.Dispose); }
            if (!sources.TryGetValue(hwnd, out source!))
            {
                if (sources.Count >= 4)
                {
                    Source? oldest = null;
                    foreach (var candidate in sources.Values)
                        if (oldest == null || candidate.LastUse < oldest.LastUse) oldest = candidate;
                    // Do not evict sources still being visited in the same sampling pass.
                    if (oldest == null || Environment.TickCount64 - oldest.LastUse < 100) return false;
                    sources.Remove(oldest.Handle);
                    _ = Task.Run(oldest.Dispose);
                }
                source = new Source(hwnd);
                sources.Add(hwnd, source);
                _ = Task.Run(source.Initialize);
            }
            source.Touch();
        }
        frame = source.Latest;
        return frame != null;
    }

    internal static void ReleaseAll()
    {
        Source[] removed;
        lock (gate) { removed = new List<Source>(sources.Values).ToArray(); sources.Clear(); }
        foreach (var source in removed) _ = Task.Run(source.Dispose);
    }

    internal static void Reset() => ReleaseAll();

    static void Reap()
    {
        var removed = new List<Source>();
        lock (gate)
        {
            foreach (var pair in sources)
                if (pair.Value.IsExpired || !IsWindow(pair.Key)) removed.Add(pair.Value);
            foreach (var source in removed) sources.Remove(source.Handle);
        }
        foreach (var source in removed) source.Dispose();
    }

    static bool Reserve(long delta)
    {
        while (true)
        {
            long old = Interlocked.Read(ref cachedPixels), next = old + delta;
            if (next > PixelBudget) return false;
            if (Interlocked.CompareExchange(ref cachedPixels, next, old) == old) return true;
        }
    }

    sealed class Source : IDisposable
    {
        readonly object sync = new object();
        readonly DateTime started = DateTime.UtcNow;
        long lastUse = Environment.TickCount64, lastRead, lastSizeCheck;
        int poolWidth, poolHeight, widthInset, heightInset;
        bool disposed, inFlight, closed;
        Frame? latest;
        Direct3D11CaptureFrame? pending;
        IDirect3DDevice? device;
        GraphicsCaptureItem? item;
        Direct3D11CaptureFramePool? pool;
        GraphicsCaptureSession? session;
        internal IntPtr Handle { get; }
        internal long LastUse => Interlocked.Read(ref lastUse);
        internal Source(IntPtr hwnd) { Handle = hwnd; }
        internal void Touch()
        {
            long now = Environment.TickCount64;
            Interlocked.Exchange(ref lastUse, now);
            long previous = Interlocked.Read(ref lastSizeCheck);
            if (now - previous >= 100 && Interlocked.CompareExchange(ref lastSizeCheck, now, previous) == previous)
                _ = Task.Run(CheckSize);
        }

        void CheckSize()
        {
            try
            {
                if (!GetWindowRect(Handle, out var bounds)) return;
                lock (sync)
                {
                    if (disposed || pool == null || inFlight) return;
                    int width = bounds.Right - bounds.Left - widthInset, height = bounds.Bottom - bounds.Top - heightInset;
                    if (width <= 0 || height <= 0 || (long)width * height > PixelBudget || (width == poolWidth && height == poolHeight)) return;
                    // Some layered windows keep an old-size capture item after resizing. Recreate the
                    // source instead of scaling that stale frame into the new native bounds.
                    Volatile.Write(ref closed, true);
                    Dispose();
                }
            }
            catch (Exception ex) { Volatile.Write(ref closed, true); Volatile.Write(ref lastError, ex.Message); Dispose(); }
        }
        internal bool IsExpired => Volatile.Read(ref closed) || Environment.TickCount64 - Interlocked.Read(ref lastUse) >= 3000;
        internal Frame? Latest => Volatile.Read(ref disposed) ? null : Volatile.Read(ref latest);

        internal void Initialize()
        {
            IDirect3DDevice? newDevice = null;
            GraphicsCaptureItem? newItem = null;
            Direct3D11CaptureFramePool? newPool = null;
            GraphicsCaptureSession? newSession = null;
            try
            {
                if (!IsWindow(Handle) || !GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Window compositor capture is unavailable.");
                newDevice = CreateDevice();
                newItem = CreateItem(Handle);
                if (newItem.Size.Width <= 0 || newItem.Size.Height <= 0 || (long)newItem.Size.Width * newItem.Size.Height > PixelBudget)
                    throw new InvalidOperationException("Window capture exceeds the pixel budget.");
                newPool = Direct3D11CaptureFramePool.CreateFreeThreaded(newDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newItem.Size);
                newSession = newPool.CreateCaptureSession(newItem);
                newSession.IsCursorCaptureEnabled = false;
                // If this ABI is unavailable, fail closed rather than changing consent or using black-keying.
                var border = newSession.As<IGraphicsCaptureSession3>();
                try { border.IsBorderRequired = false; }
                finally { if (Marshal.IsComObject(border)) Marshal.ReleaseComObject(border); }
                lock (sync)
                {
                    if (disposed) return;
                    device = newDevice; newDevice = null;
                    item = newItem; newItem = null;
                    pool = newPool; newPool = null;
                    session = newSession; newSession = null;
                    poolWidth = item.Size.Width; poolHeight = item.Size.Height;
                    if (GetWindowRect(Handle, out var bounds))
                    { widthInset = bounds.Right - bounds.Left - poolWidth; heightInset = bounds.Bottom - bounds.Top - poolHeight; }
                    item.Closed += ItemClosed;
                    pool.FrameArrived += FrameArrived;
                    session.StartCapture();
                }
            }
            catch (Exception ex) { Volatile.Write(ref closed, true); Volatile.Write(ref lastError, ex.Message); Dispose(); }
            finally { newSession?.Dispose(); newPool?.Dispose(); newDevice?.Dispose(); if (newItem != null) ((IWinRTObject)newItem).NativeObject.Dispose(); }
        }

        void ItemClosed(GraphicsCaptureItem sender, object args)
        { Volatile.Write(ref closed, true); Dispose(); }

        void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            lock (sync)
            {
                if (disposed) return;
                try
                {
                    var next = sender.TryGetNextFrame();
                    if (next == null) return;
                    pending?.Dispose();
                    pending = next;
                    if (inFlight) return;
                    inFlight = true;
                    _ = Task.Run(ReadFramesAsync);
                }
                catch (Exception ex) { Volatile.Write(ref closed, true); Volatile.Write(ref lastError, ex.Message); Dispose(); }
            }
        }

        async Task ReadFramesAsync()
        {
            try
            {
                while (true)
                {
                    int delay = (int)Math.Max(0, 100 - (Environment.TickCount64 - lastRead));
                    if (delay > 0) await Task.Delay(delay);
                    Direct3D11CaptureFrame? captured;
                    lock (sync)
                    {
                        if (disposed || pending == null) return;
                        captured = pending; pending = null;
                        lastRead = Environment.TickCount64;
                    }
                    try
                    {
                        var size = captured.ContentSize;
                        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > PixelBudget) continue;
                        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface, BitmapAlphaMode.Premultiplied);
                        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
                        if (width != size.Width || height != size.Height)
                        {
                            Volatile.Write(ref closed, true);
                            Dispose();
                            return;
                        }
                        long count = (long)width * height;
                        if (count <= 0 || count > PixelBudget) continue;
                        var buffer = new Windows.Storage.Streams.Buffer(checked((uint)(count * 4)));
                        bitmap.CopyToBuffer(buffer);
                        var pixels = new byte[buffer.Length];
                        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
                        // An evicted source releases its pixel reservation on a worker.
                        // Retain a static source's only frame while that release completes.
                        for (int retry = 0; retry < 20; retry++)
                        {
                            lock (sync)
                            {
                                if (disposed) return;
                                long previous = latest == null ? 0 : (long)latest.Width * latest.Height;
                                if (Reserve(count - previous))
                                { Volatile.Write(ref latest, new Frame(width, height, pixels, started)); break; }
                            }
                            await Task.Delay(100);
                        }
                    }
                    finally { captured?.Dispose(); }
                }
            }
            catch (Exception ex) { Volatile.Write(ref closed, true); Volatile.Write(ref lastError, ex.Message); Dispose(); }
            finally
            {
                lock (sync)
                {
                    inFlight = false;
                    if (disposed) ReleaseResources();
                    else if (pending != null) { inFlight = true; _ = Task.Run(ReadFramesAsync); }
                }
            }
        }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                pending?.Dispose(); pending = null;
                if (latest != null) { Interlocked.Add(ref cachedPixels, -(long)latest.Width * latest.Height); latest = null; }
                if (!inFlight) ReleaseResources();
            }
        }

        void ReleaseResources()
        {
            if (item != null) { try { item.Closed -= ItemClosed; } catch { } }
            if (pool != null) { try { pool.FrameArrived -= FrameArrived; } catch { } }
            try { session?.Dispose(); } catch { }
            try { pool?.Dispose(); } catch { }
            try { device?.Dispose(); } catch { }
            if (item != null) { try { ((IWinRTObject)item).NativeObject.Dispose(); } catch { } }
            session = null; pool = null; device = null; item = null;
        }
    }

    static IDirect3DDevice CreateDevice()
    {
        IntPtr native = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero, wrapped = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out native, out _, out context));
            var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, ref iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out wrapped));
            return MarshalInterface<IDirect3DDevice>.FromAbi(wrapped);
        }
        finally
        {
            if (wrapped != IntPtr.Zero) Marshal.Release(wrapped);
            if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (native != IntPtr.Zero) Marshal.Release(native);
        }
    }

    static GraphicsCaptureItem CreateItem(IntPtr hwnd)
    {
        IntPtr name = IntPtr.Zero, factory = IntPtr.Zero, result = IntPtr.Zero;
        IGraphicsCaptureItemInterop? interop = null;
        try
        {
            const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Marshal.ThrowExceptionForHR(WindowsCreateString(className, (uint)className.Length, out name));
            var iid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out factory));
            interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            interop.CreateForWindow(hwnd, ref itemId, out result);
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(result);
        }
        finally
        {
            if (result != IntPtr.Zero) Marshal.Release(result);
            if (interop != null && Marshal.IsComObject(interop)) Marshal.ReleaseComObject(interop);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (name != IntPtr.Zero) WindowsDeleteString(name);
        }
    }

    [ComImport, Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureSession3
    {
        void GetIids(out uint count, out IntPtr values);
        void GetRuntimeClassName(out IntPtr value);
        void GetTrustLevel(out int value);
        bool IsBorderRequired { [return: MarshalAs(UnmanagedType.U1)] get; [param: MarshalAs(UnmanagedType.U1)] set; }
    }
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(IntPtr hwnd, ref Guid iid, out IntPtr result);
        void CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, uint driver, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdk, out IntPtr device, out uint chosen, out IntPtr context);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);
    [DllImport("combase.dll")] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string text, uint length, out IntPtr value);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
}
