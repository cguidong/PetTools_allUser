using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OffWorkWidget.WinUI;

// Captures only underlying windows. The widget remains visible to screenshots.
internal sealed class DesktopGlassSource : IDisposable
{
    readonly IntPtr handle;
    readonly Grid surface;
    readonly Border card;
    readonly Border image = new Border { CornerRadius = new CornerRadius(36), IsHitTestVisible = false };
    readonly ImageBrush sourceBrush = new ImageBrush { Stretch = Stretch.Fill };
    readonly MatrixTransform samplingTransform = new MatrixTransform();
    readonly DispatcherQueueTimer timer;
    WriteableBitmap? bitmap, atlasBitmap;
    bool visualDrag, followMovingHost, preparingWindowMove;
    Win32Rect visualBounds;
    int uploadedVersion;
    int motionX, motionY, motionDirectionX, motionDirectionY;
    int displayedWidth, displayedHeight;
    byte[]? spareScenePixels, sceneScratchPixels;
    internal Action? BeforeRender;
    bool disposed, frozen, inFlight, paused, layoutProbeInFlight;
    WindowBackdropCapture.Frame? atlas;
    int atlasX, atlasY, atlasVersion, displayedVersion, displayedX = int.MinValue, displayedY;
    byte[] pixels = Array.Empty<byte>();
    DateTime lastCapture;
    long lastLayoutProbe, backgroundMotionUntil;
    ulong layoutStamp;
    long contentRevision;
    ulong capturedLayoutStamp;
    long capturedContentRevision;
    bool hasCapturedSceneStamp;
    bool hasLayoutStamp;
    const int CaptureMargin = 256;
    const int CaptureInterval = 100;
    const int MovingCaptureInterval = 32, MovingCaptureMargin = 256;
    internal string? LastError { get; private set; }
    internal Action? FrameUpdated;
    internal bool IsActive { get; private set; }
    internal double Luminance { get; private set; } = .5;
    internal double TextLuminance { get; private set; } = .5;
    internal bool IsReady => IsActive && sourceBrush.ImageSource != null && LastError == null;
    internal int FrameCount { get; private set; }
    internal int CapturedWindows { get; private set; }
    internal void FreezeForPreview() { frozen = true; timer.Stop(); }

    internal void Pause()
    {
        paused = true;
        timer.Stop();
    }

    internal void Resume()
    {
        if (disposed || frozen || !IsActive) return;
        paused = false;
        timer.Start();
    }

    internal DesktopGlassSource(Window window, Grid surface, Border card)
    {
        handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        this.surface = surface;
        this.card = card;
        // Clear the old setting if a reused HWND ever carries it; never exclude capture.
        SetWindowDisplayAffinity(handle, 0);
        timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(CaptureInterval);
        timer.Tick += (_, __) => RequestFrame();
        card.Loaded += (_, __) => Start();
        if (card.IsLoaded) Start();
    }

    internal void Start()
    {
        if (disposed || IsActive) return;
        image.Background = sourceBrush;
        surface.Children.Insert(0, image);
        IsActive = true;
        CompositionTarget.Rendering += OnRendering;
        RequestFrame();
        timer.Start();
    }

    void OnRendering(object? sender, object args)
    {
        BeforeRender?.Invoke();
        if (followMovingHost) RequestFrame();
        else UpdateBackgroundMotion();
        if (visualDrag) RenderLatest();
    }

    async void UpdateBackgroundMotion()
    {
        if (disposed || frozen || paused || inFlight || layoutProbeInFlight || !IsActive || visualDrag || preparingWindowMove ||
            !IsWindowVisible(handle) || IsIconic(handle)) return;
        long now = Environment.TickCount64;
        int interval = now < backgroundMotionUntil ? 0 : 50;
        if (now - lastLayoutProbe < interval) return;
        lastLayoutProbe = now;
        if (!TryGetRenderBounds(out int x, out int y, out int width, out int height)) return;
        layoutProbeInFlight = true;
        try
        {
            // Enumeration and cross-process DWM queries must never delay pointer/render work.
            ulong next = await Task.Run(() => WindowBackdropCapture.GetLayoutStamp(handle, x - 48, y - 48, width + 96, height + 96));
            if (disposed || frozen || paused || visualDrag || preparingWindowMove) return;
            long nextContent = CompositorWindowCapture.Revision;
            now = Environment.TickCount64;
            if ((hasLayoutStamp && next != layoutStamp) || nextContent != contentRevision) backgroundMotionUntil = now + 180;
            layoutStamp = next;
            contentRevision = nextContent;
            hasLayoutStamp = true;
            if (now < backgroundMotionUntil) RequestFrame();
        }
        catch (Exception ex) { if (!disposed) LastError = ex.GetType().Name; }
        finally { layoutProbeInFlight = false; }
    }

