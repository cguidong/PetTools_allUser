using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.ViewManagement;

namespace OffWorkWidget.WinUI;

// A reusable, opaque tray popup: DWM owns the outer contour rather than a hard window region.
// Windows 10 uses square corners; high contrast uses system window/highlight colors.
internal sealed class TrayMenuWindow : Window
{
    const double MenuWidth = 156, MenuHeight = 119;
    readonly Action<uint> selected;
    readonly Action dismissed;
    readonly IntPtr hwnd;
    readonly AppWindow native;
    readonly Border surface;
    readonly Border divider;
    readonly Button clickThroughItem;
    readonly List<Button> items = new();
    readonly AccessibilitySettings accessibility = new();
    readonly UISettings systemColors = new();
    readonly bool rounded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    bool closed, shuttingDown;
    int focusIndex;

    internal bool IsOpen { get; private set; }

    internal TrayMenuWindow(Action<uint> selected, Action dismissed)
    {
        this.selected = selected;
        this.dismissed = dismissed;
        Title = "下班倒计时菜单";
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        native = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        native.IsShownInSwitchers = false;
        if (native.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);
        int style = GetWindowLongW(hwnd, -16);
        SetWindowLongW(hwnd, -16, (style & ~0x00CF0000) | unchecked((int)0x80000000));
        int extended = GetWindowLongW(hwnd, -20);
        SetWindowLongW(hwnd, -20, (extended & ~0x00040000) | 0x00000080);
        int rendering = 2;
        DwmSetWindowAttribute(hwnd, 2, ref rendering, 4);
        int corner = rounded ? 3 : 1;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(4) };
        surface = new Border
        {
            CornerRadius = new CornerRadius(rounded ? 8 : 0),
            BorderThickness = new Thickness(1),
            Child = panel
        };
        AddItem(panel, 1, "显示挂件", "\uE80F");
        AddItem(panel, 3, "设置", "\uE713");
        clickThroughItem = AddItem(panel, 5, "开启鼠标穿透", "\uE962");
        divider = new Border { Height = 1, Margin = new Thickness(4, 2, 4, 2) };
        panel.Children.Add(divider);
        AddItem(panel, 4, "退出", "\uE7E8");
        Content = surface;
        surface.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
        Activated += (_, args) =>
        {
            if (IsOpen && args.WindowActivationState == WindowActivationState.Deactivated) Hide();
        };
        native.Closing += (_, args) =>
        {
            if (shuttingDown) return;
            args.Cancel = true;
            Hide();
        };
        Closed += (_, __) => { closed = true; IsOpen = false; };
    }

    Button AddItem(StackPanel panel, uint command, string label, string glyph)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        row.Children.Add(icon);
        var text = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var button = new Button
        {
            Content = row,
            Height = 26,
            Padding = new Thickness(6, 0, 6, 0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            UseSystemFocusVisuals = true
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, __) => { Hide(); selected(command); };
        items.Add(button);
        panel.Children.Add(button);
        return button;
    }

    internal void SetClickThrough(bool enabled)
    {
        string label = enabled ? "关闭鼠标穿透" : "开启鼠标穿透";
        ((TextBlock)((Grid)clickThroughItem.Content).Children[1]).Text = label;
        AutomationProperties.SetName(clickThroughItem, label);
    }

    internal void ShowAt(int physicalX, int physicalY, bool dark)
    {
        if (closed) return;
        ApplyTheme(dark);
        // Move first so the window takes on the anchor monitor's DPI before sizing in pixels.
        SetWindowPos(hwnd, new IntPtr(-1), physicalX, physicalY, 0, 0, 0x0031);
        double scale = Math.Max(96u, GetDpiForWindow(hwnd)) / 96d;
        int width = (int)Math.Ceiling(MenuWidth * scale);
        int height = (int)Math.Ceiling(MenuHeight * scale);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfoW(MonitorFromPoint(new Point { X = physicalX, Y = physicalY }, 2), ref info);
        int gap = (int)Math.Ceiling(4 * scale);
        int x = Math.Clamp(physicalX - width, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        int y = physicalY - height - gap;
        if (y < info.Work.Top) y = physicalY + gap;
        y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        SetWindowPos(hwnd, new IntPtr(-1), x, y, width, height, 0x0030);
        IsOpen = true;
        focusIndex = 0;
        native.Show();
        Activate();
        SetForegroundWindow(hwnd);
        items[0].Focus(FocusState.Programmatic);
        DispatcherQueue.TryEnqueue(() => { if (IsOpen) items[0].Focus(FocusState.Programmatic); });
    }

    internal void Hide()
    {
        if (!IsOpen || closed) return;
        IsOpen = false;
        native.Hide();
        dismissed();
    }

    internal void Shutdown()
    {
        shuttingDown = true;
        Close();
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Hide();
            return;
        }
        int index = items.FindIndex(item => item.FocusState != FocusState.Unfocused);
        if (index >= 0) focusIndex = index;
        switch (args.Key)
        {
            case VirtualKey.Down: focusIndex = (focusIndex + 1) % items.Count; break;
            case VirtualKey.Up: focusIndex = (focusIndex + items.Count - 1) % items.Count; break;
            case VirtualKey.Home: focusIndex = 0; break;
            case VirtualKey.End: focusIndex = items.Count - 1; break;
            case VirtualKey.Tab:
                bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                    & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                focusIndex = (focusIndex + (shift ? items.Count - 1 : 1)) % items.Count;
                break;
            default: return;
        }
        args.Handled = true;
        items[focusIndex].Focus(FocusState.Keyboard);
    }

    void ApplyTheme(bool dark)
    {
        bool highContrast = accessibility.HighContrast;
        surface.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        Windows.UI.Color background = highContrast ? systemColors.UIElementColor(UIElementType.Window)
            : dark ? Windows.UI.Color.FromArgb(255, 39, 39, 41) : Windows.UI.Color.FromArgb(255, 250, 250, 251);
        Windows.UI.Color foreground = highContrast ? systemColors.UIElementColor(UIElementType.WindowText)
            : dark ? Windows.UI.Color.FromArgb(255, 240, 240, 244) : Windows.UI.Color.FromArgb(255, 30, 33, 39);
        Windows.UI.Color line = highContrast ? foreground
            : dark ? Windows.UI.Color.FromArgb(255, 72, 72, 76) : Windows.UI.Color.FromArgb(255, 218, 219, 223);
        var foregroundBrush = new SolidColorBrush(foreground);
        surface.Background = new SolidColorBrush(background);
        surface.BorderBrush = new SolidColorBrush(line);
        divider.Background = new SolidColorBrush(line);
        foreach (Button button in items)
        {
            button.Foreground = foregroundBrush;
            button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Resources["ButtonBackgroundPointerOver"] = new SolidColorBrush(highContrast
                ? systemColors.UIElementColor(UIElementType.Highlight)
                : dark ? Windows.UI.Color.FromArgb(255, 58, 58, 62) : Windows.UI.Color.FromArgb(255, 235, 236, 239));
            button.Resources["ButtonBackgroundPressed"] = button.Resources["ButtonBackgroundPointerOver"];
            button.Resources["ButtonForegroundPointerOver"] = highContrast
                ? new SolidColorBrush(systemColors.UIElementColor(UIElementType.HighlightText)) : foregroundBrush;
            button.Resources["ButtonForegroundPressed"] = button.Resources["ButtonForegroundPointerOver"];
        }
        int immersive = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref immersive, 4);
        int color = line.R | (line.G << 8) | (line.B << 16);
        DwmSetWindowAttribute(hwnd, 34, ref color, 4);
    }

    [StructLayout(LayoutKind.Sequential)] struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct Margins { internal int Left, Right, Top, Bottom; }
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLongW(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
}
