using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace OffWorkWidget.WinUI;

// Call on a worker thread: PrintWindow is synchronous and depends on the source app.
// Captures windows below the widget, and seamless desktop wallpaper for uncovered areas.
internal static class WindowBackdropCapture
{
    private static string? cachedWallpaperPath;
    private static byte[]? wallpaperPixels;
    private static int wallpaperWidth, wallpaperHeight;
    private static DateTime wallpaperLastModified;
    private static readonly object wallpaperLock = new object();

    internal sealed class Frame
    {
        internal int Width { get; init; }
        internal int Height { get; init; }
        internal byte[] PixelsBGRA { get; init; } = Array.Empty<byte>();
        internal int CapturedWindows { get; init; }
        internal string? Error { get; init; }
        internal bool PendingRegion { get; init; }
        internal bool LiveUpdates { get; init; }
        internal int ScreenX { get; init; }
        internal int ScreenY { get; init; }
        internal ulong LayoutStamp { get; init; }
        internal IntPtr[] SourceWindows { get; init; } = Array.Empty<IntPtr>();
        internal bool Success => Error == null && PixelsBGRA.Length != 0;
    }

    private static string? GetActiveWallpaperPath()
    {
        try
        {
            // 1. SPI_GETDESKWALLPAPER (current active wallpaper)
            var sb = new StringBuilder(1024);
            if (SystemParametersInfo(0x0073 /* SPI_GETDESKWALLPAPER */, (uint)sb.Capacity, sb, 0))
            {
                string p = sb.ToString();
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
            }

            // 2. Registry HKCU\Control Panel\Desktop\Wallpaper
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
            {
                string? p = key?.GetValue("Wallpaper") as string;
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
            }

            // 3. Fallback: TranscodedWallpaper
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(fallback)) return fallback;
        }
        catch { }
        return null;
    }

    private static IntPtr liveWallpaperHwnd = IntPtr.Zero;
    private static DateTime lastWpeCheck = DateTime.MinValue;
    private static IntPtr desktopHostHwnd = IntPtr.Zero;
    private static DateTime lastDesktopHostCheck = DateTime.MinValue;

    private static IntPtr GetDesktopHostHandle()
    {
        if (desktopHostHwnd != IntPtr.Zero && IsWindow(desktopHostHwnd) && IsWindowVisible(desktopHostHwnd))
            return desktopHostHwnd;

        if (DateTime.UtcNow - lastDesktopHostCheck < TimeSpan.FromSeconds(2))
            return desktopHostHwnd;
        lastDesktopHostCheck = DateTime.UtcNow;

        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            IntPtr shell = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shell != IntPtr.Zero)
            {
                desktopHostHwnd = progman;
                return progman;
            }
        }

        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            var sb = new StringBuilder(32);
            if (GetClassName(hWnd, sb, sb.Capacity) > 0 && sb.ToString() == "WorkerW")
            {
                if (FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    found = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        desktopHostHwnd = found != IntPtr.Zero ? found : progman;
        return desktopHostHwnd;
    }

    private static IntPtr GetLiveWallpaperHandle()
    {
        if (liveWallpaperHwnd != IntPtr.Zero && IsWindow(liveWallpaperHwnd) && IsWindowVisible(liveWallpaperHwnd))
            return liveWallpaperHwnd;

        if (DateTime.UtcNow - lastWpeCheck < TimeSpan.FromSeconds(2))
            return liveWallpaperHwnd;
        lastWpeCheck = DateTime.UtcNow;

        IntPtr found = IntPtr.Zero;

        // 1. Direct search on Progman
        IntPtr progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            EnumChildWindows(progman, (child, _) =>
            {
                var csb = new StringBuilder(64);
                if (GetClassName(child, csb, csb.Capacity) > 0)
                {
                    string ccls = csb.ToString();
                    if ((ccls.StartsWith("WPE") || ccls.Contains("Wallpaper") || ccls.Contains("Lively")) && IsWindowVisible(child))
                    {
                        found = child;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        // 2. Fall back to EnumWindows for WorkerW / other hosts
        if (found == IntPtr.Zero)
        {
            EnumWindows((topWnd, _) =>
            {
                var sb = new StringBuilder(64);
                if (GetClassName(topWnd, sb, sb.Capacity) > 0)
                {
                    string cls = sb.ToString();
                    if (cls == "Progman" || cls == "WorkerW")
                    {
                        EnumChildWindows(topWnd, (child, _) =>
                        {
                            var csb = new StringBuilder(64);
                            if (GetClassName(child, csb, csb.Capacity) > 0)
                            {
                                string ccls = csb.ToString();
                                if ((ccls.StartsWith("WPE") || ccls.Contains("Wallpaper") || ccls.Contains("Lively")) && IsWindowVisible(child))
                                {
                                    found = child;
                                    return false;
                                }
                            }
                            return true;
                        }, IntPtr.Zero);
                    }
                }
                return found == IntPtr.Zero;
            }, IntPtr.Zero);
        }

        liveWallpaperHwnd = found;
        return found;
    }

    private static void EnsureWallpaperLoaded()
    {
        lock (wallpaperLock)
        {
            try
            {
                int screenW = GetSystemMetrics(0 /* SM_CXSCREEN */);
                int screenH = GetSystemMetrics(1 /* SM_CYSCREEN */);
                if (screenW <= 0) screenW = 1920;
                if (screenH <= 0) screenH = 1080;

                // 1. Check if Wallpaper Engine / live wallpaper window is active
                IntPtr wpe = GetLiveWallpaperHandle();
                if (wpe != IntPtr.Zero)
                {
                    if (wallpaperPixels == null || cachedWallpaperPath != "WallpaperEngine" || (DateTime.UtcNow - wallpaperLastModified).TotalMilliseconds > 150)
                    {
                        var rect = new Rect(0, 0, screenW, screenH);
                        byte[]? wpeBits = CaptureWindow(wpe, rect);
                        if (wpeBits != null && wpeBits.Length == screenW * screenH * 4)
                        {
                            wallpaperPixels = wpeBits;
                            wallpaperWidth = screenW;
                            wallpaperHeight = screenH;
                            cachedWallpaperPath = "WallpaperEngine";
                            wallpaperLastModified = DateTime.UtcNow;
                            return;
                        }
                    }
                    else
                    {
                        return; // Live frame still fresh
                    }
                }

                // 2. Fall back to static system wallpaper
                string? path = GetActiveWallpaperPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    cachedWallpaperPath = null;
                    wallpaperPixels = null;
                    wallpaperWidth = wallpaperHeight = 0;
                    wallpaperLastModified = default;
                    return;
                }

                DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                if (wallpaperPixels != null && path == cachedWallpaperPath && lastWrite == wallpaperLastModified && wallpaperWidth == screenW && wallpaperHeight == screenH)
                    return;

                byte[] pixels = DecodeWallpaperAsync(path, screenW, screenH).GetAwaiter().GetResult();
                wallpaperPixels = pixels;
                wallpaperWidth = screenW;
                wallpaperHeight = screenH;
                cachedWallpaperPath = path;
                wallpaperLastModified = lastWrite;
            }
            catch
            {
                // A failed wallpaper load must not preserve a previous image.
                cachedWallpaperPath = null;
                wallpaperPixels = null;
                wallpaperWidth = wallpaperHeight = 0;
                wallpaperLastModified = default;
            }
        }
    }

    // Windows Imaging Component codecs also handle extensionless TranscodedWallpaper files.
    // The caller runs on a capture worker; keep asynchronous decoding off the UI context.
    private static async Task<byte[]> DecodeWallpaperAsync(string path, int width, int height)
    {
        using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        var transform = new BitmapTransform
        {
            ScaledWidth = checked((uint)width),
            ScaledHeight = checked((uint)height),
            InterpolationMode = BitmapInterpolationMode.Linear
        };
        bool scaled = decoder.PixelWidth != width || decoder.PixelHeight != height;
        var provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, scaled ? BitmapAlphaMode.Premultiplied : BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
            .AsTask().ConfigureAwait(false);
        byte[] pixels = provider.DetachPixelData();
        if (pixels.Length != checked(width * height * 4))
            throw new InvalidDataException("Unexpected wallpaper pixel dimensions.");
        // Match the old RGB surface: same-size files retain straight RGB; scaled images
        // composite transparency over the black target. Both publish opaque BGRA.
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        return pixels;
    }

    // Geometry-only probe: no PrintWindow, graphics device creation or pixel readback.
    internal static ulong GetLayoutStamp(IntPtr excludedHandle, int x, int y, int width, int height)
        => GetLayoutStamp(excludedHandle, x, y, width, height, out _);

    static ulong GetLayoutStamp(IntPtr excludedHandle, int x, int y, int width, int height, out ulong order)
    {
        ulong stamp = 14695981039346656037UL;
        ulong stack = stamp;
        bool passedSelf = false;
        var target = new Rect(x, y, x + width, y + height);
        void Mix(long value) { unchecked { stamp = (stamp ^ (ulong)value) * 1099511628211UL; } }
        EnumWindows((window, _) =>
        {
            if (!passedSelf) { if (window == excludedHandle) passedSelf = true; return true; }
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            // Reject off-target windows with the cheap screen rect before asking DWM;
            // the extended-frame query is a synchronous cross-process call.
            if (!GetWindowRect(window, out Rect native) || !Intersect(target, native, out Rect _)) return true;
            if (IsDesktopWindow(window) || IsCloaked(window) || !GetWindowVisualBounds(window, out Rect bounds) || !Intersect(target, bounds, out Rect overlap)) return true;
            Mix(window.ToInt64());
            unchecked { stack = (stack ^ (ulong)window.ToInt64()) * 1099511628211UL; }
            Mix(bounds.Left); Mix(bounds.Top); Mix(bounds.Right); Mix(bounds.Bottom);
            return true;
        }, IntPtr.Zero);
        order = stack;
        return stamp;
    }

    // A pending replacement must not keep pixels from a source that left the desktop.
    internal static bool CanReuseFrame(Frame frame) => Array.TrueForAll(frame.SourceWindows, IsWindowAvailable);

    // Even a desktop-only snapshot becomes stale when a window enters its screen region.
    internal static bool CanReuseFrame(Frame frame, IntPtr excludedHandle, int x, int y, int width, int height) =>
        !frame.PendingRegion && frame.ScreenX == x && frame.ScreenY == y &&
        frame.Width == width && frame.Height == height && CanReuseFrame(frame) &&
        frame.LayoutStamp == GetLayoutStamp(excludedHandle, x, y, width, height);

    internal static bool FrameCovers(Frame frame, int x, int y, int width, int height) =>
        frame.Success && width > 0 && height > 0 && x >= frame.ScreenX && y >= frame.ScreenY &&
        (long)x + width <= (long)frame.ScreenX + frame.Width &&
        (long)y + height <= (long)frame.ScreenY + frame.Height;

    internal static bool IsWindowAvailable(IntPtr window) =>
        IsWindow(window) && IsWindowVisible(window) && !IsIconic(window) && !IsCloaked(window);

    // On Windows 10, do not ask Chromium to repaint while its GPU capture is unavailable.
    // The previous complete scene is retained until a compositor frame is ready.
    internal static bool AvoidBrowserPrintFallback(string className, bool windows11OrNewer) =>
        !windows11OrNewer && className.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);

    // Diagnostic: the window/reason that kept the last frame incomplete.
    internal static string? LastPendingReason;

    internal static Frame CaptureAt(IntPtr excludedHandle, int targetX, int targetY, int width, int height,
        byte[]? reusableOutput = null, byte[]? sliceBuffer = null, bool preservePendingScene = true,
        bool avoidBlockingSources = false, bool allowPendingFill = false)
    {
        // Capture underlying windows individually so the widget can remain visible
        // to remote desktop and screenshots without sampling itself.
        int captured = 0;
        try
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return Failed("背景采样必须在工作线程执行。", width, height, captured);
            if (width <= 0 || height <= 0 || (long)width * height > 8_000_000)
                return Failed("组件尺寸超出采样范围。", width, height, captured);
            if (ScreenCaptureExperiment.Enabled || ScreenCaptureExperiment.RemoteEnabled)
            {
                byte[]? pixels = null;
                lock (ScreenCaptureExperiment.RemoteGate)
                {
                    if (ScreenCaptureExperiment.Enabled && !ScreenCaptureExperiment.AffinityReady)
                        return Failed("实验采样无法排除挂件，已停止采样。", width, height, captured);
                    if (ScreenCaptureExperiment.Enabled || ScreenCaptureExperiment.RemoteEnabled)
                        pixels = ScreenCaptureExperiment.Capture(targetX, targetY, width, height, reusableOutput);
                }
                if (pixels != null)
                    return new Frame { Width = width, Height = height, PixelsBGRA = pixels,
                        CapturedWindows = 1, ScreenX = targetX, ScreenY = targetY,
                        LayoutStamp = GetLayoutStamp(excludedHandle, targetX, targetY, width, height) };
            }
            var target = new Rect(targetX, targetY, checked(targetX + width), checked(targetY + height));
            ulong sceneLayout = GetLayoutStamp(excludedHandle, targetX, targetY, width, height, out ulong sceneOrder);
            var uncovered = new List<Rect> { target };
            var sourceWindows = new List<IntPtr>();
            int liveSources = 0;
            int outputLength = checked(width * height * 4);
            var output = reusableOutput?.Length == outputLength ? reusableOutput : new byte[outputLength];
            if (ReferenceEquals(output, reusableOutput)) Array.Clear(output);
            if (ReferenceEquals(output, sliceBuffer)) sliceBuffer = null;
            bool passedSelf = false, hasTransparency = false, pendingRegion = false;
            LastPendingReason = null;
            void MarkPending(string reason, IntPtr source)
            {
                var text = new StringBuilder(64);
                if (GetClassName(source, text, text.Capacity) <= 0) text.Append("?");
                LastPendingReason = reason + ":" + text;
                pendingRegion = true;
            }

            EnumWindows((window, _) =>
            {
                if (uncovered.Count == 0 || captured >= 12) return false;

                if (!passedSelf)
                {
                    if (window == excludedHandle)
                    {
                        passedSelf = true;
                    }
                    return true;
                }

                // Other app windows, including settings, are valid backgrounds.
                // Only the widget itself starts the below-widget Z-order range.
                if (window == excludedHandle) return true;
                // Cheap native rect rejects most windows; only on-target ones pay
                // the DWM extended-frame round trip.
                if (!GetWindowRect(window, out Rect nativeHit) || !Intersect(target, nativeHit, out Rect _)) return true;
                if (!IsWindowAvailable(window) || IsDesktopWindow(window)) return true;
                if (!GetWindowVisualBounds(window, out Rect bounds)) return true;

                // Check if bounds intersects any piece in uncovered
                bool overlaps = false;
                foreach (Rect piece in uncovered)
                {
                    if (Intersect(piece, bounds, out Rect _)) { overlaps = true; break; }
                }
                if (!overlaps) return true;

                if (IsHungAppWindow(window)) return true;

                int exStyle = GetWindowLong(window, -20);
                var windowClass = new StringBuilder(64);
                GetClassName(window, windowClass, windowClass.Capacity);
                bool requiresAlpha = (exStyle & (0x00080000 /* WS_EX_LAYERED */ | 0x00200000 /* WS_EX_NOREDIRECTIONBITMAP */)) != 0 ||
                    windowClass.ToString() == "WinUIDesktopWin32WindowClass";
                bool blackOverlay = false;
                // GPU child surfaces need compositor capture even when their top-level
                // HWND has ordinary styles. Keep the shell overflow's existing mask.
                if (!IsTrayOverflowWindow(window) && CompositorWindowCapture.TryGetFrame(window, target.Left, target.Top,
                    width, height, out var frame) && frame != null)
                {
                    using var frameLease = frame;
                    // Sunlogin can expose an opaque black capture surface over the actual desktop.
                    blackOverlay = IsBlackCapture(frame.PixelsBGRA, frame.PixelWidth,
                        new Rect(0, 0, frame.PixelWidth, frame.PixelHeight), requireOpaque: true) && IsSunloginWindow(window);
                    if (!blackOverlay)
                    {
                        // A resized HWND can expose PrintWindow's stale black backing.
                        // Wait for a matching compositor snapshot instead of stretching it.
                        if (!TryGetCompositorBounds(window, frame.Width, frame.Height, out Rect frameBounds))
                        {
                            CompositorWindowCapture.RequestRefresh(window);
                            MarkPending("bounds", window);
                            if (allowPendingFill) return true;
                            return !preservePendingScene;
                        }
                        if (!IsWindowAvailable(window)) return true;
                        var fullBounds = frameBounds;
                        frameBounds = new Rect(frameBounds.Left + frame.PixelLeft, frameBounds.Top + frame.PixelTop,
                            frameBounds.Left + frame.PixelLeft + frame.PixelWidth, frameBounds.Top + frame.PixelTop + frame.PixelHeight);
                        if (Intersect(target, fullBounds, out Rect needed))
                        {
                            // Recrop before the 96px read-ahead is exhausted, while this frame
                            // can still be displayed. Ignore edges already at the window limit.
                            const int lookAhead = 48;
                            if (needed.Left - lookAhead < frameBounds.Left && fullBounds.Left < frameBounds.Left ||
                                needed.Top - lookAhead < frameBounds.Top && fullBounds.Top < frameBounds.Top ||
                                needed.Right + lookAhead > frameBounds.Right && fullBounds.Right > frameBounds.Right ||
                                needed.Bottom + lookAhead > frameBounds.Bottom && fullBounds.Bottom > frameBounds.Bottom)
                                CompositorWindowCapture.RequestRefresh(window);
                            if (needed.Left < frameBounds.Left || needed.Top < frameBounds.Top ||
                                needed.Right > frameBounds.Right || needed.Bottom > frameBounds.Bottom)
                            {
                                MarkPending("crop", window);
                                if (allowPendingFill) return true;
                                return !preservePendingScene;
                            }
                        }
                        if (!GetWindowRect(window, out Rect nativeBounds)) return true;
                        var compositorCoverage = new List<Rect>();
                        // A compositor snapshot can predate a native region shrink.
                        // Transparent sources retain their antialiasing guard and alpha.
                        AddScreenCoverage(ReadWindowCoverage(window, nativeBounds.Right - nativeBounds.Left,
                            nativeBounds.Bottom - nativeBounds.Top, !requiresAlpha), nativeBounds, compositorCoverage);
                        var opaqueHits = !requiresAlpha ? new List<Rect>() : null;
                        foreach (Rect remaining in uncovered)
                            foreach (Rect visible in compositorCoverage)
                                if (Intersect(remaining, visible, out Rect hit) && Intersect(hit, frameBounds, out Rect clipped))
                                {
                                    if (requiresAlpha)
                                        CompositeTransparent(output, width, target, frame.PixelsBGRA, frame.PixelWidth, frameBounds, clipped);
                                    else if (hasTransparency)
                                        CompositeOpaque(output, width, target, frame.PixelsBGRA, frame.PixelWidth, frameBounds, clipped);
                                    else
                                        for (int row = clipped.Top; row < clipped.Bottom; row++)
                                            Buffer.BlockCopy(frame.PixelsBGRA, ((row - frameBounds.Top) * frame.PixelWidth + clipped.Left - frameBounds.Left) * 4,
                                                output, ((row - target.Top) * width + clipped.Left - target.Left) * 4, (clipped.Right - clipped.Left) * 4);
                                    opaqueHits?.Add(clipped);
                                }
                        hasTransparency |= requiresAlpha;
                        liveSources++;
                        sourceWindows.Add(window);
                        captured++;
                        if (opaqueHits == null) uncovered = RemoveOpaqueCoverage(output, width, target, uncovered);
                        else foreach (Rect covered in opaqueHits)
                        {
                            var remaining = new List<Rect>();
                            foreach (Rect piece in uncovered) Subtract(piece, covered, remaining);
                            uncovered = remaining;
                        }
                        return uncovered.Count > 0;
                    }
                }
                // Keep a still-valid scene during GPU warm-up; otherwise fill from lower layers. PrintWindow
                // can synchronously stall the source app's UI (notably Qt windows).
                if (blackOverlay && (requiresAlpha || avoidBlockingSources)) return true;
                if (!blackOverlay && !IsTrayOverflowWindow(window) && CompositorWindowCapture.IsStarting(window))
                {
                    MarkPending("starting", window);
                    if (allowPendingFill) return true;
                    return !preservePendingScene;
                }
                // PrintWindow sends paint requests into the source browser. On Windows 10
                // keep the last valid glass scene instead of repeatedly repainting Chromium.
                if (AvoidBrowserPrintFallback(windowClass.ToString(), OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)))
                {
                    MarkPending("browser", window);
                    if (allowPendingFill) return true;
                    return !preservePendingScene;
                }

                // PrintWindow flattens transparent screenshot/glass windows onto black.
                // Only ordinary opaque windows may use it while compositor capture warms up.
                if (requiresAlpha)
                {
                    // An unavailable GPU source is not uncovered desktop wallpaper.
                    MarkPending("alpha", window);
                    if (allowPendingFill) return true;
                    return !preservePendingScene;
                }

                var trayCoverage = IsTrayOverflowWindow(window) ? new List<Rect>() : null;
                var windowCoverage = new List<Rect>();
                var pieces = new List<Rect>();
                foreach (Rect remaining in uncovered)
                {
                    if (Intersect(remaining, bounds, out Rect hit)) pieces.Add(hit);
                }

                if (pieces.Count != 0)
                {
                    // PrintWindow is synchronous and can stall the source app (notably
                    // Qt) while it is moving behind the widget. Hold the previous
                    // complete scene instead of repainting a moving source.
                    if (avoidBlockingSources)
                    {
                        MarkPending("blocking", window);
                        if (allowPendingFill) return true;
                        return !preservePendingScene;
                    }
                    byte[]? slice = CaptureWindowSlice(window, bounds, target, sliceBuffer, trayCoverage, windowCoverage);
                    // PrintWindow supplies BGR pixels, but its alpha byte is undefined.
                    if (slice != null && Intersect(target, nativeHit, out var screenCrop) &&
                        IsBlackCapture(slice, width, new Rect(screenCrop.Left - target.Left, screenCrop.Top - target.Top,
                            screenCrop.Right - target.Left, screenCrop.Bottom - target.Top), requireOpaque: false) &&
                        (blackOverlay || IsSunloginWindow(window)))
                        return true;
                    if (slice != null && IsWindowAvailable(window))
                    {
                        sourceWindows.Add(window);
                        pieces.Clear();
                        foreach (Rect remaining in uncovered)
                            foreach (Rect visible in windowCoverage)
                                if (Intersect(remaining, visible, out Rect hit) && Intersect(hit, bounds, out Rect clipped)) pieces.Add(clipped);
                        if (trayCoverage != null)
                        {
                            var trayPieces = new List<Rect>();
                            foreach (Rect piece in pieces)
                                foreach (Rect visible in trayCoverage)
                                    if (Intersect(piece, visible, out Rect hit)) trayPieces.Add(hit);
                            pieces = trayPieces;
                        }
                        foreach (Rect piece in pieces)
                        {
                            int pLeft = Math.Max(piece.Left, Math.Max(bounds.Left, target.Left));
                            int pRight = Math.Min(piece.Right, Math.Min(bounds.Right, target.Right));
                            int pTop = Math.Max(piece.Top, Math.Max(bounds.Top, target.Top));
                            int pBottom = Math.Min(piece.Bottom, Math.Min(bounds.Bottom, target.Bottom));
                            int pWidth = pRight - pLeft;
                            if (pWidth <= 0 || pBottom <= pTop) continue;

                            int count = pWidth * 4;
                            for (int y = pTop; y < pBottom; y++)
                            {
                                int offset = checked(((y - target.Top) * width + pLeft - target.Left) * 4);
                                if (offset >= 0 && offset + count <= slice.Length && offset + count <= output.Length)
                                {
                                    CopyOpaqueRow(slice, output, offset, count, hasTransparency);
                                }
                            }
                        }
                        captured++;
                        foreach (Rect covered in pieces)
                        {
                            var remainingPieces = new List<Rect>();
                            foreach (Rect remaining in uncovered) Subtract(remaining, covered, remainingPieces);
                            uncovered = remainingPieces;
                        }
                    }
                }

                return uncovered.Count > 0;
            }, IntPtr.Zero);

            // A window under the widget that is still warming up is not uncovered
            // desktop wallpaper. Publish a pending frame and let the glass hold the
            // last complete layer instead of flashing a lower layer during fast drags.
            // After a short grace period the caller asks for lower layers so the glass
            // cannot freeze for seconds behind an uncapturable overlay window.
            if (pendingRegion && !allowPendingFill)
                return new Frame { Error = "等待背景裁剪更新。", PendingRegion = true };

            // Fill any uncovered regions with the actual desktop wallpaper and icons
            if (uncovered.Count != 0)
            {
                IntPtr deskHost = GetDesktopHostHandle();
                byte[]? deskSlice = null;
                bool opaqueDesktopFrame = false;
                if (deskHost != IntPtr.Zero && GetWindowVisualBounds(deskHost, out Rect deskBounds))
                {
                    deskSlice = CaptureDesktopSlice(deskHost, deskBounds, target, sliceBuffer, out opaqueDesktopFrame);
                }

                // Valid opaque compositor frames may contain a black wallpaper or video.
                // The RGB heuristic is only for legacy shell screenshots with no reliable alpha.
                bool deskSliceHasWallpaper = HasDesktopContent(deskSlice, width * height, opaqueDesktopFrame);

                if (deskSlice != null && deskSliceHasWallpaper)
                {
                    // Progman/WorkerW captured both wallpaper (live/static) AND desktop icons/files!
                    foreach (Rect piece in uncovered)
                    {
                        int pLeft = Math.Max(piece.Left, target.Left);
                        int pRight = Math.Min(piece.Right, target.Right);
                        int pTop = Math.Max(piece.Top, target.Top);
                        int pBottom = Math.Min(piece.Bottom, target.Bottom);
                        int pWidth = pRight - pLeft;
                        if (pWidth <= 0 || pBottom <= pTop) continue;

                        int count = pWidth * 4;
                        for (int y = pTop; y < pBottom; y++)
                        {
                            int offset = (y - target.Top) * width * 4 + (pLeft - target.Left) * 4;
                            if (offset >= 0 && offset + count <= deskSlice.Length && offset + count <= output.Length)
                            {
                                CopyOpaqueRow(deskSlice, output, offset, count, hasTransparency);
                            }
                        }
                    }
                    captured++;
                    uncovered.Clear();
                }
                else
                {
                    // Fallback path: EnsureWallpaperLoaded + overlay deskSlice icons
                    EnsureWallpaperLoaded();
                    byte[]? wall = wallpaperPixels;
                    int wallW = wallpaperWidth, wallH = wallpaperHeight;
                    uint sysColor = GetSysColor(1 /* COLOR_DESKTOP */);
                    byte fbB = (byte)((sysColor >> 16) & 0xFF), fbG = (byte)((sysColor >> 8) & 0xFF), fbR = (byte)(sysColor & 0xFF);

                    foreach (Rect piece in uncovered)
                    {
                        int pLeft = Math.Max(piece.Left, target.Left);
                        int pRight = Math.Min(piece.Right, target.Right);
                        int pTop = Math.Max(piece.Top, target.Top);
                        int pBottom = Math.Min(piece.Bottom, target.Bottom);
                        int pWidth = pRight - pLeft;
                        if (pWidth <= 0 || pBottom <= pTop) continue;

                        for (int y = pTop; y < pBottom; y++)
                        {
                            int dstY = y - target.Top;
                            if (dstY < 0 || dstY >= height) continue;

                            for (int x = pLeft; x < pRight; x++)
                            {
                                int dstX = x - target.Left;
                                if (dstX < 0 || dstX >= width) continue;

                                int dstOffset = (dstY * width + dstX) * 4;
                                if (dstOffset < 0 || dstOffset + 3 >= output.Length) continue;

                                // 1. If deskSlice has an icon/text pixel, use it!
                                if (deskSlice != null && dstOffset + 3 < deskSlice.Length)
                                {
                                    byte db = deskSlice[dstOffset];
                                    byte dg = deskSlice[dstOffset + 1];
                                    byte dr = deskSlice[dstOffset + 2];
                                    if (db != 0 || dg != 0 || dr != 0)
                                    {
                                        WriteOpaque(output, dstOffset, db, dg, dr, hasTransparency);
                                        continue;
                                    }
                                }

                                // 2. Fill background with wallpaper
                                if (wall != null && wallW > 0 && wallH > 0)
                                {
                                    int srcX = ((x % wallW) + wallW) % wallW;
                                    int srcY = ((y % wallH) + wallH) % wallH;
                                    int srcOffset = (srcY * wallW + srcX) * 4;
                                    if (srcOffset >= 0 && srcOffset + 3 < wall.Length)
                                    {
                                        WriteOpaque(output, dstOffset, wall[srcOffset], wall[srcOffset + 1], wall[srcOffset + 2], hasTransparency);
                                        continue;
                                    }
                                }

                                // 3. Fallback desktop color
                                WriteOpaque(output, dstOffset, fbB, fbG, fbR, hasTransparency);
                            }
                        }
                    }
                    captured++;
                    uncovered.Clear();
                }
            }

            for (int index = 3; index < output.Length; index += 4) output[index] = 255;

            // Reject a composite assembled across a source move or Z-order change.
            ulong latestLayout = GetLayoutStamp(excludedHandle, targetX, targetY, width, height, out ulong latestOrder);
            if (sceneOrder != latestOrder || (!avoidBlockingSources && sceneLayout != latestLayout))
                return new Frame { Error = "背景窗口位置已变化。", PendingRegion = true };

            // Card and text-region analysis is performed by DesktopGlassSource on the capture worker.
            return new Frame
            {
                Width = width,
                Height = height,
                PixelsBGRA = output,
                CapturedWindows = captured,
                LiveUpdates = !pendingRegion && captured > 0 && captured == liveSources,
                SourceWindows = sourceWindows.ToArray(),
                ScreenX = targetX, ScreenY = targetY, LayoutStamp = sceneLayout,
                PendingRegion = pendingRegion
            };
        }
        catch (Exception exception)
        {
            return Failed("背景材质采样失败：" + exception.Message, width, height, captured);
        }
    }

    internal static bool TryGetCaptureOrigin(IntPtr window, int width, int height, out int x, out int y)
    {
        bool matched = TryGetCompositorBounds(window, width, height, out Rect bounds);
        x = bounds.Left; y = bounds.Top;
        return matched;
    }

    private static bool TryGetCompositorBounds(IntPtr window, int width, int height, out Rect bounds)
    {
        if (GetWindowVisualBounds(window, out bounds) && bounds.Right - bounds.Left == width && bounds.Bottom - bounds.Top == height)
            return true;
        return GetWindowRect(window, out bounds) && bounds.Right - bounds.Left == width && bounds.Bottom - bounds.Top == height;
    }

    private static void CompositeOpaque(byte[] output, int outputWidth, Rect target, byte[] source, int sourceWidth, Rect sourceBounds, Rect hit)
    {
        for (int y = hit.Top; y < hit.Bottom; y++)
        for (int x = hit.Left; x < hit.Right; x++)
        {
            int from = ((y - sourceBounds.Top) * sourceWidth + x - sourceBounds.Left) * 4;
            int to = ((y - target.Top) * outputWidth + x - target.Left) * 4;
            WriteOpaque(output, to, source[from], source[from + 1], source[from + 2], true);
        }
    }

    private static void CompositeTransparent(byte[] output, int outputWidth, Rect target, byte[] source, int sourceWidth, Rect sourceBounds, Rect hit)
    {
        for (int y = hit.Top; y < hit.Bottom; y++)
        for (int x = hit.Left; x < hit.Right; x++)
        {
            int from = ((y - sourceBounds.Top) * sourceWidth + x - sourceBounds.Left) * 4;
            int to = ((y - target.Top) * outputWidth + x - target.Left) * 4;
            int remaining = 255 - output[to + 3];
            if (remaining == 0 || source[from + 3] == 0) continue;
            if (remaining == 255)
            {
                output[to] = source[from]; output[to + 1] = source[from + 1];
                output[to + 2] = source[from + 2]; output[to + 3] = source[from + 3];
                continue;
            }
            // Captured RGB is already premultiplied. Accumulate front to back.
            for (int c = 0; c < 4; c++)
                output[to + c] = (byte)Math.Min(255, output[to + c] + (source[from + c] * remaining + 127) / 255);
        }
    }

    private static List<Rect> RemoveOpaqueCoverage(byte[] pixels, int width, Rect target, List<Rect> uncovered)
    {
        var result = new List<Rect>();
        foreach (Rect piece in uncovered)
        {
            var strips = new List<Rect>();
            var previous = new Dictionary<(int, int), int>();
            var current = new Dictionary<(int, int), int>();
            bool complex = false;
            for (int y = piece.Top; y < piece.Bottom && !complex; y++)
            {
                current.Clear();
                int x = piece.Left;
                while (x < piece.Right)
                {
                    while (x < piece.Right && pixels[((y - target.Top) * width + x - target.Left) * 4 + 3] == 255) x++;
                    int left = x;
                    while (x < piece.Right && pixels[((y - target.Top) * width + x - target.Left) * 4 + 3] != 255) x++;
                    if (left == x) continue;
                    var key = (left, x);
                    if (previous.TryGetValue(key, out int index))
                    {
                        Rect strip = strips[index]; strip.Bottom = y + 1; strips[index] = strip;
                        current[key] = index;
                    }
                    else
                    {
                        current[key] = strips.Count;
                        strips.Add(new Rect(left, y, x, y + 1));
                        // Unusual alpha masks must not create unbounded region bookkeeping.
                        if (strips.Count > 512) { complex = true; break; }
                    }
                }
                (previous, current) = (current, previous);
            }
            if (complex) result.Add(piece);
            else result.AddRange(strips);
        }
        return result;
    }

    private static void CopyOpaqueRow(byte[] source, byte[] output, int offset, int count, bool blend)
    {
        if (!blend) { Buffer.BlockCopy(source, offset, output, offset, count); return; }
        for (int i = offset; i < offset + count; i += 4)
            WriteOpaque(output, i, source[i], source[i + 1], source[i + 2], true);
    }

    private static void WriteOpaque(byte[] output, int offset, byte blue, byte green, byte red, bool blend)
    {
        int remaining = blend ? 255 - output[offset + 3] : 255;
        if (remaining == 255) { output[offset] = blue; output[offset + 1] = green; output[offset + 2] = red; }
        else if (remaining != 0)
        {
            output[offset] = (byte)Math.Min(255, output[offset] + (blue * remaining + 127) / 255);
            output[offset + 1] = (byte)Math.Min(255, output[offset + 1] + (green * remaining + 127) / 255);
            output[offset + 2] = (byte)Math.Min(255, output[offset + 2] + (red * remaining + 127) / 255);
        }
        output[offset + 3] = 255;
    }

    private static Frame Failed(string message, int width, int height, int count) =>
        new Frame { Width = width, Height = height, CapturedWindows = count, Error = message };

    private static bool IsDesktopWindow(IntPtr hWnd)
    {
        var sb = new StringBuilder(64);
        if (GetClassName(hWnd, sb, sb.Capacity) > 0)
        {
            string name = sb.ToString();
            if (name == "Progman" || name == "WorkerW")
                return true;
        }
        return false;
    }

    private static bool IsTrayOverflowWindow(IntPtr window)
    {
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) > 0 &&
            name.ToString() == "TopLevelWindowForOverflowXamlIsland";
    }

    private static void GetTrayContentCoverage(byte[] pixels, Rect bounds, List<Rect> coverage)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0 || pixels.Length != (long)width * height * 4)
        { coverage.Add(bounds); return; }
        var rows = new List<Rect>();
        int nonBlack = 0, firstRow = -1, lastRow = -1, minX = width, maxX = 0;
        for (int y = 0; y < height; y++)
        {
            int left = width, right = -1;
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                if (pixels[index] == 0 && pixels[index + 1] == 0 && pixels[index + 2] == 0) continue;
                if (left == width) left = x;
                right = x; nonBlack++;
            }
            if (right < left) continue;
            if (firstRow < 0) firstRow = y;
            lastRow = y; minX = Math.Min(minX, left); maxX = Math.Max(maxX, right + 1);
            rows.Add(new Rect(bounds.Left + left, bounds.Top + y, bounds.Left + right + 1, bounds.Top + y + 1));
        }
        int contentWidth = maxX - minX, contentHeight = lastRow - firstRow + 1;
        // Restrict this correction to a continuous, mostly painted tray panel.
        // Sparse icons, all-black/high-contrast captures and ordinary windows
        // must never have their legitimate black pixels treated as transparent.
        if (firstRow < 0 || rows.Count != contentHeight || contentWidth < width / 2 ||
            contentHeight < height / 2 || nonBlack < (long)contentWidth * contentHeight * .65)
        { coverage.Add(bounds); return; }
        foreach (Rect row in rows)
            if (row.Right - row.Left < contentWidth / 2)
            { coverage.Add(bounds); return; }
        foreach (Rect row in rows)
        {
            if (coverage.Count > 0)
            {
                Rect previous = coverage[coverage.Count - 1];
                if (previous.Left == row.Left && previous.Right == row.Right && previous.Bottom == row.Top)
                { previous.Bottom = row.Bottom; coverage[coverage.Count - 1] = previous; continue; }
            }
            coverage.Add(row);
        }
    }

    private static bool GetWindowVisualBounds(IntPtr window, out Rect bounds)
    {
        if (DwmGetWindowAttribute(window, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out bounds, Marshal.SizeOf<Rect>()) == 0)
        {
            if (bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                return true;
        }
        return GetWindowRect(window, out bounds);
    }

    private static bool IsCloaked(IntPtr window)
    {
        return DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    // Cache in window-local coordinates: moving a source window only changes its
    // screen origin, and never waits for its next synchronous PrintWindow call.
    private sealed class WindowSnapshot
    {
        internal int Width, Height;
        internal byte[] Pixels = Array.Empty<byte>();
        internal byte[]? Spare;
        internal Rect[] Coverage = Array.Empty<Rect>();
        internal long Updated, Used;
    }
    private static readonly object snapshotLock = new object();
    private static readonly object printLock = new object();
    private static readonly Dictionary<IntPtr, WindowSnapshot> snapshots = new();
    private static bool refreshingSnapshot;
    private const int SnapshotPixelBudget = 12_000_000;

    private static bool IsSunloginWindow(IntPtr window)
    {
        if (GetWindowThreadProcessId(window, out uint pid) == 0 || pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            return name.Equals("AweSun", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Sunlogin", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool IsBlackCapture(byte[] pixels, int stride, Rect crop, bool requireOpaque)
    {
        long pixelCount = (long)(crop.Right - crop.Left) * (crop.Bottom - crop.Top);
        if (pixelCount <= 0 || stride < crop.Right || (long)crop.Bottom * stride * 4 > pixels.Length) return false;
        long remainingNonBlack = pixelCount / 200;
        for (int y = crop.Top; y < crop.Bottom; y++)
        {
            int offset = (y * stride + crop.Left) * 4;
            for (int x = crop.Left; x < crop.Right; x++, offset += 4)
                if ((pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0 ||
                    (requireOpaque && pixels[offset + 3] != 255)) && --remainingNonBlack < 0)
                    return false;
        }
        return true;
    }


    internal static bool HasDesktopContent(byte[]? pixels, int pixelCount, bool opaqueCompositor)
    {
        if (pixels == null || pixelCount <= 0 || (long)pixelCount * 4 > pixels.Length) return false;
        if (opaqueCompositor) return true;
        int blackPixels = 0;
        for (int i = 0; i < pixelCount * 4; i += 4)
            if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0) blackPixels++;
        return blackPixels * 2 < pixelCount;
    }

    private static byte[]? CaptureDesktopSlice(IntPtr window, Rect bounds, Rect target, byte[]? reusable,
        out bool opaqueCompositor)
    {
        opaqueCompositor = false;
        int width = target.Right - target.Left, height = target.Bottom - target.Top;
        // Observe the shell without asking Explorer to repaint an entire monitor.
        // Retain the compatibility snapshot only as a bounded fallback.
        if (CompositorWindowCapture.TryGetFrame(window, target.Left, target.Top, width, height, out var frame) && frame != null)
        {
            using var lease = frame;
            if (TryGetCompositorBounds(window, frame.Width, frame.Height, out var origin))
            {
                var crop = new Rect(origin.Left + frame.PixelLeft, origin.Top + frame.PixelTop,
                    origin.Left + frame.PixelLeft + frame.PixelWidth, origin.Top + frame.PixelTop + frame.PixelHeight);
                if (Intersect(target, origin, out var needed) && needed.Left >= crop.Left && needed.Top >= crop.Top &&
                    needed.Right <= crop.Right && needed.Bottom <= crop.Bottom)
                {
                    int length = checked(width * height * 4);
                    byte[] slice = reusable?.Length == length ? reusable : new byte[length];
                    Array.Clear(slice);
                    for (int y = needed.Top; y < needed.Bottom; y++)
                        Buffer.BlockCopy(frame.PixelsBGRA, ((y - crop.Top) * frame.PixelWidth + needed.Left - crop.Left) * 4,
                            slice, ((y - target.Top) * width + needed.Left - target.Left) * 4, (needed.Right - needed.Left) * 4);
                    // The legacy desktop compositor expects opaque RGB. A transparent
                    // shell surface needs its compatibility snapshot to avoid black fringes.
                    bool opaque = true;
                    for (int y = needed.Top; opaque && y < needed.Bottom; y++)
                        for (int x = needed.Left; x < needed.Right; x++)
                            if (slice[((y - target.Top) * width + x - target.Left) * 4 + 3] != 255)
                            { opaque = false; break; }
                    if (opaque)
                    {
                        opaqueCompositor = needed.Left == target.Left && needed.Top == target.Top &&
                            needed.Right == target.Right && needed.Bottom == target.Bottom;
                        return slice;
                    }
                }
                else CompositorWindowCapture.RequestRefresh(window);
            }
        }
        return CaptureWindowSlice(window, bounds, target, reusable, minimumRefreshMilliseconds: 1000);
    }

    private static byte[]? CaptureWindowSlice(IntPtr window, Rect bounds, Rect target, byte[]? reusable = null,
        List<Rect>? trayCoverage = null, List<Rect>? windowCoverage = null, int minimumRefreshMilliseconds = 100)
    {
        if (!IsWindowAvailable(window) || !GetWindowRect(window, out bounds)) return null;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0 || (long)width * height > SnapshotPixelBudget)
        {
            trayCoverage?.Add(bounds);
            AddScreenCoverage(GetWindowCoverage(window, width, height), bounds, windowCoverage);
            return CaptureWindowSliceUncached(window, bounds, target);
        }
        WindowSnapshot? snapshot;
        long now = Environment.TickCount64;
        lock (snapshotLock)
        {
            var expired = new List<IntPtr>();
            foreach (var entry in snapshots)
                if (now - entry.Value.Used >= 10000 || !IsWindowAvailable(entry.Key)) expired.Add(entry.Key);
            foreach (var key in expired) snapshots.Remove(key);
            snapshots.TryGetValue(window, out snapshot);
            if (snapshot != null && (snapshot.Width != width || snapshot.Height != height))
            {
                snapshots.Remove(window);
                snapshot = null;
            }
        }
        if (snapshot == null)
        {
            byte[]? initial;
            Rect[] coverage;
            lock (printLock) initial = CaptureShapedWindow(window, bounds, null, out coverage);
            if (initial == null) return null;
            snapshot = new WindowSnapshot { Width = width, Height = height, Pixels = initial, Coverage = coverage, Updated = now, Used = now };
            lock (snapshotLock)
            {
                long total = (long)width * height;
                foreach (var item in snapshots.Values) total += (long)item.Width * item.Height;
                while (snapshots.Count >= 4 || total > SnapshotPixelBudget)
                {
                    IntPtr oldest = IntPtr.Zero;
                    long used = long.MaxValue;
                    foreach (var item in snapshots)
                        if (item.Value.Used < used) { used = item.Value.Used; oldest = item.Key; }
                    if (oldest == IntPtr.Zero) break;
                    var removed = snapshots[oldest];
                    total -= (long)removed.Width * removed.Height;
                    snapshots.Remove(oldest);
                }
                snapshots[window] = snapshot;
            }
        }
        lock (snapshotLock)
        {
            snapshot.Used = now;
            if (!refreshingSnapshot && now - snapshot.Updated >= minimumRefreshMilliseconds)
            {
                refreshingSnapshot = true;
                var pending = snapshot;
                _ = Task.Run(() =>
                {
                    try
                    {
                        byte[]? fresh = null;
                        Rect[] coverage = Array.Empty<Rect>();
                        lock (printLock)
                            if (IsWindowAvailable(window) && GetWindowRect(window, out Rect current) && current.Right - current.Left == width && current.Bottom - current.Top == height)
                                fresh = CaptureShapedWindow(window, current, pending.Spare, out coverage);
                        lock (snapshotLock)
                        {
                            if (fresh != null && IsWindowAvailable(window) && snapshots.TryGetValue(window, out var existing) && ReferenceEquals(existing, pending))
                            {
                                pending.Spare = pending.Pixels;
                                pending.Pixels = fresh;
                                pending.Coverage = coverage;
                            }
                            pending.Updated = Environment.TickCount64;
                        }
                    }
                    finally { lock (snapshotLock) refreshingSnapshot = false; }
                });
            }
        }
        byte[] slice;
        lock (snapshotLock)
        {
            int targetW = target.Right - target.Left, targetH = target.Bottom - target.Top;
            int length = checked(targetW * targetH * 4);
            if (trayCoverage != null) GetTrayContentCoverage(snapshot.Pixels, bounds, trayCoverage);
            // Pair pixels with their captured shape; current expansion must not expose
            // old transparent backing before the refreshed snapshot arrives.
            AddScreenCoverage(IntersectCoverage(snapshot.Coverage, GetWindowCoverage(window, width, height)), bounds, windowCoverage);
            slice = reusable?.Length == length ? reusable : new byte[length];
            if (ReferenceEquals(slice, reusable) &&
                (bounds.Left > target.Left || bounds.Top > target.Top || bounds.Right < target.Right || bounds.Bottom < target.Bottom))
                Array.Clear(slice);
            int left = Math.Max(target.Left, bounds.Left), right = Math.Min(target.Right, bounds.Right);
            int top = Math.Max(target.Top, bounds.Top), bottom = Math.Min(target.Bottom, bounds.Bottom);
            if (right > left)
                for (int y = top; y < bottom; y++)
                    Buffer.BlockCopy(snapshot.Pixels, ((y - bounds.Top) * width + left - bounds.Left) * 4,
                        slice, ((y - target.Top) * targetW + left - target.Left) * 4, (right - left) * 4);
        }
        return slice;
    }

    private static byte[]? CaptureShapedWindow(IntPtr window, Rect bounds, byte[]? reusable, out Rect[] coverage)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        var before = GetWindowCoverage(window, width, height);
        var pixels = CaptureWindow(window, bounds, reusable);
        coverage = IntersectCoverage(before, GetWindowCoverage(window, width, height));
        return pixels;
    }

    private static Rect[] GetWindowCoverage(IntPtr window, int width, int height) =>
        ReadWindowCoverage(window, width, height, true);

    private static Rect[] ReadWindowCoverage(IntPtr window, int width, int height, bool trimGuard)
    {
        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero) return Array.Empty<Rect>();
        try
        {
            int kind = GetWindowRgn(window, region);
            if (kind == 0) return new[] { new Rect(0, 0, width, height) }; // No explicit region.
            if (kind == 1) return Array.Empty<Rect>(); // Empty region.
            // Our native clip has an antialiasing guard. Do not treat its unpainted
            // fringe as opaque when this window is another widget's background.
            long diameter = GetProp(window, "OffWorkWidget.GlassDiameterPx").ToInt64();
            if (trimGuard && diameter > 0 && diameter <= 4096 && GetRgnBox(region, out Rect box) > 1)
            {
                long guard = GetProp(window, "OffWorkWidget.GlassGuardPx").ToInt64();
                if (guard > 0 && guard <= 16)
                {
                    // The HWND region is deliberately larger than the painted card.
                    // Undo that padding before reconstructing its opaque coverage.
                    box.Left += (int)guard; box.Top += (int)guard;
                    box.Right -= (int)guard; box.Bottom -= (int)guard;
                }
                IntPtr painted = CreateRoundRectRgn(box.Left, box.Top, box.Right + 1, box.Bottom + 1, (int)diameter, (int)diameter);
                if (painted != IntPtr.Zero)
                {
                    try { if (CombineRgn(region, region, painted, 1 /* RGN_AND */) == 1) return Array.Empty<Rect>(); }
                    finally { DeleteObject(painted); }
                }
            }
            uint size = GetRegionData(region, 0, null);
            if (size < 32 || size > 4_000_000) return Array.Empty<Rect>();
            var data = new byte[size];
            if (GetRegionData(region, size, data) != size) return Array.Empty<Rect>();
            int header = BitConverter.ToInt32(data, 0), count = BitConverter.ToInt32(data, 8);
            if (header < 32 || count < 0 || (long)header + (long)count * 16 > data.Length) return Array.Empty<Rect>();
            var result = new List<Rect>();
            for (int i = 0; i < count; i++)
            {
                int offset = header + i * 16;
                var part = new Rect(BitConverter.ToInt32(data, offset), BitConverter.ToInt32(data, offset + 4),
                    BitConverter.ToInt32(data, offset + 8), BitConverter.ToInt32(data, offset + 12));
                if (Intersect(part, new Rect(0, 0, width, height), out Rect clipped)) result.Add(clipped);
            }
            return result.ToArray();
        }
        finally { DeleteObject(region); }
    }

    private static Rect[] IntersectCoverage(Rect[] left, Rect[] right)
    {
        var result = new List<Rect>();
        foreach (Rect a in left)
            foreach (Rect b in right)
                if (Intersect(a, b, out Rect hit)) result.Add(hit);
        return result.ToArray();
    }

    private static void AddScreenCoverage(Rect[] local, Rect bounds, List<Rect>? output)
    {
        if (output == null) return;
        foreach (Rect part in local)
            output.Add(new Rect(part.Left + bounds.Left, part.Top + bounds.Top, part.Right + bounds.Left, part.Bottom + bounds.Top));
    }

    private static byte[]? CaptureWindowSliceUncached(IntPtr window, Rect bounds, Rect target)
    {
        // PrintWindow includes the non-client frame. DWM visual bounds can exclude
        // invisible resize borders, so they must not be used as the bitmap origin.
        if (!IsWindowAvailable(window) || !GetWindowRect(window, out bounds)) return null;
        int targetW = target.Right - target.Left;
        int targetH = target.Bottom - target.Top;
        if (targetW <= 0 || targetH <= 0 || targetW > 4096 || targetH > 4096)
            return null;

        int sourceW = bounds.Right - bounds.Left;
        int sourceH = bounds.Bottom - bounds.Top;
        if (sourceW <= 0 || sourceH <= 0 || sourceW > 16384 || sourceH > 16384 || (long)sourceW * sourceH > 32_000_000)
            return null;

        int cropX = target.Left - bounds.Left;
        int cropY = target.Top - bounds.Top;
        if (cropX + targetW <= 0 || cropX >= sourceW || cropY + targetH <= 0 || cropY >= sourceH)
            return null;

        IntPtr dc = IntPtr.Zero, dib = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                Width = sourceW,
                Height = -sourceH, // Top-down
                Planes = 1,
                BitCount = 32
            };
            dib = CreateDIBSection(dc, ref info, 0, out IntPtr pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || pixels == IntPtr.Zero) return null;
            previous = SelectObject(dc, dib);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return null;

            if (!PrintWindow(window, dc, 2) && !PrintWindow(window, dc, 0) && !PrintWindow(window, dc, 3) && !PrintWindow(window, dc, 1)) return null;

            var slice = new byte[checked(targetW * targetH * 4)];
            int validLeft = Math.Max(0, cropX);
            int validRight = Math.Min(sourceW, cropX + targetW);
            int validTop = Math.Max(0, cropY);
            int validBottom = Math.Min(sourceH, cropY + targetH);

            int copyW = validRight - validLeft;
            if (copyW > 0)
            {
                int copyBytes = copyW * 4;
                int dstStartX = validLeft - cropX;
                for (int y = validTop; y < validBottom; y++)
                {
                    int dstY = y - cropY;
                    IntPtr srcRow = IntPtr.Add(pixels, (y * sourceW + validLeft) * 4);
                    int dstOffset = (dstY * targetW + dstStartX) * 4;
                    Marshal.Copy(srcRow, slice, dstOffset, copyBytes);
                }
            }

            return slice;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (dc != IntPtr.Zero && previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }

    private static byte[]? CaptureWindow(IntPtr window, Rect bounds, byte[]? reusable = null)
    {
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > 24_000_000)
            return null;
        IntPtr dc = IntPtr.Zero, dib = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };
            dib = CreateDIBSection(dc, ref info, 0, out IntPtr pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || pixels == IntPtr.Zero) return null;
            previous = SelectObject(dc, dib);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) return null;
            if (!PrintWindow(window, dc, 2) && !PrintWindow(window, dc, 0) && !PrintWindow(window, dc, 3) && !PrintWindow(window, dc, 1)) return null;
            int length = checked(width * height * 4);
            var bytes = reusable?.Length == length ? reusable : new byte[length];
            Marshal.Copy(pixels, bytes, 0, bytes.Length);
            return bytes;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (dc != IntPtr.Zero && previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (dc != IntPtr.Zero) DeleteDC(dc);
        }
    }

    private static bool Intersect(Rect left, Rect right, out Rect result)
    {
        result = new Rect(Math.Max(left.Left, right.Left), Math.Max(left.Top, right.Top),
            Math.Min(left.Right, right.Right), Math.Min(left.Bottom, right.Bottom));
        return result.Right > result.Left && result.Bottom > result.Top;
    }

    private static void Subtract(Rect source, Rect cover, List<Rect> destination)
    {
        if (!Intersect(source, cover, out Rect hit)) { destination.Add(source); return; }
        if (source.Top < hit.Top) destination.Add(new Rect(source.Left, source.Top, source.Right, hit.Top));
        if (hit.Bottom < source.Bottom) destination.Add(new Rect(source.Left, hit.Bottom, source.Right, source.Bottom));
        if (source.Left < hit.Left) destination.Add(new Rect(source.Left, hit.Top, hit.Left, hit.Bottom));
        if (hit.Right < source.Right) destination.Add(new Rect(hit.Right, hit.Top, source.Right, hit.Bottom));
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect
    {
        internal int Left, Top, Right, Bottom;
        internal Rect(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter; internal uint ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region, out Rect bounds);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern uint GetRegionData(IntPtr region, uint size, [Out] byte[]? data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern uint GetSysColor(int nIndex);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SystemParametersInfo(uint uAction, uint uParam, StringBuilder lpvParam, uint fuWinIni);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool SetWindowOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}
