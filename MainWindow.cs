using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
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

public sealed class MainWindow : Window
{
    Settings settings = new Settings();
    double? previewScale;
    bool scalePreviewHostActive, preparingScalePreviewHost;
    int scalePreviewWidth, scalePreviewHeight;
    int scalePreviewInsetX, scalePreviewInsetY, scalePreviewInsetWidth, scalePreviewInsetHeight;
    const double ScaleReference = .75;
    double EffectiveScale => previewScale ?? settings.Scale;
    double PhysicalScale => Math.Clamp(EffectiveScale, .5, 1.5) * ScaleReference;
    readonly string dataFolder;
    readonly bool smoke;
    readonly PrivacyStore privacy;
    readonly ChinaHolidayCalendar calendar;
    readonly Grid surface = new Grid();
    readonly Grid hostSurface = new Grid();
    Microsoft.UI.Composition.CompositionRoundedRectangleGeometry? surfaceGeometry;
    readonly Border card = new Border { CornerRadius = new CornerRadius(36), Margin = new Thickness(0) };
    readonly Border backdropCard = new Border();
    readonly Canvas foregroundLayer = new Canvas();
    readonly ScaleTransform contentScale = new ScaleTransform();
    readonly Border notice;
    readonly Canvas coins = new Canvas { IsHitTestVisible = false };
    readonly TextBlock caption = new TextBlock { FontSize = 14, CharacterSpacing = 15, FontFamily = new FontFamily("Microsoft YaHei"), FontWeight = Microsoft.UI.Text.FontWeights.Normal };
    readonly TextBlock countdown = new TextBlock { FontSize = 36, FontWeight = Microsoft.UI.Text.FontWeights.Medium, FontFamily = new FontFamily("Segoe UI"), Margin = new Thickness(0, 4, 0, 10) };
    readonly TextBlock rest = new TextBlock(), pay = new TextBlock(), earned = new TextBlock();
    readonly System.Collections.Generic.List<TextBlock> statTitles = new();