    internal void BeginVisualDrag(int x, int y, int width, int height)
    {
        visualDrag = true;
        timer.Interval = TimeSpan.FromMilliseconds(MovingCaptureInterval);
        followMovingHost = false;
        UpdateVisualBounds(x, y, width, height);
        uploadedVersion = 0;
        displayedX = int.MinValue;
        RenderLatest();
    }

    internal void PrepareWindowMove()
    {
        preparingWindowMove = true;
        Capture();
    }

    internal void BeginWindowMove()
    {
        if (!TryGetHostRenderBounds(out int x, out int y, out int width, out int height)) return;
        BeginVisualDrag(x, y, width, height);
        // Keep native drag sizing unchanged; sample the live child-window origin.
        followMovingHost = true;
        motionX = x; motionY = y; motionDirectionX = motionDirectionY = 0;
        Capture();
    }

    internal void EndWindowMove()
    {
        preparingWindowMove = false;
        spareScenePixels = sceneScratchPixels = null;
        if (followMovingHost) EndVisualDrag();
    }

    void ObserveWindowMotion(int x, int y)
    {
        ObserveAxisMotion(x, ref motionX, ref motionDirectionX);
        ObserveAxisMotion(y, ref motionY, ref motionDirectionY);
    }

    static void ObserveAxisMotion(int value, ref int extreme, ref int direction)
    {
        int delta = value - extreme;
        if (delta == 0) return;
        if (Math.Sign(delta) == direction) extreme = value;
        else if (Math.Abs(delta) >= 16)
        {
            // Ignore small hand jitter before reversing the prefetch direction.
            extreme = value;
            direction = Math.Sign(delta);
        }
    }

    internal void UpdateVisualBounds(int x, int y, int width, int height)
    {
        visualBounds = new Win32Rect { Left = x, Top = y, Right = x + width, Bottom = y + height };
    }

    internal void EndVisualDrag()
    {
        visualDrag = false;
        timer.Interval = TimeSpan.FromMilliseconds(CaptureInterval);
        followMovingHost = false;
        displayedX = int.MinValue;
        // Publish a complete normal crop in this UI turn; retain the old image
        // and transform when a new screen region has not arrived yet.
        RenderLatest();
    }

    // Motion samples a screen-anchored scene without waiting for the source app.
    internal void Capture()
    {
        if (followMovingHost) { RequestFrame(); return; }
        int interval = visualDrag || preparingWindowMove ? MovingCaptureInterval : CaptureInterval;
        if (DateTime.UtcNow - lastCapture >= TimeSpan.FromMilliseconds(interval)) RequestFrame();
    }

