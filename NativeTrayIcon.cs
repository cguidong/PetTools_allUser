using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using WinUIEx.Messaging;

namespace OffWorkWidget.WinUI;

// Owns the native icon; the HWND belongs to MainWindow and is never destroyed here.
internal sealed class NativeTrayIcon : IDisposable
{
    const uint IconId = 1;
    const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetFocus = 3, NimSetVersion = 4;
    readonly IntPtr hwnd;
    readonly string iconPath;
    readonly DispatcherQueue dispatcher;
    readonly Action show, settings, exit, toggleClickThrough;
    readonly uint callbackMessage, taskbarCreated;
    WindowMessageMonitor? monitor;
    IntPtr icon;
    bool disposed, added, version4;
    TrayMenuWindow? menu;
    readonly Func<bool> isDark, isClickThrough;

    internal NativeTrayIcon(IntPtr hwnd, string path, Action showAction,
        Action settingsAction, Action exitAction, Func<bool> isDark, Action toggleClickThrough, Func<bool> isClickThrough)
    {
        this.hwnd = hwnd;
        this.isDark = isDark;
        this.toggleClickThrough = toggleClickThrough;
        this.isClickThrough = isClickThrough;
        iconPath = path;
        show = showAction; settings = settingsAction; exit = exitAction;
        dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Tray initialization requires the window thread.");
        callbackMessage = RegisterWindowMessageW("OffWorkWidget.TrayCallback");
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        if (callbackMessage == 0 || taskbarCreated == 0) throw new Win32Exception();
        try
        {
            icon = LoadTrayImage();
            if (icon == IntPtr.Zero) throw new Win32Exception();
            monitor = new WindowMessageMonitor(hwnd);
            monitor.WindowMessageReceived += OnMessage;
            // Explorer can be temporarily unavailable during sign-in. TaskbarCreated retries.
            AddIcon();
        }
        catch { Dispose(); throw; }
    }

    NotifyIconData Data() => new NotifyIconData
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = hwnd, Id = IconId,
        Flags = 0x87, CallbackMessage = callbackMessage, Icon = icon,
        Tip = "下班倒计时 · 多宠物", Info = "", InfoTitle = ""
    };

    void AddIcon()
    {
        var data = Data();
        added = Shell_NotifyIconW(NimAdd, ref data);
        version4 = false;
        if (!added) return;
        data.Version = 4;
        version4 = Shell_NotifyIconW(NimSetVersion, ref data);
    }

    IntPtr LoadTrayImage()
    {
        uint dpi = GetDpiForWindow(hwnd);
        return LoadImageW(IntPtr.Zero, iconPath, 1,
            GetSystemMetricsForDpi(49, dpi == 0 ? 96u : dpi),
            GetSystemMetricsForDpi(50, dpi == 0 ? 96u : dpi), 0x10 /* LR_LOADFROMFILE */);
    }

    void OnMessage(object? sender, WindowMessageEventArgs args)
    {
        if (disposed) return;
        if (args.Message.MessageId == taskbarCreated)
        {
            added = false;
            AddIcon();
            return;
        }
        if (args.Message.MessageId == 0x02E0 /* WM_DPICHANGED */)
        {
            IntPtr replacement = LoadTrayImage();
            if (replacement != IntPtr.Zero)
            {
                IntPtr previous = icon;
                icon = replacement;
                var data = Data();
                if (added) Shell_NotifyIconW(NimModify, ref data);
                DestroyIcon(previous);
            }
            return;
        }
        if (args.Message.MessageId != callbackMessage) return;
        long parameter = unchecked((long)args.Message.LParam);
        uint notification = version4 ? (uint)(parameter & 0xFFFF) : unchecked((uint)parameter);
        uint id = version4 ? (uint)((parameter >> 16) & 0xFFFF) : unchecked((uint)args.Message.WParam);
        if (id != IconId) return;
        args.Handled = true;
        args.Result = IntPtr.Zero;
        if (notification == 0x0203 /* WM_LBUTTONDBLCLK */ || notification == 0x0401 /* NIN_KEYSELECT */)
            QueueAction(show);
        else if (notification == 0x007B /* WM_CONTEXTMENU */ || (!version4 && notification == 0x0205))
            ShowMenu();
    }

    void QueueAction(Action action) => dispatcher.TryEnqueue(() => { if (!disposed) action(); });

    void ShowMenu()
    {
        if (disposed || menu?.IsOpen == true) return;
        var identifier = new NotifyIconIdentifier
        { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Window = hwnd, Id = IconId };
        Point anchor;
        if (Shell_NotifyIconGetRect(ref identifier, out Rect rect) == 0)
            anchor = new Point { X = rect.Left + (rect.Right - rect.Left) / 2, Y = rect.Bottom };
        else if (!GetCursorPos(out anchor)) return;
        menu ??= new TrayMenuWindow(command =>
        {
            switch (command)
            {
                case 1: QueueAction(show); break;
                case 3: QueueAction(settings); break;
                case 4: QueueAction(exit); break;
                case 5: QueueAction(toggleClickThrough); break;
            }
        }, () =>
        {
            if (disposed || !added) return;
            var data = Data();
            Shell_NotifyIconW(NimSetFocus, ref data);
        });
        menu.SetClickThrough(isClickThrough());
        menu.ShowAt(anchor.X, anchor.Y, isDark());
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        menu?.Shutdown(); menu = null;
        if (monitor != null)
        {
            monitor.WindowMessageReceived -= OnMessage;
            monitor.Dispose(); monitor = null;
        }
        if (added)
        {
            var data = Data();
            Shell_NotifyIconW(NimDelete, ref data);
            added = false;
        }
        if (icon != IntPtr.Zero) { DestroyIcon(icon); icon = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NotifyIconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetCursorPos(out Point point);
}
