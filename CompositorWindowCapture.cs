using System;
using System.Collections.Generic;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace OffWorkWidget.WinUI;

// Alpha-preserving capture for transparent underlying windows. Never requests capture consent.
internal static class CompositorWindowCapture
{
    internal sealed class Frame : IDisposable
    {
        int references = 1;
        readonly bool pooled;
        internal int Width { get; }
        internal int Height { get; }
        internal int PixelLeft { get; }
        internal int PixelTop { get; }
        internal int PixelWidth { get; }
        internal int PixelHeight { get; }
        internal long PixelCount => (long)PixelWidth * PixelHeight;
        // Premultiplied BGRA8. Consumers must treat this published snapshot as read-only.
        internal byte[] PixelsBGRA { get; }
        internal DateTime CaptureStartedUtc { get; }
        internal DateTime CapturedAtUtc { get; }
        internal Frame(int width, int height, byte[] pixels, DateTime started)
            : this(width, height, pixels, started, 0, 0, width, height) { }
        internal Frame(int width, int height, byte[] pixels, DateTime started, int left, int top, int pixelWidth, int pixelHeight, bool pooled = false)
        { Width = width; Height = height; PixelsBGRA = pixels; CaptureStartedUtc = started; CapturedAtUtc = DateTime.UtcNow;
            PixelLeft = left; PixelTop = top; PixelWidth = pixelWidth; PixelHeight = pixelHeight; this.pooled = pooled; }
        internal bool Retain()
        {
            int old;
            do { old = Volatile.Read(ref references); if (old == 0) return false; }
            while (Interlocked.CompareExchange(ref references, old + 1, old) != old);
            return true;
        }
        // Each successful TryGetFrame acquisition releases one reader reference.
        public void Dispose()
        { if (Interlocked.Decrement(ref references) == 0 && pooled) ArrayPool<byte>.Shared.Return(PixelsBGRA); }
    }

    sealed class Region
    {
        internal readonly int X, Y, Width, Height;
        internal Region(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
    }

    const long PixelBudget = 12_000_000;
    static readonly object gate = new object();
    static readonly Dictionary<IntPtr, Source> sources = new Dictionary<IntPtr, Source>();
    static readonly Dictionary<IntPtr, long> retryAfter = new Dictionary<IntPtr, long>();
    const int FrameIntervalMilliseconds = 100, FastFrameIntervalMilliseconds = 0, FailureRetryMilliseconds = 5000;
    static readonly Timer reaper = new Timer(_ => Reap(), null, 1000, 1000);
    static long cachedPixels;
    static string? lastError;
    static long revision;
    internal static long Revision => Interlocked.Read(ref revision);
    internal static string? LastError => Volatile.Read(ref lastError);

    // First use starts capture on a worker and returns immediately, without waiting for a frame.
    internal static bool TryGetFrame(IntPtr hwnd, out Frame? frame)
        => TryGetFrame(hwnd, null, out frame);

    internal static bool TryGetFrame(IntPtr hwnd, int x, int y, int width, int height, out Frame? frame)
        => TryGetFrame(hwnd, new Region(x, y, width, height), out frame);

    static bool TryGetFrame(IntPtr hwnd, Region? region, out Frame? frame)
    {
        frame = null;
        if (hwnd == IntPtr.Zero) return false;
        Source source;
        lock (gate)
        {
            if (retryAfter.TryGetValue(hwnd, out long retry))
            {
                if (Environment.TickCount64 < retry) return false;
                retryAfter.Remove(hwnd);
            }
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
            source.Touch(region);
        }
        frame = source.Latest;
        return frame != null;
    }

    // A newly starting GPU source has not failed. Do not synchronously repaint
    // its owner window just because the first compositor frame is still pending.
    internal static bool IsStarting(IntPtr hwnd)
    {
        lock (gate) return sources.TryGetValue(hwnd, out var source) && source.IsStarting;
    }

    internal static void ReleaseAll()
    {
        Source[] removed;
        lock (gate) { removed = new List<Source>(sources.Values).ToArray(); sources.Clear(); retryAfter.Clear(); }
        foreach (var source in removed) _ = Task.Run(source.Dispose);
    }

