using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
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
    Stream? bitmapStream, atlasStream;
    bool visualDrag, followMovingHost, preparingWindowMove;
    Win32Rect visualBounds;
    int uploadedVersion;
    int motionX, motionY, motionDirectionX, motionDirectionY;
    int displayedWidth, displayedHeight;
    IntPtr renderWindowHandle;
    byte[]? spareScenePixels, sceneScratchPixels;
    internal Action? BeforeRender;
    bool disposed, frozen, inFlight, paused, layoutProbeInFlight;
    WindowBackdropCapture.Frame? atlas;
    int atlasX, atlasY, atlasVersion, displayedVersion, displayedX = int.MinValue, displayedY;
    AnalysisRegions analysisRegions = AnalysisRegions.Empty;
    Metrics metrics = Metrics.None;
    double[] statsColumns = Array.Empty<double>();
    DateTime lastCapture;
    DateTime lastCaptureCompletedUtc;
    long lastRenderAt;
    long lastLayoutProbe, backgroundMotionUntil;
    ulong layoutStamp;
    long contentRevision;
    ulong capturedLayoutStamp;
    long capturedContentRevision;
    bool hasCapturedSceneStamp;
    bool hasLayoutStamp;
    long lastSceneProbeAt;
    long layoutBurstUntil;
    ulong lastSceneProbeLayout;
    const int MovingLayoutProbeInterval = 32;
    const int LayoutBurstMilliseconds = 180;
    const int CaptureMargin = 256;
    const int CaptureInterval = 100;
    const int MovingCaptureInterval = 32, MovingCaptureMargin = 256;
    internal string? LastError { get; private set; }
    internal Action? FrameUpdated;
    internal Action? MetricsUpdated;
    internal bool IsActive { get; private set; }
    internal double Luminance => Volatile.Read(ref metrics).Luminance;
    internal double TextLuminance => Volatile.Read(ref metrics).TextLuminance;
    internal double? TextRegionLuminance => Volatile.Read(ref metrics).TextRegionLuminance;
    internal bool? StatsDark => Volatile.Read(ref metrics).StatsDark;
    internal bool IsReady => IsActive && sourceBrush.ImageSource != null && LastError == null;
    internal int FrameCount { get; private set; }
    internal int CapturedWindows { get; private set; }

    sealed class AnalysisRegions
    {
        internal static readonly AnalysisRegions Empty = new(0, 0, 0, 0, 0, 0, 0, 0);
        internal readonly int TextX, TextY, TextW, TextH, StatsX, StatsY, StatsW, StatsH;
        internal AnalysisRegions(int textX, int textY, int textW, int textH,
            int statsX, int statsY, int statsW, int statsH)
        {
            TextX = textX; TextY = textY; TextW = textW; TextH = textH;
            StatsX = statsX; StatsY = statsY; StatsW = statsW; StatsH = statsH;
        }
        internal bool HasText => TextW > 0 && TextH > 0;
        internal bool HasStats => StatsW > 0 && StatsH > 0;
    }

    sealed class Metrics
    {
        internal static readonly Metrics None = new(.5, .5, null, null);
        internal readonly double Luminance;
        internal readonly double TextLuminance;
        internal readonly double? TextRegionLuminance;
        internal readonly bool? StatsDark;
        internal Metrics(double luminance, double textLuminance, double? textRegionLuminance, bool? statsDark)
        { Luminance = luminance; TextLuminance = textLuminance; TextRegionLuminance = textRegionLuminance; StatsDark = statsDark; }
        internal bool Differs(Metrics other) =>
            Math.Abs(Luminance - other.Luminance) > .001 ||
            Math.Abs(TextLuminance - other.TextLuminance) > .001 ||
            NullableDiff(TextRegionLuminance, other.TextRegionLuminance) ||
            StatsDark != other.StatsDark;
        static bool NullableDiff(double? left, double? right) =>
            left.HasValue != right.HasValue || (left.HasValue && Math.Abs(left.Value - right.Value) > .001);
    }

    internal void SetAnalysisRegions(int textX, int textY, int textW, int textH,
        int statsX, int statsY, int statsW, int statsH)
        => Volatile.Write(ref analysisRegions, new AnalysisRegions(textX, textY, textW, textH, statsX, statsY, statsW, statsH));

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
        Volatile.Write(ref lastSceneProbeAt, 0);
        Volatile.Write(ref layoutBurstUntil, 0);
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
        Volatile.Write(ref lastSceneProbeAt, 0);
        Volatile.Write(ref layoutBurstUntil, 0);
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
        int cardX = x, cardY = y, cardWidth = w, cardHeight = h;
        var regions = Volatile.Read(ref analysisRegions);
        int previousAtlasX = atlasX, previousAtlasY = atlasY;
        inFlight = true;
        try
        {
            var captured = await Task.Run(() =>
            {
                long composeStarted = GlassProfile.Timestamp();
                double layoutMs = 0;
                bool layoutProbed = false;
                if (movingScene)
                {
                    long probeNow = Environment.TickCount64;
                    int probeInterval = probeNow < Volatile.Read(ref layoutBurstUntil) ? 0 : MovingLayoutProbeInterval;
                    layoutProbed = probeNow - Volatile.Read(ref lastSceneProbeAt) >= probeInterval;
                    if (layoutProbed)
                    {
                        ulong previousProbeLayout = lastSceneProbeLayout;
                        long layoutStarted = GlassProfile.Timestamp();
                        sceneLayout = WindowBackdropCapture.GetLayoutStamp(handle, left, top, right - left, bottom - top);
                        layoutMs = GlassProfile.Ms(layoutStarted);
                        Volatile.Write(ref lastSceneProbeAt, probeNow);
                        lastSceneProbeLayout = sceneLayout;
                        if (sceneLayout != previousProbeLayout) Volatile.Write(ref layoutBurstUntil, probeNow + LayoutBurstMilliseconds);
                    }
                    else sceneLayout = lastSceneProbeLayout;
                    sceneContent = CompositorWindowCapture.Revision;
                    // Only transform the existing texture when its screen-anchored scene is unchanged.
                    if (capturedStamp && compareScene && previous != null &&
                        previous.Width == right - left && previous.Height == bottom - top &&
                        sceneLayout == previousLayout && sceneContent == previousContent && recentlyCaptured)
                    {
                        double skippedCompose = GlassProfile.Ms(composeStarted);
                        long skippedMetricsStarted = GlassProfile.Timestamp();
                        var skippedMetrics = ComputeMetrics(previous.PixelsBGRA, previous.Width, previous.Height,
                            previousAtlasX, previousAtlasY, cardX, cardY, cardWidth, cardHeight, regions);
                        return (Frame: previous, Unchanged: true, Scratch: scratchBuffer, Skipped: true,
                            ComposeMs: skippedCompose, Pending: false, LayoutMs: layoutMs, Probed: layoutProbed,
                            Analysis: skippedMetrics, MetricsMs: GlassProfile.Ms(skippedMetricsStarted));
                    }
                }
                int byteCount = checked((right - left) * (bottom - top) * 4);
                var scratch = scratchBuffer?.Length != byteCount ? new byte[byteCount] : scratchBuffer;
                var fresh = WindowBackdropCapture.CaptureAt(handle, left, top, right - left, bottom - top, outputBuffer, scratch);
                double composeMs = GlassProfile.Ms(composeStarted);
                long metricsStarted = GlassProfile.Timestamp();
                var analysis = fresh.Success
                    ? ComputeMetrics(fresh.PixelsBGRA, fresh.Width, fresh.Height, left, top,
                        cardX, cardY, cardWidth, cardHeight, regions)
                    : null;
                double metricsMs = GlassProfile.Ms(metricsStarted);
                bool unchanged = compareScene && fresh.Success && previous != null &&
                    fresh.Width == previous.Width && fresh.Height == previous.Height &&
                    fresh.PixelsBGRA.AsSpan().SequenceEqual(previous.PixelsBGRA);
                return (Frame: fresh, Unchanged: unchanged, Scratch: scratch, Skipped: false,
                    ComposeMs: composeMs, Pending: fresh.PendingRegion, LayoutMs: layoutMs, Probed: layoutProbed,
                    Analysis: analysis, MetricsMs: metricsMs);
            });
            bool metricsChanged = false;
            if (captured.Analysis != null)
            {
                var previousMetrics = Volatile.Read(ref metrics);
                metricsChanged = captured.Analysis.Differs(previousMetrics);
                Volatile.Write(ref metrics, captured.Analysis);
            }
            GlassProfile.Capture(captured.ComposeMs, captured.MetricsMs, captured.LayoutMs, captured.Probed,
                captured.Pending, captured.Unchanged, captured.Skipped, captured.Frame.CapturedWindows);
            if (captured.Skipped)
            {
                if (metricsChanged) MetricsUpdated?.Invoke();
                return;
            }
            lastCapture = DateTime.UtcNow;
            var frame = captured.Frame;
            if (disposed || frozen || !IsActive) return;
            if (!frame.Success) { if (!frame.PendingRegion) LastError = frame.Error; return; }
            lastCaptureCompletedUtc = DateTime.UtcNow;
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
            else if (metricsChanged) MetricsUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            if (!disposed) LastError = ex.GetType().Name;
        }
        finally { inFlight = false; }
    }

    internal void SynchronizeGeometry()
    {
        var radius = card.CornerRadius;
        if (!image.CornerRadius.Equals(radius)) image.CornerRadius = radius;
        if (!image.Width.Equals(surface.Width)) image.Width = surface.Width;
        if (!image.Height.Equals(surface.Height)) image.Height = surface.Height;
    }

    internal bool SynchronizeVisualFrame() => RenderLatest();

    internal bool AtlasCovers(int x, int y, int width, int height)
        => atlas != null && x >= atlasX && y >= atlasY &&
           x + width <= atlasX + atlas.Width && y + height <= atlasY + atlas.Height;

    bool RenderLatest()
    {
        if (disposed || frozen || paused || !IsActive || atlas == null || !IsWindowVisible(handle) || IsIconic(handle)) return false;
        long renderStarted = GlassProfile.Timestamp();
        double uploadMs = 0;
        SynchronizeGeometry();
        if (!TryGetRenderBounds(out int x, out int y, out int w, out int h)) return false;
        if (followMovingHost) ObserveWindowMotion(x, y);
        int offsetX = x - atlasX, offsetY = y - atlasY;
        if (offsetX < 0 || offsetY < 0 || offsetX + w > atlas.Width || offsetY + h > atlas.Height)
        {
            // Keep the last complete frame until the new screen region arrives.
            // Clearing here exposes the black shader input with stale dark text.
            Capture();
            return false;
        }
        if (displayedVersion == atlasVersion && displayedX == x && displayedY == y &&
            displayedWidth == w && displayedHeight == h &&
            (visualDrag ? uploadedVersion == atlasVersion : bitmap?.PixelWidth == w && bitmap.PixelHeight == h) &&
            sourceBrush.ImageSource != null) return false;
        if (visualDrag)
        {
            if (atlasBitmap == null || atlasBitmap.PixelWidth != atlas.Width || atlasBitmap.PixelHeight != atlas.Height)
            {
                atlasStream?.Dispose();
                atlasBitmap = new WriteableBitmap(atlas.Width, atlas.Height);
                atlasStream = atlasBitmap.PixelBuffer.AsStream();
                uploadedVersion = 0;
            }
            atlasStream ??= atlasBitmap.PixelBuffer.AsStream();
            if (uploadedVersion != atlasVersion)
            {
                long uploadStarted = GlassProfile.Timestamp();
                atlasStream.Position = 0;
                atlasStream.Write(atlas.PixelsBGRA, 0, atlas.PixelsBGRA.Length);
                atlasBitmap.Invalidate();
                uploadedVersion = atlasVersion;
                uploadMs = GlassProfile.Ms(uploadStarted);
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
                bitmapStream?.Dispose();
                bitmap = new WriteableBitmap(w, h);
                bitmapStream = bitmap.PixelBuffer.AsStream();
            }
            bitmapStream ??= bitmap.PixelBuffer.AsStream();
            long uploadStarted = GlassProfile.Timestamp();
            int sourceStride = atlas.Width * 4;
            int sourceOffset = (offsetY * atlas.Width + offsetX) * 4;
            var stream = bitmapStream;
            stream.Position = 0;
            for (int row = 0; row < h; row++)
                stream.Write(atlas.PixelsBGRA, sourceOffset + row * sourceStride, w * 4);
            bitmap.Invalidate();
            uploadMs = GlassProfile.Ms(uploadStarted);
            sourceBrush.Transform = null;
            sourceBrush.Stretch = Stretch.Fill;
            if (sourceBrush.ImageSource != bitmap) sourceBrush.ImageSource = bitmap;
        }
        displayedX = x; displayedY = y; displayedWidth = w; displayedHeight = h; displayedVersion = atlasVersion;
        FrameCount++;
        long appearanceStarted = GlassProfile.Timestamp();
        FrameUpdated?.Invoke();
        double appearanceMs = GlassProfile.Ms(appearanceStarted);
        double intervalMs = lastRenderAt == 0 ? 0 : GlassProfile.Ms(lastRenderAt);
        lastRenderAt = GlassProfile.Timestamp();
        double captureToRenderMs = lastCaptureCompletedUtc == default ? 0 : (DateTime.UtcNow - lastCaptureCompletedUtc).TotalMilliseconds;
        GlassProfile.Render(GlassProfile.Ms(renderStarted), uploadMs, appearanceMs, intervalMs, captureToRenderMs,
            visualDrag, true, visualDrag ? (long)atlas.Width * atlas.Height : (long)w * h, atlasVersion);
        return true;
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

    Metrics? ComputeMetrics(byte[] pixels, int frameWidth, int frameHeight, int frameX, int frameY,
        int cardX, int cardY, int cardWidth, int cardHeight, AnalysisRegions regions)
    {
        if (cardWidth <= 0 || cardHeight <= 0 || frameWidth <= 0 || frameHeight <= 0 ||
            pixels.Length < (long)frameWidth * frameHeight * 4) return null;
        int left = cardX - frameX, top = cardY - frameY;
        if (left < 0 || top < 0 || left + cardWidth > frameWidth || top + cardHeight > frameHeight) return null;
        double light = 0, textLight = 0;
        int samples = 0, textSamples = 0;
        int textBoundary = (int)(cardWidth * .55);
        for (int row = 0; row < cardHeight; row += 4)
        for (int column = 0; column < cardWidth; column += 4)
        {
            int index = ((top + row) * frameWidth + left + column) * 4;
            double value = (pixels[index] * .0722 + pixels[index + 1] * .7152 + pixels[index + 2] * .2126) / 255;
            light += value; samples++;
            if (column <= textBoundary) { textLight += value; textSamples++; }
        }
        double luminance = samples == 0 ? .5 : light / samples;
        double textLuminance = textSamples == 0 ? .5 : textLight / textSamples;

        double? textRegionLuminance = null;
        if (regions.HasText)
        {
            int regionX = left + regions.TextX, regionY = top + regions.TextY;
            int regionW = regions.TextW, regionH = regions.TextH;
            if (regionX >= 0 && regionY >= 0 && regionX + regionW <= frameWidth && regionY + regionH <= frameHeight)
            {
                double regionLight = 0;
                int regionSamples = 0;
                for (int row = 0; row < regionH; row += 4)
                for (int column = 0; column < regionW; column += 4)
                {
                    int index = ((regionY + row) * frameWidth + regionX + column) * 4;
                    regionLight += (pixels[index] * .0722 + pixels[index + 1] * .7152 + pixels[index + 2] * .2126) / 255;
                    regionSamples++;
                }
                if (regionSamples != 0) textRegionLuminance = regionLight / regionSamples;
            }
        }

        bool? statsDark = null;
        if (regions.HasStats)
        {
            int regionX = left + regions.StatsX, regionY = top + regions.StatsY;
            int regionW = regions.StatsW, regionH = regions.StatsH;
            if (regionX >= 0 && regionY >= 0 && regionX + regionW <= frameWidth && regionY + regionH <= frameHeight)
            {
                if (statsColumns.Length < regionW) statsColumns = new double[regionW];
                Array.Clear(statsColumns, 0, regionW);
                bool allDark = true, allLight = true;
                // Every row AND column must agree; a thin opposite-colour strip still holds the current tone.
                for (int row = 0; row < regionH; row++)
                {
                    double rowLight = 0;
                    for (int column = 0; column < regionW; column++)
                    {
                        int index = ((regionY + row) * frameWidth + regionX + column) * 4;
                        double value = (pixels[index] * .0722 + pixels[index + 1] * .7152 + pixels[index + 2] * .2126) / 255;
                        rowLight += value; statsColumns[column] += value;
                    }
                    allDark &= rowLight / regionW < .44;
                    allLight &= rowLight / regionW > .56;
                    if (!allDark && !allLight) return new Metrics(luminance, textLuminance, textRegionLuminance, null);
                }
                for (int column = 0; column < regionW; column++)
                {
                    allDark &= statsColumns[column] / regionH < .44;
                    allLight &= statsColumns[column] / regionH > .56;
                    if (!allDark && !allLight) return new Metrics(luminance, textLuminance, textRegionLuminance, null);
                }
                statsDark = allDark;
            }
        }
        return new Metrics(luminance, textLuminance, textRegionLuminance, statsDark);
    }

    internal bool TryGetHostRenderBounds(out int x, out int y, out int width, out int height)
    {
        // WinUI keeps a one-pixel inset above this XAML child even in a borderless HWND.
        IntPtr renderWindow = renderWindowHandle;
        if (renderWindow == IntPtr.Zero || !IsWindow(renderWindow))
        {
            IntPtr child = FindWindowEx(handle, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
            renderWindowHandle = child;
            renderWindow = child != IntPtr.Zero ? child : handle;
        }
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
        bitmapStream?.Dispose(); atlasStream?.Dispose();
        bitmapStream = atlasStream = null;
        bitmap = atlasBitmap = null;
        BeforeRender = null;
        metrics = Metrics.None;
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
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
}
