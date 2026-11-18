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

internal sealed class WidgetMenuWindow : Window
{
    const double MenuWidth = 100, RowHeight = 30, MenuPadding = 4;
    const double DividerSpace = 7;
    const double MenuHeight = RowHeight * 3 + DividerSpace + MenuPadding * 2;
    readonly Action<uint> selected;
    readonly Action dismissed;
    readonly IntPtr hwnd;
    readonly AppWindow native;
    readonly Border surface;
    readonly StackPanel panel;
    readonly Border divider;
    readonly List<Button> items = new();
    readonly AccessibilitySettings accessibility = new();
    readonly UISettings systemColors = new();
    readonly bool rounded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    bool closed, shuttingDown, highContrast;
    int focusIndex;
    double uiScale = 1;

    internal bool IsOpen { get; private set; }

    internal WidgetMenuWindow(Action<uint> selected, Action dismissed)
    {
        this.selected = selected;
        this.dismissed = dismissed;
        Title = "有盼菜单";
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
        int rendering = 1; // Disable the native frame around the transparent XAML surface.
        DwmSetWindowAttribute(hwnd, 2, ref rendering, 4);
        int corner = 1;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
        int noBorder = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, 4);

        panel = new StackPanel { Spacing = 0 };
        surface = new Border
        {
            CornerRadius = new CornerRadius(rounded ? 14 : 0),
            BorderThickness = new Thickness(1),
            Child = panel
        };
        AddItem(1, "设置");
        AddItem(2, "隐藏到托盘");
        divider = new Border { Height = 1 };
        panel.Children.Add(divider);
        AddItem(3, "退出");
        Content = surface;
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop(Windows.UI.Color.FromArgb(0, 0, 0, 0));
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

    void AddItem(uint command, string label)
    {
        var text = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var button = new Button
        {
            Content = text,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            UseSystemFocusVisuals = true
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, __) => { Hide(); selected(command); };
        items.Add(button);
        panel.Children.Add(button);
    }

    internal void ShowAt(int physicalX, int physicalY, bool dark, double scale)
    {
        if (closed) return;
        uiScale = Math.Clamp(scale, .85, 1.15);
        ApplyScale();
        ApplyTheme(dark);
        // Move first so the popup picks up the target monitor's DPI.
        SetWindowPos(hwnd, new IntPtr(-1), physicalX, physicalY, 0, 0, 0x0031);
        double dpi = Math.Max(96u, GetDpiForWindow(hwnd)) / 96d;
        int width = (int)Math.Ceiling(MenuWidth * uiScale * dpi);
        int height = (int)Math.Ceiling(MenuHeight * uiScale * dpi);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfoW(MonitorFromPoint(new Point { X = physicalX, Y = physicalY }, 2), ref info);
        int gap = (int)Math.Ceiling(4 * uiScale * dpi);
        // Open to the lower-right of the click point, clamped only at screen edges.
        int x = Math.Clamp(physicalX + gap, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        int y = Math.Clamp(physicalY + gap, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        SetWindowPos(hwnd, new IntPtr(-1), x, y, width, height, 0x0030);
        IsOpen = true;
        focusIndex = 0;
        native.Show();
        Activate();
        SetForegroundWindow(hwnd);
        items[0].Focus(FocusState.Pointer);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsOpen) return;
            items[0].Focus(FocusState.Pointer);
        });
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

    void ApplyScale()
    {
        panel.Margin = new Thickness(MenuPadding * uiScale);
        surface.CornerRadius = new CornerRadius((rounded ? 12 : 0) * uiScale);
        surface.BorderThickness = new Thickness(Math.Max(1, uiScale));
        divider.Height = Math.Max(1, uiScale);
        divider.Margin = new Thickness(5 * uiScale, 3 * uiScale, 5 * uiScale, 3 * uiScale);
        foreach (Button button in items)
        {
            button.Height = RowHeight * uiScale;
            button.Padding = new Thickness(8 * uiScale, 0, 8 * uiScale, 0);
            if (button.Content is TextBlock text) text.FontSize = 12.5 * uiScale;
        }
    }

    void ApplyTheme(bool dark)
    {
        highContrast = accessibility.HighContrast;
        surface.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        Windows.UI.Color background = highContrast ? systemColors.UIElementColor(UIElementType.Window)
            : dark ? Windows.UI.Color.FromArgb(255, 39, 39, 41) : Windows.UI.Color.FromArgb(255, 250, 250, 251);
        Windows.UI.Color foreground = highContrast ? systemColors.UIElementColor(UIElementType.WindowText)
            : dark ? Windows.UI.Color.FromArgb(255, 240, 240, 244) : Windows.UI.Color.FromArgb(255, 30, 33, 39);
        Windows.UI.Color line = highContrast ? foreground
            : dark ? Windows.UI.Color.FromArgb(255, 72, 72, 76) : Windows.UI.Color.FromArgb(255, 218, 219, 223);
        var foregroundBrush = new SolidColorBrush(foreground);
        surface.Background = new SolidColorBrush(background);
        surface.BorderBrush = highContrast ? new SolidColorBrush(line) : null;
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
    }

    [StructLayout(LayoutKind.Sequential)] struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLongW(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