    internal static void Reset() => ReleaseAll();

    internal static void RequestRefresh(IntPtr hwnd)
    {
        Source? source;
        lock (gate) sources.TryGetValue(hwnd, out source);
        source?.RequestRefresh();
    }

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
        readonly long createdAt = Environment.TickCount64;
        long lastUse = Environment.TickCount64, lastRead, lastSizeCheck;
        int poolWidth, poolHeight, widthInset, heightInset;
        bool disposed, inFlight, closed;
        Frame? latest;
        Region? requestedRegion;
        NativeCaptureCrop? crop;
        bool cropDisabled, fastCapture;
        long contentActiveUntil;
        bool regionRefresh;
        int refreshScheduled;
        Direct3D11CaptureFrame? pending;
        Direct3D11CaptureFrame? retained;
        IDirect3DSurface? retainedSurface;
        IDirect3DDevice? device;
        GraphicsCaptureItem? item;
        Direct3D11CaptureFramePool? pool;
        GraphicsCaptureSession? session;
        internal IntPtr Handle { get; }
        internal long LastUse => Interlocked.Read(ref lastUse);
        internal Source(IntPtr hwnd) { Handle = hwnd; }
        internal void Touch(Region? region)
        {
            Volatile.Write(ref requestedRegion, region);
            long now = Environment.TickCount64;
            if (Volatile.Read(ref fastCapture) && now >= Volatile.Read(ref contentActiveUntil))
                _ = Task.Run(() => { lock (sync) { if (Environment.TickCount64 >= contentActiveUntil) SetCaptureRate(false); } });
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
                    if (disposed || pool == null) return;
                    int width = bounds.Right - bounds.Left - widthInset, height = bounds.Bottom - bounds.Top - heightInset;
                    if (width <= 0 || height <= 0 || (long)width * height > PixelBudget || (width == poolWidth && height == poolHeight)) return;
                    // Some layered windows keep an old-size capture item after resizing. Recreate the
                    // source instead of scaling that stale frame into the new native bounds.
                    Volatile.Write(ref closed, true);
                    Dispose();
                }
            }
            catch (Exception ex) { Fail(ex); }
        }
        void Fail(Exception error)
        {
            // A denied/unsupported source must not recreate D3D devices every sampling tick.
            lock (gate)
            {
                if (sources.TryGetValue(Handle, out var current) && ReferenceEquals(current, this))
                {
                    if (retryAfter.Count >= 128)
                    {
                        IntPtr oldest = IntPtr.Zero; long expiry = long.MaxValue;
                        foreach (var entry in retryAfter)
                            if (entry.Value < expiry) { oldest = entry.Key; expiry = entry.Value; }
                        retryAfter.Remove(oldest);
                    }
                    retryAfter[Handle] = Environment.TickCount64 + FailureRetryMilliseconds;
                }
            }
            Volatile.Write(ref closed, true);
            Volatile.Write(ref lastError, error.Message);
            Dispose();
        }
        internal bool IsStarting => !Volatile.Read(ref disposed) && !Volatile.Read(ref closed) &&
            Environment.TickCount64 - createdAt < 500;
        internal bool IsExpired => Volatile.Read(ref closed) || Environment.TickCount64 - Interlocked.Read(ref lastUse) >= 3000;
        internal Frame? Latest
        {
            get
            {
                while (!Volatile.Read(ref disposed))
                {
                    var frame = Volatile.Read(ref latest);
                    if (frame == null) return null;
                    if (frame.Retain()) return frame;
                }
                return null;
            }
        }

        internal void RequestRefresh()
        {
            if (Interlocked.Exchange(ref refreshScheduled, 1) != 0) return;
            _ = Task.Run(() =>
            {
                try
                {
                    lock (sync)
                    {
                        if (disposed) return;
                        regionRefresh = true;
                        if (inFlight || retained == null) return;
                        inFlight = true;
                        _ = Task.Run(ReadFramesAsync);
                    }
                }
                finally { Volatile.Write(ref refreshScheduled, 0); }
            });
        }

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
                // One retained frame for recropping, one being read, and one pending producer frame.
                newPool = Direct3D11CaptureFramePool.CreateFreeThreaded(newDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, newItem.Size);
                newSession = newPool.CreateCaptureSession(newItem);
                newSession.IsCursorCaptureEnabled = false;
                LimitCaptureRate(newSession);
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
            catch (Exception ex) { Fail(ex); }
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
                catch (Exception ex) { Fail(ex); }
            }
        }

        async Task ReadFramesAsync()
        {
            try
            {
                while (true)
                {
                    bool fast = !cropDisabled && crop != null && Environment.TickCount64 < contentActiveUntil;
                    SetCaptureRate(fast);
                    bool refreshRequested;
                    lock (sync)
                    {
                        // Do not hold the reader active in a timer wait when no newer frame exists.
                        if (disposed || (pending == null && !regionRefresh)) return;
                        refreshRequested = regionRefresh;
                    }
                    int interval = refreshRequested || fast ? FastFrameIntervalMilliseconds : FrameIntervalMilliseconds;
                    int delay = (int)Math.Max(0, interval - (Environment.TickCount64 - lastRead));
                    if (delay > 0) await Task.Delay(delay);
                    Direct3D11CaptureFrame? captured;
                    lock (sync)
                    {
                        if (disposed) return;
                        if (pending != null)
                        {
                            retainedSurface = null;
                            retained?.Dispose(); retained = pending; pending = null;
                        }
                        else if (!regionRefresh || retained == null) return;
                        captured = retained; regionRefresh = false;
                        lastRead = Environment.TickCount64;
                    }
                    {
                        var size = captured.ContentSize;
                        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > PixelBudget) continue;
                        // This surface is borrowed from the frame pool. Closing it also closes
                        // a recycled texture used by later frames. Only dispose the owning frame.
                        var surface = retainedSurface ??= captured.Surface;
                        var original = surface.Description;
                        if (original.Width != size.Width || original.Height != size.Height)
                        { Volatile.Write(ref closed, true); Dispose(); return; }
                        int left = 0, top = 0, pixelWidth = size.Width, pixelHeight = size.Height;
                        bool cropped = false;
                        var region = Volatile.Read(ref requestedRegion);
                        if (!cropDisabled && region != null &&
                            WindowBackdropCapture.TryGetCaptureOrigin(Handle, size.Width, size.Height, out int originX, out int originY))
                        {
                            pixelWidth = Math.Min(size.Width, region.Width + 192);
                            pixelHeight = Math.Min(size.Height, region.Height + 192);
                            left = Math.Clamp(region.X - originX - 96, 0, size.Width - pixelWidth);
                            top = Math.Clamp(region.Y - originY - 96, 0, size.Height - pixelHeight);
                            try
                            {
                                crop ??= new NativeCaptureCrop(device!);
                                crop.Copy(surface, left, top, pixelWidth, pixelHeight);
                                cropped = true;
                            }
                            catch (Exception error)
                            {
                                DisableCrop(error);
                                left = top = 0; pixelWidth = size.Width; pixelHeight = size.Height;
                            }
                        }
                        int width = pixelWidth, height = pixelHeight;
                        long count = (long)width * height;
                        if (count <= 0 || count > PixelBudget) continue;
                        byte[] pixels = ArrayPool<byte>.Shared.Rent(checked((int)(count * 4)));
                        bool published = false;
                        try
                        {
                            if (cropped)
                            {
                                try { crop!.ReadPixels(pixels); }
                                catch (Exception error)
                                {
                                    DisableCrop(error); cropped = false;
                                    left = top = 0; width = size.Width; height = size.Height;
                                    count = (long)width * height;
                                    var fullPixels = ArrayPool<byte>.Shared.Rent(checked((int)(count * 4)));
                                    ArrayPool<byte>.Shared.Return(pixels); pixels = fullPixels;
                                }
                            }
                            if (!cropped)
                            {
                                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface, BitmapAlphaMode.Premultiplied);
                                if (bitmap.PixelWidth != width || bitmap.PixelHeight != height)
                                { Volatile.Write(ref closed, true); Dispose(); return; }
                                NativeCaptureCrop.CopyPixels(bitmap, pixels);
                            }
                            // An evicted source releases its pixel reservation on a worker.
                            // Retain a static source's only frame while that release completes.
                            for (int retry = 0; retry < 20; retry++)
                            {
                                lock (sync)
                                {
                                    if (disposed) return;
                                    var previousFrame = latest;
                                    long previous = previousFrame?.PixelCount ?? 0;
                                    if (Reserve(count - previous))
                                    {
                                        bool changed = previousFrame == null || previousFrame.Width != size.Width || previousFrame.Height != size.Height ||
                                            previousFrame.PixelLeft != left || previousFrame.PixelTop != top || previousFrame.PixelWidth != width ||
                                            previousFrame.PixelHeight != height || !pixels.AsSpan(0, (int)(count * 4)).SequenceEqual(previousFrame.PixelsBGRA.AsSpan(0, (int)(count * 4)));
                                        Volatile.Write(ref latest, new Frame(size.Width, size.Height, pixels, started, left, top, width, height, true));
                                        published = true;
                                        previousFrame?.Dispose();
                                        if (changed)
                                        {
                                            if (cropped) contentActiveUntil = Environment.TickCount64 + 350;
                                            Interlocked.Increment(ref revision);
                                        }
                                        break;
                                    }
                                }
                                await Task.Delay(100);
                            }
                        }
                        finally { if (!published) ArrayPool<byte>.Shared.Return(pixels); }
                    }
                }
            }
            catch (Exception ex) { Fail(ex); }
            finally
            {
                lock (sync)
                {
                    inFlight = false;
                    if (disposed) ReleaseResources();
                    else if (pending != null || (regionRefresh && retained != null)) { inFlight = true; _ = Task.Run(ReadFramesAsync); }
                }
            }
        }
        void SetCaptureRate(bool fast)
        {
            lock (sync)
            {
                if (disposed || session == null || fastCapture == fast) return;
                LimitCaptureRate(session, fast ? FastFrameIntervalMilliseconds : FrameIntervalMilliseconds);
                Volatile.Write(ref fastCapture, fast);
            }
        }
        void DisableCrop(Exception error)
        {
            Volatile.Write(ref lastError, "GPU crop fallback: " + error.Message);
            cropDisabled = true; crop?.Dispose(); crop = null;
            SetCaptureRate(false);
        }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                pending?.Dispose(); pending = null;
                if (latest != null) { Interlocked.Add(ref cachedPixels, -latest.PixelCount); latest.Dispose(); latest = null; }
                if (!inFlight) ReleaseResources();
            }
        }

        void ReleaseResources()
        {
            if (item != null) { try { item.Closed -= ItemClosed; } catch { } }
            if (pool != null) { try { pool.FrameArrived -= FrameArrived; } catch { } }
            try { session?.Dispose(); } catch { }
            retainedSurface = null;
            retained?.Dispose(); retained = null;
            try { pool?.Dispose(); } catch { }
            try { crop?.Dispose(); } catch { }
            try { device?.Dispose(); } catch { }
            if (item != null) { try { ((IWinRTObject)item).NativeObject.Dispose(); } catch { } }
            session = null; pool = null; device = null; item = null; crop = null;
        }
    }

    static void LimitCaptureRate(GraphicsCaptureSession capture, int milliseconds = FrameIntervalMilliseconds)
    {
        if (!Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
            "Windows.Graphics.Capture.GraphicsCaptureSession", "MinUpdateInterval")) return;
        try
        {
            var timing = capture.As<IGraphicsCaptureSession5>();
            try { timing.MinUpdateInterval = TimeSpan.FromMilliseconds(milliseconds).Ticks; }
            finally { if (Marshal.IsComObject(timing)) Marshal.ReleaseComObject(timing); }
        }
        catch (Exception error) when (error is COMException || error is InvalidCastException || error is NotImplementedException)
        { /* Older capture implementations retain the CPU readback limit. */ }
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

    [ComImport, Guid("67c0ea62-1f85-5061-925a-239be0ac09cb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureSession5
    {
        void GetIids(out uint count, out IntPtr values);
        void GetRuntimeClassName(out IntPtr value);
        void GetTrustLevel(out int value);
        long MinUpdateInterval { get; set; }
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
