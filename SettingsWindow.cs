using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
        readonly Action<OffWorkWidget.Settings> saveSettings;
        readonly Action<double>? previewScale;

        const double WindowCornerRadius = 36;
        readonly Grid windowSurface = new Grid();
        readonly Grid windowContent = new Grid();
        readonly SolidColorBrush cornerMaterial = new SolidColorBrush();
        readonly SystemBackdropElement frostedBackground = new SystemBackdropElement { CornerRadius = new CornerRadius(WindowCornerRadius + 4), IsHitTestVisible = false };
        readonly LinearGradientBrush glassRimBrush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        readonly Border glassTint = new Border { IsHitTestVisible = false };
        readonly SolidColorBrush glassTintBrush = new SolidColorBrush();
        readonly Border glassSheen = new Border { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, Height = 220 };
        readonly LinearGradientBrush glassSheenBrush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? windowGeometry;
        readonly Grid root = new Grid();
        readonly StackPanel form = new StackPanel { Spacing = 0 };
        readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        readonly List<OffWorkWidget.DateAdjustment> adjustments;
        readonly List<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton> weekdays = new List<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>();
        readonly List<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton> restPresetButtons = new();
        Border? restPresetIndicator;
        Grid? restPresetTrack;
        double restPresetTargetX = double.NaN;
        bool customRestPreset, settingRestPreset;
        int singleRestDays = 1, doubleRestDays = 65;
        bool alternatingWeekends;
        DateTime doubleRestWeekStart;
        Grid alternatingWeekRow = null!;
        Border alternatingWeekReveal = null!;
        TextBlock restWeekHint = null!;
        readonly Stopwatch restRowClock = new Stopwatch();
        readonly RectangleGeometry restRowClip = new RectangleGeometry();
        bool animatingRestRow, alternatingWeekShown;
        double restRowHeight, restRowFromHeight, restRowToHeight, restRowFromOpacity, restRowToOpacity, restRowNaturalHeight;
        readonly List<Border> cardBorders = new List<Border>();
        readonly StackPanel adjustmentRows = new StackPanel { Spacing = 4 };
        TextBlock adjustmentSummary = null!;
        Microsoft.UI.Dispatching.DispatcherQueueTimer? scrollRailTimer;

        bool windowClosed, petCommitted;
        bool raisedForActivation;
        TimeSpan startTime, endTime;
        TimeSpan lunchStart, lunchEnd;
        TimeSpan dayTimeStart, dayTimeEnd;
        bool dayTimeEnabled, loadingSchedule;
        string scheduleMode = "";
        readonly List<OffWorkWidget.WorkScheduleProfile> workSchedules = new();
        TextBlock dayTimeStartDisplay = null!, dayTimeEndDisplay = null!;
        ToggleSwitch dayTimeSwitch = null!;
        Grid dayTimeTimes = null!;
        readonly List<OffWorkWidget.DayWorkTime> dayTimes = new();
        int editingDay = 6;
        bool dayTimeExpanded;
        StackPanel dayTimeSection = null!, dayTimeEditor = null!;
        Border dayTimeReveal = null!, dayTimeTimesReveal = null!;
        readonly RectangleGeometry dayTimeTimesClip = new RectangleGeometry();
        double dayTimeTimesFrom, dayTimeTimesTo, dayTimeTimesOpacityFrom;
        readonly RectangleGeometry dayTimeRevealClip = new RectangleGeometry();
        readonly Stopwatch dayTimeRevealClock = new Stopwatch();
        bool animatingDayTimeReveal;
        double dayTimeRevealHeight, dayTimeRevealStartHeight, dayTimeRevealTargetHeight;
        double dayTimeRevealStartOpacity, dayTimeRevealTargetOpacity, dayTimeArrowStart, dayTimeArrowTarget;
        double dayTimeEditorStartOpacity, dayTimeEditorTargetOpacity;
        Border popupOptionsReveal = null!;
        StackPanel popupOptionsPanel = null!;
        readonly RectangleGeometry popupOptionsClip = new RectangleGeometry();
        readonly Stopwatch popupOptionsClock = new Stopwatch();
        bool animatingPopupOptions;
        double popupOptionsHeight, popupOptionsStartHeight, popupOptionsTargetHeight;
        // Hiding runs longer than revealing: the reminder-mode flyout is still
        // fading over the card for the first ~170ms of the collapse.
        double popupOptionsDuration = 260;
        double popupOptionsElapsed, popupOptionsLastTick;
        double popupOptionsStartOpacity, popupOptionsTargetOpacity;
        ComboBox dayTimeDay = null!;
        TextBlock dayTimeSummary = null!, dayTimeStartLabel = null!, dayTimeEndLabel = null!;
        Grid dayTimeChevron = null!;
        TextBlock startTimeDisplay, endTimeDisplay;
        TextBox salary, workDays, payDay;
        ToggleSwitch wishEnabled = null!;
        StackPanel wishRowsHost = null!;
        Button wishAdd = null!;
        TextBlock wishCount = null!;
        ComboBox wishAllocation = null!;
        readonly List<WishRowEditor> wishRows = new();
        readonly HashSet<string> achievedWishKeys = new();
        sealed class WishRowEditor
        {
            internal string Id = "";
            internal FrameworkElement Card = null!;
            internal TextBox Name = null!, Target = null!, Saved = null!;
            internal Button Delete = null!;
            internal TextBlock? Status;
        }
        static string WishKey(OffWorkWidget.WishEntry wish) =>
            wish.Name + "|" + wish.Target.ToString(CultureInfo.InvariantCulture);
        int waterMinutes, walkMinutes;
        ComboBox reminderDisplay = null!;
        string reminderPosition = "center";
        int reminderX, reminderY;
        Window? positionPreview;
        static readonly string[] ReminderPositions = { "bottom-right", "bottom-left", "top-right", "top-left", "center" };
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
        ToggleSwitch startup = null!, mouseClickThrough = null!, showCoinJar = null!;
        ComboBox hoverDragSeconds = null!;
        Grid hoverDragRow = null!;
        Action<bool>? hoverOptionsRevealAction;
        bool startupInitiallyEnabled;
        TextBlock holidayStatus, passwordStatus, scaleLabel;
        Action? refreshAdjustmentReveal;
        Action? refreshEffectShop;
        Action? refreshDailyFortune;
        Image petThumb;
        Border incomeContent, restContent, careContent;
        Grid incomeChevron, restChevron, careChevron;
        FontIcon salaryVisibilityIcon;
        ToggleSwitch salaryLayoutSwitch;

        ScrollViewer settingsScroll = null!;
        TextBlock compactTitle = null!;
        double titleCompactProgress, titleCompactVelocity, titleCompactTarget;
        bool titleSpringRunning;
        long titleSpringLastTick;
        Border toolbar = null!;
        Button autoAppearanceButton = null!, dayAppearanceButton = null!, nightAppearanceButton = null!, backToTop = null!, footerCancel = null!;
        Button? lastSelectedAppearanceButton;
        bool appearanceSelectionReady;
        long appearancePopGuardUntil;
        readonly ScaleTransform footerArrowScale = new ScaleTransform { ScaleX = 0, ScaleY = 0 };
        readonly Stopwatch footerChromeClock = new Stopwatch();
        bool animatingFooterChrome;
        double footerChromeProgress, footerChromeStart, footerChromeTarget;
        readonly SolidColorBrush pageBackgroundBrush = new SolidColorBrush();
        readonly AcrylicBrush headerBackgroundBrush = new AcrylicBrush { Opacity = 0 };
        readonly SolidColorBrush footerBackgroundBrush = new SolidColorBrush();
        readonly SolidColorBrush footerActionBrush = new SolidColorBrush();
        readonly SolidColorBrush petWheelSurfaceBrush = new SolidColorBrush();
        readonly RadialGradientBrush petWheelFade = new RadialGradientBrush();
        Border? footerBar;
        readonly LinearGradientBrush footerFade = new LinearGradientBrush();
        readonly LinearGradientBrush headerFade = new LinearGradientBrush();
        readonly Border headerFadeLayer = new Border { Height = 24, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Opacity = 0 };
        readonly Border themeFadeLayer = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Opacity = 0 };
        readonly Image themeFadeImage = new Image { Stretch = Stretch.Fill };
        const int ThemeEventGuardMs = 250;
        Storyboard? themeFadeStoryboard;
        long ignoreThemeEventUntil;
        bool themeReady, themeFadeActive, autoThemeCaptureActive, manualThemeCaptureActive;
        enum AppearanceMode { Automatic, Day, Night }
        AppearanceMode? pendingAppearanceMode;
        readonly Border[] accordionCards = new Border[3];
        readonly TextBlock?[] accordionSummaries = new TextBlock?[3];
        StackPanel workingHoursContent = null!, restPlanContent = null!, appearanceOptions = null!;
        Button petSelector = null!;
        readonly double[] accordionProgress = new double[3], accordionStart = new double[3], accordionTarget = new double[3];
        readonly Stopwatch accordionClock = new Stopwatch();
        readonly Stopwatch wishDetailsClock = new Stopwatch();
        readonly Windows.UI.ViewManagement.UISettings animationSettings = new Windows.UI.ViewManagement.UISettings();
        Border wishDetailsHost = null!;
        StackPanel wishDetails = null!;
        TextBlock wishSummary = null!;
        bool animatingAccordion, animatingWishDetails;
        double wishDetailsProgress, wishDetailsStart, wishDetailsTarget;
        int expandedAccordion;
        bool workSectionExpanded = true;
        bool salaryAuthorized, automatic, night, privacyPromptOpen;
        string? salaryDraft;
        string petId;
        string initialPetId;
        string hoverEffect;
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
        readonly SolidColorBrush selectionBackgroundBrush = new SolidColorBrush();
        readonly RadialGradientBrush appearanceSelectionBrush = new RadialGradientBrush();
        readonly SolidColorBrush appearanceSelectionRimBrush = new SolidColorBrush();
        readonly RadialGradientBrush eyeGlassBrush = new RadialGradientBrush();
        readonly RadialGradientBrush chipHoverBrush = new RadialGradientBrush();
        readonly SolidColorBrush restPresetSelectionBrush = new SolidColorBrush();
        readonly SolidColorBrush restPresetBorderBrush = new SolidColorBrush();
        readonly SolidColorBrush timeHoverBrush = new SolidColorBrush();
        readonly SolidColorBrush timePressedBrush = new SolidColorBrush();
        readonly SolidColorBrush dividerBrush = new SolidColorBrush();
        readonly SolidColorBrush accentBlue = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 122, 255));
        readonly SolidColorBrush cancelGray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 229, 229, 234));
        readonly SolidColorBrush cancelDarkGray = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 58, 58, 60));

        public SettingsWindow(OffWorkWidget.Settings current, OffWorkWidget.PrivacyStore privacy,
            Action toggleSalaryLayout, Action hideSalary,
            Action<OffWorkWidget.Settings> saveSettings, Action<double>? previewScale = null)
        {
            this.current = current; this.privacy = privacy; this.toggleSalaryLayout = toggleSalaryLayout;
            this.hideSalary = hideSalary; this.saveSettings = saveSettings; this.previewScale = previewScale;
            automatic = current.AutomaticAppearance;
            night = current.NightMode; petId = current.PetId; initialPetId = petId; hoverEffect = OffWorkWidget.HoverEffects.Normalize(current.HoverEffect); observedSalary = current.Salary;
            alternatingWeekends = current.AlternatingWeekends;
            doubleRestWeekStart = DateTime.TryParseExact(current.DoubleRestWeekStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime anchor)
                ? OffWorkWidget.Logic.WeekStart(anchor) : OffWorkWidget.Logic.WeekStart(DateTime.Today);
            string todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            adjustments = (current.DateAdjustments ?? new List<OffWorkWidget.DateAdjustment>())
                .Where(a => a != null && string.CompareOrdinal(a.Date, todayKey) >= 0)
                .Select(a => new OffWorkWidget.DateAdjustment { Date = a.Date, IsWorkday = a.IsWorkday }).ToList();

            startTime = OffWorkWidget.Logic.Time(current.Start);
            endTime = OffWorkWidget.Logic.Time(current.End);
            lunchStart = OffWorkWidget.Logic.Time(current.LunchStart);
            lunchEnd = OffWorkWidget.Logic.Time(current.LunchEnd);
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
            Activated += (_, args) =>
            {
                if (args.WindowActivationState == WindowActivationState.Deactivated && raisedForActivation)
                {
                    raisedForActivation = false;
                    SetWindowPos(hwnd, new IntPtr(-2), 0, 0, 0, 0, 0x0013); // HWND_NOTOPMOST, no activation or resize
                }
            };
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
            frameMonitor.WindowMessageReceived += SuppressFrameBackground;
            root.Loaded += (_, __) => UpdateWindowContour();
            native.Changed += (_, args) => { if (args.DidSizeChange) UpdateWindowContour(); };

            root.Resources["ContentControlThemeFontFamily"] = new FontFamily("Microsoft YaHei");
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Microsoft YaHei")));
            root.Resources[typeof(TextBlock)] = textStyle;

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ActualThemeChanged += (_, __) => OnActualThemeChanged();
            SystemBackdrop = new WinUIEx.TransparentTintBackdrop(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            themeFadeLayer.Child = themeFadeImage;
            glassTint.Background = glassTintBrush;
            glassSheen.Background = glassSheenBrush;
            glassSheenBrush.GradientStops.Add(new GradientStop { Offset = 0 });
            glassSheenBrush.GradientStops.Add(new GradientStop { Offset = .55 });
            glassSheenBrush.GradientStops.Add(new GradientStop { Offset = 1 });
            windowSurface.Children.Add(frostedBackground);
            // Only the curved sections need fractional pixel coverage. Leave the
            // straight perimeter to the same acrylic and content as the interior.
            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0, top = i < 2;
                const double r = WindowCornerRadius;
                windowSurface.Children.Add(new Border
                {
                    Width = r, Height = r, IsHitTestVisible = false,
                    HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                    CornerRadius = new CornerRadius(left && top ? r : 0, !left && top ? r : 0,
                        !left && !top ? r : 0, left && !top ? r : 0),
                    BorderThickness = new Thickness(left ? 2 : 0, top ? 2 : 0, left ? 0 : 2, top ? 0 : 2),
                    BorderBrush = cornerMaterial
                });
            }
            windowContent.Children.Add(glassTint); windowContent.Children.Add(glassSheen);
            windowContent.Children.Add(root); windowContent.Children.Add(themeFadeLayer);
            windowSurface.Children.Add(windowContent);
            windowSurface.SizeChanged += (_, __) => UpdateWindowContour();
            Content = windowSurface;

            ApplyTheme();
            BuildHeader();

            settingsScroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            settingsScroll.ViewChanged += (_, __) => UpdateScrollChrome();
            form.Margin = new Thickness(16, 0, 16, 5);
            form.Children.Add(new Border { Height = 56, IsHitTestVisible = false });
            settingsScroll.Loaded += (_, __) => BindTitleTransition();
            var scrollSurface = CreateScrollSurface();
            Grid.SetRow(scrollSurface, 1); root.Children.Add(scrollSurface);
            BuildPetSelector();

            BuildWorkingHoursContent();
            BuildAccordionGroup();
            BuildDesktopLayoutCard();
            BuildWindowOptionsCard();

            BuildFooter();
            headerFade.StartPoint = new Windows.Foundation.Point(0, 0); headerFade.EndPoint = new Windows.Foundation.Point(0, 1);
            foreach (double offset in new[] { 0d, .28, .68, 1d }) headerFade.GradientStops.Add(new GradientStop { Offset = offset });
            headerFadeLayer.Background = headerFade;
            Grid.SetRow(headerFadeLayer, 1); root.Children.Add(headerFadeLayer);
            PositionWindow();
            ApplyThemeBrushes();
            root.Loaded += (_, __) => themeReady = true;
            Closed += (_, __) =>
            {
                RevertPetPreview();
                positionPreview?.Close();
                windowClosed = true; scrollRailTimer?.Stop();
                pendingAppearanceMode = null;
                themeFadeStoryboard?.Stop(); themeFadeStoryboard = null;
                themeFadeActive = false; themeFadeImage.Source = null;
                CancelScaleGesture(); scaleInput?.ReleasePointerCaptures();
                CompositionTarget.Rendering -= AnimateAccordion; animatingAccordion = false;
                CompositionTarget.Rendering -= AnimateDayTimeReveal; animatingDayTimeReveal = false; dayTimeRevealClock.Stop();
                CompositionTarget.Rendering -= AnimatePopupOptionsReveal; animatingPopupOptions = false; popupOptionsClock.Stop();
                CompositionTarget.Rendering -= AnimateAlternatingWeekReveal; animatingRestRow = false; restRowClock.Stop();
                CompositionTarget.Rendering -= AnimateTitleSpring; titleSpringRunning = false;
                renderFrameMonitor?.Dispose(); frameMonitor.Dispose();
                frostedBackground.SystemBackdrop = null; SystemBackdrop = null;
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(windowContent).Clip = null;
                windowGeometry?.Dispose();
            };
        }

        void RevertPetPreview()
        {
            if (petCommitted || petId == initialPetId) return;
            petId = initialPetId;
        }

        void CloseSettings()
        {
            RevertPetPreview();
            // Hide the composed frame before Closed disconnects its backdrop and rounded clip.
            native.Hide();
            Close();
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
            double width = windowSurface.ActualWidth, height = windowSurface.ActualHeight;
            // Establish the contour before the first XAML layout is presented.
            if ((width <= 0 || height <= 0) && GetClientRect(hwnd, out ClientRect bounds))
            {
                double dpi = Math.Max(96, GetDpiForWindow(hwnd)) / 96d;
                width = (bounds.Right - bounds.Left) / dpi;
                height = (bounds.Bottom - bounds.Top) / dpi;
            }
            if (width > 0 && height > 0)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(windowContent);
                if (windowGeometry == null)
                {
                    // Keep the tinted content and footer inside the acrylic outline.
                    windowGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
                    visual.Clip = visual.Compositor.CreateGeometricClip(windowGeometry);
                }
                windowGeometry.Size = new System.Numerics.Vector2((float)width, (float)height);
                windowGeometry.CornerRadius = new System.Numerics.Vector2((float)WindowCornerRadius);
            }
            IntPtr render = FindWindowEx(hwnd, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
            if (render != IntPtr.Zero && render != frameRenderHandle)
            {
                renderFrameMonitor?.Dispose(); frameRenderHandle = render;
                renderFrameMonitor = new WinUIEx.Messaging.WindowMessageMonitor(render);
                renderFrameMonitor.WindowMessageReceived += SuppressFrameBackground;
            }
        }

        void SuppressFrameBackground(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
        {
            // Keep the pixels outside the rounded clip fully transparent instead
            // of letting the default erase paint them.
            if (args.Message.MessageId != 0x0014) return;
            args.Result = (IntPtr)1; args.Handled = true;
        }

        void ChangeAppearance(AppearanceMode mode)
        {
            // Complete the click/pointer dispatch before updating theme resources.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                if (windowClosed || !themeReady) return;
                if (themeFadeActive || autoThemeCaptureActive || manualThemeCaptureActive)
                {
                    pendingAppearanceMode = mode;
                    return;
                }
                bool targetAutomatic = mode == AppearanceMode.Automatic;
                bool targetNight = mode == AppearanceMode.Night;
                if (automatic == targetAutomatic && (targetAutomatic || night == targetNight)) return;
                // Snapshot the outgoing theme before RequestedTheme swaps every
                // built-in brush, then fade the snapshot over the new theme.
                manualThemeCaptureActive = true;
                try
                {
                    var snapshot = await CaptureThemeSnapshotAsync();
                    if (windowClosed) return;
                    automatic = targetAutomatic;
                    night = targetNight;
                    ShowThemeFade(snapshot);
                    ApplyTheme();
                    StartThemeFadeAnimation();
                }
                finally
                {
                    manualThemeCaptureActive = false;
                    ConsumePendingAppearance();
                }
            });
        }

        void ConsumePendingAppearance()
        {
            if (windowClosed || themeFadeActive || autoThemeCaptureActive || manualThemeCaptureActive) return;
            var pending = pendingAppearanceMode;
            pendingAppearanceMode = null;
            if (pending.HasValue) ChangeAppearance(pending.Value);
        }

        async void OnActualThemeChanged()
        {
            if (windowClosed || !themeReady || !automatic) return;
            // RequestedTheme changes raise this event on a later turn. Those
            // self-induced events keep the brushes in step but must not start
            // another fade, or automatic mode would toggle in a loop.
            if (Environment.TickCount64 < ignoreThemeEventUntil)
            {
                ApplyThemeBrushes();
                return;
            }
            if (!animationSettings.AnimationsEnabled || autoThemeCaptureActive || manualThemeCaptureActive)
            {
                ApplyThemeBrushes();
                return;
            }
            // Rebuild the outgoing theme for one snapshot. Setting RequestedTheme
            // on this turn keeps the new theme from painting before the fade.
            autoThemeCaptureActive = true;
            try
            {
                var outgoing = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
                ignoreThemeEventUntil = Environment.TickCount64 + ThemeEventGuardMs;
                root.RequestedTheme = outgoing;
                windowSurface.RequestedTheme = outgoing;
                var snapshot = await CaptureThemeSnapshotAsync();
                if (windowClosed || !automatic) return;
                ignoreThemeEventUntil = Environment.TickCount64 + ThemeEventGuardMs;
                root.RequestedTheme = ElementTheme.Default;
                windowSurface.RequestedTheme = ElementTheme.Default;
                ShowThemeFade(snapshot);
                ApplyThemeBrushes();
                StartThemeFadeAnimation();
            }
            finally
            {
                autoThemeCaptureActive = false;
                ConsumePendingAppearance();
            }
        }

        void ApplyTheme()
        {
            var requested = automatic ? ElementTheme.Default : night ? ElementTheme.Dark : ElementTheme.Light;
            ignoreThemeEventUntil = Environment.TickCount64 + ThemeEventGuardMs;
            root.RequestedTheme = requested;
            windowSurface.RequestedTheme = requested;
            if (frostedBackground.SystemBackdrop == null)
                frostedBackground.SystemBackdrop = new DesktopAcrylicBackdrop();
            // Keep the backdrop target alive while pointer input is being dispatched.
            // Manual themes cover it with their opaque page brush.
            ApplyThemeBrushes();
        }

        async Task<RenderTargetBitmap?> CaptureThemeSnapshotAsync()
        {
            try
            {
                if (!root.IsLoaded || root.ActualWidth <= 0 || root.ActualHeight <= 0) return null;
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(root);
                return bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 ? bitmap : null;
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidOperationException || ex is ArgumentException)
            {
                return null;
            }
        }

        void ShowThemeFade(RenderTargetBitmap? snapshot)
        {
            themeFadeStoryboard?.Stop();
            themeFadeStoryboard = null;
            themeFadeActive = false;
            if (snapshot == null || !animationSettings.AnimationsEnabled)
            {
                themeFadeImage.Source = null;
                themeFadeLayer.Opacity = 0;
                themeFadeLayer.Visibility = Visibility.Collapsed;
                return;
            }
            themeFadeImage.Source = snapshot;
            themeFadeLayer.Opacity = 1;
            themeFadeLayer.Visibility = Visibility.Visible;
            themeFadeActive = true;
        }

        void StartThemeFadeAnimation()
        {
            if (!themeFadeActive) return;
            if (!animationSettings.AnimationsEnabled) { FinishThemeFade(); return; }
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(320)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, themeFadeLayer);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Completed += (_, __) => { if (ReferenceEquals(themeFadeStoryboard, storyboard)) FinishThemeFade(); };
            themeFadeStoryboard = storyboard;
            storyboard.Begin();
        }

        void FinishThemeFade()
        {
            themeFadeStoryboard = null;
            themeFadeActive = false;
            themeFadeImage.Source = null;
            themeFadeLayer.Opacity = 0;
            themeFadeLayer.Visibility = Visibility.Collapsed;
            ConsumePendingAppearance();
        }

        void ApplyThemeBrushes()
        {
            bool dark = root.ActualTheme == ElementTheme.Dark;
            cornerMaterial.Color = dark ? Windows.UI.Color.FromArgb(233, 45, 45, 45) : Windows.UI.Color.FromArgb(226, 245, 245, 245);
            // All appearance modes share the same glass surface; the mode only
            // forces the light/dark palette, so manual day/night match automatic.
            pageBackgroundBrush.Color = dark ? Windows.UI.Color.FromArgb(0, 40, 40, 44) : Windows.UI.Color.FromArgb(0, 255, 255, 255);
            root.Background = pageBackgroundBrush;
            footerActionBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 58, 58, 60) : Windows.UI.Color.FromArgb(255, 229, 229, 234);
            footerBackgroundBrush.Color = dark ? Windows.UI.Color.FromArgb(190, 28, 28, 30) : Windows.UI.Color.FromArgb(210, 242, 242, 247);
            petWheelSurfaceBrush.Color = dark ? Windows.UI.Color.FromArgb(160, 35, 35, 38) : Windows.UI.Color.FromArgb(255, 255, 255, 255);
            UpdatePetWheelFade();
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
            headerBackgroundBrush.TintColor = dark ? Windows.UI.Color.FromArgb(255, 28, 28, 30) : Windows.UI.Color.FromArgb(255, 242, 242, 247);
            headerBackgroundBrush.TintOpacity = dark ? .25 : .55;
            headerBackgroundBrush.TintLuminosityOpacity = dark ? .40 : .85;
            headerBackgroundBrush.FallbackColor = dark ? Windows.UI.Color.FromArgb(255, 28, 28, 30) : Windows.UI.Color.FromArgb(255, 242, 242, 247);
            UpdateHeaderTransition();
            if (eyeGlassBrush.GradientStops.Count == 0)
            {
                eyeGlassBrush.Center = new Windows.Foundation.Point(.5, .5);
                eyeGlassBrush.GradientOrigin = new Windows.Foundation.Point(.5, .5);
                eyeGlassBrush.RadiusX = .5;
                eyeGlassBrush.RadiusY = .5;
                foreach (double offset in new[] { 0, .45, .7, .88, 1 })
                    eyeGlassBrush.GradientStops.Add(new GradientStop { Offset = offset });
            }
            // Neutral ink-toned fill with a long fade and no rim, so the chip
            // has no visible edge against the card.
            byte chipTone = dark ? (byte)242 : (byte)28;
            eyeGlassBrush.GradientStops[0].Color = Windows.UI.Color.FromArgb(dark ? (byte)18 : (byte)10, chipTone, chipTone, chipTone);
            eyeGlassBrush.GradientStops[1].Color = Windows.UI.Color.FromArgb(dark ? (byte)16 : (byte)9, chipTone, chipTone, chipTone);
            eyeGlassBrush.GradientStops[2].Color = Windows.UI.Color.FromArgb(dark ? (byte)12 : (byte)7, chipTone, chipTone, chipTone);
            eyeGlassBrush.GradientStops[3].Color = Windows.UI.Color.FromArgb(dark ? (byte)5 : (byte)3, chipTone, chipTone, chipTone);
            eyeGlassBrush.GradientStops[4].Color = Windows.UI.Color.FromArgb(0, chipTone, chipTone, chipTone);
            if (chipHoverBrush.GradientStops.Count == 0)
            {
                chipHoverBrush.Center = new Windows.Foundation.Point(.5, .5);
                chipHoverBrush.GradientOrigin = new Windows.Foundation.Point(.5, .5);
                chipHoverBrush.RadiusX = .5;
                chipHoverBrush.RadiusY = .5;
                foreach (double offset in new[] { 0, .7, .88, 1 })
                    chipHoverBrush.GradientStops.Add(new GradientStop { Offset = offset });
            }
            chipHoverBrush.GradientStops[0].Color = Windows.UI.Color.FromArgb(dark ? (byte)42 : (byte)26, chipTone, chipTone, chipTone);
            chipHoverBrush.GradientStops[1].Color = Windows.UI.Color.FromArgb(dark ? (byte)36 : (byte)22, chipTone, chipTone, chipTone);
            chipHoverBrush.GradientStops[2].Color = Windows.UI.Color.FromArgb(dark ? (byte)18 : (byte)11, chipTone, chipTone, chipTone);
            chipHoverBrush.GradientStops[3].Color = Windows.UI.Color.FromArgb(0, chipTone, chipTone, chipTone);
            if (autoAppearanceButton != null)
            {
                if (appearanceSelectionBrush.GradientStops.Count == 0)
                {
                    appearanceSelectionBrush.Center = new Windows.Foundation.Point(.5, .5);
                    appearanceSelectionBrush.GradientOrigin = new Windows.Foundation.Point(.5, .5);
                    appearanceSelectionBrush.RadiusX = .5;
                    appearanceSelectionBrush.RadiusY = .5;
                    foreach (double offset in new[] { 0, .7, .88, 1 })
                        appearanceSelectionBrush.GradientStops.Add(new GradientStop { Offset = offset });
                }
                // Light keeps the soft white highlight; dark uses a neutral ink
                // chip because white glowed too brightly on the smoked header.
                byte selR = dark ? chipTone : (byte)255, selG = dark ? chipTone : (byte)255, selB = dark ? chipTone : (byte)255;
                appearanceSelectionBrush.GradientStops[0].Color = Windows.UI.Color.FromArgb(dark ? (byte)54 : (byte)152, selR, selG, selB);
                appearanceSelectionBrush.GradientStops[1].Color = Windows.UI.Color.FromArgb(dark ? (byte)46 : (byte)144, selR, selG, selB);
                appearanceSelectionBrush.GradientStops[2].Color = Windows.UI.Color.FromArgb(dark ? (byte)22 : (byte)82, selR, selG, selB);
                appearanceSelectionBrush.GradientStops[3].Color = Windows.UI.Color.FromArgb(0, selR, selG, selB);
                appearanceSelectionRimBrush.Color = Windows.UI.Color.FromArgb(dark ? (byte)28 : (byte)115, selR, selG, selB);
                void StyleAppearance(Button button, bool selected)
                {
                    button.Background = selected ? appearanceSelectionBrush : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    button.BorderBrush = selected ? appearanceSelectionRimBrush : null;
                    button.BorderThickness = selected ? new Thickness(1) : new Thickness(0);
                }
                StyleAppearance(autoAppearanceButton, automatic);
                StyleAppearance(dayAppearanceButton, !automatic && !dark);
                StyleAppearance(nightAppearanceButton, !automatic && dark);
                var selected = automatic ? autoAppearanceButton : dark ? nightAppearanceButton : dayAppearanceButton;
                if (appearanceSelectionReady && selected != lastSelectedAppearanceButton &&
                    Environment.TickCount64 >= appearancePopGuardUntil)
                    PopSpringElement(selected);
                lastSelectedAppearanceButton = selected;
                appearanceSelectionReady = true;
            }
            inkBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 242, 242, 247) : Windows.UI.Color.FromArgb(255, 28, 28, 30);
            subInkBrush.Color = dark ? Windows.UI.Color.FromArgb(255, 174, 174, 182) : Windows.UI.Color.FromArgb(255, 107, 107, 116);
            cardBackgroundBrush.Color = dark
                ? Windows.UI.Color.FromArgb(28, 255, 255, 255)
                : Windows.UI.Color.FromArgb(82, 255, 255, 255);
            selectionBackgroundBrush.Color = dark
                ? Windows.UI.Color.FromArgb(70, 255, 255, 255)
                : Windows.UI.Color.FromArgb(210, 255, 255, 255);
            restPresetSelectionBrush.Color = dark
                ? Windows.UI.Color.FromArgb(255, 96, 100, 107)
                : Windows.UI.Color.FromArgb(255, 227, 228, 231);
            restPresetBorderBrush.Color = dark
                ? Windows.UI.Color.FromArgb(100, 120, 123, 130)
                : Windows.UI.Color.FromArgb(112, 196, 198, 202);
            inputBackgroundBrush.Color = dark
                ? Windows.UI.Color.FromArgb(36, 255, 255, 255)
                : Windows.UI.Color.FromArgb(175, 255, 255, 255);
            timeHoverBrush.Color = dark
                ? Windows.UI.Color.FromArgb(50, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 210, 215, 227);
            timePressedBrush.Color = dark
                ? Windows.UI.Color.FromArgb(70, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 200, 206, 221);
            dividerBrush.Color = dark
                ? Windows.UI.Color.FromArgb(110, 80, 80, 88)
                : Windows.UI.Color.FromArgb(100, 204, 207, 215);
            glassTintBrush.Color = dark
                ? Windows.UI.Color.FromArgb(84, 4, 8, 14)
                : Windows.UI.Color.FromArgb(34, 92, 104, 120);
            if (glassSheenBrush.GradientStops.Count == 3)
            {
                byte top = (byte)(dark ? 8 : 82);
                byte mid = (byte)(dark ? 2 : 24);
                glassSheenBrush.GradientStops[0].Color = Windows.UI.Color.FromArgb(top, 255, 255, 255);
                glassSheenBrush.GradientStops[1].Color = Windows.UI.Color.FromArgb(mid, 255, 255, 255);
                glassSheenBrush.GradientStops[2].Color = Windows.UI.Color.FromArgb(0, 255, 255, 255);
            }
            if (glassRimBrush.GradientStops.Count == 0)
            {
                foreach (double offset in new[] { 0d, .55, 1d })
                    glassRimBrush.GradientStops.Add(new GradientStop { Offset = offset });
            }
            // Light falls from above: bright top edge fading to almost nothing
            // at the bottom, so the outline no longer reads as a hard ring.
            glassRimBrush.GradientStops[0].Color = Windows.UI.Color.FromArgb(dark ? (byte)24 : (byte)58, 255, 255, 255);
            glassRimBrush.GradientStops[1].Color = Windows.UI.Color.FromArgb(dark ? (byte)10 : (byte)24, 255, 255, 255);
            glassRimBrush.GradientStops[2].Color = Windows.UI.Color.FromArgb(dark ? (byte)2 : (byte)6, 255, 255, 255);

            foreach (var card in cardBorders)
            {
                card.Background = cardBackgroundBrush;
            }
        }


        static Brush MenuFrost(bool dark)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1)
            };
            brush.GradientStops.Add(new GradientStop
            {
                Offset = 0,
                Color = dark ? Windows.UI.Color.FromArgb(255, 46, 46, 49) : Windows.UI.Color.FromArgb(255, 252, 252, 255)
            });
            brush.GradientStops.Add(new GradientStop
            {
                Offset = 1,
                Color = dark ? Windows.UI.Color.FromArgb(255, 34, 34, 37) : Windows.UI.Color.FromArgb(255, 246, 248, 252)
            });
            return brush;
        }

        void UpdatePetWheelFade()
        {            if (petWheelFade.GradientStops.Count != 3) return;
            var c = petWheelSurfaceBrush.Color;
            var clear = Windows.UI.Color.FromArgb(0, c.R, c.G, c.B);
            petWheelFade.GradientStops[0].Color = c;
            petWheelFade.GradientStops[1].Color = c;
            petWheelFade.GradientStops[2].Color = clear;
        }

        Grid CreateScrollSurface()
        {
            // Keep text on XAML's native DPI-aware rendering path.
            var host = new Grid(); host.Children.Add(settingsScroll);
            var rail = new Canvas
            {
                Width = 12, Margin = new Thickness(0, 2, 2, 8), HorizontalAlignment = HorizontalAlignment.Right,
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
            compactTitle = new TextBlock { Text = Title, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center, Opacity = 1 };
            header.Children.Add(compactTitle);
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(compactTitle, true);
            var titleVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(compactTitle);
            titleVisual.Properties.InsertVector3("Translation", new System.Numerics.Vector3(0, 40, 0));
            compactTitle.SizeChanged += (_, __) => titleVisual.CenterPoint = new System.Numerics.Vector3(0, (float)compactTitle.ActualHeight / 2, 0);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            void ChooseAppearance(Button button, AppearanceMode mode)
            {
                // The click plays the pop right away; the selection-change pop
                // below covers switches applied later (fade or pending mode).
                appearancePopGuardUntil = Environment.TickCount64 + 260;
                PopSpringElement(button);
                ChangeAppearance(mode);
            }
            autoAppearanceButton = CreateHeaderIconButton("\uE7F4", "自适应：跟随系统日间/夜间", () => ChooseAppearance(autoAppearanceButton, AppearanceMode.Automatic));
            dayAppearanceButton = CreateHeaderIconButton("\uE706", "日间模式", () => ChooseAppearance(dayAppearanceButton, AppearanceMode.Day));
            nightAppearanceButton = CreateHeaderIconButton("\uE708", "夜间模式", () => ChooseAppearance(nightAppearanceButton, AppearanceMode.Night));
            var closeBtn = CreateHeaderIconButton("\uE711", "关闭设置", () => CloseSettings());
            right.Children.Add(autoAppearanceButton); right.Children.Add(dayAppearanceButton); right.Children.Add(nightAppearanceButton); right.Children.Add(closeBtn);
            header.Children.Add(right);
            SetTitleBar(header);
            void UpdateDragRegion()
            {
                if (windowClosed || root.XamlRoot == null || header.ActualHeight <= 0 || right.ActualWidth <= 0) return;
                double dpi = root.XamlRoot.RasterizationScale;
                var origin = header.TransformToVisual(root).TransformPoint(default);
                var buttons = right.TransformToVisual(root).TransformPoint(default);
                // Windows handles caption dragging; the header buttons remain client input.
                native.TitleBar.SetDragRectangles(new[] { new RectInt32(
                    (int)Math.Ceiling(origin.X * dpi), (int)Math.Ceiling(origin.Y * dpi),
                    Math.Max(0, (int)Math.Floor((buttons.X - origin.X) * dpi)),
                    (int)Math.Floor(header.ActualHeight * dpi)) });
                Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(native.Id).SetRegionRects(
                    Microsoft.UI.Input.NonClientRegionKind.Passthrough, new[] { new RectInt32(
                        (int)Math.Floor(buttons.X * dpi), (int)Math.Floor(buttons.Y * dpi),
                        (int)Math.Ceiling((buttons.X + right.ActualWidth) * dpi) - (int)Math.Floor(buttons.X * dpi),
                        (int)Math.Ceiling((buttons.Y + right.ActualHeight) * dpi) - (int)Math.Floor(buttons.Y * dpi)) });
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
            Canvas.SetZIndex(toolbar, 2);
            Grid.SetRow(toolbar, 0); root.Children.Add(toolbar);
        }

        void BuildPetSelector()
        {
            string[] ids = { "cow-cat", "panda", "polar-bear" };
            if (Array.IndexOf(ids, petId) < 0)
            {
                petId = petId == "cat" || petId == "samoyed" || petId == "corgi" || petId == "red-panda" ? "cow-cat" : "polar-bear";
                if (!petCommitted) initialPetId = petId;
            }
            int count = ids.Length, repeats = 7, visibleRows = 3;
            double rowHeight = 66, viewportHeight = rowHeight * visibleRows;
            double padding = (viewportHeight - rowHeight) / 2;
            double IconShift(string id) => id == "polar-bear" ? -3 : 0;

            var petBtn = new Button
            {
                Padding = new Thickness(6, 4, 6, 4),
                CornerRadius = new CornerRadius(14),
                Background = inputBackgroundBrush,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            petBtn.Resources["ButtonBackgroundPointerOver"] = inputBackgroundBrush;
            petBtn.Resources["ButtonBackgroundPressed"] = inputBackgroundBrush;
            var petStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            var petThumbShift = new CompositeTransform { TranslateX = IconShift(petId) };
            petThumb = new Image
            {
                Width = 36, Height = 36, Source = PetImage(petId),
                RenderTransform = petThumbShift, RenderTransformOrigin = new Windows.Foundation.Point(.5, .5)
            };
            var petArrow = CreateChevron(90);
            petArrow.Width = petArrow.Height = 16;
            petStack.Children.Add(petThumb); petStack.Children.Add(petArrow);
            petBtn.Content = petStack;

            const double petIconRestScale = 1, petIconSelectedScale = 1.32;
            var petImages = new Dictionary<string, BitmapImage>();
            foreach (string id in ids) petImages[id] = PetImage(id);

            var wheelRows = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            wheelRows.Children.Add(new Border { Height = padding });
            var rowBorders = new List<Border>();
            var rowScales = new List<CompositeTransform>();
            var rowPops = new List<Grid>();
            var wheelViewer = new ScrollViewer
            {
                Height = viewportHeight, Content = wheelRows,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
                VerticalSnapPointsType = SnapPointsType.None,
                VerticalSnapPointsAlignment = Microsoft.UI.Xaml.Controls.Primitives.SnapPointsAlignment.Center,
                IsTabStop = true, UseSystemFocusVisuals = false
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(wheelViewer, "桌面宠物选择");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(wheelViewer, "PetWheel");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(wheelViewer, "上下滚动选择宠物，点击宠物或设置页空白处完成选择");
            Action<int>? finishRow = null;
            for (int index = 0; index < count * repeats; index++)
            {
                int wheelIndex = index;
                string id = ids[index % count];
                var iconScale = new CompositeTransform
                {
                    ScaleX = 1, ScaleY = 1,
                    // Source art is not always optically centred inside its square canvas.
                    TranslateX = IconShift(id)
                };
                // The outer grid carries the Q弹 pop so it never fights the
                // proximity scale used while the wheel is scrolling.
                var popHost = new Grid { Width = 60, Height = 60, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                popHost.Children.Add(new Image
                {
                    Source = petImages[id], Width = 38, Height = 44,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransform = iconScale,
                    RenderTransformOrigin = new Windows.Foundation.Point(.5, .5)
                });
                var row = new Border { Height = rowHeight, Child = popHost, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Opacity = 0 };
                row.Tapped += (_, __) =>
                {
                    wheelViewer.ChangeView(null, wheelIndex * rowHeight, null, false);
                    finishRow?.Invoke(wheelIndex);
                };
                rowBorders.Add(row); rowPops.Add(popHost); wheelRows.Children.Add(row);
                rowScales.Add(iconScale);
            }
            wheelRows.Children.Add(new Border { Height = padding });

            // The hover-effect selector also uses this fade brush.
            if (petWheelFade.GradientStops.Count == 0)
            {
                petWheelFade.Center = new Windows.Foundation.Point(.5, .5);
                petWheelFade.GradientOrigin = new Windows.Foundation.Point(.5, .5);
                petWheelFade.RadiusX = .46;
                petWheelFade.RadiusY = .46;
                petWheelFade.GradientStops.Add(new GradientStop { Offset = 0 });
                petWheelFade.GradientStops.Add(new GradientStop { Offset = .5 });
                petWheelFade.GradientStops.Add(new GradientStop { Offset = 1 });
                UpdatePetWheelFade();
            }

            bool initialized = false, wheelOpen = false;
            double? positioningOffset = null;
            int wheelTargetIndex = -1;
            bool wheelSpringing = false;
            double wheelSpringPos = 0, wheelSpringVel = 0, wheelSpringGoal = 0;
            long wheelSpringLast = 0;
            int previousFirst = 0, previousLast = -1;
            var wheelHost = new Grid
            {
                Height = viewportHeight,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
            };
            var selection = new Border
            {
                Height = rowHeight, VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(18), IsHitTestVisible = false
            };
            wheelHost.Children.Add(selection);
            wheelHost.Children.Add(wheelViewer);
            wheelHost.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, args) =>
            {
                int delta = args.GetCurrentPoint(wheelViewer).Properties.MouseWheelDelta;
                if (delta == 0) return;
                int direction = delta > 0 ? -1 : 1;
                StepWheel(direction);
                args.Handled = true;
            }), true);

            void TraceWheel(string tag)
            {
                if (!GlassProfile.Enabled) return;
                int center = (int)Math.Round((positioningOffset ?? wheelViewer.VerticalOffset) / rowHeight);
                int visibleRow = Math.Clamp(center, 0, rowBorders.Count - 1);
                GlassProfile.Event($"pet-wheel {tag} extent={wheelViewer.ExtentHeight:F1} viewport={wheelViewer.ViewportHeight:F1} offset={wheelViewer.VerticalOffset:F1} actual={wheelRows.ActualHeight:F1} desired={wheelRows.DesiredSize.Height:F1} initialized={initialized} open={wheelOpen} positioning={(positioningOffset.HasValue ? positioningOffset.Value.ToString("F1") : "null")} rows={rowBorders.Count} loaded={rowBorders.Count(r => r.IsLoaded)} center={center} centerOpacity={rowBorders[visibleRow].Opacity:F2} firstOpacity={rowBorders[0].Opacity:F2}");
            }
            wheelViewer.PointerPressed += (_, __) => wheelTargetIndex = -1;
            void ApplyPet(string id)
            {
                if (id == petId) return;
                petId = id;
                petThumb.Source = petImages[id];
                petThumbShift.TranslateX = IconShift(id);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(petBtn, "当前：" + PetName(id));
            }
            void UpdateWheelSelection()
            {
                double position = wheelViewer.VerticalOffset / rowHeight;
                int index = Math.Clamp((int)Math.Round(position), 0, count * repeats - 1);
                if (wheelOpen && positioningOffset == null) ApplyPet(ids[index % count]);
                for (int i = previousFirst; i <= previousLast; i++)
                {
                    rowBorders[i].Opacity = 0;
                    rowScales[i].ScaleX = rowScales[i].ScaleY = 1;
                }
                int first = Math.Max(0, (int)Math.Floor(position) - 3);
                int last = Math.Min(rowBorders.Count - 1, (int)Math.Ceiling(position) + 3);
                for (int i = first; i <= last; i++)
                {
                    double distance = Math.Abs(i - position);
                    rowBorders[i].Opacity = Math.Max(0, 1 - distance * .42);
                    double proximity = Math.Max(0, 1 - distance);
                    double eased = proximity * proximity * (3 - 2 * proximity);
                    rowScales[i].ScaleX = rowScales[i].ScaleY =
                        petIconRestScale + (petIconSelectedScale - petIconRestScale) * eased;
                }
                previousFirst = first; previousLast = last;
            }
            void StepWheel(int direction)
            {
                int center = (int)Math.Round(wheelViewer.VerticalOffset / rowHeight);
                if (wheelOpen && center >= 0 && center < rowBorders.Count) PopPetIcon(center);
                positioningOffset = null;
                if (!wheelSpringing)
                {
                    wheelSpringing = true;
                    wheelSpringPos = wheelViewer.VerticalOffset / rowHeight;
                    wheelSpringVel = 0;
                    wheelSpringGoal = wheelSpringPos;
                    wheelSpringLast = Stopwatch.GetTimestamp();
                    CompositionTarget.Rendering += AnimateWheelSpring;
                }
                wheelSpringGoal = Math.Clamp(wheelSpringGoal + direction, 0, count * repeats - 1);
                wheelTargetIndex = -1;
            }
            void AnimateWheelSpring(object? sender, object e)
            {
                if (!wheelSpringing) return;
                if (windowClosed || !wheelOpen)
                {
                    wheelSpringing = false;
                    CompositionTarget.Rendering -= AnimateWheelSpring;
                    return;
                }
                long now = Stopwatch.GetTimestamp();
                double dt = Math.Min(.032, (now - wheelSpringLast) / (double)Stopwatch.Frequency);
                wheelSpringLast = now;
                // Critically damped spring keeps the wheel gliding between rows
                // instead of restarting the ScrollViewer's stepped animation.
                const double omega = 14;
                for (double left = dt; left > 0;)
                {
                    double step = Math.Min(left, .004);
                    double accel = omega * omega * (wheelSpringGoal - wheelSpringPos) - 2 * omega * wheelSpringVel;
                    wheelSpringVel += accel * step;
                    wheelSpringPos += wheelSpringVel * step;
                    left -= step;
                }
                if (Math.Abs(wheelSpringGoal - wheelSpringPos) < .0008 && Math.Abs(wheelSpringVel) < .02)
                {
                    wheelSpringPos = wheelSpringGoal; wheelSpringVel = 0; wheelSpringing = false;
                    CompositionTarget.Rendering -= AnimateWheelSpring;
                    wheelViewer.ChangeView(null, wheelSpringPos * rowHeight, null, true);
                    UpdateWheelSelection();
                    int settled = (int)Math.Round(wheelViewer.VerticalOffset / rowHeight);
                    if (settled < count || settled >= count * (repeats - 1))
                        PositionWheel(count * 3 + settled % count);
                    return;
                }
                wheelViewer.ChangeView(null, wheelSpringPos * rowHeight, null, true);
                UpdateWheelSelection();
            }
            void PopPetIcon(int wheelIndex)
            {
                if (wheelIndex < 0 || wheelIndex >= rowPops.Count || !animationSettings.AnimationsEnabled) return;
                if (GlassProfile.Enabled) GlassProfile.Event($"pet-pop {wheelIndex}");
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(rowPops[wheelIndex]);
                visual.CenterPoint = new System.Numerics.Vector3(30, 30, 0);
                var spring = visual.Compositor.CreateSpringVector3Animation();
                spring.DampingRatio = .3f;
                spring.Period = TimeSpan.FromMilliseconds(190);
                spring.InitialValue = new System.Numerics.Vector3(1.10f, 1.10f, 1);
                spring.FinalValue = new System.Numerics.Vector3(1, 1, 1);
                visual.StartAnimation("Scale", spring);
            }
            void PositionWheel(int index)
            {
                // Opening/recentering is bookkeeping, not a user selection gesture.
                wheelTargetIndex = -1;
                double target = index * rowHeight;
                positioningOffset = target;
                wheelViewer.ChangeView(null, target, null, true);
                if (Math.Abs(wheelViewer.VerticalOffset - target) <= .1) positioningOffset = null;
            }
            EventHandler<object>? initialize = null;
            initialize = (_, __) =>
            {
                TraceWheel("layout");
                if (wheelViewer.ExtentHeight <= viewportHeight) return;
                initialized = true;
                wheelViewer.LayoutUpdated -= initialize;
                PositionWheel(count * 3 + Math.Max(0, Array.IndexOf(ids, petId)));
                UpdateWheelSelection();
                TraceWheel("initialized");
            };
            wheelViewer.LayoutUpdated += initialize;
            wheelViewer.ViewChanged += (_, args) =>
            {
                if (!initialized || !wheelOpen) return;
                if (wheelSpringing) return;
                if (positioningOffset is double target)
                {
                    if (Math.Abs(wheelViewer.VerticalOffset - target) > .1) return;
                    positioningOffset = null;
                }
                UpdateWheelSelection();
                if (!args.IsIntermediate)
                {
                    if (wheelTargetIndex >= 0)
                    {
                        // A new wheel step can interrupt an earlier animation. Wait
                        // for the latest target instead of snapping the old offset.
                        if (Math.Abs(wheelViewer.VerticalOffset - wheelTargetIndex * rowHeight) > .1) return;
                        wheelTargetIndex = -1;
                    }
                    int index = (int)Math.Round(wheelViewer.VerticalOffset / rowHeight);
                    if (Math.Abs(wheelViewer.VerticalOffset - index * rowHeight) > .1)
                    {
                        wheelViewer.ChangeView(null, index * rowHeight, null, false);
                        return;
                    }
                    if (index < count || index >= count * (repeats - 1))
                        PositionWheel(count * 3 + index % count);
                }
            };
            wheelViewer.KeyDown += (_, args) =>
            {
                int step = args.Key == Windows.System.VirtualKey.Up ? -1 : args.Key == Windows.System.VirtualKey.Down ? 1 : 0;
                if (step == 0) return;
                StepWheel(step);
                args.Handled = true;
            };

            var panel = new StackPanel { Width = 108 };
            panel.Children.Add(wheelHost);
            var focusSink = new Button
            {
                Width = 1, Height = 1, Opacity = 0, IsTabStop = true,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(focusSink,
                Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            var flyoutRoot = new Grid();
            flyoutRoot.Children.Add(panel);
            flyoutRoot.Children.Add(focusSink);

            var petMenu = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Left };
            var menuSurface = new Border
            {
                CornerRadius = new CornerRadius(16), Padding = new Thickness(5),
                Child = flyoutRoot
            };
            petMenu.Content = menuSurface;
            var menuStyle = new Style(typeof(FlyoutPresenter));
            menuStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            menuStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(16)));
            menuStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            menuStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            petMenu.FlyoutPresenterStyle = menuStyle;
            finishRow = wheelIndex =>
            {
                ApplyPet(ids[wheelIndex % count]);
                petMenu.Hide();
            };

            bool petKeyboardOpen = false;
            petMenu.Opening += (_, __) =>
            {
                wheelOpen = false;
                petKeyboardOpen = petBtn.FocusState == FocusState.Keyboard;
                panel.RequestedTheme = root.ActualTheme;
                bool dark = root.ActualTheme == ElementTheme.Dark;
                menuSurface.Background = MenuFrost(dark);
                menuSurface.BorderThickness = new Thickness(1);
                menuSurface.BorderBrush = new SolidColorBrush(dark
                    ? Windows.UI.Color.FromArgb(30, 255, 255, 255)
                    : Windows.UI.Color.FromArgb(90, 255, 255, 255));
                selection.Background = new SolidColorBrush(root.ActualTheme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(255, 58, 58, 60)
                    : Windows.UI.Color.FromArgb(255, 239, 240, 244));
            };
            petMenu.Closing += (_, __) =>
            {
                wheelOpen = false; positioningOffset = null; wheelTargetIndex = -1;
                if (wheelSpringing)
                {
                    wheelSpringing = false;
                    CompositionTarget.Rendering -= AnimateWheelSpring;
                }
            };
            petMenu.Opened += (_, __) =>
            {
                wheelOpen = true;
                TraceWheel("opened");
                PositionWheel(count * 3 + Math.Max(0, Array.IndexOf(ids, petId)));
                // The content can finish its first measure before Opened and jump
                // straight to the target offset. ViewChanged is suppressed while
                // wheelOpen is false, so publish the row opacity here as well.
                UpdateWheelSelection();
                focusSink.Focus(petKeyboardOpen ? FocusState.Keyboard : FocusState.Pointer);
                wheelViewer.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    TraceWheel("opened-low");
                    if (wheelOpen) UpdateWheelSelection();
                });
            };
            focusSink.KeyDown += (_, args) =>
            {
                int step = args.Key == Windows.System.VirtualKey.Up ? -1
                    : args.Key == Windows.System.VirtualKey.Down ? 1 : 0;
                if (step != 0)
                {
                    StepWheel(step);
                    args.Handled = true;
                }
                else if (args.Key == Windows.System.VirtualKey.Escape)
                {
                    petMenu.Hide();
                    args.Handled = true;
                }
            };
            void ShowPetMenu()
            {
                // Prefer a vertically centered flyout to the left of the selector;
                // if that would cross the fixed footer, align its bottom instead.
                double popupHeight = viewportHeight + menuSurface.Padding.Top + menuSurface.Padding.Bottom;
                double anchorCenter = 0, footerTop = root.ActualHeight;
                try
                {
                    var origin = petBtn.TransformToVisual(root).TransformPoint(default);
                    anchorCenter = origin.Y + petBtn.ActualHeight / 2;
                    if (footerBar != null)
                        footerTop = footerBar.TransformToVisual(root).TransformPoint(default).Y;
                }
                catch (ArgumentException)
                {
                }
                bool centeredFits = anchorCenter + popupHeight / 2 <= footerTop - 4;
                petMenu.Placement = centeredFits
                    ? Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Left
                    : Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.LeftEdgeAlignedBottom;
                petMenu.ShowAt(petBtn);
            }
            petBtn.Click += (_, __) => { if (petMenu.IsOpen) petMenu.Hide(); else ShowPetMenu(); };
            ToolTipService.SetToolTip(petBtn, "切换桌面宠物");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(petBtn, "桌面宠物");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(petBtn, "PetSelector");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(petBtn, "当前：" + PetName(petId));
            petSelector = petBtn;
        }

        void BindTitleTransition()
        {
            titleCompactProgress = titleCompactTarget = TitleCompactTarget();
            titleCompactVelocity = 0;
            ApplyTitleProgress(titleCompactProgress);
            settingsScroll.ViewChanged += (_, __) => UpdateTitleSpringTarget();
            settingsScroll.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (windowClosed) return;
                titleCompactProgress = titleCompactTarget = TitleCompactTarget();
                titleCompactVelocity = 0;
                ApplyTitleProgress(titleCompactProgress);
            });
        }

        double TitleCompactTarget() => settingsScroll == null ? 0 : Math.Clamp(settingsScroll.VerticalOffset / 56d, 0, 1);

        void UpdateTitleSpringTarget()
        {
            if (windowClosed) return;
            double next = TitleCompactTarget();
            titleCompactTarget = next;
            if (!animationSettings.AnimationsEnabled)
            {
                CompositionTarget.Rendering -= AnimateTitleSpring;
                titleSpringRunning = false;
                titleCompactProgress = next;
                titleCompactVelocity = 0;
                ApplyTitleProgress(next);
                return;
            }
            StartTitleSpring();
        }

        void StartTitleSpring()
        {
            if (windowClosed || titleSpringRunning) return;
            titleSpringRunning = true;
            titleSpringLastTick = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += AnimateTitleSpring;
        }

        void AnimateTitleSpring(object? sender, object args)
        {
            if (windowClosed)
            {
                CompositionTarget.Rendering -= AnimateTitleSpring;
                titleSpringRunning = false;
                return;
            }
            long now = Stopwatch.GetTimestamp();
            double dt = (now - titleSpringLastTick) / (double)Stopwatch.Frequency;
            titleSpringLastTick = now;
            dt = Math.Clamp(dt, .001, .032);
            int steps = Math.Max(1, (int)Math.Ceiling(dt / .008));
            double step = dt / steps;
            const double stiffness = 240;
            const double damping = 18;
            for (int i = 0; i < steps; i++)
            {
                double acceleration = stiffness * (titleCompactTarget - titleCompactProgress) - damping * titleCompactVelocity;
                titleCompactVelocity += acceleration * step;
                titleCompactProgress += titleCompactVelocity * step;
            }
            if (Math.Abs(titleCompactTarget - titleCompactProgress) < .001 && Math.Abs(titleCompactVelocity) < .01)
            {
                titleCompactProgress = titleCompactTarget;
                titleCompactVelocity = 0;
                ApplyTitleProgress(titleCompactProgress);
                CompositionTarget.Rendering -= AnimateTitleSpring;
                titleSpringRunning = false;
                return;
            }
            ApplyTitleProgress(titleCompactProgress);
        }

        void ApplyTitleProgress(double progress)
        {
            // Both ends settle with the same gentle bounce: the expanded end dips
            // down, the compact end lifts slightly. Cap the size overshoot so the
            // text never balloons or looks like it is being squashed.
            double display = Math.Clamp(progress, -.12, 1.10);
            double scaleProgress = Math.Clamp(progress, -.03, 1.04);
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(compactTitle);
            float scale = (float)(1 - scaleProgress / 3);
            visual.Scale = new System.Numerics.Vector3(scale, scale, 1);
            visual.Properties.InsertVector3("Translation", new System.Numerics.Vector3(0, (float)(40 * (1 - display)), 0));
        }

        void UpdateScrollChrome()
        {
            compactTitle.Opacity = 1;
            UpdateHeaderTransition();
            if (backToTop == null) return;
            // Any scroll reveals the arrow at full size; the bounce comes from the
            // animation, not from tracking the scroll offset.
            double target = settingsScroll.VerticalOffset > 4 ? 1 : 0;
            if (!animationSettings.AnimationsEnabled)
            {
                footerChromeProgress = footerChromeStart = footerChromeTarget = target;
                ApplyFooterChrome(target);
                return;
            }
            if (target == footerChromeTarget) return;
            footerChromeStart = footerChromeProgress;
            footerChromeTarget = target;
            footerChromeClock.Restart();
            if (!animatingFooterChrome) { animatingFooterChrome = true; CompositionTarget.Rendering += AnimateFooterChrome; }
            AnimateFooterChrome(null, null!);
        }

        static double EaseOutBack(double t)
        {
            const double c1 = 1.70158, c3 = c1 + 1;
            double u = t - 1;
            return 1 + c3 * u * u * u + c1 * u * u;
        }

        void AnimateFooterChrome(object? sender, object args)
        {
            double position = Math.Clamp(footerChromeClock.Elapsed.TotalMilliseconds / 300, 0, 1);
            double eased = EaseOutBack(position);
            footerChromeProgress = footerChromeStart + (footerChromeTarget - footerChromeStart) * eased;
            ApplyFooterChrome(footerChromeProgress);
            if (position >= 1)
            {
                footerChromeProgress = footerChromeTarget;
                ApplyFooterChrome(footerChromeProgress);
                CompositionTarget.Rendering -= AnimateFooterChrome;
                footerChromeClock.Stop();
                animatingFooterChrome = false;
            }
        }

        void ApplyFooterChrome(double value)
        {
            if (backToTop == null || footerCancel == null) return;
            double visible = Math.Clamp(value, 0, 1);
            footerCancel.Margin = new Thickness(0, 0, Math.Max(0, 46 * value), 0);
            footerArrowScale.ScaleX = footerArrowScale.ScaleY = Math.Max(0, value);
            backToTop.Opacity = visible;
            bool on = visible > 0;
            backToTop.IsEnabled = on;
            backToTop.IsHitTestVisible = on;
            backToTop.IsTabStop = on;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(backToTop,
                on ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }

        void UpdateHeaderTransition()
        {
            double progress = Math.Clamp((settingsScroll?.VerticalOffset ?? 0) / 24, 0, 1);
            headerFadeLayer.Opacity = progress;
            headerBackgroundBrush.Opacity = progress;
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
                Content = new FontIcon { Glyph = glyph, FontSize = 16, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = subInkBrush }
            };
            btn.Click += (_, __) => onClick();
            ToolTipService.SetToolTip(btn, tooltip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, tooltip);
            return btn;
        }

        void PopSpringElement(FrameworkElement? element)
        {
            if (element == null || windowClosed || !animationSettings.AnimationsEnabled) return;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint = new System.Numerics.Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
            var spring = visual.Compositor.CreateSpringVector3Animation();
            spring.DampingRatio = .32f;
            spring.Period = TimeSpan.FromMilliseconds(220);
            spring.InitialValue = new System.Numerics.Vector3(1.18f, 1.18f, 1);
            spring.FinalValue = new System.Numerics.Vector3(1, 1, 1);
            visual.StartAnimation("Scale", spring);
        }

        void BuildWorkingHoursContent()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "默认工作时间", FontSize = 13, Foreground = inkBrush, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = "工作日默认使用此时间。", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
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
            var lunchSection = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            lunchSection.Children.Add(new Border { Height = 1, Background = dividerBrush, Margin = new Thickness(0, 0, 0, 4), IsHitTestVisible = false });
            lunchSection.Children.Add(new TextBlock { Text = "午休", FontSize = 13, Foreground = inkBrush });
            var lunchTimes = new Grid { ColumnSpacing = 12 };
            lunchTimes.ColumnDefinitions.Add(new ColumnDefinition()); lunchTimes.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 2; i++)
            {
                bool first = i == 0;
                var column = new StackPanel { Spacing = 4 };
                column.Children.Add(new TextBlock { Text = first ? "开始" : "结束", FontSize = 12, Foreground = subInkBrush });
                var display = new TextBlock { Text = FormatTime(first ? lunchStart : lunchEnd), FontSize = 17, FontFamily = new FontFamily("Segoe UI"), Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var button = new Button { Height = 36, CornerRadius = new CornerRadius(8), Background = inputBackgroundBrush, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Content = display };
                button.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush; button.Resources["ButtonBackgroundPressed"] = timePressedBrush;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, first ? "午休开始时间" : "午休结束时间");
                button.Click += (_, __) => ShowTimeWheelCore(button, display, false, false, null, 0,
                    value => { if (first) lunchStart = value; else lunchEnd = value; }, first ? lunchStart : lunchEnd);
                column.Children.Add(button); Grid.SetColumn(column, i); lunchTimes.Children.Add(column);
            }
            lunchSection.Children.Add(lunchTimes);
            panel.Children.Add(lunchSection);
            dayTimeSection = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            dayTimeSection.Children.Add(new Border { Height = 1, Background = dividerBrush, Margin = new Thickness(0, 6, 0, 4), IsHitTestVisible = false });
            var dayHeader = new Grid { ColumnSpacing = 8 };
            dayHeader.ColumnDefinitions.Add(new ColumnDefinition()); dayHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); dayHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dayHeader.Children.Add(new TextBlock { Text = "按星期单独设置", FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            dayTimeSummary = new TextBlock { FontSize = 12, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dayTimeSummary, 1); dayHeader.Children.Add(dayTimeSummary);
            dayTimeChevron = CreateChevron();
            var dayHeaderButton = new Button { Content = dayHeader, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var dayArrowSurface = CreateChevronSurface(dayHeaderButton, dayTimeChevron);
            Grid.SetColumn(dayArrowSurface, 2); dayHeader.Children.Add(dayArrowSurface);
            dayHeaderButton.Resources["ButtonBackgroundPointerOver"] = dayHeaderButton.Background;
            dayHeaderButton.Resources["ButtonBackgroundPressed"] = dayHeaderButton.Background;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dayHeaderButton, "按星期设置时段，展开或收起");
            dayHeaderButton.Click += (_, __) => { dayTimeExpanded = !dayTimeExpanded; RefreshDayTimeEditor(); };
            dayTimeSection.Children.Add(dayHeaderButton);
            dayTimeEditor = new StackPanel();
            var dayRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            dayRow.ColumnDefinitions.Add(new ColumnDefinition()); dayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dayTimeDay = new ComboBox { Height = 34, MinWidth = 112, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dayTimeDay, "选择需要设置时段的星期");
            dayTimeDay.SelectionChanged += (_, __) => { if (loadingSchedule || dayTimeDay.SelectedItem is not ComboBoxItem item || item.Tag is not int day) return; RememberDayTime(); editingDay = day; LoadDayTime(); RefreshDayTimeEditor(); };
            dayRow.Children.Add(dayTimeDay);
            dayTimeSwitch = CreateGreenSwitch(dayTimeEnabled);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dayTimeSwitch, "所选星期使用单独时间，关闭后使用默认工作时间");
            dayTimeSwitch.Toggled += (_, __) => { if (loadingSchedule) return; dayTimeEnabled = dayTimeSwitch.IsOn; RememberDayTime(); RefreshDayTimeEditor(); };
            Grid.SetColumn(dayTimeSwitch, 1); dayRow.Children.Add(dayTimeSwitch); dayTimeEditor.Children.Add(dayRow);
            dayTimeTimes = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 8) };
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
            dayTimeTimesReveal = new Border { Child = dayTimeTimes, Visibility = Visibility.Collapsed };
            dayTimeEditor.Children.Add(dayTimeTimesReveal);
            dayTimeEditor.Children.Add(new TextBlock { Text = "开启后，这一天使用单独时间。", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });
            dayTimeSection.Children.Add(dayTimeEditor);
            dayTimeReveal = new Border { Child = dayTimeSection, Visibility = Visibility.Collapsed, Opacity = 0 };
            panel.Children.Add(dayTimeReveal);
            RefreshScheduleEditor();
            workingHoursContent = panel;
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
            int restMask = scheduleMode == "alternating" ? AlternatingWeekMask()
                : weekdays.Count == 7 ? weekdays.Select((d, i) => d.IsChecked == true ? 1 << i : 0).Sum() : current.RestDays;
            return (restMask & (1 << day)) == 0;
        }

        void RefreshDayTimeEditor()
        {
            if (dayTimeSection == null || dayTimeTimes == null) return;
            if (scheduleMode == "double") { UpdateDayTimeReveal(false); return; }
            var available = Enumerable.Range(0, 7).Where(CanSetDayTime).OrderBy(d => (d + 6) % 7).ToList();
            if (scheduleMode != "double" && available.Count > 0 && !CanSetDayTime(editingDay))
            { RememberDayTime(); editingDay = available.OrderBy(d => (d + 6) % 7).First(); LoadDayTime(); }
            bool selectable = available.Count > 0 && CanSetDayTime(editingDay);
            dayTimeTimesReveal.IsHitTestVisible = selectable && dayTimeEnabled;
            int count = dayTimes.Count(d => d.Enabled && CanSetDayTime(d.Day));
            dayTimeSummary.Text = available.Count == 0 ? "无每周工作日" : count == 0 ? "按需设置" : count + " 天已设置";
            loadingSchedule = true;
            try
            {
                if (!dayTimeDay.Items.Cast<ComboBoxItem>().Select(item => (int)item.Tag).SequenceEqual(available))
                {
                    dayTimeDay.Items.Clear();
                    string[] names = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
                    foreach (int day in available)
                        dayTimeDay.Items.Add(new ComboBoxItem { Content = names[day], Tag = day });
                }
                dayTimeDay.IsEnabled = available.Count > 0;
                dayTimeDay.SelectedIndex = selectable ? available.IndexOf(editingDay) : -1;
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
            double contentWidth = Math.Max(1, dayTimeSection.ActualWidth > 0 ? dayTimeSection.ActualWidth : form.ActualWidth - 32);
            double previousTimesFrom = dayTimeTimesFrom, previousTimesOpacity = dayTimeTimesOpacityFrom;
            dayTimeTimesFrom = dayTimeTimesReveal.Visibility == Visibility.Collapsed ? 0
                : double.IsNaN(dayTimeTimesReveal.Height) ? dayTimeTimesReveal.ActualHeight : dayTimeTimesReveal.Height;
            dayTimeTimesOpacityFrom = dayTimeTimesReveal.Visibility == Visibility.Collapsed ? 0 : dayTimeTimesReveal.Opacity;
            dayTimeTimes.Measure(new Windows.Foundation.Size(contentWidth, double.PositiveInfinity));
            dayTimeTimesTo = CanSetDayTime(editingDay) && dayTimeEnabled ? dayTimeTimes.DesiredSize.Height : 0;
            dayTimeTimesReveal.Visibility = Visibility.Visible;
            dayTimeTimesReveal.Height = dayTimeTimesTo;
            double fullHeight = double.IsNaN(dayTimeSection.Height) ? dayTimeSection.ActualHeight : dayTimeSection.Height;
            if (visible)
            {
                if (dayTimeEditor.Visibility == Visibility.Collapsed) dayTimeEditor.Opacity = 0;
                dayTimeEditor.Visibility = Visibility.Visible;
                dayTimeSection.Height = double.NaN;
                dayTimeSection.Measure(new Windows.Foundation.Size(contentWidth, double.PositiveInfinity));
                fullHeight = dayTimeSection.DesiredSize.Height;
            }
            double headerHeight = ((FrameworkElement)dayTimeSection.Children[0]).DesiredSize.Height
                + ((FrameworkElement)dayTimeSection.Children[1]).DesiredSize.Height + dayTimeSection.Spacing
                + dayTimeSection.Margin.Top + dayTimeSection.Margin.Bottom;
            double height = visible ? dayTimeExpanded ? fullHeight : headerHeight : 0, opacity = visible ? 1 : 0;
            double editorOpacity = visible && dayTimeExpanded ? 1 : 0;
            dayTimeEditor.IsHitTestVisible = editorOpacity > 0;
            double angle = dayTimeExpanded ? 90 : 0;
            if (animatingDayTimeReveal && Math.Abs(height - dayTimeRevealTargetHeight) < .5 && opacity == dayTimeRevealTargetOpacity && angle == dayTimeArrowTarget && editorOpacity == dayTimeEditorTargetOpacity)
            {
                dayTimeTimesReveal.Height = dayTimeTimesFrom;
                dayTimeTimesFrom = previousTimesFrom; dayTimeTimesOpacityFrom = previousTimesOpacity;
                dayTimeSection.Height = Math.Max(0, fullHeight - dayTimeSection.Margin.Top - dayTimeSection.Margin.Bottom);
                return;
            }
            double currentHeight = animatingDayTimeReveal ? dayTimeRevealHeight : dayTimeReveal.Visibility == Visibility.Collapsed ? 0 : dayTimeReveal.ActualHeight;
            dayTimeRevealStartHeight = currentHeight; dayTimeRevealTargetHeight = height;
            dayTimeRevealStartOpacity = dayTimeReveal.Opacity; dayTimeRevealTargetOpacity = opacity;
            dayTimeEditorStartOpacity = dayTimeEditor.Opacity; dayTimeEditorTargetOpacity = editorOpacity;
            dayTimeArrowStart = (dayTimeChevron.RenderTransform as RotateTransform)?.Angle ?? 0; dayTimeArrowTarget = angle;
            dayTimeReveal.IsHitTestVisible = visible;
            if (!root.IsLoaded || !animationSettings.AnimationsEnabled || (Math.Abs(height - currentHeight) < .5 && Math.Abs(opacity - dayTimeReveal.Opacity) < .01 && Math.Abs(editorOpacity - dayTimeEditor.Opacity) < .01))
            { FinishDayTimeReveal(); return; }
            dayTimeSection.Height = Math.Max(0, fullHeight - dayTimeSection.Margin.Top - dayTimeSection.Margin.Bottom);
            dayTimeReveal.Visibility = Visibility.Visible;
            dayTimeRevealClock.Restart();
            if (!animatingDayTimeReveal) { animatingDayTimeReveal = true; CompositionTarget.Rendering += AnimateDayTimeReveal; }
            AnimateDayTimeReveal(null, null!);
        }

        void AnimateDayTimeReveal(object? sender, object args)
        {
            double position = animationSettings.AnimationsEnabled ? Math.Clamp(dayTimeRevealClock.Elapsed.TotalMilliseconds / 300, 0, 1) : 1;
            double eased = position * position * (3 - 2 * position);
            dayTimeRevealHeight = dayTimeRevealStartHeight + (dayTimeRevealTargetHeight - dayTimeRevealStartHeight) * eased;
            double opacity = dayTimeRevealStartOpacity + (dayTimeRevealTargetOpacity - dayTimeRevealStartOpacity) * eased;
            dayTimeReveal.Height = dayTimeRevealHeight; dayTimeReveal.Opacity = opacity;
            dayTimeEditor.Opacity = dayTimeEditorStartOpacity + (dayTimeEditorTargetOpacity - dayTimeEditorStartOpacity) * eased;
            double timesHeight = dayTimeTimesFrom + (dayTimeTimesTo - dayTimeTimesFrom) * eased;
            dayTimeTimesReveal.Height = timesHeight;
            dayTimeTimesReveal.Opacity = dayTimeTimesOpacityFrom + ((dayTimeTimesTo > 0 ? 1 : 0) - dayTimeTimesOpacityFrom) * eased;
            dayTimeTimesClip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, dayTimeSection.ActualWidth), Math.Max(0, timesHeight));
            dayTimeTimesReveal.Clip = dayTimeTimesClip;
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
            dayTimeTimesReveal.Visibility = dayTimeTimesTo > 0 ? Visibility.Visible : Visibility.Collapsed;
            dayTimeTimesReveal.Height = double.NaN; dayTimeTimesReveal.Clip = null;
            dayTimeTimesReveal.Opacity = dayTimeTimesTo > 0 ? 1 : 0;
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
        }

        void SwitchScheduleMode(string mode)
        {
            if (scheduleMode == mode) return;
            RememberSchedule(); LoadSchedule(mode);
        }

        void RefreshScheduleEditor()
        {
            if (dayTimeTimes == null) return;
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
            string normalHint = isDuration ? "显示时长 · 分:秒" : applyMinutes != null ? "提醒间隔 · 时:分" : "";
            var wheelHint = new TextBlock { Text = normalHint, FontSize = 13, Foreground = subInkBrush, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Visibility = normalHint.Length == 0 ? Visibility.Collapsed : Visibility.Visible };
            int hourCount = isDuration ? 1441 : applyMinutes != null ? 13 : 24;
            int totalMinimum = applyMinutes != null ? 1 : 0;
            int totalMaximum = isDuration ? 86400 : applyMinutes != null ? 720 : 1439;
            bool usingInput = false, syncingInput = false;
            bool editingFirst = false, editingSecond = false, editedInput = false;
            Action hideEditors = () => { };
            Action<TextBox> hideEditor = _ => { };
            var selectedBackground = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 58, 58, 60) : Windows.UI.Color.FromArgb(255, 239, 240, 244));
            TextBox CreateSelectedInput(int value, int maximumLength, string name)
            {
                var input = new TextBox
                {
                    Text = value.ToString("00", CultureInfo.InvariantCulture), MaxLength = maximumLength,
                    Height = wheelRowHeight, MinHeight = 0, MinWidth = 0,
                    FontFamily = new FontFamily("Segoe UI Variable Display"), FontSize = compact ? 20 : 23,
                    TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(12, 0, 12, 0), Padding = new Thickness(0),
                    Background = selectedBackground, Foreground = new SolidColorBrush(foreground),
                    SelectionHighlightColor = selectedBackground,
                    SelectionHighlightColorWhenNotFocused = selectedBackground,
                    BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
                    IsSpellCheckEnabled = false, IsTextPredictionEnabled = false, UseSystemFocusVisuals = false,
                    InputScope = new InputScope { Names = { new InputScopeName { NameValue = InputScopeNameValue.Number } } }
                };
                input.Resources["TextControlBackgroundPointerOver"] = selectedBackground;
                input.Resources["TextControlBackgroundFocused"] = selectedBackground;
                input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, name);
                input.GotFocus += (_, __) => input.SelectAll();
                input.Loaded += (_, __) =>
                {
                    input.ApplyTemplate();
                    var pending = new Queue<DependencyObject>(); pending.Enqueue(input);
                    while (pending.Count > 0)
                    {
                        var node = pending.Dequeue();
                        if (node is ScrollViewer content && content.Name == "ContentElement")
                        { content.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumnSpan(content, 2); }
                        if (node is FrameworkElement clear && clear.Name == "DeleteButton")
                        {
                            clear.Width = clear.MinWidth = clear.MaxWidth = 0;
                            clear.IsHitTestVisible = false;
                        }
                        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Enqueue(VisualTreeHelper.GetChild(node, i));
                    }
                };
                return input;
            }
            var firstInput = CreateSelectedInput(hour, isDuration ? 4 : 2, isDuration ? "分钟" : "小时");
            var secondInput = CreateSelectedInput(minute, 2, isDuration ? "秒" : "分钟");
            Button? confirm = null;
            bool TrySelection(out int first, out int second)
            {
                // An empty half means the wheel still shows a value; validate
                // what the user sees instead of an incomplete "10:" string.
                first = hour; second = minute;
                if (firstInput.Text.Length > 0 && !TryComponent(firstInput.Text, hourCount - 1, out first)) return false;
                if (secondInput.Text.Length > 0 && !TryComponent(secondInput.Text, 59, out second)) return false;
                int total = first * 60 + second;
                return total >= totalMinimum && total <= totalMaximum;
            }
            void SyncInputFromWheels()
            {
                syncingInput = true;
                try
                {
                    firstInput.Text = hour.ToString("00", CultureInfo.InvariantCulture);
                    secondInput.Text = minute.ToString("00", CultureInfo.InvariantCulture);
                }
                finally { syncingInput = false; }
            }
            void UpdateTimeSelection()
            {
                if (confirm == null) return;
                confirm.IsEnabled = TrySelection(out _, out _);
                wheelHint.Text = confirm.IsEnabled ? normalHint : isDuration ? "请输入分和秒，范围0至1440:00" : applyMinutes != null ? "请输入小时和分钟，范围00:01至12:00" : "请输入小时和分钟，范围00:00至23:59";
                wheelHint.Visibility = wheelHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            void UseWheels()
            {
                editingFirst = editingSecond = false;
                usingInput = false; editedInput = false; hideEditors();
                SyncInputFromWheels(); UpdateTimeSelection();
            }
            var flyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
            var panel = new StackPanel { Width = compact ? 184 : 248, RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light };
            var wheels = new Grid { Height = wheelHeight, Background = new SolidColorBrush(background) };
            wheels.ColumnDefinitions.Add(new ColumnDefinition());
            wheels.ColumnDefinitions.Add(new ColumnDefinition());
            var selection = new Border
            {
                Height = wheelRowHeight, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(compact ? 12 : 18),
                Background = selectedBackground,
                IsHitTestVisible = false
            };
            Grid.SetColumnSpan(selection, 2); wheels.Children.Add(selection);
            // Typing animates the wheels; ignore those programmatic callbacks so
            // they cannot overwrite the edited text. A real scroll after the guard
            // must exit edit mode and revalidate.
            // ChangeView reports its value asynchronously even with animation off.
            // Remember the value we asked for so those echoes cannot overwrite a
            // newer keystroke; a real scroll after the short window still applies.
            long hourEchoUntil = 0, minuteEchoUntil = 0;
            int hourEcho = -1, minuteEcho = -1;
            var hours = CreateTimeWheel(hourCount, hour, foreground, value =>
            {
                if (Environment.TickCount64 < hourEchoUntil)
                {
                    if (value == hourEcho) hourEchoUntil = 0;
                    return;
                }
                usingInput = false;
                hour = value; SyncInputFromWheels(); UpdateTimeSelection();
            }, isDuration ? "分钟" : "小时", !isDuration && applyMinutes == null, wheelRowHeight, compact ? 3 : 5);
            var minutes = CreateTimeWheel(60, minute, foreground, value =>
            {
                if (Environment.TickCount64 < minuteEchoUntil)
                {
                    if (value == minuteEcho) minuteEchoUntil = 0;
                    return;
                }
                usingInput = false;
                minute = value; SyncInputFromWheels(); UpdateTimeSelection();
            }, isDuration ? "秒" : "分钟", true, wheelRowHeight, compact ? 3 : 5);
            foreach (var viewer in new[] { hours, minutes })
            {
                viewer.IsTabStop = false;
                viewer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, __) => UseWheels()), true);
                viewer.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, __) => UseWheels()), true);
                viewer.GotFocus += (_, __) => UseWheels();
            }
            void EditSelection()
            {
                // TextChanged may be delivered after SyncInputFromWheels returns.
                // Do not mistake that deferred notification for a user's edit, but
                // always refresh validation: a corrected value can match the wheel
                // state and must re-enable 确定.
                if (syncingInput) return;
                usingInput = true;
                bool changed = false;
                // Keep the wheel/model in sync with each valid half even when the
                // combined time is below the minimum (for example 00:00).
                if (TryComponent(firstInput.Text, hourCount - 1, out int first) && first != hour)
                { hour = first; changed = true; }
                if (TryComponent(secondInput.Text, 59, out int second) && second != minute)
                { minute = second; changed = true; }
                if (changed)
                {
                    editedInput = true;
                    hourEcho = hour; minuteEcho = minute;
                    hourEchoUntil = minuteEchoUntil = Environment.TickCount64 + 500;
                    hours.ChangeView(null, ((!isDuration && applyMinutes == null ? hourCount * 3 : 0) + hour) * wheelRowHeight, null, true);
                    minutes.ChangeView(null, (60 * 3 + minute) * wheelRowHeight, null, true);
                }
                UpdateTimeSelection();
            }
            void StepWheel(ScrollViewer viewer, int step)
            {
                UseWheels();
                viewer.Focus(FocusState.Programmatic);
                viewer.ChangeView(null, viewer.VerticalOffset + step * wheelRowHeight, null, false);
            }
            static string DigitsOnly(string text) => new string(text
                .Select(c => c >= '０' && c <= '９' ? (char)('0' + c - '０') : c)
                .Where(c => c >= '0' && c <= '9').ToArray());
            static bool TryComponent(string text, int maximum, out int value)
            {
                value = 0;
                string digits = DigitsOnly(text);
                return digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= maximum;
            }
            void ConnectInput(TextBox input, ScrollViewer viewer)
            {
                bool cleaning = false;
                input.TextChanged += (_, __) =>
                {
                    if (syncingInput || cleaning) return;
                    string digits = DigitsOnly(input.Text);
                    if (digits != input.Text)
                    {
                        // Ignore separators such as ":"; the two fields are edited
                        // as digits only and the time is split across them.
                        cleaning = true;
                        try { input.Text = digits; input.SelectionStart = digits.Length; }
                        finally { cleaning = false; }
                    }
                    if (input.FocusState != FocusState.Unfocused) EditSelection();
                    // Two hour digits complete the field; jump to minutes so typing
                    // "0001" or "00:01" fills both halves without extra clicks.
                    if (input == firstInput && digits.Length >= 2 && input.FocusState != FocusState.Unfocused &&
                        secondInput.FocusState == FocusState.Unfocused)
                    {
                        editingFirst = false; editingSecond = true;
                        secondInput.Focus(FocusState.Programmatic);
                        secondInput.SelectAll();
                    }
                };
                input.LostFocus += (sender, args) =>
                {
                    if (input == firstInput) editingFirst = false; else editingSecond = false;
                    hideEditor(input);
                    // Do not sync both fields here: auto-advancing focus to minutes
                    // would overwrite the field being typed into and swallow a key.
                    usingInput = false;
                };
                input.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, args) =>
                {
                    int delta = args.GetCurrentPoint(input).Properties.MouseWheelDelta;
                    if (delta == 0) return;
                    StepWheel(viewer, delta > 0 ? -1 : 1);
                    args.Handled = true;
                }), true);
                input.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
                {
                    int step = args.Key == Windows.System.VirtualKey.Up ? -1 : args.Key == Windows.System.VirtualKey.Down ? 1 : 0;
                    if (step == 0) return;
                    StepWheel(viewer, step); args.Handled = true;
                }), true);
            }
            ConnectInput(firstInput, hours); ConnectInput(secondInput, minutes);
            // WinUI draws selected text white even with a light selection brush.
            // Preserve the real selection for typing, while painting readable digits over it.
            Border MakeSelectionTextCover(TextBox input)
            {
                var text = new TextBlock
                {
                    FontFamily = input.FontFamily, FontSize = input.FontSize,
                    Foreground = new SolidColorBrush(foreground),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var cover = new Border
                {
                    Height = wheelRowHeight, Margin = new Thickness(12, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center, Background = selectedBackground,
                    Child = text, IsHitTestVisible = false, Visibility = Visibility.Collapsed
                };
                void UpdateCover()
                {
                    text.Text = input.Text;
                    cover.Visibility = input.SelectionLength > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                input.SelectionChanged += (_, __) => UpdateCover();
                input.TextChanged += (_, __) => UpdateCover();
                UpdateCover();
                return cover;
            }
            var firstSelectionCover = MakeSelectionTextCover(firstInput);
            var secondSelectionCover = MakeSelectionTextCover(secondInput);
            // Reveal the moving labels during scrolling instead of jumping values.
            void ConnectWheelMotion(ScrollViewer viewer, TextBox input, Border cover)
            {
                void ShowEditor(bool visible)
                {
                    input.Opacity = visible ? 1 : 0;
                    cover.Opacity = visible ? 1 : 0;
                    // Keep the transparent editor hit-testable so clicking the
                    // centered digit can still start direct numeric entry.
                    input.IsHitTestVisible = true;
                }
                ShowEditor(false);
                input.GotFocus += (_, __) => ShowEditor(true);
                input.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, __) =>
                {
                    if (input == firstInput) editingFirst = true; else editingSecond = true;
                    ShowEditor(true);
                    // Every click starts a replacement instead of inserting at the
                    // old caret position.
                    input.DispatcherQueue.TryEnqueue(() => input.SelectAll());
                }), true);
                input.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
                {
                    if (input == firstInput && (int)args.Key == 186 /* ;/: */)
                    {
                        // The separator is a focus move, not editable text.
                        editingFirst = false; editingSecond = true;
                        secondInput.Focus(FocusState.Programmatic);
                        secondInput.SelectAll();
                        args.Handled = true;
                        return;
                    }
                    if ((args.Key >= Windows.System.VirtualKey.Number0 && args.Key <= Windows.System.VirtualKey.Number9) ||
                        (args.Key >= Windows.System.VirtualKey.NumberPad0 && args.Key <= Windows.System.VirtualKey.NumberPad9) ||
                        args.Key == Windows.System.VirtualKey.Back || args.Key == Windows.System.VirtualKey.Delete)
                    {
                        if (input == firstInput) editingFirst = true; else editingSecond = true;
                        ShowEditor(true);
                    }
                }), true);
                viewer.ViewChanging += (_, __) => { if (!usingInput) ShowEditor(false); };
            }
            ConnectWheelMotion(hours, firstInput, firstSelectionCover);
            ConnectWheelMotion(minutes, secondInput, secondSelectionCover);
            hideEditor = input =>
            {
                input.Opacity = 0;
                (input == firstInput ? firstSelectionCover : secondSelectionCover).Opacity = 0;
            };
            hideEditors = () => { hideEditor(firstInput); hideEditor(secondInput); };
            wheels.Children.Add(hours); Grid.SetColumn(minutes, 1); wheels.Children.Add(minutes);
            // Fade only the outer rows, keeping the center selection uninterrupted.
            double fadeHeight = (wheelHeight - wheelRowHeight) / 2;
            var transparent = Windows.UI.Color.FromArgb(0, background.R, background.G, background.B);
            Border MakeEdgeFade(bool top)
            {
                var fade = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(0, 1)
                };
                fade.GradientStops.Add(new GradientStop { Color = top ? background : transparent, Offset = 0 });
                fade.GradientStops.Add(new GradientStop { Color = top ? transparent : background, Offset = 1 });
                var edge = new Border
                {
                    Height = fadeHeight, VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                    Background = fade, IsHitTestVisible = false
                };
                Grid.SetColumnSpan(edge, 2);
                return edge;
            }
            wheels.Children.Add(MakeEdgeFade(true)); wheels.Children.Add(MakeEdgeFade(false));
            var colon = new TextBlock { Text = ":", FontSize = compact ? 18 : 20, Foreground = new SolidColorBrush(foreground), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            // The selected digits themselves are the editors. Opaque backgrounds
            // mask the scroll labels below, so editing never doubles the glyphs.
            wheels.Children.Add(firstInput); Grid.SetColumn(secondInput, 1); wheels.Children.Add(secondInput);
            wheels.Children.Add(firstSelectionCover); Grid.SetColumn(secondSelectionCover, 1); wheels.Children.Add(secondSelectionCover);
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
            bool committed = false;
            void CommitSelection()
            {
                if (!usingInput && !editedInput && hours.ExtentHeight > wheelHeight && minutes.ExtentHeight > wheelHeight)
                {
                    // Read the actual centered rows, even if an animated scroll is
                    // still settling and the final ViewChanged has not arrived.
                    hour = Math.Clamp((int)Math.Round(hours.VerticalOffset / wheelRowHeight), 0, hourCount * (!isDuration && applyMinutes == null ? 7 : 1) - 1) % hourCount;
                    minute = Math.Clamp((int)Math.Round(minutes.VerticalOffset / wheelRowHeight), 0, 60 * 7 - 1) % 60;
                    SyncInputFromWheels();
                }
                if (committed || !TrySelection(out int selectedFirst, out int selectedSecond)) return;
                if (isDuration && !anchor.IsEnabled) return;
                committed = true;
                hour = selectedFirst; minute = selectedSecond;
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
            }
            confirm.Click += (_, __) => CommitSelection();
            panel.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, args) =>
            {
                if (args.Key == Windows.System.VirtualKey.Enter) { CommitSelection(); args.Handled = true; }
                else if (args.Key == Windows.System.VirtualKey.Escape) { flyout.Hide(); args.Handled = true; }
            }), true);
            UpdateTimeSelection();
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
            var labelScales = new List<CompositeTransform>();
            var labelPops = new List<Grid>();
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
            const double labelRestScale = 1, labelSelectedScale = 1.22;
            for (int index = 0; index < count * repeats; index++)
            {
                int selected = index;
                var labelScale = new CompositeTransform { ScaleX = 1, ScaleY = 1 };
                var text = new TextBlock
                {
                    Text = (index % count).ToString("00", CultureInfo.InvariantCulture),
                    FontSize = visibleRows == 3 ? 20 : 23, Foreground = new SolidColorBrush(foreground),
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI Variable Display"),
                    RenderTransform = labelScale, RenderTransformOrigin = new Windows.Foundation.Point(.5, .5)
                };
                text.Opacity = .2;
                labels.Add(text);
                // The pop host carries the Q弹 spring so it never fights the
                // proximity scale applied while the wheel is scrolling.
                var popHost = new Grid
                {
                    Height = rowHeight, HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
                popHost.Children.Add(text);
                var row = new Border { Height = rowHeight, Child = popHost, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                row.Tapped += (_, __) => viewer.ChangeView(null, selected * rowHeight, null, false);
                labelScales.Add(labelScale); labelPops.Add(popHost);
                rows.Children.Add(row);
            }
            rows.Children.Add(new Border { Height = padding });
            bool initialized = false;
            int previousFirst = 0, previousLast = -1, previousCenter = -1;
            void PopLabel(int wheelIndex)
            {
                if (wheelIndex < 0 || wheelIndex >= labelPops.Count || !animationSettings.AnimationsEnabled) return;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(labelPops[wheelIndex]);
                var size = visual.Size;
                visual.CenterPoint = new System.Numerics.Vector3(size.X * .5f, size.Y * .5f, 0);
                var spring = visual.Compositor.CreateSpringVector3Animation();
                spring.DampingRatio = .3f;
                spring.Period = TimeSpan.FromMilliseconds(190);
                spring.InitialValue = new System.Numerics.Vector3(1.10f, 1.10f, 1);
                spring.FinalValue = new System.Numerics.Vector3(1, 1, 1);
                visual.StartAnimation("Scale", spring);
            }
            void UpdateSelection(bool notify = true)
            {
                double position = viewer.VerticalOffset / rowHeight;
                int index = Math.Clamp((int)Math.Round(position), 0, count * repeats - 1);
                if (notify) changed(index % count);
                // Rows more than three steps away all have the same opacity.
                // Reset the previous neighborhood, then update only the new one.
                for (int i = previousFirst; i <= previousLast; i++)
                {
                    labels[i].Opacity = .2;
                    labelScales[i].ScaleX = labelScales[i].ScaleY = 1;
                }
                int first = Math.Max(0, (int)Math.Floor(position) - 3);
                int last = Math.Min(labels.Count - 1, (int)Math.Ceiling(position) + 3);
                for (int i = first; i <= last; i++)
                {
                    double distance = Math.Abs(i - position);
                    labels[i].Opacity = Math.Max(.2, 1 - distance * .3);
                    double proximity = Math.Max(0, 1 - distance);
                    double eased = proximity * proximity * (3 - 2 * proximity);
                    labelScales[i].ScaleX = labelScales[i].ScaleY =
                        labelRestScale + (labelSelectedScale - labelRestScale) * eased;
                }
                // Only a single-row crossing is the selection moving on; larger
                // jumps are recentering or programmatic value sync.
                if (notify && previousCenter >= 0 && Math.Abs(index - previousCenter) == 1) PopLabel(previousCenter);
                previousCenter = index;
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
                UpdateSelection(false);
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
            var stack = new StackPanel();
            var restExtras = BuildRestContent();
            var workAndRest = new StackPanel { Spacing = 14 };
            workAndRest.Children.Add(restPlanContent);
            workAndRest.Children.Add(workingHoursContent);
            workAndRest.Children.Add(new Border { Height = 1, Background = dividerBrush });
            workAndRest.Children.Add(restExtras);
            incomeContent = new Border { Child = new Border { Child = BuildIncomeContent(), Padding = new Thickness(16, 4, 16, 18) }, Visibility = Visibility.Collapsed };
            restContent = new Border { Child = new Border { Child = workAndRest, Padding = new Thickness(16, 4, 16, 18) }, Visibility = Visibility.Collapsed };
            careContent = new Border { Child = new Border { Child = BuildCareContent(), Padding = new Thickness(16, 4, 16, 18) }, Visibility = Visibility.Collapsed };
            var headers = new[] {
                CreateAccordionHeader("薪资与隐私", out incomeChevron, () => ToggleAccordion(1), 0),
                CreateAccordionHeader("工作与休息", out restChevron, () => ToggleAccordion(2), 1),
                CreateAccordionHeader("健康提醒", out careChevron, () => ToggleAccordion(3), 2) };
            var contents = new[] { incomeContent, restContent, careContent };
            var wishCard = BuildWishCard();
            foreach (int i in new[] { 1, 0, 2 })
            {
                var body = new StackPanel(); body.Children.Add(headers[i]); body.Children.Add(contents[i]);
                accordionCards[i] = CreateCardBorder(new Thickness(0)); accordionCards[i].Child = body;
                stack.Children.Add(accordionCards[i]);
                if (i == 0) stack.Children.Add(wishCard);
            }
            accordionProgress[1] = accordionStart[1] = accordionTarget[1] = 1;
            restContent.Visibility = Visibility.Visible;
            restChevron.RenderTransform = new RotateTransform { Angle = 90 };
            UpdateAccordionChrome();
            form.Children.Add(stack);
        }
        FrameworkElement BuildEffectShop()
        {
            var details = new StackPanel { Spacing = 6 };
            var coinJarRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            coinJarRow.ColumnDefinitions.Add(new ColumnDefinition());
            coinJarRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            coinJarRow.Children.Add(new TextBlock { Text = "显示金币罐", FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            showCoinJar = CreateGreenSwitch(current.ShowCoinJar);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(showCoinJar, "显示金币罐");
            Grid.SetColumn(showCoinJar, 1); coinJarRow.Children.Add(showCoinJar);
            details.Children.Add(coinJarRow);
            var balance = new TextBlock { FontSize = 12, Foreground = subInkBrush };
            var notice = new TextBlock { FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap, Text = "解锁后永久使用，选择立即生效。" };
            var buttons = new List<Button>();
            var effects = new[] { (Id: 0, Name: "默认"), (Id: 1, Name: "星星闪烁"), (Id: 2, Name: "暖光漂浮"), (Id: 3, Name: "雪花飘落"), (Id: 5, Name: "落叶飘飘") };
            details.Children.Add(balance);
            for (int i = 0; i < effects.Length; i++)
            {
                int effect = effects[i].Id;
                string effectName = effects[i].Name;
                var row = new Grid { MinHeight = 38 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = effectName, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
                var button = new Button { Height = 32, MinWidth = 96, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), Background = inputBackgroundBrush, Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Right, FontSize = 13 };
                button.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
                button.Resources["ButtonBackgroundPressed"] = timePressedBrush;
                buttons.Add(button); Grid.SetColumn(button, 1); row.Children.Add(button); details.Children.Add(row);
                button.Click += (_, __) =>
                {
                    try
                    {
                        if (!privacy.EffectUnlocked(effect))
                        {
                            if (!privacy.UnlockEffect(effect)) { notice.Text = "金币不足，继续收集金币吧。"; return; }
                            notice.Text = effectName + "已解锁，可以随时使用。";
                        }
                        else { privacy.SelectEffect(effect); notice.Text = "已切换为" + effectName + "。"; }
                        Refresh();
                    }
                    catch (IOException) { notice.Text = "保存失败，请稍后重试。"; }
                };
            }
            void Refresh()
            {
                balance.Text = "可用金币：" + privacy.CoinBalance.ToString("N0", CultureInfo.InvariantCulture);
                int selected = privacy.SelectedEffect;
                for (int i = 0; i < buttons.Count; i++)
                    buttons[i].Content = selected == effects[i].Id ? "使用中" : privacy.EffectUnlocked(effects[i].Id) ? "使用" : OffWorkWidget.PrivacyStore.EffectPrice(effects[i].Id) + " 金币解锁";
            }
            refreshEffectShop = () => { Refresh(); refreshDailyFortune?.Invoke(); };
            details.Children.Add(notice);
            details.Children.Add(BuildDailyFortune());
            return details;
        }

        FrameworkElement BuildDailyFortune()
        {
            var messages = new[] {
                "今天也在认真生活，给自己一个微笑。", "慢一点也没关系，你正在向前。",
                "忙碌之余，记得喝口水、伸个懒腰。", "今天的小进步，值得被看见。",
                "好运正在路上，先好好照顾自己。", "工作有期限，快乐也要安排上。",
                "摸一会儿鱼，再精神满满地出发。", "今天适合给自己一点小奖励。",
                "下班倒计时已经开始，期待好时光。", "辛苦啦，今天也离下班更近一步。",
                "不必事事完美，尽力就很棒。", "愿你今天的烦恼早早下班。"
            };
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(new Border { Height = 1, Background = dividerBrush });
            var row = new Grid { MinHeight = 38 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = "每日抽签", FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Height = 32, MinWidth = 96, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), Background = inputBackgroundBrush, Foreground = inkBrush, FontSize = 13 };
            button.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            button.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            Grid.SetColumn(button, 1); row.Children.Add(button); panel.Children.Add(row);
            var result = new TextBlock { FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(result);
            DateTime displayedDay = DateTime.Today;
            void Refresh()
            {
                displayedDay = DateTime.Today;
                int value = privacy.TodayFortune(displayedDay);
                button.Content = value > 0 ? "查看今日签" : "10 金币抽签";
                result.Text = value > 0 && value <= messages.Length ? "今日签：" + messages[value - 1] : "每天可抽一次，当天免费查看。";
            }
            button.Click += (_, __) =>
            {
                try
                {
                    if (displayedDay != DateTime.Today) { Refresh(); return; }
                    if (!privacy.DrawDailyFortune(DateTime.Now, out int drawResult)) { result.Text = "金币不足，需要 10 金币。"; return; }
                    Refresh();
                    refreshEffectShop?.Invoke();
                }
                catch (IOException) { result.Text = "保存失败，请稍后重试。"; }
            };
            refreshDailyFortune = Refresh;
            Refresh();
            return panel;
        }
        Border BuildWishCard()
        {
            var card = CreateCardBorder(new Thickness(16));
            var panel = new StackPanel { Spacing = 12 };
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headerLabels = new StackPanel { Spacing = 2 };
            headerLabels.Children.Add(new TextBlock { Text = "我的心愿", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            wishSummary = new TextBlock { FontSize = 12.5, Foreground = subInkBrush, TextTrimming = TextTrimming.CharacterEllipsis };
            headerLabels.Children.Add(wishSummary);
            Grid.SetColumn(headerLabels, 0); header.Children.Add(headerLabels);
            wishEnabled = CreateGreenSwitch(current.WishEnabled);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(wishEnabled, "显示我的心愿");
            Grid.SetColumn(wishEnabled, 1); header.Children.Add(wishEnabled);
            panel.Children.Add(header);
            var details = new StackPanel { Spacing = 8 };
            wishDetails = details;
            wishDetailsProgress = current.WishEnabled ? 1 : 0;
            wishDetailsHost = new Border { Child = details, Visibility = current.WishEnabled ? Visibility.Visible : Visibility.Collapsed };
            wishEnabled.Toggled += (_, __) => ToggleWishDetails(wishEnabled.IsOn);
            details.Children.Add(new TextBlock { Text = "进度 = 已攒 + 心愿开始后累计工资", FontSize = 13, Foreground = subInkBrush });
            var allocationRow = new Grid { ColumnSpacing = 8 };
            allocationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            allocationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            allocationRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            allocationRow.Children.Add(new TextBlock { Text = "多心愿进度", FontSize = 12.5, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center });
            wishAllocation = new ComboBox
            {
                Height = 34, MinWidth = 128, FontSize = 13,
                Background = inputBackgroundBrush, Foreground = inkBrush,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            wishAllocation.Items.Add(new ComboBoxItem { Content = "依次完成", Tag = "sequential" });
            wishAllocation.Items.Add(new ComboBoxItem { Content = "平均分配", Tag = "split" });
            wishAllocation.SelectedIndex = current.WishAllocation == "split" ? 1 : 0;
            ToolTipService.SetToolTip(wishAllocation, "依次完成：总进度池依次填满前面的心愿；平均分配：总进度池平均分到每个心愿");
            Grid.SetColumn(wishAllocation, 1); allocationRow.Children.Add(wishAllocation);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            wishCount = new TextBlock { FontSize = 12.5, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center };
            var accentTint = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 0, 122, 255));
            var accentTintHover = new SolidColorBrush(Windows.UI.Color.FromArgb(44, 0, 122, 255));
            var accentTintPressed = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 0, 122, 255));
            var accentTintDisabled = new SolidColorBrush(Windows.UI.Color.FromArgb(14, 0, 122, 255));
            wishAdd = new Button
            {
                Content = new FontIcon { Glyph = "\uE710", FontSize = 14 },
                Width = 32, Height = 32, MinWidth = 0, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Background = accentTint,
                Foreground = accentBlue,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            wishAdd.Resources["ButtonBackgroundPointerOver"] = accentTintHover;
            wishAdd.Resources["ButtonBackgroundPressed"] = accentTintPressed;
            wishAdd.Resources["ButtonBackgroundDisabled"] = accentTintDisabled;
            wishAdd.Resources["ButtonForegroundPointerOver"] = accentBlue;
            wishAdd.Resources["ButtonForegroundPressed"] = accentBlue;
            wishAdd.Resources["ButtonForegroundDisabled"] = subInkBrush;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(wishAdd, "添加心愿");
            ToolTipService.SetToolTip(wishAdd, "添加一个新的小心愿");
            wishAdd.Click += (_, __) => { if (wishRows.Count < OffWorkWidget.Logic.MaxWishes) AddWishRow(new OffWorkWidget.WishEntry(), blank: true); };
            actions.Children.Add(wishCount); actions.Children.Add(wishAdd);
            Grid.SetColumn(actions, 2); allocationRow.Children.Add(actions);
            details.Children.Add(allocationRow);
            var labels = new Grid { ColumnSpacing = 6 };
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            string[] labelText = { "心愿名称", "目标（元）", "已攒（元）" };
            for (int i = 0; i < 3; i++)
            {
                var label = new TextBlock { Text = labelText[i], FontSize = 12.5, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = subInkBrush, Margin = new Thickness(8, 0, 0, 4) };
                Grid.SetColumn(label, i); labels.Children.Add(label);
            }
            details.Children.Add(labels);
            wishRowsHost = new StackPanel { Spacing = 12 };
            details.Children.Add(wishRowsHost);
            panel.Children.Add(wishDetailsHost);
            card.Child = panel;

            OffWorkWidget.Logic.NormalizeWishes(current);
            achievedWishKeys.Clear();
            if (current.Wishes.Count > 0)
            {
                decimal pool = OffWorkWidget.Logic.WishPool(DateTime.Now, current);
                var progress = OffWorkWidget.Logic.AllocateWishProgress(current.Wishes, current.WishAllocation, pool);
                for (int i = 0; i < current.Wishes.Count && i < progress.Length; i++)
                {
                    var wish = current.Wishes[i];
                    if (wish.Target > 0 && progress[i] >= wish.Target - .005m) achievedWishKeys.Add(WishKey(wish));
                }
            }
            foreach (var wish in current.Wishes) AddWishRow(wish);
            UpdateWishAddState();
            return card;
        }

        void AddWishRow(OffWorkWidget.WishEntry wish, bool blank = false)
        {
            if (wishRows.Count >= OffWorkWidget.Logic.MaxWishes) return;
            var row = new OffWorkWidget.WishEntry { Id = wish.Id, Name = wish.Name ?? "", Target = wish.Target, Saved = wish.Saved };
            var grid = new Grid { ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var editor = new WishRowEditor
            {
                Id = row.Id,
                Name = CreateInputBox(blank ? "" : row.Name),
                Target = CreateInputBox(blank ? "" : row.Target.ToString(CultureInfo.InvariantCulture)),
                Saved = CreateInputBox(blank ? "" : row.Saved.ToString(CultureInfo.InvariantCulture))
            };
            foreach (var box in new[] { editor.Name, editor.Target, editor.Saved })
            {
                box.MinWidth = 0;
                box.MinHeight = 40;
                box.FontSize = 15;
                box.Padding = new Thickness(9, 7, 9, 7);
            }
            editor.Name.MaxLength = 8;
            int number = wishRows.Count + 1;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor.Name, "心愿名称 " + number);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor.Target, "目标金额 " + number + "，元");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor.Saved, "已攒金额 " + number + "，元");
            grid.Children.Add(editor.Name);
            Grid.SetColumn(editor.Target, 1); grid.Children.Add(editor.Target);
            Grid.SetColumn(editor.Saved, 2); grid.Children.Add(editor.Saved);
            editor.Delete = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
                Width = 28, Height = 28, MinWidth = 0, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = subInkBrush,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            editor.Delete.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            editor.Delete.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor.Delete, "删除心愿 " + number);
            ToolTipService.SetToolTip(editor.Delete, "删除这个心愿");
            editor.Delete.Click += (_, __) =>
            {
                wishRows.Remove(editor);
                wishRowsHost.Children.Remove(editor.Card);
                RenumberWishRows();
                UpdateWishAddState();
            };
            Grid.SetColumn(editor.Delete, 3); grid.Children.Add(editor.Delete);
            var rowContainer = new StackPanel { Spacing = 3 };
            rowContainer.Children.Add(grid);
            if (!blank && achievedWishKeys.Contains(WishKey(wish)))
            {
                editor.Status = new TextBlock
                {
                    Text = "已达成", FontSize = 11.5, Foreground = accentBlue,
                    Margin = new Thickness(9, 0, 0, 0), IsTextScaleFactorEnabled = false
                };
                rowContainer.Children.Add(editor.Status);
            }
            editor.Card = rowContainer;
            wishRows.Add(editor);
            wishRowsHost.Children.Add(editor.Card);
            RenumberWishRows();
            UpdateWishAddState();
        }

        void RenumberWishRows()
        {
            for (int index = 0; index < wishRows.Count; index++)
            {
                var row = wishRows[index];
                int number = index + 1;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row.Name, "心愿名称 " + number);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row.Target, "目标金额 " + number + "，元");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row.Saved, "已攒金额 " + number + "，元");
                if (row.Delete != null)
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row.Delete, "删除心愿 " + number);
            }
        }

        void UpdateWishAddState()
        {
            if (wishAdd == null) return;
            wishAdd.IsEnabled = wishRows.Count < OffWorkWidget.Logic.MaxWishes;
            if (wishCount != null) wishCount.Text = wishRows.Count + "/" + OffWorkWidget.Logic.MaxWishes;
            if (wishSummary != null)
                wishSummary.Text = wishRows.Count == 0 ? "暂无心愿" : "共 " + wishRows.Count + " 个心愿";
            ToolTipService.SetToolTip(wishAdd, wishAdd.IsEnabled ? "添加一个新的小心愿" : "最多添加 8 个小心愿");
        }

        Border CreateChevronSurface(Button owner, Grid icon)
        {
            var surface = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Background = eyeGlassBrush, Child = icon
            };
            owner.PointerEntered += (_, __) => surface.Background = chipHoverBrush;
            owner.PointerExited += (_, __) => surface.Background = eyeGlassBrush;
            owner.Click += (_, __) => PopSpringElement(surface);
            return surface;
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

        UIElement CreateAccordionHeader(string titleText, out Grid chevron, Action onToggle, int summaryIndex)
        {
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 12, 12, 12), Margin = new Thickness(4),
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
            var chevronSurface = CreateChevronSurface(btn, chevron);

            var labels = new StackPanel { Spacing = 2 };
            labels.Children.Add(title);
            var summary = new TextBlock { FontSize = 12, Foreground = subInkBrush, TextTrimming = TextTrimming.CharacterEllipsis, Height = 18 };
            accordionSummaries[summaryIndex] = summary;
            labels.Children.Add(summary);
            Grid.SetColumn(labels, 0); grid.Children.Add(labels);
            Grid.SetColumn(chevronSurface, 1); grid.Children.Add(chevronSurface);
            btn.Content = grid;
            btn.Click += (_, __) => onToggle();
            return btn;
        }

        void EnsureAccordionExpanded(int index)
        {
            if (index == 2 ? !workSectionExpanded : expandedAccordion != index) ToggleAccordion(index);
        }

        void ToggleAccordion(int index)
        {
            if (index == 2) workSectionExpanded = !workSectionExpanded;
            else expandedAccordion = expandedAccordion == index ? 0 : index;
            for (int i = 0; i < 3; i++)
            {
                accordionStart[i] = accordionProgress[i];
                accordionTarget[i] = (i == 1 ? workSectionExpanded : expandedAccordion == i + 1) ? 1 : 0;
            }
            accordionClock.Restart();
            if (!animatingAccordion) { animatingAccordion = true; CompositionTarget.Rendering += AnimateAccordion; }
            AnimateAccordion(null, null!);
        }

        void UpdateAccordionChrome()
        {
            if (accordionSummaries.All(summary => summary != null))
            {
                string mode = scheduleMode == "double" ? "双休" : scheduleMode == "single" ? "单休" : scheduleMode == "alternating" ? "大小周" : "自定义";
                accordionSummaries[1]!.Text = FormatTime(startTime).Replace(" ", "") + "–" + FormatTime(endTime).Replace(" ", "") + " · " + mode;
                accordionSummaries[0]!.Text = "金额保护 · 每月 " + payDay.Text + " 日发薪";
                var reminders = new List<string>();
                if (water.IsOn) reminders.Add("喝水 " + FormatReminderInterval(waterMinutes));
                if (walk.IsOn) reminders.Add("活动 " + FormatReminderInterval(walkMinutes));
                accordionSummaries[2]!.Text = reminders.Count == 0 ? "提醒已关闭" : string.Join(" · ", reminders);
            }
            for (int i = 0; i < 3; i++)
            {
                accordionCards[i].CornerRadius = new CornerRadius(18);
                accordionCards[i].Margin = new Thickness(0, 0, 0, 10);
                if (accordionSummaries[i] is TextBlock summary)
                {
                    summary.Height = 18 * (1 - accordionProgress[i]);
                    summary.Opacity = 1 - accordionProgress[i];
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

        void ToggleWishDetails(bool show)
        {
            if (wishDetailsHost == null) return;
            if (wishDetailsHost.XamlRoot == null)
            {
                wishDetailsProgress = wishDetailsTarget = wishDetailsStart = show ? 1 : 0;
                wishDetailsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            wishDetailsStart = wishDetailsProgress;
            wishDetailsTarget = show ? 1 : 0;
            wishDetailsHost.Visibility = Visibility.Visible;
            wishDetailsClock.Restart();
            if (!animatingWishDetails) { animatingWishDetails = true; CompositionTarget.Rendering += AnimateWishDetails; }
            AnimateWishDetails(null, null!);
        }

        void AnimateWishDetails(object? sender, object args)
        {
            bool enabled = animationSettings.AnimationsEnabled;
            double position = enabled ? Math.Clamp(wishDetailsClock.Elapsed.TotalMilliseconds / 260, 0, 1) : 1;
            double eased = position * position * (3 - 2 * position);
            double progress = wishDetailsStart + (wishDetailsTarget - wishDetailsStart) * eased;
            wishDetailsProgress = progress;
            double width = Math.Max(1, form.ActualWidth - 32);
            wishDetails.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
            double height = Math.Max(0, wishDetails.DesiredSize.Height) * progress;
            wishDetailsHost.Height = height;
            wishDetailsHost.Opacity = progress;
            wishDetailsHost.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, height) };
            if (position >= 1)
            {
                wishDetailsHost.Height = double.NaN; wishDetailsHost.Clip = null;
                wishDetailsHost.Opacity = 1;
                wishDetailsHost.Visibility = wishDetailsTarget > 0 ? Visibility.Visible : Visibility.Collapsed;
                CompositionTarget.Rendering -= AnimateWishDetails; wishDetailsClock.Stop(); animatingWishDetails = false;
            }
        }

        void BuildDesktopLayoutCard()
        {
            var card = CreateCardBorder(new Thickness(16, 16, 16, 10));
            // Last card: keep the bottom breathing room inside the card, not as
            // scrollable trailing margin behind the fixed footer.
            card.Margin = new Thickness(0);
            appearanceOptions = new StackPanel { Spacing = 14 };
            appearanceOptions.Children.Add(new TextBlock { Text = "桌面设置", FontSize = 14, Foreground = inkBrush, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var appearanceBody = new StackPanel { Spacing = 14 };
            var petRow = new Grid();
            petRow.ColumnDefinitions.Add(new ColumnDefinition());
            petRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            petRow.Children.Add(new TextBlock { Text = "桌面宠物", FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(petSelector, 1); petRow.Children.Add(petSelector);
            appearanceBody.Children.Add(petRow);
            appearanceBody.Children.Add(new Border { Height = 1, Background = dividerBrush });
            appearanceBody.Children.Add(CreateScaleRuler());
            appearanceBody.Children.Add(new Border { Height = 1, Background = dividerBrush });
            appearanceBody.Children.Add(CreateOptionsSection("金币商城", BuildEffectShop(), () => refreshEffectShop?.Invoke()));
            appearanceOptions.Children.Add(CreateOptionsSection("外观", appearanceBody));
            card.Child = appearanceOptions;
            form.Children.Add(card);
        }
        FrameworkElement CreateScaleRuler()
        {
            var header = new Grid();
            scaleLabel = new TextBlock { Text = "挂件大小", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
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
                Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                Background = eyeGlassBrush, Foreground = subInkBrush,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                VerticalAlignment = VerticalAlignment.Center
            };
            salaryVisibilityIcon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, IsTextScaleFactorEnabled = false,
                Glyph = salaryAuthorized ? "\uE890" : "\uED1A", Foreground = subInkBrush
            };
            eyeBtn.Content = salaryVisibilityIcon;
            eyeBtn.Resources["ButtonBackgroundPointerOver"] = chipHoverBrush;
            eyeBtn.Resources["ButtonBackgroundPressed"] = chipHoverBrush;
            eyeBtn.Resources["ButtonForegroundPointerOver"] = inkBrush;
            eyeBtn.Resources["ButtonForegroundPressed"] = inkBrush;
            eyeBtn.Click += async (_, __) =>
            {
                PopSpringElement(eyeBtn);
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
            amountDurationDisplay = new TextBlock
            {
                Text = FormatAmountDuration(amountDurationSeconds), Foreground = inkBrush, FontSize = 16,
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            amountDurationButton = new Button
            {
                Content = amountDurationDisplay, Width = 136, Height = 36, CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush, BorderThickness = new Thickness(0), Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
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
            pwdBtns.ColumnDefinitions.Add(new ColumnDefinition());
            var changePwd = new Button
            {
                Content = "管理密码", FontSize = 13, MinWidth = 0, Height = 34, CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 4, 12, 4)
            };
            changePwd.Click += async (_, __) =>
            {
                if (privacyPromptOpen) return;
                privacyPromptOpen = true;
                try { if (!await PrivacyPrompts.ChangeAsync(root.XamlRoot, privacy, root.ActualTheme)) LockSalaryEditor(); }
                catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
                finally { ReconcilePrivacyAfterDialog(); privacyPromptOpen = false; }
            };
            pwdBtns.Children.Add(changePwd);

            var forgotPwd = new Button
            {
                Content = "忘记密码", FontSize = 13, MinWidth = 0, Height = 34, CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 4, 12, 4)
            };
            foreach (var action in new[] { changePwd, forgotPwd })
            {
                action.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
                action.Resources["ButtonBackgroundPressed"] = timePressedBrush;
                action.Resources["ButtonForegroundPointerOver"] = inkBrush;
                action.Resources["ButtonForegroundPressed"] = inkBrush;
            }
            forgotPwd.Click += async (_, __) =>
            {
                if (privacyPromptOpen) return;
                privacyPromptOpen = true;
                try
                {
                    if (await PrivacyPrompts.ResetAsync(root.XamlRoot, privacy, root.ActualTheme))
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
                bool unlocked = await PrivacyPrompts.UnlockAsync(root.XamlRoot, privacy, root.ActualTheme);
                if (ReconcilePrivacyAfterDialog() && unlocked)
                {
                    salaryAuthorized = true;
                    salary.IsReadOnly = false;
                    salary.Text = salaryDraft ?? privacy.LoadSalary().ToString(CultureInfo.InvariantCulture);
                    salaryVisibilityIcon.Glyph = "\uE890";
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

            var presets = new Grid { ColumnSpacing = 2 };
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
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = inkBrush
                };
                foreach (string state in new[] { "", "PointerOver", "Pressed" })
                {
                    preset.Resources["ToggleButtonBackground" + state] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    preset.Resources["ToggleButtonBackgroundChecked" + state] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    preset.Resources["ToggleButtonForegroundChecked" + state] = inkBrush;
                    preset.Resources["ToggleButtonBorderBrushChecked" + state] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
                preset.Click += (_, __) => SelectPreset(choice);
                restPresetButtons.Add(preset); Grid.SetColumn(preset, i); presets.Children.Add(preset);
            }
            void SelectPreset(int choice)
            {
                if (choice == 3) { SwitchScheduleMode("custom"); dayTimeExpanded = true; alternatingWeekends = false; customRestPreset = true; UpdateRestPresets(); }
                else if (choice == 2) SetWeek(AlternatingWeekMask(), true);
                else SetWeek(choice == 0 ? doubleRestDays : singleRestDays);
            }

            var presetInput = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                ManipulationMode = ManipulationModes.None
            };
            uint? presetPointer = null;
            Windows.Foundation.Point presetPress = default;
            double presetScrollStart = 0;
            int presetAxis = 0, pressedChoice = -1, lastSlideChoice = -1;
            int ChoiceAt(double x) => presets.ActualWidth > 0
                ? Math.Clamp((int)(x / presets.ActualWidth * restPresetButtons.Count), 0, restPresetButtons.Count - 1) : -1;
            void SlideTo(double x)
            {
                int choice = ChoiceAt(x);
                if (choice >= 0 && choice != lastSlideChoice) { lastSlideChoice = choice; SelectPreset(choice); }
            }
            presetInput.PointerPressed += (_, e) =>
            {
                var point = e.GetCurrentPoint(presetInput);
                if (presetPointer.HasValue || (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)) return;
                if (!presetInput.CapturePointer(e.Pointer)) return;
                presetPointer = e.Pointer.PointerId;
                presetPress = e.GetCurrentPoint(windowSurface).Position;
                presetScrollStart = settingsScroll.VerticalOffset;
                presetAxis = 0;
                lastSlideChoice = -1;
                pressedChoice = ChoiceAt(point.Position.X);
                if (pressedChoice >= 0) restPresetButtons[pressedChoice].Focus(FocusState.Pointer);
                e.Handled = true;
            };
            presetInput.PointerMoved += (_, e) =>
            {
                if (presetPointer != e.Pointer.PointerId) return;
                var point = e.GetCurrentPoint(presetInput).Position;
                var gesturePoint = e.GetCurrentPoint(windowSurface).Position;
                double dx = gesturePoint.X - presetPress.X, dy = gesturePoint.Y - presetPress.Y;
                if (presetAxis == 0 && Math.Max(Math.Abs(dx), Math.Abs(dy)) > 6)
                    presetAxis = Math.Abs(dx) > Math.Abs(dy) ? 1 : 2;
                if (presetAxis == 1) SlideTo(point.X);
                else if (presetAxis == 2 && e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
                    settingsScroll.ChangeView(null, Math.Clamp(presetScrollStart - dy, 0, settingsScroll.ScrollableHeight), null, true);
                e.Handled = true;
            };
            presetInput.PointerReleased += (_, e) =>
            {
                if (presetPointer != e.Pointer.PointerId) return;
                var point = e.GetCurrentPoint(presetInput).Position;
                if (presetAxis == 1) SlideTo(point.X);
                else if (presetAxis == 0 && point.X >= 0 && point.X <= presetInput.ActualWidth && point.Y >= 0 && point.Y <= presetInput.ActualHeight && ChoiceAt(point.X) == pressedChoice)
                    SelectPreset(pressedChoice);
                presetPointer = null;
                presetInput.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
            };
            presetInput.PointerCanceled += (_, e) =>
            {
                if (presetPointer != e.Pointer.PointerId) return;
                presetPointer = null;
                presetInput.ReleasePointerCapture(e.Pointer);
                UpdateRestPresets();
            };
            presetInput.PointerCaptureLost += (_, e) =>
            {
                if (presetPointer == e.Pointer.PointerId) { presetPointer = null; UpdateRestPresets(); }
            };
            restPresetIndicator = new Border
            {
                Background = restPresetSelectionBrush, BorderBrush = restPresetBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false
            };
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(restPresetIndicator, true);
            restPresetTrack = presets;
            var indicatorLayer = new Grid { IsHitTestVisible = false };
            indicatorLayer.Children.Add(restPresetIndicator);
            indicatorLayer.SizeChanged += (_, e) =>
            {
                int selected = restPresetButtons.FindIndex(button => button.IsChecked == true);
                if (selected >= 0) UpdateRestPresetIndicator(selected, false);
            };
            var presetHost = new Grid();
            presetHost.Children.Add(indicatorLayer);
            presetHost.Children.Add(presets);
            presetHost.Children.Add(presetInput);
            panel.Children.Add(new Border { Background = inputBackgroundBrush, CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Child = presetHost });

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
            alternatingWeekReveal = new Border { Child = alternatingWeekRow, Visibility = Visibility.Collapsed, Height = 0, Opacity = 0 };
            var dayGroup = new StackPanel();
            dayGroup.Children.Add(alternatingWeekReveal);
            panel.Children.Add(dayGroup);

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
                    cb.Resources["ToggleButtonBackgroundChecked" + state] = restPresetSelectionBrush;
                    cb.Resources["ToggleButtonForegroundChecked" + state] = inkBrush;
                    cb.Resources["ToggleButtonBorderBrushChecked" + state] = restPresetBorderBrush;
                }
                void UpdateDay()
                {
                    bool selected = cb.IsChecked == true;
                    cb.Background = selected ? restPresetSelectionBrush : inputBackgroundBrush;
                    cb.Foreground = inkBrush;
                    cb.BorderBrush = selected ? restPresetBorderBrush : dividerBrush;
                    cb.BorderThickness = new Thickness(selected ? 1 : 0);
                    cb.Padding = new Thickness(0, selected ? 2 : 0, 0, 0);
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
            dayGroup.Children.Add(daysGrid);
            restWeekHint = new TextBlock { FontSize = 12, LineHeight = 19, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(restWeekHint);
            UpdateRestPresets();
            restPlanContent = panel;
            panel = new StackPanel { Spacing = 10 };



            var holidayRow = new Grid();
            holidayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            holidayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var holidayTitle = new TextBlock { Text = "中国节假日与调休", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            holidays = CreateGreenSwitch(current.UseChinaHolidays);
            Grid.SetColumn(holidayTitle, 0); holidayRow.Children.Add(holidayTitle);
            Grid.SetColumn(holidays, 1); holidayRow.Children.Add(holidays);


            var holidayStatusRow = new Grid();
            holidayStatusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            holidayStatusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int calendarYear = DateTime.Now.Year;
            holidayStatus = new TextBlock { Text = current.Calendar == null ? "节假日数据未加载" : current.Calendar.Status(calendarYear), FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var updateBtn = new Button { Content = "更新", Height = 32, MinWidth = 64, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), Background = inputBackgroundBrush, Foreground = inkBrush, FontSize = 13 };
            updateBtn.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            updateBtn.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            updateBtn.Click += async (_, __) =>
            {
                if (current.Calendar == null) return;
                updateBtn.IsEnabled = false; holidayStatus.Text = "正在更新节假日…";
                try
                {
                    // Next year's schedule is often published late in the current
                    // year, so refresh both instead of only the calendar year.
                    await Task.WhenAll(
                        current.Calendar.RefreshAsync(calendarYear, true),
                        current.Calendar.RefreshAsync(calendarYear + 1, true),
                        current.Calendar.RefreshAsync(calendarYear + 2, true));
                    holidayStatus.Text = current.Calendar.Status(calendarYear);
                }
                catch { holidayStatus.Text = "更新失败，继续使用本地安排。"; }
                finally { updateBtn.IsEnabled = true; }
            };
            Grid.SetColumn(holidayStatus, 0); holidayStatusRow.Children.Add(holidayStatus);
            Grid.SetColumn(updateBtn, 1); holidayStatusRow.Children.Add(updateBtn);



            var adjustmentBody = new StackPanel { Spacing = 10 };
            adjustmentBody.Children.Add(holidayRow);
            adjustmentBody.Children.Add(holidayStatusRow);
            adjustmentBody.Children.Add(new Border { Height = 1, Background = dividerBrush, Margin = new Thickness(0, 4, 0, 4) });
            adjustmentBody.Children.Add(new TextBlock { Text = "手动调休", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Foreground = inkBrush });
            var adjustmentReveal = new Border { Child = adjustmentBody, Padding = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            var adjustmentClip = new RectangleGeometry();
            var adjustmentClock = new Stopwatch();
            var adjustmentRotation = new RotateTransform();
            bool adjustmentOpen = false, adjustmentAnimating = false;
            double adjustmentHeight = 0, adjustmentFrom = 0, adjustmentTo = 0, adjustmentOpacityFrom = 0, adjustmentAngleFrom = 0;
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
            adjustmentTitle.Children.Add(new TextBlock { Text = "调休", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            adjustmentSummary = new TextBlock { FontSize = 12, Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(adjustmentSummary, 1); adjustmentTitle.Children.Add(adjustmentSummary);
            var adjustmentChevron = CreateChevron();
            var adjustmentArrowSurface = CreateChevronSurface(adjustmentHeader, adjustmentChevron);
            Grid.SetColumn(adjustmentArrowSurface, 2); adjustmentTitle.Children.Add(adjustmentArrowSurface);
            adjustmentChevron.RenderTransform = adjustmentRotation;
            void FinishAdjustmentReveal()
            {
                CompositionTarget.Rendering -= AnimateAdjustmentReveal;
                adjustmentAnimating = false; adjustmentClock.Stop();
                adjustmentHeight = adjustmentTo;
                adjustmentReveal.Visibility = adjustmentOpen ? Visibility.Visible : Visibility.Collapsed;
                adjustmentReveal.Height = double.NaN; adjustmentReveal.Clip = null;
                adjustmentBody.Height = double.NaN;
                adjustmentReveal.Opacity = adjustmentOpen ? 1 : 0;
                adjustmentRotation.Angle = adjustmentOpen ? 90 : 0;
            }
            void AnimateAdjustmentReveal(object? sender, object args)
            {
                double progress = animationSettings.AnimationsEnabled ? Math.Clamp(adjustmentClock.Elapsed.TotalMilliseconds / 260, 0, 1) : 1;
                double eased = progress * progress * (3 - 2 * progress);
                adjustmentHeight = adjustmentFrom + (adjustmentTo - adjustmentFrom) * eased;
                adjustmentReveal.Height = adjustmentHeight;
                adjustmentReveal.Opacity = adjustmentOpacityFrom + ((adjustmentOpen ? 1 : 0) - adjustmentOpacityFrom) * eased;
                adjustmentClip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, panel.ActualWidth), Math.Max(0, adjustmentHeight));
                adjustmentReveal.Clip = adjustmentClip;
                adjustmentRotation.Angle = adjustmentAngleFrom + ((adjustmentOpen ? 90 : 0) - adjustmentAngleFrom) * eased;
                if (progress >= 1) FinishAdjustmentReveal();
            }
            void UpdateAdjustmentReveal()
            {
                adjustmentFrom = adjustmentAnimating ? adjustmentHeight : adjustmentReveal.Visibility == Visibility.Collapsed ? 0 : adjustmentReveal.ActualHeight;
                adjustmentBody.Height = double.NaN;
                adjustmentBody.Measure(new Windows.Foundation.Size(Math.Max(1, panel.ActualWidth), double.PositiveInfinity));
                double naturalHeight = adjustmentBody.DesiredSize.Height + adjustmentReveal.Padding.Top + adjustmentReveal.Padding.Bottom;
                adjustmentTo = adjustmentOpen ? naturalHeight : 0;
                adjustmentOpacityFrom = adjustmentReveal.Visibility == Visibility.Collapsed ? 0 : adjustmentReveal.Opacity;
                adjustmentAngleFrom = adjustmentRotation.Angle;
                adjustmentReveal.IsHitTestVisible = adjustmentOpen;
                if (!root.IsLoaded || !animationSettings.AnimationsEnabled)
                { FinishAdjustmentReveal(); return; }
                adjustmentBody.Height = Math.Max(0, naturalHeight - adjustmentReveal.Padding.Top - adjustmentReveal.Padding.Bottom);
                adjustmentReveal.Visibility = Visibility.Visible;
                adjustmentClock.Restart();
                if (!adjustmentAnimating) { adjustmentAnimating = true; CompositionTarget.Rendering += AnimateAdjustmentReveal; }
                AnimateAdjustmentReveal(null, null!);
            }
            Closed += (_, __) => { CompositionTarget.Rendering -= AnimateAdjustmentReveal; adjustmentClock.Stop(); };
            refreshAdjustmentReveal = () => { if (adjustmentOpen) UpdateAdjustmentReveal(); };
            adjustmentHeader.Content = adjustmentTitle;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(adjustmentHeader, "调休，展开编辑");
            adjustmentHeader.Click += (_, __) =>
            {
                bool open = adjustmentOpen = !adjustmentOpen;
                UpdateAdjustmentReveal();

                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(adjustmentHeader, open ? "调休，收起编辑" : "调休，展开编辑");
            };
            var adjustmentGroup = new StackPanel();
            adjustmentGroup.Children.Add(adjustmentHeader);
            adjustmentBody.Children.Add(new TextBlock { Text = "手动安排优先于每周规则和节假日", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });

            var adjustGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6 };
            adjustGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            adjustGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            adjustGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            adjustGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var from = new CalendarDatePicker { Date = DateTimeOffset.Now.Date, PlaceholderText = "开始日期", Height = 38, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            var to = new CalendarDatePicker { Date = DateTimeOffset.Now.Date, PlaceholderText = "结束日期", Height = 38, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            var compactCalendar = new Style { TargetType = typeof(CalendarView), BasedOn = (Style)Application.Current.Resources["DefaultCalendarViewStyle"] };
            compactCalendar.Setters.Add(new Setter(FrameworkElement.WidthProperty, 240d));
            compactCalendar.Setters.Add(new Setter(FrameworkElement.HeightProperty, 292d));
            compactCalendar.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
            compactCalendar.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
            compactCalendar.Setters.Add(new Setter(CalendarView.DayItemFontSizeProperty, 13d));
            compactCalendar.Setters.Add(new Setter(CalendarView.MonthYearItemFontSizeProperty, 13d));
            compactCalendar.Setters.Add(new Setter(CalendarView.DayItemMarginProperty, new Thickness(0)));
            var compactDay = new Style { TargetType = typeof(CalendarViewDayItem), BasedOn = (Style)Application.Current.Resources[typeof(CalendarViewDayItem)] };
            compactDay.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 32d));
            compactDay.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
            compactDay.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            compactDay.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            compactCalendar.Setters.Add(new Setter(CalendarView.CalendarViewDayItemStyleProperty, compactDay));
            foreach (var picker in new[] { from, to })
            {
                picker.MinWidth = 0;
                picker.CalendarViewStyle = compactCalendar;





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
                if (b < DateTime.Today) { adjErr.Text = "请选择今天或之后的日期。"; adjErr.Visibility = Visibility.Visible; return; }
                // Ranges touching the past start today; earlier days were auto-cleaned.
                if (a < DateTime.Today) a = DateTime.Today;
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
            adjustmentGroup.Children.Add(adjustmentReveal);
            panel.Children.Add(adjustmentGroup);
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

        void UpdateRestPresetIndicator(int choice, bool animate = true)
        {
            if (restPresetIndicator == null || restPresetTrack == null || choice < 0 || choice >= restPresetButtons.Count) return;
            var button = restPresetButtons[choice];
            if (button.ActualWidth <= 0) return;
            double x = button.TransformToVisual(restPresetTrack).TransformPoint(default).X;
            bool initialized = !double.IsNaN(restPresetTargetX);
            bool changed = !initialized || Math.Abs(x - restPresetTargetX) > .1;
            restPresetIndicator.Width = button.ActualWidth;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(restPresetIndicator);
            if (!animate || !initialized || !root.IsLoaded || !animationSettings.AnimationsEnabled)
            {
                visual.StopAnimation("Scale");
                visual.Scale = System.Numerics.Vector3.One;
                visual.StopAnimation("Translation.X");
                visual.Properties.InsertVector3("Translation", new System.Numerics.Vector3((float)x, 0, 0));
            }
            else if (changed)
            {
                var easing = visual.Compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(.33f, 0), new System.Numerics.Vector2(.67f, 1));
                var travel = visual.Compositor.CreateScalarKeyFrameAnimation();
                travel.Duration = TimeSpan.FromMilliseconds(240);
                travel.InsertExpressionKeyFrame(0, "this.StartingValue");
                travel.InsertKeyFrame(1, (float)x, easing);
                visual.StartAnimation("Translation.X", travel);
                // Squash inward around the centre; never grow beyond the segment.
                visual.CenterPoint = new System.Numerics.Vector3((float)button.ActualWidth / 2, (float)button.ActualHeight / 2, 0);
                var bounce = visual.Compositor.CreateVector3KeyFrameAnimation();
                bounce.Duration = travel.Duration;
                bounce.InsertExpressionKeyFrame(0, "this.StartingValue");
                if (Math.Abs(x - restPresetTargetX) <= button.ActualWidth * 1.5)
                {
                    // Short moves need only a gentle single compression.
                    bounce.InsertKeyFrame(.25f, new System.Numerics.Vector3(.985f, .93f, 1));
                }
                else
                {
                    bounce.InsertKeyFrame(.15f, new System.Numerics.Vector3(.93f, .78f, 1));
                    bounce.InsertKeyFrame(.62f, System.Numerics.Vector3.One);
                    bounce.InsertKeyFrame(.8f, new System.Numerics.Vector3(.96f, .9f, 1));
                }
                bounce.InsertKeyFrame(1, System.Numerics.Vector3.One);
                visual.StartAnimation("Scale", bounce);
            }
            restPresetTargetX = x;
        }
        void UpdateRestPresets()
        {
            if (weekdays.Count != 7 || restPresetButtons.Count != 4) return;
            int mask = weekdays.Select((day, index) => day.IsChecked == true ? 1 << index : 0).Sum();
            int count = OffWorkWidget.Logic.RestDayCount(mask);
            int choice = alternatingWeekends ? 2 : customRestPreset ? 3 : count == 2 ? 0 : count == 1 ? 1 : 3;
            restWeekHint.Text = alternatingWeekends ? "选中的是本周休息日。" : "选择休息日";
            for (int i = 0; i < restPresetButtons.Count; i++)
            {
                var preset = restPresetButtons[i]; bool selected = i == choice;
                preset.IsChecked = selected;
                preset.Foreground = inkBrush;
            }
            UpdateRestPresetIndicator(choice);
            UpdateAlternatingWeekReveal(alternatingWeekends);
            RefreshScheduleEditor();
        }

        void UpdateAlternatingWeekReveal(bool show)
        {
            if (alternatingWeekReveal == null || alternatingWeekShown == show) return;
            alternatingWeekShown = show;
            if (!root.IsLoaded || !animationSettings.AnimationsEnabled)
            {
                FinishAlternatingWeekReveal(show);
                return;
            }
            double width = Math.Max(1, form.ActualWidth - 32);
            alternatingWeekRow.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
            restRowNaturalHeight = Math.Max(1, alternatingWeekRow.DesiredSize.Height);
            double fromHeight = animatingRestRow ? restRowHeight
                : alternatingWeekReveal.Visibility == Visibility.Visible ? alternatingWeekReveal.ActualHeight : 0;
            restRowFromHeight = Math.Clamp(fromHeight, 0, restRowNaturalHeight);
            restRowToHeight = show ? restRowNaturalHeight : 0;
            restRowFromOpacity = animatingRestRow
                ? alternatingWeekReveal.Opacity
                : alternatingWeekReveal.Visibility == Visibility.Visible ? alternatingWeekReveal.Opacity : 0;
            restRowToOpacity = show ? 1 : 0;
            alternatingWeekReveal.Visibility = Visibility.Visible;
            alternatingWeekReveal.Height = restRowFromHeight;
            alternatingWeekReveal.Opacity = restRowFromOpacity;
            alternatingWeekReveal.Margin = new Thickness(0, 0, 0, 10 * restRowFromHeight / restRowNaturalHeight);
            alternatingWeekReveal.IsHitTestVisible = show;
            restRowClip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, form.ActualWidth), restRowFromHeight);
            alternatingWeekReveal.Clip = restRowClip;
            restRowClock.Restart();
            if (!animatingRestRow) { animatingRestRow = true; CompositionTarget.Rendering += AnimateAlternatingWeekReveal; }
            AnimateAlternatingWeekReveal(null, null!);
        }

        void AnimateAlternatingWeekReveal(object? sender, object args)
        {
            if (windowClosed) { CompositionTarget.Rendering -= AnimateAlternatingWeekReveal; animatingRestRow = false; restRowClock.Stop(); return; }
            double position = animationSettings.AnimationsEnabled ? Math.Clamp(restRowClock.Elapsed.TotalMilliseconds / 260, 0, 1) : 1;
            double remaining = 1 - position;
            double eased = 1 - remaining * remaining * remaining;
            restRowHeight = restRowFromHeight + (restRowToHeight - restRowFromHeight) * eased;
            alternatingWeekReveal.Height = restRowHeight;
            alternatingWeekReveal.Opacity = restRowFromOpacity + (restRowToOpacity - restRowFromOpacity) * eased;
            alternatingWeekReveal.Margin = new Thickness(0, 0, 0, 10 * restRowHeight / restRowNaturalHeight);
            restRowClip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, form.ActualWidth), Math.Max(0, restRowHeight));
            if (position >= 1) FinishAlternatingWeekReveal(restRowToHeight > 0);
        }

        void FinishAlternatingWeekReveal(bool show)
        {
            CompositionTarget.Rendering -= AnimateAlternatingWeekReveal;
            animatingRestRow = false; restRowClock.Stop(); alternatingWeekShown = show;
            alternatingWeekReveal.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            alternatingWeekReveal.Height = show ? double.NaN : 0;
            alternatingWeekReveal.Opacity = show ? 1 : 0;
            alternatingWeekReveal.Margin = new Thickness(0, 0, 0, show ? 10 : 0);
            alternatingWeekReveal.Clip = null;
            alternatingWeekReveal.IsHitTestVisible = show;
            restWeekHint.Opacity = 1;
        }

        internal void PruneExpiredAdjustments()
        {
            RenderAdjustments();
        }

        void RenderAdjustments()
        {
            // Past make-up days have already taken effect; remove them instead
            // of leaving stale rows for the user to clean up by hand.
            string todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            adjustments.RemoveAll(a => string.CompareOrdinal(a.Date, todayKey) < 0);
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
                row.ColumnSpacing = 10;
                txt.TextWrapping = TextWrapping.Wrap;
                var del = new Button
                {
                    Content = "撤销安排", Height = 30, MinWidth = 0, FontSize = 12,
                    Padding = new Thickness(10, 2, 10, 2), CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    Foreground = subInkBrush, VerticalAlignment = VerticalAlignment.Center
                };
                del.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
                del.Resources["ButtonBackgroundPressed"] = timePressedBrush;
                del.Resources["ButtonForegroundPointerOver"] = inkBrush;
                del.Resources["ButtonForegroundPressed"] = inkBrush;
                ToolTipService.SetToolTip(del, "撤销这段日期的手动安排，按每周规则和节假日安排计算。保存后生效。");
                del.Click += (_, __) => { adjustments.RemoveAll(a => string.CompareOrdinal(a.Date, low) >= 0 && string.CompareOrdinal(a.Date, high) <= 0); RenderAdjustments(); };
                Grid.SetColumn(txt, 0); row.Children.Add(txt);
                Grid.SetColumn(del, 1); row.Children.Add(del);
                adjustmentRows.Children.Add(row);
                i = j + 1;
            }
            refreshAdjustmentReveal?.Invoke();
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
            var titles = new[] { "提醒喝水", "活动一下" };
            for (int i = 0; i < 2; i++)
            {
                var column = new StackPanel { Spacing = 8 };
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = titles[i], FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(switches[i], 1); row.Children.Add(switches[i]); column.Children.Add(row);
                column.Children.Add(new TextBlock { Text = "提醒间隔", FontSize = 13, Foreground = subInkBrush }); column.Children.Add(inputs[i]);
                Grid.SetColumn(column, i); grid.Children.Add(column);
            }
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(grid);
            var modeRow = new Grid { ColumnSpacing = 8 };
            modeRow.ColumnDefinitions.Add(new ColumnDefinition());
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            modeRow.Children.Add(new TextBlock { Text = "提醒方式", FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            reminderDisplay = new ComboBox { MinWidth = 112, Height = 36, FontSize = 13, Background = inputBackgroundBrush, Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8) };
            reminderDisplay.Items.Add("挂件内提醒"); reminderDisplay.Items.Add("弹窗提醒");
            reminderDisplay.SelectedIndex = current.ReminderPopup ? 1 : 0;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(reminderDisplay, "喝水与活动的提醒方式");
            Grid.SetColumn(reminderDisplay, 1); modeRow.Children.Add(reminderDisplay); panel.Children.Add(modeRow);
            var popupOptions = new StackPanel { Spacing = 10, Margin = new Thickness(0, 6, 0, 2) };
            reminderPosition = current.ReminderPosition;
            reminderX = current.ReminderX;
            reminderY = current.ReminderY;
            var positionSummary = new TextBlock { FontSize = 12, Foreground = subInkBrush };
            bool IsCenteredPosition()
            {
                if (reminderPosition == "center") return true;
                if (reminderPosition != "custom") return false;
                var area = DisplayArea.GetFromPoint(new PointInt32(reminderX, reminderY), DisplayAreaFallback.Primary).WorkArea;
                double dpi = Math.Max(96, GetDpiForWindow(hwnd)) / 96d;
                double dipWidth = double.IsFinite(current.ReminderWidth) ? Math.Clamp(current.ReminderWidth, 280, 800) : 360;
                double dipHeight = double.IsFinite(current.ReminderHeight) ? Math.Clamp(current.ReminderHeight, 190, 600) : 205;
                int width = Math.Min(area.Width, (int)Math.Ceiling(dipWidth * dpi));
                int height = Math.Min(area.Height, (int)Math.Ceiling(dipHeight * dpi));
                return Math.Abs(reminderX - (area.X + (area.Width - width) / 2)) <= 2 &&
                    Math.Abs(reminderY - (area.Y + (area.Height - height) / 2)) <= 2;
            }
            void RefreshPositionSummary()
            {
                int index = Array.IndexOf(ReminderPositions, reminderPosition);
                string[] names = { "右下角", "左下角", "右上角", "左上角", "屏幕居中" };
                positionSummary.Text = "当前位置：" + (IsCenteredPosition() ? "屏幕居中" : index >= 0 ? names[index] : "自定义");
            }
            RefreshPositionSummary();
            var positionCard = new StackPanel { Spacing = 6 };
            positionCard.Children.Add(new TextBlock { Text = "弹窗位置", FontSize = 13, Foreground = inkBrush });
            positionCard.Children.Add(positionSummary);
            var dragIcon = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
            dragIcon.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                // Two overlapping window frames with a pointer tail, matching the
                // settings icon language instead of a generic move cross.
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
                    "M8.6,1.8 L16.2,1.8 A2.2,2.2 0 0 1 18.4,4 L18.4,10.1 A2.2,2.2 0 0 1 16.2,12.3 L12.2,12.3 " +
                    "M3.4,8.7 L9.1,8.7 A1.8,1.8 0 0 1 10.9,10.5 L10.9,15.1 A1.8,1.8 0 0 1 9.1,16.9 L4.3,16.9 L1.3,18.7 L2.3,15.7 A1.8,1.8 0 0 1 1.7,14.2 L1.7,10.5 A1.8,1.8 0 0 1 3.4,8.7 Z"),
                Stroke = accentBlue, StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            });
            dragIcon.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 2.6, Height = 2.6, Fill = accentBlue,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(13.9, 3.6, 0, 0)
            });
            var positionButton = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        dragIcon,
                        new TextBlock { Text = "调整位置", FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, Height = 42,
                CornerRadius = new CornerRadius(12), Background = inputBackgroundBrush,
                BorderBrush = dividerBrush, BorderThickness = new Thickness(1), Foreground = inkBrush
            };
            positionButton.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
            positionButton.Resources["ButtonBackgroundPressed"] = timePressedBrush;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(positionButton, "调整位置");
            ToolTipService.SetToolTip(positionButton, "打开预览窗口，拖动调整弹窗位置");
            positionButton.Click += (_, __) => ShowPositionPreview(RefreshPositionSummary);
            positionCard.Children.Add(positionButton);
            popupOptions.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(18), Background = inputBackgroundBrush,
                BorderBrush = dividerBrush, BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12), Child = positionCard
            });
            popupOptions.Visibility = reminderDisplay.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            // A switch between 挂件内提醒 and 弹窗提醒 grows/collapses the position
            // card instead of snapping the section height.
            popupOptionsPanel = popupOptions;
            popupOptionsReveal = new Border
            {
                Child = popupOptions,
                Visibility = popupOptions.Visibility,
                Opacity = popupOptions.Visibility == Visibility.Visible ? 1 : 0,
                IsHitTestVisible = popupOptions.Visibility == Visibility.Visible
            };
            reminderDisplay.SelectionChanged += (_, __) => UpdatePopupOptionsReveal(reminderDisplay.SelectedIndex == 1);
            panel.Children.Add(popupOptionsReveal);
            return panel;
        }

        void UpdatePopupOptionsReveal(bool visible)
        {
            if (popupOptionsReveal == null) return;
            // A collapsed child measures to zero; keep it visible so the card can
            // be measured and faded out instead of flashing an empty slot.
            popupOptionsPanel.Visibility = Visibility.Visible;
            double contentWidth = Math.Max(1, popupOptionsReveal.ActualWidth > 0 ? popupOptionsReveal.ActualWidth :
                popupOptionsPanel.ActualWidth > 0 ? popupOptionsPanel.ActualWidth : form.ActualWidth - 32);
            popupOptionsPanel.Measure(new Windows.Foundation.Size(contentWidth, double.PositiveInfinity));
            double targetHeight = popupOptionsPanel.DesiredSize.Height;
            double height = visible ? targetHeight : 0;
            double currentHeight = animatingPopupOptions ? popupOptionsHeight :
                popupOptionsReveal.Visibility == Visibility.Collapsed ? 0 : popupOptionsReveal.ActualHeight;
            popupOptionsStartHeight = currentHeight; popupOptionsTargetHeight = height;
            popupOptionsStartOpacity = popupOptionsReveal.Opacity; popupOptionsTargetOpacity = visible ? 1 : 0;
            popupOptionsDuration = visible ? 260 : 300;
            popupOptionsElapsed = 0;
            popupOptionsLastTick = 0;
            popupOptionsReveal.IsHitTestVisible = visible;
            if (!root.IsLoaded || !animationSettings.AnimationsEnabled ||
                (Math.Abs(height - currentHeight) < .5 && Math.Abs(popupOptionsTargetOpacity - popupOptionsReveal.Opacity) < .01))
            { FinishPopupOptionsReveal(); return; }
            popupOptionsReveal.Visibility = Visibility.Visible;
            popupOptionsClock.Restart();
            if (!animatingPopupOptions) { animatingPopupOptions = true; CompositionTarget.Rendering += AnimatePopupOptionsReveal; }
            AnimatePopupOptionsReveal(null, null!);
        }

        void AnimatePopupOptionsReveal(object? sender, object args)
        {
            if (windowClosed) { CompositionTarget.Rendering -= AnimatePopupOptionsReveal; animatingPopupOptions = false; popupOptionsClock.Stop(); return; }
            double now = popupOptionsClock.Elapsed.TotalMilliseconds;
            // The reminder-mode flyout blocks rendering for ~150ms while it
            // closes. Advancing by wall time made the card jump 38% when frames
            // resumed; cap each tick so the collapse continues from where it was.
            double delta = popupOptionsLastTick == 0 ? 0 : Math.Min(20, Math.Max(0, now - popupOptionsLastTick));
            popupOptionsLastTick = now;
            popupOptionsElapsed += delta;
            double position = animationSettings.AnimationsEnabled ? Math.Clamp(popupOptionsElapsed / popupOptionsDuration, 0, 1) : 1;
            double eased = position * position * (3 - 2 * position);
            popupOptionsHeight = popupOptionsStartHeight + (popupOptionsTargetHeight - popupOptionsStartHeight) * eased;
            double opacity = popupOptionsStartOpacity + (popupOptionsTargetOpacity - popupOptionsStartOpacity) * eased;
            popupOptionsReveal.Height = popupOptionsHeight;
            popupOptionsReveal.Opacity = opacity;
            // The parent StackPanel reserves 12px of spacing as soon as the card
            // becomes visible; cancel it while the card is still near zero height.
            popupOptionsReveal.Margin = new Thickness(0, -12 * (1 - eased), 0, 0);
            // Keep the caption gap proportional so the first millimetres of the
            // reveal do not jump by the full top margin.
            popupOptionsPanel.Margin = new Thickness(0, 6 * opacity, 0, 2 * opacity);
            double width = Math.Max(1, popupOptionsReveal.ActualWidth > 0 ? popupOptionsReveal.ActualWidth : popupOptionsPanel.ActualWidth);
            popupOptionsClip.Rect = new Windows.Foundation.Rect(0, 0, width, Math.Max(0, popupOptionsHeight));
            popupOptionsReveal.Clip = popupOptionsClip;
            if (position >= 1) FinishPopupOptionsReveal();
        }

        void FinishPopupOptionsReveal()
        {
            CompositionTarget.Rendering -= AnimatePopupOptionsReveal; animatingPopupOptions = false; popupOptionsClock.Stop();
            bool show = popupOptionsTargetOpacity > 0;
            popupOptionsReveal.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            popupOptionsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            popupOptionsReveal.Height = double.NaN;
            popupOptionsReveal.Opacity = show ? 1 : 0;
            popupOptionsReveal.Clip = null;
            popupOptionsReveal.IsHitTestVisible = show;
            popupOptionsReveal.Margin = new Thickness(0);
            popupOptionsPanel.Margin = new Thickness(0, 6, 0, 2);
        }

        void ShowPositionPreview(Action onSaved)
        {
            if (positionPreview != null) { positionPreview.Activate(); return; }
            var preview = new Window { Title = "设置弹窗位置" };
            positionPreview = preview;
            IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(preview);
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }
            preview.ExtendsContentIntoTitleBar = true;
            preview.SetTitleBar(null);
            int style = GetWindowLong(handle, -16);
            SetWindowLong(handle, -16, (style & ~(0x00C00000 | 0x00040000 | 0x00020000 | 0x00010000)) | unchecked((int)0x80000000));
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
            int round = 1, noBorder = -2, ncDisabled = 1;
            DwmSetWindowAttribute(handle, 2, ref ncDisabled, 4);
            DwmSetWindowAttribute(handle, 33, ref round, 4);
            DwmSetWindowAttribute(handle, 34, ref noBorder, 4);

            var area = reminderPosition == "custom"
                ? DisplayArea.GetFromPoint(new PointInt32(reminderX, reminderY), DisplayAreaFallback.Primary).WorkArea
                : DisplayArea.GetFromWindowId(native.Id, DisplayAreaFallback.Primary).WorkArea;
            double dpi = Math.Max(96, GetDpiForWindow(hwnd)) / 96d;
            double width = double.IsFinite(current.ReminderWidth) ? Math.Clamp(current.ReminderWidth, 280, 800) : 360;
            double height = double.IsFinite(current.ReminderHeight) ? Math.Clamp(current.ReminderHeight, 190, 600) : 205;
            var bounds = MainWindow.GetReminderBounds(area, reminderPosition, width, height, dpi);
            if (reminderPosition == "custom")
            {
                bounds.X = Math.Clamp(reminderX, area.X, area.X + area.Width - bounds.Width);
                bounds.Y = Math.Clamp(reminderY, area.Y, area.Y + area.Height - bounds.Height);
            }
            appWindow.MoveAndResize(bounds);

            const double corner = 24;
            var shell = new Grid { RequestedTheme = root.ActualTheme };
            shell.Resources["ContentControlThemeFontFamily"] = new FontFamily("Microsoft YaHei");
            var previewTextStyle = new Style(typeof(TextBlock));
            previewTextStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Microsoft YaHei")));
            shell.Resources[typeof(TextBlock)] = previewTextStyle;
            var frosted = new SystemBackdropElement
            {
                CornerRadius = new CornerRadius(corner), IsHitTestVisible = false,
                SystemBackdrop = new DesktopAcrylicBackdrop()
            };
            var tint = new Border { CornerRadius = new CornerRadius(corner), Background = glassTintBrush, IsHitTestVisible = false };
            var sheen = new Border
            {
                CornerRadius = new CornerRadius(corner - 1), Background = glassSheenBrush,
                Height = 132, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
            };
            var rim = new Border
            {
                CornerRadius = new CornerRadius(corner), BorderThickness = new Thickness(1),
                BorderBrush = glassRimBrush, IsHitTestVisible = false
            };
            var content = new Grid { Margin = new Thickness(10), Padding = new Thickness(16, 13, 16, 13) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel { Spacing = 3 };
            heading.Children.Add(new TextBlock { Text = "设置弹窗位置", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = inkBrush });
            heading.Children.Add(new TextBlock { Text = "拖动窗口到想要的位置", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(heading);
            var coordinate = new TextBlock { FontSize = 12, Foreground = subInkBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(coordinate, 1); content.Children.Add(coordinate);
            var actions = new Grid { ColumnSpacing = 8 };
            for (int i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Button MakeButton(string label, bool primary)
            {
                var button = new Button
                {
                    Content = label, Height = 34, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(2, 0, 2, 0),
                    CornerRadius = new CornerRadius(10), FontSize = 13,
                    FontWeight = primary ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    BorderThickness = primary ? new Thickness(0) : new Thickness(1),
                    BorderBrush = dividerBrush,
                    Background = primary ? accentBlue : inputBackgroundBrush,
                    Foreground = primary ? new SolidColorBrush(Microsoft.UI.Colors.White) : inkBrush
                };
                if (primary)
                {
                    button.Resources["ButtonBackgroundPointerOver"] = accentBlue;
                    button.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 105, 225));
                    button.Resources["ButtonForegroundPointerOver"] = button.Foreground;
                    button.Resources["ButtonForegroundPressed"] = button.Foreground;
                }
                else
                {
                    button.Resources["ButtonBackgroundPointerOver"] = timeHoverBrush;
                    button.Resources["ButtonBackgroundPressed"] = timePressedBrush;
                    button.Resources["ButtonForegroundPointerOver"] = inkBrush;
                    button.Resources["ButtonForegroundPressed"] = inkBrush;
                }
                return button;
            }
            var center = MakeButton("屏幕居中", false);
            var cancel = MakeButton("取消", false);
            var save = MakeButton("保存位置", true);
            Grid.SetColumn(cancel, 1); Grid.SetColumn(save, 2);
            actions.Children.Add(center); actions.Children.Add(cancel); actions.Children.Add(save);
            Grid.SetRow(actions, 2); content.Children.Add(actions);
            var card = new Border
            {
                Margin = new Thickness(10), CornerRadius = new CornerRadius(16),
                Background = cardBackgroundBrush, Child = content
            };
            shell.Children.Add(frosted); shell.Children.Add(tint); shell.Children.Add(sheen);
            shell.Children.Add(card); shell.Children.Add(rim);
            preview.SystemBackdrop = new WinUIEx.TransparentTintBackdrop(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            preview.Content = shell;

            Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? geometry = null;
            WinUIEx.Messaging.WindowMessageMonitor? frameMonitor = new WinUIEx.Messaging.WindowMessageMonitor(handle);
            WinUIEx.Messaging.WindowMessageMonitor? renderMonitor = null;
            IntPtr renderHandle = IntPtr.Zero;
            frameMonitor.WindowMessageReceived += SuppressFrameBackground;
            void UpdateContour()
            {
                // A composition clip keeps antialiased corners; a window region
                // only keeps whole pixels and leaves stair steps.
                double w = shell.ActualWidth, h = shell.ActualHeight;
                if ((w <= 0 || h <= 0) && GetClientRect(handle, out ClientRect client))
                {
                    double scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
                    w = (client.Right - client.Left) / scale; h = (client.Bottom - client.Top) / scale;
                }
                if (w > 0 && h > 0)
                {
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(shell);
                    if (geometry == null)
                    {
                        geometry = visual.Compositor.CreateRoundedRectangleGeometry();
                        visual.Clip = visual.Compositor.CreateGeometricClip(geometry);
                    }
                    geometry.Size = new System.Numerics.Vector2((float)w, (float)h);
                    geometry.CornerRadius = new System.Numerics.Vector2((float)corner);
                }
                IntPtr render = FindWindowEx(handle, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
                if (render != IntPtr.Zero && render != renderHandle)
                {
                    renderMonitor?.Dispose(); renderHandle = render;
                    renderMonitor = new WinUIEx.Messaging.WindowMessageMonitor(render);
                    renderMonitor.WindowMessageReceived += SuppressFrameBackground;
                }
            }
            shell.Loaded += (_, __) => UpdateContour();
            shell.SizeChanged += (_, __) => UpdateContour();
            bool IsPreviewCentered()
            {
                var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                var pos = appWindow.Position;
                var size = appWindow.Size;
                return Math.Abs(pos.X - (work.X + (work.Width - size.Width) / 2)) <= 2 &&
                    Math.Abs(pos.Y - (work.Y + (work.Height - size.Height) / 2)) <= 2;
            }
            void RefreshPreview()
            {
                var p = appWindow.Position;
                coordinate.Text = IsPreviewCentered() ? "当前位置：屏幕居中" : $"当前位置：{p.X}，{p.Y}";
            }
            bool resizing = false;
            appWindow.Changed += (_, e) =>
            {
                if (!e.DidPositionChange && !e.DidSizeChange) return;
                if (!resizing)
                {
                    var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                    double scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
                    int w = Math.Min(work.Width, (int)Math.Ceiling(width * scale));
                    int h = Math.Min(work.Height, (int)Math.Ceiling(height * scale));
                    if (appWindow.Size.Width != w || appWindow.Size.Height != h)
                    {
                        resizing = true; appWindow.Resize(new SizeInt32(w, h)); resizing = false;
                    }
                }
                RefreshPreview();
            };
            var dragTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            dragTimer.Interval = TimeSpan.FromMilliseconds(8);
            ScreenPoint dragStart = default;
            PointInt32 windowStart = default;
            dragTimer.Tick += (_, __) =>
            {
                if (GetCursorPos(out var point))
                    appWindow.Move(new PointInt32(windowStart.X + point.X - dragStart.X, windowStart.Y + point.Y - dragStart.Y));
                if ((GetAsyncKeyState(1) & 0x8000) == 0) dragTimer.Stop();
            };
            shell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(shell).Properties.IsLeftButtonPressed) return;
                for (var source = e.OriginalSource as DependencyObject; source != null && source != shell; source = VisualTreeHelper.GetParent(source))
                    if (source is Button) return;
                if (!GetCursorPos(out dragStart)) return;
                windowStart = appWindow.Position;
                dragTimer.Start();
                e.Handled = true;
            };
            center.Click += (_, __) =>
            {
                var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                var size = appWindow.Size;
                appWindow.Move(new PointInt32(work.X + (work.Width - size.Width) / 2, work.Y + (work.Height - size.Height) / 2));
            };
            cancel.Click += (_, __) => preview.Close();
            save.Click += (_, __) =>
            {
                var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                var pos = appWindow.Position; var size = appWindow.Size;
                bool centered = IsPreviewCentered();
                reminderX = Math.Clamp(pos.X, work.X, work.X + work.Width - Math.Min(size.Width, work.Width));
                reminderY = Math.Clamp(pos.Y, work.Y, work.Y + work.Height - Math.Min(size.Height, work.Height));
                reminderPosition = centered ? "center" : "custom";
                onSaved(); preview.Close();
            };
            preview.Closed += (_, __) =>
            {
                dragTimer.Stop();
                renderMonitor?.Dispose(); frameMonitor.Dispose();
                frosted.SystemBackdrop = null;
                preview.SystemBackdrop = null;
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(shell).Clip = null;
                geometry?.Dispose();
                if (positionPreview == preview) positionPreview = null;
            };
            preview.Activate(); UpdateContour(); RefreshPreview();
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
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, (isWater ? "喝水" : "活动") + "提醒间隔，小时和分钟");
            ToolTipService.SetToolTip(button, "滚动或输入提醒间隔，1分钟至12小时");
            button.Click += (_, __) => ShowTimeWheelCore(button, label, false, false,
                value => { if (isWater) waterMinutes = value; else walkMinutes = value; },
                isWater ? waterMinutes : walkMinutes);
            return button;
        }

        internal void RefreshMouseClickThrough(bool enabled)
        {
            current.MouseClickThrough = enabled;
            if (mouseClickThrough != null) mouseClickThrough.IsOn = enabled;
            UpdateHoverDragAvailability();
        }

        void UpdateHoverDragAvailability()
        {
            bool enabled = mouseClickThrough?.IsOn == true;
            if (hoverDragRow is null) return;
            hoverOptionsRevealAction?.Invoke(enabled);
        }

        void BuildWindowOptionsCard()
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = "始终置顶", FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center };
            topmost = CreateGreenSwitch(current.Topmost);
            Grid.SetColumn(title, 0); row.Children.Add(title);
            Grid.SetColumn(topmost, 1); row.Children.Add(topmost);
            var behavior = new StackPanel { Spacing = 14 };
            behavior.Children.Add(row);
            behavior.Children.Add(new Border { Height = 1, Background = dividerBrush });
            var throughRow = new Grid { ColumnSpacing = 12 };
            throughRow.ColumnDefinitions.Add(new ColumnDefinition());
            throughRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var throughLabels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            throughLabels.Children.Add(new TextBlock { Text = "鼠标穿透", FontSize = 14, Foreground = inkBrush, TextWrapping = TextWrapping.Wrap });
            throughLabels.Children.Add(new TextBlock { Text = "点击可穿透下一层", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });
            mouseClickThrough = CreateGreenSwitch(current.MouseClickThrough);
            mouseClickThrough.VerticalAlignment = VerticalAlignment.Center;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mouseClickThrough, "鼠标穿透");
            ToolTipService.SetToolTip(mouseClickThrough, "开启后点击穿透到桌面；悬停可临时拖动挂件");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(mouseClickThrough, "开启后点击穿透到桌面；悬停可临时拖动挂件");
            throughRow.Children.Add(throughLabels);
            Grid.SetColumn(mouseClickThrough, 1); throughRow.Children.Add(mouseClickThrough);
            behavior.Children.Add(throughRow);

            hoverDragRow = new Grid { ColumnSpacing = 12 };
            hoverDragRow.ColumnDefinitions.Add(new ColumnDefinition());
            hoverDragRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hoverLabels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            hoverLabels.Children.Add(new TextBlock { Text = "悬停拖动", FontSize = 14, Foreground = inkBrush, TextWrapping = TextWrapping.Wrap });
            int initialDragSeconds = Math.Clamp(current.HoverDragSeconds, 1, 3);
            var hoverDragHint = new TextBlock { Text = $"悬停 {initialDragSeconds} 秒后可拖动", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap };
            hoverLabels.Children.Add(hoverDragHint);
            hoverDragSeconds = new ComboBox
            {
                Height = 34, Width = 92, MinWidth = 0, FontSize = 13, Background = inputBackgroundBrush,
                Foreground = inkBrush, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            for (int seconds = 1; seconds <= 3; seconds++) hoverDragSeconds.Items.Add(seconds + " 秒");
            hoverDragSeconds.SelectedIndex = initialDragSeconds - 1;
            hoverDragSeconds.SelectionChanged += (_, __) =>
            {
                int seconds = hoverDragSeconds.SelectedIndex + 1;
                if (seconds >= 1) hoverDragHint.Text = $"悬停 {seconds} 秒后可拖动";
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(hoverDragSeconds, "鼠标穿透悬停拖动时长");
            ToolTipService.SetToolTip(hoverDragSeconds, "鼠标穿透时，悬停该时长即可临时拖动挂件");
            hoverDragRow.Children.Add(hoverLabels);
            Grid.SetColumn(hoverDragSeconds, 1); hoverDragRow.Children.Add(hoverDragSeconds);

            var hoverOptions = new StackPanel { Spacing = 14 };
            hoverOptions.Children.Add(hoverDragRow);
            hoverOptions.Children.Add(BuildHoverEffectSelector());

            // These two rows only exist while click-through is on; they reveal
            // with an eased height/opacity transition.
            var hoverReveal = new Border { Child = hoverOptions, Visibility = Visibility.Collapsed };
            var hoverClip = new RectangleGeometry();
            var hoverClock = new Stopwatch();
            bool hoverShown = !current.MouseClickThrough, hoverAnimating = false;
            double hoverHeight = 0, hoverFrom = 0, hoverTarget = 0, hoverOpacityFrom = 0;
            void FinishHoverReveal()
            {
                CompositionTarget.Rendering -= AnimateHoverReveal;
                hoverClock.Stop(); hoverAnimating = false; hoverHeight = hoverTarget;
                hoverReveal.Height = double.NaN; hoverOptions.Height = double.NaN;
                hoverReveal.Clip = null;
                hoverReveal.Visibility = hoverShown ? Visibility.Visible : Visibility.Collapsed;
                hoverReveal.Opacity = hoverShown ? 1 : 0;
                hoverReveal.IsHitTestVisible = hoverShown;
            }
            void AnimateHoverReveal(object? sender, object args)
            {
                double progress = animationSettings.AnimationsEnabled ? Math.Clamp(hoverClock.Elapsed.TotalMilliseconds / 220, 0, 1) : 1;
                double eased = progress * progress * (3 - 2 * progress);
                hoverHeight = hoverFrom + (hoverTarget - hoverFrom) * eased;
                hoverReveal.Height = hoverHeight;
                hoverReveal.Opacity = hoverOpacityFrom + ((hoverShown ? 1 : 0) - hoverOpacityFrom) * eased;
                hoverClip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, behavior.ActualWidth), Math.Max(0, hoverHeight));
                hoverReveal.Clip = hoverClip;
                if (progress >= 1) FinishHoverReveal();
            }
            void SetHoverOptionsVisible(bool show)
            {
                if (hoverShown == show && !hoverAnimating) return;
                hoverShown = show;
                hoverFrom = hoverAnimating ? hoverHeight : hoverReveal.Visibility == Visibility.Collapsed ? 0 : hoverReveal.ActualHeight;
                hoverOptions.Height = double.NaN;
                hoverOptions.Measure(new Windows.Foundation.Size(Math.Max(1, behavior.ActualWidth), double.PositiveInfinity));
                double natural = hoverOptions.DesiredSize.Height;
                hoverTarget = show ? natural : 0;
                hoverOpacityFrom = hoverReveal.Visibility == Visibility.Collapsed ? 0 : hoverReveal.Opacity;
                hoverReveal.IsHitTestVisible = show;
                if (!root.IsLoaded || !animationSettings.AnimationsEnabled) { FinishHoverReveal(); return; }
                hoverOptions.Height = natural;
                hoverReveal.Visibility = Visibility.Visible;
                hoverClock.Restart();
                if (!hoverAnimating) { hoverAnimating = true; CompositionTarget.Rendering += AnimateHoverReveal; }
                AnimateHoverReveal(null, null!);
            }
            hoverOptionsRevealAction = SetHoverOptionsVisible;
            behavior.Children.Add(hoverReveal);
            Closed += (_, __) => { CompositionTarget.Rendering -= AnimateHoverReveal; hoverClock.Stop(); };
            mouseClickThrough.Toggled += (_, __) => UpdateHoverDragAvailability();
            UpdateHoverDragAvailability();
            behavior.Children.Add(new Border { Height = 1, Background = dividerBrush });
            behavior.Children.Add(BuildStartupRow());
            appearanceOptions.Children.Add(CreateDivider());
            appearanceOptions.Children.Add(CreateOptionsSection("使用偏好", behavior));
        }

        FrameworkElement CreateOptionsSection(string title, FrameworkElement content, Action? onOpen = null)
        {
            var section = new StackPanel();
            var header = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
                Padding = new Thickness(0), MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            header.Resources["ButtonBackgroundPointerOver"] = header.Background;
            header.Resources["ButtonBackgroundPressed"] = header.Background;
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(new TextBlock { Text = title, FontSize = 14, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center });
            var arrow = CreateChevron();
            var rotation = (RotateTransform)arrow.RenderTransform;
            var arrowSurface = CreateChevronSurface(header, arrow);
            Grid.SetColumn(arrowSurface, 1); heading.Children.Add(arrowSurface);
            header.Content = heading;
            var reveal = new Border { Child = content, Padding = new Thickness(0, 10, 0, 4), Visibility = Visibility.Collapsed };
            var clip = new RectangleGeometry();
            var clock = new Stopwatch();
            bool open = false, animating = false;
            double height = 0, from = 0, target = 0, fromOpacity = 0, fromAngle = 0;
            void Finish()
            {
                CompositionTarget.Rendering -= Animate;
                clock.Stop(); animating = false; height = target;
                reveal.Height = double.NaN; content.Height = double.NaN; reveal.Clip = null;
                reveal.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                reveal.Opacity = open ? 1 : 0; rotation.Angle = open ? 90 : 0;
            }
            void Animate(object? sender, object args)
            {
                double p = animationSettings.AnimationsEnabled ? Math.Clamp(clock.Elapsed.TotalMilliseconds / 260, 0, 1) : 1;
                double eased = p * p * (3 - 2 * p);
                height = from + (target - from) * eased;
                reveal.Height = height; reveal.Opacity = fromOpacity + ((open ? 1 : 0) - fromOpacity) * eased;
                rotation.Angle = fromAngle + ((open ? 90 : 0) - fromAngle) * eased;
                clip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(1, section.ActualWidth), Math.Max(0, height));
                reveal.Clip = clip;
                if (p >= 1) Finish();
            }
            header.Click += (_, __) =>
            {
                open = !open;
                if (open) onOpen?.Invoke();
                from = animating ? height : reveal.Visibility == Visibility.Collapsed ? 0 : reveal.ActualHeight;
                content.Height = double.NaN;
                content.Measure(new Windows.Foundation.Size(Math.Max(1, section.ActualWidth), double.PositiveInfinity));
                double natural = content.DesiredSize.Height;
                target = open ? natural + reveal.Padding.Top + reveal.Padding.Bottom : 0;
                fromOpacity = reveal.Visibility == Visibility.Collapsed ? 0 : reveal.Opacity;
                fromAngle = rotation.Angle;
                reveal.IsHitTestVisible = open;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(header, title + (open ? "，收起" : "，展开"));
                if (!animationSettings.AnimationsEnabled) { Finish(); return; }
                content.Height = natural; reveal.Visibility = Visibility.Visible;
                clock.Restart();
                if (!animating) { animating = true; CompositionTarget.Rendering += Animate; }
                Animate(null, null!);
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(header, title + "，展开");
            Closed += (_, __) => { CompositionTarget.Rendering -= Animate; clock.Stop(); };
            section.Children.Add(header); section.Children.Add(reveal);
            return section;
        }
        Grid BuildStartupRow()
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = "开机启动", FontSize = 14, Foreground = inkBrush, TextWrapping = TextWrapping.Wrap });
            var hint = new TextBlock { Text = "登录后自动显示挂件", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap };
            labels.Children.Add(hint);
            bool canRead = true;
            try { startupInitiallyEnabled = StartupRegistration.IsEnabled(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            { canRead = false; hint.Text = "无法读取启动项，请检查系统权限后重试。"; }
            startup = CreateGreenSwitch(startupInitiallyEnabled);
            startup.IsEnabled = canRead;
            startup.VerticalAlignment = VerticalAlignment.Center;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(startup, "开机启动");
            row.Children.Add(labels);
            Grid.SetColumn(startup, 1); row.Children.Add(startup);
            return row;
        }
        FrameworkElement BuildHoverEffectSelector()
        {
            string[] ids = { OffWorkWidget.HoverEffects.Halo, OffWorkWidget.HoverEffects.Aurora, OffWorkWidget.HoverEffects.Orbit };
            int count = ids.Length, repeats = 7, visibleRows = 3;
            double rowHeight = 42, viewportHeight = rowHeight * visibleRows;
            double padding = (viewportHeight - rowHeight) / 2;

            FrameworkElement Icon(string id, double size)
            {
                var host = new Grid { Width = size, Height = size, IsHitTestVisible = false };
                switch (OffWorkWidget.HoverEffects.Normalize(id))
                {
                    case OffWorkWidget.HoverEffects.Halo:
                        var glow = new RadialGradientBrush
                        {
                            Center = new Windows.Foundation.Point(.5, .5),
                            GradientOrigin = new Windows.Foundation.Point(.5, .5),
                            RadiusX = .5, RadiusY = .5
                        };
                        glow.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(230, accentBlue.Color.R, accentBlue.Color.G, accentBlue.Color.B) });
                        glow.GradientStops.Add(new GradientStop { Offset = .55, Color = Windows.UI.Color.FromArgb(80, accentBlue.Color.R, accentBlue.Color.G, accentBlue.Color.B) });
                        glow.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, accentBlue.Color.R, accentBlue.Color.G, accentBlue.Color.B) });
                        host.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = size * .86, Height = size * .86, Fill = glow, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                        host.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = size * .22, Height = size * .22, Fill = inkBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                        break;
                    case OffWorkWidget.HoverEffects.Aurora:
                        var aurora = new Canvas { Width = size, Height = size };
                        var auroraBlue = new RadialGradientBrush
                        {
                            Center = new Windows.Foundation.Point(.5, .5),
                            GradientOrigin = new Windows.Foundation.Point(.45, .45),
                            RadiusX = .5, RadiusY = .5
                        };
                        auroraBlue.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(225, accentBlue.Color.R, accentBlue.Color.G, accentBlue.Color.B) });
                        auroraBlue.GradientStops.Add(new GradientStop { Offset = .6, Color = Windows.UI.Color.FromArgb(70, accentBlue.Color.R, accentBlue.Color.G, accentBlue.Color.B) });
                        auroraBlue.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, accentBlue.Color.R, accentBlue.Color.G, accentBlue.Color.B) });
                        var auroraWarm = new RadialGradientBrush
                        {
                            Center = new Windows.Foundation.Point(.5, .5),
                            GradientOrigin = new Windows.Foundation.Point(.5, .5),
                            RadiusX = .5, RadiusY = .5
                        };
                        auroraWarm.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(200, 255, 217, 168) });
                        auroraWarm.GradientStops.Add(new GradientStop { Offset = .6, Color = Windows.UI.Color.FromArgb(55, 255, 217, 168) });
                        auroraWarm.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, 255, 217, 168) });
                        var auroraBlueDot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = size * .86, Height = size * .86, Fill = auroraBlue };
                        Canvas.SetLeft(auroraBlueDot, size * .02); Canvas.SetTop(auroraBlueDot, size * .02);
                        var auroraWarmDot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = size * .58, Height = size * .58, Fill = auroraWarm };
                        Canvas.SetLeft(auroraWarmDot, size * .38); Canvas.SetTop(auroraWarmDot, size * .38);
                        aurora.Children.Add(auroraBlueDot); aurora.Children.Add(auroraWarmDot);
                        host.Children.Add(aurora);
                        break;
                    case OffWorkWidget.HoverEffects.Orbit:
                        var orbit = new Canvas { Width = size, Height = size };
                        var orbitRing = new Microsoft.UI.Xaml.Shapes.Ellipse
                        {
                            Width = size * .74, Height = size * .74, Stroke = accentBlue,
                            StrokeThickness = Math.Max(1, size * .07)
                        };
                        Canvas.SetLeft(orbitRing, size * .13); Canvas.SetTop(orbitRing, size * .13);
                        var orbitDot = new Microsoft.UI.Xaml.Shapes.Ellipse
                        {
                            Width = size * .18, Height = size * .18, Fill = inkBrush
                        };
                        Canvas.SetLeft(orbitDot, size * .67); Canvas.SetTop(orbitDot, size * .1);
                        orbit.Children.Add(orbitRing); orbit.Children.Add(orbitDot);
                        host.Children.Add(orbit);
                        break;
                }
                return host;
            }

            var iconHost = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
            var effectName = new TextBlock { Text = OffWorkWidget.HoverEffects.DisplayName(hoverEffect), FontSize = 13, Foreground = inkBrush, VerticalAlignment = VerticalAlignment.Center, IsTextScaleFactorEnabled = false };
            var arrow = CreateChevron(90); arrow.Width = arrow.Height = 14;
            var selectorContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            selectorContent.Children.Add(iconHost); selectorContent.Children.Add(effectName); selectorContent.Children.Add(arrow);
            var selector = new Button
            {
                Content = selectorContent,
                Width = 92, MinWidth = 0, Height = 34,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(8),
                Background = inputBackgroundBrush,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            selector.Resources["ButtonBackgroundPointerOver"] = inputBackgroundBrush;
            selector.Resources["ButtonBackgroundPressed"] = inputBackgroundBrush;
            ToolTipService.SetToolTip(selector, "选择穿透悬停到点时的特效");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(selector, "悬停特效");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(selector, "HoverEffectSelector");

            var wheelRows = new StackPanel();
            wheelRows.Children.Add(new Border { Height = padding });
            var rowBorders = new List<Border>();
            var rowScales = new List<CompositeTransform>();
            var rowPops = new List<Grid>();
            var wheelViewer = new ScrollViewer
            {
                Height = viewportHeight, Content = wheelRows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
                VerticalSnapPointsType = SnapPointsType.None,
                VerticalSnapPointsAlignment = Microsoft.UI.Xaml.Controls.Primitives.SnapPointsAlignment.Center,
                IsTabStop = true, UseSystemFocusVisuals = false
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(wheelViewer, "悬停特效选择");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(wheelViewer, "HoverEffectWheel");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(wheelViewer, "上下滚动选择悬停特效，点击或关闭完成选择");
            Action<int>? finishRow = null;
            const double hoverIconRestScale = 1, hoverIconSelectedScale = 1.32;
            for (int index = 0; index < count * repeats; index++)
            {
                int wheelIndex = index;
                string id = ids[index % count];
                var rowScale = new CompositeTransform { ScaleX = 1, ScaleY = 1 };
                var icon = Icon(id, 26);
                icon.RenderTransform = rowScale;
                icon.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
                // The pop host carries the Q弹 spring so it never fights the
                // proximity scale used while the wheel is scrolling.
                var popHost = new Grid { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                popHost.Children.Add(icon);
                var content = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(popHost);
                content.Children.Add(new TextBlock { Text = OffWorkWidget.HoverEffects.DisplayName(id), FontSize = 12, Foreground = inkBrush, HorizontalAlignment = HorizontalAlignment.Center, IsTextScaleFactorEnabled = false });
                var row = new Border { Height = rowHeight, Child = content, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Opacity = 0 };
                row.Tapped += (_, __) =>
                {
                    wheelViewer.ChangeView(null, wheelIndex * rowHeight, null, false);
                    finishRow?.Invoke(wheelIndex);
                };
                rowBorders.Add(row); rowPops.Add(popHost); rowScales.Add(rowScale); wheelRows.Children.Add(row);
            }
            wheelRows.Children.Add(new Border { Height = padding });

            var wheelHost = new Grid { Height = viewportHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            wheelHost.Children.Add(wheelViewer);

            bool initialized = false, wheelOpen = false;
            double? positioningOffset = null;
            bool wheelSpringing = false;
            double wheelSpringPos = 0, wheelSpringVel = 0, wheelSpringGoal = 0;
            long wheelSpringLast = 0;
            int previousFirst = 0, previousLast = -1;
            void ApplyEffect(string id)
            {
                hoverEffect = OffWorkWidget.HoverEffects.Normalize(id);
                iconHost.Children.Clear();
                iconHost.Children.Add(Icon(hoverEffect, 20));
                effectName.Text = OffWorkWidget.HoverEffects.DisplayName(hoverEffect);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(selector, "当前：" + OffWorkWidget.HoverEffects.DisplayName(hoverEffect));
            }
            void UpdateWheelSelection()
            {
                double position = wheelViewer.VerticalOffset / rowHeight;
                int index = Math.Clamp((int)Math.Round(position), 0, count * repeats - 1);
                if (wheelOpen && positioningOffset == null) ApplyEffect(ids[index % count]);
                for (int i = previousFirst; i <= previousLast; i++)
                {
                    rowBorders[i].Opacity = 0;
                    rowScales[i].ScaleX = rowScales[i].ScaleY = 1;
                }
                int first = Math.Max(0, (int)Math.Floor(position) - 3);
                int last = Math.Min(rowBorders.Count - 1, (int)Math.Ceiling(position) + 3);
                for (int i = first; i <= last; i++)
                {
                    double distance = Math.Abs(i - position);
                    rowBorders[i].Opacity = Math.Max(0, 1 - distance * .42);
                    double proximity = Math.Max(0, 1 - distance);
                    double eased = proximity * proximity * (3 - 2 * proximity);
                    rowScales[i].ScaleX = rowScales[i].ScaleY =
                        hoverIconRestScale + (hoverIconSelectedScale - hoverIconRestScale) * eased;
                }
                previousFirst = first; previousLast = last;
            }
            void StepWheel(int direction)
            {
                int center = (int)Math.Round(wheelViewer.VerticalOffset / rowHeight);
                if (wheelOpen && center >= 0 && center < rowBorders.Count) PopHoverIcon(center);
                positioningOffset = null;
                if (!wheelSpringing)
                {
                    wheelSpringing = true;
                    wheelSpringPos = wheelViewer.VerticalOffset / rowHeight;
                    wheelSpringVel = 0;
                    wheelSpringGoal = wheelSpringPos;
                    wheelSpringLast = Stopwatch.GetTimestamp();
                    CompositionTarget.Rendering += AnimateWheelSpring;
                }
                wheelSpringGoal = Math.Clamp(wheelSpringGoal + direction, 0, count * repeats - 1);
            }
            void AnimateWheelSpring(object? sender, object e)
            {
                if (!wheelSpringing) return;
                if (windowClosed || !wheelOpen)
                {
                    wheelSpringing = false;
                    CompositionTarget.Rendering -= AnimateWheelSpring;
                    return;
                }
                long now = Stopwatch.GetTimestamp();
                double dt = Math.Min(.032, (now - wheelSpringLast) / (double)Stopwatch.Frequency);
                wheelSpringLast = now;
                // Critically damped spring keeps the wheel gliding between rows
                // instead of restarting the ScrollViewer's stepped animation.
                const double omega = 14;
                for (double left = dt; left > 0;)
                {
                    double step = Math.Min(left, .004);
                    double accel = omega * omega * (wheelSpringGoal - wheelSpringPos) - 2 * omega * wheelSpringVel;
                    wheelSpringVel += accel * step;
                    wheelSpringPos += wheelSpringVel * step;
                    left -= step;
                }
                if (Math.Abs(wheelSpringGoal - wheelSpringPos) < .0008 && Math.Abs(wheelSpringVel) < .02)
                {
                    wheelSpringPos = wheelSpringGoal; wheelSpringVel = 0; wheelSpringing = false;
                    CompositionTarget.Rendering -= AnimateWheelSpring;
                    wheelViewer.ChangeView(null, wheelSpringPos * rowHeight, null, true);
                    UpdateWheelSelection();
                    int settled = (int)Math.Round(wheelViewer.VerticalOffset / rowHeight);
                    if (settled < count || settled >= count * (repeats - 1))
                        PositionWheel(count * 3 + settled % count);
                    return;
                }
                wheelViewer.ChangeView(null, wheelSpringPos * rowHeight, null, true);
                UpdateWheelSelection();
            }
            void PopHoverIcon(int wheelIndex)
            {
                if (wheelIndex < 0 || wheelIndex >= rowPops.Count || !animationSettings.AnimationsEnabled) return;
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(rowPops[wheelIndex]);
                visual.CenterPoint = new System.Numerics.Vector3(13, 13, 0);
                var spring = visual.Compositor.CreateSpringVector3Animation();
                spring.DampingRatio = .3f;
                spring.Period = TimeSpan.FromMilliseconds(190);
                spring.InitialValue = new System.Numerics.Vector3(1.10f, 1.10f, 1);
                spring.FinalValue = new System.Numerics.Vector3(1, 1, 1);
                visual.StartAnimation("Scale", spring);
            }
            wheelHost.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, args) =>
            {
                int delta = args.GetCurrentPoint(wheelViewer).Properties.MouseWheelDelta;
                if (delta == 0) return;
                StepWheel(delta > 0 ? -1 : 1);
                args.Handled = true;
            }), true);
            void PositionWheel(int index)
            {
                double target = index * rowHeight;
                positioningOffset = target;
                wheelViewer.ChangeView(null, target, null, true);
                if (Math.Abs(wheelViewer.VerticalOffset - target) <= .1) positioningOffset = null;
            }
            EventHandler<object>? initialize = null;
            initialize = (_, __) =>
            {
                if (wheelViewer.ExtentHeight <= viewportHeight) return;
                initialized = true;
                wheelViewer.LayoutUpdated -= initialize;
                PositionWheel(count * 3 + Math.Max(0, Array.IndexOf(ids, hoverEffect)));
                UpdateWheelSelection();
            };
            wheelViewer.LayoutUpdated += initialize;
            wheelViewer.ViewChanged += (_, args) =>
            {
                if (!initialized || !wheelOpen) return;
                if (wheelSpringing) return;
                if (positioningOffset is double target)
                {
                    if (Math.Abs(wheelViewer.VerticalOffset - target) > .1) return;
                    positioningOffset = null;
                }
                UpdateWheelSelection();
                if (!args.IsIntermediate)
                {
                    int index = (int)Math.Round(wheelViewer.VerticalOffset / rowHeight);
                    if (Math.Abs(wheelViewer.VerticalOffset - index * rowHeight) > .1)
                    {
                        wheelViewer.ChangeView(null, index * rowHeight, null, false);
                        return;
                    }
                    if (index < count || index >= count * (repeats - 1))
                        PositionWheel(count * 3 + index % count);
                }
            };
            wheelViewer.KeyDown += (_, args) =>
            {
                int step = args.Key == Windows.System.VirtualKey.Up ? -1 : args.Key == Windows.System.VirtualKey.Down ? 1 : 0;
                if (step == 0) return;
                StepWheel(step);
                args.Handled = true;
            };

            var panel = new StackPanel { Width = 104 };
            panel.Children.Add(wheelHost);
            var focusSink = new Button
            {
                Width = 1, Height = 1, Opacity = 0, IsTabStop = true,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(focusSink,
                Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            var flyoutRoot = new Grid();
            flyoutRoot.Children.Add(panel);
            flyoutRoot.Children.Add(focusSink);

            var effectMenu = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Left };
            var menuSurface = new Border
            {
                CornerRadius = new CornerRadius(16), Padding = new Thickness(6),
                BorderThickness = new Thickness(1), Child = flyoutRoot
            };
            effectMenu.Content = menuSurface;
            var menuStyle = new Style(typeof(FlyoutPresenter));
            menuStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            menuStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(16)));
            menuStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
            menuStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            effectMenu.FlyoutPresenterStyle = menuStyle;
            finishRow = wheelIndex =>
            {
                ApplyEffect(ids[wheelIndex % count]);
                effectMenu.Hide();
            };

            bool keyboardOpen = false;
            effectMenu.Opening += (_, __) =>
            {
                wheelOpen = false;
                keyboardOpen = selector.FocusState == FocusState.Keyboard;
                panel.RequestedTheme = root.ActualTheme;
                bool dark = root.ActualTheme == ElementTheme.Dark;
                menuSurface.Background = MenuFrost(dark);
                menuSurface.BorderBrush = new SolidColorBrush(dark
                    ? Windows.UI.Color.FromArgb(30, 255, 255, 255)
                    : Windows.UI.Color.FromArgb(90, 255, 255, 255));
            };
            effectMenu.Closing += (_, __) =>
            {
                wheelOpen = false; positioningOffset = null;
                if (wheelSpringing)
                {
                    wheelSpringing = false;
                    CompositionTarget.Rendering -= AnimateWheelSpring;
                }
            };
            effectMenu.Opened += (_, __) =>
            {
                wheelOpen = true;
                PositionWheel(count * 3 + Math.Max(0, Array.IndexOf(ids, hoverEffect)));
                UpdateWheelSelection();
                focusSink.Focus(keyboardOpen ? FocusState.Keyboard : FocusState.Pointer);
                wheelViewer.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (wheelOpen) UpdateWheelSelection();
                });
            };
            focusSink.KeyDown += (_, args) =>
            {
                int step = args.Key == Windows.System.VirtualKey.Up ? -1
                    : args.Key == Windows.System.VirtualKey.Down ? 1 : 0;
                if (step != 0)
                {
                    StepWheel(step);
                    args.Handled = true;
                }
                else if (args.Key == Windows.System.VirtualKey.Escape)
                {
                    effectMenu.Hide();
                    args.Handled = true;
                }
            };

            void ShowEffectMenu()
            {
                // Always open beside the selector; the footer-aware fallback made
                // the wheel cover the rows at 2K/high-DPI layouts.
                effectMenu.Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Left;
                effectMenu.ShowAt(selector);
            }
            selector.Click += (_, __) => { if (effectMenu.IsOpen) effectMenu.Hide(); else ShowEffectMenu(); };
            ApplyEffect(hoverEffect);

            var selectorRow = new Grid { ColumnSpacing = 12 };
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition());
            selectorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = "悬停特效", FontSize = 14, Foreground = inkBrush, TextWrapping = TextWrapping.Wrap });
            labels.Children.Add(new TextBlock { Text = "悬停光标样式", FontSize = 12, Foreground = subInkBrush, TextWrapping = TextWrapping.Wrap });
            selectorRow.Children.Add(labels);
            Grid.SetColumn(selector, 1); selectorRow.Children.Add(selector);
            return selectorRow;
        }

        void BuildFooter()
        {
            var footer = new Border
            {
                Background = footerBackgroundBrush,
                BorderBrush = dividerBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 10, 16, 16)
            };
            footerBar = footer;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(error);

            var actions = new Grid { ColumnSpacing = 10 };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var save = new Button
            {
                Content = "保存",
                FontSize = 14, Padding = new Thickness(12, 0, 12, 0),
                Height = 38, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0),
                Background = accentBlue, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            save.Resources["ButtonBackgroundPointerOver"] = accentBlue;
            save.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 105, 225));
            save.Resources["ButtonForegroundPointerOver"] = save.Foreground;
            save.Resources["ButtonForegroundPressed"] = save.Foreground;
            save.Click += Save;
            Grid.SetColumn(save, 0); actions.Children.Add(save);

            var cancel = new Button
            {
                Content = "取消", FontSize = 14, Padding = new Thickness(12, 0, 12, 0),
                Height = 38, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0),
                Background = footerActionBrush, Foreground = inkBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cancel.Resources["ButtonBackgroundPointerOver"] = selectionBackgroundBrush;
            cancel.Resources["ButtonBackgroundPressed"] = selectionBackgroundBrush;
            cancel.Resources["ButtonForegroundPointerOver"] = inkBrush;
            cancel.Resources["ButtonForegroundPressed"] = inkBrush;
            footerCancel = cancel;
            cancel.Click += (_, __) => CloseSettings();
            Grid.SetColumn(cancel, 1); actions.Children.Add(cancel);

            backToTop = CreateHeaderIconButton("\uE74A", "回到顶部", () => settingsScroll.ChangeView(null, 0, null, false));
            backToTop.Width = backToTop.Height = 36; backToTop.CornerRadius = new CornerRadius(18);
            backToTop.VerticalAlignment = VerticalAlignment.Center;
            backToTop.HorizontalAlignment = HorizontalAlignment.Right;
            backToTop.RenderTransformOrigin = new Windows.Foundation.Point(1, .5);
            backToTop.RenderTransform = footerArrowScale;
            backToTop.BorderThickness = new Thickness(0);
            backToTop.Background = footerActionBrush;
            backToTop.Resources["ButtonBackgroundPointerOver"] = selectionBackgroundBrush;
            backToTop.Resources["ButtonBackgroundPressed"] = selectionBackgroundBrush;
            backToTop.Opacity = 0; backToTop.IsEnabled = false; backToTop.IsHitTestVisible = false; backToTop.IsTabStop = false;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(backToTop, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetColumn(backToTop, 1); actions.Children.Add(backToTop);
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
            if (salaryVisibilityIcon != null) salaryVisibilityIcon.Glyph = "\uED1A";
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
                EnsureAccordionExpanded(2);
                error.Text = "下班时间须晚于上班时间。"; error.Visibility = Visibility.Visible; return;
            }
            if (dayTimeEnabled && dayTimeEnd <= dayTimeStart)
            {
                dayTimeExpanded = true; RefreshDayTimeEditor(); EnsureAccordionExpanded(2);
                error.Text = "所选星期的下班时间须晚于上班时间。"; error.Visibility = Visibility.Visible; return;
            }
            if (lunchEnd <= lunchStart)
            {
                EnsureAccordionExpanded(2);
                error.Text = "午休结束时间须晚于开始时间。"; error.Visibility = Visibility.Visible; return;
            }

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

            var wishItems = new List<OffWorkWidget.WishEntry>();
            for (int i = 0; i < wishRows.Count; i++)
            {
                var row = wishRows[i];
                string rowName = row.Name.Text.Trim();
                string targetText = row.Target.Text.Trim();
                string savedText = row.Saved.Text.Trim();
                if (rowName.Length == 0 && targetText.Length == 0 && savedText.Length == 0) continue;
                if (rowName.Length == 0)
                {
                    error.Text = $"请填写第 {i + 1} 个心愿的名称。"; error.Visibility = Visibility.Visible;
                    return;
                }
                if (rowName.Length > 8)
                {
                    error.Text = $"第 {i + 1} 个心愿名称最多 8 个字。"; error.Visibility = Visibility.Visible;
                    return;
                }
                if (!decimal.TryParse(targetText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rowTarget) || rowTarget <= 0 || rowTarget > 100000000)
                {
                    error.Text = $"第 {i + 1} 个心愿的目标金额须大于 0 且不超过 100000000 元。"; error.Visibility = Visibility.Visible;
                    return;
                }
                if (!decimal.TryParse(savedText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rowSaved) || rowSaved < 0 || rowSaved > 100000000)
                {
                    error.Text = $"第 {i + 1} 个心愿的已攒金额须在 0 到 100000000 元之间。"; error.Visibility = Visibility.Visible;
                    return;
                }
                wishItems.Add(new OffWorkWidget.WishEntry { Id = row.Id, Name = rowName, Target = rowTarget, Saved = rowSaved });
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
                error.Text = "活动提醒间隔须为 1–720 分钟的整数。"; error.Visibility = Visibility.Visible;
                EnsureAccordionExpanded(3);
                return;
            }

            double popupWidth = double.IsFinite(current.ReminderWidth) ? Math.Clamp(current.ReminderWidth, 280, 800) : 360;
            double popupHeight = double.IsFinite(current.ReminderHeight) ? Math.Clamp(current.ReminderHeight, 190, 600) : 205;

            RememberSchedule();
            string wishStartDate = current.WishStartDate;
            if (wishEnabled.IsOn && string.IsNullOrWhiteSpace(wishStartDate))
                wishStartDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var next = new OffWorkWidget.Settings
            {
                Start = startTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                End = endTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                LunchEnabled = true,
                LunchStart = lunchStart.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                LunchEnd = lunchEnd.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
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
                ShowCoinJar = showCoinJar.IsOn,
                CoinJarX = current.CoinJarX,
                CoinJarY = current.CoinJarY,
                AmountVisibleSeconds = seconds,
                UseChinaHolidays = holidays.IsOn,
                Calendar = current.Calendar,
                RestDays = weekdays.Select((day, index) => day.IsChecked == true ? (1 << index) : 0).Sum(),
                SingleRestDays = singleRestDays,
                DoubleRestDays = doubleRestDays,
                AlternatingWeekends = alternatingWeekends,
                DoubleRestWeekStart = doubleRestWeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateAdjustments = adjustments.Select(a => new OffWorkWidget.DateAdjustment { Date = a.Date, IsWorkday = a.IsWorkday }).ToList(),
                WishEnabled = wishEnabled.IsOn,
                Wishes = wishItems,
                WishAllocation = (wishAllocation.SelectedItem as ComboBoxItem)?.Tag as string ?? "sequential",
                WishStartDate = wishStartDate,
                WishName = wishItems.Count > 0 ? wishItems[0].Name : "",
                WishTarget = wishItems.Count > 0 ? wishItems[0].Target : 0,
                WishSaved = wishItems.Count > 0 ? wishItems[0].Saved : 0,
                WaterReminder = water.IsOn,
                WalkReminder = walk.IsOn,
                ReminderPopup = reminderDisplay.SelectedIndex == 1,
                ReminderPosition = reminderPosition,
                ReminderX = reminderX,
                ReminderY = reminderY,
                ReminderWidth = popupWidth,
                ReminderHeight = popupHeight,
                WaterMinutes = waterVal,
                WalkMinutes = walkVal,
                Topmost = topmost.IsOn,
                MouseClickThrough = mouseClickThrough.IsOn,
                HoverDragSeconds = Math.Clamp(hoverDragSeconds.SelectedIndex + 1, 1, 3),
                HoverEffect = hoverEffect,
                X = current.X,
                Y = current.Y
            };

            try
            {
                OffWorkWidget.Logic.Validate(next);
                saveSettings(next);
                petCommitted = true;
                if (startup.IsEnabled && startup.IsOn != startupInitiallyEnabled)
                {
                    try
                    {
                        StartupRegistration.SetEnabled(startup.IsOn);
                        startupInitiallyEnabled = startup.IsOn;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                    {
                        error.Text = "其他设置已保存，但开机启动设置失败：" + ex.Message;
                        error.Visibility = Visibility.Visible;
                        return;
                    }
                }
                CloseSettings();
            }
            catch (Exception ex)
            {
                error.Text = "未保存：" + ex.Message;
                error.Visibility = Visibility.Visible;
            }
        }

        static BitmapImage PetImage(string id) => new BitmapImage(new Uri("ms-appx:///assets/" + (id == "polar-bear" ? "polar-bear-idle" : id) + ".png"));
        static string PetName(string id) => id switch { "panda" => "熊猫", "polar-bear" => "北极熊", _ => "狸花猫" };

        [StructLayout(LayoutKind.Sequential)] struct ClientRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct ScreenPoint { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out ScreenPoint point);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int style);
        internal void BringToFront()
        {
            if (windowClosed) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
            Activate();
            raisedForActivation = true;
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0003); // HWND_TOPMOST, no resize or move
            SetForegroundWindow(hwnd);
        }

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out ClientRect rect);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    }
}
