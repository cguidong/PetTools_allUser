using System;
using System.Diagnostics;
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
    // The parent composition clip supplies the single antialiased outer contour.
    readonly Border image = new Border { IsHitTestVisible = false };
    long nextOverlayCheck;
    readonly ImageBrush sourceBrush = new ImageBrush { Stretch = Stretch.Fill };
    readonly MatrixTransform samplingTransform = new MatrixTransform();
    readonly DispatcherQueueTimer timer;
    readonly DispatcherQueue dispatcher;
    WriteableBitmap? bitmap, atlasBitmap;
    Stream? bitmapStream, atlasStream;
    bool visualDrag, followMovingHost, preparingWindowMove, retainedVisualBounds;
    Win32Rect visualBounds;
    int uploadedVersion;
    int motionX, motionY, motionDirectionX, motionDirectionY;
    int displayedWidth, displayedHeight;
    IntPtr renderWindowHandle;
    byte[]? spareScenePixels, sceneScratchPixels;
    internal Action? BeforeRender;
    internal Func<bool>? NeedsAnimation;
    bool rendering;
    internal void WakeRendering()
    {
        if (rendering || paused || disposed || !IsActive) return;
        CompositionTarget.Rendering += OnRendering;
        rendering = true;
    }
    void RestRendering()
    {
        if (!rendering) return;
        CompositionTarget.Rendering -= OnRendering;
        rendering = false;
    }
    bool disposed, frozen, inFlight, paused, layoutProbeInFlight;
    WindowBackdropCapture.Frame? atlas;
    int atlasX, atlasY, atlasVersion, displayedVersion, displayedX = int.MinValue, displayedY;
    AnalysisRegions analysisRegions = AnalysisRegions.Empty;
    Metrics metrics = Metrics.None;
    double[] statsColumns = Array.Empty<double>();
    long lastCaptureAt, lastPassStart;
    long captureStartedAt, blockingCooldownUntil;
    long blockingCooldown = BlockingCooldownMilliseconds;
    long lastMetricsAt, lastPublishAt, lastStallLogAt;
    int captureGeneration, pendingStreak;
    long pendingSince;
    string? pendingReason;
    bool pendingFillLogged;
    // A pending window must not freeze the whole glass for seconds. After this
    // grace period the capture fills the missing area from lower layers instead.
    const int PendingFillAfterMilliseconds = 150;
    // A pass blocked longer than this is treated as stuck and replaced.
    const int StuckCaptureMilliseconds = 1500;
    const int BlockingCooldownMilliseconds = 30000;
    const int MetricsRefreshMilliseconds = 200;
    DateTime lastCaptureCompletedUtc;
    long lastRenderAt, lastRenderingTick;
    long lastLayoutProbe, backgroundMotionUntil, backgroundLayoutUntil;
    ulong layoutStamp;
    long contentRevision;
    ulong capturedLayoutStamp;
    long capturedContentRevision;
    bool hasCapturedSceneStamp;
    bool hasLayoutStamp;
    int backgroundFrameKick, backgroundFrameWanted;
    long lastSceneProbeAt;
    long layoutBurstUntil;
    long lastRefreshProbe;
    ulong lastSceneProbeLayout;
    const int MovingLayoutProbeInterval = 32;
    const int LayoutBurstMilliseconds = 180;
    const int CaptureMargin = 256;
    const int CaptureInterval = 100;
    // Motion sampling follows the monitor's refresh rate; slower displays keep a
    // 17ms floor so the worker is never asked for more than the compositor shows.
    int motionInterval = 16;
    const int MovingCaptureMargin = 64;
    const int FastDragSpeed = 1400, FastMovingCaptureMargin = 128;
    // A window moving under the stationary widget is latency-sensitive; sample
    // at the display cadence for a short burst instead of the idle 100 ms cadence.
    int BackgroundMotionInterval => motionInterval;
    double dragSpeed;
    long dragSampleAt;
    int dragSampleX, dragSampleY;
    int windowMotionDirty;
    Win32Rect motionBounds;
    WinEventProc? windowEventProc;
    readonly IntPtr[] windowEventHooks = new IntPtr[4];
    // High-resolution monotonic time without requesting a global timer period.
    static long MotionClock => (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

    // Keep motion sampling on the display cadence instead of a fixed 60 Hz cap.
    static int DisplayRefreshInterval()
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            int hz = dc == IntPtr.Zero ? 0 : GetDeviceCaps(dc, 116 /* VREFRESH */);
            if (hz < 30 || hz > 500) hz = 60;
            // Floor keeps 60/99/120/144/165 Hz all on their own frame cadence.
            return Math.Clamp((int)Math.Floor(1000.0 / hz), 5, 17);
        }
        finally
        {
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
        }
    }
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
            left.HasValue != right.HasValue || (left is double a && right is double b && Math.Abs(a - b) > .001);
    }

    internal void SetAnalysisRegions(int textX, int textY, int textW, int textH,
        int statsX, int statsY, int statsW, int statsH)
        => Volatile.Write(ref analysisRegions, new AnalysisRegions(textX, textY, textW, textH, statsX, statsY, statsW, statsH));

    internal void FreezeForPreview() { frozen = true; timer.Stop(); }

    internal void Pause()
    {
        if (paused) return;
        paused = true;
        timer.Stop();
        RestRendering();
        CompositorWindowCapture.ReleaseAll();
    }

    internal void Resume()
    {
        if (disposed || frozen || !IsActive || !paused) return;
        paused = false;
        WakeRendering();
        timer.Start();
        RequestFrame();
    }

    internal DesktopGlassSource(Window window, Grid surface, Border card)
    {
        handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        this.surface = surface;
        // The opt-in experiment excludes the widget from screen capture. Normal runs remain visible.
        if (ScreenCaptureExperiment.Enabled)
        {
            ScreenCaptureExperiment.AffinityReady = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
                SetWindowDisplayAffinity(handle, 0x11);
            if (GlassProfile.Enabled) GlassProfile.Event("screen-capture-test affinity=" + ScreenCaptureExperiment.AffinityReady);
            if (!ScreenCaptureExperiment.AffinityReady)
                MessageBoxW(handle, "当前系统无法启用无黄边采样，玻璃背景暂时不可用。请联系开发者检查。", "有盼", 0x30);
        }
        else SetWindowDisplayAffinity(handle, 0);
        dispatcher = window.DispatcherQueue;
        timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(CaptureInterval);
        timer.Tick += (_, __) =>
        {
            // Rendering samples every drag frame. Keep the timer as a fallback
            // if rendering stops, instead of running a second drag capture loop.
            int minimumInterval = followMovingHost && rendering ? CaptureInterval : motionInterval;
            if (MotionClock - lastPassStart >= minimumInterval) RequestFrame();
        };
        card.Loaded += (_, __) => Start();
        if (card.IsLoaded) Start();
    }

    internal void UpdateRemoteBackdrop()
    {
        if (ScreenCaptureExperiment.Enabled || disposed || !IsActive || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            Environment.TickCount64 < nextOverlayCheck) return;
        nextOverlayCheck = Environment.TickCount64 + 600;
        if (!GetWindowRect(handle, out var w)) return;
        bool active = false;
        IntPtr overlay = IntPtr.Zero;
        while ((overlay = FindWindowEx(IntPtr.Zero, overlay, "ORAY_FLUTTER_VIEW_CONTAINER", null)) != IntPtr.Zero)
        {
            if (!WindowBackdropCapture.IsWindowAvailable(overlay) || !GetWindowRect(overlay, out var r)) continue;
            if (r.Left < w.Right && w.Left < r.Right && r.Top < w.Bottom && w.Top < r.Bottom)
            {
                active = true;
                break;
            }
        }
        if (active == ScreenCaptureExperiment.RemoteEnabled) return;
        lock (ScreenCaptureExperiment.RemoteGate)
        {
            if (!SetWindowDisplayAffinity(handle, active ? 0x11u : 0u)) return;
            ScreenCaptureExperiment.RemoteEnabled = active;
        }
        Capture();
    }

    internal void Start()
    {
        if (disposed || IsActive) return;
        motionInterval = DisplayRefreshInterval();
        image.Background = sourceBrush;
        surface.Children.Insert(0, image);
        IsActive = true;
        lastPublishAt = MotionClock;
        WakeRendering();
        StartWindowEvents();
        CompositorWindowCapture.ContentChanged += OnContentChanged;
        RequestFrame();
        timer.Start();
    }

    void OnRendering(object? sender, object args)
    {
        // A remote-session or display change can invalidate composition state;
        // never let that exception escape into the XAML loop and kill the app.
        try
        {
            if (GlassProfile.Enabled)
            {
                GlassProfile.Pacing(lastRenderingTick == 0 ? 0 : GlassProfile.Ms(lastRenderingTick), followMovingHost,
                    paused || frozen || !IsWindowVisible(handle) || IsIconic(handle), inFlight, layoutProbeInFlight);
                lastRenderingTick = GlassProfile.Timestamp();
            }
            BeforeRender?.Invoke();
            if (!visualDrag && !preparingWindowMove && !followMovingHost &&
                MotionClock >= backgroundMotionUntil && NeedsAnimation?.Invoke() != true)
            {
                if (!frozen)
                    timer.Interval = TimeSpan.FromMilliseconds(atlas?.LiveUpdates == true ? 500 : CaptureInterval);
                RestRendering();
                return;
            }
            // Throttle through Capture; requesting a frame every render (144 Hz)
            // was the main source of fast-drag stutter.
            if (followMovingHost) Capture();
            else UpdateBackgroundMotion();
            if (visualDrag) RenderLatest();
            // A stationary widget that has not published for several seconds is
            // holding a stale scene; leave a trace for the next bug report.
            long now = MotionClock;
            if (!followMovingHost && !visualDrag && now - lastPublishAt > 5000 && now - lastStallLogAt > 60000)
            {
                lastStallLogAt = now;
                try
                {
                    App.RecordEvent("glass stalled " + (now - lastPublishAt) + "ms pending=" + pendingStreak +
                        " inFlight=" + inFlight + " error=" + (LastError ?? "-"));
                }
                catch { }
            }
        }
        catch (Exception ex) { NoteFailure("render", ex); }
    }

    void MarkPending(string phase)
    {
        if (pendingSince != 0) return;
        pendingSince = MotionClock;
        pendingReason = WindowBackdropCapture.LastPendingReason;
        try { App.RecordEvent("glass pending " + phase + " reason=" + (pendingReason ?? "-")); } catch { }
    }

    long lastFailureLogAt;
    void NoteFailure(string site, Exception error)    {
        long now = MotionClock;
        if (now - lastFailureLogAt < 5000) return;
        lastFailureLogAt = now;
        try { App.RecordEvent("glass-error " + site + " " + error.GetType().Name + ": " + error.Message); } catch { }
    }

    void StartWindowEvents()
    {
        windowEventProc = OnWindowEvent;
        // Out-of-context callbacks run on this UI thread and only mark relevant motion.
        // No screen capture, application messages, or dispatcher work is done in the hook.
        windowEventHooks[0] = SetWinEventHook(3, 3, IntPtr.Zero, windowEventProc, 0, 0, 0);
        windowEventHooks[1] = SetWinEventHook(0x16, 0x17, IntPtr.Zero, windowEventProc, 0, 0, 0);
        windowEventHooks[2] = SetWinEventHook(0x8001, 0x8003, IntPtr.Zero, windowEventProc, 0, 0, 0);
        windowEventHooks[3] = SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, windowEventProc, 0, 0, 0);
        if (GlassProfile.Enabled)
            GlassProfile.Event("window-motion-hooks " + Array.FindAll(windowEventHooks, h => h != IntPtr.Zero).Length);
    }

    void OnWindowEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint at)
    {
        try { OnWindowEventCore(kind, window, objectId, childId); }
        catch (Exception ex) { NoteFailure("hook", ex); }
    }

    void OnWindowEventCore(uint kind, IntPtr window, int objectId, int childId)
    {
        if (disposed || frozen || paused || !IsActive || window == IntPtr.Zero || window == handle ||
            objectId != 0 || childId != 0 || !IsWindowVisible(handle) || IsIconic(handle)) return;
        // A hidden/minimized/destroyed source can have invalid or offscreen bounds.
        // Keep its removal event even when it no longer intersects the current card.
        bool knownSource = atlas != null && Array.IndexOf(atlas.SourceWindows, window) >= 0;
        if (!knownSource)
        {
            if (GetAncestor(window, 2 /* GA_ROOT */) != window || !GetWindowRect(window, out Win32Rect bounds) ||
                bounds.Right <= motionBounds.Left || bounds.Left >= motionBounds.Right ||
                bounds.Bottom <= motionBounds.Top || bounds.Top >= motionBounds.Bottom) return;
        }
        Interlocked.Exchange(ref windowMotionDirty, 1);
        // Start the next sampling pass from the event instead of waiting for the
        // render tick; a moving window is already one frame late when it lands.
        backgroundMotionUntil = MotionClock + 180;
        backgroundLayoutUntil = backgroundMotionUntil;
        WakeRendering();
        // Several location events may arrive in one display frame. The render
        // callback consumes the dirty flag even when this kick is coalesced.
        if (MotionClock - lastPassStart >= motionInterval) KickBackgroundFrame();
    }

    void KickBackgroundFrame()
    {
        if (disposed || frozen || paused || !IsActive) return;
        if (Interlocked.Exchange(ref backgroundFrameKick, 1) != 0) return;
        dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            Volatile.Write(ref backgroundFrameKick, 0);
            try { RequestFrame(); } catch (Exception ex) { NoteFailure("kick", ex); }
        });
    }

    int contentNotificationQueued;
    void OnContentChanged()
    {
        // The capture worker only signals. Read UI state on its owning dispatcher.
        if (Interlocked.Exchange(ref contentNotificationQueued, 1) != 0) return;
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            Volatile.Write(ref contentNotificationQueued, 0);
            if (disposed || paused || frozen || !IsActive || visualDrag || preparingWindowMove) return;
            long revision = CompositorWindowCapture.Revision;
            if (revision == contentRevision) return;
            contentRevision = revision;
            backgroundMotionUntil = MotionClock + 180;
            RequestFrame();
        })) Volatile.Write(ref contentNotificationQueued, 0);
    }

    async void UpdateBackgroundMotion()
    {
        if (disposed || frozen || paused || !IsActive || visualDrag || preparingWindowMove ||
            !IsWindowVisible(handle) || IsIconic(handle)) return;
        long now = MotionClock;
        // Content and HWND events wake capture directly. Between events the timer
        // also captures the current layout, so a separate idle probe is redundant.
        if (now >= backgroundMotionUntil && atlas != null && !atlas.PendingRegion &&
            Volatile.Read(ref windowMotionDirty) == 0 && CompositorWindowCapture.Revision == contentRevision)
        {
            int idle = atlas.LiveUpdates ? 500 : CaptureInterval;
            if ((int)timer.Interval.TotalMilliseconds != idle) timer.Interval = TimeSpan.FromMilliseconds(idle);
            return;
        }
        if (now - lastRefreshProbe > 2000)
        {
            lastRefreshProbe = now;
            motionInterval = DisplayRefreshInterval();
        }
        if (!TryGetRenderBounds(out int x, out int y, out int width, out int height)) return;
        motionBounds = new Win32Rect { Left = x - 48, Top = y - 48, Right = x + width + 48, Bottom = y + height + 48 };
        long nextContent = CompositorWindowCapture.Revision;
        if (Volatile.Read(ref windowMotionDirty) != 0)
        {
            backgroundMotionUntil = now + 180;
            backgroundLayoutUntil = now + 180;
        }
        if (nextContent != contentRevision)
        {
            contentRevision = nextContent;
            backgroundMotionUntil = now + 180;
        }
        // Publish new content without waiting for a separate layout-probe round trip.
        // RequestFrame has its own single-worker guard and validates the live geometry.
        if (now < backgroundMotionUntil)
        {
            if ((int)timer.Interval.TotalMilliseconds != BackgroundMotionInterval)
                timer.Interval = TimeSpan.FromMilliseconds(BackgroundMotionInterval);
            if (now - lastPassStart >= motionInterval) RequestFrame();
        }
        else if ((int)timer.Interval.TotalMilliseconds != CaptureInterval)
            timer.Interval = TimeSpan.FromMilliseconds(CaptureInterval);
        int interval = now < backgroundMotionUntil ? MovingLayoutProbeInterval : 50;
        // An active capture already probes and stamps the same scene. Only run
        // the idle discovery probe when no compositor worker is doing that work.
        if (inFlight || layoutProbeInFlight || now - lastLayoutProbe < interval) return;
        lastLayoutProbe = now;
        layoutProbeInFlight = true;
        try
        {
            ulong next = await Task.Run(() => WindowBackdropCapture.GetLayoutStamp(handle, x - 48, y - 48, width + 96, height + 96));
            if (disposed || frozen || paused || visualDrag || preparingWindowMove) return;
            now = MotionClock;
            if (hasLayoutStamp && next != layoutStamp)
            {
                backgroundMotionUntil = now + 180;
                backgroundLayoutUntil = now + 180;
                RequestFrame();
            }
            layoutStamp = next;
            hasLayoutStamp = true;
        }
        catch (Exception ex) { if (!disposed) LastError = ex.GetType().Name; }
        finally { layoutProbeInFlight = false; }
    }

    internal void BeginVisualDrag(int x, int y, int width, int height, bool renderImmediately = true)
    {
        WakeRendering();
        visualDrag = true; retainedVisualBounds = false;
        dragSpeed = 0; dragSampleAt = 0;
        // A drag must not reuse the pre-drag scene stamps: the window under the
        // new position may have been minimized or covered since the last frame.
        hasCapturedSceneStamp = false;
        capturedContentRevision = -1;
        Volatile.Write(ref lastSceneProbeAt, 0);
        Volatile.Write(ref layoutBurstUntil, 0);
        timer.Interval = TimeSpan.FromMilliseconds(motionInterval);
        followMovingHost = false;
        UpdateVisualBounds(x, y, width, height);
        uploadedVersion = 0;
        displayedX = int.MinValue;
        if (renderImmediately) RenderLatest();
    }

    internal void PrepareWindowMove()
    {
        WakeRendering();
        preparingWindowMove = true;
        Capture();
    }

    internal void BeginWindowMove()
    {
        if (!TryGetHostRenderBounds(out int x, out int y, out int width, out int height)) return;
        // OnRendering publishes once after the native window has moved.
        BeginVisualDrag(x, y, width, height, renderImmediately: false);
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

    internal void EndVisualDrag(bool keepBounds = false)
    {
        visualDrag = false; retainedVisualBounds = keepBounds;
        dragSpeed = 0; dragSampleAt = 0;
        Volatile.Write(ref lastSceneProbeAt, 0);
        Volatile.Write(ref layoutBurstUntil, 0);
        timer.Interval = TimeSpan.FromMilliseconds(CaptureInterval);
        followMovingHost = false;
        displayedX = int.MinValue;
        // Publish a complete normal crop in this UI turn; retain the old image
        // and transform when a new screen region has not arrived yet.
        RenderLatest();
    }

    // A scene whose source window was minimized/hidden must not stay on screen
    // while a replacement frame is pending; clearing it reveals the live tint.
    void DropUnavailableScene()
    {
        if (atlas == null || WindowBackdropCapture.CanReuseFrame(atlas)) return;
        atlas = null;
        uploadedVersion = 0;
        displayedVersion = -1;
        sourceBrush.ImageSource = null;
        atlasBitmap = null;
        atlasStream?.Dispose(); atlasStream = null;
        bitmap = null;
        bitmapStream?.Dispose(); bitmapStream = null;
        GlassProfile.Event("glass-stale-scene-dropped");
    }

    bool FastDrag => followMovingHost && dragSpeed > FastDragSpeed;

    void SampleDragSpeed(int x, int y)
    {
        if (!followMovingHost) return;
        long now = MotionClock;
        if (dragSampleAt != 0)
        {
            if (now - dragSampleAt < 4) return;
            double seconds = (now - dragSampleAt) / 1000.0;
            double instant = (Math.Abs(x - dragSampleX) + Math.Abs(y - dragSampleY)) / seconds;
            dragSpeed = dragSpeed * .55 + instant * .45;
        }
        dragSampleAt = now; dragSampleX = x; dragSampleY = y;
    }

    // Motion samples a screen-anchored scene without waiting for the source app.
    internal void Capture()
    {
        DropUnavailableScene();
        if (followMovingHost)
        {
            if (!TryGetRenderBounds(out int x, out int y, out int w, out int h)) return;
            SampleDragSpeed(x, y);
            // Reuse the uploaded pixels inside the atlas. Once outside it, avoid
            // waiting an extra refresh interval after the previous capture finished.
            long lastSample = AtlasCovers(x, y, w, h) ? lastCaptureAt : lastPassStart;
            if (MotionClock - lastSample >= motionInterval) RequestFrame();
            return;
        }
        int idleInterval = visualDrag || preparingWindowMove ? motionInterval : CaptureInterval;
        if (MotionClock - lastCaptureAt >= idleInterval) RequestFrame();
    }

    async void RequestFrame()
    {
        // While a moving window is under the card, chain sampling passes with no
        // idle gap: the timer cadence is only a floor, not the update rate.
        if (inFlight)
        {
            long stuck = MotionClock - captureStartedAt;
            if (stuck <= StuckCaptureMilliseconds)
            {
                Volatile.Write(ref backgroundFrameWanted, 1);
                return;
            }
            // PrintWindow is a synchronous cross-process message: one hung source
            // window can block the worker forever and freeze the glass on an old
            // scene. Abandon that pass and stop using blocking sources for a while.
            captureGeneration++;
            inFlight = false;
            blockingCooldownUntil = MotionClock + blockingCooldown;
            // Retrying a hung source every second would pile up blocked workers.
            blockingCooldown = Math.Min(300000, blockingCooldown * 2);
            Volatile.Write(ref backgroundFrameWanted, 0);
            try { App.RecordEvent("glass capture abandoned after " + stuck + "ms"); } catch { }
            DropUnavailableScene();
        }
        if (disposed || frozen || paused || !IsActive || !IsWindowVisible(handle) || IsIconic(handle)) return;
        Volatile.Write(ref backgroundFrameWanted, 0);
        int pass = ++captureGeneration;
        if (!TryGetRenderBounds(out int x, out int y, out int w, out int h)) return;
        long now = MotionClock;
        SampleDragSpeed(x, y);
        // A static, complete atlas only needs geometry probing every 32ms. Moving
        // the sampling transform still happens every render, without a worker task.
        if (!ScreenCaptureExperiment.Enabled && !ScreenCaptureExperiment.RemoteEnabled && followMovingHost && hasCapturedSceneStamp && atlas != null && !atlas.PendingRegion &&
            AtlasCovers(x, y, w, h) && Volatile.Read(ref windowMotionDirty) == 0 &&
            now >= Volatile.Read(ref layoutBurstUntil) &&
            now - Volatile.Read(ref lastSceneProbeAt) < MovingLayoutProbeInterval &&
            CompositorWindowCapture.Revision == capturedContentRevision &&
            MotionClock - lastCaptureAt < CaptureInterval) return;
        if (preparingWindowMove && !followMovingHost &&
            MotionClock - lastCaptureAt < motionInterval) return;
        if (followMovingHost) ObserveWindowMotion(x, y);
        // Allow roughly 35ms of travel for a fast drag, with bounded texture size.
        int movingMargin = FastDrag
            ? Math.Clamp((int)(dragSpeed * .035), FastMovingCaptureMargin, FastMovingCaptureMargin * 3)
            : MovingCaptureMargin;
        int margin = followMovingHost || preparingWindowMove ? movingMargin : visualDrag ? CaptureMargin : 0;
        // Preserve look-ahead along motion without reading a full margin on the idle axis.
        int marginX = followMovingHost && motionDirectionX == 0 ? MovingCaptureMargin / 2 : margin;
        int marginY = followMovingHost && motionDirectionY == 0 ? MovingCaptureMargin / 2 : margin;
        int leadX = followMovingHost ? motionDirectionX * (movingMargin / 2) : 0;
        int leadY = followMovingHost ? motionDirectionY * (movingMargin / 2) : 0;
        int screenLeft = GetSystemMetrics(76), screenTop = GetSystemMetrics(77);
        int screenRight = screenLeft + GetSystemMetrics(78), screenBottom = screenTop + GetSystemMetrics(79);
        int left = Math.Min(x, Math.Max(screenLeft, x + leadX - marginX));
        int top = Math.Min(y, Math.Max(screenTop, y + leadY - marginY));
        int right = Math.Max(x + w, Math.Min(screenRight, x + w + leadX + marginX));
        int bottom = Math.Max(y + h, Math.Min(screenBottom, y + h + leadY + marginY));
        int probeX = Math.Clamp(x + leadX, screenLeft, Math.Max(screenLeft, screenRight - w));
        int probeY = Math.Clamp(y + leadY, screenTop, Math.Max(screenTop, screenBottom - h));
        if (right <= left || bottom <= top) return;
        // Keep the scene screen-anchored while there is room to move inside it.
        // Recentring every capture changes every pixel even on a static desktop.
        if (followMovingHost && atlas != null && x >= atlasX && y >= atlasY &&
            x + w <= atlasX + atlas.Width && y + h <= atlasY + atlas.Height &&
            (probeX >= atlasX + 32 || atlasX <= screenLeft) &&
            (probeY >= atlasY + 32 || atlasY <= screenTop) &&
            (probeX + w <= atlasX + atlas.Width - 32 || atlasX + atlas.Width >= screenRight) &&
            (probeY + h <= atlasY + atlas.Height - 32 || atlasY + atlas.Height >= screenBottom))
        {
            left = atlasX; top = atlasY;
            right = left + atlas.Width; bottom = top + atlas.Height;
        }
        motionBounds = new Win32Rect { Left = left, Top = top, Right = right, Bottom = bottom };
        bool motionHint = Interlocked.Exchange(ref windowMotionDirty, 0) != 0;
        lastPassStart = now;
        if (motionHint) GlassProfile.Event("window-motion-consumed");
        var previous = atlas;
        // Keep immutable published pixels separate from the reusable worker buffers.
        var outputBuffer = spareScenePixels;
        var scratchBuffer = sceneScratchPixels;
        bool compareScene = left == atlasX && top == atlasY;
        ulong sceneLayout = 0;
        long sceneContent = 0;
        bool movingScene = followMovingHost;
        bool avoidBlockingSources = movingScene || preparingWindowMove ||
            MotionClock < backgroundLayoutUntil || MotionClock < blockingCooldownUntil;
        bool capturedStamp = hasCapturedSceneStamp;
        ulong previousLayout = capturedLayoutStamp;
        long previousContent = capturedContentRevision;
        bool recentlyCaptured = MotionClock - lastCaptureAt < CaptureInterval;
        int cardX = x, cardY = y, cardWidth = w, cardHeight = h;
        var regions = Volatile.Read(ref analysisRegions);
        bool sampleMetrics = !FastDrag && ((!movingScene && MotionClock >= backgroundMotionUntil) ||
            MotionClock - lastMetricsAt >= MetricsRefreshMilliseconds);
        int previousAtlasX = atlasX, previousAtlasY = atlasY;
        long startingContentRevision = CompositorWindowCapture.Revision;
        inFlight = true;
        captureStartedAt = MotionClock;
        long gcPauseBefore = GlassProfile.Enabled ? (long)GC.GetTotalPauseDuration().TotalMilliseconds : 0;
        bool retryLatestBounds = false;
        try
        {
            var captured = await Task.Run(() =>
            {
                long composeStarted = GlassProfile.Timestamp();
                double layoutMs = 0;
                bool layoutProbed = false;
                if (movingScene)
                {
                    long probeNow = MotionClock;
                    int probeInterval = probeNow < Volatile.Read(ref layoutBurstUntil) ? 0 : MovingLayoutProbeInterval;
                    layoutProbed = motionHint || probeNow - Volatile.Read(ref lastSceneProbeAt) >= probeInterval;
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
                    if (!ScreenCaptureExperiment.Enabled && !ScreenCaptureExperiment.RemoteEnabled && capturedStamp && compareScene && previous != null && !previous.PendingRegion &&
                        previous.Width == right - left && previous.Height == bottom - top &&
                        sceneLayout == previousLayout && sceneContent == previousContent && recentlyCaptured)
                    {
                        double skippedCompose = GlassProfile.Ms(composeStarted);
                        long skippedMetricsStarted = GlassProfile.Timestamp();
                        var skippedMetrics = sampleMetrics ? ComputeMetrics(previous.PixelsBGRA, previous.Width, previous.Height,
                            previousAtlasX, previousAtlasY, cardX, cardY, cardWidth, cardHeight, regions) : null;
                        return (Frame: previous, Unchanged: true, Scratch: scratchBuffer, Skipped: true,
                            ComposeMs: skippedCompose, Pending: false, LayoutMs: layoutMs, Probed: layoutProbed,
                            Analysis: skippedMetrics, MetricsMs: GlassProfile.Ms(skippedMetricsStarted));
                    }
                }
                int byteCount = checked((right - left) * (bottom - top) * 4);
                var scratch = scratchBuffer?.Length != byteCount ? new byte[byteCount] : scratchBuffer;
                // A look-ahead crop can change while the displayed card is still covered by the
                // previous complete texture. Hold it during GPU warm-up instead of flashing lower layers.
                var fresh = WindowBackdropCapture.CaptureAt(handle, left, top, right - left, bottom - top,
                    outputBuffer, scratch, preservePendingScene: true, avoidBlockingSources: avoidBlockingSources,
                    // During a drag, a missing source crop must not be replaced by
                    // the wallpaper or a lower window. Keep the last complete texture.
                    allowPendingFill: !movingScene && pendingSince != 0 &&
                        MotionClock - pendingSince >= PendingFillAfterMilliseconds);
                double composeMs = GlassProfile.Ms(composeStarted);
                long metricsStarted = GlassProfile.Timestamp();
                // Adaptive ink metrics are not worth a full pixel sweep on every
                // moving frame; the frame after settling refreshes them.
                // Adaptive ink must not starve while a live wallpaper or video keeps
                // the burst open: still refresh it a few times per second.
                var analysis = fresh.Success && sampleMetrics
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
            // An abandoned pass must never publish: its scene is older than the
            // pass that replaced it.
            if (pass != captureGeneration) return;
            if (disposed || frozen || paused || !IsActive) return;
            if (followMovingHost && captured.Frame.Success && captured.Frame.PendingRegion)
            {
                // A stationary capture may have started just before the first move.
                spareScenePixels = captured.Frame.PixelsBGRA;
                sceneScratchPixels = captured.Scratch;
                retryLatestBounds = true;
                return;
            }
            if (!TryGetRenderBounds(out int latestX, out int latestY, out int latestWidth, out int latestHeight)) return;
            bool movedDuringCapture = latestX != cardX || latestY != cardY ||
                latestWidth != cardWidth || latestHeight != cardHeight;
            // Validate against the live position even if the pointer was released while awaiting capture.
            // This must precede metrics, backoff, and the unchanged-scene early return.
            if ((captured.Frame.Success && !WindowBackdropCapture.FrameCovers(captured.Frame,
                    latestX, latestY, latestWidth, latestHeight)) ||
                (!captured.Frame.Success && movedDuringCapture))
            {
                if (!captured.Skipped && captured.Frame.Success) spareScenePixels = captured.Frame.PixelsBGRA;
                sceneScratchPixels = captured.Scratch;
                retryLatestBounds = true;
                return;
            }
            // Geometry can move within a valid atlas while the worker runs. Match text tone
            // to the live crop too, rather than applying luminance from the old position.
            long liveMetricsStarted = movedDuringCapture ? GlassProfile.Timestamp() : 0;
            var currentAnalysis = movedDuringCapture && captured.Frame.Success && !FastDrag &&
                MotionClock - lastMetricsAt >= MetricsRefreshMilliseconds
                ? ComputeMetrics(captured.Frame.PixelsBGRA, captured.Frame.Width, captured.Frame.Height,
                    captured.Frame.ScreenX, captured.Frame.ScreenY, latestX, latestY, latestWidth, latestHeight,
                    Volatile.Read(ref analysisRegions))
                : movedDuringCapture ? null : captured.Analysis;
            if (movedDuringCapture && GlassProfile.Enabled)
            {
                double liveMetricsMs = GlassProfile.Ms(liveMetricsStarted);
                if (liveMetricsMs >= 2) GlassProfile.Event($"drag-metrics-ui {liveMetricsMs:F1}ms");
            }
            bool metricsChanged = false;
            if (currentAnalysis != null)
            {
                lastMetricsAt = MotionClock;
                var previousMetrics = Volatile.Read(ref metrics);
                metricsChanged = currentAnalysis.Differs(previousMetrics);
                Volatile.Write(ref metrics, currentAnalysis);
            }
            GlassProfile.Capture(captured.ComposeMs, captured.MetricsMs, captured.LayoutMs, captured.Probed,
                captured.Pending, captured.Unchanged, captured.Skipped, captured.Frame.CapturedWindows);
            if (GlassProfile.Enabled && (captured.ComposeMs > 20 || captured.LayoutMs > 20))
            {
                long gcPause = (long)GC.GetTotalPauseDuration().TotalMilliseconds - gcPauseBefore;
                GlassProfile.Event($"capture-slow compose={captured.ComposeMs:F0} layout={captured.LayoutMs:F0} gc={gcPause} windows={captured.Frame.CapturedWindows} pending={captured.Pending}");
            }
            if (captured.Skipped)
            {
                if (metricsChanged) MetricsUpdated?.Invoke();
                return;
            }
            lastCaptureAt = MotionClock;
            var frame = captured.Frame;
            if (disposed || frozen || !IsActive) return;
            if (!frame.Success)
            {
                if (!frame.PendingRegion) LastError = frame.Error;
                else
                {
                    MarkPending("hold");
                    pendingStreak++;
                    if (pendingStreak == 50)
                    {
                        try { App.RecordEvent("glass pending x50 (holding previous scene) reason=" + (pendingReason ?? "-")); } catch { }
                    }
                }
                return;
            }
            if (frame.PendingRegion)
            {
                MarkPending("fallback");
                if (!pendingFillLogged)
                {
                    pendingFillLogged = true;
                    try { App.RecordEvent("glass pending fill reason=" + (pendingReason ?? "-")); } catch { }
                }
                pendingStreak++;
            }
            else
            {
                if (pendingSince != 0)
                {
                    try { App.RecordEvent("glass pending resolved after " + (MotionClock - pendingSince) + "ms reason=" + (pendingReason ?? "-")); } catch { }
                }
                pendingSince = 0;
                pendingReason = null;
                pendingFillLogged = false;
                pendingStreak = 0;
            }
            lastCaptureCompletedUtc = DateTime.UtcNow;
            pendingStreak = 0;
            blockingCooldown = BlockingCooldownMilliseconds;
            lastPublishAt = MotionClock;
            // Static captures often contain the same compositor snapshot. Recycle
            // the unconsumed result or old scene without reuploading unchanged pixels.
            spareScenePixels = previous?.PixelsBGRA;
            sceneScratchPixels = captured.Scratch;
            // Moving does not invalidate this snapshot: each pixel has a screen position.
            atlas = frame; // Adopt current source/layout metadata even when pixels are identical.
            // Geometry-only movement can leave GPU pixels and Revision unchanged.
            // Reuse capture metadata to sustain motion without a duplicate probe.
            if (!followMovingHost && !visualDrag && previous != null &&
                frame.ScreenX == previous.ScreenX && frame.ScreenY == previous.ScreenY &&
                frame.Width == previous.Width && frame.Height == previous.Height &&
                frame.LayoutStamp != previous.LayoutStamp)
            {
                backgroundMotionUntil = MotionClock + 180;
                backgroundLayoutUntil = backgroundMotionUntil;
            }
            if (!captured.Unchanged)
            {
                atlasX = left; atlasY = top;
                atlasVersion++;
            }
            if (followMovingHost)
            {
                capturedLayoutStamp = sceneLayout;
                capturedContentRevision = sceneContent;
                hasCapturedSceneStamp = !frame.PendingRegion;
            }
            else hasCapturedSceneStamp = false;
            CapturedWindows = frame.CapturedWindows;
            if (!rendering && !followMovingHost && !visualDrag && !preparingWindowMove)
            {
                var interval = TimeSpan.FromMilliseconds(frame.LiveUpdates ? 500 : CaptureInterval);
                if (timer.Interval != interval) timer.Interval = interval;
            }
            LastError = null;
            // The UI continuation can publish before the next render tick; do not hold a ready frame.
            if (!captured.Unchanged) RenderLatest();
            else if (metricsChanged) MetricsUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            if (!disposed) LastError = ex.GetType().Name;
        }
        finally
        {
            if (pass == captureGeneration)
            {
                inFlight = false;
                if (retryLatestBounds && !disposed && !frozen && !paused && IsActive)
                    surface.DispatcherQueue.TryEnqueue(RequestFrame);
                else if (Volatile.Read(ref backgroundFrameWanted) != 0 && !followMovingHost && !visualDrag &&
                    MotionClock < backgroundMotionUntil &&
                    (Volatile.Read(ref windowMotionDirty) != 0 || CompositorWindowCapture.Revision != startingContentRevision) &&
                    !disposed && !frozen && !paused && IsActive)
                    RequestFrame();
            }
        }
    }

    internal void SynchronizeGeometry()
    {
        if (!image.Width.Equals(surface.Width)) image.Width = surface.Width;
        if (!image.Height.Equals(surface.Height)) image.Height = surface.Height;
    }

    internal bool SynchronizeVisualFrame() => RenderLatest();

    internal bool AtlasCovers(int x, int y, int width, int height)
        => atlas != null && WindowBackdropCapture.FrameCovers(atlas, x, y, width, height);

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
            if (w == atlas.Width)
                stream.Write(atlas.PixelsBGRA, sourceOffset, sourceStride * h);
            else
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
        if ((visualDrag || retainedVisualBounds) && !followMovingHost)
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
        CompositorWindowCapture.ContentChanged -= OnContentChanged;
        for (int i = 0; i < windowEventHooks.Length; i++)
        {
            if (windowEventHooks[i] != IntPtr.Zero) UnhookWinEvent(windowEventHooks[i]);
            windowEventHooks[i] = IntPtr.Zero;
        }
        windowEventProc = null;
        Interlocked.Exchange(ref windowMotionDirty, 0);
        atlas = null;
        bitmapStream?.Dispose(); atlasStream?.Dispose();
        bitmapStream = atlasStream = null;
        bitmap = atlasBitmap = null;
        BeforeRender = null;
        FrameUpdated = MetricsUpdated = null;
        metrics = Metrics.None;
        spareScenePixels = sceneScratchPixels = null;
        surface.Children.Remove(image);
        sourceBrush.ImageSource = null;
        CompositorWindowCapture.ReleaseAll();
    }

    delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint at);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Win32Rect bounds);
    [StructLayout(LayoutKind.Sequential)] struct Win32Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Win32Point { internal int X, Y; }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Win32Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Win32Point point);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr handle, string text, string title, uint type);
}