    readonly Button earningsEye = new Button { Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)), MinWidth = 0, MinHeight = 0 };
    readonly FontIcon earningsVisibilityIcon = new() { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Glyph = "\uED1A", IsTextScaleFactorEnabled = false };
    readonly SolidColorBrush eyeBackground = new(Windows.UI.Color.FromArgb(16, 0, 0, 0));
    readonly SolidColorBrush eyeHoverBackground = new(Windows.UI.Color.FromArgb(32, 0, 0, 0));
    readonly SolidColorBrush eyePressedBackground = new(Windows.UI.Color.FromArgb(48, 0, 0, 0));
    readonly StackPanel salaryArea = new StackPanel { Spacing = 5 };
    readonly Grid statsRow = new Grid();
    readonly Border stats = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(-8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Image pet = new Image { Width = 132, Height = 150, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom };
    readonly TextBlock reminder = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 240 };
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
    readonly Random random = new Random();
    readonly AppWindow native;
    readonly GlassBackdrop glass;
    readonly DesktopGlassSource desktopSource;
    double pixelScale = 1;
    readonly IntPtr hwnd;
    NativeTrayIcon? tray;
    WinUIEx.Messaging.WindowMessageMonitor? sessionMonitor;
    bool sessionNotificationRegistered;
    double nextSessionRegistrationAttempt;
    SettingsWindow? settingsWindow;
    Mutex? instance;
    EventWaitHandle? activation;
    RegisteredWaitHandle? activationWait;
    EventWaitHandle? installerExit;
    RegisteredWaitHandle? installerExitWait;
    DateTime revealedUntil = DateTime.MaxValue, reminderUntil;
    DateTime holidayDay;
    bool exiting;
    double lightness = 1, textFromTone = 1, textTargetTone = 1;
    bool textAppearanceInitialized;
    readonly Stopwatch textAppearanceClock = new Stopwatch();
    readonly Stopwatch glassAppearanceClock = new Stopwatch();
    bool glassAppearanceInitialized;
    double glassTone, glassFromTone, glassTargetTone, glassTint, glassFromTint, glassTargetTint;
    bool analysisRegionsDirty = true;
    int walkingFrame, petRevision;
    Microsoft.UI.Xaml.Media.ImageSource[]? walkingFrames;
    readonly DispatcherQueueTimer walkTimer;

    public MainWindow()
    {
        smoke = Environment.GetCommandLineArgs().Contains("--smoke-test");
        try { timeBeginPeriod(1); } catch { }
        dataFolder = Environment.GetEnvironmentVariable("OFFWORKWIDGET_TEST_DATA") ?? (smoke ? Path.Combine(Path.GetTempPath(), "OffWorkWidget-WinUI-smoke") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OffWorkWidget"));
        Directory.CreateDirectory(dataFolder);
        GlassProfile.Configure(dataFolder);
        Title = "下班倒计时 · 液体玻璃";
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        native = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        string instanceKey = Environment.UserName + (Environment.GetEnvironmentVariable("OFFWORKWIDGET_TEST_DATA") == null ? "" : "-WinUI-Isolated");
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
            installerExitWait = ThreadPool.RegisterWaitForSingleObject(installerExit, (_, __) => DispatcherQueue.TryEnqueue(Exit), null, Timeout.Infinite, false);
        }
        string config = Path.Combine(dataFolder, "settings.xml");
        if (File.Exists(config))
        {
            try
            {
                using var stream = File.OpenRead(config);
                var loaded = (Settings?)new XmlSerializer(typeof(Settings)).Deserialize(stream);
                if (loaded == null) throw new InvalidDataException("设置文件内容为空。");
                if (loaded.Scale > 1.5 && loaded.Scale <= 2) loaded.Scale = 1.5; // Preserve existing settings from the former range.
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
                settings = new Settings();
                Debug.WriteLine("设置文件无法读取，已使用默认设置；隐私数据库保持独立。");
            }
        }
        calendar = new ChinaHolidayCalendar(Path.Combine(dataFolder, "holidays"));
        settings.Calendar = calendar;
        privacy = new PrivacyStore(Path.Combine(dataFolder, "privacy.db"), settings.Salary);
        settings.Salary = privacy.LoadSalary(); settings.HideAmount = true;
        if (smoke)
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("--smoke-scale=") && double.TryParse(arg.Substring(14), NumberStyles.Float, CultureInfo.InvariantCulture, out double size)) settings.Scale = Math.Clamp(size, .5, 1.5);
                if (arg == "--smoke-dark") { settings.AutomaticAppearance = false; settings.NightMode = true; }
            }
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);
        Content = hostSurface;
        hostSurface.Children.Add(surface);
        hostSurface.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        surface.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        surface.Children.Add(backdropCard);
        surface.Children.Add(foregroundLayer);
        foregroundLayer.Children.Add(card);
        card.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        BuildCard();
        surface.Children.Add(coins);
        coins.RenderTransform = contentScale;
        notice = new Border { Child = reminder, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 6, 12, 6), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(235, 38, 38, 42)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 10, 0, 0), Visibility = Visibility.Collapsed };
        notice.RenderTransform = contentScale;
        reminder.Foreground = new SolidColorBrush(Colors.White);
        surface.Children.Add(notice);

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
        try { native.IsShownInSwitchers = false; } catch { }
        try
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
            style |= WS_POPUP;
            SetWindowLong(hwnd, GWL_STYLE, style);

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        }
        catch { }
        string icon = Path.Combine(AppContext.BaseDirectory, "assets", "app-icon.ico");
        if (File.Exists(icon)) native.SetIcon(icon);
        glass = GlassBackdrop.Apply(this, backdropCard, surface);
        surface.Loaded += (_, __) => EnsureRenderEraseMonitor();
        desktopSource = new DesktopGlassSource(this, surface, backdropCard);
        desktopSource.FrameUpdated = UpdateAppearance;
        desktopSource.MetricsUpdated = UpdateAppearance;
        desktopSource.BeforeRender = () => { UpdateDragFrame(); UpdateScalePreviewBounds(); UpdateStatsTransition(); UpdateTextTransition(); UpdateGlassTransition(); };
        surface.Loaded += (_, __) => UpdateRenderViewport();
        surface.LayoutUpdated += (_, __) => analysisRegionsDirty = true;
        caption.SizeChanged += (_, __) => analysisRegionsDirty = true;
        countdown.SizeChanged += (_, __) => analysisRegionsDirty = true;
        stats.SizeChanged += (_, __) => analysisRegionsDirty = true;
        native.Changed += (_, args) => { if (args.DidSizeChange) UpdateRenderViewport(); };
        ApplySettings();

        surface.RightTapped += (_, e) => ShowContextMenu(e.GetPosition(surface));
        card.PointerPressed += OnCardPointerPressed;
        card.PointerMoved += OnCardPointerMoved;
        card.PointerExited += (_, __) => glass.SetInteraction(0, 0, false);
        card.PointerReleased += OnCardPointerReleased;
        card.PointerCaptureLost += OnCardPointerCaptureLost;
        card.PointerCanceled += OnCardPointerCaptureLost;
        Activated += (_, args) => { if (args.WindowActivationState == WindowActivationState.Deactivated) { EndDrag(null); glass.SetInteraction(0, 0, false); } };
        surface.DoubleTapped += (_, __) => OpenSettings();
        native.Closing += (_, e) => { if (!exiting) { EndDrag(null); e.Cancel = true; native.Hide(); } };
        Closed += (_, __) => DisposeResources();
        sessionMonitor = new WinUIEx.Messaging.WindowMessageMonitor(hwnd);
        sessionMonitor.WindowMessageReceived += OnSessionChanged;
        sessionMonitor.WindowMessageReceived += OnLayoutHitTest;
        TryRegisterSessionNotifications();

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, __) =>
        {
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
            Refresh();
            UpdateAppearance();
            notice.Visibility = DateTime.Now < reminderUntil ? Visibility.Visible : Visibility.Collapsed;
        };
        walkTimer = DispatcherQueue.CreateTimer();
        walkTimer.Interval = TimeSpan.FromMilliseconds(140);
        walkTimer.Tick += (_, __) =>
        {
            if (DateTime.Now >= reminderUntil) { walkTimer.Stop(); LoadPet(); return; }
            if (walkingFrames != null) pet.Source = walkingFrames[(walkingFrame++) % walkingFrames.Length];
        };
        timer.Start();
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
    }

    static void Add(MenuFlyout menu, string title, Action action)
    {
        var item = new MenuFlyoutItem { Text = title };
        item.Click += (_, __) => action();
        menu.Items.Add(item);
    }

    MenuFlyout CreateWidgetMenu()
    {
        // Popup theme resources may retain the app theme, so set the menu palette explicitly.
        ElementTheme theme = settings.AutomaticAppearance ? surface.ActualTheme
            : settings.NightMode ? ElementTheme.Dark : ElementTheme.Light;
        bool dark = theme == ElementTheme.Dark;
        bool highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var systemColors = new Windows.UI.ViewManagement.UISettings();
        var background = highContrast ? systemColors.UIElementColor(Windows.UI.ViewManagement.UIElementType.Window)
            : dark ? Windows.UI.Color.FromArgb(255, 39, 39, 42) : Windows.UI.Color.FromArgb(255, 250, 250, 252);
        var foreground = highContrast ? systemColors.UIElementColor(Windows.UI.ViewManagement.UIElementType.WindowText)
            : dark ? Windows.UI.Color.FromArgb(255, 242, 242, 247) : Windows.UI.Color.FromArgb(255, 28, 28, 30);
        var hover = highContrast ? systemColors.UIElementColor(Windows.UI.ViewManagement.UIElementType.Highlight)
            : dark ? Windows.UI.Color.FromArgb(255, 60, 60, 65) : Windows.UI.Color.FromArgb(255, 232, 232, 237);
        var border = highContrast ? foreground : dark ? Windows.UI.Color.FromArgb(255, 68, 68, 74) : Windows.UI.Color.FromArgb(255, 218, 218, 224);
        var ink = new SolidColorBrush(foreground);
        var hoverInk = highContrast ? new SolidColorBrush(systemColors.UIElementColor(Windows.UI.ViewManagement.UIElementType.HighlightText)) : ink;
        var menu = new MenuFlyout();
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(background)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, ink));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(border)));
        menu.MenuFlyoutPresenterStyle = style;
        menu.Opening += (_, __) =>
        {
            foreach (var item in menu.Items)
            {
                item.RequestedTheme = theme;
                item.Foreground = ink;
                item.Resources["MenuFlyoutItemBackgroundPointerOver"] = new SolidColorBrush(hover);
                item.Resources["MenuFlyoutItemBackgroundPressed"] = new SolidColorBrush(hover);
                item.Resources["MenuFlyoutItemForegroundPointerOver"] = hoverInk;
                item.Resources["MenuFlyoutItemForegroundPressed"] = hoverInk;
            }
        };
        return menu;
    }
    void BuildCard()
    {
        var body = new Grid { Margin = new Thickness(24, 18, 12, 18), ColumnSpacing = 20 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        caption.Foreground = foreground; countdown.Foreground = foreground;
        text.Children.Add(caption); text.Children.Add(countdown);
        var row = statsRow;
        for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var restColumn = Stat("距休息", rest); var payColumn = Stat("距发薪", pay);
        restColumn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(payColumn, 1); row.Children.Add(restColumn); row.Children.Add(payColumn);
        var salaryHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var salaryTitle = new TextBlock { Text = "今天赚了", FontFamily = new FontFamily("Microsoft YaHei"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = statsForeground, VerticalAlignment = VerticalAlignment.Center };
        statTitles.Add(salaryTitle);
        salaryHeader.Children.Add(salaryTitle);
        earningsVisibilityIcon.Foreground = statsForeground;
        earningsEye.Content = earningsVisibilityIcon;
        earningsEye.Foreground = statsForeground;
        earningsEye.Background = eyeBackground;
        earningsEye.CornerRadius = new CornerRadius(100);
        earningsEye.Resources["ButtonBackgroundPointerOver"] = eyeHoverBackground;
        earningsEye.Resources["ButtonBackgroundPressed"] = eyePressedBackground;
        earningsEye.Resources["ButtonForegroundPointerOver"] = statsForeground;
        earningsEye.Resources["ButtonForegroundPressed"] = statsForeground;
        earningsEye.Click += async (_, __) =>
        {
            earningsEye.IsEnabled = false;
            try { if (settings.HideAmount) await RevealSalary(surface.XamlRoot); else HideSalary(); }
            catch (Exception ex) { App.RecordFailure(ex); HideSalary(); }
            finally { earningsEye.IsEnabled = true; }
        };
        salaryHeader.Children.Add(earningsEye);
        salaryArea.Children.Add(salaryHeader); salaryArea.Children.Add(new Border { Child = earned });
        earned.FontSize = 20; earned.FontFamily = new FontFamily("Segoe UI"); earned.FontWeight = Microsoft.UI.Text.FontWeights.Medium; earned.Foreground = statsForeground;
        Grid.SetColumn(salaryArea, 2); row.Children.Add(salaryArea);
        foreach (FrameworkElement column in row.Children) column.HorizontalAlignment = HorizontalAlignment.Center;


        stats.Background = statsGlass; stats.Child = row;
        stats.SizeChanged += (_, __) => SynchronizeStatsGeometry();
        text.Children.Add(stats);
        body.Children.Add(text);
        Grid.SetColumn(pet, 1); body.Children.Add(pet);
        card.Child = body;
        pet.Tapped += (_, e) => { EmitCoin(); e.Handled = true; };
        pet.RenderTransformOrigin = new Point(.5, 1);
        var petTransform = new CompositeTransform();
        pet.RenderTransform = petTransform;
        pet.PointerEntered += (_, __) => { petTransform.ScaleX = 1.035; petTransform.ScaleY = 1.035; };
        pet.PointerExited += (_, __) => { petTransform.ScaleX = 1; petTransform.ScaleY = 1; };
        pet.RightTapped += (_, e) => { ShowContextMenu(e.GetPosition(surface)); e.Handled = true; };
        var breathe = new DoubleAnimation { From = 0, To = -2, Duration = new Duration(TimeSpan.FromSeconds(2.8)), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(breathe, petTransform); Storyboard.SetTargetProperty(breathe, "TranslateY");
        var breathing = new Storyboard(); breathing.Children.Add(breathe);
        pet.Loaded += (_, __) => breathing.Begin();
        ToolTipService.SetToolTip(pet, "点击爆金币 · 双击卡片打开设置");
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

    void UpdateRestTypography()
    {
        bool unset = rest.Text == "未设置";
        bool weekday = rest.Text.Length == 2 && rest.Text[0] == '周';
        double logicalSize = unset ? 18 : weekday ? 20 : 22;
        double dpi = Math.Max(96u, GetDpiForWindow(hwnd)) / 96d;
        rest.FontSize = Math.Round(pay.FontSize * logicalSize / 22 * dpi) / dpi;
        rest.FontWeight = unset ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.Medium;
    }
    static string NormalizePet(string id) => id == "cat" ? "cow-cat" : id == "samoyed" ? "corgi" : id == "red-panda" || id == "cow-cat" || id == "corgi" || id == "panda" || id == "polar-bear" ? id : "cow-cat";

    void PreviewPet(string id)
    {
        id = NormalizePet(id);
        petRevision++; walkTimer?.Stop(); settings.PetId = id; LoadPet();
        if (DateTime.Now < reminderUntil) _ = StartWalk();
    }

    void LoadPet() { pet.Source = new BitmapImage(new Uri("ms-appx:///assets/" + settings.PetId + ".png")); }

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

    void ApplySettings()
    {
        settings.Calendar = calendar;
        settings.PetId = NormalizePet(settings.PetId);
        ApplyWidgetSize();
        ApplyMouseClickThrough();
        LoadPet(); reminders.Reset(); Refresh();
    }

    void PreviewScale(double value)
    {
        if (disposed || exiting || !double.IsFinite(value)) return;
        if (layoutTransitionActive) CompleteLayoutTransition();
        if (!scalePreviewHostActive) BeginScalePreviewHost();
        previewScale = Math.Clamp(value, .5, 1.5);
        ApplyWidgetSize(); Refresh();
    }

    void ApplyWidgetSize()
    {
        if (layoutHostActive) FinishLayoutHost();
        if (!previewScale.HasValue) EndScalePreviewHost();
        layoutTransitionActive = false; layoutTransitionClock.Stop(); CompositionTarget.Rendering -= AnimateLayoutTransition; salaryArea.Opacity = 1;
        shownWidgetWidth = 368; // Re-measure at the current scale instead of retaining a wider preview.
        double scale = PhysicalScale;
        surface.Width = WidgetLogicalWidth; surface.Height = 190;
        card.Width = WidgetLogicalWidth * scale; card.Height = 190 * scale;
        card.RenderTransformOrigin = new Point(0, 0);
        card.RenderTransform = null;
        ApplyTypography();
        card.HorizontalAlignment = HorizontalAlignment.Left; card.VerticalAlignment = VerticalAlignment.Top;
        contentScale.ScaleX = contentScale.ScaleY = scale;
        backdropCard.CornerRadius = new CornerRadius(36 * scale);
        notice.Margin = new Thickness(16 * scale, 10 * scale, 0, 0);
        glass.Brush.BlurAmount = 0;
        surface.HorizontalAlignment = HorizontalAlignment.Left; surface.VerticalAlignment = VerticalAlignment.Top;
        pixelScale = GetDpiForWindow(hwnd) / 96.0 * scale;
        int winW = (int)Math.Ceiling(WidgetLogicalWidth * pixelScale);
        int winH = (int)Math.Ceiling(190 * pixelScale);
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
            posX = area.X + area.Width - winW - 35;
            posY = area.Y + 60;
        }
        else
        {
            // Clamp to screen work area to prevent disappearing off-screen
            posX = Math.Clamp(posX, area.X, Math.Max(area.X, area.X + area.Width - winW));
            posY = Math.Clamp(posY, area.Y, Math.Max(area.Y, area.Y + area.Height - winH));
        }
        native.Move(new PointInt32(posX, posY));
    }

    void BeginScalePreviewHost()
    {
        if (!desktopSource.TryGetHostRenderBounds(out int x, out int y, out int width, out int height)) return;
        scalePreviewInsetX = x - native.Position.X; scalePreviewInsetY = y - native.Position.Y;
        scalePreviewInsetWidth = native.Size.Width - width; scalePreviewInsetHeight = native.Size.Height - height;
        scalePreviewWidth = native.Size.Width; scalePreviewHeight = native.Size.Height;
        scalePreviewHostActive = true; preparingScalePreviewHost = true;
        desktopSource.BeginVisualDrag(x, y, width, height);
        try
        {
            // Allocate once; the ruler only changes XAML and the precise region.
            double dpi = GetDpiForWindow(hwnd) / 96.0;
            native.Resize(new SizeInt32((int)Math.Ceiling(Math.Max(368, shownWidgetWidth) * 1.5 * ScaleReference * dpi),
                (int)Math.Ceiling(190 * 1.5 * ScaleReference * dpi)));
            ClearTransparentHostBackground();
        }
        finally { preparingScalePreviewHost = false; }
    }

    void EndScalePreviewHost()
    {
        if (!scalePreviewHostActive) return;
        scalePreviewHostActive = false;
        desktopSource.EndVisualDrag();
    }

    void UpdateScalePreviewBounds()
    {
        if (!scalePreviewHostActive || preparingScalePreviewHost) return;
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
        scalePreviewWidth = width; scalePreviewHeight = height;
        double dpi = GetDpiForWindow(hwnd) / 96.0;
        pixelScale = dpi * PhysicalScale;
        SetRenderGeometry(Math.Max(1, width - scalePreviewInsetWidth), Math.Max(1, height - scalePreviewInsetHeight),
            dpi, PhysicalScale);
        UpdateScalePreviewBounds();
        desktopSource.SynchronizeVisualFrame();
        UpdateSurfaceClip();
    }

    void ApplyTypography()
    {
        double scale = PhysicalScale, dpi = GetDpiForWindow(hwnd) / 96.0;
        double Size(double logical) => Math.Round(logical * scale * dpi) / dpi;
        card.UseLayoutRounding = true;
        caption.FontSize = Size(17); caption.CharacterSpacing = 0;
        caption.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        countdown.FontSize = Size(36); countdown.Margin = new Thickness(0, Size(4), 0, Size(8));
        foreach (var heading in statTitles) { heading.FontSize = Size(15); heading.FontWeight = Microsoft.UI.Text.FontWeights.Normal; heading.IsTextScaleFactorEnabled = false; }
        foreach (var value in new[] { rest, pay, earned })
        {
            value.FontSize = Size(22); value.IsTextScaleFactorEnabled = false;
            value.LineHeight = 0; value.LineStackingStrategy = LineStackingStrategy.MaxHeight;
            value.TextLineBounds = TextLineBounds.Tight;
            value.VerticalAlignment = VerticalAlignment.Center;
            if (value.Parent is Border valueSlot) valueSlot.Height = Size(30);
        }
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
        foreach (StackPanel column in statsRow.Children) column.Spacing = Size(5);
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
            foreach (FrameworkElement column in statsRow.Children)
            {
                column.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                widest = Math.Max(widest, column.DesiredSize.Width);
            }
            shownWidgetWidth = Math.Max(shownWidgetWidth, Math.Ceiling((widest + 2 / (GetDpiForWindow(hwnd) / 96.0)) * 3 / PhysicalScale + 168));
        }
        double scale = GetDpiForWindow(hwnd) / 96.0 * PhysicalScale;
        var size = new SizeInt32((int)Math.Ceiling(WidgetLogicalWidth * scale), (int)Math.Ceiling(190 * scale));
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
        glass.SynchronizeEdge(radius, GetDpiForWindow(hwnd) / 96.0, PhysicalScale);
        glass.Brush.ShapeRadius = Math.Clamp(radius / Math.Max(1, Math.Min(surface.Width, surface.Height) * .5 - 1), 0, 1);
    }

    void SynchronizeStatsGeometry()
    {
        statsGlass.ShapeRadius = Math.Clamp(stats.CornerRadius.TopLeft / Math.Max(1, Math.Min(stats.ActualWidth, stats.ActualHeight) * .5 - 1), 0, 1);
    }

    void UpdateSurfaceClip()
    {
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
            int right = left + Math.Max(1, width), bottom = top + Math.Max(1, height);
            // HRGN edges are binary. A real outward guard preserves the compositor's
            // partial-coverage pixels, including the right/bottom raster boundary.
            const int guard = 4;
            SetProp(hwnd, "OffWorkWidget.GlassDiameterPx", new IntPtr(diameter));
            SetProp(hwnd, "OffWorkWidget.GlassGuardPx", new IntPtr(guard));
            IntPtr region = CreateRoundRectRgn(left - guard, top - guard, right + guard, bottom + guard,
                diameter + guard * 2, diameter + guard * 2);
            if (region != IntPtr.Zero)
            {
                if (SetWindowRgn(hwnd, region, true) == 0)
                    DeleteObject(region); // Windows owns the region only after success.
                else
                    ClearTransparentHostBackground(); // Also initialize newly revealed resize/animation pixels.
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
        if (!work) { caption.Text = "今天休息，好好放松"; countdown.Text = "休息中"; }
        else if (now.TimeOfDay < start) { caption.Text = "距离上班还有"; left = start - now.TimeOfDay; }
        else if (now.TimeOfDay < end) { caption.Text = "距离下班还有"; left = end - now.TimeOfDay; }
        else { caption.Text = "今天辛苦了，自由时间到"; countdown.Text = "已下班"; }
        bool showStatus = !left.HasValue;
        if (countdownIsStatus != showStatus)
        {
            countdownIsStatus = showStatus;
            countdown.FontFamily = new FontFamily(countdownIsStatus ? "Microsoft YaHei" : "Segoe UI");
            countdown.FontWeight = countdownIsStatus ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Medium;
        }
        if (left.HasValue) countdown.Text = string.Format(CultureInfo.InvariantCulture, "{0:00} : {1:00} : {2:00}", (int)left.Value.TotalHours, left.Value.Minutes, left.Value.Seconds);
        int days = Logic.UntilRest(now, settings);
        bool restStarted = Logic.RestStarted(now, settings, days);
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
        earned.Text = settings.HideAmount ? "***元" : Logic.Earned(now, settings).ToString("F2", CultureInfo.InvariantCulture) + "元";
        earningsVisibilityIcon.Glyph = settings.HideAmount ? "\uED1A" : "\uE890";
        string eyeAction = settings.HideAmount ? "显示金额（需验证密码）" : "隐藏金额";
        ToolTipService.SetToolTip(earningsEye, eyeAction);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(earningsEye, eyeAction);
        UpdateSalaryLayout();
        FitCaptionToColumn();
        settingsWindow?.RefreshSalaryLayoutButton();
        int due = reminders.Tick(now, elapsed.Elapsed, settings);
        if (due != 0) { reminder.Text = due == 3 ? "喝口水，也站起来走走吧" : due == 1 ? "喝口水，照顾好自己" : "站起来走走，放松一下"; reminderUntil = now.AddSeconds(20); _ = StartWalk(); }
        if (!smoke && settings.UseChinaHolidays && holidayDay != now.Date) { holidayDay = now.Date; _ = calendar.RefreshAsync(now.Year, false); }
    }

    async Task StartWalk()
    {
        try
        {
            int revision = petRevision; string petId = settings.PetId;
            string path = Path.Combine(AppContext.BaseDirectory, "assets", petId + "-walk.png");
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            var frames = new Microsoft.UI.Xaml.Media.ImageSource[4];
            for (uint i = 0; i < 4; i++)
            {
                var transform = new Windows.Graphics.Imaging.BitmapTransform { Bounds = new Windows.Graphics.Imaging.BitmapBounds { X = (i % 2) * decoder.PixelWidth / 2, Y = (i / 2) * decoder.PixelHeight / 2, Width = decoder.PixelWidth / 2, Height = decoder.PixelHeight / 2 } };
                using var bitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, transform, Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
                var source = new SoftwareBitmapSource(); await source.SetBitmapAsync(bitmap); frames[i] = source;
            }
            if (exiting || disposed || revision != petRevision || petId != settings.PetId) return;
            walkingFrames = frames; walkingFrame = 0; walkTimer.Start();
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
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
            double manual = settings.NightMode ? 1.0 : 0.0;
            textTarget = manual;
            glassTarget = manual;
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

        double tintTarget = settings.AutomaticAppearance ? 0 : .54;
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
        if (surface.RequestedTheme != theme) surface.RequestedTheme = theme;
        if (hostSurface.RequestedTheme != theme) hostSurface.RequestedTheme = theme;
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
        glass.SetTone(glassTone);
        double tintRGB = 255 * (1 - glassTone);
        if (Math.Abs(glass.Brush.TintR - tintRGB) > .5) glass.Brush.TintR = glass.Brush.TintG = glass.Brush.TintB = tintRGB;
        if (Math.Abs(glass.Brush.TintA - glassTint) > .001) glass.Brush.TintA = glassTint;
        var fallback = Windows.UI.Color.FromArgb(200, (byte)Math.Round(245 - 205 * glassTone), (byte)Math.Round(245 - 205 * glassTone), (byte)Math.Round(248 - 204 * glassTone));
        if (glassFallback.Color != fallback) glassFallback.Color = fallback;
        glass.UpdateInteraction(uiSettings.AnimationsEnabled);
        if (Math.Abs(statsGlass.GlareAngle - glass.Brush.GlareAngle) > .01) statsGlass.GlareAngle = glass.Brush.GlareAngle;
        double innerGlare = 4 + .3 * (glass.Brush.GlareFactor - 18);
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
    }

    void UpdateStatsTransition()
    {
        if (disposed || !statsAppearanceInitialized) return;
        double progress = statsAppearanceClock.IsRunning && uiSettings.AnimationsEnabled ? Math.Clamp(statsAppearanceClock.Elapsed.TotalMilliseconds / 320, 0, 1) : 1;
        double eased = progress * progress * (3 - 2 * progress);
        statsTone = statsFromTone + (statsTargetTone - statsFromTone) * eased;
        if (progress >= 1) statsAppearanceClock.Stop();
        byte Mix(double light, double dark) => (byte)Math.Round(light + (dark - light) * statsTone);
        var ink = Windows.UI.Color.FromArgb(255, Mix(24, 255), Mix(26, 255), Mix(30, 255));
        if (statsForeground.Color != ink) statsForeground.Color = ink;
        byte eyeTone = Mix(0, 255);
        var eyeColor = Windows.UI.Color.FromArgb(16, eyeTone, eyeTone, eyeTone);
        if (eyeBackground.Color != eyeColor) eyeBackground.Color = eyeColor;
        var hoverColor = Windows.UI.Color.FromArgb(36, eyeTone, eyeTone, eyeTone);
        if (eyeHoverBackground.Color != hoverColor) eyeHoverBackground.Color = hoverColor;
        var pressedColor = Windows.UI.Color.FromArgb(52, eyeTone, eyeTone, eyeTone);
        if (eyePressedBackground.Color != pressedColor) eyePressedBackground.Color = pressedColor;
        var tint = Windows.UI.Color.FromArgb(210, Mix(245, 40), Mix(245, 40), Mix(248, 44));
        if (statsBackground.Color != tint) statsBackground.Color = tint;
        if (statsGlass.FallbackColor != tint) statsGlass.FallbackColor = tint;
        if (Math.Abs(statsGlass.TintR - tint.R) > .5) { statsGlass.TintR = tint.R; statsGlass.TintG = tint.G; statsGlass.TintB = tint.B; }
        double alpha = .42 - .06 * statsTone;
        if (Math.Abs(statsGlass.TintA - alpha) > .001) statsGlass.TintA = alpha;
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
                salaryArea.Opacity = 1; FinishLayoutHost(); Refresh();
            }
        }
        Save();
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
        salaryArea.Opacity = layoutProgress * layoutProgress;
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
        // XAML has now produced one frame at the animated width; safe to reveal
        // the full host while the rounded clip still owns the visible edge.
        if (layoutFrameCount == 0) UpdateLayoutHostClip(layoutHostX, layoutHostWidth);
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
        sessionMonitor = new WinUIEx.Messaging.WindowMessageMonitor(hwnd);
        sessionMonitor.WindowMessageReceived += OnSessionChanged;
        sessionMonitor.WindowMessageReceived += OnLayoutHitTest;
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
        IntPtr region = CreateRoundRectRgn(left - guard, top - guard, left + renderWidth + guard, top + renderHeight + guard,
            diameter + guard * 2, diameter + guard * 2);
        if (region == IntPtr.Zero) return;
        if (SetWindowRgn(hwnd, region, false) == 0) DeleteObject(region);
        else
        {
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
        salaryArea.Opacity = 1;
        FinishLayoutHost();
        Refresh();
    }

    void HideSalary() { if (exiting || disposed) return; settings.HideAmount = true; Refresh(); }

    void TryRegisterSessionNotifications()
    {
        if (disposed) return;
        nextSessionRegistrationAttempt = elapsed.Elapsed.TotalSeconds + 5;
        sessionNotificationRegistered = WTSRegisterSessionNotification(hwnd, 0);
    }

    void OnLayoutHitTest(object? sender, WinUIEx.Messaging.WindowMessageEventArgs args)
    {
        if (!layoutHostActive || args.Message.MessageId != 0x0084 /* WM_NCHITTEST */) return;
        long position = args.Message.LParam.ToInt64();
        double x = (short)(position & 0xffff) - layoutCurrentX - layoutInsetX;
        double y = (short)((position >> 16) & 0xffff) - native.Position.Y - layoutInsetY;
        double width = Math.Max(1, layoutCurrentWidth - layoutInsetWidth);
        double height = Math.Max(1, layoutTargetHeight - layoutInsetHeight);
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
        if (args.Message.MessageId == 0x02B1 && unchecked((long)args.Message.WParam) == 7)
            DispatcherQueue.TryEnqueue(() => { EndDrag(null); HideSalary(); });
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool WTSUnRegisterSessionNotification(IntPtr window);

    void OpenSettings()
    {
        if (exiting || disposed) return;
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(settings, privacy, ToggleSalaryLayout, HideSalary, PreviewPet, next => { privacy.SaveSalary(next.Salary); settings = next; previewScale = null; ApplySettings(); Save(true); }, PreviewScale);
        settingsWindow.Closed += (_, __) =>
        {
            settingsWindow = null;
            if (!previewScale.HasValue || disposed || exiting) return;
            previewScale = null;
            ApplyWidgetSize(); Refresh();
        };
        settingsWindow.Activate();
    }

    void EmitCoin()
    {
        var coin = new TextBlock { Text = "🪙", FontSize = 24 };
        var petOrigin = pet.TransformToVisual(card).TransformPoint(new Point(0, 0));
        double x = (petOrigin.X + pet.ActualWidth * .5) / PhysicalScale - 24 + random.Next(-14, 15), y = 80;
        Canvas.SetLeft(coin, x); Canvas.SetTop(coin, y); coins.Children.Add(coin);
        var transform = new TranslateTransform(); coin.RenderTransform = transform;
        var animation = new DoubleAnimation { From = 0, To = -55, Duration = new Duration(TimeSpan.FromMilliseconds(950)), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, transform); Storyboard.SetTargetProperty(animation, "Y");
        var fade = new DoubleAnimation { From = 1, To = 0, BeginTime = TimeSpan.FromMilliseconds(500), Duration = new Duration(TimeSpan.FromMilliseconds(450)) };
        Storyboard.SetTarget(fade, coin); Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard(); storyboard.Children.Add(animation); storyboard.Children.Add(fade); storyboard.Completed += (_, __) => coins.Children.Remove(coin); storyboard.Begin();
    }

    bool isDragging, endingDrag, dragMoved;
    Win32Point initialCursor;
    PointInt32 initialWindowPos;
    PointInt32 lastDragPosition;

    void OnCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(surface);
        if (!point.Properties.IsLeftButtonPressed) return;
        for (DependencyObject? source = e.OriginalSource as DependencyObject; source != null && source != card; source = VisualTreeHelper.GetParent(source))
            if (source is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase || source is Image) return;

        if (layoutTransitionActive) CompleteLayoutTransition();
        if (scalePreviewHostActive) { EndScalePreviewHost(); ApplyWidgetSize(); }
        isDragging = true;
        if (!card.CapturePointer(e.Pointer)) { isDragging = false; return; }
        desktopSource.PrepareWindowMove();
        dragMoved = false;
        initialWindowPos = native.Position;
        lastDragPosition = initialWindowPos;
        IntPtr renderWindow = FindWindowEx(hwnd, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (renderWindow == IntPtr.Zero) renderWindow = hwnd;
        var origin = new Win32Point();
        if (ClientToScreen(renderWindow, ref origin))
        {
            double dpi = GetDpiForWindow(hwnd) / 96.0;
            initialCursor = new Win32Point { X = origin.X + (int)Math.Round(point.Position.X * dpi), Y = origin.Y + (int)Math.Round(point.Position.Y * dpi) };
        }
        else GetCursorPos(out initialCursor);
        e.Handled = true;
    }

    void OnCardPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(surface).Position;
        glass.SetInteraction(2 * point.X / Math.Max(1, surface.Width) - 1, 2 * point.Y / Math.Max(1, surface.Height) - 1, true);
        if (isDragging) e.Handled = true;
    }

    void UpdateDragFrame()
    {
        if (!isDragging || disposed) return;
        if (!GetCursorPos(out Win32Point currentCursor)) return;
        int dx = currentCursor.X - initialCursor.X, dy = currentCursor.Y - initialCursor.Y;
        if (!dragMoved && Math.Abs(dx) < GetSystemMetrics(68) && Math.Abs(dy) < GetSystemMetrics(69)) return;
        var position = ClampDragPosition(new PointInt32(initialWindowPos.X + dx, initialWindowPos.Y + dy));
        if (position.X == lastDragPosition.X && position.Y == lastDragPosition.Y) return;
        // Keep the HWND at normal size. Pure motion reuses one uploaded scene
        // instead of cropping and uploading the card bitmap at each position.
        if (!dragMoved) desktopSource.BeginWindowMove();
        native.Move(position);
        lastDragPosition = position;
        dragMoved = true;
    }

    PointInt32 ClampDragPosition(PointInt32 position)
    {
        var size = native.Size;
        var center = new PointInt32(position.X + size.Width / 2, position.Y + size.Height / 2);
        var bounds = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest).OuterBounds;
        // Keep the entire glass source on a real display, including the taskbar area.
        return new PointInt32(
            Math.Clamp(position.X, bounds.X, Math.Max(bounds.X, bounds.X + bounds.Width - size.Width)),
            Math.Clamp(position.Y, bounds.Y, Math.Max(bounds.Y, bounds.Y + bounds.Height - size.Height)));
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
        EndDrag(e.Pointer);
        e.Handled = true;
    }

    void OnCardPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e.Pointer);
    }

    void EndDrag(Pointer? pointer)
    {
        if (!isDragging || endingDrag) return;
        endingDrag = true;
        isDragging = false;
        try
        {
            // A very short gesture may finish before the next Rendering callback.
            // Sample its final point without creating or restoring a larger host.
            if (GetCursorPos(out Win32Point cursor))
            {
                int dx = cursor.X - initialCursor.X, dy = cursor.Y - initialCursor.Y;
                if (dragMoved || Math.Abs(dx) >= GetSystemMetrics(68) || Math.Abs(dy) >= GetSystemMetrics(69))
                {
                    lastDragPosition = ClampDragPosition(new PointInt32(initialWindowPos.X + dx, initialWindowPos.Y + dy));
                    if (native.Position.X != lastDragPosition.X || native.Position.Y != lastDragPosition.Y) native.Move(lastDragPosition);
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
            dragMoved = false;
            endingDrag = false;
        }
    }

    void ShowContextMenu(Point point)
    {
        if (disposed) return;
        var menu = CreateWidgetMenu();
        Add(menu, "设置", OpenSettings);
        Add(menu, "隐藏到托盘", () => native.Hide());
        Add(menu, "退出", Exit);
        menu.ShowAt(surface, point);
    }

    void ApplyMouseClickThrough()
    {
        if (settings.MouseClickThrough)
        {
            EndDrag(null);
            glass.SetInteraction(0, 0, false);
        }
        int extended = GetWindowLong(hwnd, GWL_EXSTYLE);
        int next = settings.MouseClickThrough
            ? extended | WS_EX_LAYERED | WS_EX_TRANSPARENT
            : extended & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT);
        if (next == extended) return;
        SetWindowLong(hwnd, GWL_EXSTYLE, next);
        // Keep XAML opaque at the window level; only mouse input passes through.
        if (settings.MouseClickThrough) SetLayeredWindowAttributes(hwnd, 0, 255, 2 /* LWA_ALPHA */);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037 /* FRAMECHANGED | NOACTIVATE | NOZORDER | NOMOVE | NOSIZE */);
    }

    void ToggleMouseClickThrough()
    {
        if (exiting || disposed) return;
        settings.MouseClickThrough = !settings.MouseClickThrough;
        ApplyMouseClickThrough();
        settingsWindow?.RefreshMouseClickThrough(settings.MouseClickThrough);
        Save(true);
    }

    void ShowWidget()
    {
        if (exiting || disposed) return;
        native.Show();
        ApplyMouseClickThrough();
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
        tray = new NativeTrayIcon(hwnd, path, ShowWidget, OpenSettings, Exit, () => false,
            ToggleMouseClickThrough, () => settings.MouseClickThrough);
    }

    static Settings CloneSettings(Settings s)
    {
        return new Settings
        {
            Start = s.Start,
            End = s.End,
            WorkScheduleMode = s.WorkScheduleMode,
            WorkSchedules = s.WorkSchedules != null ? s.WorkSchedules.ConvertAll(p => new WorkScheduleProfile { Mode = p.Mode, Start = p.Start, End = p.End, SaturdayEnabled = p.SaturdayEnabled, SaturdayStart = p.SaturdayStart, SaturdayEnd = p.SaturdayEnd, DayTimes = p.DayTimes != null ? p.DayTimes.ConvertAll(d => new DayWorkTime { Day = d.Day, Enabled = d.Enabled, Start = d.Start, End = d.End }) : new System.Collections.Generic.List<DayWorkTime>() }) : new System.Collections.Generic.List<WorkScheduleProfile>(),
            Salary = 0, // privacy protected
            WorkDays = s.WorkDays,
            PayDay = s.PayDay,
            Topmost = s.Topmost,
            MouseClickThrough = s.MouseClickThrough,
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
            AmountVisibleSeconds = s.AmountVisibleSeconds,
            WaterReminder = s.WaterReminder,
            WalkReminder = s.WalkReminder,
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
                using (var writer = new StreamWriter(temp, false, new System.Text.UTF8Encoding(true)))
                {
                    new XmlSerializer(typeof(Settings)).Serialize(writer, snapshot);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
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
        try
        {
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

        layoutTransitionActive = false; layoutTransitionClock.Stop(); CompositionTarget.Rendering -= AnimateLayoutTransition;
        timer?.Stop();
        walkTimer?.Stop();
        smokeTimer?.Stop();
        if (sessionNotificationRegistered) { WTSUnRegisterSessionNotification(hwnd); sessionNotificationRegistered = false; }
        sessionMonitor?.Dispose();
        tray?.Dispose();
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
        try { timeEndPeriod(1); } catch { }
        privacy.Dispose();
        GlassProfile.Shutdown();
    }

    [StructLayout(LayoutKind.Sequential)] struct NativeClientRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out NativeClientRect rect);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref NativeClientRect rect, IntPtr brush);
    [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int index);
    [StructLayout(LayoutKind.Sequential)] struct Win32Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct GlassMargins { internal int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref GlassMargins margins);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Win32Point lpPoint);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Win32Point point);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr hwnd, string name, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint uPeriod);
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
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
}