    async void RequestFrame()
    {
        if (disposed || frozen || paused || inFlight || !IsActive || !IsWindowVisible(handle) || IsIconic(handle)) return;
        if (preparingWindowMove && !followMovingHost &&
            DateTime.UtcNow - lastCapture < TimeSpan.FromMilliseconds(MovingCaptureInterval)) return;
        if (!TryGetRenderBounds(out int x, out int y, out int w, out int h)) return;
        if (followMovingHost) ObserveWindowMotion(x, y);
        int margin = followMovingHost || preparingWindowMove ? MovingCaptureMargin : visualDrag ? CaptureMargin : 48;
        // Preserve look-ahead along motion without reading a full margin on the idle axis.
        int marginX = followMovingHost && motionDirectionX == 0 ? MovingCaptureMargin / 2 : margin;
        int marginY = followMovingHost && motionDirectionY == 0 ? MovingCaptureMargin / 2 : margin;
        int leadX = followMovingHost ? motionDirectionX * (MovingCaptureMargin / 2) : 0;
        int leadY = followMovingHost ? motionDirectionY * (MovingCaptureMargin / 2) : 0;
        int left = Math.Min(x, Math.Max(GetSystemMetrics(76), x + leadX - marginX));
        int top = Math.Min(y, Math.Max(GetSystemMetrics(77), y + leadY - marginY));
        int right = Math.Max(x + w, Math.Min(GetSystemMetrics(76) + GetSystemMetrics(78), x + w + leadX + marginX));
        int bottom = Math.Max(y + h, Math.Min(GetSystemMetrics(77) + GetSystemMetrics(79), y + h + leadY + marginY));
        int probeX = Math.Clamp(x + leadX, GetSystemMetrics(76), Math.Max(GetSystemMetrics(76), GetSystemMetrics(76) + GetSystemMetrics(78) - w));
        int probeY = Math.Clamp(y + leadY, GetSystemMetrics(77), Math.Max(GetSystemMetrics(77), GetSystemMetrics(77) + GetSystemMetrics(79) - h));
        if (right <= left || bottom <= top) return;
        // Keep the scene screen-anchored while there is room to move inside it.
        // Recentring every capture changes every pixel even on a static desktop.
        if (followMovingHost && atlas != null && x >= atlasX && y >= atlasY &&
            x + w <= atlasX + atlas.Width && y + h <= atlasY + atlas.Height &&
            (probeX >= atlasX + 32 || atlasX <= GetSystemMetrics(76)) &&
            (probeY >= atlasY + 32 || atlasY <= GetSystemMetrics(77)) &&
            (probeX + w <= atlasX + atlas.Width - 32 || atlasX + atlas.Width >= GetSystemMetrics(76) + GetSystemMetrics(78)) &&
            (probeY + h <= atlasY + atlas.Height - 32 || atlasY + atlas.Height >= GetSystemMetrics(77) + GetSystemMetrics(79)))
        {
            left = atlasX; top = atlasY;
            right = left + atlas.Width; bottom = top + atlas.Height;
        }
        var previous = atlas;
        // Keep immutable published pixels separate from the reusable worker buffers.
        var outputBuffer = spareScenePixels;
        var scratchBuffer = sceneScratchPixels;
        bool compareScene = left == atlasX && top == atlasY;
        ulong sceneLayout = 0;
        long sceneContent = 0;
        bool movingScene = followMovingHost;
        bool capturedStamp = hasCapturedSceneStamp;
        ulong previousLayout = capturedLayoutStamp;
        long previousContent = capturedContentRevision;
        bool recentlyCaptured = DateTime.UtcNow - lastCapture < TimeSpan.FromMilliseconds(CaptureInterval);
        inFlight = true;
        try
        {
            var captured = await Task.Run(() =>
            {
                if (movingScene)
                {
                    sceneLayout = WindowBackdropCapture.GetLayoutStamp(handle, left, top, right - left, bottom - top);
                    sceneContent = CompositorWindowCapture.Revision;
                    // Only transform the existing texture when its screen-anchored scene is unchanged.
                    if (capturedStamp && compareScene && previous != null &&
                        previous.Width == right - left && previous.Height == bottom - top &&
                        sceneLayout == previousLayout && sceneContent == previousContent && recentlyCaptured)
                        return (Frame: previous, Unchanged: true, Scratch: scratchBuffer, Skipped: true);
                }
                int byteCount = checked((right - left) * (bottom - top) * 4);
                var scratch = scratchBuffer?.Length != byteCount ? new byte[byteCount] : scratchBuffer;
                var fresh = WindowBackdropCapture.CaptureAt(handle, left, top, right - left, bottom - top, outputBuffer, scratch);
                bool unchanged = compareScene && fresh.Success && previous != null &&
                    fresh.Width == previous.Width && fresh.Height == previous.Height &&
                    fresh.PixelsBGRA.AsSpan().SequenceEqual(previous.PixelsBGRA);
                return (Frame: fresh, Unchanged: unchanged, Scratch: scratch, Skipped: false);
            });
            if (captured.Skipped) return;
            lastCapture = DateTime.UtcNow;
            var frame = captured.Frame;
            if (disposed || frozen || !IsActive) return;
            if (!frame.Success) { if (!frame.PendingRegion) LastError = frame.Error; return; }
            // Static captures often contain the same compositor snapshot. Recycle
            // the unconsumed result or old scene without reuploading unchanged pixels.
            spareScenePixels = captured.Unchanged ? frame.PixelsBGRA : previous?.PixelsBGRA;
            sceneScratchPixels = captured.Scratch;
            // Moving does not invalidate this snapshot: each pixel has a screen position.
            if (!captured.Unchanged)
            {
                atlas = frame;
                atlasX = left; atlasY = top;
                atlasVersion++;
            }
            if (followMovingHost)
            {
                capturedLayoutStamp = sceneLayout;
                capturedContentRevision = sceneContent;
                hasCapturedSceneStamp = true;
            }
            else hasCapturedSceneStamp = false;
            CapturedWindows = frame.CapturedWindows;
            LastError = null;
            // The UI continuation can publish before the next render tick; do not hold a ready frame.
            if (!captured.Unchanged) RenderLatest();
        }
        catch (Exception ex)
        {
            if (!disposed) LastError = ex.GetType().Name;
        }
        finally { inFlight = false; }
    }

