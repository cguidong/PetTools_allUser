using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;

namespace OffWorkWidget.WinUI;

public sealed partial class MainWindow : Window
{
    Settings settings = new Settings();
    double? previewScale;
    bool scalePreviewHostActive, preparingScalePreviewHost, scalePreviewSettled, scalePreviewRevealPending;
    DispatcherQueueTimer? scalePreviewSettleTimer;
    DispatcherQueueTimer? hoverDragTimer;
    DispatcherQueueTimer? dragReleaseTimer;
    long hoverDragSinceTick;
    bool hoverDragArmed;
    bool hoverMonitorActive;
    // Countdown waits until the pointer rests in place, so sweeping across
    // the click-through widget never consumes the hover delay.
    const int HoverHoldDelayMs = 300;
    const int HoverHoldDriftPx = 5;
    const int HoverMoveSamplePx = 2;
    long hoverHoldSinceTick;
    int hoverHoldX, hoverHoldY, hoverSampleX, hoverSampleY;
    double scaleViewportDpi, scaleViewportScale;
    (int Left, int Top, int Right, int Bottom, int Diameter)? surfaceClipBounds;
    int scalePreviewWidth, scalePreviewHeight, scalePreviewRevealFrames;
    int scalePreviewInsetX, scalePreviewInsetY, scalePreviewInsetWidth, scalePreviewInsetHeight;
    const double ScaleReference = .75;
    double EffectiveScale => previewScale ?? settings.Scale;
    double PhysicalScale => Math.Clamp(EffectiveScale, .5, 1.5) * ScaleReference;
    readonly string dataFolder;
    readonly bool smoke;
    bool smokeLayoutCycle, smokeScaleCycle, smokeScaleFast;
    DispatcherQueueTimer? cycleStartTimer, cycleTimer;
    readonly PrivacyStore privacy;
    readonly ChinaHolidayCalendar calendar;
    readonly Grid surface = new Grid();
    readonly Grid hostSurface = new Grid { Opacity = 0 };
    DispatcherQueueTimer? startupRevealTimer;
    bool startupRevealStarted, startupMaskActive = true;
    Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? surfaceGeometry;
    readonly Border card = new Border { CornerRadius = new CornerRadius(36), Margin = new Thickness(0) };
    readonly Border backdropCard = new Border();
    readonly Canvas ambientEffects = new Canvas { IsHitTestVisible = false };
    readonly ScaleTransform ambientEffectScale = new ScaleTransform();
    readonly System.Collections.Generic.HashSet<Storyboard> ambientEffectStories = new();
    readonly Canvas foregroundLayer = new Canvas();
    readonly ScaleTransform contentScale = new ScaleTransform();
    readonly Canvas coins = new Canvas { IsHitTestVisible = false };
    readonly Button coinBankButton = new Button { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    readonly TranslateTransform coinJarPosition = new TranslateTransform();
    Point coinJarPressPoint, coinJarPressPosition;
    bool coinJarPointerDown, coinJarMoved, suppressCoinJarClick;
    readonly Border coinBankBalance = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TextBlock coinBankBalanceText = new TextBlock { IsTextScaleFactorEnabled = false };
    Canvas? coinJarFillLayer;
    DispatcherQueueTimer? coinBankTimer;
    readonly Canvas coinOverlay = new Canvas { IsHitTestVisible = false };
    readonly Canvas hoverEffectLayer = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Canvas hoverEffectRoot = new Canvas { Width = 120, Height = 120, RenderTransformOrigin = new Point(.5, .5), IsHitTestVisible = false };
    readonly CompositeTransform hoverEffectTransform = new CompositeTransform();
    readonly SolidColorBrush hoverEffectAccent = new(Colors.Transparent);
    readonly SolidColorBrush hoverEffectCompanion = new(Colors.Transparent);
    readonly RadialGradientBrush hoverEffectHalo = new();
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverHaloVisual;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverReadyRing;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverReadyRingOuter;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverRipple1;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverRipple2;
    Microsoft.UI.Xaml.Shapes.Path? hoverOrbitTrail;
    PathFigure? hoverOrbitTrailFigure;
    PolyLineSegment? hoverOrbitTrailSegment;
    Microsoft.UI.Xaml.Shapes.Path? hoverOrbitTrailSoft;
    PathFigure? hoverOrbitTrailSoftFigure;
    PolyLineSegment? hoverOrbitTrailSoftSegment;
    Microsoft.UI.Xaml.Shapes.Path? hoverOrbitProgress;
    PathFigure? hoverOrbitProgressFigure;
    ArcSegment? hoverOrbitProgressSegment;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverOrbitHead;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverOrbitGlow;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverOrbitTrack;
    readonly RadialGradientBrush hoverOrbitGlowBrush = new();
    readonly SolidColorBrush hoverOrbitInk = new();
    readonly SolidColorBrush hoverOrbitRingBrush = new();
    readonly SolidColorBrush hoverOrbitHeadBrush = new();
    Canvas? hoverAuroraLayer;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverAuroraA;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverAuroraB;
    Microsoft.UI.Xaml.Shapes.Ellipse? hoverAuroraC;
    Microsoft.UI.Xaml.Shapes.Path? hoverAuroraCrescent;
    readonly RotateTransform hoverAuroraRotate = new();
    readonly ScaleTransform hoverAuroraScale = new();
    readonly ScaleTransform hoverHaloScale = new();
    readonly ScaleTransform hoverReadyScale = new();
    readonly ScaleTransform hoverReadyOuterScale = new();
    ScaleTransform? hoverRipple1Scale;
    ScaleTransform? hoverRipple2Scale;
    Storyboard? hoverBloomStoryboard;
    bool hoverEffectVisible, hoverEffectReady, hoverEffectLoopActive;
    long hoverEffectStartTick, hoverEffectReadyAt;
    string hoverEffectStyle = HoverEffects.Halo;
    bool? hoverEffectDark;
    bool hoverEffectPale;
    readonly Microsoft.UI.Xaml.Controls.Primitives.Popup coinPopup = new()
    {
        ShouldConstrainToRootBounds = false, IsLightDismissEnabled = false, IsHitTestVisible = false
    };
    const double CoinHeadroom = 90;
    bool trackingCoins;
    readonly TranslateTransform petClickLift = new TranslateTransform();
    Storyboard? petClickAnimation;
    int emittedCoins;
    long coinScoreLastAt;
    long nextAmbientEffectAt;
    bool ambientPreviewPending;
    readonly System.Collections.Generic.Dictionary<string, BitmapImage> ambientSprites = new();
    int ambientEffectKind = -1, ambientParticlesActive;
    bool coinScoreHasEvent;
    readonly TextBlock coinScore = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTextScaleFactorEnabled = false, TextLineBounds = TextLineBounds.Full, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock coinScoreTarget = new TextBlock { IsTextScaleFactorEnabled = false, TextLineBounds = TextLineBounds.Full, Opacity = .65, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock coinScoreHeading = new TextBlock { Text = "累计金币", IsTextScaleFactorEnabled = false, TextLineBounds = TextLineBounds.Full, Opacity = .65 };
    readonly TextBlock coinScoreDivider = new TextBlock { Text = "/", IsTextScaleFactorEnabled = false, TextLineBounds = TextLineBounds.Full, Opacity = .4, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel coinScoreNumbers = new StackPanel { Orientation = Orientation.Horizontal };
    readonly StackPanel coinScoreWords = new StackPanel();
    readonly StackPanel coinScoreContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Viewbox coinScoreRingHost = new Viewbox { VerticalAlignment = VerticalAlignment.Center };
    readonly SolidColorBrush coinScoreRingAccent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 181, 134));
    readonly ArcSegment coinScoreRingSegment = new ArcSegment { Size = new Windows.Foundation.Size(10, 10), SweepDirection = SweepDirection.Clockwise };
    readonly Microsoft.UI.Xaml.Shapes.Path coinScoreRingArc = new Microsoft.UI.Xaml.Shapes.Path { StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    readonly Border coinScoreBadge = new Border
    {
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed
    };
    readonly System.Collections.Generic.HashSet<Storyboard> coinStories = new();
    Border? activeCoinUpgrade;
    readonly TextBlock caption = new TextBlock { FontSize = 14, CharacterSpacing = 15, FontFamily = new FontFamily("Microsoft YaHei"), FontWeight = Microsoft.UI.Text.FontWeights.Normal };
    readonly TextBlock countdown = new TextBlock { FontSize = 36, FontWeight = Microsoft.UI.Text.FontWeights.Medium, FontFamily = new FontFamily("Segoe UI"), Margin = new Thickness(0, 4, 0, 10) };
    readonly StreamingText captionStream, countdownStream;
    readonly TextBlock rest = new TextBlock(), pay = new TextBlock(), earned = new TextBlock();
    readonly System.Collections.Generic.List<TextBlock> statTitles = new();


    readonly Button earningsEye = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)), MinWidth = 0, MinHeight = 0 };
    readonly FontIcon earningsVisibilityIcon = new() { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Glyph = "\uED1A", IsTextScaleFactorEnabled = false };
    readonly RadialGradientBrush eyeGlass = new();
    readonly RadialGradientBrush eyeHoverGlass = new();
    readonly RadialGradientBrush eyePressedGlass = new();
    readonly StackPanel salaryArea = new StackPanel { Spacing = 5 };
    readonly Grid statsRow = new Grid();
    readonly InteractiveCursorGrid statsPages = new InteractiveCursorGrid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    readonly InteractiveCursorGrid statsLayout = new InteractiveCursorGrid();
    readonly StackPanel statsDots = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    readonly System.Collections.Generic.List<Microsoft.UI.Xaml.Shapes.Ellipse> statsDotShapes = new();
    readonly System.Collections.Generic.List<FrameworkElement> statsPageItems = new();
    readonly System.Collections.Generic.List<FrameworkElement> statsCorePages = new();
    readonly System.Collections.Generic.List<StackPanel> wishPagePanels = new();
    readonly System.Collections.Generic.List<TextBlock> wishPageNames = new();
    readonly System.Collections.Generic.List<TextBlock> wishPagePercents = new();
    readonly System.Collections.Generic.List<Grid> wishBarHosts = new();
    readonly System.Collections.Generic.List<Border> wishBarTracks = new();
    readonly System.Collections.Generic.List<Border> wishBarFills = new();
    readonly System.Collections.Generic.List<ColumnDefinition> wishBarFillColumns = new();
    readonly System.Collections.Generic.List<ColumnDefinition> wishBarRestColumns = new();
    bool statsWishMode;
    readonly System.Collections.Generic.HashSet<string> completedWishKeys = new();
    bool wishCompletionPrimed;
    string statsWishSignature = "";
    int statsWishCount, statsDotActiveIndex = -1;
    int statsPageIndex, statsAvailablePages;
    bool statsPageAnimating;
    int statsPageQueued = -1, statsPageQueuedDirection, statsPagePending = -1;
    Storyboard? statsPageStory, statsDotStory, statsDragStory;
    readonly RectangleGeometry statsPageClip = new();
    double statsDragStartY, statsDragOffset;
    bool statsDragging, statsDragMoved, statsDragSettling;
    int statsDragCurrent, statsDragAdjacent = -1, statsDragDirection;
    uint statsDragPointerId;
    readonly Border stats = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Image pet = new Image { Width = 132, Height = 150, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    readonly Image waterPetOverlay = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    PetAnimationPlayer? petAnimation;
    PetAnimationPlayer? slackAnimation;
    bool slackActive, slackLoading, slackMouseBlocked;
    long nextSlackAt;
    PetAnimationPlayer? reminderAnimation;
    PetAnimationPlayer? coinPetAnimation;
    bool coinPetActive;
    long coinPetRequestedAt = -10000;
    bool reminderPetActive;
    int reminderAnimationRevision, reminderDue, pendingReminders;
    PetAnimationPlayer? napAnimation;
    bool napActive, napPending;
    int napRevision;
    DateTime napEndAt = DateTime.MinValue;
    PetAnimationPlayer? startWorkAnimation;
    bool startWorkActive;
    int startWorkRevision;
    DispatcherQueueTimer? petHoverTimer;
    delegate IntPtr KeyboardHookProc(int code, IntPtr message, IntPtr data);
    KeyboardHookProc? petEnterHookProc;
    IntPtr petEnterHook;
    bool petEnterHeld, petEnterHandled;
    int petAnimationRevision;
    readonly DispatcherQueueTimer timer;
    DispatcherQueueTimer? smokeTimer;
    bool previewFrozen, countdownIsStatus;
    readonly ReminderSchedule reminders = new ReminderSchedule();
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    readonly SolidColorBrush statsBackground = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    readonly SolidColorBrush glassFallback = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    readonly SolidColorBrush statsForeground = new SolidColorBrush(Colors.White);
    readonly LiquidGlassWinUI.LiquidGlassBrush statsGlass = new LiquidGlassWinUI.LiquidGlassBrush
    {
        BlurAmount = 6, RefThickness = 1, RefFactor = 1.003, RefDispersion = 0,
        GlareFactor = 4, RefFresnelFactor = 4, ShapeRoundness = 2,
        TintR = 40, TintG = 40, TintB = 44, TintA = .36
    };
    bool statsDark = true, statsAppearanceInitialized;
    readonly Stopwatch statsAppearanceClock = new Stopwatch();
    double statsTone = 1, statsFromTone = 1, statsTargetTone = 1;
    readonly Windows.UI.ViewManagement.UISettings uiSettings = new Windows.UI.ViewManagement.UISettings();
    readonly SolidColorBrush foreground = new SolidColorBrush(Colors.White);
    readonly AppWindow native;
    readonly GlassBackdrop glass;
    readonly DesktopGlassSource desktopSource;
    double pixelScale = 1;
    readonly IntPtr hwnd;
    NativeTrayIcon? tray;
    WinUIEx.Messaging.WindowMessageMonitor? sessionMonitor;
    static readonly IntPtr handCursor = LoadCursor(IntPtr.Zero, (IntPtr)32649 /* IDC_HAND */);
    bool sessionNotificationRegistered;
    double nextSessionRegistrationAttempt;
    SettingsWindow? settingsWindow;
    Window? reminderPopup;
    Action<int>? refreshReminderPopup;
    Mutex? instance;
    EventWaitHandle? activation;
    RegisteredWaitHandle? activationWait;
    EventWaitHandle? installerExit;
    RegisteredWaitHandle? installerExitWait;
    DateTime revealedUntil = DateTime.MaxValue, reminderUntil;
    DateTime holidayDay;
    bool exiting;
    string exitReason = "unspecified";
    double lightness = 1, textFromTone = 1, textTargetTone = 1;
    bool textAppearanceInitialized;
    readonly Stopwatch textAppearanceClock = new Stopwatch();
    readonly Stopwatch glassAppearanceClock = new Stopwatch();
    bool glassAppearanceInitialized;
    double glassTone, glassFromTone, glassTargetTone, glassTint, glassFromTint, glassTargetTint;
    bool analysisRegionsDirty = true;
    readonly DispatcherQueueTimer walkTimer;

    public MainWindow()
    {
        smoke = Environment.GetCommandLineArgs().Contains("--smoke-test");
        string? isolatedDataFolder = Environment.GetEnvironmentVariable("OFFWORKWIDGET_TEST_DATA");
        dataFolder = isolatedDataFolder ?? (smoke ? Path.Combine(Path.GetTempPath(), "OffWorkWidget-WinUI-smoke") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OffWorkWidget"));
        Directory.CreateDirectory(dataFolder);
        GlassProfile.Configure(dataFolder);
        Title = "有盼";
        Diag("start pid=" + Environment.ProcessId);
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // Resize/presenter setup can expose native backing before XAML is ready.
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, 0, 2 /* LWA_ALPHA */);
        native = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        string instanceKey = Environment.UserName + (isolatedDataFolder == null ? "" : "-WinUI-Isolated");
        if (!smoke)
        {
            instance = new Mutex(true, "Local\\OffWorkWidget-" + instanceKey, out bool first);
            if (!first)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    try { using var signal = EventWaitHandle.OpenExisting("Local\\OffWorkWidget-Activate-" + instanceKey); signal.Set(); break; }
                    catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
                }
                instance.Dispose(); Environment.Exit(0);
            }
            activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\OffWorkWidget-Activate-" + instanceKey);
            activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, __) => DispatcherQueue.TryEnqueue(ShowWidget), null, Timeout.Infinite, false);
            installerExit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\OffWorkWidget-InstallerExit-" + instanceKey);
            installerExitWait = ThreadPool.RegisterWaitForSingleObject(installerExit, (_, __) => DispatcherQueue.TryEnqueue(() => { exitReason = "installer"; Exit(); }), null, Timeout.Infinite, false);
        }
        string config = Path.Combine(dataFolder, "settings.xml");
        bool wishAnchorRepaired = false;
        if (File.Exists(config))
        {
            try
            {
                using var stream = File.OpenRead(config);
                var loaded = (Settings?)new XmlSerializer(typeof(Settings)).Deserialize(stream);
                if (loaded == null) throw new InvalidDataException("设置文件内容为空。");
                if (loaded.Scale > 1.5 && loaded.Scale <= 2) loaded.Scale = 1.5; // Preserve existing settings from the former range.
                loaded.HoverEffect = HoverEffects.Normalize(loaded.HoverEffect);
                Logic.Repair(loaded);
                // Existing wish pools keep the current cycle's progress, then never
                // drop back when the next payday arrives.
                if (loaded.WishEnabled &&
                    !DateTime.TryParseExact(loaded.WishStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    loaded.WishStartDate = Logic.CurrentPayCycleStart(DateTime.Today, loaded).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    wishAnchorRepaired = true;
                }
                Logic.Validate(loaded);
                string configText = File.ReadAllText(config);
                if (!configText.Contains("<ScaleReferenceVersion>") || loaded.ScaleReferenceVersion < 1)
                {
                    loaded.Scale = Math.Clamp(loaded.Scale / ScaleReference, .5, 1.5);
                    loaded.ScaleReferenceVersion = 1;
                }
                if (!configText.Contains("<ShowSalaryLayout>")) loaded.ShowSalaryLayout = !loaded.HideAmount;
                settings = loaded;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException || ex is OverflowException)
            {
                try { File.Copy(config, config + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                wishAnchorRepaired = false;
                settings = new Settings();
                Debug.WriteLine("设置文件无法读取，已使用默认设置；隐私数据库保持独立。");
            }
        }
        calendar = new ChinaHolidayCalendar(Path.Combine(dataFolder, "holidays"));
        settings.Calendar = calendar;
        privacy = new PrivacyStore(Path.Combine(dataFolder, "privacy.db"), settings.Salary);
        privacy.EffectParametersChanged += QueueAmbientPreview;
        settings.Salary = privacy.LoadSalary(); settings.HideAmount = true;
        if (wishAnchorRepaired) Save();
        emittedCoins = privacy.CoinCount;
        // Manual make-up work/rest entries only matter on their date; drop the
        // ones that have already passed so the list cleans itself up.
        string todayKey = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        bool prunedAdjustments = settings.DateAdjustments != null &&
            settings.DateAdjustments.RemoveAll(a => a == null || string.CompareOrdinal(a.Date, todayKey) < 0) > 0;
        if (prunedAdjustments) Save();
        // Keep the Run entry pointing at the current install when the app moved.
        if (!smoke && isolatedDataFolder == null)
        {
            try { StartupRegistration.RepairPathIfNeeded(); } catch { }
        }
        bool smokeSettings = false;
        if (smoke)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("--smoke-scale=") && double.TryParse(arg.Substring(14), NumberStyles.Float, CultureInfo.InvariantCulture, out double size)) settings.Scale = Math.Clamp(size, .5, 1.5);
                if (arg == "--smoke-dark") { settings.AutomaticAppearance = false; settings.NightMode = true; }
                if (arg == "--smoke-light") { settings.AutomaticAppearance = false; settings.NightMode = false; }
                if (arg == "--smoke-settings") smokeSettings = true;
            }
        }
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg == "--smoke-layout-cycle") smokeLayoutCycle = true;
            if (arg == "--smoke-scale-cycle") smokeScaleCycle = true;
            if (arg == "--smoke-scale-fast") smokeScaleFast = true;
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);
        Content = hostSurface;
        hostSurface.Children.Add(surface);
        hostSurface.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        surface.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        surface.Children.Add(backdropCard);
        ambientEffects.RenderTransform = ambientEffectScale;
        surface.Children.Add(ambientEffects);
        surface.Children.Add(foregroundLayer);
        foregroundLayer.Children.Add(card);
        card.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        captionStream = new StreamingText(caption);
        countdownStream = new StreamingText(countdown);
        BuildCard();
        coinOverlay.Children.Add(coins);
        coinPopup.Child = coinOverlay;
        coins.RenderTransform = contentScale;
        surface.Children.Add(waterPetOverlay);
        surface.LayoutUpdated += (_, __) => UpdateWaterOverlayBounds();
        coinScore.Foreground = coinScoreHeading.Foreground = coinScoreTarget.Foreground = coinScoreDivider.Foreground = foreground;
        coinScore.FontFamily = coinScoreTarget.FontFamily = coinScoreDivider.FontFamily = new FontFamily("Segoe UI");
        var scoreRing = new Canvas { Width = 26, Height = 26 };
        var scoreTrack = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 21.5, Height = 21.5, Stroke = foreground, StrokeThickness = 1.5, Opacity = .1 };
        Canvas.SetLeft(scoreTrack, 2.25); Canvas.SetTop(scoreTrack, 2.25); scoreRing.Children.Add(scoreTrack);
        var scoreFigure = new PathFigure { StartPoint = new Point(13, 3), IsFilled = false };
        scoreFigure.Segments.Add(coinScoreRingSegment);
        var scoreGeometry = new PathGeometry(); scoreGeometry.Figures.Add(scoreFigure);
        coinScoreRingArc.Data = scoreGeometry; coinScoreRingArc.Stroke = coinScoreRingAccent; scoreRing.Children.Add(coinScoreRingArc);
        coinScoreRingHost.Child = scoreRing;
        coinScoreNumbers.Children.Add(coinScore); coinScoreNumbers.Children.Add(coinScoreDivider); coinScoreNumbers.Children.Add(coinScoreTarget);
        coinScoreWords.Children.Add(coinScoreHeading); coinScoreWords.Children.Add(coinScoreNumbers);
        coinScoreContent.Children.Add(coinScoreRingHost); coinScoreContent.Children.Add(coinScoreWords);
        coinScoreBadge.Child = coinScoreContent;
        coinScoreBadge.SizeChanged += (_, __) => FitCaptionToColumn();
        surface.Children.Add(coinScoreBadge);
        BuildCoinBank();
        BuildHoverEffect();

        if (native.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
        int noBorder = -2, ncRenderingDisabled = 1, doNotRound = 1;
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, 4); // DWMWA_BORDER_COLOR = NONE
        DwmSetWindowAttribute(hwnd, 33, ref doNotRound, 4); // DWMWA_WINDOW_CORNER_PREFERENCE = DONOTROUND (handled precisely by SetWindowRgn)
        DwmSetWindowAttribute(hwnd, 2, ref ncRenderingDisabled, 4); // DWMWA_NCRENDERING_POLICY = DISABLED (prevents DWM caption/frame border line)
        var margins = new GlassMargins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        bool visibleTestWindow = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OFFWORKWIDGET_TEST_DATA")) && Environment.GetEnvironmentVariable("OFFWORKWIDGET_TEST_WINDOW") == "1";
        try { native.IsShownInSwitchers = visibleTestWindow; } catch { }
        try
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
            style |= WS_POPUP;
            SetWindowLong(hwnd, GWL_STYLE, style);

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, visibleTestWindow ? (exStyle | WS_EX_APPWINDOW) & ~WS_EX_TOOLWINDOW : (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        }
        catch { }
        string icon = Path.Combine(AppContext.BaseDirectory, "assets", "app-icon.ico");
        if (File.Exists(icon)) native.SetIcon(icon);
        glass = GlassBackdrop.Apply(this, backdropCard);
        surface.Loaded += (_, __) => EnsureRenderEraseMonitor();
        desktopSource = new DesktopGlassSource(this, surface, backdropCard);
        desktopSource.FrameUpdated = UpdateAppearance;
        glass.InteractionChanged = desktopSource.WakeRendering;
        desktopSource.NeedsAnimation = () => glass.IsAnimating || textAppearanceClock.IsRunning ||
            statsAppearanceClock.IsRunning || glassAppearanceClock.IsRunning;
        desktopSource.MetricsUpdated = UpdateAppearance;
        desktopSource.BeforeRender = () => { UpdateDragFrame(); UpdateScalePreviewBounds(rendering: true); UpdateStatsTransition(); UpdateTextTransition(); UpdateGlassTransition(); };
        surface.Loaded += (_, __) => UpdateRenderViewport();
        surface.Loaded += (_, __) => WarmWidgetMenu();
        hostSurface.Loaded += (_, __) => BeginStartupReveal();
        surface.LayoutUpdated += (_, __) => analysisRegionsDirty = true;
        caption.SizeChanged += (_, __) => analysisRegionsDirty = true;
        countdown.SizeChanged += (_, __) => analysisRegionsDirty = true;
        stats.SizeChanged += (_, __) => analysisRegionsDirty = true;
        native.Changed += (_, args) => { if (args.DidSizeChange) UpdateRenderViewport(); };
        ApplySettings();

        surface.PointerReleased += OnSurfacePointerReleased;
        surface.RightTapped += OnSurfaceRightTapped;
        card.PointerPressed += OnCardPointerPressed;
        card.PointerMoved += OnCardPointerMoved;
        card.PointerExited += (_, __) => glass.SetInteraction(0, 0, false);
        card.PointerReleased += OnCardPointerReleased;
        card.PointerCaptureLost += OnCardPointerCaptureLost;
        card.PointerCanceled += OnCardPointerCaptureLost;
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated) { EndDrag(null); glass.SetInteraction(0, 0, false); } };
        surface.DoubleTapped += (_, e) =>
        {
            var position = e.GetPosition(stats);
            if (settings.WishEnabled && position.X >= 0 && position.Y >= 0 &&
                position.X < stats.ActualWidth && position.Y < stats.ActualHeight)
            {
                e.Handled = true;
                return;
            }
            // A small pointer shift between the two taps can otherwise be
            // interpreted as a drag before the double-tap is recognized.
            if (restoreDoubleTapPosition)
            {
                EndDrag(null);
                if (native.Position.X != doubleTapStartWindow.X || native.Position.Y != doubleTapStartWindow.Y)
                {
                    native.Move(doubleTapStartWindow);
                    settings.X = doubleTapStartWindow.X; settings.Y = doubleTapStartWindow.Y;
                    Save(); desktopSource.Capture();
                }
                restoreDoubleTapPosition = false;
            }
            OpenSettings();
        };
        native.Closing += (_, e) => { if (!exiting) { EndDrag(null); Diag("closing -> hide"); e.Cancel = true; HideWidget(); } };
        Closed += (_, __) => { Diag("closed"); DisposeResources(); };
        sessionMonitor = new WinUIEx.Messaging.WindowMessageMonitor(this);
        sessionMonitor.WindowMessageReceived += OnSessionChanged;
        sessionMonitor.WindowMessageReceived += OnLayoutHitTest;
        sessionMonitor.WindowMessageReceived += OnEraseBackground;
        sessionMonitor.WindowMessageReceived += OnKeyMessage;
        TryRegisterSessionNotifications();

        petEnterHookProc = OnPetKeyboardHook;
        petEnterHook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, petEnterHookProc, GetModuleHandle(null), 0);
        if (petEnterHook == IntPtr.Zero) GlassProfile.Event("pet-keyboard-hook-error " + Marshal.GetLastWin32Error());
        else GlassProfile.Event("pet-keyboard-hook-ready");

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, __) =>
        {
            desktopSource.UpdateRemoteBackdrop();
            if (!sessionNotificationRegistered && elapsed.Elapsed.TotalSeconds >= nextSessionRegistrationAttempt) TryRegisterSessionNotifications();
            if (!smoke && !previewFrozen && elapsed.Elapsed.TotalSeconds > 5 && Environment.GetEnvironmentVariable("OFFWORKWIDGET_TEST_DATA") != null)
            {
                previewFrozen = true;
                File.WriteAllText(Path.Combine(dataFolder, "capture-status.txt"),
                    "Frames=" + desktopSource.FrameCount + "; Ready=" + desktopSource.IsReady + "; Error=" + desktopSource.LastError +
                    "; Glass=" + glass.LastError + "; Supported=" + glass.IsSupported);
            }
            if (smoke && !previewFrozen && elapsed.Elapsed.TotalSeconds > 5 && desktopSource.IsReady)
            {
                desktopSource.FreezeForPreview();
                previewFrozen = true;
                File.WriteAllText(Path.Combine(dataFolder, "preview-source.txt"), "Frames=" + desktopSource.FrameCount + "; Luminance=" + desktopSource.Luminance + "; TextLuminance=" + desktopSource.TextLuminance + "; LastError=" + desktopSource.LastError);
            }
            // Freeze text/appearance churn while the HWND is being dragged; the
            // next tick after release catches up. This keeps fast drags smooth.
            if (!isDragging)
            {
                Refresh();
                UpdateAppearance();
            }
            if (elapsed.ElapsedMilliseconds >= nextAmbientEffectAt)
            {
                nextAmbientEffectAt = elapsed.ElapsedMilliseconds + Random.Shared.Next(2500, 4501);
                ShowAmbientShopEffect();
            }
            KeepCoinCursor();
            if (coinScoreHasEvent) coinScoreBadge.Visibility = elapsed.ElapsedMilliseconds - coinScoreLastAt < 10000 ? Visibility.Visible : Visibility.Collapsed;
        };
        walkTimer = DispatcherQueue.CreateTimer();
        walkTimer.Interval = TimeSpan.FromMilliseconds(140);
        walkTimer.Tick += (_, __) =>
        {
            if (DateTime.Now >= reminderUntil)
            {
                walkTimer.Stop();
                CloseReminderPopup();
                if (settings.PetId == "polar-bear") EndPolarReminder();
                else { reminderDue = 0; LoadPet(); }
                StartNextReminder();
                return;
            }
        };
        timer.Start();
        if (smokeSettings)
        {
            var settingsTimer = DispatcherQueue.CreateTimer();
            settingsTimer.Interval = TimeSpan.FromMilliseconds(1200);
            settingsTimer.Tick += (_, __) => { settingsTimer.Stop(); OpenSettings(); };
            settingsTimer.Start();
        }
        if (!smoke) CreateTray(icon);
        Refresh();
        UpdateAppearance();
        if (smoke)
        {
            smokeTimer = DispatcherQueue.CreateTimer();
            var end = smokeTimer;
            end.Interval = TimeSpan.FromSeconds(300);
            end.Tick += (_, __) =>
            {
                File.WriteAllText(Path.Combine(dataFolder, "runtime.txt"), "LiquidGlass.LastError=" + LiquidGlassWinUI.LiquidGlassBrush.LastError + Environment.NewLine + "SalaryHidden=" + settings.HideAmount);
                end.Stop();
                Exit();
            };
            end.Start();
        }
        if (smokeLayoutCycle || smokeScaleCycle)
        {
            cycleStartTimer = DispatcherQueue.CreateTimer();
            cycleStartTimer.Interval = TimeSpan.FromSeconds(2.5);
            cycleStartTimer.IsRepeating = false;
            cycleStartTimer.Tick += (_, __) =>
            {
                GlassProfile.Event("cycle-start-timer");
                if (smokeLayoutCycle) RunSmokeLayoutCycle();
                else RunSmokeScaleCycle();
            };
            cycleStartTimer.Start();
        }
        // XAML opacity cannot hide the parent HWND's cold native backing.
        // Mask the whole window before its first Activate/Show, until the real
        // content viewport and transparent backing are initialized.
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, 0, 2 /* LWA_ALPHA */);
    }

    WidgetMenuWindow? widgetMenu;
    bool widgetMenuOpen;
    long widgetMenuShownAt;

    WidgetMenuWindow EnsureWidgetMenu()
    {
        if (widgetMenu != null) return widgetMenu;
        widgetMenu = new WidgetMenuWindow(command =>
        {
            switch (command)
            {
                case 1: OpenSettings(); break;
                case 2: HideWidget(); break;
                case 3: exitReason = "widget-menu"; Exit(); break;
            }
        }, () => widgetMenuOpen = false);
        return widgetMenu;
    }

    void WarmWidgetMenu()
    {
        if (disposed || exiting) return;
        try { EnsureWidgetMenu(); }
        catch (Exception ex) { Debug.WriteLine("右键菜单预热失败：" + ex.Message); }
    }
    void BuildCard()
    {
        var body = new Grid { Margin = new Thickness(24, 18, 12, 18), ColumnSpacing = 20 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        caption.Foreground = foreground; countdown.Foreground = foreground;
        text.Children.Add(captionStream); text.Children.Add(countdownStream);
        var restPage = Stat("距休息", rest); var payPage = Stat("距发薪", pay);
        restPage.IsHitTestVisible = false;
        payPage.IsHitTestVisible = false;
        var salaryHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var salaryTitle = new TextBlock { Text = "今天赚了", FontFamily = new FontFamily("Microsoft YaHei"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = statsForeground, VerticalAlignment = VerticalAlignment.Center };
        statTitles.Add(salaryTitle);
        salaryHeader.Children.Add(salaryTitle);
        earningsVisibilityIcon.Foreground = statsForeground;
        earningsEye.Content = earningsVisibilityIcon;
        earningsEye.Foreground = statsForeground;
        earningsEye.Background = eyeGlass;
        earningsEye.CornerRadius = new CornerRadius(100);
        earningsEye.Resources["ButtonBackgroundPointerOver"] = eyeHoverGlass;
        earningsEye.Resources["ButtonBackgroundPressed"] = eyePressedGlass;
        earningsEye.Resources["ButtonForegroundPointerOver"] = statsForeground;
        earningsEye.Resources["ButtonForegroundPressed"] = statsForeground;
        earningsEye.Click += async (_, __) =>
        {
            PopEyeButton();
            earningsEye.IsEnabled = false;
            try { if (settings.HideAmount) await RevealSalary(surface.XamlRoot); else HideSalary(); }
            catch (Exception ex) { App.RecordFailure(ex); HideSalary(); }
            finally { earningsEye.IsEnabled = true; }
        };
        salaryHeader.Children.Add(earningsEye);
        salaryArea.Children.Add(salaryHeader); salaryArea.Children.Add(new Border { Child = earned });
        earned.FontSize = 20; earned.FontFamily = new FontFamily("Segoe UI"); earned.FontWeight = Microsoft.UI.Text.FontWeights.Medium; earned.Foreground = statsForeground;
        for (int i = 0; i < 8; i++)
        {
            var wishHeading = new TextBlock
            {
                FontFamily = new FontFamily("Microsoft YaHei"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = statsForeground, IsTextScaleFactorEnabled = false,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center
            };
            var wishValue = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI"), FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = statsForeground, IsTextScaleFactorEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
            };
            var wishRow = new Grid { IsHitTestVisible = false };
            wishRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wishRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wishRow.Children.Add(wishHeading);
            Grid.SetColumn(wishValue, 1); wishRow.Children.Add(wishValue);
            var wishBar = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var wishTrack = new Border { Background = statsForeground, Opacity = .16, CornerRadius = new CornerRadius(2.5) };
            var wishFillGrid = new Grid();
            var fillColumn = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            var restColumn = new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) };
            wishFillGrid.ColumnDefinitions.Add(fillColumn); wishFillGrid.ColumnDefinitions.Add(restColumn);
            var wishFill = new Border { Background = statsForeground, Opacity = .9, CornerRadius = new CornerRadius(2.5) };
            wishFillGrid.Children.Add(wishFill);
            wishBar.Children.Add(wishTrack); wishBar.Children.Add(wishFillGrid);

            var wishPage = new StackPanel
            {
                Spacing = 4, Margin = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0, IsHitTestVisible = false,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new CompositeTransform()
            };
            wishPage.Children.Add(wishRow);
            wishPage.Children.Add(wishBar);
            statTitles.Add(wishHeading);
            wishPagePanels.Add(wishPage); wishPageNames.Add(wishHeading); wishPagePercents.Add(wishValue);
            wishBarHosts.Add(wishBar); wishBarTracks.Add(wishTrack); wishBarFills.Add(wishFill);
            wishBarFillColumns.Add(fillColumn); wishBarRestColumns.Add(restColumn);
            statsPages.Children.Add(wishPage);
        }
        statsCorePages.Add(restPage); statsCorePages.Add(payPage); statsCorePages.Add(salaryArea);
        var row = statsRow;
        for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        restPage.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(payPage, 1); row.Children.Add(restPage); row.Children.Add(payPage);
        Grid.SetColumn(salaryArea, 2); row.Children.Add(salaryArea);
        foreach (FrameworkElement column in row.Children) column.HorizontalAlignment = HorizontalAlignment.Center;

        statsLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statsLayout.Children.Add(statsPages);
        Grid.SetColumn(statsDots, 1); statsLayout.Children.Add(statsDots);
        statsDots.Visibility = Visibility.Collapsed;

        statsPages.Children.Insert(0, statsRow);
        statsPageItems.Add(statsRow); statsAvailablePages = 1; statsPageIndex = 0;
        statsRow.Opacity = 1; statsRow.IsHitTestVisible = true;
        stats.Background = statsGlass; stats.Child = statsLayout;
        statsPages.Clip = statsPageClip;
        statsPages.SizeChanged += (_, __) => UpdateStatsPageClip();
        stats.SizeChanged += (_, __) => { SynchronizeStatsGeometry(); UpdateStatsPageClip(); };
        stats.PointerWheelChanged += OnStatsPointerWheelChanged;
        text.Children.Add(stats);
        body.Children.Add(text);
        var petHost = new HandCursorGrid
        {
            Background = new SolidColorBrush(Colors.Transparent), VerticalAlignment = VerticalAlignment.Bottom
        };
        petHost.Children.Add(pet);
        Grid.SetColumn(petHost, 1); body.Children.Add(petHost);
        card.Child = body;
        petHost.PointerPressed += (_, __) => suppressPetTap = false;
        petHost.Tapped += (_, e) => { if (!suppressPetTap) EmitCoin(); suppressPetTap = false; e.Handled = true; };
        petHost.DoubleTapped += (_, e) => { e.Handled = true; };
        pet.RenderTransformOrigin = new Point(.5, 1);
        pet.RenderTransform = petClickLift;
        pet.PointerEntered += (_, __) => UpdatePetAnimationHover();
        pet.PointerMoved += (_, __) => UpdatePetAnimationHover();
    }

    void BuildCoinBank()
    {
        var jarImage = new Image
        {
            Width = 32, Height = 36, Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri("ms-appx:///assets/coin-jar.png")),
            IsHitTestVisible = false
        };
        var jarLift = new TranslateTransform();
        var floatStory = new Storyboard();
        var lift = new DoubleAnimation
        {
            From = 0, To = -3.2, Duration = new Duration(TimeSpan.FromSeconds(2.8)),
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(lift, jarLift); Storyboard.SetTargetProperty(lift, "Y");
        floatStory.Children.Add(lift);

        coinJarFillLayer = new Canvas
        {
            Width = 32, Height = 36, IsHitTestVisible = false,
            Clip = new RectangleGeometry { Rect = new Rect(9.5, 12.5, 14, 19) }
        };
        var jarFloat = new Grid { Width = 32, Height = 36 };
        jarFloat.RenderTransform = jarLift;
        jarFloat.Children.Add(jarImage);
        jarFloat.Children.Add(coinJarFillLayer);

        var jarHost = new Grid { Width = 32, Height = 36 };
        jarHost.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 14, Height = 3, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1),
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 20, 12, 6))
        });
        jarHost.Children.Add(jarFloat);

        coinBankButton.Content = new Viewbox { Width = 19, Height = 21, Stretch = Stretch.Uniform, Child = jarHost };
        coinBankButton.Padding = new Thickness(0); coinBankButton.MinWidth = coinBankButton.MinHeight = 0;
        coinBankButton.BorderThickness = new Thickness(0); coinBankButton.Background = new SolidColorBrush(Colors.Transparent);
        coinBankButton.IsTabStop = false;
        coinBankButton.UseSystemFocusVisuals = false;
        coinBankButton.FocusVisualPrimaryThickness = new Thickness(0);
        coinBankButton.FocusVisualSecondaryThickness = new Thickness(0);
        coinBankButton.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(Colors.Transparent);
        coinBankButton.Resources["ButtonBackgroundPressed"] = new SolidColorBrush(Colors.Transparent);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(coinBankButton, "查看可用金币");
        coinBankButton.DoubleTapped += (_, e) => e.Handled = true;
        coinBankButton.RenderTransform = coinJarPosition;
        coinBankButton.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(CoinJarPointerPressed), true);
        coinBankButton.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(CoinJarPointerMoved), true);
        coinBankButton.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(CoinJarPointerReleased), true);
        coinBankButton.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(CoinJarPointerCaptureLost), true);
        coinBankButton.Click += (_, __) => { if (!coinJarPointerDown && !suppressCoinJarClick) ShowCoinBankBalance(); };
        surface.SizeChanged += (_, __) => ApplyCoinJarPosition();
        coinBankButton.Loaded += (_, __) =>
        {
            if (uiSettings.AnimationsEnabled) floatStory.Begin();
            else jarLift.Y = -1;
        };
        coinBankBalance.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(225, 35, 35, 40));
        coinBankBalance.CornerRadius = new CornerRadius(10);
        coinBankBalanceText.Foreground = new SolidColorBrush(Colors.White);
        coinBankBalance.Child = coinBankBalanceText;
        privacy.BalanceChanged += UpdateCoinJarFill;
        UpdateCoinJarFill();
        surface.Children.Add(coinBankBalance); surface.Children.Add(coinBankButton);
    }

    void ApplyCoinJarPosition()
    {
        if (surface.ActualWidth <= 0 || surface.ActualHeight <= 0)
        { coinJarPosition.X = coinJarPosition.Y = 0; PositionCoinBankBalance(); return; }
        double scale = PhysicalScale, inset = 12 * scale;
        if (settings.CoinJarX < 0 || settings.CoinJarY < 0)
        {
            coinJarPosition.X = 0;
            coinJarPosition.Y = Math.Min(34 * scale, Math.Max(0, surface.ActualHeight - coinBankButton.Height))
                - (surface.ActualHeight - coinBankButton.Height - 10 * scale);
            PositionCoinBankBalance(); return;
        }
        double maxX = Math.Max(inset, surface.ActualWidth - coinBankButton.Width - inset);
        double maxY = Math.Max(inset, surface.ActualHeight - coinBankButton.Height - inset);
        double x = inset + Math.Clamp(settings.CoinJarX, 0, 1) * (maxX - inset);
        double y = inset + Math.Clamp(settings.CoinJarY, 0, 1) * (maxY - inset);
        coinJarPosition.X = x - (surface.ActualWidth - coinBankButton.Width - 4 * scale);
        coinJarPosition.Y = y - (surface.ActualHeight - coinBankButton.Height - 10 * scale);
        PositionCoinBankBalance();
    }

    void CoinJarPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(coinBankButton).Properties.IsLeftButtonPressed) return;
        coinJarPointerDown = true; coinJarMoved = false; suppressCoinJarClick = true;
        coinJarPressPoint = e.GetCurrentPoint(surface).Position;
        coinJarPressPosition = new Point(coinJarPosition.X, coinJarPosition.Y);
        coinBankButton.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    void CoinJarPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!coinJarPointerDown) return;
        Point point = e.GetCurrentPoint(surface).Position;
        double dx = point.X - coinJarPressPoint.X, dy = point.Y - coinJarPressPoint.Y;
        if (!coinJarMoved && dx * dx + dy * dy < 16 * PhysicalScale * PhysicalScale) { e.Handled = true; return; }
        coinJarMoved = true;
        double inset = 12 * PhysicalScale;
        double maxX = Math.Max(inset, surface.ActualWidth - coinBankButton.Width - inset);
        double maxY = Math.Max(inset, surface.ActualHeight - coinBankButton.Height - inset);
        double baseX = surface.ActualWidth - coinBankButton.Width - 4 * PhysicalScale;
        double baseY = surface.ActualHeight - coinBankButton.Height - 10 * PhysicalScale;
        double x = Math.Clamp(baseX + coinJarPressPosition.X + dx, inset, maxX);
        double y = Math.Clamp(baseY + coinJarPressPosition.Y + dy, inset, maxY);
        settings.CoinJarX = maxX > inset ? (x - inset) / (maxX - inset) : 0;
        settings.CoinJarY = maxY > inset ? (y - inset) / (maxY - inset) : 0;
        ApplyCoinJarPosition();
        e.Handled = true;
    }

    void CoinJarPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!coinJarPointerDown) return;
        bool moved = coinJarMoved;
        coinJarPointerDown = false;
        coinBankButton.ReleasePointerCapture(e.Pointer);
        if (moved) Save(); else ShowCoinBankBalance();
        DispatcherQueue.TryEnqueue(() => suppressCoinJarClick = false);
        e.Handled = true;
    }

    void CoinJarPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!coinJarPointerDown) return;
        coinJarPointerDown = false;
        if (coinJarMoved) Save();
        DispatcherQueue.TryEnqueue(() => suppressCoinJarClick = false);
    }

    void UpdateCoinJarFill()
    {
        var layer = coinJarFillLayer;
        if (layer == null) return;
        int balance;
        try { balance = privacy.CoinBalance; }
        catch { return; }
        layer.Children.Clear();
        int level = balance <= 100 ? 1 : balance <= 500 ? 2 : balance < 1000 ? 3 : 4;
        if (balance <= 0) return;
        var fill = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
        fill.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 230, 160), Offset = 0 });
        fill.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 227, 166, 60), Offset = .6 });
        fill.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 193, 122, 34), Offset = 1 });
        var face = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 255, 217, 128));
        var edge = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 158, 102, 24));
        var glint = new SolidColorBrush(Windows.UI.Color.FromArgb(210, 255, 255, 244));
        layer.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 15, Height = 3.4, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(38, 0, 0, 0))
        });
        Canvas.SetLeft(layer.Children[0], 8.8); Canvas.SetTop(layer.Children[0], 28.2);
        var rows = new (double Top, double[] Lefts, double Width, double Height)[]
        {
            (27.2, new[] { 9.4, 15.4 }, 6.8, 4.2),
            (23.2, new[] { 10.6, 16.6 }, 6.8, 4.2),
            (19.2, new[] { 9.2, 15.2 }, 6.8, 4.2),
            (15.2, new[] { 11.2, 16.8 }, 6.8, 4.2)
        };
        for (int row = 0; row < level && row < rows.Length; row++)
        {
            var (top, lefts, width, height) = rows[row];
            foreach (double left in lefts)
            {
                var coin = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = width, Height = height, Fill = fill, Stroke = edge, StrokeThickness = .5
                };
                Canvas.SetLeft(coin, left); Canvas.SetTop(coin, top); layer.Children.Add(coin);
                var core = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = width * .56, Height = height * .5, Fill = face };
                Canvas.SetLeft(core, left + width * .22); Canvas.SetTop(core, top + height * .24); layer.Children.Add(core);
                var shine = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 1.1, Height = .6, Fill = glint };
                Canvas.SetLeft(shine, left + width * .22); Canvas.SetTop(shine, top + .5); layer.Children.Add(shine);
            }
        }
        var sheen = new Border
        {
            Width = 2.2, Height = 13, CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(64, 255, 255, 255))
        };
        Canvas.SetLeft(sheen, 8.3); Canvas.SetTop(sheen, 15.5); layer.Children.Add(sheen);
    }


    void ShowCoinBankBalance()
    {
        if (coinBankBalance.Visibility == Visibility.Visible)
        {
            coinBankTimer?.Stop();
            coinBankBalance.Visibility = Visibility.Collapsed;
            return;
        }
        try { coinBankBalanceText.Text = "可用金币  " + privacy.CoinBalance.ToString("N0", CultureInfo.InvariantCulture); }
        catch (IOException ex) { App.RecordEvent("coin balance read failed: " + ex.Message); return; }
        UpdateCoinJarFill();
        coinBankBalance.Visibility = Visibility.Visible;
        PositionCoinBankBalance();
        if (coinBankTimer == null)
        {
            coinBankTimer = DispatcherQueue.CreateTimer();
            coinBankTimer.Interval = TimeSpan.FromSeconds(4);
            coinBankTimer.IsRepeating = false;
            coinBankTimer.Tick += (_, __) => coinBankBalance.Visibility = Visibility.Collapsed;
        }
        coinBankTimer.Stop(); coinBankTimer.Start();
    }

    void PositionCoinBankBalance()
    {
        if (coinBankBalance.Visibility != Visibility.Visible || surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return;
        coinBankBalance.Margin = new Thickness(0);
        coinBankBalance.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = coinBankBalance.DesiredSize.Width, height = coinBankBalance.DesiredSize.Height;
        double scale = PhysicalScale, gap = 3 * scale, edge = 4 * scale;
        double jarX = surface.ActualWidth - coinBankButton.Width - 4 * scale + coinJarPosition.X;
        double jarY = surface.ActualHeight - coinBankButton.Height - 10 * scale + coinJarPosition.Y;
        double x = Math.Clamp(jarX + (coinBankButton.Width - width) / 2, edge, Math.Max(edge, surface.ActualWidth - width - edge));
        double y = jarY - height - gap;
        if (y < edge) y = jarY + coinBankButton.Height + gap;
        y = Math.Clamp(y, edge, Math.Max(edge, surface.ActualHeight - height - edge));
        coinBankBalance.Margin = new Thickness(x, y, 0, 0);
    }

    StackPanel Stat(string title, TextBlock value)
    {
        var panel = new StackPanel { Spacing = 5 };
        var heading = new TextBlock { Text = title, FontFamily = new FontFamily("Microsoft YaHei"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = statsForeground };
        statTitles.Add(heading); panel.Children.Add(heading);
        value.FontSize = 20; value.FontFamily = new FontFamily("Segoe UI"); value.FontWeight = Microsoft.UI.Text.FontWeights.Medium; value.Foreground = statsForeground;
        panel.Children.Add(new Border { Child = value });
        return panel;
    }

    void UpdateStatsPager()
    {
        if (statsCorePages.Count == 0) return;
        var allWishes = ValidWishes();
        decimal wishPool = Logic.WishPool(DateTime.Now, settings);
        var wishProgress = Logic.AllocateWishProgress(allWishes, settings.WishAllocation, wishPool);
        UpdateWishCompletion(allWishes, wishProgress);
        // Fully funded wishes leave the card; their target stays consumed so the
        // remaining pool is not reused. Settings keeps them as 已达成.
        var wishes = new System.Collections.Generic.List<WishEntry>();
        decimal visiblePool = wishPool;
        for (int i = 0; i < allWishes.Count; i++)
        {
            var wish = allWishes[i];
            if (wish.Target > 0 && wishProgress[i] >= wish.Target - .005m)
            {
                visiblePool -= Math.Min(wish.Target, wishProgress[i]);
                continue;
            }
            wishes.Add(wish);
        }
        if (visiblePool < 0) visiblePool = 0;
        statsWishCount = Math.Min(wishes.Count, wishPagePanels.Count);
        bool wanted = settings.WishEnabled && statsWishCount > 0;
        bool busy = statsPageAnimating || statsDragging || statsDragSettling;
        if (wanted && !busy) UpdateWishPageValues(wishes, visiblePool);
        if (wanted != statsWishMode)
        {
            if (wanted) EnterWishPager();
            else ExitWishPager();
        }
        if (!wanted || busy) return;

        string signature = "";
        for (int i = 0; i < statsWishCount; i++) signature += wishes[i].Name + "|" + wishes[i].Target.ToString(CultureInfo.InvariantCulture) + ";";
        if (signature != statsWishSignature || statsPageItems.Count == 0)
        {
            statsWishSignature = signature;
            RewireStatsPages(reset: statsPageItems.Count == 0);
        }
    }

    void EnterWishPager()
    {
        statsWishMode = true;
        statsPages.Interactive = true;
        statsLayout.Interactive = true;
        StopStatsPageAnimation();
        statsRow.HorizontalAlignment = HorizontalAlignment.Stretch;
        statsRow.VerticalAlignment = VerticalAlignment.Stretch;
        salaryArea.Visibility = settings.ShowSalaryLayout ? Visibility.Visible : Visibility.Collapsed;
        statsRow.ColumnDefinitions[2].Width = settings.ShowSalaryLayout ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        statsPageIndex = 0;
        statsDots.Visibility = Visibility.Visible;
        RewireStatsPages(reset: true);
        SynchronizeStatsGeometry();
    }

    void ExitWishPager()
    {
        statsWishMode = false;
        statsPages.Interactive = false;
        statsLayout.Interactive = false;
        StopStatsPageAnimation();
        statsWishSignature = "";
        statsWishCount = 0;
        statsRow.Opacity = 1; statsRow.IsHitTestVisible = true;
        if (statsRow.RenderTransform is CompositeTransform transform)
        {
            transform.TranslateY = 0;
            transform.ScaleX = transform.ScaleY = 1;
        }
        foreach (var page in wishPagePanels) { page.Opacity = 0; page.IsHitTestVisible = false; }
        salaryArea.Visibility = settings.ShowSalaryLayout ? Visibility.Visible : Visibility.Collapsed;
        salaryArea.Opacity = 1;
        statsRow.ColumnDefinitions[2].Width = settings.ShowSalaryLayout ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        statsPageItems.Clear(); statsPageItems.Add(statsRow);
        statsAvailablePages = 1; statsPageIndex = 0;
        statsDots.Children.Clear(); statsDotShapes.Clear(); statsDots.Visibility = Visibility.Collapsed;
        SynchronizeStatsGeometry();
    }

    void UpdateWishPageValues(System.Collections.Generic.List<WishEntry> wishes, decimal pool)
    {
        var progressByWish = Logic.AllocateWishProgress(wishes, settings.WishAllocation, pool);
        for (int i = 0; i < statsWishCount && i < wishes.Count; i++)
        {
            var wish = wishes[i];
            string fullName = string.IsNullOrWhiteSpace(wish.Name) ? "小心愿" : wish.Name.Trim();
            string displayName = fullName.Length > 5 ? fullName.Substring(0, 5) + "…" : fullName;
            if (wishPageNames[i].Text != displayName) wishPageNames[i].Text = displayName;
            string? tip = fullName.Length > 5 ? fullName : null;
            if (!Equals(ToolTipService.GetToolTip(wishPageNames[i]), tip)) ToolTipService.SetToolTip(wishPageNames[i], tip);
            decimal target = Math.Max(0, wish.Target);
            decimal progress = i < progressByWish.Length ? progressByWish[i] : 0;
            int percent = target > 0 ? (int)Math.Floor(progress / target * 100) : 0;
            string text = percent.ToString(CultureInfo.InvariantCulture) + "%";
            if (wishPagePercents[i].Text != text) wishPagePercents[i].Text = text;
            var fillWidth = new GridLength(percent, GridUnitType.Star);
            if (!wishBarFillColumns[i].Width.Equals(fillWidth)) wishBarFillColumns[i].Width = fillWidth;
            var restWidth = new GridLength(100 - percent, GridUnitType.Star);
            if (!wishBarRestColumns[i].Width.Equals(restWidth)) wishBarRestColumns[i].Width = restWidth;
        }
    }

    void RewireStatsPages(bool reset)
    {
        statsPageItems.Clear();
        statsPageItems.Add(statsRow);
        for (int i = 0; i < statsWishCount; i++) statsPageItems.Add(wishPagePanels[i]);
        statsAvailablePages = statsPageItems.Count;
        foreach (var page in wishPagePanels)
            if (!statsPageItems.Contains(page)) { page.Opacity = 0; page.IsHitTestVisible = false; }
        RebuildStatsDots(statsAvailablePages);
        if (reset || statsPageIndex >= statsAvailablePages) statsPageIndex = 0;
        ShowStatsPage(statsPageIndex, animate: false);
    }

    void RebuildStatsDots(int count)
    {
        statsDots.Children.Clear(); statsDotShapes.Clear();
        statsDotActiveIndex = -1;
        if (count <= 1) { statsDots.Visibility = Visibility.Collapsed; return; }
        statsDots.Visibility = Visibility.Visible;
        for (int i = 0; i < count; i++)
        {
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Fill = statsForeground, Opacity = .3, IsHitTestVisible = false,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new CompositeTransform()
            };
            statsDotShapes.Add(dot); statsDots.Children.Add(dot);
        }
        ApplyDotTypography();
        UpdateStatsDotState();
    }

    void ApplyDotTypography()
    {
        int count = statsDotShapes.Count;
        if (count == 0) return;
        double scale = PhysicalScale, dpi = GetDpiForWindow(hwnd) / 96.0;
        double Size(double logical) => Math.Round(logical * scale * dpi) / dpi;
        double size = count > 8 ? 3.4 : count > 5 ? 4.4 : 5.2;
        double gap = count > 8 ? 3.0 : 4.8;
        statsDots.Width = Size(8);
        statsDots.Spacing = Size(gap);
        statsDots.Margin = new Thickness(Size(4), 0, 0, 0);
        foreach (var dot in statsDotShapes) { dot.Width = dot.Height = Size(size); dot.HorizontalAlignment = HorizontalAlignment.Center; }
    }

    void UpdateStatsDotState()
    {
        for (int i = 0; i < statsDotShapes.Count; i++)
            statsDotShapes[i].Opacity = i == statsPageIndex ? .95 : .3;
        if (statsDotActiveIndex != statsPageIndex)
        {
            statsDotActiveIndex = statsPageIndex;
            PopStatsDot(statsPageIndex);
        }
    }

    void PopStatsDot(int index)
    {
        if (index < 0 || index >= statsDotShapes.Count) return;
        if (statsDotShapes[index].RenderTransform is not CompositeTransform transform) return;
        transform.ScaleX = transform.ScaleY = 1;
        if (!uiSettings.AnimationsEnabled) return;
        var story = new Storyboard();
        AddDotPop(story, transform, "ScaleX");
        AddDotPop(story, transform, "ScaleY");
        statsDotStory?.Stop();
        statsDotStory = story;
        story.Begin();
    }

    static void AddDotPop(Storyboard story, CompositeTransform target, string property)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)), Value = 1.25, EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), Value = 1, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        story.Children.Add(animation);
    }

    static CompositeTransform PagerTransform(FrameworkElement page)
    {
        if (page.RenderTransform is CompositeTransform transform) return transform;
        transform = new CompositeTransform();
        page.RenderTransform = transform;
        page.RenderTransformOrigin = new Point(.5, .5);
        return transform;
    }

    void StopStatsPageAnimation()
    {
        statsPageStory?.Stop(); statsPageStory = null;
        statsPageAnimating = false; statsPageQueued = -1; statsPageQueuedDirection = 0; statsPagePending = -1;
    }

    double StatsPagePitch => Math.Max(20, statsPages.ActualHeight) + 8 * PhysicalScale;

    void UpdateStatsPageClip()
    {
        statsPageClip.Rect = new Rect(0, 0, Math.Max(0, statsPages.ActualWidth), Math.Max(0, statsPages.ActualHeight));
    }

    void ResetStatsPageVisual(FrameworkElement page)
    {
        var transform = PagerTransform(page);
        transform.TranslateY = 0; transform.ScaleX = transform.ScaleY = 1;
        page.Opacity = 0; page.IsHitTestVisible = false;
    }

    void NormalizeStatsPages()
    {
        foreach (var page in statsPageItems) ResetStatsPageVisual(page);
        if (statsPageIndex >= 0 && statsPageIndex < statsPageItems.Count)
        {
            var current = statsPageItems[statsPageIndex];
            current.Opacity = 1; current.IsHitTestVisible = true;
        }
    }

    void BeginStatsDrag()
    {
        StopStatsPageAnimation();
        statsDragStory?.Stop(); statsDragStory = null;
        statsDragSettling = false;
        statsDragCurrent = statsPageIndex;
        statsDragAdjacent = -1; statsDragDirection = 0; statsDragOffset = 0; statsDragMoved = false;
        UpdateStatsPageClip();
        NormalizeStatsPages();
        var current = statsPageItems[statsDragCurrent];
        current.Opacity = 1; current.IsHitTestVisible = false;
    }

    void UpdateStatsDrag(double y)
    {
        int count = statsAvailablePages;
        if (count <= 1) return;
        double pitch = StatsPagePitch;
        double dy = Math.Clamp(y - statsDragStartY, -pitch, pitch);
        statsDragOffset = dy;
        if (Math.Abs(dy) > 4) statsDragMoved = true;
        int direction = dy < 0 ? -1 : dy > 0 ? 1 : 0;
        if (direction != statsDragDirection)
        {
            if (statsDragAdjacent >= 0 && statsDragAdjacent < statsPageItems.Count)
                ResetStatsPageVisual(statsPageItems[statsDragAdjacent]);
            statsDragDirection = direction;
            statsDragAdjacent = direction == 0 ? -1 : (statsDragCurrent + (direction < 0 ? 1 : -1) + count) % count;
            if (statsDragAdjacent >= 0)
            {
                var adjacentPage = statsPageItems[statsDragAdjacent];
                adjacentPage.Opacity = 1; adjacentPage.IsHitTestVisible = false;
            }
        }
        var current = statsPageItems[statsDragCurrent];
        var currentTransform = PagerTransform(current);
        currentTransform.TranslateY = dy; currentTransform.ScaleX = currentTransform.ScaleY = 1;
        current.Opacity = 1; current.IsHitTestVisible = false;
        if (statsDragAdjacent >= 0)
        {
            var adjacentPage = statsPageItems[statsDragAdjacent];
            var adjacentTransform = PagerTransform(adjacentPage);
            adjacentTransform.TranslateY = dy - direction * pitch;
            adjacentTransform.ScaleX = adjacentTransform.ScaleY = 1;
            adjacentPage.Opacity = 1; adjacentPage.IsHitTestVisible = false;
        }
    }

    void EndStatsDragRelease(Pointer? pointer, bool allowClick, int clickDirection = 0)
    {
        if (!statsDragging) return;
        statsDragging = false;
        if (pointer != null) card.ReleasePointerCapture(pointer);
        if (statsAvailablePages <= 1) { NormalizeStatsPages(); return; }
        if (!statsDragMoved)
        {
            NormalizeStatsPages();
            if (allowClick) ShowStatsPage(statsPageIndex + (clickDirection == 0 ? 1 : clickDirection), true);
            return;
        }
        double pitch = StatsPagePitch;
        bool commit = statsDragAdjacent >= 0 && Math.Abs(statsDragOffset) >= pitch * .3;
        int target = commit ? statsDragAdjacent : statsDragCurrent;
        var current = statsPageItems[statsDragCurrent];
        var currentTransform = PagerTransform(current);
        var adjacent = statsDragAdjacent >= 0 ? statsPageItems[statsDragAdjacent] : null;
        var adjacentTransform = adjacent != null ? PagerTransform(adjacent) : null;
        double currentTarget = commit ? (statsDragDirection < 0 ? -pitch : pitch) : 0;
        double adjacentTarget = commit ? 0 : (statsDragDirection < 0 ? pitch : -pitch);
        var story = new Storyboard();
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var currentAnimation = new DoubleAnimation { From = currentTransform.TranslateY, To = currentTarget, Duration = new Duration(TimeSpan.FromMilliseconds(commit ? 340 : 240)), EasingFunction = ease };
        Storyboard.SetTarget(currentAnimation, currentTransform); Storyboard.SetTargetProperty(currentAnimation, "TranslateY"); story.Children.Add(currentAnimation);
        if (adjacentTransform != null)
        {
            var adjacentAnimation = new DoubleAnimation { From = adjacentTransform.TranslateY, To = adjacentTarget, Duration = new Duration(TimeSpan.FromMilliseconds(commit ? 340 : 240)), EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(adjacentAnimation, adjacentTransform); Storyboard.SetTargetProperty(adjacentAnimation, "TranslateY"); story.Children.Add(adjacentAnimation);
        }
        statsDragStory = story; statsDragSettling = true;
        story.Completed += (_, __) =>
        {
            statsDragStory = null; statsDragSettling = false;
            foreach (var page in statsPageItems) ResetStatsPageVisual(page);
            var visible = statsPageItems[commit ? target : statsDragCurrent];
            visible.Opacity = 1; visible.IsHitTestVisible = true;
            if (commit) { statsPageIndex = target; UpdateStatsDotState(); }
            statsDragDirection = 0; statsDragAdjacent = -1; statsDragOffset = 0; statsDragMoved = false;
        };
        story.Begin();
    }

    void ShowStatsPage(int index, bool animate, int direction = 0)
    {
        if (statsDragging || statsDragSettling) return;
        int count = statsAvailablePages;
        if (count <= 0) return;
        index = ((index % count) + count) % count;
        statsDragStory?.Stop(); statsDragStory = null;
        UpdateStatsPageClip();
        if (!animate || !uiSettings.AnimationsEnabled)
        {
            StopStatsPageAnimation();
            for (int i = 0; i < count; i++)
            {
                var page = statsPageItems[i];
                var transform = PagerTransform(page);
                page.Opacity = i == index ? 1 : 0;
                page.IsHitTestVisible = i == index;
                transform.TranslateY = 0; transform.ScaleX = transform.ScaleY = 1;
            }
            statsPageIndex = index; UpdateStatsDotState(); return;
        }
        if (statsPageAnimating) { statsPageQueued = index; statsPageQueuedDirection = direction; return; }
        if (index == statsPageIndex) { UpdateStatsDotState(); return; }
        var outgoing = statsPageItems[statsPageIndex];
        var incoming = statsPageItems[index];
        int step = direction != 0 ? Math.Sign(direction) : index > statsPageIndex ? 1 : -1;
        if (direction == 0)
        {
            if (index == 0 && statsPageIndex != 0) step = -1;
            else if (statsPageIndex == 0 && index == count - 1) step = -1;
        }
        GlassProfile.Event($"stats-page {statsPageIndex}->{index} step={step} count={count} direction={direction}");
        double pitch = StatsPagePitch;
        for (int i = 0; i < count; i++)
        {
            var page = statsPageItems[i];
            if (page == outgoing || page == incoming) continue;
            ResetStatsPageVisual(page);
        }
        var outTransform = PagerTransform(outgoing);
        var inTransform = PagerTransform(incoming);
        outTransform.TranslateY = 0; outTransform.ScaleX = outTransform.ScaleY = 1;
        inTransform.TranslateY = step > 0 ? pitch : -pitch; inTransform.ScaleX = inTransform.ScaleY = 1;
        outgoing.Opacity = 1; outgoing.IsHitTestVisible = false;
        incoming.Opacity = 1; incoming.IsHitTestVisible = false;
        var story = new Storyboard();
        var slideOut = new DoubleAnimation { From = 0, To = step > 0 ? -pitch : pitch, Duration = new Duration(TimeSpan.FromMilliseconds(380)), EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(slideOut, outTransform); Storyboard.SetTargetProperty(slideOut, "TranslateY"); story.Children.Add(slideOut);
        var slideIn = new DoubleAnimation { From = step > 0 ? pitch : -pitch, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(380)), EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(slideIn, inTransform); Storyboard.SetTargetProperty(slideIn, "TranslateY"); story.Children.Add(slideIn);
        statsPageStory = story; statsPageAnimating = true; statsPagePending = index;
        story.Completed += (_, __) =>
        {
            outgoing.Opacity = 0; outTransform.TranslateY = 0;
            incoming.Opacity = 1; inTransform.TranslateY = 0; incoming.IsHitTestVisible = true;
            statsPageIndex = index; statsPageAnimating = false; statsPageStory = null; statsPagePending = -1;
            UpdateStatsDotState();
            int queued = statsPageQueued, queuedDirection = statsPageQueuedDirection;
            statsPageQueued = -1; statsPageQueuedDirection = 0;
            if (queued >= 0 && queued != statsPageIndex) ShowStatsPage(queued, animate: true, direction: queuedDirection);
        };
        story.Begin();
    }

    void OnStatsPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (disposed || exiting || !statsWishMode) return;
        if (statsDragging || statsDragSettling) { e.Handled = true; return; }
        int delta = e.GetCurrentPoint(stats).Properties.MouseWheelDelta;
        if (delta == 0) return;
        int current = statsPageAnimating && statsPagePending >= 0 ? statsPagePending : statsPageIndex;
        int direction = delta < 0 ? 1 : -1;
        ShowStatsPage(current + direction, animate: true, direction);
        e.Handled = true;
    }

    bool IsCursorOverHandArea(Point point)
    {
        if (IsPointInside(coinBankButton, point) || IsPointInside(pet, point)) return true;
        return statsWishMode && IsPointInside(stats, point);
    }

    void KeepCoinCursor()
    {
        // The coin popup window can pin the stationary cursor to the arrow while
        // coins are in the air; reassert the hand until the popup closes.
        if (!trackingCoins || disposed || exiting || !IsWindowVisible(hwnd)) return;
        if (!IsCursorOverWidget() || !TryGetHoverPoint(out Point point) || !IsCursorOverHandArea(point)) return;
        SetCursor(handCursor);
    }

    bool IsPointInside(FrameworkElement element, Point point)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        try
        {
            Windows.Foundation.Rect bounds = element.TransformToVisual(hostSurface)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return bounds.Contains(point);
        }
        catch (ArgumentException) { return false; }
    }

    void UpdateRestTypography()
    {
        bool unset = rest.Text == "未设置";
        bool weekday = rest.Text.Length == 2 && rest.Text[0] == '周';
        double logicalSize = unset ? 18 : weekday ? 20 : 22;
        double dpi = Math.Max(96u, GetDpiForWindow(hwnd)) / 96d;
        rest.FontSize = Math.Round(pay.FontSize * logicalSize / 22 * dpi) / dpi;
        UiTypography.SetWeight(rest, unset ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.Medium, surface.RequestedTheme == ElementTheme.Dark);
    }
    static string NormalizePet(string id) =>
        id == "cat" || id == "samoyed" || id == "corgi" || id == "red-panda" ? "cow-cat" :
        id == "cow-cat" || id == "panda" || id == "polar-bear" ? id : "polar-bear";

    void LoadPet()
    {
        int revision = ++petAnimationRevision;
        CancelSlack(false); slackAnimation?.Dispose(); slackAnimation = null; slackLoading = false; slackMouseBlocked = false;
        ScheduleSlack();
        reminderAnimationRevision++;
        napActive = false; napPending = false; napRevision++;
        napEndAt = DateTime.MinValue;
        napAnimation?.Dispose(); napAnimation = null;
        startWorkRevision++; startWorkActive = false;
        startWorkAnimation?.Dispose(); startWorkAnimation = null;
        reminderPetActive = false;
        coinPetActive = false; coinPetRequestedAt = -10000;
        coinPetAnimation?.Dispose(); coinPetAnimation = null;
        reminderAnimation?.Dispose(); reminderAnimation = null;
        waterPetOverlay.Visibility = Visibility.Collapsed; waterPetOverlay.Source = null; pet.Opacity = 1;
        petAnimation?.Dispose(); petAnimation = null;
        petHoverTimer?.Stop();
        bool polar = settings.PetId == "polar-bear";
        pet.Source = new BitmapImage(new Uri("ms-appx:///assets/" + (polar ? "polar-bear-idle" : settings.PetId) + ".png"));
        _ = LoadPetAnimationAsync(revision, settings.PetId);
        if (polar) _ = LoadCoinPetAnimationAsync(revision);
    }

    async Task LoadPetAnimationAsync(int revision, string petId)
    {
        try
        {
            var animation = await PetAnimationPlayer.LoadAsync(pet, Path.Combine(AppContext.BaseDirectory, "assets", petId == "polar-bear" ? "polar-bear-idle.frames.br" : petId == "panda" ? "panda-doze.frames.br" : "tabby-office.frames.br")).ConfigureAwait(false);
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (exiting || disposed || revision != petAnimationRevision || settings.PetId != petId)
                { animation.Dispose(); return; }
                petAnimation = animation;
                animation.RestorePosterAfterHover = petId == "cow-cat";
                if (!reminderPetActive && !napActive) { animation.ShowPoster(); UpdatePetHoverMonitor(); }
                else if (!napActive && (petId == "panda" || petId == "cow-cat") && DateTime.Now < reminderUntil) animation.PlayOnce();
                if (petId == "polar-bear" && elapsed.ElapsedMilliseconds - coinPetRequestedAt < PetAnimationPlayer.TriggerQuietMilliseconds) PlayPolarCoinAction();
            })) animation.Dispose();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is OverflowException || ex is COMException)
        { Debug.WriteLine("宠物动画无法载入：" + ex.Message); GlassProfile.Event("pet-animation-error " + ex.GetType().Name); }
    }

    async Task LoadCoinPetAnimationAsync(int revision)
    {
        try
        {
            var animation = await PetAnimationPlayer.LoadAsync(pet, Path.Combine(AppContext.BaseDirectory, "assets", "polar-bear-coins.frames.br")).ConfigureAwait(false);
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (exiting || disposed || revision != petAnimationRevision || settings.PetId != "polar-bear")
                { animation.Dispose(); return; }
                coinPetAnimation = animation;
                GlassProfile.Event("pet-coin-animation-ready");
                if (elapsed.ElapsedMilliseconds - coinPetRequestedAt < PetAnimationPlayer.TriggerQuietMilliseconds) PlayPolarCoinAction();
            })) animation.Dispose();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is OverflowException || ex is COMException)
        { Debug.WriteLine("北极熊金币动画无法载入：" + ex.Message); GlassProfile.Event("pet-coin-animation-error " + ex.GetType().Name); }
    }

    void PlayPolarCoinAction()
    {
        CancelSlack(true);
        var source = napActive ? napAnimation : petAnimation;
        if (settings.PetId != "polar-bear" || reminderPetActive || startWorkActive || source == null || coinPetAnimation == null ||
            !uiSettings.AnimationsEnabled || disposed || exiting || !IsWindowVisible(hwnd)) return;
        if (coinPetActive) { coinPetAnimation.PulseTriggeredLoop(); return; }
        var from = source.CaptureFrame();
        var idle = napActive && napAnimation != null ? napAnimation.LastFrame() : petAnimation!.PosterFrame();
        source.Stop();
        coinPetActive = true;
        var action = coinPetAnimation;
        int revision = petAnimationRevision;
        action.StartTriggeredLoop(from, idle, () =>
        {
            if (disposed || exiting || revision != petAnimationRevision || coinPetAnimation != action) return;
            coinPetActive = false;
            if (napActive && napAnimation != null) napAnimation.ShowLastFrame();
            else { petAnimation?.ResetIdle(); UpdatePetHoverMonitor(); }
        });
    }

    void CancelPolarCoinAction(bool showIdle)
    {
        coinPetRequestedAt = -10000;
        coinPetAnimation?.Stop();
        if (!coinPetActive) return;
        coinPetActive = false;
        if (showIdle) petAnimation?.ResetIdle();
    }

    void UpdatePetHoverMonitor()
    {
        bool active = petAnimation != null && !reminderPetActive && !napActive && !napPending && !startWorkActive && !exiting && !disposed && !startupMaskActive && startupRevealStarted && IsWindowVisible(hwnd);
        if (!active) { petHoverTimer?.Stop(); CancelSlack(false); CancelPolarCoinAction(true); petAnimation?.Stop(); return; }
        if (petHoverTimer == null)
        {
            petHoverTimer = DispatcherQueue.CreateTimer();
            petHoverTimer.Interval = TimeSpan.FromMilliseconds(50);
            petHoverTimer.Tick += (_, __) => UpdatePetAnimationHover();
        }
        petHoverTimer.Start();
    }

    void UpdatePetAnimationHover()
    {
        if (petAnimation == null || reminderPetActive || napActive || napPending || startWorkActive || exiting || disposed) return;
        if (!IsWindowVisible(hwnd) || startupMaskActive) { UpdatePetHoverMonitor(); return; }
        if (coinPetActive) return;
        if (UpdateSlack()) return;
        bool inside = false, overPetPixels = false;
        if (uiSettings.AnimationsEnabled && !isDragging && TryGetHoverPoint(out Point point))
        {
            var inverse = pet.TransformToVisual(hostSurface).Inverse;
            if (inverse != null)
            {
                Point local = inverse.TransformPoint(point);
                overPetPixels = petAnimation.HitTest(local.X, local.Y);
                inside = overPetPixels && IsCursorOverWidget();
            }
        }
        petAnimation.SetHover(inside, overPetPixels);
    }

    void ScheduleSlack() => nextSlackAt = elapsed.ElapsedMilliseconds + Random.Shared.Next(60 * 60_000, 120 * 60_000 + 1);

    void CancelSlack(bool restoreIdle)
    {
        if (!slackActive) return;
        slackActive = false;
        slackAnimation?.Stop();
        ScheduleSlack();
        if (restoreIdle) petAnimation?.ResetIdle();
    }

    bool CanStartSlack()
    {
        if (settings.PetId != "polar-bear" || petAnimation == null || petAnimation.IsPlaying ||
            reminderPetActive || reminderDue != 0 || pendingReminders != 0 || napActive || napPending || startWorkActive ||
            coinPetActive || isDragging || !uiSettings.AnimationsEnabled || IsCursorOverWidget()) return false;
        try
        {
            DateTime now = DateTime.Now;
            var hours = Logic.WorkHours(now, settings);
            return Logic.WorkingDay(now, settings) && now.TimeOfDay >= hours.Start &&
                now.AddSeconds(12).Date == now.Date && now.AddSeconds(12).TimeOfDay < hours.End &&
                !Logic.IsWorkdayLunch(now, settings) && !Logic.IsWorkdayLunch(now.AddSeconds(12), settings);
        }
        catch (FormatException) { return false; }
    }

    bool UpdateSlack()
    {
        if (settings.PetId != "polar-bear" || (!slackActive && !slackMouseBlocked && elapsed.ElapsedMilliseconds < nextSlackAt)) return false;
        bool over = IsCursorOverWidget();
        if (slackMouseBlocked)
        {
            if (over) return true;
            slackMouseBlocked = false;
        }
        if (slackActive)
        {
            if (over)
            {
                CancelSlack(true);
                slackMouseBlocked = true;
            }
            else if (slackAnimation == null || !slackAnimation.IsPlaying) CancelSlack(true);
            return true;
        }
        if (elapsed.ElapsedMilliseconds < nextSlackAt || !CanStartSlack()) return false;
        if (slackAnimation == null)
        {
            if (!slackLoading) { slackLoading = true; _ = LoadSlackAsync(petAnimationRevision); }
            return false;
        }
        petAnimation!.Stop();
        slackActive = true;
        slackAnimation.PlayOnce();
        GlassProfile.Event("pet-slack-start");
        return true;
    }

    async Task LoadSlackAsync(int revision)
    {
        try
        {
            var animation = await PetAnimationPlayer.LoadAsync(pet, Path.Combine(AppContext.BaseDirectory, "assets", "polar-bear-slack.frames.br")).ConfigureAwait(false);
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (exiting || disposed || revision != petAnimationRevision || settings.PetId != "polar-bear")
                { animation.Dispose(); return; }
                slackLoading = false;
                slackAnimation = animation;
            })) animation.Dispose();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is OverflowException || ex is COMException)
        {
            Debug.WriteLine("北极熊摸鱼动画无法载入：" + ex.Message);
            DispatcherQueue.TryEnqueue(() => { if (revision == petAnimationRevision) { slackLoading = false; ScheduleSlack(); } });
        }
    }

    readonly Stopwatch layoutTransitionClock = new Stopwatch();
    bool layoutTransitionActive, layoutHostActive;
    long layoutLastFrameAt;
    int layoutFrameCount;
    int layoutClipLeft = int.MinValue, layoutClipWidth = -1;
    readonly TranslateTransform layoutTranslation = new TranslateTransform();
    int layoutCurrentWidth, layoutCurrentX, layoutHostX, layoutHostWidth;
    int layoutInsetX, layoutInsetY, layoutInsetWidth, layoutInsetHeight;
    int layoutRenderHostX, layoutRenderHostY;
    double layoutProgress, layoutStartProgress;
    int layoutStartWidth, layoutTargetWidth, layoutTargetHeight, layoutStartX, layoutTargetX;
    double shownWidgetWidth = 368;
    double WidgetLogicalWidth => !settings.ShowSalaryLayout ? 336 : shownWidgetWidth;
    double WidgetLogicalHeight => 190;

    void ApplySettings()
    {
        settings.Calendar = calendar;
        settings.PetId = NormalizePet(settings.PetId);
        coinBankButton.Visibility = settings.ShowCoinJar ? Visibility.Visible : Visibility.Collapsed;
        if (!settings.ShowCoinJar) { coinBankBalance.Visibility = Visibility.Collapsed; coinBankTimer?.Stop(); }
        if (!settings.MouseClickThrough) hoverDragArmed = false;
        ApplyWidgetSize();
        ApplyMouseClickThrough();
        UpdateHoverDragMonitor();
        CloseReminderPopup();
        pendingReminders = reminderDue = 0;
        reminderUntil = DateTime.MinValue;
        walkTimer?.Stop();
        LoadPet(); reminders.Reset(); Refresh();
    }

    void PreviewScale(double value)
    {
        if (disposed || exiting || !double.IsFinite(value)) return;
        long applyStarted = GlassProfile.Timestamp();
        if (layoutTransitionActive) CompleteLayoutTransition();
        if (!scalePreviewHostActive) BeginScalePreviewHost();
        else if (scalePreviewSettled && desktopSource.TryGetRenderBounds(out int x, out int y, out int width, out int height))
        {
            scalePreviewSettled = false;
            desktopSource.BeginVisualDrag(x, y, width, height, renderImmediately: false);
        }
        previewScale = Math.Clamp(value, .5, 1.5);
        long stageStarted = GlassProfile.Timestamp();
        ApplyWidgetSize();
        double sizeMs = GlassProfile.Ms(stageStarted);
        stageStarted = GlassProfile.Timestamp();
        Refresh();
        if (GlassProfile.Enabled)
            GlassProfile.Event($"scale-apply total={GlassProfile.Ms(applyStarted):F2} size={sizeMs:F2} refresh={GlassProfile.Ms(stageStarted):F2} value={previewScale:F3}");
        // The settings page can remain open for hours after using the ruler.
        // Stop high-frequency capture after input settles, while retaining the host
        // until preview closes so the next jump cannot expose new native backing.
        if (scalePreviewSettleTimer == null)
        {
            scalePreviewSettleTimer = DispatcherQueue.CreateTimer();
            scalePreviewSettleTimer.Interval = TimeSpan.FromMilliseconds(250);
            scalePreviewSettleTimer.Tick += (_, __) =>
            {
                scalePreviewSettleTimer.Stop();
                if (disposed || exiting || !scalePreviewHostActive) return;
                desktopSource.EndVisualDrag(keepBounds: true);
                scalePreviewSettled = true;
                GlassProfile.Event("scale-preview-settled");
            };
        }
        scalePreviewSettleTimer.Stop(); scalePreviewSettleTimer.Start();
    }

    void ApplyWidgetSize()
    {
        if (layoutHostActive) FinishLayoutHost();
        if (!previewScale.HasValue) EndScalePreviewHost();
        layoutTransitionActive = false; layoutTransitionClock.Stop(); CompositionTarget.Rendering -= AnimateLayoutTransition;
        if (!statsWishMode) salaryArea.Opacity = 1;
        shownWidgetWidth = 368; // Re-measure at the current scale instead of retaining a wider preview.
        double scale = PhysicalScale;
        surface.Width = WidgetLogicalWidth; surface.Height = WidgetLogicalHeight;
        card.Width = WidgetLogicalWidth * scale; card.Height = WidgetLogicalHeight * scale;
        card.RenderTransformOrigin = new Point(0, 0);
        card.RenderTransform = null;
        ApplyTypography();
        card.HorizontalAlignment = HorizontalAlignment.Left; card.VerticalAlignment = VerticalAlignment.Top;
        contentScale.ScaleX = contentScale.ScaleY = scale;
        ambientEffectScale.ScaleX = ambientEffectScale.ScaleY = scale;
        glass.Brush.BlurAmount = 0;
        surface.HorizontalAlignment = HorizontalAlignment.Left; surface.VerticalAlignment = VerticalAlignment.Top;
        pixelScale = GetDpiForWindow(hwnd) / 96.0 * scale;
        int winW = (int)Math.Ceiling(WidgetLogicalWidth * pixelScale);
        int winH = (int)Math.Ceiling(WidgetLogicalHeight * pixelScale);
        // Resolve the target XAML geometry before resizing the native surface.
        // A native resize can present before the next XAML layout/render pass.
        double dpi = GetDpiForWindow(hwnd) / 96.0;
        if (scalePreviewHostActive)
            UpdateScalePreviewViewport(winW, winH);
        else
        {
            int insetW = 0, insetH = 0;
            if (desktopSource.TryGetHostRenderBounds(out _, out _, out int previousW, out int previousH))
            {
                insetW = Math.Max(0, native.Size.Width - previousW);
                insetH = Math.Max(0, native.Size.Height - previousH);
            }
            SetRenderGeometry(Math.Max(1, winW - insetW), Math.Max(1, winH - insetH), dpi, scale);
            native.Resize(new SizeInt32(winW, winH));
            UpdateRenderViewport();
        }
        if (native.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = settings.Topmost;
        int posX = (int)settings.X;
        int posY = (int)settings.Y;
        bool hasPosition = posX != -1 || posY != -1;
        var area = hasPosition
            ? DisplayArea.GetFromPoint(new PointInt32(posX + winW / 2, posY + winH / 2), DisplayAreaFallback.Nearest).WorkArea
            : DisplayArea.GetFromWindowId(native.Id, DisplayAreaFallback.Primary).WorkArea;
        if (posX == -1 && posY == -1)
        {
            posX = area.X + Math.Max(0, (area.Width - winW) / 2);
            posY = area.Y + Math.Max(0, (area.Height - winH) / 2);
        }
        else
        {
            // Clamp to screen work area to prevent disappearing off-screen
            posX = Math.Clamp(posX, area.X, Math.Max(area.X, area.X + area.Width - winW));
            posY = Math.Clamp(posY, area.Y, Math.Max(area.Y, area.Y + area.Height - winH));
        }
        native.Move(new PointInt32(posX, posY));
    }

    void BeginStartupReveal()
    {
        if (startupRevealStarted || disposed) return;
        startupRevealStarted = true;
        var wait = Stopwatch.StartNew();
        startupRevealTimer = DispatcherQueue.CreateTimer();
        startupRevealTimer.Interval = TimeSpan.FromMilliseconds(16);
        startupRevealTimer.Tick += (_, __) =>
        {
            if (disposed) { startupRevealTimer.Stop(); return; }
            // Give the first captured background a chance to arrive, without
            // leaving the widget invisible when capture isn't available.
            if (!desktopSource.IsReady && wait.ElapsedMilliseconds < 300) return;
            startupRevealTimer.Stop();
            if (settings.X == -1 && settings.Y == -1)
            {
                var area = DisplayArea.Primary.WorkArea;
                native.Move(new PointInt32(area.X + Math.Max(0, (area.Width - native.Size.Width) / 2),
                    area.Y + Math.Max(0, (area.Height - native.Size.Height) / 2)));
            }
            RememberWindowPosition();
            Save();
            ClearTransparentHostBackground();
            startupMaskActive = false;
            SetLayeredWindowAttributes(hwnd, 0, 255, 2 /* LWA_ALPHA */);
            if (!uiSettings.AnimationsEnabled) { hostSurface.Opacity = 1; ApplyMouseClickThrough(); UpdateHoverDragMonitor(); return; }
            var fade = new DoubleAnimation
            {
                From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(420)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, hostSurface); Storyboard.SetTargetProperty(fade, "Opacity");
            var reveal = new Storyboard(); reveal.Children.Add(fade);
            reveal.Completed += (_, __) => { hostSurface.Opacity = 1; reveal.Stop(); ApplyMouseClickThrough(); UpdateHoverDragMonitor(); };
            reveal.Begin();
        };
        startupRevealTimer.Start();
    }

    void RememberWindowPosition()
    {
        // Temporary backing windows used by layout/scale previews aren't the
        // user's visible card position. Those paths already track their target.
        if (layoutHostActive || scalePreviewHostActive) return;
        settings.X = native.Position.X;
        settings.Y = native.Position.Y;
    }

    void BeginScalePreviewHost()
    {
        if (!desktopSource.TryGetHostRenderBounds(out int x, out int y, out int width, out int height)) return;
        scalePreviewInsetX = x - native.Position.X; scalePreviewInsetY = y - native.Position.Y;
        scalePreviewInsetWidth = native.Size.Width - width; scalePreviewInsetHeight = native.Size.Height - height;
        scalePreviewWidth = native.Size.Width; scalePreviewHeight = native.Size.Height;
        scalePreviewHostActive = true; preparingScalePreviewHost = true; scalePreviewRevealPending = true; scalePreviewRevealFrames = 2;
        // Keep the filled crop until the enlarged viewport has a covering scene.
        desktopSource.BeginVisualDrag(x, y, width, height, renderImmediately: false);
        try
        {
            // Reserve rounding headroom; keep this host until the preview closes.
            double dpi = GetDpiForWindow(hwnd) / 96.0;
            native.Resize(new SizeInt32((int)Math.Ceiling((Math.Max(368, shownWidgetWidth) + 96) * 1.5 * ScaleReference * dpi),
                (int)Math.Ceiling(WidgetLogicalHeight * 1.5 * ScaleReference * dpi)));
            ClearTransparentHostBackground();
        }
        finally { preparingScalePreviewHost = false; }
    }

    void EndScalePreviewHost()
    {
        scalePreviewSettleTimer?.Stop();
        if (!scalePreviewHostActive) return;
        scalePreviewHostActive = false; scalePreviewSettled = false; scalePreviewRevealPending = false;
        desktopSource.EndVisualDrag();
    }

    void UpdateScalePreviewBounds(bool rendering = false)
    {
        if (!scalePreviewHostActive || preparingScalePreviewHost) return;
        // Let XAML submit its new outline before exposing the initialized spare host.
        if (rendering && scalePreviewRevealPending && --scalePreviewRevealFrames <= 0)
        {
            scalePreviewRevealPending = false;
            UpdateSurfaceClip();
        }
        if (desktopSource.TryGetHostRenderBounds(out int x, out int y, out _, out _))
            desktopSource.UpdateVisualBounds(x, y, Math.Max(1, scalePreviewWidth - scalePreviewInsetWidth),
                Math.Max(1, scalePreviewHeight - scalePreviewInsetHeight));
    }

    void UpdateScalePreviewViewport(int width, int height)
    {
        if (preparingScalePreviewHost) return;
        if (width > native.Size.Width || height > native.Size.Height)
        {
            // Rounded font metrics can make a salary row slightly wider than the
            // original maximum reservation. Grow once, with spare pixels still clipped.
            preparingScalePreviewHost = true;
            try
            {
                native.Resize(new SizeInt32(Math.Max(width, native.Size.Width), Math.Max(height, native.Size.Height)));
                ClearTransparentHostBackground();
            }
            finally { preparingScalePreviewHost = false; }
        }
        double dpi = GetDpiForWindow(hwnd) / 96.0;
        int renderWidth = Math.Max(1, width - scalePreviewInsetWidth);
        int renderHeight = Math.Max(1, height - scalePreviewInsetHeight);
        if (scalePreviewWidth == width && scalePreviewHeight == height &&
            scaleViewportDpi == dpi && scaleViewportScale == PhysicalScale &&
            surface.Width == renderWidth / dpi && surface.Height == renderHeight / dpi &&
            card.Width == surface.Width && card.Height == surface.Height)
        {
            UpdateScalePreviewBounds();
            return;
        }
        scalePreviewWidth = width; scalePreviewHeight = height;
        scaleViewportDpi = dpi; scaleViewportScale = PhysicalScale;
        pixelScale = dpi * PhysicalScale;
        SetRenderGeometry(renderWidth, renderHeight, dpi, PhysicalScale);
        UpdateScalePreviewBounds();
        desktopSource.SynchronizeVisualFrame();
        UpdateSurfaceClip();
    }

    void ApplyTypography()
    {
        double scale = PhysicalScale, dpi = GetDpiForWindow(hwnd) / 96.0;
        double Size(double logical) => Math.Round(logical * scale * dpi) / dpi;
        card.UseLayoutRounding = true;
        coinScoreBadge.UseLayoutRounding = true;
        coinScore.FontSize = Size(18);
        coinScoreHeading.FontSize = Size(12);
        coinScoreTarget.FontSize = Size(14); coinScoreDivider.FontSize = Size(13);
        coinScoreNumbers.Spacing = Size(4);
        coinScoreWords.Spacing = Size(1); coinScoreContent.Spacing = Size(8);
        coinScoreRingHost.Width = coinScoreRingHost.Height = Size(26);
        coinScoreBadge.Padding = new Thickness(0); coinScoreBadge.BorderThickness = new Thickness(0);
        coinScoreBadge.Margin = new Thickness(0, Size(6), Size(16), 0);
        coinBankButton.Width = Size(32); coinBankButton.Height = Size(36);
        coinBankButton.Margin = new Thickness(0, 0, Size(4), Size(10));
        ApplyCoinJarPosition();
        coinBankBalance.Padding = new Thickness(Size(9), Size(5), Size(9), Size(5));
        coinBankBalanceText.FontSize = Size(13);
        caption.FontSize = Size(17); caption.CharacterSpacing = 0;
        countdown.FontSize = Size(36); countdown.Margin = new Thickness(0, Size(4), 0, Size(8));
        foreach (var heading in statTitles)
        {
            heading.FontSize = Size(15);
            UiTypography.SetWeight(heading, Microsoft.UI.Text.FontWeights.Normal, surface.RequestedTheme == ElementTheme.Dark);
            heading.IsTextScaleFactorEnabled = false;
        }
        foreach (var value in new[] { rest, pay, earned })
        {
            value.FontSize = Size(22); value.IsTextScaleFactorEnabled = false;
            value.LineHeight = 0; value.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            value.TextLineBounds = TextLineBounds.Tight;
            value.VerticalAlignment = VerticalAlignment.Center;
            if (value.Parent is Border valueSlot) valueSlot.Height = Size(30);
        }
        foreach (var value in wishPagePercents)
        {
            value.FontSize = Size(18); value.IsTextScaleFactorEnabled = false;
            value.LineHeight = 0; value.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            value.TextLineBounds = TextLineBounds.Tight;
            value.VerticalAlignment = VerticalAlignment.Center;
            if (value.Parent is Border valueSlot) valueSlot.Height = Size(30);
        }
        foreach (var name in wishPageNames) name.FontSize = Size(16);
        foreach (var bar in wishBarHosts) { bar.Height = Size(5); bar.Margin = new Thickness(0); }
        foreach (var track in wishBarTracks) track.CornerRadius = new CornerRadius(Size(2.5));
        foreach (var fill in wishBarFills) fill.CornerRadius = new CornerRadius(Size(2.5));
        UpdateRestTypography();
        caption.IsTextScaleFactorEnabled = false; countdown.IsTextScaleFactorEnabled = false;
        if (card.Child is Grid body)
        {
            body.Margin = new Thickness(Size(24), Size(18), Size(4), Size(18));
            body.ColumnDefinitions[1].Width = new GridLength(132 * scale);
            body.ColumnSpacing = 0;
        }
        FitCaptionToColumn();
        pet.Width = 132 * scale; pet.Height = 150 * scale;
        statsGlass.BlurAmount = Math.Max(3, 8 * scale);
        statsGlass.RefThickness = scale;
        statsGlass.RefFactor = Math.Sqrt(1 + (1.003 * 1.003 - 1) * scale * scale);
        statsGlass.GlareRange = 30 * Math.Sqrt(scale);
        statsGlass.RefFresnelRange = 30 * Math.Sqrt(scale);
        statsGlass.GlareHardness = statsGlass.RefFresnelHardness = 0;
        stats.Padding = new Thickness(Size(8), Size(6), Size(8), Size(6));
        stats.Margin = new Thickness(-Size(8), 0, 0, 0); stats.CornerRadius = new CornerRadius(16 * scale);
        SynchronizeStatsGeometry();
        foreach (var page in wishPagePanels) { page.Spacing = Size(4); page.Margin = new Thickness(Size(12), 0, Size(12), 0); }
        foreach (var page in statsCorePages) if (page is StackPanel panel) panel.Spacing = Size(5);
        ApplyDotTypography();
        earningsEye.Width = Size(20); earningsEye.Height = Size(20);
        earningsVisibilityIcon.FontSize = Size(13);
    }

    string? fittedCaptionText;
    double fittedCaptionPreferred, fittedCaptionAvailable, fittedCaptionFont;

    void FitCaptionToColumn()
    {
        if (card.Child is not Grid body || string.IsNullOrEmpty(caption.Text)) return;
        double dpi = GetDpiForWindow(hwnd) / 96.0;
        double preferred = Math.Round(17 * PhysicalScale * dpi) / dpi;
        double available = card.Width - body.Margin.Left - body.Margin.Right - body.ColumnDefinitions[1].Width.Value - body.ColumnSpacing - 2 / dpi;
        if (coinScoreBadge.Visibility == Visibility.Visible && coinScoreBadge.ActualWidth > 0)
            available = Math.Min(available, card.Width - body.Margin.Left - coinScoreBadge.Margin.Right - coinScoreBadge.ActualWidth - 8 * PhysicalScale);
        if (available <= 0) return;
        if (fittedCaptionText == caption.Text && fittedCaptionPreferred == preferred && fittedCaptionAvailable == available && caption.FontSize == fittedCaptionFont) return;
        fittedCaptionText = caption.Text; fittedCaptionPreferred = preferred; fittedCaptionAvailable = available;
        caption.FontSize = preferred;
        caption.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        if (caption.DesiredSize.Width > available)
            caption.FontSize = Math.Max(1, Math.Floor(preferred * available / caption.DesiredSize.Width * dpi) / dpi);
        fittedCaptionFont = caption.FontSize;
    }

    void UpdateSalaryLayout()
    {
        if (isDragging || disposed) return;
        if (card.Child is Grid body)
        {
            double layoutScale = PhysicalScale;
            var margin = new Thickness(24 * layoutScale, 18 * layoutScale, 4 * layoutScale, 18 * layoutScale);
            if (!body.Margin.Equals(margin)) body.Margin = margin;
            double gap = 0;
            if (body.ColumnSpacing != gap) body.ColumnSpacing = gap;
        }
        if (settings.ShowSalaryLayout)
        {
            // Measure the complete row, not the space left by the current window.
            // Extra-long amounts can expand the card without clipping the last glyph.
            double widest = 0;
            foreach (FrameworkElement page in statsCorePages)
            {
                page.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                widest = Math.Max(widest, page.DesiredSize.Width);
            }
            shownWidgetWidth = Math.Max(shownWidgetWidth, Math.Ceiling((widest + 2 / (GetDpiForWindow(hwnd) / 96.0)) * 3 / PhysicalScale + 168));
        }
        double scale = GetDpiForWindow(hwnd) / 96.0 * PhysicalScale;
        var size = new SizeInt32((int)Math.Ceiling(WidgetLogicalWidth * scale), (int)Math.Ceiling(WidgetLogicalHeight * scale));
        if (layoutTransitionActive)
        {
            layoutTargetWidth = size.Width; layoutTargetHeight = size.Height;
            return;
        }
        if (scalePreviewHostActive)
        {
            UpdateScalePreviewViewport(size.Width, size.Height);
            return;
        }
        if (native.Size.Width != size.Width || native.Size.Height != size.Height)
        {
            double dpi = GetDpiForWindow(hwnd) / 96.0;
            int insetW = 0, insetH = 0;
            if (desktopSource.TryGetHostRenderBounds(out _, out _, out int previousW, out int previousH))
            {
                insetW = Math.Max(0, native.Size.Width - previousW); insetH = Math.Max(0, native.Size.Height - previousH);
            }
            SetRenderGeometry(Math.Max(1, size.Width - insetW), Math.Max(1, size.Height - insetH), dpi, PhysicalScale);
            native.Resize(size);
            ClearTransparentHostBackground(); UpdateRenderViewport();
        }
    }

    void UpdateRenderViewport()
    {
        if (disposed || layoutHostActive) return;
        if (scalePreviewHostActive)
        {
            UpdateScalePreviewViewport(scalePreviewWidth, scalePreviewHeight);
            return;
        }
        double scale = PhysicalScale;
        double dpi = GetDpiForWindow(hwnd) / 96.0;
        pixelScale = dpi * scale;
        if (desktopSource.TryGetRenderBounds(out _, out _, out int renderW, out int renderH))
        {
            // Keep the captured bitmap at one physical pixel per displayed pixel.
            // Only the foreground card is transformed for the user's chosen scale.
            SetRenderGeometry(renderW, renderH, dpi, scale);
        }
        ApplyTypography();
        UpdateSurfaceClip();
    }

    void SetRenderGeometry(int renderWidth, int renderHeight, double dpi, double scale)
    {
        surface.Width = renderWidth / dpi; surface.Height = renderHeight / dpi;
        card.Width = surface.Width; card.Height = surface.Height;
        UpdateCompositionClip();
        desktopSource.SynchronizeGeometry();
        surface.UpdateLayout();
    }

    void UpdateCompositionClip()
    {
        if (surface.Width <= 0 || surface.Height <= 0) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(surface);
        visual.BorderMode = Microsoft.UI.Composition.CompositionBorderMode.Soft;
        if (surfaceGeometry == null)
        {
            surfaceGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
            visual.Clip = visual.Compositor.CreateGeometricClip(surfaceGeometry);
        }
        // This clip travels and resizes with the composed card, rather than exposing
        // HWND pixels before XAML has presented the matching rounded visual.
        surfaceGeometry.Size = new System.Numerics.Vector2((float)surface.Width, (float)surface.Height);
        float radius = (float)Math.Min(36 * PhysicalScale, Math.Min(surface.Width, surface.Height) * .5);
        surfaceGeometry.CornerRadius = new System.Numerics.Vector2(radius);
        glass.SynchronizeScale(PhysicalScale);
        glass.Brush.ShapeRadius = Math.Clamp(radius / Math.Max(1, Math.Min(surface.Width, surface.Height) * .5 - 1), 0, 1);
    }

    void SynchronizeStatsGeometry()
    {
        statsGlass.ShapeRadius = Math.Clamp(stats.CornerRadius.TopLeft / Math.Max(1, Math.Min(stats.ActualWidth, stats.ActualHeight) * .5 - 1), 0, 1);
    }

    void UpdateSurfaceClip()
    {
        if (scalePreviewHostActive && scalePreviewRevealPending) return;
        try
        {
            // Native hit testing follows the same child viewport as the composed clip.
            // The child has a real inset; the spare HWND row is not part of the card.
            int diameter = (int)Math.Round(72 * pixelScale);
            int left = scalePreviewHostActive ? scalePreviewInsetX : 0;
            int top = scalePreviewHostActive ? scalePreviewInsetY : 0;
            int width = Math.Max(1, scalePreviewWidth - scalePreviewInsetWidth);
            int height = Math.Max(1, scalePreviewHeight - scalePreviewInsetHeight);
            if (!scalePreviewHostActive)
            {
                width = native.Size.Width; height = native.Size.Height;
                if (desktopSource.TryGetHostRenderBounds(out int childX, out int childY, out int childWidth, out int childHeight))
                {
                    left = childX - native.Position.X; top = childY - native.Position.Y;
                    width = Math.Min(childWidth, (int)Math.Ceiling(surface.Width * GetDpiForWindow(hwnd) / 96.0));
                    height = Math.Min(childHeight, (int)Math.Ceiling(surface.Height * GetDpiForWindow(hwnd) / 96.0));
                }
            }
            // Keep the preview host region stable; composition owns the changing outline.
            // Shrinking its native corner before XAML presents can expose a white wedge.
            if (scalePreviewHostActive)
            {
                width = Math.Max(1, native.Size.Width - scalePreviewInsetWidth);
                height = Math.Max(1, native.Size.Height - scalePreviewInsetHeight);
                diameter = 0;
            }
            int right = left + Math.Max(1, width), bottom = top + Math.Max(1, height);
            var clipBounds = (left, top, right, bottom, diameter);
            if (surfaceClipBounds == clipBounds) return;
            // HRGN edges are binary. A real outward guard preserves the compositor's
            // partial-coverage pixels, including the right/bottom raster boundary.
            const int guard = 4;
            SetProp(hwnd, "OffWorkWidget.GlassDiameterPx", new IntPtr(diameter));
            SetProp(hwnd, "OffWorkWidget.GlassGuardPx", new IntPtr(guard));
            IntPtr region = CreateContentRegion(left, top, right, bottom, diameter, guard);
            if (region != IntPtr.Zero)
            {
                if (SetWindowRgn(hwnd, region, true) == 0)
                    DeleteObject(region); // Windows owns the region only after success.
                else
                {
                    // Retry identical geometry if its native backing couldn't be
                    // cleared during a transient resize; don't cache that failure.
                    surfaceClipBounds = ClearTransparentHostBackground() ? clipBounds : null;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    void Refresh()
    {
        DateTime now = DateTime.Now;
        if (!settings.HideAmount && now >= revealedUntil) HideSalary();
        bool work = Logic.WorkingDay(now, settings); var hours = Logic.WorkHours(now, settings); TimeSpan start = hours.Start, end = hours.End;
        TimeSpan? left = null;
        int days = Logic.UntilRest(now, settings);
        bool restStarted = Logic.RestStarted(now, settings, days);
        string captionText, status = "";
        if (!work) { captionText = "今天休息，好好放松"; status = "休息中"; }
        else if (now.TimeOfDay < start) { captionText = "距离上班还有"; left = start - now.TimeOfDay; }
        else if (now.TimeOfDay < end) { captionText = "距离下班还有"; left = end - now.TimeOfDay; }
        else if (restStarted) { captionText = "已经放假啦"; status = "好好休息"; }
        else { captionText = "今天辛苦了，自由时间到"; status = "已下班"; }
        if (now < reminderUntil && reminderDue != 0)
            captionText = reminderDue == 3 ? "起身走走，喝口水吧"
                : reminderDue == 1 ? "歇一歇，喝口水吧" : "活动一下，放松身心";
        captionStream.Show(captionText, animate: true, prepareLayout: FitCaptionToColumn);
        bool showStatus = !left.HasValue;
        if (countdownIsStatus != showStatus)
        {
            countdownIsStatus = showStatus;
            countdown.FontFamily = new FontFamily(countdownIsStatus ? "Microsoft YaHei" : "Segoe UI");
            UiTypography.SetWeight(countdown, countdownIsStatus ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Medium, surface.RequestedTheme == ElementTheme.Dark);
        }
        if (left.HasValue) countdownStream.Show(string.Format(CultureInfo.InvariantCulture, "{0:00} : {1:00} : {2:00}", (int)left.Value.TotalHours, left.Value.Minutes, left.Value.Seconds), animate: false);
        else countdownStream.Show(status, animate: true);
        statTitles[0].Text = restStarted ? "下次上班" : "距休息";
        if (restStarted)
        {
            var next = Logic.NextWorkStart(now, settings);
            rest.Text = !next.HasValue ? "未设置" : (next.Value.Date - now.Date).Days < 7
                ? "周" + "日一二三四五六"[(int)next.Value.DayOfWeek] : next.Value.ToString("M/d", CultureInfo.InvariantCulture);
            ToolTipService.SetToolTip(rest, next.HasValue ? "下次上班：" + next.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "尚未设置可用的上班日期");
        }
        else
        {
            rest.Text = days < 0 ? "未设置" : days + "天";
            ToolTipService.SetToolTip(rest, Logic.RestDescription(now, settings, days));
        }
        UpdateRestTypography();
        pay.Text = Logic.UntilPay(now, settings.PayDay) + "天";
        if (!layoutTransitionActive)
        {
            salaryArea.Visibility = settings.ShowSalaryLayout ? Visibility.Visible : Visibility.Collapsed;
            var salaryColumnWidth = settings.ShowSalaryLayout ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            if (!statsRow.ColumnDefinitions[2].Width.Equals(salaryColumnWidth)) statsRow.ColumnDefinitions[2].Width = salaryColumnWidth;
        }
        else salaryArea.Visibility = Visibility.Visible;
        UpdateStatsPager();
        earned.Text = settings.HideAmount ? "***元" : Logic.Earned(now, settings).ToString("F2", CultureInfo.InvariantCulture) + "元";
        earningsVisibilityIcon.Glyph = settings.HideAmount ? "\uED1A" : "\uE890";
        string eyeAction = settings.HideAmount ? "显示金额（需验证密码）" : "隐藏金额";
        ToolTipService.SetToolTip(earningsEye, eyeAction);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(earningsEye, eyeAction);
        UpdateSalaryLayout();
        FitCaptionToColumn();
        settingsWindow?.RefreshSalaryLayoutButton();
        int due = reminders.Tick(now, elapsed.Elapsed, settings);
        if (due != 0) QueueReminders(due);
        UpdateNap(now);
        if (!smoke && holidayDay != now.Date)
        {
            holidayDay = now.Date;
            // A manual make-up day only applies to its own date; drop it once
            // that date has passed, even when the app stays open overnight.
            string todayKey = now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (settings.DateAdjustments != null &&
                settings.DateAdjustments.RemoveAll(a => a == null || string.CompareOrdinal(a.Date, todayKey) < 0) > 0)
            {
                settingsWindow?.PruneExpiredAdjustments();
                Save();
            }
            if (settings.UseChinaHolidays)
            {
                // Upcoming years can be published early; keep two years ahead warm.
                _ = calendar.RefreshAsync(now.Year, false);
                _ = calendar.RefreshAsync(now.Year + 1, false);
                _ = calendar.RefreshAsync(now.Year + 2, false);
            }
        }
    }

    System.Collections.Generic.List<WishEntry> ValidWishes()
    {
        var wishes = new System.Collections.Generic.List<WishEntry>();
        if (settings.Wishes != null)
            foreach (var wish in settings.Wishes)
                if (wish != null && !string.IsNullOrWhiteSpace(wish.Name) && wish.Target > 0 && wish.Saved >= 0)
                    wishes.Add(wish);
        return wishes;
    }

    void UpdateWishCompletion(System.Collections.Generic.List<WishEntry> wishes, decimal[] progress)
    {
        var completed = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < wishes.Count && i < progress.Length; i++)
        {
            var wish = wishes[i];
            decimal target = Math.Max(0, wish.Target);
            if (target <= 0 || progress[i] < target - .005m) continue;
            string key = wish.Name + "|" + wish.Target.ToString(CultureInfo.InvariantCulture);
            completed.Add(key);
            if (wishCompletionPrimed && settings.WishEnabled && !completedWishKeys.Contains(key))
                CelebrateWishCompleted(wish.Name);
        }
        completedWishKeys.Clear();
        foreach (var key in completed) completedWishKeys.Add(key);
        wishCompletionPrimed = true;
    }

    void UpdateNap(DateTime now)
    {
        // Lunch is a break for every pet: discard queued drink/walk reminders;
        // ReminderSchedule restarts both counters from zero when lunch ends.
        if (Logic.IsWorkdayLunch(now, settings))
            pendingReminders = 0;
        if (settings.PetId != "polar-bear")
        {
            if (napActive) EndNap();
            napPending = false;
            return;
        }
        if (napActive)
        {
            if (now < napEndAt) return;
            bool playStartWork = settings.PetId == "polar-bear";
            if (playStartWork) PlayStartWork();
            else { EndNap(); StartNextReminder(); }
            return;
        }
        (DateTime? PlayFrom, DateTime End, bool Lunch)? window;
        try { window = Logic.NapWindow(now, settings); }
        catch (FormatException) { window = null; }
        if (!window.HasValue)
        {
            napPending = false;
            return;
        }
        if (reminderPetActive || coinPetActive)
        {
            // Let the current reminder or coin action finish; the nap starts on
            // the next tick and then holds until the window ends.
            napPending = true;
            return;
        }
        napPending = false;
        BeginNap(now, window.Value);
    }

    void BeginNap(DateTime now, (DateTime? PlayFrom, DateTime End, bool Lunch) window)
    {
        if (exiting || disposed || napActive || settings.PetId != "polar-bear") return;
        CancelSlack(true);
        napActive = true;
        napEndAt = window.End;
        int revision = ++napRevision;
        petHoverTimer?.Stop();
        // Entering near the start plays the transition; opening the app mid-lunch
        // or after work goes straight to the sleeping frame.
        bool play = window.PlayFrom.HasValue &&
            now >= window.PlayFrom.Value && now - window.PlayFrom.Value <= TimeSpan.FromSeconds(6) &&
            window.End - now >= TimeSpan.FromSeconds(11);
        byte[]? fromFrame = null;
        if (play)
        {
            try { fromFrame = petAnimation?.CaptureFrame(); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException) { }
        }
        petAnimation?.Stop();
        CancelPolarCoinAction(false);
        reminderAnimation?.Dispose(); reminderAnimation = null;
        waterPetOverlay.Visibility = Visibility.Collapsed; waterPetOverlay.Source = null; pet.Opacity = 1;
        _ = LoadNapAsync(revision, play, fromFrame);
        GlassProfile.Event($"pet-nap-start {(play ? "play" : "hold")} {(window.Lunch ? "lunch" : "off-work")} remain={(int)(window.End - now).TotalSeconds}");
    }

    async Task LoadNapAsync(int revision, bool play, byte[]? fromFrame)
    {
        try
        {
            var animation = await PetAnimationPlayer.LoadAsync(pet, Path.Combine(AppContext.BaseDirectory, "assets", "polar-bear-nap.frames.br")).ConfigureAwait(false);
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (exiting || disposed || revision != napRevision || !napActive || settings.PetId != "polar-bear")
                { animation.Dispose(); return; }
                napAnimation = animation;
                if (play) animation.PlayOnceFrom(fromFrame, 180); else animation.ShowLastFrame();
                GlassProfile.Event("pet-nap-ready " + (play ? "play" : "last"));
            })) animation.Dispose();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is OverflowException || ex is COMException)
        {
            Debug.WriteLine("北极熊午睡动画无法载入：" + ex.Message);
            GlassProfile.Event("pet-nap-error " + ex.GetType().Name);
            DispatcherQueue.TryEnqueue(() => EndNap());
        }
    }

    void EndNap(bool startReminders = true, bool resetIdle = true)
    {
        if (!napActive && napAnimation == null) return;
        napActive = false; napPending = false; napRevision++;
        napEndAt = DateTime.MinValue;
        CancelPolarCoinAction(false);
        var animation = napAnimation; napAnimation = null;
        animation?.Dispose();
        if (resetIdle)
        {
            if (petAnimation != null)
            {
                petAnimation.ResetIdle();
                UpdatePetHoverMonitor();
            }
            else LoadPet();
        }
        GlassProfile.Event("pet-nap-end");
        if (startReminders) StartNextReminder();
    }

    void PlayStartWork()
    {
        if (exiting || disposed || startWorkActive || settings.PetId != "polar-bear") return;
        startWorkActive = true;
        int revision = ++startWorkRevision;
        petHoverTimer?.Stop();
        CancelPolarCoinAction(false);
        reminderAnimation?.Dispose(); reminderAnimation = null;
        // Keep the nap's last frame visible while the next pack loads; EndNap
        // happens only when the wake-up animation is ready, so there is no
        // idle-frame flash between the two animations.
        GlassProfile.Event("pet-start-work-load");
        _ = LoadStartWorkAsync(revision);
    }

    async Task LoadStartWorkAsync(int revision)
    {
        try
        {
            var animation = await PetAnimationPlayer.LoadAsync(pet, Path.Combine(AppContext.BaseDirectory, "assets", "polar-bear-start-work.frames.br")).ConfigureAwait(false);
            var duration = animation.Duration;
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (exiting || disposed || revision != startWorkRevision || !startWorkActive || settings.PetId != "polar-bear")
                { animation.Dispose(); return; }
                startWorkAnimation?.Dispose();
                startWorkAnimation = animation;
                if (napActive) EndNap(startReminders: false, resetIdle: false);
                petAnimation?.Stop();
                animation.PlayOnce();
                GlassProfile.Event("pet-start-work-play");
            })) { animation.Dispose(); return; }
            await Task.Delay(duration + TimeSpan.FromMilliseconds(120)).ConfigureAwait(false);
            DispatcherQueue.TryEnqueue(() => EndStartWork(revision));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is OverflowException || ex is COMException)
        {
            Debug.WriteLine("北极熊上班动画无法载入：" + ex.Message);
            GlassProfile.Event("pet-start-work-error " + ex.GetType().Name);
            DispatcherQueue.TryEnqueue(() => EndStartWork(revision));
        }
    }

    void EndStartWork(int revision)
    {
        if (revision != startWorkRevision || !startWorkActive) return;
        startWorkActive = false;
        var animation = startWorkAnimation; startWorkAnimation = null;
        animation?.Dispose();
        if (napActive) EndNap(startReminders: false, resetIdle: false);
        if (petAnimation != null)
        {
            petAnimation.ResetIdle();
            UpdatePetHoverMonitor();
        }
        else LoadPet();
        GlassProfile.Event("pet-start-work-end");
        StartNextReminder();
    }

    void QueueReminders(int due)
    {
        pendingReminders |= due & 3;
        StartNextReminder();
    }

    void StartNextReminder()
    {
        // Never start a queued reminder during lunch, even if it was queued
        // in the last tick before lunch began.
        if (pendingReminders != 0 && Logic.IsWorkdayLunch(DateTime.Now, settings))
            pendingReminders = 0;
        if (reminderDue != 0 || pendingReminders == 0 || napActive || napPending || startWorkActive || exiting || disposed) return;
        bool water = (pendingReminders & 1) != 0, walk = (pendingReminders & 2) != 0;
        reminderDue = (water ? 1 : 0) | (walk ? 2 : 0);
        pendingReminders = 0;
        // A combined reminder plays the stretch pose first, then the cup-walk pose.
        reminderUntil = DateTime.Now.AddSeconds(reminderDue == 3 ? 22 : 20);
        if (settings.ReminderPopup) ShowReminderPopup(reminderDue);
        GlassProfile.Event("pet-reminder-queued-start " + reminderDue);
        _ = StartReminderAnimation();
    }

    static (string Title, string Message, string Body) ReminderText(int due) => due == 3
        ? ("休息提醒", "起身走走，喝口水吧", "先舒展一下身体，再补充水分。")
        : due == 1
            ? ("喝水提醒", "喝口水，休息一下", "补充水分，让自己舒服一点。")
            : ("活动提醒", "站起来活动一下", "伸伸懒腰，放松一下身体。");

    void ShowReminderPopup(int due)
    {
        Window? popup = null;
        try
        {
            if (reminderPopup != null && refreshReminderPopup != null)
            {
                refreshReminderPopup(due);
                reminderPopup.AppWindow.Show(false);
                return;
            }
            bool dark = SystemTheme.IsDark();
            var reminderText = ReminderText(due);
            popup = new Window { Title = reminderText.Title };
            bool acrylicSupported = Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
            var background = new SolidColorBrush(acrylicSupported
                ? (dark ? Windows.UI.Color.FromArgb(84, 4, 8, 14) : Windows.UI.Color.FromArgb(34, 92, 104, 120))
                : (dark ? Windows.UI.Color.FromArgb(255, 38, 38, 41) : Windows.UI.Color.FromArgb(255, 248, 248, 250)));
            var textBrush = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 245, 245, 247) : Windows.UI.Color.FromArgb(255, 28, 28, 30));
            var secondaryBrush = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 172, 172, 180) : Windows.UI.Color.FromArgb(255, 105, 105, 114));
            var panel = new Grid { Padding = new Thickness(22, 16, 22, 18) };
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new Grid { Height = 32 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headerTitle = new TextBlock { Text = reminderText.Title, Foreground = secondaryBrush, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }; header.Children.Add(headerTitle);
            var dismiss = new Button { Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 }, Width = 32, Height = 32, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = secondaryBrush, BorderThickness = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dismiss, "关闭提醒");
            Grid.SetColumn(dismiss, 1); header.Children.Add(dismiss); panel.Children.Add(header);
            var message = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var messageTitle = new TextBlock { Text = reminderText.Message, Foreground = textBrush, FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }; message.Children.Add(messageTitle);
            var messageBody = new TextBlock { Text = reminderText.Body, Foreground = secondaryBrush, FontSize = 14, TextWrapping = TextWrapping.Wrap }; message.Children.Add(messageBody);
            var messageBackground = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 65, 65, 68) : Windows.UI.Color.FromArgb(255, 250, 250, 251));
            var messageCard = new Border { Background = messageBackground, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Center, Child = message };
            var messageGroup = new StackPanel { MaxWidth = 420, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            messageGroup.Children.Add(messageCard);
            Grid.SetRow(messageGroup, 1); panel.Children.Add(messageGroup);
            var close = new Button { Content = "知道了", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 88, Height = 36, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 122, 255)), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new Thickness(0) };
            var owned = popup;
            close.Click += (_, __) => CloseReminderPopup();
            dismiss.Click += (_, __) => CloseReminderPopup();
            messageGroup.Children.Add(close);
            // Use the same antialiased composition contour as the settings window.
            var popupSurface = new Grid { RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light };
            var frostedBackground = new SystemBackdropElement { CornerRadius = new CornerRadius(24), IsHitTestVisible = false, SystemBackdrop = acrylicSupported ? new ActiveAcrylicBackdrop() : null };
            popupSurface.Children.Add(frostedBackground);
            popupSurface.Children.Add(new Border { Background = background, Child = panel });
            popup.Content = popupSurface;
            popup.SystemBackdrop = new WinUIEx.TransparentTintBackdrop(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            WinUIEx.Messaging.WindowMessageMonitor? popupFrameMonitor = null, popupRenderMonitor = null;
            Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? popupGeometry = null;
            IntPtr popupRenderHandle = IntPtr.Zero;
            bool popupClosed = false;
            void PaintPopupFrame(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
            {
                if (args.Message.MessageId != 0x0014 ||
                    !GetClientRect(args.Message.Hwnd, out NativeClientRect bounds)) return;
                IntPtr dc = (IntPtr)args.Message.WParam;
                if (dc == IntPtr.Zero || FillRect(dc, ref bounds, GetStockObject(4)) == 0) return;
                args.Result = (IntPtr)1;
                args.Handled = true;
            }
            popup.Closed += (_, __) =>
            {
                popupClosed = true;
                popupRenderMonitor?.Dispose(); popupFrameMonitor?.Dispose();
                frostedBackground.SystemBackdrop = null;
                popup.SystemBackdrop = null;
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(popupSurface).Clip = null;
                popupGeometry?.Dispose();
                if (ReferenceEquals(reminderPopup, owned)) { reminderPopup = null; refreshReminderPopup = null; }
            };
            reminderPopup = popup;
            var popupWindow = popup.AppWindow;
            popupWindow.Closing += (_, e) => { e.Cancel = true; popupWindow.Hide(); };
            if (popupWindow.Presenter is OverlappedPresenter presenter)
            { presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true; }
            // Win10 does not round borderless windows automatically; match the settings radius in physical pixels.
            IntPtr popupHandle = WinRT.Interop.WindowNative.GetWindowHandle(popup);
            PositionReminderPopup(popupWindow, popupHandle);
            int popupStyle = GetWindowLong(popupHandle, GWL_STYLE);
            SetWindowLong(popupHandle, GWL_STYLE, (popupStyle & ~(0x00C00000 | 0x00040000 | 0x00020000 | 0x00010000)) | WS_POPUP);
            SetWindowPos(popupHandle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
            int noBorder = -2, ncDisabled = 1, round = 1;
            DwmSetWindowAttribute(popupHandle, 2, ref ncDisabled, 4);
            DwmSetWindowAttribute(popupHandle, 33, ref round, 4);
            DwmSetWindowAttribute(popupHandle, 34, ref noBorder, 4);
            popup.ExtendsContentIntoTitleBar = true;
            popup.SetTitleBar(null);
            popupFrameMonitor = new WinUIEx.Messaging.WindowMessageMonitor(popupHandle);
            popupFrameMonitor.WindowMessageReceived += PaintPopupFrame;
            void UpdatePopupContour()
            {
                if (popupClosed) return;
                SetWindowRgn(popupHandle, IntPtr.Zero, false);
                double w = popupSurface.ActualWidth, h = popupSurface.ActualHeight;
                if ((w <= 0 || h <= 0) && GetClientRect(popupHandle, out NativeClientRect bounds))
                {
                    double dpi = Math.Max(96, GetDpiForWindow(popupHandle)) / 96d;
                    w = (bounds.Right - bounds.Left) / dpi; h = (bounds.Bottom - bounds.Top) / dpi;
                }
                if (w > 0 && h > 0)
                {
                    var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(popupSurface);
                    if (popupGeometry == null)
                    {
                        popupGeometry = visual.Compositor.CreateRoundedRectangleGeometry();
                        visual.Clip = visual.Compositor.CreateGeometricClip(popupGeometry);
                    }
                    popupGeometry.Size = new System.Numerics.Vector2((float)w, (float)h);
                    popupGeometry.CornerRadius = new System.Numerics.Vector2(24);
                }
                IntPtr render = FindWindowEx(popupHandle, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
                if (render != IntPtr.Zero && render != popupRenderHandle)
                {
                    popupRenderMonitor?.Dispose(); popupRenderHandle = render;
                    popupRenderMonitor = new WinUIEx.Messaging.WindowMessageMonitor(render);
                    popupRenderMonitor.WindowMessageReceived += PaintPopupFrame;
                }
                ClearTransparentWindowBackground(popupHandle);
                if (popupRenderHandle != IntPtr.Zero) ClearTransparentWindowBackground(popupRenderHandle);
            }
            popupSurface.Loaded += (_, __) => UpdatePopupContour();
            popupSurface.SizeChanged += (_, __) => UpdatePopupContour();
            UpdatePopupContour();
            refreshReminderPopup = newDue =>
            {
                bool isDark = SystemTheme.IsDark();
                var text = ReminderText(newDue);
                owned.Title = text.Title;
                headerTitle.Text = text.Title;
                messageTitle.Text = text.Message;
                messageBody.Text = text.Body;
                popupSurface.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
                UiTypography.Apply(popupSurface, isDark);
                if (frostedBackground.SystemBackdrop is ActiveAcrylicBackdrop acrylic) acrylic.UpdateTheme(isDark);
                background.Color = acrylicSupported ? (isDark ? Windows.UI.Color.FromArgb(84, 4, 8, 14) : Windows.UI.Color.FromArgb(34, 92, 104, 120)) : (isDark ? Windows.UI.Color.FromArgb(255, 38, 38, 41) : Windows.UI.Color.FromArgb(255, 248, 248, 250));
                messageBackground.Color = isDark ? Windows.UI.Color.FromArgb(255, 65, 65, 68) : Windows.UI.Color.FromArgb(255, 250, 250, 251);
                textBrush.Color = isDark ? Windows.UI.Color.FromArgb(255, 245, 245, 247) : Windows.UI.Color.FromArgb(255, 28, 28, 30);
                secondaryBrush.Color = isDark ? Windows.UI.Color.FromArgb(255, 172, 172, 180) : Windows.UI.Color.FromArgb(255, 105, 105, 114);
                PositionReminderPopup(popupWindow, popupHandle);
            };
            refreshReminderPopup(due);
            popupWindow.Show(false);
            GlassProfile.Event("reminder-popup-show " + due);
        }
        catch (Exception ex) when (ex is COMException || ex is InvalidOperationException || ex is ArgumentException)
        {
            Debug.WriteLine("提醒弹窗无法显示：" + ex.Message);
            GlassProfile.Event("reminder-popup-error " + ex.GetType().Name);
            CloseReminderPopup();
        }
    }

    void PositionReminderPopup(AppWindow popupWindow, IntPtr popupHandle)
    {
        var targetPoint = new Win32Point { X = settings.ReminderX, Y = settings.ReminderY };
        IntPtr targetMonitor = settings.ReminderPosition == "custom"
            ? MonitorFromPoint(targetPoint, 2 /* MONITOR_DEFAULTTONEAREST */)
            : MonitorFromWindow(hwnd, 2);
        if (targetMonitor != IntPtr.Zero && MonitorFromWindow(popupHandle, 2) != targetMonitor)
        {
            // Move to the destination before measuring the popup's effective DPI.
            double initialScale = Math.Max(1, GetDpiForWindow(popupHandle) / 96.0);
            popupWindow.MoveAndResize(GetReminderPopupBounds(initialScale));
        }
        double scale = Math.Max(1, GetDpiForWindow(popupHandle) / 96.0);
        popupWindow.MoveAndResize(GetReminderPopupBounds(scale));
    }
    RectInt32 GetReminderPopupBounds(double scale)
    {
        bool custom = settings.ReminderPosition == "custom";
        var area = custom
            ? DisplayArea.GetFromPoint(new PointInt32(settings.ReminderX, settings.ReminderY), DisplayAreaFallback.Nearest).WorkArea
            : DisplayArea.GetFromWindowId(native.Id, DisplayAreaFallback.Primary).WorkArea;
        var bounds = GetReminderBounds(area, settings.ReminderPosition, settings.ReminderWidth, settings.ReminderHeight, scale);
        if (custom)
        {
            bounds.X = Math.Clamp(settings.ReminderX, area.X, area.X + area.Width - bounds.Width);
            bounds.Y = Math.Clamp(settings.ReminderY, area.Y, area.Y + area.Height - bounds.Height);
        }
        return bounds;
    }
    // Position within the widget monitor's work area, including negative monitor coordinates.
    internal static RectInt32 GetReminderBounds(RectInt32 area, string position, double widthDip, double heightDip, double scale)
    {
        int width = Math.Min(area.Width, (int)Math.Ceiling(widthDip * scale));
        int height = Math.Min(area.Height, (int)Math.Ceiling(heightDip * scale));
        int marginX = Math.Min(20, area.Width - width), marginY = Math.Min(20, area.Height - height);
        int left = area.X + marginX, right = area.X + area.Width - width - marginX;
        int top = area.Y + marginY, bottom = area.Y + area.Height - height - marginY;
        int x = position switch { "top-left" or "center-left" or "bottom-left" => left, "top-right" or "center-right" or "bottom-right" => right, _ => area.X + (area.Width - width) / 2 };
        int y = position switch { "top-left" or "top-center" or "top-right" => top, "bottom-left" or "bottom-center" or "bottom-right" => bottom, _ => area.Y + (area.Height - height) / 2 };
        return new RectInt32(x, y, width, height);
    }

    void CloseReminderPopup()
    {
        reminderPopup?.AppWindow.Hide();
    }

    Task StartReminderAnimation()
    {
        if (settings.PetId == "polar-bear") return StartPolarReminder(reminderDue);
        reminderPetActive = true;
        petHoverTimer?.Stop();
        petAnimation?.PlayOnce();
        walkTimer.Stop();
        walkTimer.Start();
        return Task.CompletedTask;
    }


    async Task StartPolarReminder(int due)
    {
        if (exiting || disposed || settings.PetId != "polar-bear") return;
        int revision = ++reminderAnimationRevision;
        CancelSlack(true);
        CancelPolarCoinAction(false);
        reminderPetActive = true;
        petHoverTimer?.Stop();
        petAnimation?.Stop();
        petAnimation?.ShowPoster();
        reminderAnimation?.Dispose(); reminderAnimation = null;
        walkTimer.Stop(); walkTimer.Start();
        // Each reminder owns the shared pet image until its display period ends.
        bool water = (due & 1) != 0, walk = (due & 2) != 0;
        if (water && walk)
        {
            var stretch = await PlayPolarPhase(true, revision).ConfigureAwait(false);
            if (stretch == null) return;
            await Task.Delay(stretch.Value).ConfigureAwait(false);
            if (exiting || disposed || revision != reminderAnimationRevision || settings.PetId != "polar-bear" || DateTime.Now >= reminderUntil) return;
            await PlayPolarPhase(false, revision).ConfigureAwait(false);
            return;
        }
        await PlayPolarPhase(water, revision).ConfigureAwait(false);
    }

    async Task<TimeSpan?> PlayPolarPhase(bool stretch, int revision)
    {
        string file = stretch ? "polar-bear-stretch.frames.br" : "polar-bear-cup-walk.frames.br";
        try
        {
            var animation = await PetAnimationPlayer.LoadAsync(stretch ? pet : waterPetOverlay, Path.Combine(AppContext.BaseDirectory, "assets", file)).ConfigureAwait(false);
            var duration = animation.Duration;
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (exiting || disposed || revision != reminderAnimationRevision || settings.PetId != "polar-bear" || DateTime.Now >= reminderUntil)
                { animation.Dispose(); return; }
                reminderAnimation?.Dispose();
                reminderAnimation = animation;
                animation.ShowPoster();
                if (!stretch) { pet.Opacity = 0; waterPetOverlay.Visibility = Visibility.Visible; UpdateWaterOverlayBounds(); }
                animation.PlayOnce();
                GlassProfile.Event("pet-reminder " + (stretch ? "stretch" : "cup-walk"));
            })) { animation.Dispose(); return null; }
            return duration;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is OverflowException || ex is COMException)
        { Debug.WriteLine("北极熊提醒动画无法载入：" + ex.Message); GlassProfile.Event("pet-reminder-error " + ex.GetType().Name); return null; }
    }

    void UpdateWaterOverlayBounds()
    {
        if (waterPetOverlay.Visibility != Visibility.Visible || disposed || pet.ActualWidth <= 0 || pet.ActualHeight <= 0) return;
        try
        {
            // The idle asset is square, centered within the taller Image. The
            // 4:3 water canvas contains that square in its rightmost 3/4.
            double side = Math.Min(pet.ActualWidth, pet.ActualHeight);
            var origin = pet.TransformToVisual(surface).TransformPoint(new Point((pet.ActualWidth - side) / 2, (pet.ActualHeight - side) / 2));
            double width = side * 4 / 3, left = origin.X + side - width, top = origin.Y;
            if (!double.IsFinite(left) || !double.IsFinite(top)) return;
            if (!double.IsFinite(waterPetOverlay.Width) || Math.Abs(waterPetOverlay.Width - width) > .01) waterPetOverlay.Width = width;
            if (!double.IsFinite(waterPetOverlay.Height) || Math.Abs(waterPetOverlay.Height - side) > .01) waterPetOverlay.Height = side;
            var margin = waterPetOverlay.Margin;
            if (Math.Abs(margin.Left - left) > .01 || Math.Abs(margin.Top - top) > .01)
                waterPetOverlay.Margin = new Thickness(left, top, 0, 0);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is COMException)
        { Debug.WriteLine("喝水动画位置更新失败：" + ex.Message); }
    }

    void EndPolarReminder()
    {
        reminderAnimationRevision++;
        reminderPetActive = false;
        reminderDue = 0;
        var animation = reminderAnimation;
        reminderAnimation = null;
        animation?.Dispose();
        waterPetOverlay.Visibility = Visibility.Collapsed; waterPetOverlay.Source = null; pet.Opacity = 1;
        if (petAnimation != null)
        {
            petAnimation.ResetIdle();
            UpdatePetHoverMonitor();
        }
        else
        {
            LoadPet();
        }
        GlassProfile.Event("pet-reminder-reset");
    }

    void UpdateAppearance()
    {
        long appearanceStarted = GlassProfile.Timestamp();
        double layoutMs = 0;
        if (analysisRegionsDirty)
        {
            long layoutStarted = GlassProfile.Timestamp();
            UpdateAnalysisRegions();
            layoutMs = GlassProfile.Ms(layoutStarted);
        }
        double textTarget;
        double glassTarget;
        if (settings.AutomaticAppearance)
        {
            if (desktopSource.IsReady)
            {
                double textLight = desktopSource.TextRegionLuminance ?? desktopSource.TextLuminance;
                textTarget = textLight < .43 ? 1 : textLight > .53 ? 0 : textAppearanceInitialized ? textTargetTone : textLight < .48 ? 1 : 0;
                double light = desktopSource.Luminance;
                glassTarget = light < .43 ? 1 : light > .53 ? 0 : glassAppearanceInitialized ? glassTargetTone : light < .48 ? 1 : 0;
            }
            else
            {
                double sysDark = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128 ? 1.0 : 0.0;
                textTarget = sysDark;
                glassTarget = sysDark;
            }
        }
        else
        {
            glassTarget = settings.NightMode ? 1.0 : 0.0;
            if (desktopSource.IsReady)
            {
                // Both manual materials stay fixed, but the outer heading
                // follows the background so it stays readable on any wallpaper.
                double textLight = desktopSource.TextRegionLuminance ?? desktopSource.TextLuminance;
                textTarget = textLight < .43 ? 1 : textLight > .53 ? 0 : textAppearanceInitialized ? textTargetTone : textLight < .48 ? 1 : 0;
            }
            else textTarget = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128 ? 1.0 : 0.0;
        }

        if (!textAppearanceInitialized)
        {
            textAppearanceInitialized = true;
            lightness = textFromTone = textTargetTone = textTarget;
        }
        else if (textTarget != textTargetTone)
        {
            UpdateTextTransition();
            textFromTone = lightness; textTargetTone = textTarget;
            textAppearanceClock.Restart();
        }
        UpdateTextTransition();

        // The inner stats/wish card keeps the day/night material; only the text
        // ink follows the background.
        if (settings.AutomaticAppearance)
        {
            if (desktopSource.IsReady && desktopSource.StatsDark is bool dark) statsDark = dark;
            else if (!desktopSource.IsReady && !statsAppearanceInitialized) statsDark = textTarget > .5;
        }
        else statsDark = settings.NightMode;
        double targetTone = statsDark ? 1 : 0;
        if (!statsAppearanceInitialized)
        {
            statsAppearanceInitialized = true;
            statsTone = statsFromTone = statsTargetTone = targetTone;
        }
        else if (targetTone != statsTargetTone)
        {
            UpdateStatsTransition();
            statsFromTone = statsTone; statsTargetTone = targetTone;
            statsAppearanceClock.Restart();
        }
        UpdateStatsTransition();
        Brush statsBrush = string.IsNullOrEmpty(LiquidGlassWinUI.LiquidGlassBrush.LastError) ? statsGlass : statsBackground;
        if (stats.Background != statsBrush) stats.Background = statsBrush;

        var expBg = string.IsNullOrEmpty(LiquidGlassWinUI.LiquidGlassBrush.LastError) ? (Brush)glass.Brush : glassFallback;
        if (backdropCard.Background != expBg) backdropCard.Background = expBg;

        // Manual modes only dye the glass when the wallpaper fights the chosen
        // palette; on a matching background a flat dye washes out the glass.
        double tintTarget = 0;
        // Manual night uses a charcoal tint; day stays clear frost.
        if (!settings.AutomaticAppearance && settings.NightMode) tintTarget = .5;
        // Manual day is a clear frost, manual night a smoked frost: both diffuse
        // the wallpaper instead of switching material with the background.
        double blurTarget = !settings.AutomaticAppearance ? 6 : 0;
        if (Math.Abs(glass.Brush.BlurAmount - blurTarget) > .01) glass.Brush.BlurAmount = blurTarget;
        if (!glassAppearanceInitialized)
        {
            glassAppearanceInitialized = true;
            glassTone = glassFromTone = glassTargetTone = glassTarget;
            glassTint = glassFromTint = glassTargetTint = tintTarget;
        }
        else if (glassTarget != glassTargetTone || tintTarget != glassTargetTint)
        {
            UpdateGlassTransition();
            glassFromTone = glassTone; glassTargetTone = glassTarget;
            glassFromTint = glassTint; glassTargetTint = tintTarget;
            glassAppearanceClock.Restart();
        }
        UpdateGlassTransition();

        var theme = glassTarget > .5 ? ElementTheme.Dark : ElementTheme.Light;
        if (surface.RequestedTheme != theme)
        {
            surface.RequestedTheme = theme;
            UiTypography.Apply(surface, theme == ElementTheme.Dark);
            fittedCaptionText = null;
            FitCaptionToColumn();
        }
        if (hostSurface.RequestedTheme != theme) hostSurface.RequestedTheme = theme;
        if (desktopSource.NeedsAnimation?.Invoke() == true) desktopSource.WakeRendering();
        GlassProfile.Appearance(GlassProfile.Ms(appearanceStarted), layoutMs, 0, 0,
            textTarget, glassTarget, statsDark, settings.AutomaticAppearance);
    }

    void UpdateAnalysisRegions()
    {
        if (!analysisRegionsDirty) return;
        if (caption.ActualWidth <= 0 || countdown.ActualWidth <= 0) return;
        var a = caption.TransformToVisual(surface).TransformPoint(new Point(0, 0));
        var b = countdown.TransformToVisual(surface).TransformPoint(new Point(0, 0));
        double dpi = GetDpiForWindow(hwnd) / 96.0;
        double left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        double right = Math.Max(a.X + caption.ActualWidth, b.X + countdown.ActualWidth);
        double bottom = Math.Max(a.Y + caption.ActualHeight, b.Y + countdown.ActualHeight);
        int textX = (int)Math.Floor(left * dpi), textY = (int)Math.Floor(top * dpi);
        int textW = (int)Math.Ceiling((right - left) * dpi), textH = (int)Math.Ceiling((bottom - top) * dpi);
        int statsX = 0, statsY = 0, statsW = 0, statsH = 0;
        if (stats.ActualWidth > 0 && stats.ActualHeight > 0)
        {
            var origin = stats.TransformToVisual(surface).TransformPoint(new Point(0, 0));
            statsX = (int)Math.Round(origin.X * dpi); statsY = (int)Math.Round(origin.Y * dpi);
            statsW = (int)Math.Round(stats.ActualWidth * dpi); statsH = (int)Math.Round(stats.ActualHeight * dpi);
        }
        desktopSource.SetAnalysisRegions(textX, textY, textW, textH, statsX, statsY, statsW, statsH);
        analysisRegionsDirty = false;
    }

    void UpdateGlassTransition()
    {
        if (disposed || !glassAppearanceInitialized) return;
        double progress = glassAppearanceClock.IsRunning && uiSettings.AnimationsEnabled ? Math.Clamp(glassAppearanceClock.Elapsed.TotalMilliseconds / 320, 0, 1) : 1;
        double eased = progress * progress * (3 - 2 * progress);
        glassTone = glassFromTone + (glassTargetTone - glassFromTone) * eased;
        glassTint = glassFromTint + (glassTargetTint - glassFromTint) * eased;
        if (progress >= 1) glassAppearanceClock.Stop();
        const double darkGlassTint = 24;
        double tintRGB = 255 + (darkGlassTint - 255) * glassTone;
        if (Math.Abs(glass.Brush.TintR - tintRGB) > .5) glass.Brush.TintR = glass.Brush.TintG = glass.Brush.TintB = tintRGB;
        if (Math.Abs(glass.Brush.TintA - glassTint) > .001) glass.Brush.TintA = glassTint;
        var fallback = Windows.UI.Color.FromArgb(200, (byte)Math.Round(245 - 205 * glassTone), (byte)Math.Round(245 - 205 * glassTone), (byte)Math.Round(248 - 204 * glassTone));
        if (glassFallback.Color != fallback) glassFallback.Color = fallback;
        glass.UpdateInteraction(uiSettings.AnimationsEnabled);
        if (Math.Abs(statsGlass.GlareAngle - glass.Brush.GlareAngle) > .01) statsGlass.GlareAngle = glass.Brush.GlareAngle;
        double innerGlare = glass.Brush.GlareFactor * .5;
        if (Math.Abs(statsGlass.GlareFactor - innerGlare) > .005) statsGlass.GlareFactor = innerGlare;
    }

    void UpdateTextTransition()
    {
        if (disposed || !textAppearanceInitialized) return;
        double progress = textAppearanceClock.IsRunning && uiSettings.AnimationsEnabled ? Math.Clamp(textAppearanceClock.Elapsed.TotalMilliseconds / 320, 0, 1) : 1;
        double eased = progress * progress * (3 - 2 * progress);
        lightness = textFromTone + (textTargetTone - textFromTone) * eased;
        if (progress >= 1) textAppearanceClock.Stop();
        byte channel = (byte)Math.Round(20 + 235 * lightness);
        var color = Windows.UI.Color.FromArgb(255, channel, channel, channel);
        if (foreground.Color != color) foreground.Color = color;
        var ringColor = Windows.UI.Color.FromArgb(255, (byte)Math.Round(154 + 46 * lightness), (byte)Math.Round(126 + 55 * lightness), (byte)Math.Round(67 + 67 * lightness));
        if (coinScoreRingAccent.Color != ringColor) coinScoreRingAccent.Color = ringColor;
    }

    void UpdateStatsTransition()
    {
        if (disposed || !statsAppearanceInitialized) return;
        double progress = statsAppearanceClock.IsRunning && uiSettings.AnimationsEnabled ? Math.Clamp(statsAppearanceClock.Elapsed.TotalMilliseconds / 320, 0, 1) : 1;
        double eased = progress * progress * (3 - 2 * progress);
        statsTone = statsFromTone + (statsTargetTone - statsFromTone) * eased;
        if (progress >= 1) statsAppearanceClock.Stop();
        byte Mix(double light, double dark) => (byte)Math.Round(light + (dark - light) * statsTone);
        // Card tint stays fixed by mode; its ink uses the same adaptive tone as
        // the outer heading.
        byte inkTone = (byte)Math.Round(20 + 235 * lightness);
        var ink = Windows.UI.Color.FromArgb(255, inkTone, inkTone, inkTone);
        if (statsForeground.Color != ink) statsForeground.Color = ink;
        // The eye sits on the stats card, so derive its chip tone from the card
        // (light card -> dark chip, black card -> light chip), not the outer ink.
        byte eyeTone = (byte)Math.Round(20 + 235 * statsTone);
        if (eyeGlass.GradientStops.Count == 0)
        {
            foreach (var brush in new[] { eyeGlass, eyeHoverGlass, eyePressedGlass })
            {
                brush.Center = new Windows.Foundation.Point(.5, .5);
                brush.GradientOrigin = new Windows.Foundation.Point(.5, .5);
                brush.RadiusX = .5;
                brush.RadiusY = .5;
                foreach (double offset in new[] { 0d, .45, .7, .88, 1d })
                    brush.GradientStops.Add(new GradientStop { Offset = offset });
            }
        }
        // Long feathered fade with no rim, matching the settings arrows.
        void SetEyeChip(RadialGradientBrush brush, byte a0, byte a1, byte a2, byte a3)
        {
            brush.GradientStops[0].Color = Windows.UI.Color.FromArgb(a0, eyeTone, eyeTone, eyeTone);
            brush.GradientStops[1].Color = Windows.UI.Color.FromArgb(a1, eyeTone, eyeTone, eyeTone);
            brush.GradientStops[2].Color = Windows.UI.Color.FromArgb(a2, eyeTone, eyeTone, eyeTone);
            brush.GradientStops[3].Color = Windows.UI.Color.FromArgb(a3, eyeTone, eyeTone, eyeTone);
            brush.GradientStops[4].Color = Windows.UI.Color.FromArgb(0, eyeTone, eyeTone, eyeTone);
        }
        SetEyeChip(eyeGlass, 20, 18, 13, 6);
        SetEyeChip(eyeHoverGlass, 34, 30, 21, 10);
        SetEyeChip(eyePressedGlass, 46, 41, 29, 14);
        double tintStrength = 1 - .75 * (1 - statsTone) * lightness;
        var tint = Windows.UI.Color.FromArgb((byte)Math.Round(210 * tintStrength), Mix(245, 40), Mix(245, 40), Mix(248, 44));
        if (statsBackground.Color != tint) statsBackground.Color = tint;
        if (statsGlass.FallbackColor != tint) statsGlass.FallbackColor = tint;
        if (Math.Abs(statsGlass.TintR - tint.R) > .5) { statsGlass.TintR = tint.R; statsGlass.TintG = tint.G; statsGlass.TintB = tint.B; }
        double alpha = (.42 - .06 * statsTone) * tintStrength;
        if (Math.Abs(statsGlass.TintA - alpha) > .001) statsGlass.TintA = alpha;
    }

    void PopEyeButton()
    {
        if (disposed || !uiSettings.AnimationsEnabled) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(earningsEye);
        visual.CenterPoint = new System.Numerics.Vector3((float)earningsEye.ActualWidth / 2, (float)earningsEye.ActualHeight / 2, 0);
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.DampingRatio = .32f;
        spring.Period = TimeSpan.FromMilliseconds(220);
        spring.InitialValue = new System.Numerics.Vector3(1.18f, 1.18f, 1);
        spring.FinalValue = new System.Numerics.Vector3(1, 1, 1);
        visual.StartAnimation("Scale", spring);
    }

    async Task<bool> RevealSalary(XamlRoot root)
    {
        bool unlocked = await PrivacyPrompts.UnlockAsync(root, privacy, settings.AutomaticAppearance ? surface.ActualTheme : settings.NightMode ? ElementTheme.Dark : ElementTheme.Light);
        if (exiting) return false;
        decimal storedSalary = privacy.LoadSalary();
        if (settings.Salary != storedSalary) { settings.Salary = storedSalary; HideSalary(); }
        if (!unlocked) return false;
        settings.HideAmount = false; settings.Salary = privacy.LoadSalary(); revealedUntil = settings.AmountVisibleSeconds == 0 ? DateTime.MaxValue : DateTime.Now.AddSeconds(settings.AmountVisibleSeconds); Refresh(); return true;
    }

    void ToggleSalaryLayout()
    {
        if (exiting || disposed) return;
        if (isDragging) EndDrag(null);
        if (scalePreviewHostActive) { EndScalePreviewHost(); ApplyWidgetSize(); }
        layoutStartProgress = layoutTransitionActive ? layoutProgress : settings.ShowSalaryLayout ? 1 : 0;
        layoutStartWidth = layoutHostActive ? layoutCurrentWidth : native.Size.Width;
        layoutStartX = layoutHostActive ? layoutCurrentX : native.Position.X;
        settings.ShowSalaryLayout = !settings.ShowSalaryLayout;
        if (!settings.ShowSalaryLayout) settings.HideAmount = true;
        shownWidgetWidth = 368;
        layoutTransitionActive = uiSettings.AnimationsEnabled;
        layoutProgress = layoutStartProgress;
        layoutFrameCount = 0; layoutLastFrameAt = 0;
        // Refresh calculates the target geometry without resizing during the transition.
        Refresh();
        if (GlassProfile.Enabled)
            GlassProfile.Event($"layout-toggle show={settings.ShowSalaryLayout} startProgress={layoutStartProgress:F3} startWidth={layoutStartWidth} target={layoutTargetWidth}");
        if (layoutTransitionActive)
        {
            var area = DisplayArea.GetFromWindowId(native.Id, DisplayAreaFallback.Primary).WorkArea;
            layoutTargetX = Math.Clamp(layoutStartX, area.X, Math.Max(area.X, area.X + area.Width - layoutTargetWidth));
            settings.X = layoutTargetX;
            try
            {
                BeginLayoutHost();
                layoutTransitionClock.Restart(); UpdateLayoutTransition();
                CompositionTarget.Rendering -= AnimateLayoutTransition;
                if (layoutTransitionActive) CompositionTarget.Rendering += AnimateLayoutTransition;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                layoutTransitionActive = false; layoutTransitionClock.Stop(); CompositionTarget.Rendering -= AnimateLayoutTransition;
                if (!statsWishMode) salaryArea.Opacity = 1;
                FinishLayoutHost(); Refresh();
            }
        }
        Save();
    }

    void RunSmokeLayoutCycle()
    {
        int ticks = 0;
        GlassProfile.Event("smoke-layout-start");
        cycleTimer = DispatcherQueue.CreateTimer();
        cycleTimer.Interval = TimeSpan.FromMilliseconds(1000);
        cycleTimer.Tick += (_, __) =>
        {
            if (disposed || exiting || ++ticks > 20) { cycleTimer.Stop(); GlassProfile.Event("smoke-layout-end"); return; }
            ToggleSalaryLayout();
        };
        cycleTimer.Start();
    }

    void RunSmokeScaleCycle()
    {
        int step = 0;
        GlassProfile.Event("smoke-scale-start");
        cycleTimer = DispatcherQueue.CreateTimer();
        cycleTimer.Interval = TimeSpan.FromMilliseconds(smokeScaleFast ? 5 : 33);
        cycleTimer.Tick += (_, __) =>
        {
            if (disposed || exiting || ++step > 240) { cycleTimer.Stop(); GlassProfile.Event("smoke-scale-end"); return; }
            PreviewScale(1.125 + 0.375 * Math.Sin(step * Math.PI * 4 / 240.0));
        };
        cycleTimer.Start();
    }

    void AnimateLayoutTransition(object? sender, object args)
    {
        try { UpdateLayoutTransition(); }
        catch (Exception ex)
        {
            App.RecordFailure(ex);
            if (!disposed) CompleteLayoutTransition();
        }
        if (!layoutTransitionActive || disposed) CompositionTarget.Rendering -= AnimateLayoutTransition;
    }

    void UpdateLayoutTransition()
    {
        if (!layoutTransitionActive || disposed) return;
        long frameStarted = GlassProfile.Timestamp();
        double time = uiSettings.AnimationsEnabled ? Math.Clamp(layoutTransitionClock.Elapsed.TotalMilliseconds / 260, 0, 1) : 1;
        double eased = time * time * (3 - 2 * time);
        layoutProgress = layoutStartProgress + ((settings.ShowSalaryLayout ? 1 : 0) - layoutStartProgress) * eased;
        statsRow.ColumnDefinitions[2].Width = layoutProgress > 0 ? new GridLength(layoutProgress, GridUnitType.Star) : new GridLength(0);
        if (!statsWishMode) salaryArea.Opacity = layoutProgress * layoutProgress;
        int width = (int)Math.Round(layoutStartWidth + (layoutTargetWidth - layoutStartWidth) * eased);
        int x = (int)Math.Round(layoutStartX + (layoutTargetX - layoutStartX) * eased);
        layoutCurrentWidth = width; layoutCurrentX = x;
        double dpi = GetDpiForWindow(hwnd) / 96.0;
        int renderWidth = Math.Max(1, width - layoutInsetWidth);
        int renderHeight = Math.Max(1, layoutTargetHeight - layoutInsetHeight);
        bool sizeChanged = surface.Width != renderWidth / dpi || surface.Height != renderHeight / dpi;
        surface.Width = renderWidth / dpi; surface.Height = renderHeight / dpi;
        card.Width = surface.Width; card.Height = surface.Height;
        UpdateCompositionClip();
        layoutTranslation.X = (x + layoutInsetX - layoutRenderHostX) / dpi;
        layoutTranslation.Y = (native.Position.Y + layoutInsetY - layoutRenderHostY) / dpi;
        // Resolve XAML sizing (including the glass brush's size-dependent geometry)
        // before widening the native outline into the newly visible pixels.
        if (sizeChanged) surface.UpdateLayout();
        desktopSource.UpdateVisualBounds(x + layoutInsetX, native.Position.Y + layoutInsetY, renderWidth, renderHeight);
        bool covered = desktopSource.AtlasCovers(x + layoutInsetX, native.Position.Y + layoutInsetY, renderWidth, renderHeight);
        bool published = desktopSource.SynchronizeVisualFrame();
        // Keep the initial native outline through two render submissions, matching
        // scale preview's reveal guard; UpdateLayout alone has not presented XAML.
        if (layoutFrameCount == 2) UpdateLayoutHostClip(layoutHostX, layoutHostWidth);
        layoutFrameCount++;
        if (GlassProfile.Enabled)
        {
            double interval = layoutLastFrameAt == 0 ? 0 : GlassProfile.Ms(layoutLastFrameAt);
            layoutLastFrameAt = GlassProfile.Timestamp();
            GlassProfile.Event($"layout-frame p={layoutProgress:F3} ui={GlassProfile.Ms(frameStarted):F2} int={interval:F2} w={width} h={renderHeight} target={layoutTargetWidth} covered={(covered ? 1 : 0)} published={(published ? 1 : 0)}");
        }
        // Keep the native region stable; the composed clip owns the animated edge.
        if (time >= 1) CompleteLayoutTransition();
    }

    void BeginLayoutHost()
    {
        EnsureRenderEraseMonitor();
        if (!layoutHostActive)
        {
            if (!desktopSource.TryGetHostRenderBounds(out int x, out int y, out int width, out int height))
                throw new InvalidOperationException("无法读取布局视口。");
            layoutInsetX = x - native.Position.X; layoutInsetY = y - native.Position.Y;
            layoutInsetWidth = native.Size.Width - width; layoutInsetHeight = native.Size.Height - height;
        }
        layoutHostActive = true;
        layoutCurrentX = layoutStartX; layoutCurrentWidth = layoutStartWidth;
        layoutHostX = Math.Min(layoutStartX, layoutTargetX);
        layoutHostWidth = Math.Max(layoutStartX + layoutStartWidth, layoutTargetX + layoutTargetWidth) - layoutHostX;
        // Initialize the maximum native backing once; the composed clip animates inside it.
        layoutClipLeft = int.MinValue; layoutClipWidth = -1;
        UpdateLayoutHostClip(layoutHostX, layoutHostWidth);
        native.MoveAndResize(new RectInt32(layoutHostX, native.Position.Y, layoutHostWidth, layoutTargetHeight));
        ClearTransparentHostBackground();
        if (!desktopSource.TryGetHostRenderBounds(out layoutRenderHostX, out layoutRenderHostY, out _, out _))
            throw new InvalidOperationException("无法读取布局宿主。");
        surface.RenderTransform = layoutTranslation;
        desktopSource.BeginVisualDrag(layoutStartX + layoutInsetX, native.Position.Y + layoutInsetY,
            Math.Max(1, layoutStartWidth - layoutInsetWidth), Math.Max(1, layoutTargetHeight - layoutInsetHeight));
        // Keep the exposed spare area clipped to the card until XAML has presented
        // the first animated frame; otherwise stale white backing shows at the
        // right rounded corners on the first expansion after launch.
        UpdateLayoutHostClip(layoutStartX, layoutStartWidth);
        if (GlassProfile.Enabled)
            GlassProfile.Event($"layout-host x={layoutHostX} w={layoutHostWidth} start={layoutStartX}:{layoutStartWidth} target={layoutTargetX}:{layoutTargetWidth} inset={layoutInsetX},{layoutInsetY}:{layoutInsetWidth},{layoutInsetHeight}");
        // WinUI installs its caption handler after activation; keep our spare-area hit test first.
        sessionMonitor?.Dispose();
        sessionMonitor = new WinUIEx.Messaging.WindowMessageMonitor(this);
        sessionMonitor.WindowMessageReceived += OnSessionChanged;
        sessionMonitor.WindowMessageReceived += OnLayoutHitTest;
        sessionMonitor.WindowMessageReceived += OnEraseBackground;
        sessionMonitor.WindowMessageReceived += OnKeyMessage;
    }

    void UpdateLayoutHostClip(int x, int width)
    {
        int left = x + layoutInsetX - layoutHostX;
        int renderWidth = Math.Max(1, width - layoutInsetWidth);
        if (left == layoutClipLeft && renderWidth == layoutClipWidth) return;
        int top = layoutInsetY;
        int renderHeight = Math.Max(1, layoutTargetHeight - layoutInsetHeight);
        int diameter = (int)Math.Round(72 * pixelScale);
        const int guard = 4;
        SetProp(hwnd, "OffWorkWidget.GlassDiameterPx", new IntPtr(diameter));
        SetProp(hwnd, "OffWorkWidget.GlassGuardPx", new IntPtr(guard));
        IntPtr region = CreateContentRegion(left, top, left + renderWidth, top + renderHeight, diameter, guard);
        if (region == IntPtr.Zero) return;
        if (SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
        else
        {
            surfaceClipBounds = null;
            layoutClipLeft = left; layoutClipWidth = renderWidth;
            // Initialize the visible backing, including the XAML child that the root DC may exclude.
            ClearTransparentHostBackground();
        }
    }

    void FinishLayoutHost()
    {
        if (!layoutHostActive) return;
        // The whole maximum rectangle was initialized before animation; this only shrinks it.
        native.MoveAndResize(new RectInt32(layoutTargetX, native.Position.Y, layoutTargetWidth, layoutTargetHeight));
        desktopSource.EndVisualDrag();
        surface.RenderTransform = null;
        layoutHostActive = false;
        UpdateRenderViewport();
    }

    void CompleteLayoutTransition()
    {
        if (GlassProfile.Enabled)
            GlassProfile.Event($"layout-complete frames={layoutFrameCount} elapsed={layoutTransitionClock.Elapsed.TotalMilliseconds:F1} progress={layoutProgress:F3}");
        layoutTransitionActive = false; layoutTransitionClock.Stop(); CompositionTarget.Rendering -= AnimateLayoutTransition;
        if (!statsWishMode) salaryArea.Opacity = 1;
        FinishLayoutHost();
        Refresh();
    }

    void HideSalary() { if (exiting || disposed) return; settings.HideAmount = true; Refresh(); }

    static IntPtr CreateContentRegion(int left, int top, int right, int bottom, int diameter, int guard)
    {
        IntPtr rounded = CreateRoundRectRgn(left - guard, top - guard, right + guard, bottom + guard,
            diameter + guard * 2, diameter + guard * 2);
        if (rounded == IntPtr.Zero) return IntPtr.Zero;
        // Keep the rounded AA guard inside the real XAML viewport. The parent
        // HWND has a spare top row that XAML never paints; exposing it shows a
        // white native edge even when the entire XAML tree is transparent.
        IntPtr content = CreateRectRgn(left, top, right, bottom);
        if (content == IntPtr.Zero) { DeleteObject(rounded); return IntPtr.Zero; }
        try
        {
            if (CombineRgn(rounded, rounded, content, 1 /* RGN_AND */) == 0)
            { DeleteObject(rounded); return IntPtr.Zero; }
            return rounded;
        }
        finally { DeleteObject(content); }
    }

    void TryRegisterSessionNotifications()
    {
        if (disposed) return;
        nextSessionRegistrationAttempt = elapsed.Elapsed.TotalSeconds + 5;
        sessionNotificationRegistered = WTSRegisterSessionNotification(hwnd, 0);
    }

    void OnLayoutHitTest(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
    {
        if ((!layoutHostActive && !scalePreviewHostActive) || args.Message.MessageId != 0x0084 /* WM_NCHITTEST */) return;
        long position = args.Message.LParam.ToInt64();
        double x = (short)(position & 0xffff) - (scalePreviewHostActive ? native.Position.X + scalePreviewInsetX : layoutCurrentX + layoutInsetX);
        double y = (short)((position >> 16) & 0xffff) - native.Position.Y - (scalePreviewHostActive ? scalePreviewInsetY : layoutInsetY);
        double width = Math.Max(1, scalePreviewHostActive ? scalePreviewWidth - scalePreviewInsetWidth : layoutCurrentWidth - layoutInsetWidth);
        double height = Math.Max(1, scalePreviewHostActive ? scalePreviewHeight - scalePreviewInsetHeight : layoutTargetHeight - layoutInsetHeight);
        double radius = Math.Min(36 * pixelScale, Math.Min(width, height) * .5);
        double dx = x - Math.Clamp(x, radius, width - radius);
        double dy = y - Math.Clamp(y, radius, height - radius);
        if (x < 0 || x >= width || y < 0 || y >= height || dx * dx + dy * dy > radius * radius)
        {
            args.Result = new IntPtr(-1); // HTTRANSPARENT: spare animation host is not interactive.
            args.Handled = true;
        }
    }

    void OnSessionChanged(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
    {
        if (args.Message.MessageId != 0x02B1) return;
        long change = unchecked((long)args.Message.WParam);
        Diag("session-change " + change);
        if (change == 7)
            DispatcherQueue.TryEnqueue(() => { EndDrag(null); HideSalary(); });
    }

    void Diag(string note) => App.RecordEvent("widget " + note);

    void OnKeyMessage(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
    {
        if (disposed || exiting || isDragging || !IsWindowVisible(hwnd)) return;
        if (args.Message.MessageId != 0x0100 /* WM_KEYDOWN */) return;
        if (unchecked((int)args.Message.WParam) != 0x0D /* VK_RETURN */) return;
        // Ignore keyboard auto-repeat; one coin burst per physical press.
        if ((args.Message.LParam.ToInt64() & (1L << 30)) != 0) return;
        args.Handled = true;
        if (petEnterHandled) return;
        petEnterHeld = petEnterHandled = true;
        EmitCoin();
        GlassProfile.Event("coin-key enter");
    }

    IntPtr OnPetKeyboardHook(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && Marshal.ReadInt32(data) == 0x0D /* VK_RETURN */)
            {
                int kind = unchecked((int)message.ToInt64());
                if (kind == 0x0101 || kind == 0x0105) petEnterHeld = petEnterHandled = false;
                else if ((kind == 0x0100 || kind == 0x0104) && !petEnterHeld)
                {
                    petEnterHeld = true;
                    if (IsPointerOnPet())
                    {
                        petEnterHandled = true;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!disposed && !exiting) { EmitCoin(); GlassProfile.Event("coin-key hover-enter"); }
                        });
                    }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine("Pet keyboard hook: " + ex.Message); }
        return CallNextHookEx(petEnterHook, code, message, data);
    }

    bool IsPointerOnPet()
    {
        if (disposed || exiting || isDragging || startupMaskActive || !IsWindowVisible(hwnd) || !IsCursorOverWidget()) return false;
        var animation = napActive && napAnimation != null ? napAnimation :
            waterPetOverlay.Visibility == Visibility.Visible ? petAnimation : reminderAnimation ?? (coinPetActive ? coinPetAnimation : petAnimation);
        if (animation == null || !TryGetHoverPoint(out Point point)) return false;
        var inverse = pet.TransformToVisual(hostSurface).Inverse;
        if (inverse == null) return false;
        Point local = inverse.TransformPoint(point);
        return animation.HitTest(local.X, local.Y) || (coinPetActive && petAnimation != null && petAnimation.HitTest(local.X, local.Y));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int hook, KeyboardHookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool WTSUnRegisterSessionNotification(IntPtr window);

    void OpenSettings()
    {
        if (exiting || disposed) return;
        if (settingsWindow != null) { settingsWindow.BringToFront(); return; }
        settingsWindow = new SettingsWindow(settings, privacy, ToggleSalaryLayout, HideSalary, next => { privacy.SaveSalary(next.Salary); settings = next; previewScale = null; ApplySettings(); Save(true); }, PreviewScale);
        settingsWindow.Closed += (_, __) =>
        {
            settingsWindow = null;
            if (previewScale.HasValue && !disposed && !exiting) { previewScale = null; ApplyWidgetSize(); Refresh(); }
        };
        settingsWindow.BringToFront();
    }

    void PositionCoinLayer()
    {
        double scale = PhysicalScale;
        var origin = surface.TransformToVisual(hostSurface).TransformPoint(new Point(0, 0));
        coinOverlay.Width = surface.Width;
        coinOverlay.Height = surface.Height + CoinHeadroom * scale;
        coins.Width = surface.Width / scale;
        coins.Height = surface.Height / scale + CoinHeadroom;
        coinPopup.HorizontalOffset = origin.X;
        coinPopup.VerticalOffset = origin.Y - CoinHeadroom * scale;
    }

    void TrackCoinLayer(object? sender, object args)
    {
        if (disposed || !IsWindowVisible(hwnd) || IsIconic(hwnd)) { CloseCoinLayer(); return; }
        MakeCoinPopupClickThrough();
        PositionCoinLayer();
    }

    IntPtr coinPopupWindow;

    void MakeCoinPopupClickThrough()
    {
        // The overlay popup owns the cursor while coins are in the air and pins it
        // to the arrow; make it transparent and answer its WM_SETCURSOR.
        if (coinPopupWindow != IntPtr.Zero && IsWindow(coinPopupWindow) && IsWindowVisible(coinPopupWindow)) return;
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((candidate, _) =>
            {
                uint pid;
                GetWindowThreadProcessId(candidate, out pid);
                if (pid != (uint)Environment.ProcessId || !IsWindowVisible(candidate)) return true;
                var name = new StringBuilder(64);
                GetClassName(candidate, name, name.Capacity);
                if (name.ToString() != "Microsoft.UI.Content.PopupWindowSiteBridge") return true;
                found = candidate;
                return false;
            }, IntPtr.Zero);
            coinPopupWindow = found;
        }
        if (coinPopupWindow == IntPtr.Zero) return;
        long style = GetWindowLongPtr(coinPopupWindow, -20 /* GWL_EXSTYLE */).ToInt64();
        long clickThrough = style | 0x00000020 /* WS_EX_TRANSPARENT */ | 0x08000000 /* WS_EX_NOACTIVATE */ | 0x00000080 /* WS_EX_TOOLWINDOW */;
        if (clickThrough != style) SetWindowLongPtr(coinPopupWindow, -20, new IntPtr(clickThrough));
        popupSubclass ??= (h, msg, wParam, lParam, id, data) =>
        {
            if (msg == 0x0084 /* WM_NCHITTEST */) return new IntPtr(-1); // HTTRANSPARENT
            if (msg == 0x0020 /* WM_SETCURSOR */ && (unchecked((long)lParam) & 0xFFFF) == 1 &&
                TryGetHoverPoint(out Point point) && IsCursorOverHandArea(point))
            {
                SetCursor(handCursor);
                return new IntPtr(1);
            }
            return DefSubclassProcCursor(h, msg, wParam, lParam);
        };
        SetWindowSubclass(coinPopupWindow, popupSubclass, (UIntPtr)1, UIntPtr.Zero);
    }

    SUBCLASSPROC? popupSubclass;

    void CloseCoinLayer()
    {
        CompositionTarget.Rendering -= TrackCoinLayer;
        trackingCoins = false;
        coinPopup.IsOpen = false;
        foreach (var story in coinStories.ToArray()) story.Stop();
        coinStories.Clear();
        activeCoinUpgrade = null;
        coins.Children.Clear();
        if (popupSettleTimer == null)
        {
            popupSettleTimer = DispatcherQueue.CreateTimer();
            popupSettleTimer.Interval = TimeSpan.FromMilliseconds(150);
            popupSettleTimer.IsRepeating = false;
            popupSettleTimer.Tick += (_, __) => NudgeForCursor();
        }
        popupSettleTimer.Start();
    }

    DispatcherQueueTimer? popupSettleTimer, nudgeReturnTimer;
    int nudgeX, nudgeY;

    void NudgeForCursor()
    {
        if (disposed || exiting || !IsWindowVisible(hwnd)) return;
        if (!IsCursorOverWidget() || !TryGetHoverPoint(out Point point) || !IsCursorOverHandArea(point)) return;
        if (!GetCursorPos(out Win32Point cursor)) return;
        nudgeX = cursor.X; nudgeY = cursor.Y;
        MoveCursorAbsolute(nudgeX + 1, nudgeY);
        if (nudgeReturnTimer == null)
        {
            nudgeReturnTimer = DispatcherQueue.CreateTimer();
            nudgeReturnTimer.Interval = TimeSpan.FromMilliseconds(60);
            nudgeReturnTimer.IsRepeating = false;
            nudgeReturnTimer.Tick += (_, __) => MoveCursorAbsolute(nudgeX, nudgeY);
        }
        nudgeReturnTimer.Start();
    }

    static void MoveCursorAbsolute(int x, int y)
    {
        int width = Math.Max(1, GetSystemMetrics(0)), height = Math.Max(1, GetSystemMetrics(1));
        mouse_event(0x8001 /* MOVE | ABSOLUTE */,
            (int)Math.Round(x * 65535.0 / Math.Max(1, width - 1)),
            (int)Math.Round(y * 65535.0 / Math.Max(1, height - 1)), 0, IntPtr.Zero);
    }

    void ClearAmbientShopEffects()
    {
        StopFishEffect();
        foreach (var story in ambientEffectStories) story.Stop();
        ambientEffectStories.Clear();
        ambientEffects.Children.Clear();
        ambientParticlesActive = 0;
    }

    void QueueAmbientPreview()
    {
        if (!ambientPreviewPending) nextAmbientEffectAt = elapsed.ElapsedMilliseconds + 150;
        ambientPreviewPending = true;
    }

    bool UsesDayEffectPalette()
    {
        if (!settings.AutomaticAppearance) return !settings.NightMode;
        return glassAppearanceInitialized ? glassTargetTone < .5 :
            uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R >= 128;
    }


    void ShowAmbientShopEffect()
    {
        if (disposed || exiting || !uiSettings.AnimationsEnabled || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return;
        int selectedEffect = privacy.SelectedEffect;
        bool preview = ambientPreviewPending && ambientEffectKind == selectedEffect;
        ambientPreviewPending = false;
        if (ambientEffectKind != selectedEffect || preview)
        {
            ClearAmbientShopEffects();
            ambientEffectKind = selectedEffect;
        }
        if (selectedEffect == 0 || !privacy.EffectUnlocked(selectedEffect)) return;
        var parameters = privacy.GetEffectParameters(selectedEffect);
        int limit = parameters.Count;
        int cadence = selectedEffect == 1 ? 320 : selectedEffect is 10 or 11 ? 420 : 850;
        nextAmbientEffectAt = elapsed.ElapsedMilliseconds + (long)(cadence / parameters.Speed);
        double cardWidth = surface.Width / PhysicalScale;
        double cardHeight = surface.Height / PhysicalScale;
        if (cardWidth < 32 || cardHeight < 32 || surface.XamlRoot == null) return;
        if (selectedEffect == FishShopEffect)
        {
            StartFishEffect(parameters, cardWidth, cardHeight);
            return;
        }
        int batchTarget = (limit + 2) / 3;
        int particleCount = Math.Min(batchTarget, Math.Max(0, limit - ambientParticlesActive));
        if (particleCount == 0) return;
        var storyboard = new Storyboard();
        void Motion(DependencyObject target, string property, params (int At, double Value, EasingMode? Ease)[] keys)
        {
            var motion = new DoubleAnimationUsingKeyFrames();
            foreach (var key in keys)
            {
                var frame = new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(key.At / parameters.Speed)), Value = key.Value };
                if (key.Ease is EasingMode ease) frame.EasingFunction = new QuadraticEase { EasingMode = ease };
                motion.KeyFrames.Add(frame);
            }
            Storyboard.SetTarget(motion, target); Storyboard.SetTargetProperty(motion, property);
            storyboard.Children.Add(motion);
        }
        var effectVisuals = new System.Collections.Generic.List<UIElement>(particleCount);
        bool lightEffectBackground = UsesDayEffectPalette();
        double peakOpacity = (lightEffectBackground ? .95 : .68) * parameters.Opacity / 100;
        double holdOpacity = (lightEffectBackground ? .86 : .62) * parameters.Opacity / 100;
        string daySuffix = lightEffectBackground ? "-day" : "";
        double wind = parameters.Wind * 18;
        Image Sprite(string name, double width, double height, double x, double y, out CompositeTransform transform)
        {
            if (!ambientSprites.TryGetValue(name, out var source))
                ambientSprites[name] = source = new BitmapImage(new Uri("ms-appx:///assets/shop-effects/" + name + ".png"));
            transform = new CompositeTransform();
            var image = new Image { Width = width, Height = height, Stretch = Stretch.Fill, Source = source,
                IsHitTestVisible = false, Opacity = 0, RenderTransformOrigin = new Point(.5, .5), RenderTransform = transform };
            Canvas.SetLeft(image, x); Canvas.SetTop(image, y);
            ambientEffects.Children.Add(image); effectVisuals.Add(image);
            return image;
        }
        int Delay(int index) => index * Math.Min(150, cadence / Math.Max(1, particleCount)) + Random.Shared.Next(15, 70);
        if (selectedEffect == 13)
        {
            // Animate each link from its two moving endpoints. A nearest-neighbour
            // link per node keeps the graph sparse at the maximum particle count.
            var nodes = new (double X, double Y, double Dx, double Dy, int Delay)[particleCount];
            int end = 4600;
            double range = Math.Min(cardWidth, cardHeight) * .38;
            var linkBrush = new SolidColorBrush(lightEffectBackground
                ? Windows.UI.Color.FromArgb(255, 36, 104, 155) : Windows.UI.Color.FromArgb(255, 125, 211, 252));
            for (int i = 0; i < nodes.Length; i++)
            {
                double x = 6 + Random.Shared.NextDouble() * (cardWidth - 12);
                double y = 6 + Random.Shared.NextDouble() * (cardHeight - 12);
                if (i % 4 != 0)
                {
                    var origin = nodes[i - 1];
                    double angle = Random.Shared.NextDouble() * Math.PI * 2;
                    double distance = range * (.36 + Random.Shared.NextDouble() * .55);
                    x = Math.Clamp(origin.X + Math.Cos(angle) * distance, 5, cardWidth - 5);
                    y = Math.Clamp(origin.Y + Math.Sin(angle) * distance, 5, cardHeight - 5);
                }
                int delay = Delay(i);
                double dx = Math.Clamp(wind + Random.Shared.Next(-7, 8), 4 - x, cardWidth - 4 - x);
                double dy = Math.Clamp(Random.Shared.Next(-7, 8), 4 - y, cardHeight - 4 - y);
                nodes[i] = (x, y, dx, dy, delay);
                double size = (8 + Random.Shared.NextDouble() * 4) * parameters.Size;
                var node = Sprite("const-node" + daySuffix, size, size, x - size / 2, y - size / 2, out var glow);
                Motion(glow, "TranslateX", (0, 0, null), (end, dx, null));
                Motion(glow, "TranslateY", (0, 0, null), (end, dy, null));
                Motion(node, "Opacity", (delay, 0, null), (delay + 450, peakOpacity, null),
                    (end / 2, holdOpacity * .65, null), (end - 650, peakOpacity, null), (end, 0, null));
                int nearest = -1;
                double length = range;
                for (int j = 0; j < i; j++)
                {
                    double vx = nodes[j].X - x, vy = nodes[j].Y - y;
                    double distance = Math.Sqrt(vx * vx + vy * vy);
                    if (distance < 12 || distance >= length) continue;
                    length = distance; nearest = j;
                }
                if (nearest < 0) continue;
                var other = nodes[nearest];
                double vx0 = other.X - x, vy0 = other.Y - y;
                double vx1 = vx0 + other.Dx - dx, vy1 = vy0 + other.Dy - dy;
                double angle0 = Math.Atan2(vy0, vx0) * 180 / Math.PI;
                double angle1 = Math.Atan2(vy1, vx1) * 180 / Math.PI;
                angle1 = angle0 + Math.IEEERemainder(angle1 - angle0, 360);
                double endLength = Math.Sqrt(vx1 * vx1 + vy1 * vy1);
                var follow = new CompositeTransform();
                var edge = new Border { Width = length, Height = .85 * parameters.Size, Background = linkBrush,
                    Opacity = 0, IsHitTestVisible = false, RenderTransform = follow, RenderTransformOrigin = new Point(0, .5) };
                Canvas.SetLeft(edge, x); Canvas.SetTop(edge, y - edge.Height / 2);
                ambientEffects.Children.Insert(0, edge); effectVisuals.Add(edge);
                Motion(follow, "TranslateX", (0, 0, null), (end, dx, null));
                Motion(follow, "TranslateY", (0, 0, null), (end, dy, null));
                Motion(follow, "Rotation", (0, angle0, null), (end, angle1, null));
                Motion(follow, "ScaleX", (0, 1, null), (end, endLength / length, null));
                int bothVisible = Math.Max(delay, other.Delay);
                double strength = peakOpacity * .55;
                Motion(edge, "Opacity", (bothVisible, 0, null), (bothVisible + 450, strength * (1 - length / range), null),
                    (end - 650, strength * Math.Max(0, 1 - endLength / range), null), (end, 0, null));
            }
        }
        else if (selectedEffect == 1)
        {
            for (int i = 0; i < particleCount; i++)
            {
                double size = ((lightEffectBackground ? 11 : 10) + Random.Shared.NextDouble() * 3) * parameters.Size;
                var star = CreateShopStar(size, lightEffectBackground);
                var twinkle = new CompositeTransform();
                star.RenderTransformOrigin = new Point(.5, .5); star.RenderTransform = twinkle;
                Canvas.SetLeft(star, 8 + Random.Shared.NextDouble() * Math.Max(0, cardWidth - size - 16));
                Canvas.SetTop(star, 8 + Random.Shared.NextDouble() * Math.Max(0, cardHeight - size - 16));
                ambientEffects.Children.Add(star); effectVisuals.Add(star);
                int delay = Delay(i), end = delay + 850 + Random.Shared.Next(0, 180);
                int second = delay + (end - delay) * 2 / 3;
                Motion(twinkle, "ScaleX", (0, .55, null), (delay, .55, null), (delay + 180, 1, EasingMode.EaseOut), (end, .7, EasingMode.EaseIn));
                Motion(twinkle, "ScaleY", (0, .55, null), (delay, .55, null), (delay + 180, 1, EasingMode.EaseOut), (end, .7, EasingMode.EaseIn));
                if (wind != 0) Motion(twinkle, "TranslateX", (delay, 0, null), (end, wind * .4, null));
                Motion(star, "Opacity", (0, 0, null), (delay, 0, null), (delay + 180, peakOpacity, null),
                    (delay + 400, peakOpacity * .24, null), (second, peakOpacity * .72, null), (end, 0, EasingMode.EaseIn));
            }
        }
        else
        {
            for (int i = 0; i < particleCount; i++)
            {
                bool warm = selectedEffect == 2, leaf = selectedEffect is 5 or 7;
                bool firefly = selectedEffect == 8, aurora = selectedEffect == 9, rain = selectedEffect == 10;
                bool ember = selectedEffect == 12, foil = selectedEffect == 14;
                double kind = Random.Shared.NextDouble();
                if (selectedEffect == 11)
                {
                    double length = (52 + kind * 39) * parameters.Size;
                    double meteorX = cardWidth * (.3 + Random.Shared.NextDouble() * .74);
                    double meteorY = cardHeight * (.02 + Random.Shared.NextDouble() * .4);
                    double fallX = -cardWidth * (.32 + Random.Shared.NextDouble() * .25) + wind;
                    double fallY = cardHeight * (.28 + Random.Shared.NextDouble() * .2);
                    var meteor = Sprite((Random.Shared.NextDouble() < .32 ? "meteor-gold" : "meteor-ice") + daySuffix,
                        length, length / 6, meteorX - length / 12, meteorY - length / 12, out var flight);
                    meteor.RenderTransformOrigin = new Point(1d / 12, .5);
                    flight.Rotation = Math.Atan2(-fallY, -fallX) * 180 / Math.PI;
                    int appear = Delay(i), finish = appear + Random.Shared.Next(700, 1101);
                    Motion(flight, "TranslateX", (appear, 0, null), (finish, fallX, null));
                    Motion(flight, "TranslateY", (appear, 0, null), (finish, fallY, null));
                    Motion(meteor, "Opacity", (appear, 0, null), (appear + 150, peakOpacity, null),
                        (finish - 220, holdOpacity, null), (finish, 0, EasingMode.EaseIn));
                    continue;
                }
                if (selectedEffect == 15)
                {
                    const string glyphs = "01アイウエオカキクケコサシスセソ∑∆Ω79";
                    double fontSize = (8 + kind * 3) * parameters.Size;
                    int chars = Random.Shared.Next(4, 8);
                    var flow = new TranslateTransform();
                    var stream = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = fontSize,
                        IsTextScaleFactorEnabled = false, LineHeight = fontSize * 1.15, Opacity = 0, IsHitTestVisible = false,
                        TextAlignment = TextAlignment.Center, RenderTransform = flow };
                    var green = lightEffectBackground ? Windows.UI.Color.FromArgb(255, 6, 101, 80) : Windows.UI.Color.FromArgb(255, 52, 211, 153);
                    int appear = Delay(i), finish = appear + Random.Shared.Next(1800, 2701);
                    for (int k = 0; k < chars; k++)
                    {
                        if (k > 0) stream.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                        bool head = k == chars - 1;
                        var tint = head ? (lightEffectBackground ? Windows.UI.Color.FromArgb(255, 0, 105, 75) : Windows.UI.Color.FromArgb(255, 236, 253, 245)) :
                            Windows.UI.Color.FromArgb((byte)(190 * (k + 1) / chars), green.R, green.G, green.B);
                        var rune = new Microsoft.UI.Xaml.Documents.Run { Text = glyphs[Random.Shared.Next(glyphs.Length)].ToString(), Foreground = new SolidColorBrush(tint) };
                        stream.Inlines.Add(rune);
                        if (head)
                        {
                            var swap = new ObjectAnimationUsingKeyFrames();
                            for (int tick = appear + 260; tick < finish; tick += 280)
                                swap.KeyFrames.Add(new DiscreteObjectKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(tick / parameters.Speed)),
                                    Value = glyphs[Random.Shared.Next(glyphs.Length)].ToString() });
                            Storyboard.SetTarget(swap, rune); Storyboard.SetTargetProperty(swap, "Text"); storyboard.Children.Add(swap);
                        }
                    }
                    double x = 4 + Random.Shared.NextDouble() * (cardWidth - fontSize - 6);
                    double y = Random.Shared.NextDouble() * cardHeight * .5 - chars * stream.LineHeight;
                    Canvas.SetLeft(stream, x); Canvas.SetTop(stream, y);
                    ambientEffects.Children.Add(stream); effectVisuals.Add(stream);
                    Motion(flow, "X", (appear, 0, null), (finish, wind * .45, null));
                    Motion(flow, "Y", (appear, 0, null), (finish, cardHeight - y, null));
                    Motion(stream, "Opacity", (appear, 0, null), (appear + 200, peakOpacity, null),
                        (finish - 240, holdOpacity, null), (finish, 0, null));
                    continue;
                }
                string sprite = selectedEffect switch
                {
                    2 => kind < .5 ? "warm-gold" : "warm-amber",
                    7 => kind < .5 ? "sakura-pink" : "sakura-pale",
                    8 => kind < .65 ? "firefly-lime" : "firefly-amber",
                    9 => "aurora-star",
                    10 => "rain-drop",
                    12 => kind < .45 ? "ember-glow" : "ember-hot",
                    14 => kind < .24 ? "gold-glint" : "gold-flake",
                    5 => kind < .36 ? "maple-red" : kind < .70 ? "maple-amber" : "ginkgo",
                    _ => (kind < .42 ? "snow-soft" : "snow-crystal") + daySuffix
                };
                if (!leaf && selectedEffect != 3) sprite += daySuffix;
                double size = ((ember ? 9 : foil ? 15 : warm ? 14 : leaf ? 19 : firefly ? 14 : aurora ? 8 : rain ? 16 : kind < .42 ? 8 : 16) + Random.Shared.NextDouble() * 6) * parameters.Size;
                double travel = Math.Min(Math.Max(0, cardHeight - size - 12), (warm ? 22 : 38) + Random.Shared.NextDouble() * 16);
                double startX = 12 + Random.Shared.NextDouble() * Math.Max(0, cardWidth - size - 24);
                double startY = 6 + Random.Shared.NextDouble() * Math.Max(0, cardHeight - size - travel - 12) + (warm ? travel : 0);
                if (ember)
                {
                    travel = cardHeight * (.45 + Random.Shared.NextDouble() * .32);
                    startY = cardHeight - size - Random.Shared.NextDouble() * cardHeight * .18;
                }
                int delay = Delay(i);
                if (rain)
                {
                    double landingY = Math.Max(size, cardHeight - 10 - Random.Shared.NextDouble() * 8);
                    double distance = Math.Max(1, landingY - startY - size * .8);
                    double shift = Math.Clamp(distance * parameters.Wind * .22, 4 - startX, Math.Max(4 - startX, cardWidth - size - startX - 4));
                    var drop = Sprite(sprite, size, size, startX, startY, out var falling);
                    falling.Rotation = -11 - Math.Atan2(shift, distance) * 180 / Math.PI;
                    int impact = delay + 320 + (int)(distance * 4);
                    Motion(falling, "TranslateX", (delay, 0, null), (impact, shift, null));
                    Motion(falling, "TranslateY", (delay, 0, null), (impact, distance, null));
                    Motion(drop, "Opacity", (delay, 0, null), (delay + 100, peakOpacity, null), (impact - 1, peakOpacity, null), (impact, 0, null));
                    double width = size * 1.35;
                    var ripple = Sprite("rain-ripple" + daySuffix, width, width, startX + shift + (size - width) / 2, landingY - width / 2, out var spread);
                    Motion(spread, "ScaleX", (impact, .15, null), (impact + 600, 1, EasingMode.EaseOut));
                    Motion(spread, "ScaleY", (impact, .15, null), (impact + 600, 1, EasingMode.EaseOut));
                    Motion(ripple, "Opacity", (impact, 0, null), (impact + 75, holdOpacity, null), (impact + 600, 0, null));
                    continue;
                }
                int duration = Random.Shared.Next(3200, 4601), end = delay + duration, mid = delay + duration / 2;
                if (ember && i == 0 && !ambientEffects.Children.OfType<Border>().Any(element => element.Name == "EmberHearth"))
                {
                    var gradient = new LinearGradientBrush { StartPoint = new Point(.5, 1), EndPoint = new Point(.5, 0) };
                    gradient.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(lightEffectBackground ? (byte)40 : (byte)60, 234, 88, 12) });
                    gradient.GradientStops.Add(new GradientStop { Offset = .45, Color = Windows.UI.Color.FromArgb(20, 245, 158, 11) });
                    gradient.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, 245, 158, 11) });
                    var hearth = new Border { Name = "EmberHearth", Width = cardWidth, Height = cardHeight * .55, Background = gradient,
                        Opacity = 0, IsHitTestVisible = false };
                    Canvas.SetTop(hearth, cardHeight - hearth.Height);
                    ambientEffects.Children.Insert(0, hearth); effectVisuals.Add(hearth);
                    Motion(hearth, "Opacity", (0, 0, null), (450, peakOpacity, null), (end - 500, holdOpacity, null), (end, 0, null));
                }
                if (aurora && i == 0 && !ambientEffects.Children.OfType<Image>().Any(image => image.Name == "AuroraCurtain"))
                {
                    var wave = Sprite(lightEffectBackground ? "aurora-wave-day" : "aurora-wave-night", cardWidth * .9, cardHeight * .65, cardWidth * .05, cardHeight * .08, out var curtain);
                    wave.Name = "AuroraCurtain";
                    Motion(curtain, "TranslateX", (0, -4, null), (end, 4 + wind * .2, EasingMode.EaseInOut));
                    Motion(curtain, "ScaleY", (0, .75, null), (mid, 1, EasingMode.EaseInOut), (end, .88, EasingMode.EaseInOut));
                    Motion(wave, "Opacity", (0, 0, null), (delay + 600, parameters.Opacity / 100d, null), (end - 800, parameters.Opacity / 100d, null), (end, 0, null));
                }
                var particle = Sprite(sprite, size, size, startX, startY, out var particleTransform);
                double drift = (Random.Shared.Next(2) == 0 ? -1 : 1) * (5 + Random.Shared.NextDouble() * 6);
                Motion(particleTransform, "TranslateX", (delay, 0, null), (mid, drift + wind * .5, EasingMode.EaseInOut), (end, -drift * .45 + wind, EasingMode.EaseInOut));
                if (firefly)
                    Motion(particleTransform, "TranslateY", (delay, 0, null), (mid, drift, EasingMode.EaseInOut), (end, -drift * .7, EasingMode.EaseInOut));
                else
                    Motion(particleTransform, "TranslateY", (delay, 0, null), (end, (warm || ember) ? -travel : aurora ? 3 : travel, null));
                if (!warm && !ember && !firefly && !aurora)
                {
                    double angle = Random.Shared.Next(-55, 56);
                    Motion(particleTransform, "Rotation", (delay, angle, null), (end, angle + Math.Sign(drift) * (foil ? 170 : leaf ? 115 : 45), null));
                    if (leaf || foil) Motion(particleTransform, "ScaleX", (delay, 1, null), (mid, foil ? .18 : .34, EasingMode.EaseInOut), (end, .94, EasingMode.EaseInOut));
                }
                Motion(particle, "Opacity", (0, 0, null), (delay, 0, null), (delay + 440, peakOpacity, null),
                    (mid, firefly || ember ? holdOpacity * .12 : warm || aurora || foil ? holdOpacity * .55 : holdOpacity, EasingMode.EaseInOut),
                    (end - 620, holdOpacity, EasingMode.EaseInOut), (end, 0, EasingMode.EaseInOut));
            }
        }
        ambientParticlesActive += particleCount;
        storyboard.Completed += (_, __) =>
        {
            if (!ambientEffectStories.Remove(storyboard)) return;
            foreach (var visual in effectVisuals) ambientEffects.Children.Remove(visual);
            ambientParticlesActive -= particleCount;
        };
        ambientEffectStories.Add(storyboard);
        storyboard.Begin();
        if (preview) storyboard.Seek(TimeSpan.FromMilliseconds(380 / parameters.Speed));
    }

    void EmitCoin()
    {
        if (emittedCoins == int.MaxValue) return;
        try { emittedCoins = privacy.AddCoin(); }
        catch (IOException ex) { App.RecordEvent("coin save failed: " + ex.Message); return; }
        GlassProfile.Event("coin-emit");
        if (settings.PetId == "polar-bear")
        {
            coinPetRequestedAt = elapsed.ElapsedMilliseconds;
            PlayPolarCoinAction();
        }
        if (!trackingCoins)
        {
            coinPopup.XamlRoot = surface.XamlRoot;
            PositionCoinLayer();
            coinPopup.IsOpen = true;
            trackingCoins = true;
            CompositionTarget.Rendering += TrackCoinLayer;
        }
        int tier = CoinTier(emittedCoins);
        double coinSize = 27 + tier * .35, halfCoin = coinSize / 2;
        var face = CreateCoinFace(tier, coinSize, out Border? sheen);
        var coin = new Border
        {
            Width = coinSize, Height = coinSize, Child = face, RenderTransformOrigin = new Point(.5, .5)
        };
        // The popup has a different visual root: map via the main surface, then
        // convert both axes to the logical coordinates of the scaled coin canvas.
        var head = pet.TransformToVisual(surface).TransformPoint(new Point(pet.ActualWidth * .49, pet.ActualHeight * .04));
        double left = head.X / PhysicalScale - halfCoin;
        double top = CoinHeadroom + head.Y / PhysicalScale - coinSize;
        double landing = CoinHeadroom - coinSize - top;
        double glassWidth = surface.Width / PhysicalScale;
        double startX = left + halfCoin;
        // A balanced fan of short arcs, with small variations instead of unrelated random parameters.
        int lane = (emittedCoins - 1) % 5;
        double offset = lane switch { 0 => -36, 1 => -12, 2 => 22, 3 => -24, _ => 10 };
        double hitX = Math.Clamp(startX + offset + Random.Shared.NextDouble() * 6 - 3, 40, glassWidth - 40);
        double drift = Math.Sign(hitX - startX) * (7 + Random.Shared.NextDouble() * 3);
        double secondX = Math.Clamp(hitX + drift * .7, 40, glassWidth - 40);
        double finalX = Math.Clamp(hitX + drift, 40, glassWidth - 40);
        int apexAt = Random.Shared.Next(240, 257), hitAt = Random.Shared.Next(515, 542);
        int secondAt = hitAt + 245, thirdAt = secondAt + 165;
        double jump = (lane == 0 ? 44 : lane == 2 ? 42 : 38) + Random.Shared.NextDouble() * 4 - 2 + tier * .5;
        double bounce = 10.5 + Random.Shared.NextDouble() * 3;
        bool rollBorder = uiSettings.AnimationsEnabled && Random.Shared.NextDouble() < .15;
        Canvas.SetLeft(coin, left); Canvas.SetTop(coin, top);
        var transform = new CompositeTransform(); coin.RenderTransform = transform;
        coins.Children.Add(coin);
        var storyboard = new Storyboard();
        void Motion(DependencyObject target, string property, params (int At, double Value, EasingMode? Ease)[] keys)
        {
            var motion = new DoubleAnimationUsingKeyFrames();
            foreach (var key in keys)
            {
                var frame = new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(key.At)), Value = key.Value };
                if (key.Ease is EasingMode ease) frame.EasingFunction = new QuadraticEase { EasingMode = ease };
                motion.KeyFrames.Add(frame);
            }
            Storyboard.SetTarget(motion, target); Storyboard.SetTargetProperty(motion, property);
            storyboard.Children.Add(motion);
        }
        if (sheen?.RenderTransform is CompositeTransform shimmer)
        {
            Motion(shimmer, "TranslateX", (0, -6, null), (280, 5, EasingMode.EaseOut), (530, 5, null));
            Motion(sheen, "Opacity", (0, 0, null), (160, .2 + tier * .035, null), (380, 0, null));
        }
        var xKeys = new System.Collections.Generic.List<(int At, double Value, EasingMode? Ease)>
        {
            (0, 0, null), (apexAt, (hitX - startX) * apexAt / hitAt, null),
            (hitAt, hitX - startX, null), (secondAt, secondX - startX, null), (thirdAt, finalX - startX, null)
        };
        var yKeys = new System.Collections.Generic.List<(int At, double Value, EasingMode? Ease)>
        {
            (0, 0, null), (apexAt, landing - jump, EasingMode.EaseOut), (hitAt, landing, EasingMode.EaseIn),
            (hitAt + (secondAt - hitAt) / 2, landing - bounce, EasingMode.EaseOut), (secondAt, landing, EasingMode.EaseIn),
            (secondAt + (thirdAt - secondAt) / 2, landing - bounce * .35, EasingMode.EaseOut), (thirdAt, landing, EasingMode.EaseIn)
        };
        int endAt = thirdAt + 330;
        double tilt = Math.Sign(hitX - startX) * (12 + Random.Shared.NextDouble() * 6);
        var rotationKeys = new System.Collections.Generic.List<(int At, double Value, EasingMode? Ease)>
        { (0, 0, null), (thirdAt, tilt, null) };
        if (rollBorder)
        {
            // Stay on the straight top edge and fade during the latter half of the short roll.
            double topLength = glassWidth - 80;
            int direction = Random.Shared.Next(2) == 0 ? -1 : 1;
            double available = direction > 0 ? glassWidth - 40 - finalX : finalX - 40;
            if (available < Math.Min(70, topLength * .3))
            {
                direction = -direction;
                available = direction > 0 ? glassWidth - 40 - finalX : finalX - 40;
            }
            double distance = Math.Min(available, topLength * (.32 + Random.Shared.NextDouble() * .18));
            int rollDuration = Random.Shared.Next(900, 1401);
            endAt = thirdAt + rollDuration;
            xKeys.Add((endAt, finalX - startX + direction * distance, EasingMode.EaseOut));
            rotationKeys.Add((endAt, tilt + direction * distance / halfCoin * 180 / Math.PI, EasingMode.EaseOut));
        }
        Motion(transform, "TranslateX", xKeys.ToArray());
        Motion(transform, "TranslateY", yKeys.ToArray());
        Motion(transform, "Rotation", rotationKeys.ToArray());
        long goal = NextCoinGoal(emittedCoins);
        coinScore.Text = emittedCoins.ToString("N0", CultureInfo.InvariantCulture);
        coinScoreTarget.Text = goal.ToString("N0", CultureInfo.InvariantCulture);
        double scoreProgress = Math.Clamp(emittedCoins / (double)goal, 0, 1);
        double scoreAngle = -Math.PI / 2 + scoreProgress * Math.PI * 2;
        coinScoreRingSegment.Point = new Point(13 + Math.Cos(scoreAngle) * 10, 13 + Math.Sin(scoreAngle) * 10);
        coinScoreRingSegment.IsLargeArc = scoreProgress > .5;
        coinScoreLastAt = elapsed.ElapsedMilliseconds;
        coinScoreHasEvent = true;
        coinScoreBadge.Visibility = Visibility.Visible;
        Motion(transform, "ScaleX", (0, 1, null), (apexAt / 2, .3 + Random.Shared.NextDouble() * .06, null),
            (apexAt, 1, null), ((apexAt + hitAt) / 2, .25, null), (hitAt, 1, null),
            (hitAt + 30, 1.1, null), (hitAt + 80, 1, null));
        Motion(transform, "ScaleY", (0, 1, null), (hitAt - 10, 1, null), (hitAt + 20, .8, null),
            (hitAt + 80, 1, null), (secondAt - 10, 1, null), (secondAt + 15, .92, null), (secondAt + 65, 1, null));
        int fadeAt = rollBorder ? thirdAt + (int)((endAt - thirdAt) * .4) : endAt - 250;
        Motion(coin, "Opacity", (0, 1, null), (fadeAt, 1, null), (endAt, 0, null));

        var impacts = new System.Collections.Generic.List<Canvas>();
        if (uiSettings.AnimationsEnabled)
        {
            foreach (var hit in new[] { (At: hitAt, X: hitX, Strength: 1d), (At: secondAt, X: secondX, Strength: .36),
                (At: thirdAt, X: finalX, Strength: .15) })
            {
                var impact = CreateCoinImpact(tier, hit.X, hit.At, hit.Strength, storyboard);
                Canvas.SetTop(impact, CoinHeadroom - 16);
                coins.Children.Insert(0, impact); impacts.Add(impact);
            }
        }
        storyboard.Completed += (_, __) =>
        {
            coinStories.Remove(storyboard);
            coins.Children.Remove(coin);
            foreach (var impact in impacts) coins.Children.Remove(impact);
            if (coins.Children.Count == 0) CloseCoinLayer();
        };
        coinStories.Add(storyboard);
        storyboard.Begin();
        if (IsCoinMilestone(emittedCoins)) CelebrateCoins(emittedCoins, left + halfCoin, top + halfCoin);

        // A separate transform keeps click feedback independent of breathing/hover.
        petClickAnimation?.Stop();
        var lift = new DoubleAnimation
        {
            From = 0, To = -2, AutoReverse = true, Duration = new Duration(TimeSpan.FromMilliseconds(100)),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(lift, petClickLift); Storyboard.SetTargetProperty(lift, "Y");
        petClickAnimation = new Storyboard(); petClickAnimation.Children.Add(lift); petClickAnimation.Begin();
    }

    static FrameworkElement CreateShopStar(double size, bool lightBackground)
    {
        var canvas = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
        var tint = lightBackground ? Windows.UI.Color.FromArgb(255, 171, 105, 28) : Windows.UI.Color.FromArgb(255, 255, 224, 157);
        var glow = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        glow.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(85, tint.R, tint.G, tint.B), Offset = 0 });
        glow.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, tint.R, tint.G, tint.B), Offset = 1 });
        canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 20, Height = 20, Fill = glow });
        var star = new Microsoft.UI.Xaml.Shapes.Polygon { Points = new PointCollection(), Fill = new SolidColorBrush(tint), Stroke = new SolidColorBrush(lightBackground ? Windows.UI.Color.FromArgb(230, 111, 63, 12) : Windows.UI.Color.FromArgb(190, 128, 82, 30)), StrokeThickness = .8 };
        foreach (var point in new[] { new Point(10, 2.7), new Point(11.6, 8.2), new Point(17.3, 10), new Point(11.6, 11.8), new Point(10, 17.3), new Point(8.4, 11.8), new Point(2.7, 10), new Point(8.4, 8.2) }) star.Points.Add(point);
        canvas.Children.Add(star);
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas, IsHitTestVisible = false, Opacity = 0 };
    }

    Canvas CreateCoinImpact(int tier, double x, int hitAt, double strength, Storyboard story)
    {
        int duration = 420 + tier * 65;
        double reach = 8 + tier * 3;
        var layer = new Canvas
        {
            Width = coins.Width, Height = 32, Opacity = strength, IsHitTestVisible = false,
            Clip = new RectangleGeometry { Rect = new Rect(20, 0, Math.Max(1, coins.Width - 40), 32) }
        };
        var gold = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 227, 162));
        var reflection = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        reflection.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, 255, 233, 179), Offset = 0 });
        reflection.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(220, 255, 233, 179), Offset = .5 });
        reflection.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, 255, 233, 179), Offset = 1 });
        void Add(FrameworkElement element, double left, double top)
        { element.Opacity = 0; Canvas.SetLeft(element, left); Canvas.SetTop(element, top); layer.Children.Add(element); }
        void Fade(UIElement target, double peak, int delay = 0)
        {
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(hitAt + delay) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
            fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(24)), Value = peak });
            fade.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Math.Max(25, duration - delay))), Value = 0,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            Storyboard.SetTarget(fade, target); Storyboard.SetTargetProperty(fade, "Opacity"); story.Children.Add(fade);
        }
        void Move(DependencyObject target, string property, double from, double to, int delay = 0)
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, BeginTime = TimeSpan.FromMilliseconds(hitAt + delay),
                Duration = new Duration(TimeSpan.FromMilliseconds(Math.Max(1, duration - delay))),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); story.Children.Add(animation);
        }
        var contact = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10 + tier, Height = 4, Fill = reflection };
        Add(contact, x - contact.Width / 2, 14); Fade(contact, .65);
        if (tier >= 1)
        {
            int count = tier >= 2 ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                double radius = (4 + reach) * (i == 0 ? 1 : .72);
                var wave = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = radius * 2, Height = 2.2 + tier * .24, Stroke = gold, StrokeThickness = .7,
                    RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform()
                };
                Add(wave, x - radius, 16 - wave.Height / 2 + i * .8);
                Fade(wave, i == 0 ? .4 : .23, i * 80);
                Move(wave.RenderTransform, "ScaleX", .25, 1, i * 80);
                Move(wave.RenderTransform, "ScaleY", 1, 1.6, i * 80);
            }
        }
        if (tier >= 3)
        {
            var edgeLight = new Border { Width = reach * 2.8, Height = .8, Background = reflection, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform() };
            Add(edgeLight, x - edgeLight.Width / 2, 15.6); Fade(edgeLight, .5);
            Move(edgeLight.RenderTransform, "ScaleX", .35, 1);
        }
        if (tier >= 4)
        {
            int count = tier >= 6 ? 3 : 2;
            for (int i = 0; i < count; i++)
            {
                double side = i - (count - 1) / 2d;
                var spark = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = i == 1 ? 1.8 : 1.3, Height = i == 1 ? 1.8 : 1.3, Fill = gold,
                    RenderTransform = new TranslateTransform()
                };
                Add(spark, x + side * 5 - spark.Width / 2, 13); Fade(spark, .6);
                Move(spark.RenderTransform, "X", 0, side * 8);
                Move(spark.RenderTransform, "Y", 0, -3 - (i % 2) * 3);
            }
        }
        if (tier >= 5)
        {
            var innerReflection = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = reach * 2, Height = 4.5, Fill = reflection };
            Add(innerReflection, x - reach, 17); Fade(innerReflection, .22);
        }
        GlassProfile.Event("coin-impact " + tier + " " + hitAt);
        return layer;
    }

    static readonly uint[,] CoinMetals =
    {
        { 0xD9AD69, 0xC38B3D, 0x9C6327 }, { 0xFFE49A, 0xEDB94D, 0xCA8C26 },
        { 0xFFEDAE, 0xF2BF4B, 0xCB8D25 }, { 0xFFF1C7, 0xEFD38B, 0xB99036 },
        { 0xFFF1C3, 0xF1CC70, 0xC79531 }, { 0xFFF2BE, 0xF4D078, 0xC79531 },
        { 0xFFF7DC, 0xECDBA4, 0xAE893A }, { 0xFFFCE9, 0xF7E5B2, 0xB6994A }
    };

    static Canvas CreateCoinFace(int tier, double size, out Border? sheen)
    {
        double r = size / 2;
        Windows.UI.Color Tint(uint rgb) => Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        var top = new SolidColorBrush(Tint(CoinMetals[tier, 0]));
        var middle = Tint(CoinMetals[tier, 1]);
        var edge = new SolidColorBrush(Tint(CoinMetals[tier, 2]));
        var face = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        void Add(UIElement visual, double x, double y)
        { Canvas.SetLeft(visual, x); Canvas.SetTop(visual, y); face.Children.Add(visual); }
        void Outline(double radius, Brush? fill, Brush stroke, double thickness, bool faceted)
        {
            Microsoft.UI.Xaml.Shapes.Shape shape;
            if (faceted)
            {
                var polygon = new Microsoft.UI.Xaml.Shapes.Polygon();
                for (int i = 0; i < 12; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / 6;
                    polygon.Points.Add(new Point(radius + Math.Cos(angle) * radius, radius + Math.Sin(angle) * radius));
                }
                shape = polygon;
            }
            else shape = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = radius * 2, Height = radius * 2 };
            shape.Fill = fill; shape.Stroke = stroke; shape.StrokeThickness = thickness;
            Add(shape, r - radius, r - radius);
        }
        var metal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        metal.GradientStops.Add(new GradientStop { Color = top.Color, Offset = 0 });
        metal.GradientStops.Add(new GradientStop { Color = middle, Offset = .4 });
        metal.GradientStops.Add(new GradientStop { Color = edge.Color, Offset = .7 });
        metal.GradientStops.Add(new GradientStop { Color = middle, Offset = 1 });
        Outline(r - .65, metal, edge, 1.3, tier >= 6);
        if (tier >= 1) Outline(r - 1.5, null, top, 1.15, tier >= 6);
        if (tier >= 2)
        {
            var grooves = new PathGeometry();
            for (int i = 0; i < 24; i++)
            {
                double angle = i * Math.PI / 12;
                var line = new PathFigure { StartPoint = new Point(r + Math.Cos(angle) * (r - 1.8), r + Math.Sin(angle) * (r - 1.8)), IsFilled = false };
                line.Segments.Add(new LineSegment { Point = new Point(r + Math.Cos(angle) * (r - 3.5), r + Math.Sin(angle) * (r - 3.5)) });
                grooves.Figures.Add(line);
            }
            Add(new Microsoft.UI.Xaml.Shapes.Path { Data = grooves, Stroke = edge, StrokeThickness = .65 }, 0, 0);
        }
        if (tier >= 3)
        {
            var center = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
            center.GradientStops.Add(new GradientStop { Color = top.Color, Offset = 0 });
            center.GradientStops.Add(new GradientStop { Color = middle, Offset = 1 });
            Outline(r - 4, center, edge, .6, false);
            Outline(r - 4.8, null, top, .6, false);
        }
        if (tier >= 4)
        {
            var wreath = new PathGeometry();
            foreach (int side in new[] { -1, 1 })
            {
                var stem = new PathFigure { StartPoint = new Point(r + side * 3, r + 8), IsFilled = false };
                stem.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(r + side * 11, r + 4), Point2 = new Point(r + side * 7, r - 6) });
                wreath.Figures.Add(stem);
                for (int i = 0; i < 4; i++)
                {
                    double x = r + side * (6.3 + Math.Sin(i * .7) * 1.8), y = r + 5 - i * 2.6;
                    var leaf = new PathFigure { StartPoint = new Point(x - .8, y), IsClosed = true };
                    leaf.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(x - side * 1.2, y - 2), Point2 = new Point(x + .8, y) });
                    leaf.Segments.Add(new QuadraticBezierSegment { Point1 = new Point(x + side * 1.2, y + 2), Point2 = new Point(x - .8, y) });
                    wreath.Figures.Add(leaf);
                }
            }
            Add(new Microsoft.UI.Xaml.Shapes.Path { Data = wreath, Stroke = edge, StrokeThickness = .55, Fill = edge }, 0, 0);
        }
        Add(new Border
        {
            Width = size, Height = size, Padding = new Thickness(0, tier >= 5 ? 3.6 : 1, 0, 0),
            Child = new TextBlock
            {
                Text = "￥", FontSize = tier >= 4 ? 12 : 16, FontFamily = new FontFamily("Microsoft YaHei"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold, IsTextScaleFactorEnabled = false, TextLineBounds = TextLineBounds.Tight,
                Foreground = tier == 0 ? new SolidColorBrush(Tint(0x754817)) : edge,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        }, 0, 0);
        if (tier >= 5)
            Add(new TextBlock { Text = "✦", FontSize = 7, IsTextScaleFactorEnabled = false, Foreground = edge, TextLineBounds = TextLineBounds.Tight, Width = 8, TextAlignment = TextAlignment.Center }, r - 4, r - 10);
        sheen = null;
        if (tier >= 1)
        {
            sheen = new Border
            {
                Width = 2, Height = size * .48, Opacity = 0, CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 255, 254, 240)),
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new CompositeTransform { Rotation = -25 }
            };
            Add(sheen, r - 1, r - size * .24);
        }
        if (tier == 7)
        {
            var glint = new PathFigure { StartPoint = new Point(r - r * .7, r - r * .35), IsFilled = false };
            glint.Segments.Add(new LineSegment { Point = new Point(r - r * .5, r - r * .65) });
            glint.Segments.Add(new LineSegment { Point = new Point(r - r * .15, r - r * .81) });
            var geometry = new PathGeometry(); geometry.Figures.Add(glint);
            Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = new SolidColorBrush(Tint(0xFFFDF1)), StrokeThickness = .7 }, 0, 0);
        }
        return face;
    }

    static int CoinTier(int count) => count < 10 ? 0 : count < 50 ? 1 : count < 100 ? 2 : count < 200 ? 3 :
        count < 300 ? 4 : count < 500 ? 5 : count < 1000 ? 6 : 7;

    static long NextCoinGoal(int count) => count < 10 ? 10 : count < 50 ? 50 : count < 100 ? 100 :
        count < 200 ? 200 : count < 300 ? 300 : count < 500 ? 500 : count < 1000 ? 1000 : ((long)count / 1000 + 1) * 1000;

    static bool IsCoinMilestone(int count) => count == 1 || count == 10 || count == 50 || count == 100 ||
        count == 200 || count == 300 || count == 500 || (count >= 1000 && count % 1000 == 0);

    void CelebrateCoins(int count, double x, double y)
    {
        int level = CoinTier(count);
        string[] titles = { "第一枚金币", "小有收获", "渐入佳境", "百枚达成", "金币高手", "财运亨通", "闪耀时刻", "千金成就" };
        var palette = new[] { Windows.UI.Color.FromArgb(255, 255, 230, 172), Windows.UI.Color.FromArgb(255, 245, 204, 120),
            Windows.UI.Color.FromArgb(255, 255, 241, 204) };
        var story = new Storyboard();
        var visuals = new System.Collections.Generic.List<UIElement>();
        void Add(UIElement visual, double left, double top)
        {
            Canvas.SetLeft(visual, left); Canvas.SetTop(visual, top);
            visuals.Add(visual); coins.Children.Add(visual);
        }
        void Animate(DependencyObject target, string property, double from, double to, int start, int duration, bool ease = true)
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, BeginTime = TimeSpan.FromMilliseconds(start),
                Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
                EasingFunction = ease ? new CubicEase { EasingMode = EasingMode.EaseOut } : null
            };
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }
        bool paleBackground = textAppearanceInitialized && lightness < .5;
        var gold = new SolidColorBrush(paleBackground ? Windows.UI.Color.FromArgb(255, 111, 79, 30) : Windows.UI.Color.FromArgb(255, 255, 232, 174));
        bool upgraded = CoinTier(count) > CoinTier(Math.Max(0, count - 1));
        double width = Math.Min(upgraded ? 216 : 204, coins.Width - 32);
        var label = new Border
        {
            Width = width, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
        };
        if (upgraded)
        {
            if (activeCoinUpgrade != null) coins.Children.Remove(activeCoinUpgrade);
            activeCoinUpgrade = label;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            var rotation = new PlaneProjection();
            var upgradedCoin = new Viewbox
            {
                Width = 34, Height = 34, Child = CreateCoinFace(level, 34, out _), Projection = rotation
            };
            row.Children.Add(upgradedCoin);
            var wording = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            wording.Children.Add(new TextBlock
            {
                Text = titles[level], FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                IsTextScaleFactorEnabled = false, Foreground = gold
            });
            wording.Children.Add(new TextBlock
            {
                Text = "金币升级 · " + count.ToString("N0", CultureInfo.InvariantCulture) + " 枚", FontSize = 12,
                IsTextScaleFactorEnabled = false, Foreground = new SolidColorBrush(paleBackground ? Windows.UI.Color.FromArgb(255, 76, 76, 84) : Windows.UI.Color.FromArgb(255, 196, 198, 208))
            });
            row.Children.Add(wording); label.Child = row;
            if (uiSettings.AnimationsEnabled)
            {
                var spin = new DoubleAnimation
                {
                    From = 0, To = 720, Duration = new Duration(TimeSpan.FromMilliseconds(1800)),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                Storyboard.SetTarget(spin, rotation); Storyboard.SetTargetProperty(spin, "RotationY"); story.Children.Add(spin);
            }
        }
        else label.Child = new TextBlock
        {
            Text = titles[level] + " · " + count.ToString("N0", CultureInfo.InvariantCulture), FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, IsTextScaleFactorEnabled = false,
            Foreground = gold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Add(label, upgraded ? Math.Max(16, (coins.Width - width) / 2) : Math.Clamp(x - width / 2, 8, Math.Max(8, coins.Width - width - 8)), 10);
        var labelMove = new TranslateTransform { Y = 8 }; label.RenderTransform = labelMove;
        Animate(labelMove, "Y", 8, 0, 0, 260);
        Animate(label, "Opacity", 1, 0, upgraded ? 2200 : 1300, 400, false);
        if (uiSettings.AnimationsEnabled)
        {
            int particles = 4 + level * 2;
            for (int i = 0; i < particles; i++)
            {
                double angle = -Math.PI + Math.PI * 2 * i / particles;
                double distance = 20 + level * 3 + (i % 3) * 5;
                var spark = new TextBlock
                {
                    Text = i % 3 == 0 ? "✦" : "•", FontSize = i % 3 == 0 ? 13 : 10,
                    Foreground = new SolidColorBrush(palette[i % palette.Length]),
                    IsTextScaleFactorEnabled = false, RenderTransformOrigin = new Point(.5, .5)
                };
                Add(spark, x - 7, y - 7);
                var movement = new CompositeTransform(); spark.RenderTransform = movement;
                double endX = Math.Clamp(x + Math.Cos(angle) * distance, 10, coins.Width - 18) - x;
                double endY = Math.Clamp(y + Math.Sin(angle) * distance, 48, coins.Height - 18) - y;
                int duration = 950 + (i % 4) * 100;
                Animate(movement, "TranslateX", 0, endX, 0, duration);
                Animate(movement, "TranslateY", 0, endY, 0, duration);
                Animate(spark, "Opacity", 1, 0, 200, duration, false);
            }
            if (level >= 3)
            {
                int rings = level >= 5 ? 2 : 1;
                for (int i = 0; i < rings; i++)
                {
                    var halo = new Microsoft.UI.Xaml.Shapes.Ellipse
                    {
                        Width = 40, Height = 40, StrokeThickness = 1,
                        Stroke = new SolidColorBrush(palette[i]), RenderTransformOrigin = new Point(.5, .5)
                    };
                    Add(halo, x - 20, y - 20);
                    var expansion = new ScaleTransform(); halo.RenderTransform = expansion;
                    double radius = Math.Min(34 + level * 2, Math.Max(20, Math.Min(Math.Min(x - 2, coins.Width - x - 2), Math.Min(y - 2, coins.Height - y - 2))));
                    int delay = i * 120;
                    halo.Opacity = 0;
                    Animate(expansion, "ScaleX", .7, radius / 20, delay, 1000);
                    Animate(expansion, "ScaleY", level >= 5 ? .46 : .7, radius / 20 * (level >= 5 ? .66 : 1), delay, 1000);
                    Animate(halo, "Opacity", .32, 0, delay, 1200, false);
                }
            }
            if (level >= 5)
            {
                double radius = Math.Min(31 + level, Math.Max(10, Math.Min(x - 10, coins.Width - x - 10)));
                for (int i = 0; i < 2; i++)
                {
                    var spark = new TextBlock
                    {
                        Text = "✦", FontSize = 12, IsTextScaleFactorEnabled = false,
                        Foreground = new SolidColorBrush(palette[i]), Opacity = .65
                    };
                    Add(spark, x - 6, y - 6);
                    var orbit = new TranslateTransform(); spark.RenderTransform = orbit;
                    foreach (bool horizontal in new[] { true, false })
                    {
                        var motion = new DoubleAnimationUsingKeyFrames();
                        for (int k = 0; k <= 18; k++)
                        {
                            double angle = i * Math.PI + k / 18d * Math.PI;
                            motion.KeyFrames.Add(new LinearDoubleKeyFrame
                            {
                                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(k * 100)),
                                Value = horizontal ? Math.Cos(angle) * radius : Math.Sin(angle) * radius * .65
                            });
                        }
                        Storyboard.SetTarget(motion, orbit); Storyboard.SetTargetProperty(motion, horizontal ? "X" : "Y");
                        story.Children.Add(motion);
                    }
                    Animate(spark, "Opacity", .65, 0, 800, 1000, false);
                }
            }
        }
        story.Completed += (_, __) =>
        {
            coinStories.Remove(story);
            if (ReferenceEquals(activeCoinUpgrade, label)) activeCoinUpgrade = null;
            foreach (var visual in visuals) coins.Children.Remove(visual);
            if (coins.Children.Count == 0) CloseCoinLayer();
        };
        coinStories.Add(story); story.Begin();
        GlassProfile.Event("coin-milestone " + count);
    }

    void CelebrateWishCompleted(string name)
    {
        if (!uiSettings.AnimationsEnabled) return;
        if (!trackingCoins)
        {
            coinPopup.XamlRoot = surface.XamlRoot;
            PositionCoinLayer();
            coinPopup.IsOpen = true;
            trackingCoins = true;
            CompositionTarget.Rendering += TrackCoinLayer;
        }
        var story = new Storyboard();
        var visuals = new System.Collections.Generic.List<UIElement>();
        void Add(UIElement visual, double left, double top)
        {
            Canvas.SetLeft(visual, left); Canvas.SetTop(visual, top);
            visuals.Add(visual); coins.Children.Add(visual);
        }
        void Animate(DependencyObject target, string property, double from, double to, int start, int duration, bool ease = true)
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, BeginTime = TimeSpan.FromMilliseconds(start),
                Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
                EasingFunction = ease ? new CubicEase { EasingMode = EasingMode.EaseOut } : null
            };
            Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
            story.Children.Add(animation);
        }
        bool paleBackground = textAppearanceInitialized && lightness < .5;
        var gold = new SolidColorBrush(paleBackground ? Windows.UI.Color.FromArgb(255, 111, 79, 30) : Windows.UI.Color.FromArgb(255, 255, 232, 174));
        var palette = new[] { Windows.UI.Color.FromArgb(255, 255, 214, 120), Windows.UI.Color.FromArgb(255, 255, 170, 200), Windows.UI.Color.FromArgb(255, 150, 200, 255) };
        string display = string.IsNullOrWhiteSpace(name) ? "小心愿" : name.Trim();
        double width = Math.Min(212, Math.Max(120, coins.Width - 32));
        var label = new Border
        {
            Width = width, IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = "心愿达成 · " + display, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = gold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis, IsTextScaleFactorEnabled = false
            }
        };
        double centerX = coins.Width / 2;
        double centerY = CoinHeadroom + Math.Max(24, (coins.Height - CoinHeadroom) * .6);
        Add(label, centerX - width / 2, centerY - 14);
        var labelMove = new TranslateTransform { Y = 8 }; label.RenderTransform = labelMove;
        Animate(labelMove, "Y", 8, 0, 0, 260);
        Animate(label, "Opacity", 1, 0, 2600, 400, false);
        int sparks = 12;
        for (int i = 0; i < sparks; i++)
        {
            double angle = Math.PI * 2 * i / sparks;
            double distance = 26 + (i % 4) * 6;
            var spark = new TextBlock
            {
                Text = i % 3 == 0 ? "✦" : "•", FontSize = i % 3 == 0 ? 13 : 10,
                Foreground = new SolidColorBrush(palette[i % palette.Length]),
                IsTextScaleFactorEnabled = false, RenderTransformOrigin = new Point(.5, .5)
            };
            Add(spark, centerX - 6, centerY - 6);
            var movement = new CompositeTransform(); spark.RenderTransform = movement;
            double endX = Math.Clamp(centerX + Math.Cos(angle) * distance, 8, coins.Width - 16) - centerX;
            double endY = Math.Clamp(centerY + Math.Sin(angle) * distance * .7, CoinHeadroom + 8, coins.Height - 16) - centerY;
            int duration = 900 + (i % 4) * 90;
            Animate(movement, "TranslateX", 0, endX, 0, duration);
            Animate(movement, "TranslateY", 0, endY, 0, duration);
            Animate(spark, "Opacity", 1, 0, 180, duration, false);
        }
        story.Completed += (_, __) =>
        {
            coinStories.Remove(story);
            foreach (var visual in visuals) coins.Children.Remove(visual);
            if (coins.Children.Count == 0) CloseCoinLayer();
        };
        coinStories.Add(story); story.Begin();
        GlassProfile.Event("wish-completed");
    }

    bool isDragging, endingDrag, dragMoved, dragUsesMouse, dragReleasePending;
    bool suppressPetTap, dragStartedOnPet;
    Win32Point initialCursor;
    PointInt32 initialWindowPos;
    PointInt32 lastDragPosition;
    Win32Point previousTapCursor;
    PointInt32 doubleTapStartWindow;
    long previousTapAt = -10000;
    bool restoreDoubleTapPosition;

    void OnCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(surface);
        if (!point.Properties.IsLeftButtonPressed) return;
        bool overStats = false;
        dragStartedOnPet = false;
        for (DependencyObject? source = e.OriginalSource as DependencyObject; source != null && source != card; source = VisualTreeHelper.GetParent(source))
        {
            if (source is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return;
            if (source is Image || ReferenceEquals(source, pet.Parent)) dragStartedOnPet = true;
            if (ReferenceEquals(source, stats)) overStats = true;
        }

        HideHoverEffect();
        if (layoutTransitionActive) CompleteLayoutTransition();
        if (scalePreviewHostActive) { EndScalePreviewHost(); ApplyWidgetSize(); }

        if (overStats && statsWishMode)
        {
            if (statsDragSettling) { e.Handled = true; return; }
            if (card.CapturePointer(e.Pointer))
            {
                statsDragging = true;
                statsDragPointerId = e.Pointer.PointerId; statsDragStartY = point.Position.Y;
                BeginStatsDrag();
                e.Handled = true;
            }
            return;
        }

        // Use the same physical coordinate source as the move/release paths.
        if (!GetCursorPos(out Win32Point pressCursor)) return;

        long tapAt = elapsed.ElapsedMilliseconds;
        bool sameTapSequence = tapAt - previousTapAt <= GetDoubleClickTime() &&
            Math.Abs(pressCursor.X - previousTapCursor.X) <= GetSystemMetrics(36) &&
            Math.Abs(pressCursor.Y - previousTapCursor.Y) <= GetSystemMetrics(37);
        if (!sameTapSequence) doubleTapStartWindow = native.Position;
        restoreDoubleTapPosition = sameTapSequence;
        previousTapCursor = pressCursor; previousTapAt = tapAt;

        dragUsesMouse = e.Pointer.PointerDeviceType == PointerDeviceType.Mouse;
        dragReleasePending = false;
        isDragging = true;
        if (!card.CapturePointer(e.Pointer)) { isDragging = false; dragUsesMouse = false; return; }
        StartDragReleaseMonitor();
        desktopSource.PrepareWindowMove();
        dragMoved = false;
        initialWindowPos = native.Position;
        lastDragPosition = initialWindowPos;
        initialCursor = pressCursor;
        e.Handled = true;
    }

    void OnCardPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(surface).Position;
        glass.SetInteraction(2 * point.X / Math.Max(1, surface.Width) - 1, 2 * point.Y / Math.Max(1, surface.Height) - 1, true);
        if (statsDragging)
        {
            if (e.Pointer.PointerId == statsDragPointerId) UpdateStatsDrag(point.Y);
            e.Handled = true;
            return;
        }
        if (isDragging) e.Handled = true;
    }

    void StartDragReleaseMonitor()
    {
        if (dragReleaseTimer == null)
        {
            dragReleaseTimer = DispatcherQueue.CreateTimer();
            dragReleaseTimer.Tick += (_, __) => CheckDragRelease();
        }
        // Only a release watchdog. HWND motion and atlas sampling share Rendering.
        dragReleaseTimer.Interval = TimeSpan.FromMilliseconds(50);
        dragReleaseTimer.Start();
    }

    bool CheckDragRelease()
    {
        if (!isDragging || disposed || dragReleasePending) return true;
        // Pointer events can be lost while the HWND moves under the cursor.
        if (dragUsesMouse && (GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
        {
            dragReleasePending = true;
            GlassProfile.Event("drag-auto-release");
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!dragReleasePending) return;
                dragReleasePending = false;
                if (isDragging) EndDrag(null, sampleFinalPosition: true);
            });
            return true;
        }
        return false;
    }

    void UpdateDragFrame()
    {
        if (CheckDragRelease()) return;
        if (!GetCursorPos(out Win32Point currentCursor)) return;
        int dx = currentCursor.X - initialCursor.X, dy = currentCursor.Y - initialCursor.Y;
        if (!dragMoved && Math.Abs(dx) < GetSystemMetrics(68) && Math.Abs(dy) < GetSystemMetrics(69)) return;
        var target = new PointInt32(initialWindowPos.X + dx, initialWindowPos.Y + dy);
        if (target.X == lastDragPosition.X && target.Y == lastDragPosition.Y) return;
        var position = ClampDragPosition(target);
        if (position.X == lastDragPosition.X && position.Y == lastDragPosition.Y) return;
        // Keep the HWND at normal size. Pure motion reuses one uploaded scene
        // instead of cropping and uploading the card bitmap at each position.
        bool firstMove = !dragMoved;
        long moveStarted = GlassProfile.Timestamp();
        native.Move(position);
        if (GlassProfile.Enabled)
        {
            double moveMs = GlassProfile.Ms(moveStarted);
            if (moveMs >= 4) GlassProfile.Event($"drag-move {moveMs:F1}ms");
        }
        suppressPetTap = true;
        lastDragPosition = position;
        dragMoved = true;
        if (firstMove) desktopSource.BeginWindowMove();
    }

    PointInt32 ClampDragPosition(PointInt32 position)
    {
        var size = native.Size;
        var center = new Win32Point { X = position.X + size.Width / 2, Y = position.Y + size.Height / 2 };
        int left, top, right, bottom;
        if (TryGetMonitorBounds(center, out var bounds))
        {
            left = bounds.Left; top = bounds.Top; right = bounds.Right; bottom = bounds.Bottom;
        }
        else
        {
            var area = DisplayArea.GetFromPoint(new PointInt32(center.X, center.Y), DisplayAreaFallback.Nearest).OuterBounds;
            left = area.X; top = area.Y; right = area.X + area.Width; bottom = area.Y + area.Height;
        }
        // Keep the entire glass source on a real display, including the taskbar area.
        return new PointInt32(
            Math.Clamp(position.X, left, Math.Max(left, right - size.Width)),
            Math.Clamp(position.Y, top, Math.Max(top, bottom - size.Height)));
    }

    bool TryGetMonitorBounds(Win32Point point, out NativeClientRect bounds)
    {
        IntPtr monitor = MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            bounds = info.MonitorRect;
            return true;
        }
        bounds = default;
        return false;
    }

    void EnsureRenderEraseMonitor()
    {
        IntPtr render = FindWindowEx(hwnd, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (render == IntPtr.Zero || render == eraseRenderHandle) return;
        renderEraseMonitor?.Dispose();
        eraseRenderHandle = render;
        renderEraseMonitor = new WinUIEx.Messaging.WindowMessageMonitor(render);
        renderEraseMonitor.WindowMessageReceived += OnEraseBackground;
        renderEraseMonitor.WindowMessageReceived += OnLayoutHitTest;
    }

    void OnEraseBackground(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
    {
        if (disposed) return;
        if (args.Message.Hwnd == eraseRenderHandle && args.Message.MessageId == 0x0005 /* WM_SIZE */)
        {
            // The XAML child can resize after AppWindow. Clear its final backing,
            // then align the glass shape and native region to that actual viewport.
            args.Result = DefSubclassProc(args.Message.Hwnd, args.Message.MessageId, args.Message.WParam, args.Message.LParam);
            args.Handled = true;
            ClearTransparentHostBackground();
            UpdateRenderViewport();
            return;
        }
        if (args.Message.MessageId != 0x0014 /* WM_ERASEBKGND */) return;
        IntPtr dc = (IntPtr)args.Message.WParam;
        if (dc == IntPtr.Zero || !GetClientRect(args.Message.Hwnd, out NativeClientRect bounds)) return;
        // Paint before the default procedure can expose white during a cold resize.
        // Black is the zero-alpha backing used by TransparentTintBackdrop.
        if (FillRect(dc, ref bounds, GetStockObject(4 /* BLACK_BRUSH */)) == 0) return;
        args.Result = (IntPtr)1;
        args.Handled = true;
    }

    bool ClearTransparentHostBackground()
    {
        EnsureRenderEraseMonitor();
        bool cleared = ClearTransparentWindowBackground(hwnd);
        if (eraseRenderHandle != IntPtr.Zero)
            cleared = ClearTransparentWindowBackground(eraseRenderHandle) && cleared;
        return cleared;
    }

    static bool ClearTransparentWindowBackground(IntPtr window)
    {
        if (!GetClientRect(window, out NativeClientRect bounds)) return false;
        IntPtr dc = GetDC(window);
        if (dc == IntPtr.Zero) return false;
        try { return FillRect(dc, ref bounds, GetStockObject(4 /* BLACK_BRUSH */)) != 0; }
        finally { ReleaseDC(window, dc); }
    }

    void OnCardPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (statsDragging)
        {
            double clickY = e.GetCurrentPoint(stats).Position.Y;
            int clickDirection = clickY < stats.ActualHeight / 2 ? 1 : -1;
            EndStatsDragRelease(e.Pointer, allowClick: true, clickDirection);
            e.Handled = true;
            return;
        }
        bool petClick = isDragging && dragStartedOnPet && !dragMoved;
        if (petClick) suppressPetTap = true;
        EndDrag(e.Pointer, sampleFinalPosition: true);
        if (petClick) EmitCoin();
        e.Handled = true;
    }

    void OnCardPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (statsDragging)
        {
            statsDragOffset = 0; statsDragMoved = false;
            EndStatsDragRelease(e.Pointer, allowClick: false);
            return;
        }
        EndDrag(e.Pointer);
    }

    void EndDrag(Pointer? pointer, bool sampleFinalPosition = false)
    {
        if (!isDragging || endingDrag) return;
        endingDrag = true;
        isDragging = false;
        try
        {
            // Only a normal release can add the final unsampled movement.
            // Cancellation/activation loss keeps the last position already shown.
            if (sampleFinalPosition && GetCursorPos(out Win32Point cursor))
            {
                int dx = cursor.X - initialCursor.X, dy = cursor.Y - initialCursor.Y;
                if (dragMoved || Math.Abs(dx) >= GetSystemMetrics(68) || Math.Abs(dy) >= GetSystemMetrics(69))
                {
                    lastDragPosition = ClampDragPosition(new PointInt32(initialWindowPos.X + dx, initialWindowPos.Y + dy));
                    if (native.Position.X != lastDragPosition.X || native.Position.Y != lastDragPosition.Y)
                    {
                        native.Move(lastDragPosition);
                        suppressPetTap = true;
                    }
                }
            }
            settings.X = native.Position.X; settings.Y = native.Position.Y;
            Save(); desktopSource.Capture();
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
        finally
        {
            try { desktopSource.EndWindowMove(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
            try { card.ReleasePointerCaptures(); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
            dragReleaseTimer?.Stop();
            dragMoved = false;
            dragUsesMouse = false;
            dragReleasePending = false;
            dragStartedOnPet = false;
            endingDrag = false;
            if (!disposed && !exiting) Refresh();
            if (hoverDragArmed && settings.MouseClickThrough)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (isDragging) return;
                    DisarmHoverDrag();
                    // Disarming resets the dwell timer so hovering can start again in place.
                });
        }
    }

    void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(surface);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.RightButtonReleased) return;
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        ShowContextMenu(point.Position, mouse: true);
        e.Handled = true;
    }

    void OnSurfaceRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Card releases are handled before they bubble, so RightTapped remains
        // the reliable mouse entry, and it also covers touch long-press.
        if (e.PointerDeviceType == PointerDeviceType.Mouse &&
            Environment.TickCount64 - widgetMenuShownAt < 600) { e.Handled = true; return; }
        ShowContextMenu(e.GetPosition(surface), e.PointerDeviceType == PointerDeviceType.Mouse);
        e.Handled = true;
    }

    void ShowContextMenu(Point point, bool mouse)
    {
        if (disposed || widgetMenuOpen) return;
        if (isDragging) EndDrag(null, sampleFinalPosition: true);
        int screenX, screenY;
        if (mouse)
        {
            if (!GetCursorPos(out Win32Point cursor)) return;
            screenX = cursor.X; screenY = cursor.Y;
        }
        else if (!TrySurfacePointToScreen(point, out screenX, out screenY)) return;
        WidgetMenuWindow menu;
        try { menu = EnsureWidgetMenu(); }
        catch (Exception ex) { Debug.WriteLine("右键菜单无法创建：" + ex.Message); return; }
        widgetMenuShownAt = Environment.TickCount64;
        GlassProfile.Event("context-menu-show");
        widgetMenuOpen = true;
        try
        {
            menu.ShowAt(screenX, screenY, SystemTheme.IsDark(), Math.Clamp(EffectiveScale, .85, 1.25));
        }
        catch (Exception ex)
        {
            widgetMenuOpen = false;
            Debug.WriteLine("右键菜单无法显示：" + ex.Message);
        }
    }

    bool TrySurfacePointToScreen(Point point, out int x, out int y)
    {
        x = y = 0;
        var origin = new Win32Point();
        if (!ClientToScreen(hwnd, ref origin)) return false;
        double dpi = Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
        var local = surface.TransformToVisual(hostSurface).TransformPoint(point);
        x = origin.X + (int)Math.Round(local.X * dpi);
        y = origin.Y + (int)Math.Round(local.Y * dpi);
        return true;
    }

    void ApplyMouseClickThrough()
    {
        if (!settings.MouseClickThrough) HideHoverEffect();
        bool clickThrough = settings.MouseClickThrough && !hoverDragArmed;
        if (clickThrough)
        {
            EndDrag(null);
            glass.SetInteraction(0, 0, false);
        }
        int extended = GetWindowLong(hwnd, GWL_EXSTYLE);
        int next = clickThrough
            ? extended | WS_EX_LAYERED | WS_EX_TRANSPARENT
            : extended & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT);
        if (startupMaskActive) next |= WS_EX_LAYERED;
        if (next == extended) return;
        SetWindowLong(hwnd, GWL_EXSTYLE, next);
        // Keep XAML opaque at the window level; only mouse input passes through.
        if (clickThrough || startupMaskActive) SetLayeredWindowAttributes(hwnd, 0, startupMaskActive ? (byte)0 : (byte)255, 2 /* LWA_ALPHA */);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037 /* FRAMECHANGED | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE */);
    }

    int HoverDragDelaySeconds => Math.Clamp(settings.HoverDragSeconds, 1, 3);

    void ResetHoverHold()
    {
        hoverHoldSinceTick = 0;
        hoverHoldX = hoverHoldY = hoverSampleX = hoverSampleY = 0;
    }

    void UpdateHoverDragMonitor()
    {
        UpdatePetHoverMonitor();
        bool active = !exiting && !disposed && settings.MouseClickThrough && !startupMaskActive && startupRevealStarted && IsWindowVisible(hwnd);
        if (hoverMonitorActive != active)
        {
            hoverMonitorActive = active;
            GlassProfile.Event(active ? "hover-monitor-start" : "hover-monitor-stop");
        }
        if (!active)
        {
            hoverDragTimer?.Stop();
            hoverDragSinceTick = 0;
            ResetHoverHold();
            HideHoverEffect();
            if (hoverDragArmed) DisarmHoverDrag(false);
            return;
        }
        if (hoverDragTimer == null)
        {
            hoverDragTimer = DispatcherQueue.CreateTimer();
            hoverDragTimer.Interval = TimeSpan.FromMilliseconds(100);
            hoverDragTimer.Tick += (_, __) => OnHoverDragTick();
        }
        hoverDragTimer.Start();
    }

    bool IsCursorOverWidget()
    {
        if (!GetCursorPos(out Win32Point cursor) || !GetClientRect(hwnd, out NativeClientRect client)) return false;
        var origin = new Win32Point();
        if (!ClientToScreen(hwnd, ref origin)) return false;
        const int inset = 2;
        if (cursor.X < origin.X + inset || cursor.X >= origin.X + client.Right - inset ||
            cursor.Y < origin.Y + inset || cursor.Y >= origin.Y + client.Bottom - inset) return false;
        // WindowFromPoint skips this click-through widget. If the window it does
        // return is above us, the widget is visually covered and must not take input.
        IntPtr under = WindowFromPoint(cursor);
        if (under != IntPtr.Zero)
        {
            IntPtr cover = GetAncestor(under, GA_ROOT);
            if (cover == IntPtr.Zero) cover = under;
            if (cover != hwnd && trackingCoins)
            {
                GetWindowThreadProcessId(cover, out uint ownerProcess);
                var name = new System.Text.StringBuilder(128);
                GetClassName(cover, name, name.Capacity);
                if (ownerProcess == (uint)Environment.ProcessId && name.ToString().Contains("PopupWindowSiteBridge", StringComparison.Ordinal)) return true;
            }
            if (cover != hwnd)
            {
                for (IntPtr window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero; window = GetWindow(window, GW_HWNDNEXT))
                {
                    if (window == hwnd) break;
                    if (window == cover) return false;
                }
            }
        }
        return true;
    }

    void OnHoverDragTick()
    {
        if (exiting || disposed || !settings.MouseClickThrough || startupMaskActive || !IsWindowVisible(hwnd))
        {
            UpdateHoverDragMonitor();
            return;
        }
        if (isDragging) return;
        bool inside = IsCursorOverWidget();
        if (!inside)
        {
            hoverDragSinceTick = 0;
            ResetHoverHold();
            HideHoverEffect();
            if (hoverDragArmed && !isDragging) DisarmHoverDrag();
            return;
        }
        if (hoverDragArmed)
        {
            if (uiSettings.AnimationsEnabled)
            {
                if (!hoverEffectVisible)
                    ShowHoverEffect(HoverEffects.Normalize(settings.HoverEffect), Environment.TickCount64 - HoverDragDelaySeconds * 1000L);
                SetHoverEffectReady();
            }
            return;
        }
        long now = Environment.TickCount64;
        if (!GetCursorPos(out Win32Point cursor))
        {
            hoverDragSinceTick = 0;
            ResetHoverHold();
            HideHoverEffect();
            return;
        }
        bool moved = hoverHoldSinceTick != 0 &&
            ((cursor.X - hoverSampleX) * (cursor.X - hoverSampleX) + (cursor.Y - hoverSampleY) * (cursor.Y - hoverSampleY) > HoverMoveSamplePx * HoverMoveSamplePx ||
             (cursor.X - hoverHoldX) * (cursor.X - hoverHoldX) + (cursor.Y - hoverHoldY) * (cursor.Y - hoverHoldY) > HoverHoldDriftPx * HoverHoldDriftPx);
        if (hoverHoldSinceTick == 0 || moved)
        {
            bool wasCounting = hoverDragSinceTick != 0;
            hoverHoldSinceTick = now;
            hoverHoldX = hoverSampleX = cursor.X;
            hoverHoldY = hoverSampleY = cursor.Y;
            hoverDragSinceTick = 0;
            HideHoverEffect();
            GlassProfile.Event(moved ? "hover-hold-reset" : "hover-hold-start");
            if (wasCounting) GlassProfile.Event("hover-drag-count-cancel");
            return;
        }
        hoverSampleX = cursor.X;
        hoverSampleY = cursor.Y;
        if (now - hoverHoldSinceTick < HoverHoldDelayMs)
        {
            hoverDragSinceTick = 0;
            HideHoverEffect();
            return;
        }
        if (hoverDragSinceTick == 0)
        {
            hoverDragSinceTick = now;
            GlassProfile.Event("hover-drag-count-start");
        }
        EnsureHoverEffect(hoverDragSinceTick);
        if (now - hoverDragSinceTick >= HoverDragDelaySeconds * 1000L) ArmHoverDrag();
    }

    void ArmHoverDrag()
    {
        if (hoverDragArmed || !settings.MouseClickThrough || exiting || disposed) return;
        hoverDragArmed = true;
        hoverDragSinceTick = 0;
        ResetHoverHold();
        // Drop only the input mask; the click-through setting stays enabled, so
        // the next leave/drag-end can hand mouse input back to the desktop.
        ApplyMouseClickThrough();
        if (uiSettings.AnimationsEnabled)
        {
            if (!hoverEffectVisible)
                ShowHoverEffect(HoverEffects.Normalize(settings.HoverEffect), Environment.TickCount64 - HoverDragDelaySeconds * 1000L);
            SetHoverEffectReady();
        }
        GlassProfile.Event("hover-drag-armed");
    }

    void DisarmHoverDrag(bool applyStyle = true)
    {
        if (!hoverDragArmed) return;
        hoverDragArmed = false;
        hoverDragSinceTick = 0;
        ResetHoverHold();
        HideHoverEffect();
        GlassProfile.Event("hover-drag-disarmed");
        if (applyStyle && settings.MouseClickThrough && !startupMaskActive && !exiting && !disposed)
            ApplyMouseClickThrough();
    }

    void HideWidget()
    {
        Diag("hide");
        CancelPolarCoinAction(true);
        hoverDragArmed = false;
        hoverDragSinceTick = 0;
        ResetHoverHold();
        HideHoverEffect();
        hoverDragTimer?.Stop();
        native.Hide();
        desktopSource.Pause();
        timer.Interval = TimeSpan.FromSeconds(1);
    }

    void BuildHoverEffect()
    {
        hoverEffectRoot.RenderTransform = hoverEffectTransform;
        hoverEffectTransform.ScaleX = hoverEffectTransform.ScaleY = (float)PhysicalScale;
        hoverEffectHalo.Center = new Point(.5, .5);
        hoverEffectHalo.GradientOrigin = new Point(.5, .5);
        hoverEffectHalo.RadiusX = hoverEffectHalo.RadiusY = .5;
        foreach (double offset in new[] { 0d, .35, .62, .84, 1d })
            hoverEffectHalo.GradientStops.Add(new GradientStop { Offset = offset });

        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        hoverHaloVisual = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 92, Height = 92, Fill = hoverEffectHalo, Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(.5, .5), RenderTransform = hoverHaloScale };
        Canvas.SetLeft(hoverHaloVisual, 14); Canvas.SetTop(hoverHaloVisual, 14);
        hoverEffectRoot.Children.Add(hoverHaloVisual);

        hoverReadyRingOuter = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 56, Height = 56, Stroke = hoverEffectCompanion, StrokeThickness = 4.5,
            Fill = transparent, Opacity = 0, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = hoverReadyOuterScale
        };
        Canvas.SetLeft(hoverReadyRingOuter, 32); Canvas.SetTop(hoverReadyRingOuter, 32);
        hoverEffectRoot.Children.Add(hoverReadyRingOuter);

        hoverReadyRing = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 42, Height = 42, Stroke = hoverEffectAccent, StrokeThickness = 1.3,
            Fill = transparent, Opacity = 0, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = hoverReadyScale
        };
        Canvas.SetLeft(hoverReadyRing, 39); Canvas.SetTop(hoverReadyRing, 39);
        hoverEffectRoot.Children.Add(hoverReadyRing);

        hoverRipple1Scale = new ScaleTransform();
        hoverRipple1 = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 52, Height = 52, Stroke = hoverEffectCompanion, StrokeThickness = 3.4,
            Fill = transparent, Opacity = 0, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = hoverRipple1Scale
        };
        Canvas.SetLeft(hoverRipple1, 34); Canvas.SetTop(hoverRipple1, 34);
        hoverEffectRoot.Children.Add(hoverRipple1);

        hoverRipple2Scale = new ScaleTransform();
        hoverRipple2 = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 52, Height = 52, Stroke = hoverEffectAccent, StrokeThickness = 1.1,
            Fill = transparent, Opacity = 0, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = hoverRipple2Scale
        };
        Canvas.SetLeft(hoverRipple2, 34); Canvas.SetTop(hoverRipple2, 34);
        hoverEffectRoot.Children.Add(hoverRipple2);

        hoverOrbitTrack = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 46, Height = 46, Stroke = hoverOrbitRingBrush, StrokeThickness = 1,
            Fill = transparent, Opacity = 0, IsHitTestVisible = false
        };
        Canvas.SetLeft(hoverOrbitTrack, 37); Canvas.SetTop(hoverOrbitTrack, 37);
        hoverEffectRoot.Children.Add(hoverOrbitTrack);

        hoverOrbitProgress = CreateOrbitArc(20, hoverOrbitInk, 2.4, out hoverOrbitProgressFigure, out hoverOrbitProgressSegment);
        hoverEffectRoot.Children.Add(hoverOrbitProgress);

        hoverOrbitTrailSoft = CreateOrbitTrail(out hoverOrbitTrailSoftFigure, out hoverOrbitTrailSoftSegment);
        hoverEffectRoot.Children.Add(hoverOrbitTrailSoft);

        hoverOrbitTrail = CreateOrbitTrail(out hoverOrbitTrailFigure, out hoverOrbitTrailSegment);
        hoverEffectRoot.Children.Add(hoverOrbitTrail);

        hoverOrbitGlowBrush.Center = new Point(.5, .5);
        hoverOrbitGlowBrush.GradientOrigin = new Point(.5, .5);
        hoverOrbitGlowBrush.RadiusX = hoverOrbitGlowBrush.RadiusY = .5;
        hoverOrbitGlowBrush.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(232, 255, 255, 255) });
        hoverOrbitGlowBrush.GradientStops.Add(new GradientStop { Offset = .34, Color = Windows.UI.Color.FromArgb(110, 255, 255, 255) });
        hoverOrbitGlowBrush.GradientStops.Add(new GradientStop { Offset = .68, Color = Windows.UI.Color.FromArgb(28, 255, 255, 255) });
        hoverOrbitGlowBrush.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, 255, 255, 255) });
        hoverOrbitGlow = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 28, Height = 28, Fill = hoverOrbitGlowBrush, Opacity = 0, IsHitTestVisible = false
        };
        Canvas.SetLeft(hoverOrbitGlow, 46); Canvas.SetTop(hoverOrbitGlow, 46);
        hoverEffectRoot.Children.Add(hoverOrbitGlow);

        hoverOrbitHead = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 5.6, Height = 5.6, Fill = hoverOrbitHeadBrush, Opacity = 0, IsHitTestVisible = false
        };
        Canvas.SetLeft(hoverOrbitHead, 57.2); Canvas.SetTop(hoverOrbitHead, 57.2);
        hoverEffectRoot.Children.Add(hoverOrbitHead);

        var auraA = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.45, .4), RadiusX = .5, RadiusY = .5 };
        auraA.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(86, 234, 245, 255) });
        auraA.GradientStops.Add(new GradientStop { Offset = .55, Color = Windows.UI.Color.FromArgb(28, 234, 245, 255) });
        auraA.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, 234, 245, 255) });
        var auraB = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        auraB.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(92, 159, 212, 255) });
        auraB.GradientStops.Add(new GradientStop { Offset = .6, Color = Windows.UI.Color.FromArgb(26, 159, 212, 255) });
        auraB.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, 159, 212, 255) });
        var auraC = new RadialGradientBrush { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
        auraC.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(70, 255, 217, 168) });
        auraC.GradientStops.Add(new GradientStop { Offset = .6, Color = Windows.UI.Color.FromArgb(20, 255, 217, 168) });
        auraC.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, 255, 217, 168) });

        hoverAuroraA = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 110, Height = 110, Fill = auraA, Opacity = 0, IsHitTestVisible = false };
        hoverAuroraB = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 96, Height = 96, Fill = auraB, Opacity = 0, IsHitTestVisible = false };
        hoverAuroraC = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 72, Height = 72, Fill = auraC, Opacity = 0, IsHitTestVisible = false };
        Canvas.SetLeft(hoverAuroraA, 4); Canvas.SetTop(hoverAuroraA, 4);
        Canvas.SetLeft(hoverAuroraB, 6); Canvas.SetTop(hoverAuroraB, 6);
        Canvas.SetLeft(hoverAuroraC, 48); Canvas.SetTop(hoverAuroraC, 46);

        var auraLayer = new Canvas { Width = 120, Height = 120, RenderTransformOrigin = new Point(.5, .5), IsHitTestVisible = false };
        var auraGroup = new TransformGroup();
        auraGroup.Children.Add(hoverAuroraScale);
        auraGroup.Children.Add(hoverAuroraRotate);
        auraLayer.RenderTransform = auraGroup;
        auraLayer.Children.Add(hoverAuroraA);
        auraLayer.Children.Add(hoverAuroraB);
        auraLayer.Children.Add(hoverAuroraC);
        hoverAuroraLayer = auraLayer;
        hoverEffectRoot.Children.Insert(1, auraLayer);

        double auraStart = 200 * Math.PI / 180, auraEnd = 320 * Math.PI / 180, auraRadius = 28;
        var crescentFigure = new PathFigure
        {
            StartPoint = new Point(60 + auraRadius * Math.Cos(auraStart), 60 + auraRadius * Math.Sin(auraStart)),
            IsClosed = false
        };
        crescentFigure.Segments.Add(new ArcSegment
        {
            Size = new Size(auraRadius, auraRadius), SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false, Point = new Point(60 + auraRadius * Math.Cos(auraEnd), 60 + auraRadius * Math.Sin(auraEnd))
        });
        var crescentGeometry = new PathGeometry(); crescentGeometry.Figures.Add(crescentFigure);
        hoverAuroraCrescent = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = crescentGeometry, Stroke = hoverEffectCompanion, StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Fill = transparent, Opacity = 0, IsHitTestVisible = false
        };
        hoverEffectRoot.Children.Add(hoverAuroraCrescent);

        var bloom = new Storyboard();
        AddHoverWave(bloom, hoverRipple1, hoverRipple1Scale, 0, .16, 1300);
        AddHoverWave(bloom, hoverRipple2, hoverRipple2Scale, 240, .42, 1300);
        hoverBloomStoryboard = bloom;

        hoverEffectLayer.Children.Add(hoverEffectRoot);
        surface.Children.Add(hoverEffectLayer);
        UpdateHoverEffectTheme();
    }

    static Microsoft.UI.Xaml.Shapes.Path CreateOrbitArc(double radius, Brush stroke, double thickness, out PathFigure? figure, out ArcSegment? segment)
    {
        figure = new PathFigure { StartPoint = new Point(60, 60 - radius), IsClosed = false };
        segment = new ArcSegment
        {
            Size = new Size(radius, radius), SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = false, Point = new Point(60, 60 - radius)
        };
        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry, Stroke = stroke, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Opacity = 0, IsHitTestVisible = false
        };
    }

    static Microsoft.UI.Xaml.Shapes.Path CreateOrbitTrail(out PathFigure? figure, out PolyLineSegment? segment)
    {
        figure = new PathFigure { StartPoint = new Point(60, 40), IsClosed = true, IsFilled = true };
        segment = new PolyLineSegment();
        // Reuse the strip's points on each frame instead of allocating geometry.
        for (int i = 0; i < 65; i++) segment.Points.Add(new Point(60, 40));
        figure.Segments.Add(segment);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var fill = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute };
        foreach (double offset in new[] { 0d, .3, .65, 1d })
            fill.GradientStops.Add(new GradientStop { Offset = offset });
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Width = 120, Height = 120, Data = geometry, Fill = fill,
            Opacity = 0, IsHitTestVisible = false
        };
    }

    static void AddHoverWave(Storyboard storyboard, Microsoft.UI.Xaml.Shapes.Ellipse ripple, ScaleTransform scale, double delayMs, double peakOpacity, double durationMs)
    {
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var growEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (string property in new[] { "ScaleX", "ScaleY" })
        {
            var grow = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
            grow.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = .9, EasingFunction = growEase });
            grow.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(durationMs), Value = 2.3, EasingFunction = growEase });
            Storyboard.SetTarget(grow, scale); Storyboard.SetTargetProperty(grow, property);
            storyboard.Children.Add(grow);
        }
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = duration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0 });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(90), Value = peakOpacity, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(540), Value = peakOpacity * .45, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        fade.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(durationMs), Value = 0, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        Storyboard.SetTarget(fade, ripple); Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);
    }

    void UpdateHoverEffectTheme()
    {
        bool dark = surface.ActualTheme == ElementTheme.Dark;
        if (surface.ActualTheme == ElementTheme.Default) dark = !settings.AutomaticAppearance && settings.NightMode;
        bool pale = !dark && (desktopSource is null || !desktopSource.IsReady || desktopSource.Luminance > .62);
        if (hoverEffectDark == dark && hoverEffectPale == pale) return;
        hoverEffectDark = dark;
        hoverEffectPale = pale;
        Windows.UI.Color accent = dark
            ? Windows.UI.Color.FromArgb(214, 255, 255, 255)
            : Windows.UI.Color.FromArgb(238, 255, 255, 255);
        Windows.UI.Color companion;
        Windows.UI.Color halo;
        int[] alphas;
        if (dark)
        {
            companion = Windows.UI.Color.FromArgb(140, 220, 235, 250);
            halo = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            alphas = new[] { 28, 12, 4, 0, 0 };
        }
        else if (pale)
        {
            companion = Windows.UI.Color.FromArgb(255, 70, 86, 108);
            halo = companion;
            alphas = new[] { 100, 48, 20, 5, 0 };
        }
        else
        {
            companion = Windows.UI.Color.FromArgb(255, 124, 142, 159);
            halo = companion;
            alphas = new[] { 48, 22, 8, 2, 0 };
        }
        hoverEffectAccent.Color = accent;
        hoverEffectCompanion.Color = companion;
        if (hoverReadyRing != null) hoverReadyRing.Stroke = pale ? hoverEffectCompanion : hoverEffectAccent;
        var orbitColor = dark ? Windows.UI.Color.FromArgb(255, 222, 241, 255) : Windows.UI.Color.FromArgb(255, 130, 190, 242);
        hoverOrbitInk.Color = orbitColor;
        hoverOrbitRingBrush.Color = Windows.UI.Color.FromArgb(dark ? (byte)100 : (byte)135, 174, 213, 246);
        hoverOrbitHeadBrush.Color = dark ? Microsoft.UI.Colors.White : Windows.UI.Color.FromArgb(255, 210, 236, 255);
        foreach (var path in new[] { hoverOrbitTrail, hoverOrbitTrailSoft })
        {
            if (path?.Fill is not LinearGradientBrush fill) continue;
            int[] opacity = { 0, 30, 130, 245 };
            for (int i = 0; i < opacity.Length; i++)
                fill.GradientStops[i].Color = Windows.UI.Color.FromArgb((byte)opacity[i], orbitColor.R, orbitColor.G, orbitColor.B);
        }
        if (hoverEffectStyle == HoverEffects.Orbit)
        {
            if (hoverReadyRing != null) hoverReadyRing.Stroke = hoverOrbitRingBrush;
            if (hoverReadyRingOuter != null) hoverReadyRingOuter.Stroke = hoverOrbitRingBrush;
        }
        if (hoverOrbitGlowBrush.GradientStops.Count == 4)
        {
            Windows.UI.Color glow = orbitColor;
            int[] glowAlphas = { 210, 90, 20, 0 };
            for (int i = 0; i < 4; i++)
                hoverOrbitGlowBrush.GradientStops[i].Color = Windows.UI.Color.FromArgb((byte)glowAlphas[i], glow.R, glow.G, glow.B);
        }
        if (hoverEffectHalo.GradientStops.Count == 5)
        {
            for (int i = 0; i < 5; i++)
                hoverEffectHalo.GradientStops[i].Color = Windows.UI.Color.FromArgb((byte)alphas[i], halo.R, halo.G, halo.B);
        }
    }

    void ApplyHoverEffectStyle(bool ready)
    {
        string style = hoverEffectStyle;
        if (hoverReadyRing != null) hoverReadyRing.Stroke = style == HoverEffects.Orbit ? hoverOrbitRingBrush : hoverEffectPale ? hoverEffectCompanion : hoverEffectAccent;
        if (hoverReadyRingOuter != null) hoverReadyRingOuter.Stroke = style == HoverEffects.Orbit ? hoverOrbitRingBrush : hoverEffectCompanion;
        if (hoverReadyRing != null) hoverReadyRing.Opacity = 0;
        if (hoverReadyRingOuter != null) hoverReadyRingOuter.Opacity = 0;
        hoverReadyScale.ScaleX = hoverReadyScale.ScaleY = 1;
        hoverReadyOuterScale.ScaleX = hoverReadyOuterScale.ScaleY = 1;
        if (hoverRipple2 != null) hoverRipple2.Visibility = style == HoverEffects.Halo ? Visibility.Collapsed : Visibility.Visible;
        if (ready) return;
        if (hoverHaloVisual != null) hoverHaloVisual.Opacity = .12;
        hoverHaloScale.ScaleX = hoverHaloScale.ScaleY = 1;
        if (hoverRipple1 != null) { hoverRipple1.Opacity = 0; if (hoverRipple1Scale != null) hoverRipple1Scale.ScaleX = hoverRipple1Scale.ScaleY = 1; }
        if (hoverRipple2 != null) { hoverRipple2.Opacity = 0; if (hoverRipple2Scale != null) hoverRipple2Scale.ScaleX = hoverRipple2Scale.ScaleY = 1; }
    }

    void ResetHoverEffectVisuals()
    {
        if (hoverReadyRing != null) hoverReadyRing.Opacity = 0;
        if (hoverReadyRingOuter != null) hoverReadyRingOuter.Opacity = 0;
        if (hoverOrbitTrack != null) hoverOrbitTrack.Opacity = 0;
        if (hoverOrbitProgress != null) hoverOrbitProgress.Opacity = 0;
        if (hoverOrbitTrail != null) hoverOrbitTrail.Opacity = 0;
        if (hoverOrbitTrailSoft != null) hoverOrbitTrailSoft.Opacity = 0;
        if (hoverOrbitHead != null) hoverOrbitHead.Opacity = 0;
        if (hoverOrbitGlow != null) hoverOrbitGlow.Opacity = 0;
        if (hoverAuroraA != null) hoverAuroraA.Opacity = 0;
        if (hoverAuroraB != null) hoverAuroraB.Opacity = 0;
        if (hoverAuroraC != null) hoverAuroraC.Opacity = 0;
        if (hoverAuroraCrescent != null) hoverAuroraCrescent.Opacity = 0;
        hoverAuroraScale.ScaleX = hoverAuroraScale.ScaleY = 1;
        hoverAuroraRotate.Angle = 0;
        hoverReadyScale.ScaleX = hoverReadyScale.ScaleY = 1;
        hoverReadyOuterScale.ScaleX = hoverReadyOuterScale.ScaleY = 1;
        if (hoverRipple1 != null) { hoverRipple1.Opacity = 0; if (hoverRipple1Scale != null) hoverRipple1Scale.ScaleX = hoverRipple1Scale.ScaleY = 1; }
        if (hoverRipple2 != null) { hoverRipple2.Opacity = 0; if (hoverRipple2Scale != null) hoverRipple2Scale.ScaleX = hoverRipple2Scale.ScaleY = 1; }
        if (hoverHaloVisual != null) hoverHaloVisual.Opacity = .12;
        hoverHaloScale.ScaleX = hoverHaloScale.ScaleY = 1;
        hoverEffectRoot.Opacity = 0;
    }

    void ShowHoverEffect(string style, long startTick)
    {
        hoverEffectStyle = HoverEffects.Normalize(style);
        hoverEffectStartTick = startTick;
        hoverEffectReady = false;
        hoverBloomStoryboard?.Stop();
        UpdateHoverEffectTheme();
        ResetHoverEffectVisuals();
        ApplyHoverEffectStyle(false);
        hoverEffectTransform.ScaleX = hoverEffectTransform.ScaleY = (float)PhysicalScale;
        if (TryGetHoverPoint(out Point point))
        {
            hoverEffectTransform.TranslateX = point.X - 60;
            hoverEffectTransform.TranslateY = point.Y - 60;
        }
        hoverEffectLayer.Visibility = Visibility.Visible;
        hoverEffectVisible = true;
        if (!hoverEffectLoopActive)
        {
            hoverEffectLoopActive = true;
            CompositionTarget.Rendering += AnimateHoverEffect;
        }
    }

    void HideHoverEffect()
    {
        if (!hoverEffectVisible && hoverEffectLayer.Visibility == Visibility.Collapsed) return;
        hoverEffectVisible = false;
        hoverEffectReady = false;
        if (hoverEffectLoopActive)
        {
            CompositionTarget.Rendering -= AnimateHoverEffect;
            hoverEffectLoopActive = false;
        }
        hoverBloomStoryboard?.Stop();
        hoverEffectLayer.Visibility = Visibility.Collapsed;
        ResetHoverEffectVisuals();
    }

    void EnsureHoverEffect(long startTick)
    {
        if (!uiSettings.AnimationsEnabled)
        {
            HideHoverEffect();
            return;
        }
        string style = HoverEffects.Normalize(settings.HoverEffect);
        if (!hoverEffectVisible || hoverEffectStyle != style) ShowHoverEffect(style, startTick);
    }

    void SetHoverEffectReady()
    {
        if (!hoverEffectVisible || hoverEffectReady) return;
        hoverEffectReady = true;
        hoverEffectReadyAt = Environment.TickCount64;
        UpdateHoverEffectTheme();
        ApplyHoverEffectStyle(true);
        if (hoverRipple1 != null) { hoverRipple1.Opacity = 0; if (hoverRipple1Scale != null) hoverRipple1Scale.ScaleX = hoverRipple1Scale.ScaleY = .9; }
        if (hoverRipple2 != null) { hoverRipple2.Opacity = 0; if (hoverRipple2Scale != null) hoverRipple2Scale.ScaleX = hoverRipple2Scale.ScaleY = .9; }
        hoverBloomStoryboard?.Stop();
        if (hoverEffectStyle == HoverEffects.Orbit || hoverEffectStyle == HoverEffects.Aurora)
        {
            if (hoverRipple1 != null) { hoverRipple1.Opacity = 0; if (hoverRipple1Scale != null) hoverRipple1Scale.ScaleX = hoverRipple1Scale.ScaleY = 1; }
            if (hoverRipple2 != null) { hoverRipple2.Opacity = 0; if (hoverRipple2Scale != null) hoverRipple2Scale.ScaleX = hoverRipple2Scale.ScaleY = 1; }
            if (hoverOrbitProgress != null) hoverOrbitProgress.Opacity = 0;
        }
        else
        {
            hoverBloomStoryboard?.Begin();
        }
    }

    void AnimateHoverEffect(object? sender, object args)
    {
        if (disposed || !hoverEffectVisible || !settings.MouseClickThrough || startupMaskActive || exiting || !IsWindowVisible(hwnd))
        {
            HideHoverEffect();
            return;
        }
        if (!TryGetHoverPoint(out Point point) || point.X < 0 || point.Y < 0 || point.X > surface.ActualWidth || point.Y > surface.ActualHeight)
        {
            HideHoverEffect();
            return;
        }
        if (hoverEffectReady)
        {
            hoverEffectTransform.TranslateX = point.X - 60;
            hoverEffectTransform.TranslateY = point.Y - 60;
        }
        long now = Environment.TickCount64;
        UpdateHoverEffectTheme();
        hoverEffectRoot.Opacity = Math.Min(1, Math.Max(0, (now - hoverEffectStartTick) / 180.0));
        string style = hoverEffectStyle;
        if (!hoverEffectReady)
        {
            double progress = Math.Clamp((now - hoverEffectStartTick) / (double)(HoverDragDelaySeconds * 1000), 0, 1);
            if (style == HoverEffects.Halo)
            {
                if (hoverHaloVisual != null) hoverHaloVisual.Opacity = .12 + .4 * progress;
            }
            else if (style == HoverEffects.Orbit)
            {
                if (hoverOrbitTrack != null) hoverOrbitTrack.Opacity = .4;
                UpdateOrbit(-Math.PI / 2 + progress * Math.PI * 2, 1.1 + .4 * progress, .5 + .5 * progress);
                UpdateOrbitProgress(progress);
                if (hoverHaloVisual != null) hoverHaloVisual.Opacity = 0;
            }
            else
            {
                hoverAuroraScale.ScaleX = hoverAuroraScale.ScaleY = .82 + .18 * progress;
                hoverAuroraRotate.Angle = progress * 26;
                if (hoverAuroraA != null) hoverAuroraA.Opacity = .22 + .4 * progress;
                if (hoverAuroraB != null) hoverAuroraB.Opacity = .22 + .42 * progress;
                if (hoverAuroraC != null) hoverAuroraC.Opacity = .18 + .36 * progress;
                if (hoverAuroraCrescent != null) hoverAuroraCrescent.Opacity = .06 + .2 * progress;
                if (hoverHaloVisual != null) hoverHaloVisual.Opacity = hoverEffectPale ? .05 + .03 * progress : 0;
            }
        }
        else
        {
            double readyElapsed = now - hoverEffectReadyAt;

            double appear = 1 - Math.Pow(1 - Math.Clamp(readyElapsed / 340.0, 0, 1), 3);
            double breath = .5 + .5 * Math.Sin(readyElapsed / 2600.0 * Math.PI * 2);
            if (hoverReadyRing != null)
            {
                hoverReadyRing.Opacity = .46 * appear * (.9 + .1 * breath);
                hoverReadyScale.ScaleX = hoverReadyScale.ScaleY = .92 + .08 * appear + .012 * breath;
            }
            if (hoverReadyRingOuter != null)
            {
                hoverReadyRingOuter.Opacity = .16 * appear * (.7 + .3 * breath);
                hoverReadyOuterScale.ScaleX = hoverReadyOuterScale.ScaleY = .95 + .05 * appear + .03 * breath;
            }
            if (hoverHaloVisual != null)
            {
                double from = style == HoverEffects.Halo ? .52 : .30;
                double to = style == HoverEffects.Halo ? .62 : .44;
                hoverHaloVisual.Opacity = from + (to - from) * appear + .03 * breath;
                hoverHaloScale.ScaleX = hoverHaloScale.ScaleY = 1 + .025 * appear + .015 * breath;
            }
            if (style == HoverEffects.Orbit)
            {
                // Keep the light orbiting while its tail recedes to a point and grows back.
                double tailWave = Math.Clamp((.5 + .5 * Math.Cos(readyElapsed / 3000.0 * Math.PI * 2) - .12) / .88, 0, 1);
                double tailLength = 1.7 * tailWave * tailWave * (3 - 2 * tailWave);
                UpdateOrbit(-Math.PI / 2 + readyElapsed * .0017, tailLength, .9 * appear + .08 * breath);
                if (hoverOrbitProgress != null) hoverOrbitProgress.Opacity = 0;
                if (hoverOrbitTrack != null) hoverOrbitTrack.Opacity = .4 * appear + .04 * breath;
                if (hoverReadyRing != null) hoverReadyRing.Opacity = .32 * appear * (.9 + .1 * breath);
                if (hoverReadyRingOuter != null) hoverReadyRingOuter.Opacity = .14 * appear * (.7 + .3 * breath);
                if (hoverHaloVisual != null) hoverHaloVisual.Opacity = 0;
            }
            else if (style == HoverEffects.Aurora)
            {
                if (hoverReadyRing != null) hoverReadyRing.Opacity = (hoverEffectPale ? .1 : .04) * appear * (.9 + .1 * breath);
                if (hoverReadyRingOuter != null) hoverReadyRingOuter.Opacity = (hoverEffectPale ? .06 : .03) * appear;
                if (hoverHaloVisual != null) hoverHaloVisual.Opacity = hoverEffectPale ? .07 + .02 * breath : 0;
                hoverAuroraScale.ScaleX = hoverAuroraScale.ScaleY = 1.05 + .06 * breath;
                hoverAuroraRotate.Angle = readyElapsed * .012;
                if (hoverAuroraA != null) hoverAuroraA.Opacity = .65 * appear + .06 * breath;
                if (hoverAuroraB != null) hoverAuroraB.Opacity = .6 * appear + .05 * breath;
                if (hoverAuroraC != null) hoverAuroraC.Opacity = .5 * appear + .05 * breath;
                if (hoverAuroraCrescent != null) hoverAuroraCrescent.Opacity = .22 * appear + .04 * breath;
            }
        }
    }

    void UpdateOrbit(double angle, double trail, double headOpacity)
    {
        if (hoverOrbitHead == null || hoverOrbitGlow == null) return;
        double x = 60 + 20 * Math.Cos(angle);
        double y = 60 + 20 * Math.Sin(angle);
        Canvas.SetLeft(hoverOrbitHead, x - hoverOrbitHead.Width / 2);
        Canvas.SetTop(hoverOrbitHead, y - hoverOrbitHead.Height / 2);
        Canvas.SetLeft(hoverOrbitGlow, x - hoverOrbitGlow.Width / 2);
        Canvas.SetTop(hoverOrbitGlow, y - hoverOrbitGlow.Height / 2);
        hoverOrbitHead.Opacity = headOpacity;
        hoverOrbitGlow.Opacity = headOpacity * (hoverEffectPale ? .55 : .9);
        UpdateOrbitTrail(hoverOrbitTrailFigure, hoverOrbitTrailSegment, hoverOrbitTrail, angle, trail, headOpacity * .95, 3.8);
        UpdateOrbitTrail(hoverOrbitTrailSoftFigure, hoverOrbitTrailSoftSegment, hoverOrbitTrailSoft, angle, trail, headOpacity * .2, 7);
    }

    static void UpdateOrbitTrail(PathFigure? figure, PolyLineSegment? segment, Microsoft.UI.Xaml.Shapes.Path? path, double angle, double trail, double opacity, double width)
    {
        if (figure == null || segment == null || path == null) return;
        if (trail < .02 || opacity < .005)
        {
            path.Opacity = 0;
            return;
        }
        double start = angle - trail;
        figure.StartPoint = new Point(60 + 20 * Math.Cos(start), 60 + 20 * Math.Sin(start));
        for (int i = 1; i <= 32; i++)
        {
            double t = i / 32.0, at = start + trail * t;
            double radius = 20 + width * .5 * Math.Pow(t, .85);
            segment.Points[i - 1] = new Point(60 + radius * Math.Cos(at), 60 + radius * Math.Sin(at));
        }
        for (int i = 32; i >= 0; i--)
        {
            double t = i / 32.0, at = start + trail * t;
            double radius = 20 - width * .5 * Math.Pow(t, .85);
            segment.Points[64 - i] = new Point(60 + radius * Math.Cos(at), 60 + radius * Math.Sin(at));
        }
        if (path.Fill is LinearGradientBrush fill)
        {
            fill.StartPoint = figure.StartPoint;
            fill.EndPoint = new Point(60 + 20 * Math.Cos(angle), 60 + 20 * Math.Sin(angle));
        }
        path.Opacity = opacity;
    }

    void UpdateOrbitProgress(double progress)
    {
        if (hoverOrbitProgress == null || hoverOrbitProgressFigure == null || hoverOrbitProgressSegment == null) return;
        progress = Math.Clamp(progress, 0, 1);
        double end = -Math.PI / 2 + progress * Math.PI * 2;
        hoverOrbitProgressFigure.StartPoint = new Point(60, 40);
        hoverOrbitProgressSegment.Point = new Point(60 + 20 * Math.Cos(end), 60 + 20 * Math.Sin(end));
        hoverOrbitProgressSegment.IsLargeArc = progress > .5;
        hoverOrbitProgress.Opacity = progress > .02 ? .18 : 0;
    }

    bool TryGetHoverPoint(out Point point)
    {
        point = default;
        var origin = new Win32Point();
        if (!GetCursorPos(out Win32Point cursor) || !ClientToScreen(hwnd, ref origin)) return false;
        double dpi = Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
        point = new Point((cursor.X - origin.X) / dpi, (cursor.Y - origin.Y) / dpi);
        return true;
    }

    void ToggleMouseClickThrough()
    {
        if (exiting || disposed) return;
        settings.MouseClickThrough = !settings.MouseClickThrough;
        hoverDragArmed = false;
        hoverDragSinceTick = 0;
        ResetHoverHold();
        ApplyMouseClickThrough();
        UpdateHoverDragMonitor();
        settingsWindow?.RefreshMouseClickThrough(settings.MouseClickThrough);
        Save(true);
    }

    void ShowWidget()
    {
        if (exiting || disposed) return;
        Diag("show");
        native.Show();
        desktopSource.Resume();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        ApplyMouseClickThrough();
        UpdateHoverDragMonitor();
        Activate();
        try
        {
            SetForegroundWindow(hwnd);
            SetWindowPos(hwnd, new IntPtr(settings.Topmost ? -1 : -2) /* HWND_TOPMOST / HWND_NOTOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 /* SWP_NOSIZE | SWP_NOMOVE */);
        }
        catch { }
    }

    void CreateTray(string path)
    {
        tray = new NativeTrayIcon(hwnd, path, ShowWidget, OpenSettings, () => { exitReason = "tray-menu"; Exit(); },
            ToggleMouseClickThrough, () => settings.MouseClickThrough);
    }

    static Settings CloneSettings(Settings s)
    {
        return new Settings
        {
            Start = s.Start,
            End = s.End,
            LunchEnabled = s.LunchEnabled,
            LunchStart = s.LunchStart,
            LunchEnd = s.LunchEnd,
            WorkScheduleMode = s.WorkScheduleMode,
            WorkSchedules = s.WorkSchedules != null ? s.WorkSchedules.ConvertAll(p => new WorkScheduleProfile { Mode = p.Mode, Start = p.Start, End = p.End, SaturdayEnabled = p.SaturdayEnabled, SaturdayStart = p.SaturdayStart, SaturdayEnd = p.SaturdayEnd, DayTimes = p.DayTimes != null ? p.DayTimes.ConvertAll(d => new DayWorkTime { Day = d.Day, Enabled = d.Enabled, Start = d.Start, End = d.End }) : new System.Collections.Generic.List<DayWorkTime>() }) : new System.Collections.Generic.List<WorkScheduleProfile>(),
            Salary = 0, // privacy protected
            WorkDays = s.WorkDays,
            PayDay = s.PayDay,
            Topmost = s.Topmost,
            MouseClickThrough = s.MouseClickThrough,
            HoverDragSeconds = s.HoverDragSeconds,
            HoverEffect = HoverEffects.Normalize(s.HoverEffect),
            NightMode = s.NightMode,
            AutomaticAppearance = s.AutomaticAppearance,
            PetId = s.PetId,
            UseChinaHolidays = s.UseChinaHolidays,
            RestDays = s.RestDays,
            SingleRestDays = s.SingleRestDays,
            DoubleRestDays = s.DoubleRestDays,
            AlternatingWeekends = s.AlternatingWeekends,
            DoubleRestWeekStart = s.DoubleRestWeekStart,
            HideAmount = true,
            ShowSalaryLayout = s.ShowSalaryLayout,
            ShowCoinJar = s.ShowCoinJar,
            CoinJarX = s.CoinJarX,
            CoinJarY = s.CoinJarY,
            WishEnabled = s.WishEnabled,
            Wishes = s.Wishes != null ? s.Wishes.ConvertAll(w => new WishEntry { Id = w.Id, Name = w.Name, Target = w.Target, Saved = w.Saved }) : new System.Collections.Generic.List<WishEntry>(),
            WishAllocation = s.WishAllocation,
            WishStartDate = s.WishStartDate,
            WishName = s.WishName,
            WishTarget = s.WishTarget,
            WishSaved = s.WishSaved,
            AmountVisibleSeconds = s.AmountVisibleSeconds,
            WaterReminder = s.WaterReminder,
            WalkReminder = s.WalkReminder,
            ReminderPopup = s.ReminderPopup,
            ReminderPosition = s.ReminderPosition,
            ReminderX = s.ReminderX,
            ReminderY = s.ReminderY,
            ReminderWidth = s.ReminderWidth,
            ReminderHeight = s.ReminderHeight,
            WaterMinutes = s.WaterMinutes,
            WalkMinutes = s.WalkMinutes,
            Scale = s.Scale,
            ScaleReferenceVersion = s.ScaleReferenceVersion,
            X = s.X,
            Y = s.Y,
            DateAdjustments = s.DateAdjustments != null ? s.DateAdjustments.ConvertAll(a => new DateAdjustment { Date = a.Date, IsWorkday = a.IsWorkday }) : new System.Collections.Generic.List<DateAdjustment>()
        };
    }

    static readonly object saveLock = new object();
    long saveRevision;

    void WriteSettingsSnapshot(Settings snapshot, string folder, long revision, bool throwOnFailure = false)
    {
        try
        {
            lock (saveLock)
            {
                if (revision < System.Threading.Volatile.Read(ref saveRevision)) return;
                string path = Path.Combine(folder, "settings.xml");
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var writer = new StreamWriter(temp, false, new System.Text.UTF8Encoding(true)))
                        new XmlSerializer(typeof(Settings)).Serialize(writer, snapshot);
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                }
                finally
                {
                    try { File.Delete(temp); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("保存设置失败：" + ex.Message);
            if (throwOnFailure) throw;
        }
    }

    void Save(bool synchronous = false)
    {
        if (smoke) return;
        long revision = System.Threading.Interlocked.Increment(ref saveRevision);
        var snapshot = CloneSettings(settings);
        if (synchronous) WriteSettingsSnapshot(snapshot, dataFolder, revision, true);
        else _ = Task.Run(() => WriteSettingsSnapshot(snapshot, dataFolder, revision));
    }

    void Exit()
    {
        if (exiting) return; exiting = true;
        Diag("exit reason=" + exitReason);
        // XAML/capture teardown must never leave a ghost process behind.
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(3000);
            Diag("watchdog force-exit");
            Environment.Exit(0);
        }) { IsBackground = true, Name = "ExitWatchdog" };
        watchdog.Start();
        try
        {
            EndDrag(null);
            RememberWindowPosition();
            if (!smoke)
            {
                long revision = System.Threading.Interlocked.Increment(ref saveRevision);
                var snapshot = CloneSettings(settings);
                WriteSettingsSnapshot(snapshot, dataFolder, revision);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        { Debug.WriteLine("退出时未能保存设置；继续释放应用资源。"); }
        finally
        {
            try { settingsWindow?.Close(); }
            finally
            {
                try { DisposeResources(); }
                finally { try { Close(); } finally { Application.Current.Exit(); } }
            }
        }
    }

    WinUIEx.Messaging.WindowMessageMonitor? renderEraseMonitor;
    IntPtr eraseRenderHandle;

    bool disposed;
    void DisposeResources()
    {
        if (disposed) return;
        try { EndDrag(null); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
        disposed = true;
        coinBankTimer?.Stop();
        hoverDragArmed = false;
        ResetHoverHold();
        HideHoverEffect();
        hoverDragTimer?.Stop();
        dragReleaseTimer?.Stop();
        CloseCoinLayer();
        ClearAmbientShopEffects();
        petAnimationRevision++;
        if (petEnterHook != IntPtr.Zero) { UnhookWindowsHookEx(petEnterHook); petEnterHook = IntPtr.Zero; }
        petEnterHookProc = null;
        petHoverTimer?.Stop(); CancelSlack(false); slackAnimation?.Dispose(); slackAnimation = null; petAnimation?.Dispose(); petAnimation = null;
        startWorkRevision++; startWorkActive = false;
        startWorkAnimation?.Dispose(); startWorkAnimation = null;
        coinPetActive = false;
        coinPetAnimation?.Dispose(); coinPetAnimation = null;
        reminderAnimationRevision++;
        reminderAnimation?.Dispose(); reminderAnimation = null;
        CloseReminderPopup();
        reminderPetActive = false; pendingReminders = reminderDue = 0;

        layoutTransitionActive = false; layoutTransitionClock.Stop(); CompositionTarget.Rendering -= AnimateLayoutTransition;
        timer?.Stop();
        startupRevealTimer?.Stop();
        scalePreviewSettleTimer?.Stop();
        walkTimer?.Stop();
        smokeTimer?.Stop();
        if (sessionNotificationRegistered) { WTSUnRegisterSessionNotification(hwnd); sessionNotificationRegistered = false; }
        sessionMonitor?.Dispose();
        tray?.Dispose();
        widgetMenu?.Shutdown(); widgetMenu = null;
        installerExitWait?.Unregister(null);
        installerExit?.Dispose();
        activationWait?.Unregister(null);
        activation?.Dispose();
        // Keep the instance handle until process teardown so installers wait for all mapped files.
        desktopSource.Dispose();
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(surface).Clip = null;
        surfaceGeometry?.Dispose(); surfaceGeometry = null;
        renderEraseMonitor?.Dispose();
        statsAppearanceClock.Stop(); textAppearanceClock.Stop(); glassAppearanceClock.Stop();
        stats.Background = null;
        glass.Dispose();
        privacy.EffectParametersChanged -= QueueAmbientPreview;
        privacy.Dispose();
        GlassProfile.Shutdown();
    }

    [StructLayout(LayoutKind.Sequential)] struct NativeClientRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out NativeClientRect rect);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    delegate IntPtr SUBCLASSPROC(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hwnd, SUBCLASSPROC callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")] static extern IntPtr DefSubclassProcCursor(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Win32Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref NativeClientRect rect, IntPtr brush);
    [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int index);
    [StructLayout(LayoutKind.Sequential)] struct Win32Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct GlassMargins { internal int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref GlassMargins margins);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Win32Point lpPoint);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    const int VK_LBUTTON = 0x01;
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Win32Point point);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr hwnd, string name, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_CAPTION = 0x00C00000;
    const int WS_THICKFRAME = 0x00040000;
    const int WS_MINIMIZEBOX = 0x00020000;
    const int WS_MAXIMIZEBOX = 0x00010000;
    const int WS_SYSMENU = 0x00080000;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_APPWINDOW = 0x00040000;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const uint GA_ROOT = 2;
    const uint GW_HWNDNEXT = 2;
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        internal int Size;
        internal NativeClientRect MonitorRect;
        internal NativeClientRect WorkRect;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Win32Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}

static class InputCursors
{
    internal static readonly InputCursor Hand = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}

sealed class HandCursorGrid : Grid
{
    internal HandCursorGrid() => ProtectedCursor = InputCursors.Hand;
}

sealed class InteractiveCursorGrid : Grid
{
    internal bool Interactive { set => ProtectedCursor = value ? InputCursors.Hand : null; }
}
