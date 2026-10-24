using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace OffWorkWidget.WinUI
{
    public sealed class SettingsWindow : Window
    {
        readonly OffWorkWidget.Settings current;
        readonly OffWorkWidget.PrivacyStore privacy;
        readonly Action toggleSalaryLayout;
        readonly Action hideSalary;
        readonly Action<string> selectPet;
        readonly Action<OffWorkWidget.Settings> saveSettings;
        readonly Action<double>? previewScale;

        readonly Grid windowSurface = new Grid();
        readonly SystemBackdropElement frostedBackground = new SystemBackdropElement { CornerRadius = new CornerRadius(24), IsHitTestVisible = false };
        Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? windowGeometry;
        readonly Grid root = new Grid();
        readonly StackPanel form = new StackPanel { Spacing = 0 };
        readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        readonly List<OffWorkWidget.DateAdjustment> adjustments;
        readonly List<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton> weekdays = new List<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>();
        readonly List<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton> restPresetButtons = new();
        bool customRestPreset, settingRestPreset;
        int singleRestDays = 1, doubleRestDays = 65;
        bool alternatingWeekends;
        DateTime doubleRestWeekStart;
        Grid alternatingWeekRow = null!;
        TextBlock restWeekHint = null!;
        readonly List<Border> cardBorders = new List<Border>();
        readonly StackPanel adjustmentRows = new StackPanel { Spacing = 4 };
        TextBlock adjustmentSummary = null!;
        Microsoft.UI.Dispatching.DispatcherQueueTimer? scrollRailTimer;

        bool windowClosed;
        TimeSpan startTime, endTime;
        TimeSpan dayTimeStart, dayTimeEnd;
        bool dayTimeEnabled, loadingSchedule;
        string scheduleMode = "";
        readonly List<OffWorkWidget.WorkScheduleProfile> workSchedules = new();
        TextBlock scheduleLabel = null!, dayTimeStartDisplay = null!, dayTimeEndDisplay = null!;
        ToggleSwitch dayTimeSwitch = null!;
        Grid dayTimeTimes = null!;
        readonly List<OffWorkWidget.DayWorkTime> dayTimes = new();
        int editingDay = 6;
        bool dayTimeExpanded;
        StackPanel dayTimeSection = null!, dayTimeEditor = null!;
        Border dayTimeReveal = null!;
        readonly RectangleGeometry dayTimeRevealClip = new RectangleGeometry();
        readonly Stopwatch dayTimeRevealClock = new Stopwatch();
        bool animatingDayTimeReveal;
        double dayTimeRevealHeight, dayTimeRevealStartHeight, dayTimeRevealTargetHeight;
        double dayTimeRevealStartOpacity, dayTimeRevealTargetOpacity, dayTimeArrowStart, dayTimeArrowTarget;
        double dayTimeEditorStartOpacity, dayTimeEditorTargetOpacity;
        ComboBox dayTimeDay = null!;
        TextBlock dayTimeSummary = null!, dayTimeStartLabel = null!, dayTimeEndLabel = null!;
        Grid dayTimeChevron = null!;
        TextBlock startTimeDisplay, endTimeDisplay;
        TextBox salary, workDays, payDay;
        int waterMinutes, walkMinutes;
        Button waterIntervalButton = null!, walkIntervalButton = null!;
        Button amountDurationButton = null!;
        TextBlock amountDurationDisplay = null!;
        int amountDurationSeconds;
        Flyout? amountDurationFlyout;
        Slider scale = null!;
        Canvas scaleRuler = null!;
        Grid scaleInput = null!;
        enum ScaleGestureDirection { Pending, Horizontal, Vertical }
        ScaleGestureDirection scaleGestureDirection;
        uint? scalePointerId;
        Windows.Foundation.Point scaleGestureStart;
        double scaleGestureStartX, scaleGestureScrollStart;
        bool scaleGestureMoved, scalePositionQueued;
        double? pendingScaleX;
        TextBlock scaleReadout = null!;
        readonly List<Border> scaleTicks = new List<Border>();
        Border scaleIndicator = null!, scaleReadoutBadge = null!;
        double? scaleDragValue;
        bool settingScaleFromPointer;
        ToggleSwitch holidays, water, walk, topmost;
        TextBlock holidayStatus, passwordStatus, scaleLabel;
        Image petThumb;
        Border incomeContent, restContent, careContent;
        Grid incomeChevron, restChevron, careChevron;
        Microsoft.UI.Xaml.Shapes.Path eyeSlash;
        ToggleSwitch salaryLayoutSwitch;

        ScrollViewer settingsScroll = null!;
        TextBlock compactTitle = null!;
        Border toolbar = null!;
        Button autoAppearanceButton = null!, nightAppearanceButton = null!, backToTop = null!;
        readonly SolidColorBrush pageBackgroundBrush = new SolidColorBrush();
        readonly SolidColorBrush headerBackgroundBrush = new SolidColorBrush();
        readonly SolidColorBrush footerBackgroundBrush = new SolidColorBrush();
        readonly SolidColorBrush footerActionBrush = new SolidColorBrush();
        readonly LinearGradientBrush footerFade = new LinearGradientBrush();
        readonly LinearGradientBrush headerFade = new LinearGradientBrush();
        readonly Border headerFadeLayer = new Border { Height = 24, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };
        readonly Border[] accordionCards = new Border[3];
        readonly Border[] accordionDividers = new Border[2];
        readonly double[] accordionProgress = new double[3], accordionStart = new double[3], accordionTarget = new double[3];
        readonly Stopwatch accordionClock = new Stopwatch();
        readonly Windows.UI.ViewManagement.UISettings animationSettings = new Windows.UI.ViewManagement.UISettings();
        bool animatingAccordion;
        int expandedAccordion;
        bool salaryAuthorized, automatic, night, privacyPromptOpen;
        string? salaryDraft;
        string petId;
        decimal observedSalary;
        IntPtr hwnd;
        readonly AppWindow native;
        readonly WinUIEx.Messaging.WindowMessageMonitor frameMonitor;
        WinUIEx.Messaging.WindowMessageMonitor? renderFrameMonitor;
        IntPtr frameRenderHandle;

        readonly SolidColorBrush inkBrush = new SolidColorBrush();
        readonly SolidColorBrush subInkBrush = new SolidColorBrush();
        readonly SolidColorBrush cardBackgroundBrush = new SolidColorBrush();
        readonly SolidColorBrush inputBackgroundBrush = new SolidColorBrush();
        readonly SolidColorBrush timeHoverBrush = new SolidColorBrush();
        readonly SolidColorBrush timePressedBrush = new SolidColorBrush();
        readonly SolidColorBrush dividerBrush = new SolidColorBrush();
        readonly SolidColorBrush accentBlue = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 122, 255));
        readonly SolidColorBrush cancelGray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 229, 234));
        readonly SolidColorBrush cancelDarkGray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 58, 60));

        public SettingsWindow(OffWorkWidget.Settings current, OffWorkWidget.PrivacyStore privacy,
            Action toggleSalaryLayout, Action hideSalary, Action<string> selectPet,
            Action<OffWorkWidget.Settings> saveSettings, Action<double>? previewScale = null)
        {
            this.current = current; this.privacy = privacy; this.toggleSalaryLayout = toggleSalaryLayout;
            this.hideSalary = hideSalary; this.selectPet = selectPet; this.saveSettings = saveSettings; this.previewScale = previewScale;
            automatic = current.AutomaticAppearance; night = current.NightMode; petId = current.PetId; observedSalary = current.Salary;
            alternatingWeekends = current.AlternatingWeekends;
            doubleRestWeekStart = DateTime.TryParseExact(current.DoubleRestWeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime anchor)
                ? OffWorkWidget.Logic.WeekStart(anchor) : OffWorkWidget.Logic.WeekStart(DateTime.Today);
            adjustments = (current.DateAdjustments ?? new List<OffWorkWidget.DateAdjustment>())
                .Select(a => new OffWorkWidget.DateAdjustment { Date = a.Date, IsWorkday = a.IsWorkday }).ToList();

            startTime = OffWorkWidget.Logic.Time(current.Start);
            endTime = OffWorkWidget.Logic.Time(current.End);
            foreach (var profile in current.WorkSchedules ?? new List<OffWorkWidget.WorkScheduleProfile>())
                workSchedules.Add(new OffWorkWidget.WorkScheduleProfile { Mode = profile.Mode, Start = profile.Start, End = profile.End, SaturdayEnabled = profile.SaturdayEnabled, SaturdayStart = profile.SaturdayStart, SaturdayEnd = profile.SaturdayEnd, DayTimes = (profile.DayTimes ?? new List<OffWorkWidget.DayWorkTime>()).Select(d => new OffWorkWidget.DayWorkTime { Day = d.Day, Enabled = d.Enabled, Start = d.Start, End = d.End }).ToList() });
            customRestPreset = current.WorkScheduleMode == "custom";
            singleRestDays = current.SingleRestDays; doubleRestDays = current.DoubleRestDays;
            if (!current.AlternatingWeekends)
            {
                if (OffWorkWidget.Logic.RestDayCount(current.RestDays) == 1) singleRestDays = current.RestDays;
                if (OffWorkWidget.Logic.RestDayCount(current.RestDays) == 2) doubleRestDays = current.RestDays;
            }
            LoadSchedule(OffWorkWidget.Logic.ScheduleMode(current));

            Title = "把下班，设成期待";
            hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            native = AppWindow.GetFromWindowId(id);
            if (native.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
            int style = GetWindowLong(hwnd, -16);
            SetWindowLong(hwnd, -16, (style & ~(0x00C00000 | 0x00040000 | 0x00020000 | 0x00010000)) | unchecked((int)0x80000000));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
            int round = 1, noBorder = -2, ncDisabled = 1;
            DwmSetWindowAttribute(hwnd, 2, ref ncDisabled, 4);
            DwmSetWindowAttribute(hwnd, 33, ref round, 4);
            DwmSetWindowAttribute(hwnd, 34, ref noBorder, 4);
            frameMonitor = new WinUIEx.Messaging.WindowMessageMonitor(hwnd);
            frameMonitor.WindowMessageReceived += PaintFrameBackground;
            root.Loaded += (_, __) => UpdateWindowContour();
            native.Changed += (_, args) => { if (args.DidSizeChange) UpdateWindowContour(); };

            root.Resources["ContentControlThemeFontFamily"] = new FontFamily("Microsoft YaHei");
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Microsoft YaHei")));
            root.Resources[typeof(TextBlock)] = textStyle;

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ActualThemeChanged += (_, __) => ApplyThemeBrushes();
            SystemBackdrop = new WinUIEx.TransparentTintBackdrop();
            windowSurface.Children.Add(frostedBackground); windowSurface.Children.Add(root);
            windowSurface.SizeChanged += (_, __) => UpdateWindowContour();
            Content = windowSurface;

            ApplyTheme();
            BuildHeader();

            settingsScroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            settingsScroll.ViewChanged += (_, __) => UpdateScrollChrome();
            form.Margin = new Thickness(16, 0, 16, 8);
            var scrollSurface = CreateScrollSurface();
            Grid.SetRow(scrollSurface, 1); root.Children.Add(scrollSurface);
            BuildHero();

            BuildWorkingHoursCard();
            BuildAccordionGroup();
            BuildDesktopLayoutCard();
            BuildTopmostCard();

            BuildFooter();
            headerFade.StartPoint = new Windows.Foundation.Point(0, 0); headerFade.EndPoint = new Windows.Foundation.Point(0, 1);
            foreach (double offset in new[] { 0d, .28, .68, 1d }) headerFade.GradientStops.Add(new GradientStop { Offset = offset });
            headerFadeLayer.Background = headerFade;
            Grid.SetRow(headerFadeLayer, 1); root.Children.Add(headerFadeLayer);
            PositionWindow();
            ApplyThemeBrushes();
            Closed += (_, __) =>
            {
                windowClosed = true; scrollRailTimer?.Stop();
                CancelScaleGesture(); scaleInput?.ReleasePointerCaptures();
                CompositionTarget.Rendering -= AnimateAccordion; animatingAccordion = false;
                CompositionTarget.Rendering -= AnimateDayTimeReveal; animatingDayTimeReveal = false; dayTimeRevealClock.Stop();
                renderFrameMonitor?.Dispose(); frameMonitor.Dispose();
                frostedBackground.SystemBackdrop = null; SystemBackdrop = null;
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(windowSurface).Clip = null;
                windowGeometry?.Dispose();
            };
        }

        void PositionWindow()
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "assets", "app-icon.ico");
            if (File.Exists(icon)) native.SetIcon(icon);
            var area = DisplayArea.GetFromWindowId(native.Id, DisplayAreaFallback.Primary).WorkArea;
            double dpi = Math.Max(96, GetDpiForWindow(hwnd)) / 96d;
            int width = Math.Min((int)Math.Round(344 * dpi), area.Width);
            int height = Math.Min((int)Math.Round(580 * dpi), area.Height);
            native.Resize(new SizeInt32(width, height));
            native.Move(new PointInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2));
            UpdateWindowContour();
        }

        void UpdateWindowContour()
        {
            // A composition clip has fractional-alpha edges; a native window
            // region only keeps or discards complete pixels and creates stair steps.
            SetWindowRgn(hwnd, IntPtr.Zero, false);
            if (windowSurface.ActualWidth > 0 && windowSurface.ActualHeight > 0)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(windowSurface);
                if (windowGeometry == null)
                {
                    windowGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
                    visual.Clip = visual.Compositor.CreateGeometricClip(windowGeometry);
                }
                windowGeometry.Size = new System.Numerics.Vector2((float)windowSurface.ActualWidth, (float)windowSurface.ActualHeight);
                windowGeometry.CornerRadius = new System.Numerics.Vector2(24);
            }
            IntPtr render = FindWindowEx(hwnd, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
            if (render != IntPtr.Zero && render != frameRenderHandle)
            {
                renderFrameMonitor?.Dispose(); frameRenderHandle = render;
                renderFrameMonitor = new WinUIEx.Messaging.WindowMessageMonitor(render);
                renderFrameMonitor.WindowMessageReceived += PaintFrameBackground;
            }
            ClearFrameBackground();
        }

        bool FillFrameBackground(IntPtr window, IntPtr dc)
        {
            if (dc == IntPtr.Zero || !GetClientRect(window, out ClientRect rect)) return false;
            // TransparentTintBackdrop uses zero-alpha black beneath the XAML
            // surface, including the pixels outside its antialiased rounded clip.
            IntPtr brush = CreateSolidBrush(0);
            if (brush == IntPtr.Zero) return false;
            try { return FillRect(dc, ref rect, brush) != 0; }
            finally { DeleteObject(brush); }
        }

        void PaintFrameBackground(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
        {
            if (args.Message.MessageId != 0x0014 || !FillFrameBackground(args.Message.Hwnd, (IntPtr)args.Message.WParam)) return;
            args.Result = (IntPtr)1; args.Handled = true;
        }

        void ClearFrameBackground()
        {
            IntPtr dc = GetDC(hwnd);
            if (dc == IntPtr.Zero) return;
            try { FillFrameBackground(hwnd, dc); }
            finally { ReleaseDC(hwnd, dc); }
        }

        void ChangeAppearance(bool useAutomatic)
        {
            // Complete the click/pointer dispatch before updating theme resources.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (windowClosed) return;
                automatic = useAutomatic;
                night = !useAutomatic && root.ActualTheme != ElementTheme.Dark;
                ApplyTheme();
            });
        }

        void ApplyTheme()
        {
            root.RequestedTheme = automatic ? ElementTheme.Default : night ? ElementTheme.Dark : ElementTheme.Light;
            windowSurface.RequestedTheme = root.RequestedTheme;
            if (frostedBackground.SystemBackdrop == null)
                frostedBackground.SystemBackdrop = new DesktopAcrylicBackdrop();
            // Keep the backdrop target alive while pointer input is being dispatched.
            // Manual themes cover it with their opaque page brush.
            ApplyThemeBrushes();
        }

        void ApplyThemeBrushes()
        {
            bool dark = root.ActualTheme == ElementTheme.Dark;
            pageBackgroundBrush.Color = dark ? Windows.UI.Color.FromArgb((byte)(automatic ? 16 : 255), 28, 28, 30) : Windows.UI.Color.FromArgb((byte)(automatic ? 20 : 255), 242, 242, 247);
            root.Background = pageBackgroundBrush;
            footerActionBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 64, 64, 72) : Windows.UI.Color.FromArgb(255, 220, 223, 232);
            footerBackgroundBrush.Color = dark ? Windows.UI.Color.FromArgb((byte)(automatic ? 190 : 255), 28, 28, 30) : Windows.UI.Color.FromArgb((byte)(automatic ? 210 : 255), 242, 242, 247);
            if (footerFade.GradientStops.Count == 2)
            {
                var c = footerBackgroundBrush.Color;
                footerFade.GradientStops[0].Color = Windows.UI.Color.FromArgb(0, c.R, c.G, c.B);
                footerFade.GradientStops[1].Color = c;
            }
            if (headerFade.GradientStops.Count == 4)
            {
                var c = footerBackgroundBrush.Color;
                double[] strengths = { 1, .8, .28, 0 };
                for (int i = 0; i < strengths.Length; i++)
                    headerFade.GradientStops[i].Color = Windows.UI.Color.FromArgb((byte)Math.Round(c.A * strengths[i]), c.R, c.G, c.B);
            }
            UpdateHeaderTransition();
            if (autoAppearanceButton != null)
            {
                autoAppearanceButton.Background = automatic ? footerActionBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                nightAppearanceButton.Background = !automatic ? footerActionBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                ((FontIcon)nightAppearanceButton.Content).Glyph = dark ? "\uE706" : "\uE708";
            }
            inkBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 242, 242, 247) : Windows.UI.Color.FromArgb(255, 28, 28, 30);
            subInkBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 174, 174, 182) : Windows.UI.Color.FromArgb(255, 107, 107, 116);
            cardBackgroundBrush.Color = dark ? Windows.UI.Color.FromArgb((byte)(automatic ? 80 : 255), 40, 40, 44) : Windows.UI.Color.FromArgb((byte)(automatic ? 115 : 255), 255, 255, 255);
            inputBackgroundBrush.Color = dark
                ? (automatic ? Windows.UI.Color.FromArgb(240, 18, 18, 22) : Windows.UI.Color.FromArgb(255, 28, 28, 30))
                : (automatic ? Windows.UI.Color.FromArgb(240, 220, 224, 234) : Windows.UI.Color.FromArgb(255, 242, 243, 247));
            timeHoverBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 48, 48, 54) : automatic ? Windows.UI.Color.FromArgb(255, 210, 215, 227) : Windows.UI.Color.FromArgb(255, 233, 234, 241);
            timePressedBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 58, 58, 64) : automatic ? Windows.UI.Color.FromArgb(255, 200, 206, 221) : Windows.UI.Color.FromArgb(255, 221, 223, 233);
            dividerBrush.Color = dark ? Windows.UI.Color.FromArgb(160, 80, 80, 88) : Windows.UI.Color.FromArgb(180, 218, 220, 228);

            foreach (var card in cardBorders)
            {
                card.Background = cardBackgroundBrush;
            }
            ClearFrameBackground();
        }

        Grid CreateScrollSurface()
        {
            var host = new Grid(); host.Children.Add(settingsScroll);
            var rail = new Canvas
            {
                Width = 12, Margin = new Thickness(0, 4, 2, 4), HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Opacity = 0
            };
            var thumb = new Border { Width = 3, CornerRadius = new CornerRadius(3), Background = subInkBrush };
            rail.Children.Add(thumb); host.Children.Add(rail);
            bool dragging = false;
            double dragY = 0, dragOffset = 0;
            void UpdateRail()
            {
                if (windowClosed) return;
                double height = Math.Max(0, host.ActualHeight - rail.Margin.Top - rail.Margin.Bottom);
                bool needed = settingsScroll.ScrollableHeight > 1 && height > 0;
                rail.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
                if (!needed) return;
                thumb.Height = Math.Min(height, Math.Max(28, height * settingsScroll.ViewportHeight / Math.Max(1, settingsScroll.ExtentHeight)));
                Canvas.SetLeft(thumb, (rail.Width - thumb.Width) / 2);
                Canvas.SetTop(thumb, (height - thumb.Height) * settingsScroll.VerticalOffset / settingsScroll.ScrollableHeight);
            }
            scrollRailTimer = DispatcherQueue.CreateTimer(); scrollRailTimer.Interval = TimeSpan.FromMilliseconds(1200);
            scrollRailTimer.Tick += (_, __) => { scrollRailTimer.Stop(); if (!windowClosed && !dragging) rail.Opacity = 0; };
            void Reveal()
            {
                if (windowClosed) return;
                UpdateRail(); rail.Opacity = dragging ? .85 : .55;
                scrollRailTimer.Stop(); if (!dragging) scrollRailTimer.Start();
            }
            void EndDrag()
            {
                if (!dragging) return;
                dragging = false; rail.ReleasePointerCaptures(); Reveal();
            }
            settingsScroll.ViewChanged += (_, __) => Reveal();
            settingsScroll.SizeChanged += (_, __) => UpdateRail();
            rail.SizeChanged += (_, __) => UpdateRail();
            host.PointerMoved += (_, __) => Reveal();
            host.PointerExited += (_, __) => { if (!windowClosed && !dragging) scrollRailTimer.Start(); };
            rail.PointerPressed += (_, e) =>
            {
                var point = e.GetCurrentPoint(rail);
                if (!point.Properties.IsLeftButtonPressed && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse) return;
                double travel = rail.ActualHeight - thumb.Height;
                if (travel <= 0 || settingsScroll.ScrollableHeight <= 0) return;
                double top = Canvas.GetTop(thumb);
                dragOffset = point.Position.Y >= top && point.Position.Y <= top + thumb.Height
                    ? settingsScroll.VerticalOffset
                    : Math.Clamp((point.Position.Y - thumb.Height / 2) / travel, 0, 1) * settingsScroll.ScrollableHeight;
                dragY = point.Position.Y; dragging = true; rail.CapturePointer(e.Pointer);
                settingsScroll.ChangeView(null, dragOffset, null, true); Reveal(); e.Handled = true;
            };
            rail.PointerMoved += (_, e) =>
            {
                if (!dragging) return;
                double travel = rail.ActualHeight - thumb.Height;
                if (travel > 0) settingsScroll.ChangeView(null, Math.Clamp(dragOffset + (e.GetCurrentPoint(rail).Position.Y - dragY) / travel * settingsScroll.ScrollableHeight, 0, settingsScroll.ScrollableHeight), null, true);
                e.Handled = true;
            };
            rail.PointerReleased += (_, __) => EndDrag();
            rail.PointerCanceled += (_, __) => EndDrag();
            rail.PointerCaptureLost += (_, __) => EndDrag();
            rail.PointerWheelChanged += (_, e) =>
            {
                settingsScroll.ChangeView(null, Math.Clamp(settingsScroll.VerticalOffset - e.GetCurrentPoint(rail).Properties.MouseWheelDelta * .5, 0, settingsScroll.ScrollableHeight), null, false);
                Reveal(); e.Handled = true;
            };
            return host;
        }

        void BuildHeader()
        {
            var header = new Grid { Height = 40, Padding = new Thickness(16, 4, 16, 0), Background = headerBackgroundBrush };
            compactTitle = new TextBlock { Text = "把下班，设成期待", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 108, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
            header.Children.Add(compactTitle);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            autoAppearanceButton = CreateHeaderIconButton("\uE7F4", "系统自适应：随背景明暗变化", () => ChangeAppearance(true));
            nightAppearanceButton = CreateHeaderIconButton("\uE708", "切换日间或夜间模式", () => ChangeAppearance(false));
            var closeBtn = CreateHeaderIconButton("\uE711", "关闭设置", () => Close());
            right.Children.Add(autoAppearanceButton); right.Children.Add(nightAppearanceButton); right.Children.Add(closeBtn);
            header.Children.Add(right);
            SetTitleBar(header);
            void UpdateDragRegion()
            {
                if (windowClosed || root.XamlRoot == null || header.ActualHeight <= 0 || right.ActualWidth <= 0) return;
                double dpi = root.XamlRoot.RasterizationScale;
                var origin = header.TransformToVisual(root).TransformPoint(default);
                var buttons = right.TransformToVisual(root).TransformPoint(default);
                // Windows handles caption dragging; the three header buttons remain client input.
                native.TitleBar.SetDragRectangles(new[] { new RectInt32(
                    (int)Math.Ceiling(origin.X * dpi), (int)Math.Ceiling(origin.Y * dpi),
                    Math.Max(0, (int)Math.Floor((buttons.X - origin.X) * dpi)),
                    (int)Math.Floor(header.ActualHeight * dpi)) });
            }
            header.SizeChanged += (_, __) => UpdateDragRegion();
            right.SizeChanged += (_, __) => UpdateDragRegion();
            XamlRoot? dragRoot = null;
            void OnDragRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateDragRegion();
            header.Loaded += (_, __) =>
            {
                if (dragRoot != null) dragRoot.Changed -= OnDragRootChanged;
                dragRoot = root.XamlRoot;
                dragRoot.Changed += OnDragRootChanged;
                UpdateDragRegion();
            };
            header.Unloaded += (_, __) =>
            {
                if (dragRoot != null) dragRoot.Changed -= OnDragRootChanged;
                dragRoot = null;
            };

            toolbar = new Border { Child = header, BorderBrush = dividerBrush };
            Grid.SetRow(toolbar, 0); root.Children.Add(toolbar);
        }

        void BuildHero()
        {
            var hero = new Grid { Height = 64, Margin = new Thickness(0, 0, 0, 12) };
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.Children.Add(new TextBlock { Text = "把下班，设成期待", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            var petBtn = new Button
            {
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(14),
                Background = cardBackgroundBrush,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var petStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            petThumb = new Image { Width = 36, Height = 36, Source = PetImage(petId) };
            var petArrow = CreateChevron(90);
            petArrow.Width = petArrow.Height = 16;
            petStack.Children.Add(petThumb); petStack.Children.Add(petArrow);
            petBtn.Content = petStack;

            var petPanel = new StackPanel { Width = 176, Spacing = 4 };
            var petMenu = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight, Content = petPanel };
            var menuStyle = new Style(typeof(FlyoutPresenter));
            menuStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
            menuStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(16)));
            menuStyle.Setters.Add(new Setter(Control.BackgroundProperty, cardBackgroundBrush));
            menuStyle.Setters.Add(new Setter(Control.BorderBrushProperty, dividerBrush));
            menuStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            petMenu.FlyoutPresenterStyle = menuStyle;
            var petRows = new List<(string Id, Button Button, FontIcon Check)>();
            foreach (string id in new[] { "cat", "samoyed", "polar-bear" })
            {
                string selected = id;
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                row.Children.Add(new Image { Source = PetImage(id), Width = 32, Height = 34 });
                var name = new TextBlock { Text = PetName(id), FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
                var check = new FontIcon { Glyph = "\uE73E", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 13, Foreground = accentBlue };
                Grid.SetColumn(name, 1); row.Children.Add(name); Grid.SetColumn(check, 2); row.Children.Add(check);
                var item = new Button { Content = row, Height = 46, Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                item.FocusVisualPrimaryBrush = accentBlue; item.FocusVisualPrimaryThickness = new Thickness(1.5);
                item.Resources["ButtonBackgroundPointerOver"] = inputBackgroundBrush;
                item.Resources["ButtonBackgroundPressed"] = timePressedBrush;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, "选择" + PetName(id));
                item.Click += (_, __) =>
                {
                    petId = selected;
                    petThumb.Source = PetImage(selected);
                    selectPet(selected);
                    petMenu.Hide();
                };
                petRows.Add((id, item, check)); petPanel.Children.Add(item);
            }
            bool petKeyboardOpen = false;
            petMenu.Opened += (_, __) => petRows.First(row => row.Id == petId).Button.Focus(petKeyboardOpen ? FocusState.Keyboard : FocusState.Pointer);
            petMenu.Opening += (_, __) =>
            {
                petKeyboardOpen = petBtn.FocusState == FocusState.Keyboard;
                petPanel.RequestedTheme = root.ActualTheme;
                foreach (var row in petRows)
                {
                    bool selected = row.Id == petId;
                    row.Button.Background = selected ? inputBackgroundBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    row.Check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            petBtn.Flyout = petMenu;
            ToolTipService.SetToolTip(petBtn, "切换桌面宠物");
            Grid.SetColumn(petBtn, 1); hero.Children.Add(petBtn);
            form.Children.Add(hero);
        }

        void UpdateScrollChrome()
        {
            double amount = Math.Clamp((settingsScroll.VerticalOffset - 24) / 32, 0, 1);
            compactTitle.Opacity = amount;
            UpdateHeaderTransition();
            if (backToTop != null) backToTop.Visibility = settingsScroll.VerticalOffset > 32 ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateHeaderTransition()
        {
            double progress = Math.Clamp((settingsScroll?.VerticalOffset ?? 0) / 24, 0, 1);
            headerFadeLayer.Opacity = progress;
            var page = pageBackgroundBrush.Color;
            var opaque = footerBackgroundBrush.Color;
            headerBackgroundBrush.Color = Windows.UI.Color.FromArgb((byte)Math.Round(page.A + (opaque.A - page.A) * progress), page.R, page.G, page.B);
        }

        Button CreateHeaderIconButton(string glyph, string tooltip, Action onClick)
        {
            var btn = new Button
            {
                Width = 30, Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = glyph, FontSize = 16, FontFamily = new FontFamily("Segoe Fluent Icons"), Foreground = subInkBrush }
            };
            btn.Click += (_, __) => onClick();
            ToolTipService.SetToolTip(btn, tooltip);
            return btn;
        }

        void BuildWorkingHoursCard()
        {
            var card = CreateCardBorder();
            var panel = new StackPanel();
            var heading = new Grid { MinHeight = 24, Margin = new Thickness(0, 0, 0, 10) };
            heading.Children.Add(new TextBlock { Text = "工作时段", FontSize = 14, LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(heading);
            scheduleLabel = new TextBlock { FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(scheduleLabel);

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var startCol = new StackPanel { Spacing = 4 };
            startCol.Children.Add(new TextBlock { Text = "上班", FontSize = 12, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = subInkBrush });
            startTimeDisplay = new TextBlock { Text = FormatTime(startTime), FontFamily = new FontFamily("Segoe UI"), FontSize = 17, IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Center };
            var startBtn = CreateTimeButton(startTimeDisplay, true);
            startCol.Children.Add(startBtn);
            Grid.SetColumn(startCol, 0); grid.Children.Add(startCol);

            var endCol = new StackPanel { Spacing = 4 };
            endCol.Children.Add(new TextBlock { Text = "下班", FontSize = 12, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = subInkBrush });
            endTimeDisplay = new TextBlock { Text = FormatTime(endTime), FontFamily = new FontFamily("Segoe UI"), FontSize = 17, IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Center };
            var endBtn = CreateTimeButton(endTimeDisplay, false);
            endCol.Children.Add(endBtn);
            Grid.SetColumn(endCol, 1); grid.Children.Add(endCol);

            panel.Children.Add(grid);
            dayTimeSection = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            var dayHeader = new Grid { ColumnSpacing = 8 };
            dayHeader.ColumnDefinitions.Add(new ColumnDefinition()); dayHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); dayHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dayHeader.Children.Add(new TextBlock { Text = "按星期设置时段", FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            dayTimeSummary = new TextBlock { FontSize = 12, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dayTimeSummary, 1); dayHeader.Children.Add(dayTimeSummary);
            dayTimeChevron = CreateChevron(); Grid.SetColumn(dayTimeChevron, 2); dayHeader.Children.Add(dayTimeChevron);
            var dayHeaderButton = new Button { Content = dayHeader, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            dayHeaderButton.Resources["ButtonBackgroundPointerOver"] = dayHeaderButton.Background;
            dayHeaderButton.Resources["ButtonBackgroundPressed"] = dayHeaderButton.Background;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dayHeaderButton, "按星期设置时段，展开或收起");
            dayHeaderButton.Click += (_, __) => { dayTimeExpanded = !dayTimeExpanded; RefreshDayTimeEditor(); };
            dayTimeSection.Children.Add(dayHeaderButton);
            dayTimeEditor = new StackPanel { Spacing = 8 };
            var dayRow = new Grid { ColumnSpacing = 8 };
            dayRow.ColumnDefinitions.Add(new ColumnDefinition()); dayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dayTimeDay = new ComboBox { Height = 34, MinWidth = 112, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
            foreach (string name in new[] { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" })
                dayTimeDay.Items.Add(new ComboBoxItem { Content = name });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dayTimeDay, "选择需要设置时段的星期");
            dayTimeDay.SelectionChanged += (_, __) => { if (loadingSchedule || dayTimeDay.SelectedIndex < 0) return; RememberDayTime(); editingDay = (dayTimeDay.SelectedIndex + 1) % 7; LoadDayTime(); RefreshDayTimeEditor(); };
            dayRow.Children.Add(dayTimeDay);
            dayTimeSwitch = CreateGreenSwitch(dayTimeEnabled);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dayTimeSwitch, "所选星期使用独立时段");
            dayTimeSwitch.Toggled += (_, __) => { if (loadingSchedule) return; dayTimeEnabled = dayTimeSwitch.IsOn; RememberDayTime(); RefreshDayTimeEditor(); };
            Grid.SetColumn(dayTimeSwitch, 1); dayRow.Children.Add(dayTimeSwitch); dayTimeEditor.Children.Add(dayRow);
            dayTimeTimes = new Grid { ColumnSpacing = 12 };
            dayTimeTimes.ColumnDefinitions.Add(new ColumnDefinition()); dayTimeTimes.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 2; i++)
            {
                bool first = i == 0;
                var column = new StackPanel { Spacing = 4 };
                var timeLabel = new TextBlock { FontSize = 12, Foreground = subInkBrush };
                if (first) dayTimeStartLabel = timeLabel; else dayTimeEndLabel = timeLabel;
                column.Children.Add(timeLabel);
                var display = new TextBlock { FontSize = 17, FontFamily = new FontFamily("Segoe UI"), Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (first) dayTimeStartDisplay = display; else dayTimeEndDisplay = display;
                var button = new Button { Height = 36, CornerRadius = new CornerRadius(8), Background = inputBackgroundBrush, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Content = display };
                button.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush; button.Resources["ButtonBackgroundPressed"] = timePressedBrush;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, first ? "所选星期上班时间" : "所选星期下班时间");
                button.Click += (_, __) => ShowTimeWheelCore(button, display, false, false, null, 0,
                    value => { if (first) dayTimeStart = value; else dayTimeEnd = value; RememberDayTime(); }, first ? dayTimeStart : dayTimeEnd);
                column.Children.Add(button); Grid.SetColumn(column, i); dayTimeTimes.Children.Add(column);
            }
            dayTimeEditor.Children.Add(dayTimeTimes);
            dayTimeEditor.Children.Add(new TextBlock { Text = "固定休息日不可设置；关闭开关沿用常规时间。时段仅在当天上班时生效。", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });
            dayTimeSection.Children.Add(dayTimeEditor);
            dayTimeReveal = new Border { Child = dayTimeSection, Visibility = Visibility.Collapsed, Opacity = 0 };
            panel.Children.Add(dayTimeReveal);
            RefreshScheduleEditor();
            card.Child = panel;
            form.Children.Add(card);
        }

        void RememberSchedule()
        {
            if (scheduleMode.Length == 0) return;
            RememberDayTime();
            var profile = workSchedules.FirstOrDefault(p => p.Mode == scheduleMode);
            if (profile == null) { profile = new OffWorkWidget.WorkScheduleProfile { Mode = scheduleMode }; workSchedules.Add(profile); }
            profile.Start = startTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            profile.End = endTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            profile.SaturdayEnabled = false;
            profile.DayTimes = dayTimes.Select(d => new OffWorkWidget.DayWorkTime { Day = d.Day, Enabled = d.Enabled, Start = d.Start, End = d.End }).ToList();
        }

        void RememberDayTime()
        {
            var day = dayTimes.FirstOrDefault(d => d.Day == editingDay);
            if (day == null)
            {
                if (!dayTimeEnabled) return;
                day = new OffWorkWidget.DayWorkTime { Day = editingDay }; dayTimes.Add(day);
            }
            day.Enabled = dayTimeEnabled;
            day.Start = dayTimeStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            day.End = dayTimeEnd.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        void LoadDayTime()
        {
            var day = dayTimes.FirstOrDefault(d => d.Day == editingDay);
            dayTimeEnabled = day?.Enabled == true;
            dayTimeStart = TimeSpan.TryParseExact(day?.Start, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan savedStart) ? savedStart : startTime;
            dayTimeEnd = TimeSpan.TryParseExact(day?.End, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan savedEnd) ? savedEnd : endTime;
        }

        bool CanSetDayTime(int day)
        {
            int restMask = scheduleMode == "alternating" ? 1
                : weekdays.Count == 7 ? weekdays.Select((d, i) => d.IsChecked == true ? 1 << i : 0).Sum() : current.RestDays;
            return (restMask & (1 << day)) == 0;
        }

        void RefreshDayTimeEditor()
        {
            if (dayTimeSection == null || dayTimeTimes == null) return;
            if (scheduleMode == "double") { UpdateDayTimeReveal(false); return; }
            var available = Enumerable.Range(0, 7).Where(CanSetDayTime).ToList();
            if (scheduleMode != "double" && available.Count > 0 && !CanSetDayTime(editingDay))
            { RememberDayTime(); editingDay = available.OrderBy(d => (d + 6) % 7).First(); LoadDayTime(); }
            bool selectable = available.Count > 0 && CanSetDayTime(editingDay);
            dayTimeTimes.Visibility = selectable && dayTimeEnabled ? Visibility.Visible : Visibility.Collapsed;
            int count = dayTimes.Count(d => d.Enabled && CanSetDayTime(d.Day));
            dayTimeSummary.Text = available.Count == 0 ? "无每周工作日" : count == 0 ? "按需设置" : count + " 天已设置";
            loadingSchedule = true;
            try
            {
                for (int i = 0; i < 7; i++)
                {
                    var item = (ComboBoxItem)dayTimeDay.Items[i];
                    item.IsEnabled = CanSetDayTime((i + 1) % 7);
                    ToolTipService.SetToolTip(item, item.IsEnabled ? null : "每周休息日，不能设置工作时段");
                }
                dayTimeDay.IsEnabled = available.Count > 0;
                dayTimeDay.SelectedIndex = selectable ? (editingDay + 6) % 7 : -1;
                dayTimeSwitch.IsEnabled = selectable; dayTimeSwitch.IsOn = selectable && dayTimeEnabled;
            }
            finally { loadingSchedule = false; }
            string name = new[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" }[editingDay];
            dayTimeStartLabel.Text = name + "上班"; dayTimeEndLabel.Text = name + "下班";
            dayTimeStartDisplay.Text = FormatTime(dayTimeStart); dayTimeEndDisplay.Text = FormatTime(dayTimeEnd);
            UpdateDayTimeReveal(true);
        }

        void UpdateDayTimeReveal(bool visible)
        {
            if (dayTimeReveal == null) return;
            double fullHeight = double.IsNaN(dayTimeSection.Height) ? dayTimeSection.ActualHeight : dayTimeSection.Height;
            if (visible)
            {
                if (dayTimeEditor.Visibility == Visibility.Collapsed) dayTimeEditor.Opacity = 0;
                dayTimeEditor.Visibility = Visibility.Visible;
                dayTimeSection.Height = double.NaN;
                dayTimeSection.Measure(new Windows.Foundation.Size(Math.Max(1, form.ActualWidth - 32), double.PositiveInfinity));
                fullHeight = dayTimeSection.DesiredSize.Height;
            }
            double headerHeight = ((FrameworkElement)dayTimeSection.Children[0]).DesiredSize.Height + dayTimeSection.Margin.Top + dayTimeSection.Margin.Bottom;
            double height = visible ? dayTimeExpanded ? fullHeight : headerHeight : 0, opacity = visible ? 1 : 0;
            double editorOpacity = visible && dayTimeExpanded ? 1 : 0;
            dayTimeEditor.IsHitTestVisible = editorOpacity > 0;
            double angle = dayTimeExpanded ? 90 : 0;
            if (animatingDayTimeReveal && Math.Abs(height - dayTimeRevealTargetHeight) < .5 && opacity == dayTimeRevealTargetOpacity && angle == dayTimeArrowTarget && editorOpacity == dayTimeEditorTargetOpacity)
            { dayTimeSection.Height = fullHeight; return; }
            double currentHeight = animatingDayTimeReveal ? dayTimeRevealHeight : dayTimeReveal.Visibility == Visibility.Collapsed ? 0 : dayTimeReveal.ActualHeight;
            dayTimeRevealStartHeight = currentHeight; dayTimeRevealTargetHeight = height;
            dayTimeRevealStartOpacity = dayTimeReveal.Opacity; dayTimeRevealTargetOpacity = opacity;
            dayTimeEditorStartOpacity = dayTimeEditor.Opacity; dayTimeEditorTargetOpacity = editorOpacity;
            dayTimeArrowStart = (dayTimeChevron.RenderTransform as RotateTransform)?.Angle ?? 0; dayTimeArrowTarget = angle;
            dayTimeReveal.IsHitTestVisible = visible;
            if (!root.IsLoaded || !animationSettings.AnimationsEnabled || (Math.Abs(height - currentHeight) < .5 && Math.Abs(opacity - dayTimeReveal.Opacity) < .01 && Math.Abs(editorOpacity - dayTimeEditor.Opacity) < .01))
            { FinishDayTimeReveal(); return; }
            dayTimeSection.Height = fullHeight;
            dayTimeReveal.Visibility = Visibility.Visible;
            dayTimeRevealClock.Restart();
            if (!animatingDayTimeReveal) { animatingDayTimeReveal = true; CompositionTarget.Rendering += AnimateDayTimeReveal; }
            AnimateDayTimeReveal(null, null!);
        }

        void AnimateDayTimeReveal(object? sender, object args)
        {
            double position = animationSettings.AnimationsEnabled ? Math.Clamp(dayTimeRevealClock.Elapsed.TotalMilliseconds / 260, 0, 1) : 1;
            double eased = position * position * (3 - 2 * position);
            dayTimeRevealHeight = dayTimeRevealStartHeight + (dayTimeRevealTargetHeight - dayTimeRevealStartHeight) * eased;
            double opacity = dayTimeRevealStartOpacity + (dayTimeRevealTargetOpacity - dayTimeRevealStartOpacity) * eased;
            dayTimeReveal.Height = dayTimeRevealHeight; dayTimeReveal.Opacity = opacity;
            dayTimeEditor.Opacity = dayTimeEditorStartOpacity + (dayTimeEditorTargetOpacity - dayTimeEditorStartOpacity) * eased;
            dayTimeReveal.Margin = new Thickness(0, 10 * opacity, 0, 0);
            dayTimeRevealClip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, form.ActualWidth - 32), Math.Max(0, dayTimeRevealHeight));
            dayTimeReveal.Clip = dayTimeRevealClip;
            dayTimeChevron.RenderTransform = new RotateTransform { Angle = dayTimeArrowStart + (dayTimeArrowTarget - dayTimeArrowStart) * eased };
            if (position >= 1) FinishDayTimeReveal();
        }

        void FinishDayTimeReveal()
        {
            CompositionTarget.Rendering -= AnimateDayTimeReveal; animatingDayTimeReveal = false; dayTimeRevealClock.Stop();
            dayTimeReveal.Visibility = dayTimeRevealTargetOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
            dayTimeReveal.Opacity = dayTimeRevealTargetOpacity; dayTimeReveal.Height = double.NaN;
            dayTimeReveal.Margin = new Thickness(0, 10 * dayTimeRevealTargetOpacity, 0, 0); dayTimeReveal.Clip = null; dayTimeSection.Height = double.NaN;
            dayTimeEditor.Opacity = dayTimeEditorTargetOpacity;
            dayTimeEditor.Visibility = dayTimeEditorTargetOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
            dayTimeChevron.RenderTransform = new RotateTransform { Angle = dayTimeArrowTarget };
        }

        void LoadSchedule(string mode)
        {
            scheduleMode = mode;
            var profile = workSchedules.FirstOrDefault(p => p.Mode == mode);
            dayTimes.Clear();
            if (profile != null)
            {
                startTime = OffWorkWidget.Logic.Time(profile.Start); endTime = OffWorkWidget.Logic.Time(profile.End);
                for (int day = 0; day < 7; day++)
                {
                    var value = OffWorkWidget.Logic.DayTime(profile, day);
                    if (value != null) dayTimes.Add(new OffWorkWidget.DayWorkTime { Day = value.Day, Enabled = value.Enabled, Start = value.Start, End = value.End });
                }
            }
            dayTimeExpanded = mode != "double";
            LoadDayTime();
            RefreshScheduleEditor();
        }

        void SwitchScheduleMode(string mode)
        {
            if (scheduleMode == mode) return;
            RememberSchedule(); LoadSchedule(mode);
        }

        void RefreshScheduleEditor()
        {
            if (scheduleLabel == null || dayTimeTimes == null) return;
            string name = scheduleMode == "double" ? "双休" : scheduleMode == "single" ? "单休" : scheduleMode == "alternating" ? "大小周" : "自定义";
            scheduleLabel.Text = name + " · 常规工作时段";
            startTimeDisplay.Text = FormatTime(startTime); endTimeDisplay.Text = FormatTime(endTime);
            RefreshDayTimeEditor();
        }

        Button CreateTimeButton(TextBlock label, bool isStart)
        {
            var btn = new Button
            {
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = label
            };

            btn.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            btn.Resources["ButtonBackgroundPressed"] = timePressedBrush;

            btn.Click += (_, __) =>
            {
                ShowTimeWheel(btn, label, isStart);
            };
            return btn;
        }

        static string FormatAmountDuration(int seconds) => $"{seconds / 60:00} : {seconds % 60:00}";

        void ShowTimeWheel(Button anchor, TextBlock label, bool isStart, bool isDuration = false)
            => ShowTimeWheelCore(anchor, label, isStart, isDuration, null, 0);

        void ShowTimeWheelCore(Button anchor, TextBlock label, bool isStart, bool isDuration, Action<int>? applyMinutes, int initialMinutes, Action<TimeSpan>? applyTime = null, TimeSpan? initialTime = null)
        {
            var initial = initialTime ?? (applyMinutes != null ? TimeSpan.FromMinutes(initialMinutes) : isStart ? startTime : endTime);
            int hour = isDuration ? amountDurationSeconds / 60 : initial.Hours;
            int minute = isDuration ? amountDurationSeconds % 60 : initial.Minutes;
            bool dark = root.ActualTheme == ElementTheme.Dark;
            var background = dark ? Windows.UI.Color.FromArgb(255, 44, 44, 46) : Microsoft.UI.Colors.White;
            var foreground = dark ? Windows.UI.Color.FromArgb(255, 238, 238, 242) : Windows.UI.Color.FromArgb(255, 28, 28, 30);
            bool compact = applyMinutes != null || isDuration;
            double wheelRowHeight = compact ? 32 : 36;
            double wheelHeight = compact ? 96 : 180;
            var wheelHint = new TextBlock { Text = isDuration ? "显示时长 · 分:秒" : compact ? "提醒间隔 · 时:分" : "滑动选择小时和分钟", FontSize = 13, Foreground = subInkBrush, Margin = new Thickness(0, 0, 0, 8) };
            Button? confirm = null;
            void UpdateReminderSelection()
            {
                if (applyMinutes == null || confirm == null) return;
                int value = hour * 60 + minute;
                confirm.IsEnabled = value >= 1 && value <= 720;
                wheelHint.Text = confirm.IsEnabled ? "提醒间隔 · 时:分" : "请选择1分钟至12小时";
            }
            var flyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
            var panel = new StackPanel { Width = compact ? 184 : 248, RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light };
            var wheels = new Grid { Height = wheelHeight, Background = new SolidColorBrush(background) };
            wheels.ColumnDefinitions.Add(new ColumnDefinition());
            wheels.ColumnDefinitions.Add(new ColumnDefinition());
            var selection = new Border
            {
                Height = wheelRowHeight, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(compact ? 12 : 18),
                Background = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 58, 58, 60) : Windows.UI.Color.FromArgb(255, 239, 240, 244)),
                IsHitTestVisible = false
            };
            Grid.SetColumnSpan(selection, 2); wheels.Children.Add(selection);
            var hours = CreateTimeWheel(isDuration ? 1441 : applyMinutes != null ? 13 : 24, hour, foreground, value => { hour = value; UpdateReminderSelection(); }, isDuration ? "分钟" : "小时", !isDuration && applyMinutes == null, wheelRowHeight, compact ? 3 : 5);
            var minutes = CreateTimeWheel(60, minute, foreground, value => { minute = value; UpdateReminderSelection(); }, isDuration ? "秒" : "分钟", true, wheelRowHeight, compact ? 3 : 5);
            wheels.Children.Add(hours); Grid.SetColumn(minutes, 1); wheels.Children.Add(minutes);
            // Fade the unselected rows into the same theme surface, without a border
            // or a separate blue rectangle for each column.
            var fade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
            var transparent = Windows.UI.Color.FromArgb(0, background.R, background.G, background.B);
            fade.GradientStops.Add(new GradientStop { Color = background, Offset = 0 });
            fade.GradientStops.Add(new GradientStop { Color = transparent, Offset = .4 });
            fade.GradientStops.Add(new GradientStop { Color = transparent, Offset = .6 });
            fade.GradientStops.Add(new GradientStop { Color = background, Offset = 1 });
            var fadingEdges = new Border { Background = fade, IsHitTestVisible = false };
            Grid.SetColumnSpan(fadingEdges, 2); wheels.Children.Add(fadingEdges);
            var colon = new TextBlock { Text = ":", FontSize = compact ? 18 : 20, Foreground = new SolidColorBrush(foreground), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            Grid.SetColumnSpan(colon, 2); wheels.Children.Add(colon);
            panel.Children.Add(wheelHint);
            panel.Children.Add(wheels);
            var actions = new Grid { Margin = new Thickness(0, 8, 0, 0), ColumnSpacing = 10 };
            actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
            var cancel = new Button { Content = "取消", MinHeight = 32, Padding = new Thickness(8, 4, 8, 4), FontSize = compact ? 13 : 14, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(8), Background = footerActionBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            cancel.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            cancel.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            cancel.Resources["ButtonForegroundPointerOver"] = inkBrush;
            cancel.Resources["ButtonForegroundPressed"] = inkBrush;
            confirm = new Button { Content = "确定", MinHeight = 32, Padding = new Thickness(8, 4, 8, 4), FontSize = compact ? 13 : 14, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(8), Background = accentBlue, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            cancel.Click += (_, __) => flyout.Hide();
            confirm.Click += (_, __) =>
            {
                if (applyMinutes != null)
                {
                    int value = hour * 60 + minute;
                    if (value < 1 || value > 720) return;
                    applyMinutes(value);
                    label.Text = FormatReminderInterval(value);
                }
                else if (isDuration)
                {
                    if (!anchor.IsEnabled) return;
                    amountDurationSeconds = Math.Min(86400, hour * 60 + minute);
                    label.Text = FormatAmountDuration(amountDurationSeconds);
                }
                else
                {
                    var selected = new TimeSpan(hour, minute, 0);
                    if (applyTime != null) applyTime(selected);
                    else if (isStart) startTime = selected; else endTime = selected;
                    label.Text = FormatTime(selected);
                }
                flyout.Hide();
            };
            UpdateReminderSelection();
            actions.Children.Add(confirm); Grid.SetColumn(cancel, 1); actions.Children.Add(cancel);
            panel.Children.Add(actions);
            flyout.Content = panel;
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(compact ? 8 : 10)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(background)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(compact ? 14 : 18)));
            style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, panel.RequestedTheme));
            flyout.FlyoutPresenterStyle = style;
            if (isDuration)
            {
                amountDurationFlyout = flyout;
                flyout.Closed += (_, __) => { if (amountDurationFlyout == flyout) amountDurationFlyout = null; };
            }
            flyout.ShowAt(anchor);
        }

        ScrollViewer CreateTimeWheel(int count, int value, Windows.UI.Color foreground, Action<int> changed, string name, bool wrap = true, double rowHeight = 36, int visibleRows = 5)
        {
            double viewportHeight = rowHeight * visibleRows;
            double padding = (viewportHeight - rowHeight) / 2;
            int repeats = wrap ? 7 : 1;
            var rows = new StackPanel();
            rows.Children.Add(new Border { Height = padding });
            var labels = new List<TextBlock>();
            var viewer = new ScrollViewer
            {
                Height = viewportHeight, Content = rows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
                VerticalSnapPointsType = Microsoft.UI.Xaml.Controls.SnapPointsType.None,
                VerticalSnapPointsAlignment = Microsoft.UI.Xaml.Controls.Primitives.SnapPointsAlignment.Center,
                IsTabStop = true
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(viewer, name);
            for (int index = 0; index < count * repeats; index++)
            {
                int selected = index;
                var text = new TextBlock
                {
                    Text = (index % count).ToString("00", CultureInfo.InvariantCulture),
                    FontSize = visibleRows == 3 ? 20 : 23, Foreground = new SolidColorBrush(foreground),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI Variable Display")
                };
                text.Opacity = .2;
                labels.Add(text);
                var row = new Border { Height = rowHeight, Child = text, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                row.Tapped += (_, __) => viewer.ChangeView(null, selected * rowHeight, null, false);
                rows.Children.Add(row);
            }
            rows.Children.Add(new Border { Height = padding });
            bool initialized = false;
            int previousFirst = 0, previousLast = -1;
            void UpdateSelection()
            {
                double position = viewer.VerticalOffset / rowHeight;
                int index = Math.Clamp((int)Math.Round(position), 0, count * repeats - 1);
                changed(index % count);
                // Rows more than three steps away all have the same opacity.
                // Reset the previous neighborhood, then update only the new one.
                for (int i = previousFirst; i <= previousLast; i++) labels[i].Opacity = .2;
                int first = Math.Max(0, (int)Math.Floor(position) - 3);
                int last = Math.Min(labels.Count - 1, (int)Math.Ceiling(position) + 3);
                for (int i = first; i <= last; i++)
                    labels[i].Opacity = Math.Max(.2, 1 - Math.Abs(i - position) * .3);
                previousFirst = first; previousLast = last;
            }
            EventHandler<object>? initialize = null;
            initialize = (_, __) =>
            {
                // Loaded can precede extent measurement; wait for a valid range.
                if (viewer.ExtentHeight <= viewportHeight) return;
                initialized = true;
                viewer.LayoutUpdated -= initialize;
                viewer.ChangeView(null, ((wrap ? count * 3 : 0) + value) * rowHeight, null, true);
                UpdateSelection();
            };
            viewer.LayoutUpdated += initialize;
            viewer.ViewChanged += (_, args) =>
            {
                if (!initialized) return;
                UpdateSelection();
                // Recenter silently after settling so 23↔00 and 59↔00 wrap smoothly.
                if (!args.IsIntermediate)
                {
                    int index = (int)Math.Round(viewer.VerticalOffset / rowHeight);
                    if (Math.Abs(viewer.VerticalOffset - index * rowHeight) > .1)
                    {
                        viewer.ChangeView(null, index * rowHeight, null, false);
                        return;
                    }
                    if (wrap && (index < count || index >= count * (repeats - 1)))
                        viewer.ChangeView(null, (count * 3 + index % count) * rowHeight, null, true);
                }
            };
            viewer.KeyDown += (_, args) =>
            {
                int step = args.Key == Windows.System.VirtualKey.Up ? -1 : args.Key == Windows.System.VirtualKey.Down ? 1 : 0;
                if (step == 0) return;
                viewer.ChangeView(null, viewer.VerticalOffset + step * rowHeight, null, false);
                args.Handled = true;
            };
            return viewer;
        }

        static string FormatTime(TimeSpan t) => $"{t.Hours:00} : {t.Minutes:00}";

        void BuildAccordionGroup()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            incomeContent = new Border { Child = new Border { Child = BuildIncomeContent(), Padding = new Thickness(16, 8, 16, 18) }, Visibility = Visibility.Collapsed };
            restContent = new Border { Child = new Border { Child = BuildRestContent(), Padding = new Thickness(16, 8, 16, 18) }, Visibility = Visibility.Collapsed };
            careContent = new Border { Child = new Border { Child = BuildCareContent(), Padding = new Thickness(16, 8, 16, 18) }, Visibility = Visibility.Collapsed };
            var headers = new[] {
                CreateAccordionHeader("薪水与小秘密", out incomeChevron, () => ToggleAccordion(1)),
                CreateAccordionHeader("把休息安排上", out restChevron, () => ToggleAccordion(2)),
                CreateAccordionHeader("喝口水，动一动", out careChevron, () => ToggleAccordion(3)) };
            var contents = new[] { incomeContent, restContent, careContent };
            for (int i = 0; i < 3; i++)
            {
                var body = new StackPanel(); body.Children.Add(headers[i]); body.Children.Add(contents[i]);
                if (i < 2) { accordionDividers[i] = CreateDivider(); accordionDividers[i].Margin = new Thickness(16, 0, 16, 0); body.Children.Add(accordionDividers[i]); }
                accordionCards[i] = CreateCardBorder(new Thickness(0)); accordionCards[i].Child = body;
                stack.Children.Add(accordionCards[i]);
            }
            UpdateAccordionChrome();
            form.Children.Add(stack);
        }

        Grid CreateChevron(double angle = 0)
        {
            var icon = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Windows.Foundation.Point(.5, .5), RenderTransform = new RotateTransform { Angle = angle }, IsHitTestVisible = false };
            icon.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M4,2 L8,6 L4,10"),
                Stroke = subInkBrush, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            });
            return icon;
        }

        UIElement CreateAccordionHeader(string titleText, out Grid chevron, Action onToggle)
        {
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(4),
                CornerRadius = new CornerRadius(12)
            };
            var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            btn.Resources["ButtonBackgroundPointerOver"] = transparent;
            btn.Resources["ButtonBackgroundPressed"] = transparent;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock { Text = titleText, FontSize = 14, LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            chevron = CreateChevron();
            var chevronSurface = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = transparent, Child = chevron };
            btn.PointerEntered += (_, __) => chevronSurface.Background = timeHoverBrush;
            btn.PointerExited += (_, __) => chevronSurface.Background = transparent;

            Grid.SetColumn(title, 0); grid.Children.Add(title);
            Grid.SetColumn(chevronSurface, 1); grid.Children.Add(chevronSurface);
            btn.Content = grid;
            btn.Click += (_, __) => onToggle();
            return btn;
        }

        void EnsureAccordionExpanded(int index)
        {
            if (expandedAccordion != index) ToggleAccordion(index);
        }

        void ToggleAccordion(int index)
        {
            expandedAccordion = expandedAccordion == index ? 0 : index;
            for (int i = 0; i < 3; i++)
            {
                accordionStart[i] = accordionProgress[i];
                accordionTarget[i] = expandedAccordion == i + 1 ? 1 : 0;
            }
            accordionClock.Restart();
            if (!animatingAccordion) { animatingAccordion = true; CompositionTarget.Rendering += AnimateAccordion; }
            AnimateAccordion(null, null!);
        }

        void UpdateAccordionChrome()
        {
            for (int i = 0; i < 3; i++)
            {
                double top = i == 0 ? 1 : Math.Max(accordionProgress[i - 1], accordionProgress[i]);
                double bottom = i == 2 ? 1 : Math.Max(accordionProgress[i], accordionProgress[i + 1]);
                accordionCards[i].CornerRadius = new CornerRadius(18 * top, 18 * top, 18 * bottom, 18 * bottom);
                accordionCards[i].Margin = new Thickness(0, 0, 0, i < 2 ? 10 * bottom : 0);
                if (i < 2)
                {
                    accordionDividers[i].Opacity = 1 - bottom;
                    accordionDividers[i].Height = 1 - bottom;
                }
            }
        }

        void AnimateAccordion(object? sender, object args)
        {
            bool enabled = animationSettings.AnimationsEnabled;
            double position = enabled ? Math.Clamp(accordionClock.Elapsed.TotalMilliseconds / 260, 0, 1) : 1;
            double eased = position * position * (3 - 2 * position);
            var contents = new[] { incomeContent, restContent, careContent };
            var arrows = new[] { incomeChevron, restChevron, careChevron };
            for (int i = 0; i < 3; i++)
            {
                double progress = accordionStart[i] + (accordionTarget[i] - accordionStart[i]) * eased;
                accordionProgress[i] = progress;
                var content = contents[i]; var inner = (Border)content.Child;
                double width = Math.Max(1, form.ActualWidth - 32);
                ((FrameworkElement)inner.Child).Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                double height = ((FrameworkElement)inner.Child).DesiredSize.Height + inner.Padding.Top + inner.Padding.Bottom;
                content.Visibility = progress > 0 || accordionTarget[i] > 0 ? Visibility.Visible : Visibility.Collapsed;
                inner.Height = height;
                content.Height = height * progress;
                content.Opacity = progress;
                content.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, form.ActualWidth), height * progress) };
                arrows[i].RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
                arrows[i].RenderTransform = new RotateTransform { Angle = progress * 90 };
                if (position >= 1)
                {
                    content.Height = double.NaN; inner.Height = double.NaN; content.Clip = null;
                    content.Visibility = accordionTarget[i] > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            UpdateAccordionChrome();
            if (position >= 1) { CompositionTarget.Rendering -= AnimateAccordion; accordionClock.Stop(); animatingAccordion = false; }
        }

        void BuildDesktopLayoutCard()
        {
            var card = CreateCardBorder(new Thickness(16, 12, 16, 10));
            card.Child = CreateScaleRuler();
            form.Children.Add(card);
        }

        FrameworkElement CreateScaleRuler()
        {
            var header = new Grid();
            scaleLabel = new TextBlock { Text = "桌面布局", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            scaleReadout = new TextBlock { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontSize = 13, FontFamily = new FontFamily("Segoe UI"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush };
            header.Children.Add(scaleLabel);
            var band = new Grid { Height = 52 };
            scaleRuler = new Canvas { IsHitTestVisible = false };
            for (int i = 0; i <= 50; i++)
            {
                var tick = new Border { CornerRadius = new CornerRadius(1), Background = subInkBrush };
                scaleTicks.Add(tick); scaleRuler.Children.Add(tick);
            }
            scaleIndicator = new Border { Width = 2, Height = 24, CornerRadius = new CornerRadius(1), Background = inkBrush, IsHitTestVisible = false };
            scaleRuler.Children.Add(scaleIndicator);
            scaleReadoutBadge = new Border
            {
                Width = 54, Height = 22, CornerRadius = new CornerRadius(11),
                Background = inputBackgroundBrush, Child = scaleReadout, IsHitTestVisible = false
            };
            scaleRuler.Children.Add(scaleReadoutBadge);
            scale = new Slider
            {
                Minimum = .5, Maximum = 1.5, StepFrequency = .02, SmallChange = .02, LargeChange = .5,
                Value = Math.Clamp(current.Scale, .5, 1.5), Height = 34, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 0, 0), Padding = new Thickness(0),
                IsThumbToolTipEnabled = false, UseSystemFocusVisuals = false, Opacity = .001
            };
            // Keep the native keyboard/accessibility range. A separate pointer
            // surface delays edits until tap release or a locked horizontal drag.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(scale, "桌面布局大小");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(scale, "范围0.5至1.5倍，每格0.02倍；横向拖动或点击刻度调整，左右方向键微调；滚轮和纵向拖动滚动页面。");
            scale.ValueChanged += (_, __) =>
            {
                if (settingScaleFromPointer) return;
                scaleDragValue = null;
                UpdateScaleRuler();
                previewScale?.Invoke(scale.Value);
            };
            scaleInput = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                ManipulationMode = ManipulationModes.None
            };
            scaleInput.PointerPressed += OnScalePointerPressed;
            scaleInput.PointerMoved += OnScalePointerMoved;
            scaleInput.PointerReleased += OnScalePointerReleased;
            scaleInput.PointerCanceled += (_, __) => CancelScaleGesture();
            scaleInput.PointerCaptureLost += (_, __) => CancelScaleGesture();
            // No wheel handler here: wheel events bubble to the page ScrollViewer.
            band.Children.Add(scaleRuler); band.Children.Add(scale); band.Children.Add(scaleInput);
            var body = new StackPanel { Spacing = 6 }; body.Children.Add(header); body.Children.Add(band);
            var frame = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), CornerRadius = new CornerRadius(8), Padding = new Thickness(0), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Child = body };
            band.SizeChanged += (_, __) => UpdateScaleRuler();
            UpdateScaleRuler();
            return frame;
        }

        void OnScalePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (scalePointerId.HasValue || !e.GetCurrentPoint(scaleInput).Properties.IsLeftButtonPressed) return;
            scale.Focus(FocusState.Pointer);
            if (!scaleInput.CapturePointer(e.Pointer)) return;
            scalePointerId = e.Pointer.PointerId;
            scaleGestureStart = e.GetCurrentPoint(windowSurface).Position;
            scaleGestureStartX = e.GetCurrentPoint(scaleInput).Position.X;
            scaleGestureScrollStart = settingsScroll.VerticalOffset;
            scaleGestureDirection = ScaleGestureDirection.Pending;
            scaleGestureMoved = false;
            e.Handled = true;
        }

        void UpdateScaleGesture(Windows.Foundation.Point position)
        {
            double dx = position.X - scaleGestureStart.X, dy = position.Y - scaleGestureStart.Y;
            double ax = Math.Abs(dx), ay = Math.Abs(dy);
            if (Math.Max(ax, ay) >= 8) scaleGestureMoved = true;
            if (scaleGestureDirection == ScaleGestureDirection.Pending && scaleGestureMoved)
            {
                if (ax >= 8 && ax > ay * 1.25) scaleGestureDirection = ScaleGestureDirection.Horizontal;
                else if (ay >= 8 && ay > ax * 1.25) scaleGestureDirection = ScaleGestureDirection.Vertical;
            }
            if (scaleGestureDirection == ScaleGestureDirection.Horizontal)
                QueueScaleFromPosition(scaleGestureStartX + dx);
            else if (scaleGestureDirection == ScaleGestureDirection.Vertical)
                settingsScroll.ChangeView(null, scaleGestureScrollStart - dy, null, true);
        }

        void QueueScaleFromPosition(double x)
        {
            pendingScaleX = x;
            if (scalePositionQueued) return;
            scalePositionQueued = true;
            CompositionTarget.Rendering += ApplyPendingScalePosition;
        }

        void ApplyPendingScalePosition(object? sender, object args)
        {
            CompositionTarget.Rendering -= ApplyPendingScalePosition;
            scalePositionQueued = false;
            double? x = pendingScaleX;
            pendingScaleX = null;
            if (x.HasValue) SetScaleFromPosition(x.Value);
        }

        void CancelScaleGesture()
        {
            scalePointerId = null;
            pendingScaleX = null;
            scalePositionQueued = false;
            CompositionTarget.Rendering -= ApplyPendingScalePosition;
            FinishScaleDrag();
        }

        void FinishScaleDrag()
        {
            if (!scaleDragValue.HasValue) return;
            scaleDragValue = null;
            UpdateScaleRuler();
            previewScale?.Invoke(scale.Value);
        }

        void SetScaleFromPosition(double x)
        {
            double span = scaleInput.ActualWidth - 24;
            if (span <= 0) return;
            double value = scale.Minimum + Math.Clamp((x - 12) / span, 0, 1) * (scale.Maximum - scale.Minimum);
            // Render the latest pointer position continuously; only the stored draft snaps.
            scaleDragValue = value;
            settingScaleFromPointer = true;
            try
            {
                scale.Value = Math.Clamp(Math.Round(value / scale.StepFrequency, MidpointRounding.AwayFromZero) * scale.StepFrequency,
                    scale.Minimum, scale.Maximum);
            }
            finally { settingScaleFromPointer = false; }
            UpdateScaleRuler();
            previewScale?.Invoke(value);
        }

        void OnScalePointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (scalePointerId != e.Pointer.PointerId) return;
            UpdateScaleGesture(e.GetCurrentPoint(windowSurface).Position);
            e.Handled = true;
        }

        void OnScalePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (scalePointerId != e.Pointer.PointerId) return;
            UpdateScaleGesture(e.GetCurrentPoint(windowSurface).Position);
            ApplyPendingScalePosition(null, null!);
            if (!scaleGestureMoved) SetScaleFromPosition(e.GetCurrentPoint(scaleInput).Position.X);
            FinishScaleDrag();
            scalePointerId = null;
            scaleInput.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        void UpdateScaleRuler()
        {
            if (scale == null || scaleRuler == null) return;
            scaleReadout.Text = scale.Value.ToString("0.0#", CultureInfo.InvariantCulture) + "×";
            double width = scaleRuler.ActualWidth;
            if (width <= 24) return;
            double selected = ((scaleDragValue ?? scale.Value) - .5) / .02;
            for (int i = 0; i < scaleTicks.Count; i++)
            {
                bool major = i % 25 == 0, medium = i % 5 == 0;
                double distance = i - selected;
                double proximity = Math.Max(0, 1 - Math.Abs(distance) / 4);
                proximity = proximity * proximity * (3 - 2 * proximity);
                var tick = scaleTicks[i];
                tick.Visibility = Visibility.Visible;
                tick.Height = Math.Max(major ? 12 : medium ? 7 : 5, 5 + 15 * proximity);
                tick.Width = Math.Max(major ? 1.2 : .9, .9 + .35 * proximity);
                tick.Background = subInkBrush;
                tick.Opacity = Math.Max(major ? .5 : medium ? .34 : .22, .22 + .38 * proximity);
                Canvas.SetLeft(tick, 12 + (width - 24) * i / 50 - tick.Width / 2);
                Canvas.SetTop(tick, 24 - tick.Height);
            }
            double selectedX = 12 + (width - 24) * selected / 50;
            Canvas.SetLeft(scaleIndicator, selectedX - scaleIndicator.Width / 2);
            Canvas.SetTop(scaleIndicator, 0);
            Canvas.SetLeft(scaleReadoutBadge, Math.Clamp(selectedX - 27, 0, width - 54));
            Canvas.SetTop(scaleReadoutBadge, 30);
        }

        FrameworkElement BuildIncomeContent()
        {
            var panel = new StackPanel { Spacing = 10 };

            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var salaryHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            salaryHeader.Children.Add(new TextBlock { Text = "月薪（元）", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });

            var eyeBtn = new Button
            {
                Width = 24, Height = 20, Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var eyeGrid = new Grid { Width = 16, Height = 14 };
            var eyePath = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M1,7 C4,1 12,1 15,7 C12,13 4,13 1,7 Z M10,7 A2,2 0 1 1 6,7 A2,2 0 1 1 10,7"),
                Stroke = subInkBrush, StrokeThickness = 1.2
            };
            eyeSlash = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), "M2,1 L14,13"),
                Stroke = subInkBrush, StrokeThickness = 1.4,
                Visibility = salaryAuthorized ? Visibility.Collapsed : Visibility.Visible
            };
            eyeGrid.Children.Add(eyePath); eyeGrid.Children.Add(eyeSlash);
            eyeBtn.Content = eyeGrid;
            eyeBtn.Click += async (_, __) =>
            {
                if (salaryAuthorized)
                {
                    salaryDraft = salary.Text;
                    LockSalaryEditor(false);
                }
                else await RequestUnlockSalary();
            };
            salaryHeader.Children.Add(eyeBtn);
            grid.Children.Add(salaryHeader);

            salary = CreateInputBox("****", true);
            salary.GotFocus += async (_, __) => { if (!salaryAuthorized) await RequestUnlockSalary(); };
            salary.Width = 136;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(salary, "月薪，元");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(eyeBtn, "显示或隐藏月薪");
            Grid.SetColumn(salary, 1); grid.Children.Add(salary);

            var daysLabel = new TextBlock { Text = "计薪天数", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            workDays = CreateInputBox(current.WorkDays.ToString(CultureInfo.InvariantCulture));
            workDays.Width = 136;
            ToolTipService.SetToolTip(workDays, "每月计薪天数，支持小数");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(workDays, "每月计薪天数");
            Grid.SetRow(daysLabel, 1); grid.Children.Add(daysLabel);
            Grid.SetRow(workDays, 1); Grid.SetColumn(workDays, 1); grid.Children.Add(workDays);

            var payLabel = new TextBlock { Text = "每月发薪日", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            payDay = CreateInputBox(current.PayDay.ToString(CultureInfo.InvariantCulture));
            payDay.Width = 136; payDay.PlaceholderText = "1–31";
            ToolTipService.SetToolTip(payDay, "每月发薪日期，填写1–31");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(payDay, "每月发薪日，1至31");
            Grid.SetRow(payLabel, 2); grid.Children.Add(payLabel);
            Grid.SetRow(payDay, 2); Grid.SetColumn(payDay, 1); grid.Children.Add(payDay);

            panel.Children.Add(grid);

            panel.Children.Add(new Border { Height = 1, Background = dividerBrush, Margin = new Thickness(0, 6, 0, 4) });

            panel.Children.Add(new TextBlock { Text = "隐私保护", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = subInkBrush, Margin = new Thickness(0, 2, 0, 2) });

            var layoutRow = new Grid { ColumnSpacing = 12 };
            layoutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layoutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layoutRow.Children.Add(new TextBlock { Text = "显示薪资", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            salaryLayoutSwitch = CreateGreenSwitch(current.ShowSalaryLayout);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(salaryLayoutSwitch, "显示薪资布局");
            salaryLayoutSwitch.Toggled += (_, __) =>
            {
                if (salaryLayoutSwitch.IsOn != current.ShowSalaryLayout) ToggleSalaryLayout();
            };
            Grid.SetColumn(salaryLayoutSwitch, 1); layoutRow.Children.Add(salaryLayoutSwitch);
            panel.Children.Add(layoutRow);
            ToolTipService.SetToolTip(salaryLayoutSwitch, "控制桌面薪资列的显示；眼睛按钮只隐藏金额，查看时需验证密码。");

            var durationRow = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 2, 0, 0) };
            durationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            durationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            durationRow.Children.Add(new TextBlock { Text = "显示时长", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            amountDurationSeconds = Math.Clamp(current.AmountVisibleSeconds, 0, 86400);
            amountDurationDisplay = new TextBlock { Text = FormatAmountDuration(amountDurationSeconds), Foreground = inkBrush, FontSize = 16 };
            var durationValue = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            durationValue.Children.Add(amountDurationDisplay);
            durationValue.Children.Add(new TextBlock { Text = "分:秒", FontSize = 11, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center });
            amountDurationButton = new Button
            {
                Content = durationValue, Width = 136, Height = 36, CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(amountDurationButton, "金额显示时长，分钟和秒");
            amountDurationButton.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            amountDurationButton.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            amountDurationButton.Click += (_, __) => ShowTimeWheel(amountDurationButton, amountDurationDisplay, false, true);
            RefreshSalaryLayoutButton();
            Grid.SetColumn(amountDurationButton, 1); durationRow.Children.Add(amountDurationButton);
            panel.Children.Add(durationRow);
            panel.Children.Add(new TextBlock { Text = "00:00 常显；到时仅隐藏金额", FontSize = 12, Foreground = subInkBrush });

            var passwordRow = new Grid { Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 12 };
            passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            passwordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            passwordRow.Children.Add(new TextBlock { Text = "金额密码", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            passwordStatus = new TextBlock { Text = privacy.HasPassword ? "已设置" : "未设置", FontSize = 12, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(passwordStatus, 1); passwordRow.Children.Add(passwordStatus);
            panel.Children.Add(passwordRow);

            var pwdBtns = new Grid { ColumnSpacing = 10 };
            pwdBtns.ColumnDefinitions.Add(new ColumnDefinition());
            pwdBtns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var changePwd = new Button
            {
                Content = "管理密码", Width = 136, Height = 34, CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4)
            };
            changePwd.Click += async (_, __) =>
            {
                if (privacyPromptOpen) return;
                privacyPromptOpen = true;
                try { if (!await PrivacyPrompts.ChangeAsync(root.XamlRoot, privacy)) LockSalaryEditor(); }
                catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
                finally { ReconcilePrivacyAfterDialog(); privacyPromptOpen = false; }
            };
            pwdBtns.Children.Add(changePwd);

            var forgotPwd = new HyperlinkButton
            {
                Content = "忘记密码", FontSize = 12, Height = 34, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = subInkBrush, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(4, 0, 0, 0)
            };
            forgotPwd.Click += async (_, __) =>
            {
                if (privacyPromptOpen) return;
                privacyPromptOpen = true;
                try
                {
                    if (await PrivacyPrompts.ResetAsync(root.XamlRoot, privacy))
                    {
                        current.Salary = privacy.LoadSalary();
                        hideSalary();
                        LockSalaryEditor();
                    }
                }
                catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
                finally { ReconcilePrivacyAfterDialog(); privacyPromptOpen = false; }
            };
            Grid.SetColumn(forgotPwd, 1); pwdBtns.Children.Add(forgotPwd);
            panel.Children.Add(pwdBtns);

            return panel;
        }

        internal void RefreshSalaryLayoutButton()
        {
            if (salaryLayoutSwitch != null && salaryLayoutSwitch.IsOn != current.ShowSalaryLayout)
                salaryLayoutSwitch.IsOn = current.ShowSalaryLayout;
            if (amountDurationButton != null)
            {
                amountDurationButton.IsEnabled = !current.ShowSalaryLayout;
                amountDurationDisplay.Opacity = current.ShowSalaryLayout ? .45 : 1;
                if (current.ShowSalaryLayout) amountDurationFlyout?.Hide();
            }
        }

        void ToggleSalaryLayout()
        {
            salaryLayoutSwitch.IsEnabled = false;
            try
            {
                toggleSalaryLayout();
                if (!current.ShowSalaryLayout) LockSalaryEditor();
            }
            catch (Exception ex)
            {
                error.Text = "无法切换薪资布局：" + ex.Message; error.Visibility = Visibility.Visible;
            }
            finally
            {
                ReconcilePrivacyAfterDialog();
                RefreshSalaryLayoutButton();
                salaryLayoutSwitch.IsEnabled = true;
            }
        }

        async Task RequestUnlockSalary()
        {
            if (salaryAuthorized || privacyPromptOpen) return;
            privacyPromptOpen = true;
            try
            {
                bool unlocked = await PrivacyPrompts.UnlockAsync(root.XamlRoot, privacy);
                if (ReconcilePrivacyAfterDialog() && unlocked)
                {
                    salaryAuthorized = true;
                    salary.IsReadOnly = false;
                    salary.Text = salaryDraft ?? privacy.LoadSalary().ToString(CultureInfo.InvariantCulture);
                    eyeSlash.Visibility = Visibility.Collapsed;
                    salary.Focus(FocusState.Programmatic);
                }
                else if (!unlocked)
                {
                    LockSalaryEditor();
                }
            }
            catch (Exception ex)
            {
                error.Text = "无法读取薪资：" + ex.Message; error.Visibility = Visibility.Visible;
            }
            finally { ReconcilePrivacyAfterDialog(); privacyPromptOpen = false; }
        }

        FrameworkElement BuildRestContent()
        {
            var panel = new StackPanel { Spacing = 10 };

            var presets = new Grid { ColumnSpacing = 6 };
            restPresetButtons.Clear();
            string[] presetNames = { "双休", "单休", "大小周", "自定义" };
            for (int i = 0; i < presetNames.Length; i++)
            {
                int choice = i;
                presets.ColumnDefinitions.Add(new ColumnDefinition());
                var preset = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
                {
                    Content = presetNames[i], MinHeight = 32, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 4, 4, 4), MinWidth = 0, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = inputBackgroundBrush, Foreground = inkBrush
                };
                foreach (string state in new[] { "", "PointerOver", "Pressed" })
                {
                    preset.Resources["ToggleButtonBackgroundChecked" + state] = accentBlue;
                    preset.Resources["ToggleButtonForegroundChecked" + state] = new SolidColorBrush(Microsoft.UI.Colors.White);
                    preset.Resources["ToggleButtonBorderBrushChecked" + state] = accentBlue;
                }
                preset.Click += (_, __) =>
                {
                    if (choice == 3) { SwitchScheduleMode("custom"); dayTimeExpanded = true; alternatingWeekends = false; customRestPreset = true; UpdateRestPresets(); }
                    else if (choice == 2) SetWeek(AlternatingWeekMask(), true);
                    else SetWeek(choice == 0 ? doubleRestDays : singleRestDays);
                };
                restPresetButtons.Add(preset); Grid.SetColumn(preset, i); presets.Children.Add(preset);
            }
            panel.Children.Add(presets);
            panel.Children.Add(new TextBlock { Text = "上方工作时段随方案切换，分别保存", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });

            alternatingWeekRow = new Grid { ColumnSpacing = 8 };
            alternatingWeekRow.ColumnDefinitions.Add(new ColumnDefinition());
            alternatingWeekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            alternatingWeekRow.Children.Add(new TextBlock { Text = "本周安排", FontSize = 13, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center });
            var weekPhase = new ComboBox { MinWidth = 120, Height = 34, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(weekPhase, "大小周本周安排");
            weekPhase.Items.Add("本周双休"); weekPhase.Items.Add("本周单休");
            weekPhase.SelectedIndex = AlternatingWeekMask() == 65 ? 0 : 1;
            weekPhase.SelectionChanged += (_, __) =>
            {
                doubleRestWeekStart = OffWorkWidget.Logic.WeekStart(DateTime.Today).AddDays(weekPhase.SelectedIndex == 0 ? 0 : 7);
                if (alternatingWeekends) SetWeek(AlternatingWeekMask(), true);
            };
            Grid.SetColumn(weekPhase, 1); alternatingWeekRow.Children.Add(weekPhase);
            panel.Children.Add(alternatingWeekRow);

            var daysGrid = new Grid { ColumnSpacing = 6, Margin = new Thickness(0, 4, 0, 2) };
            for (int i = 0; i < 7; i++) daysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] names = { "日", "一", "二", "三", "四", "五", "六" };
            weekdays.Clear();
            var weekdayButtons = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton[7];
            for (int column = 0; column < 7; column++)
            {
                int i = (column + 1) % 7;
                string day = "周" + names[i];
                var cb = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
                {
                    Content = names[i],
                    IsChecked = ((alternatingWeekends ? AlternatingWeekMask() : current.RestDays) & (1 << i)) != 0,
                    FontFamily = new FontFamily("Microsoft YaHei"), FontSize = 13,
                    Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                foreach (string state in new[] { "", "PointerOver", "Pressed" })
                {
                    cb.Resources["ToggleButtonBackgroundChecked" + state] = accentBlue;
                    cb.Resources["ToggleButtonForegroundChecked" + state] = new SolidColorBrush(Microsoft.UI.Colors.White);
                    cb.Resources["ToggleButtonBorderBrushChecked" + state] = accentBlue;
                }
                void UpdateDay()
                {
                    bool selected = cb.IsChecked == true;
                    cb.Background = selected ? accentBlue : inputBackgroundBrush;
                    cb.Foreground = selected ? new SolidColorBrush(Microsoft.UI.Colors.White) : inkBrush;
                    cb.BorderBrush = selected ? accentBlue : dividerBrush;
                    string description = day + (selected ? "：休息日" : "：工作日");
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cb, description);
                    ToolTipService.SetToolTip(cb, description);
                }
                void ChangeDay()
                {
                    UpdateDay();
                    if (!settingRestPreset)
                    {
                        int mask = weekdays.Select((d, index) => d.IsChecked == true ? 1 << index : 0).Sum();
                        int count = OffWorkWidget.Logic.RestDayCount(mask);
                        if (count == 1) singleRestDays = mask;
                        if (count == 2) doubleRestDays = mask;
                        SwitchScheduleMode(customRestPreset ? "custom" : count == 2 ? "double" : count == 1 ? "single" : "custom");
                        alternatingWeekends = false; UpdateRestPresets();
                    }
                }
                cb.Checked += (_, __) => ChangeDay();
                cb.Unchecked += (_, __) => ChangeDay();
                UpdateDay();
                weekdayButtons[i] = cb;
                Grid.SetColumn(cb, column);
                daysGrid.Children.Add(cb);
            }
            weekdays.AddRange(weekdayButtons);
            panel.Children.Add(daysGrid);
            restWeekHint = new TextBlock { FontSize = 12, LineHeight = 19, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(restWeekHint);
            UpdateRestPresets();

            panel.Children.Add(new Border { Height = 1, Background = dividerBrush, Margin = new Thickness(0, 4, 0, 4) });

            var holidayRow = new Grid();
            holidayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            holidayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var holidayTitle = new TextBlock { Text = "中国节假日与调休", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            holidays = CreateGreenSwitch(current.UseChinaHolidays);
            Grid.SetColumn(holidayTitle, 0); holidayRow.Children.Add(holidayTitle);
            Grid.SetColumn(holidays, 1); holidayRow.Children.Add(holidays);
            panel.Children.Add(holidayRow);

            var holidayStatusRow = new Grid();
            holidayStatusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            holidayStatusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            holidayStatus = new TextBlock { Text = current.Calendar == null ? "节假日数据未加载" : current.Calendar.Status(DateTime.Now.Year), FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var updateBtn = new Button { Content = "更新", Height = 32, MinWidth = 64, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), Background = inputBackgroundBrush, Foreground = inkBrush, FontSize = 13 };
            updateBtn.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            updateBtn.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            updateBtn.Click += async (_, __) =>
            {
                if (current.Calendar == null) return;
                updateBtn.IsEnabled = false; holidayStatus.Text = "正在更新节假日…";
                try { await current.Calendar.RefreshAsync(DateTime.Now.Year, true); holidayStatus.Text = current.Calendar.Status(DateTime.Now.Year); }
                catch { holidayStatus.Text = "更新失败，继续使用本地安排。"; }
                finally { updateBtn.IsEnabled = true; }
            };
            Grid.SetColumn(holidayStatus, 0); holidayStatusRow.Children.Add(holidayStatus);
            Grid.SetColumn(updateBtn, 1); holidayStatusRow.Children.Add(updateBtn);
            panel.Children.Add(holidayStatusRow);

            panel.Children.Add(new Border { Height = 1, Background = dividerBrush, Margin = new Thickness(0, 4, 0, 4) });
            var adjustmentBody = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
            var adjustmentHeader = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
                Padding = new Thickness(0), MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            adjustmentHeader.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            adjustmentHeader.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var adjustmentTitle = new Grid { ColumnSpacing = 8 };
            adjustmentTitle.ColumnDefinitions.Add(new ColumnDefinition());
            adjustmentTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            adjustmentTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            adjustmentTitle.Children.Add(new TextBlock { Text = "手动调休", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            adjustmentSummary = new TextBlock { FontSize = 12, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(adjustmentSummary, 1); adjustmentTitle.Children.Add(adjustmentSummary);
            var adjustmentChevron = CreateChevron(); Grid.SetColumn(adjustmentChevron, 2); adjustmentTitle.Children.Add(adjustmentChevron);
            adjustmentHeader.Content = adjustmentTitle;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(adjustmentHeader, "手动调休，展开编辑");
            adjustmentHeader.Click += (_, __) =>
            {
                bool open = adjustmentBody.Visibility != Visibility.Visible;
                adjustmentBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                adjustmentChevron.RenderTransform = new RotateTransform { Angle = open ? 90 : 0 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(adjustmentHeader, open ? "手动调休，收起编辑" : "手动调休，展开编辑");
            };
            panel.Children.Add(adjustmentHeader);
            adjustmentBody.Children.Add(new TextBlock { Text = "手动安排优先于每周规则和节假日", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });

            var adjustGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
            adjustGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            adjustGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            adjustGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            adjustGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var from = new CalendarDatePicker { Date = DateTimeOffset.Now.Date, PlaceholderText = "开始日期", Height = 38, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            var to = new CalendarDatePicker { Date = DateTimeOffset.Now.Date, PlaceholderText = "结束日期", Height = 38, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var picker in new[] { from, to })
            {
                picker.MinWidth = 0;
                picker.Resources["CalendarDatePickerBackgroundPointerOver"] = timeHoverBrush;
                picker.Resources["CalendarDatePickerBackgroundPressed"] = timePressedBrush;
            }
            var fromColumn = new StackPanel { Spacing = 4 };
            fromColumn.Children.Add(new TextBlock { Text = "开始日期", FontSize = 12, Foreground = subInkBrush }); fromColumn.Children.Add(from);
            var toColumn = new StackPanel { Spacing = 4 };
            toColumn.Children.Add(new TextBlock { Text = "结束日期", FontSize = 12, Foreground = subInkBrush }); toColumn.Children.Add(to);
            Grid.SetColumn(fromColumn, 0); adjustGrid.Children.Add(fromColumn);
            Grid.SetColumn(toColumn, 1); adjustGrid.Children.Add(toColumn);

            var type = new ComboBox { Height = 38, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            type.Resources["ComboBoxBackgroundPointerOver"] = timeHoverBrush;
            type.Resources["ComboBoxBackgroundPressed"] = timePressedBrush;
            type.Items.Add("休息"); type.Items.Add("上班"); type.SelectedIndex = 0;
            Grid.SetRow(type, 1); Grid.SetColumn(type, 0); adjustGrid.Children.Add(type);

            var add = new Button { Content = "应用安排", Height = 38, FontSize = 13, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), Background = accentBlue, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Stretch };
            var adjErr = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed), FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            add.Click += (_, __) =>
            {
                adjErr.Visibility = Visibility.Collapsed;
                if (!from.Date.HasValue || !to.Date.HasValue) { adjErr.Text = "请选择开始和结束日期。"; adjErr.Visibility = Visibility.Visible; return; }
                DateTime a = from.Date.Value.Date, b = to.Date.Value.Date;
                if (b < a || (b - a).Days >= 366) { adjErr.Text = "结束日期不能早于开始日期，最多 366 天。"; adjErr.Visibility = Visibility.Visible; return; }
                for (DateTime d = a; d <= b; d = d.AddDays(1))
                {
                    string key = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    adjustments.RemoveAll(x => x.Date == key);
                    adjustments.Add(new OffWorkWidget.DateAdjustment { Date = key, IsWorkday = type.SelectedIndex == 1 });
                    if (d == DateTime.MaxValue.Date) break;
                }
                RenderAdjustments();
            };
            Grid.SetRow(add, 1); Grid.SetColumn(add, 1); adjustGrid.Children.Add(add);
            adjustmentBody.Children.Add(adjustGrid);
            adjustmentBody.Children.Add(adjErr);
            adjustmentBody.Children.Add(adjustmentRows);
            panel.Children.Add(adjustmentBody);
            RenderAdjustments();

            return panel;
        }

        int AlternatingWeekMask() => OffWorkWidget.Logic.RestDaysForWeek(DateTime.Today,
            new OffWorkWidget.Settings { AlternatingWeekends = true, DoubleRestWeekStart = doubleRestWeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });

        void SetWeek(int mask, bool alternating = false)
        {
            int count = OffWorkWidget.Logic.RestDayCount(mask);
            if (!alternating && count == 1) singleRestDays = mask;
            if (!alternating && count == 2) doubleRestDays = mask;
            SwitchScheduleMode(alternating ? "alternating" : count == 2 ? "double" : count == 1 ? "single" : "custom");
            dayTimeExpanded = scheduleMode != "double";
            alternatingWeekends = alternating;
            settingRestPreset = true;
            try { for (int i = 0; i < weekdays.Count; i++) weekdays[i].IsChecked = (mask & (1 << i)) != 0; }
            finally { settingRestPreset = false; }
            customRestPreset = false;
            UpdateRestPresets();
        }

        void UpdateRestPresets()
        {
            if (weekdays.Count != 7 || restPresetButtons.Count != 4) return;
            int mask = weekdays.Select((day, index) => day.IsChecked == true ? 1 << index : 0).Sum();
            int count = OffWorkWidget.Logic.RestDayCount(mask);
            int choice = alternatingWeekends ? 2 : customRestPreset ? 3 : count == 2 ? 0 : count == 1 ? 1 : 3;
            alternatingWeekRow.Visibility = alternatingWeekends ? Visibility.Visible : Visibility.Collapsed;
            restWeekHint.Text = alternatingWeekends ? "单双休隔周交替，蓝色为本周休息日" : "选中日期为休息日，可自由选择星期";
            for (int i = 0; i < restPresetButtons.Count; i++)
            {
                var preset = restPresetButtons[i]; bool selected = i == choice;
                preset.IsChecked = selected;
                preset.Background = selected ? accentBlue : inputBackgroundBrush;
                preset.Foreground = selected ? new SolidColorBrush(Microsoft.UI.Colors.White) : inkBrush;
            }
            RefreshDayTimeEditor();
        }

        void RenderAdjustments()
        {
            adjustmentRows.Children.Clear();
            adjustmentSummary.Text = adjustments.Count == 0 ? "按需设置" : $"{adjustments.Count} 天已调整";
            var ordered = adjustments.OrderBy(a => a.Date, StringComparer.Ordinal).ToList();
            for (int i = 0; i < ordered.Count;)
            {
                int j = i; DateTime first = DateTime.ParseExact(ordered[i].Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), last = first;
                while (j + 1 < ordered.Count && ordered[j + 1].IsWorkday == ordered[i].IsWorkday && DateTime.ParseExact(ordered[j + 1].Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) == last.AddDays(1)) { j++; last = last.AddDays(1); }
                string low = ordered[i].Date, high = ordered[j].Date;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var txt = new TextBlock { Text = low + (low == high ? "" : " 至 " + high) + (ordered[i].IsWorkday ? "  上班" : "  休息"), FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
                var del = new Button { Content = "恢复默认", Height = 26, Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(6), Background = inputBackgroundBrush, FontSize = 12 };
                del.Click += (_, __) => { adjustments.RemoveAll(a => string.CompareOrdinal(a.Date, low) >= 0 && string.CompareOrdinal(a.Date, high) <= 0); RenderAdjustments(); };
                Grid.SetColumn(txt, 0); row.Children.Add(txt);
                Grid.SetColumn(del, 1); row.Children.Add(del);
                adjustmentRows.Children.Add(row);
                i = j + 1;
            }
        }

        FrameworkElement BuildCareContent()
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
            water = CreateGreenSwitch(current.WaterReminder); walk = CreateGreenSwitch(current.WalkReminder);
            waterMinutes = Math.Clamp(current.WaterMinutes, 1, 720);
            walkMinutes = Math.Clamp(current.WalkMinutes, 1, 720);
            waterIntervalButton = CreateReminderIntervalButton(true);
            walkIntervalButton = CreateReminderIntervalButton(false);
            var switches = new[] { water, walk }; var inputs = new[] { waterIntervalButton, walkIntervalButton };
            var titles = new[] { "提醒喝水", "站起来走走" };
            for (int i = 0; i < 2; i++)
            {
                var column = new StackPanel { Spacing = 8 };
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = titles[i], FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(switches[i], 1); row.Children.Add(switches[i]); column.Children.Add(row);
                column.Children.Add(new TextBlock { Text = "提醒间隔", FontSize = 13, Foreground = subInkBrush }); column.Children.Add(inputs[i]);
                Grid.SetColumn(column, i); grid.Children.Add(column);
            }
            return grid;
        }

        static string FormatReminderInterval(int minutes)
            => minutes < 60 ? $"{minutes}分钟" : minutes % 60 == 0 ? $"{minutes / 60}小时" : $"{minutes / 60}小时{minutes % 60}分";

        Button CreateReminderIntervalButton(bool isWater)
        {
            var label = new TextBlock { Text = FormatReminderInterval(isWater ? waterMinutes : walkMinutes), FontSize = 14, Foreground = inkBrush };
            var button = new Button
            {
                Content = label, Height = 32, CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            button.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, (isWater ? "喝水" : "走动") + "提醒间隔，小时和分钟");
            ToolTipService.SetToolTip(button, "滑动选择提醒间隔，1分钟至12小时");
            button.Click += (_, __) => ShowTimeWheelCore(button, label, false, false,
                value => { if (isWater) waterMinutes = value; else walkMinutes = value; },
                isWater ? waterMinutes : walkMinutes);
            return button;
        }

        void BuildTopmostCard()
        {
            var card = CreateCardBorder();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock { Text = "始终置顶", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            topmost = CreateGreenSwitch(current.Topmost);
            Grid.SetColumn(title, 0); row.Children.Add(title);
            Grid.SetColumn(topmost, 1); row.Children.Add(topmost);

            card.Child = row;
            form.Children.Add(card);
        }

        void BuildFooter()
        {
            var footer = new Border
            {
                Background = footerBackgroundBrush,
                BorderBrush = dividerBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 10, 16, 12)
            };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(error);

            var actions = new Grid { ColumnSpacing = 10 };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var save = new Button
            {
                Content = "保存设置",
                FontSize = 14, Padding = new Thickness(12, 0, 12, 0),
                Height = 38, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0),
                Background = accentBlue, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            save.Click += Save;
            Grid.SetColumn(save, 0); actions.Children.Add(save);

            var cancel = new Button
            {
                Content = "取消", FontSize = 14, Padding = new Thickness(12, 0, 12, 0),
                Height = 38, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0),
                Background = footerActionBrush, Foreground = inkBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cancel.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            cancel.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            cancel.Resources["ButtonForegroundPointerOver"] = inkBrush;
            cancel.Resources["ButtonForegroundPressed"] = inkBrush;
            cancel.Click += (_, __) => Close();
            Grid.SetColumn(cancel, 1); actions.Children.Add(cancel);

            backToTop = CreateHeaderIconButton("\uE74A", "回到顶部", () => settingsScroll.ChangeView(null, 0, null, false));
            backToTop.Width = 36; backToTop.Height = 38; backToTop.CornerRadius = new CornerRadius(10);
            backToTop.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            backToTop.Resources["ButtonBackgroundPointerOver"] = footerActionBrush;
            backToTop.Resources["ButtonBackgroundPressed"] = footerActionBrush; backToTop.Visibility = Visibility.Collapsed;
            Grid.SetColumn(backToTop, 2); actions.Children.Add(backToTop);
            panel.Children.Add(actions);
            footer.Child = panel;
            footerFade.StartPoint = new Windows.Foundation.Point(0, 0); footerFade.EndPoint = new Windows.Foundation.Point(0, 1);
            footerFade.GradientStops.Add(new GradientStop { Offset = 0 }); footerFade.GradientStops.Add(new GradientStop { Offset = 1 });
            var fade = new Border { Height = 12, VerticalAlignment = VerticalAlignment.Bottom, Background = footerFade, IsHitTestVisible = false };
            Grid.SetRow(fade, 1); root.Children.Add(fade);
            Grid.SetRow(footer, 2); root.Children.Add(footer);
        }

        Border CreateCardBorder(Thickness? padding = null)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = cardBackgroundBrush,
                Padding = padding ?? new Thickness(16),
                Margin = new Thickness(0, 0, 0, 10)
            };
            cardBorders.Add(card);
            return card;
        }

        Border CreateDivider()
        {
            return new Border { Height = 1, Background = dividerBrush };
        }

        TextBox CreateInputBox(string text, bool isReadOnly = false)
        {
            var box = new TextBox
            {
                Text = text,
                IsReadOnly = isReadOnly,
                MinHeight = 38,
                FontSize = 14, FontFamily = new FontFamily("Microsoft YaHei"),
                CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush, Foreground = inkBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 6, 10, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            box.Resources["TextControlBorderBrushFocused"] = subInkBrush;
            box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
            box.Resources["TextControlBackgroundFocused"] = inputBackgroundBrush;
            box.Resources["TextControlBackgroundPointerOver"] = timeHoverBrush;
            void CenterContent()
            {
                box.ApplyTemplate();
                var pending = new Queue<DependencyObject>(); pending.Enqueue(box);
                while (pending.Count > 0)
                {
                    var node = pending.Dequeue();
                    if (node is ScrollViewer viewer && viewer.Name == "ContentElement")
                    { viewer.VerticalAlignment = VerticalAlignment.Center; break; }
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Enqueue(VisualTreeHelper.GetChild(node, i));
                }
            }
            void FitHeight()
            {
                var probe = new TextBlock { Text = "Ag国", FontSize = box.FontSize, FontFamily = box.FontFamily, FontWeight = box.FontWeight };
                probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                box.MinHeight = Math.Max(38, Math.Ceiling(probe.DesiredSize.Height + box.Padding.Top + box.Padding.Bottom + 2));
            }
            box.Loaded += (_, __) => { FitHeight(); CenterContent(); };
            box.RegisterPropertyChangedCallback(Control.FontSizeProperty, (_, __) => FitHeight());
            box.RegisterPropertyChangedCallback(Control.FontFamilyProperty, (_, __) => FitHeight());
            FitHeight();
            return box;
        }

        ToggleSwitch CreateGreenSwitch(bool isOn) => SettingsSwitchStyle.Create(isOn);

        void LockSalaryEditor(bool clearDraft = true)
        {
            if (clearDraft) salaryDraft = null;
            salaryAuthorized = false;
            salary.IsReadOnly = true;
            salary.Text = "****";
            if (eyeSlash != null) eyeSlash.Visibility = Visibility.Visible;
        }

        bool ReconcilePrivacyAfterDialog()
        {
            try
            {
                if (passwordStatus != null) passwordStatus.Text = privacy.HasPassword ? "已设置" : "未设置";
                decimal saved = privacy.LoadSalary();
                if (saved != current.Salary || saved != observedSalary)
                {
                    current.Salary = saved; observedSalary = saved;
                    LockSalaryEditor(); hideSalary();
                }
                return true;
            }
            catch (Exception ex)
            {
                LockSalaryEditor(); hideSalary();
                error.Text = "无法读取薪资，请重新打开设置：" + ex.Message;
                error.Visibility = Visibility.Visible;
                return false;
            }
        }

        void Save(object sender, RoutedEventArgs args)
        {
            error.Visibility = Visibility.Collapsed;
            if (!ReconcilePrivacyAfterDialog()) return;

            if (endTime <= startTime)
            {
                error.Text = "下班时间须晚于上班时间。"; error.Visibility = Visibility.Visible; return;
            }
            if (dayTimeEnabled && dayTimeEnd <= dayTimeStart)
            { error.Text = "所选星期的下班时间须晚于上班时间。"; error.Visibility = Visibility.Visible; return; }

            decimal salaryValue = current.Salary;
            string salaryInput = salaryAuthorized ? salary.Text : salaryDraft ?? string.Empty;
            if ((salaryAuthorized || salaryDraft != null) && (!decimal.TryParse(salaryInput, NumberStyles.Number, CultureInfo.InvariantCulture, out salaryValue) || salaryValue < 0 || salaryValue > 100000000))
            {
                error.Text = "月薪须为 0–100000000 的数值。"; error.Visibility = Visibility.Visible;
                EnsureAccordionExpanded(1);
                return;
            }

            if (!decimal.TryParse(workDays.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal work) || work <= 0 || work > 31)
            {
                error.Text = "计薪天数须大于 0 且不超过 31。"; error.Visibility = Visibility.Visible;
                EnsureAccordionExpanded(1);
                return;
            }

            if (!int.TryParse(payDay.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pay) || pay < 1 || pay > 31)
            {
                error.Text = "发薪日须为 1–31 的整数。"; error.Visibility = Visibility.Visible;
                EnsureAccordionExpanded(1);
                return;
            }

            double size = scale.Value;
            if (!double.IsFinite(size) || size < 0.5 || size > 1.5)
            {
                error.Text = "桌面布局大小范围为 0.5–1.5 倍。"; error.Visibility = Visibility.Visible;
                EnsureAccordionExpanded(1);
                return;
            }

            int seconds = amountDurationSeconds;

            int waterVal = waterMinutes;
            if (waterVal < 1 || waterVal > 720)
            {
                error.Text = "喝水提醒间隔须为 1–720 分钟的整数。"; error.Visibility = Visibility.Visible;
                EnsureAccordionExpanded(3);
                return;
            }

            int walkVal = walkMinutes;
            if (walkVal < 1 || walkVal > 720)
            {
                error.Text = "走动提醒间隔须为 1–720 分钟的整数。"; error.Visibility = Visibility.Visible;
                EnsureAccordionExpanded(3);
                return;
            }

            RememberSchedule();
            var next = new OffWorkWidget.Settings
            {
                Start = startTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                End = endTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                WorkScheduleMode = scheduleMode,
                WorkSchedules = workSchedules.Select(p => new OffWorkWidget.WorkScheduleProfile { Mode = p.Mode, Start = p.Start, End = p.End, SaturdayEnabled = p.SaturdayEnabled, SaturdayStart = p.SaturdayStart, SaturdayEnd = p.SaturdayEnd, DayTimes = (p.DayTimes ?? new List<OffWorkWidget.DayWorkTime>()).Select(d => new OffWorkWidget.DayWorkTime { Day = d.Day, Enabled = d.Enabled, Start = d.Start, End = d.End }).ToList() }).ToList(),
                Salary = salaryValue,
                WorkDays = work,
                PayDay = pay,
                Scale = size,
                ScaleReferenceVersion = current.ScaleReferenceVersion,
                AutomaticAppearance = automatic,
                NightMode = night,
                PetId = petId,
                HideAmount = current.HideAmount,
                ShowSalaryLayout = current.ShowSalaryLayout,
                AmountVisibleSeconds = seconds,
                UseChinaHolidays = holidays.IsOn,
                Calendar = current.Calendar,
                RestDays = weekdays.Select((day, index) => day.IsChecked == true ? (1 << index) : 0).Sum(),
                SingleRestDays = singleRestDays,
                DoubleRestDays = doubleRestDays,
                AlternatingWeekends = alternatingWeekends,
                DoubleRestWeekStart = doubleRestWeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateAdjustments = adjustments.Select(a => new OffWorkWidget.DateAdjustment { Date = a.Date, IsWorkday = a.IsWorkday }).ToList(),
                WaterReminder = water.IsOn,
                WalkReminder = walk.IsOn,
                WaterMinutes = waterVal,
                WalkMinutes = walkVal,
                Topmost = topmost.IsOn,
                X = current.X,
                Y = current.Y
            };

            try
            {
                OffWorkWidget.Logic.Validate(next);
                saveSettings(next);
                Close();
            }
            catch (Exception ex)
            {
                error.Text = "未保存：" + ex.Message;
                error.Visibility = Visibility.Visible;
            }
        }

        static BitmapImage PetImage(string id) => new BitmapImage(new Uri("ms-appx:///assets/" + id + ".png"));
        static string PetName(string id) => id == "samoyed" ? "萨摩耶" : id == "polar-bear" ? "大白熊" : "小猫";

        [StructLayout(LayoutKind.Sequential)] struct ClientRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int style);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out ClientRect rect);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref ClientRect rect, IntPtr brush);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    }
}