    internal void SynchronizeGeometry()
    {
        image.CornerRadius = card.CornerRadius;
        image.Width = surface.Width; image.Height = surface.Height;
    }

    internal void SynchronizeVisualFrame() => RenderLatest();

    void RenderLatest()
    {
        if (disposed || frozen || paused || !IsActive || atlas == null || !IsWindowVisible(handle) || IsIconic(handle)) return;
        SynchronizeGeometry();
        if (!TryGetRenderBounds(out int x, out int y, out int w, out int h)) return;
        if (followMovingHost) ObserveWindowMotion(x, y);
        int offsetX = x - atlasX, offsetY = y - atlasY;
        if (offsetX < 0 || offsetY < 0 || offsetX + w > atlas.Width || offsetY + h > atlas.Height)
        {
            // Keep the last complete frame until the new screen region arrives.
            // Clearing here exposes the black shader input with stale dark text.
            Capture();
            return;
        }
        if (displayedVersion == atlasVersion && displayedX == x && displayedY == y &&
            displayedWidth == w && displayedHeight == h &&
            (visualDrag ? uploadedVersion == atlasVersion : bitmap?.PixelWidth == w && bitmap.PixelHeight == h) &&
            sourceBrush.ImageSource != null) return;
        if (visualDrag)
        {
            if (atlasBitmap == null || atlasBitmap.PixelWidth != atlas.Width || atlasBitmap.PixelHeight != atlas.Height)
            {
                atlasBitmap = new WriteableBitmap(atlas.Width, atlas.Height);
                uploadedVersion = 0;
            }
            if (uploadedVersion != atlasVersion)
            {
                using (Stream stream = atlasBitmap.PixelBuffer.AsStream())
                    stream.Write(atlas.PixelsBGRA, 0, atlas.PixelsBGRA.Length);
                atlasBitmap.Invalidate();
                uploadedVersion = atlasVersion;
            }
            sourceBrush.Stretch = Stretch.None;
            sourceBrush.AlignmentX = AlignmentX.Left;
            sourceBrush.AlignmentY = AlignmentY.Top;
            sourceBrush.Transform = samplingTransform;
            if (sourceBrush.ImageSource != atlasBitmap) sourceBrush.ImageSource = atlasBitmap;
            double pixelRatio = 96.0 / GetDpiForWindow(handle);
            // Reuse the uploaded scene while moving: only its screen-anchored
            // sampling transform changes until a fresh background frame arrives.
            samplingTransform.Matrix = new Matrix(pixelRatio, 0, 0, pixelRatio, -offsetX * pixelRatio, -offsetY * pixelRatio);
        }
        else
        {
            if (bitmap == null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
            {
                bitmap = new WriteableBitmap(w, h);
                pixels = new byte[checked(w * h * 4)];
            }
            int stride = w * 4;
            for (int row = 0; row < h; row++)
                Buffer.BlockCopy(atlas.PixelsBGRA, ((offsetY + row) * atlas.Width + offsetX) * 4, pixels, row * stride, stride);
            using (Stream stream = bitmap.PixelBuffer.AsStream())
                stream.Write(pixels, 0, pixels.Length);
            bitmap.Invalidate();
            sourceBrush.Transform = null;
            sourceBrush.Stretch = Stretch.Fill;
            if (sourceBrush.ImageSource != bitmap) sourceBrush.ImageSource = bitmap;
        }
        byte[] luminancePixels = visualDrag ? atlas.PixelsBGRA : pixels;
        int luminanceWidth = visualDrag ? atlas.Width : w;
        int luminanceX = visualDrag ? offsetX : 0, luminanceY = visualDrag ? offsetY : 0;
        double light = 0, textLight = 0;
        int samples = 0, textSamples = 0;
        for (int row = 0; row < h; row += 4)
        for (int column = 0; column < w; column += 4)
        {
            int index = ((row + luminanceY) * luminanceWidth + column + luminanceX) * 4;
            double value = (luminancePixels[index] * .0722 + luminancePixels[index + 1] * .7152 + luminancePixels[index + 2] * .2126) / 255;
            light += value; samples++;
            if (column <= (int)(w * .55)) { textLight += value; textSamples++; }
        }
        Luminance = samples == 0 ? .5 : light / samples;
        TextLuminance = textSamples == 0 ? .5 : textLight / textSamples;
        displayedX = x; displayedY = y; displayedWidth = w; displayedHeight = h; displayedVersion = atlasVersion;
        FrameCount++;
        FrameUpdated?.Invoke();
    }

    internal bool TryGetRenderBounds(out int x, out int y, out int width, out int height)
    {
        if (visualDrag && !followMovingHost)
        {
            x = visualBounds.Left; y = visualBounds.Top;
            width = visualBounds.Right - x; height = visualBounds.Bottom - y;
            return width > 0 && height > 0;
        }
        return TryGetHostRenderBounds(out x, out y, out width, out height);
    }

    internal bool TryGetRegionLuminance(int offsetX, int offsetY, int width, int height, out double light)
    {
        light = .5;
        if (atlas == null || width <= 0 || height <= 0 || !TryGetRenderBounds(out int x, out int y, out _, out _)) return false;
        long left = (long)x + offsetX - atlasX, top = (long)y + offsetY - atlasY;
        if (left < 0 || top < 0 || left + width > atlas.Width || top + height > atlas.Height) return false;
        double total = 0;
        int samples = 0;
        for (int row = 0; row < height; row += 4)
        for (int column = 0; column < width; column += 4)
        {
            int index = (((int)top + row) * atlas.Width + (int)left + column) * 4;
            total += (atlas.PixelsBGRA[index] * .0722 + atlas.PixelsBGRA[index + 1] * .7152 + atlas.PixelsBGRA[index + 2] * .2126) / 255;
            samples++;
        }
        light = total / samples;
        return true;
    }

    double[] regionColumns = Array.Empty<double>();
    internal bool TryGetUniformRegionTone(int offsetX, int offsetY, int width, int height, out bool dark)
    {
        dark = false;
        if (atlas == null || width <= 0 || height <= 0 || !TryGetRenderBounds(out int x, out int y, out _, out _)) return false;
        int left = x + offsetX - atlasX, top = y + offsetY - atlasY;
        if (left < 0 || top < 0 || left + width > atlas.Width || top + height > atlas.Height) return false;
        if (regionColumns.Length < width) regionColumns = new double[width];
        Array.Clear(regionColumns, 0, width);
        bool allDark = true, allLight = true;
        // Every row AND column must agree; a thin opposite-colour strip still holds the current tone.
        for (int row = 0; row < height; row++)
        {
            double rowLight = 0;
            for (int column = 0; column < width; column++)
            {
                int index = ((top + row) * atlas.Width + left + column) * 4;
                double light = (atlas.PixelsBGRA[index] * .0722 + atlas.PixelsBGRA[index + 1] * .7152 + atlas.PixelsBGRA[index + 2] * .2126) / 255;
                rowLight += light; regionColumns[column] += light;
            }
            allDark &= rowLight / width < .44;
            allLight &= rowLight / width > .56;
            if (!allDark && !allLight) return false;
        }
        for (int column = 0; column < width; column++)
        {
            allDark &= regionColumns[column] / height < .44;
            allLight &= regionColumns[column] / height > .56;
            if (!allDark && !allLight) return false;
        }
        dark = allDark;
        return true;
    }

    internal bool TryGetHostRenderBounds(out int x, out int y, out int width, out int height)
    {
        // WinUI keeps a one-pixel inset above this XAML child even in a borderless HWND.
        IntPtr renderWindow = FindWindowEx(handle, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (renderWindow == IntPtr.Zero) renderWindow = handle;
        x = y = width = height = 0;
        var origin = new Win32Point();
        if (!GetClientRect(renderWindow, out Win32Rect client) || !ClientToScreen(renderWindow, ref origin)) return false;
        x = origin.X; y = origin.Y;
        width = client.Right - client.Left; height = client.Bottom - client.Top;
        return width > 0 && height > 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        IsActive = false;
        timer.Stop();
        CompositionTarget.Rendering -= OnRendering;
        atlas = null;
        bitmap = atlasBitmap = null;
        BeforeRender = null;
        pixels = Array.Empty<byte>();
        spareScenePixels = sceneScratchPixels = null;
        surface.Children.Remove(image);
        sourceBrush.ImageSource = null;
        CompositorWindowCapture.ReleaseAll();
    }

    [StructLayout(LayoutKind.Sequential)] struct Win32Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Win32Point { internal int X, Y; }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Win32Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Win32Point point);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
}
