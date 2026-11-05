#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OffWorkWidget.WinUI;

// Experimental screen capture. Excluding the widget from capture is the caller's responsibility.
internal static class ScreenCaptureExperiment
{
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("OFFWORKWIDGET_SCREEN_CAPTURE_TEST") == "1" ||
        (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
    internal static bool AffinityReady;
    private const int Srccopy = 0x00CC0020;
    private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;

    internal static byte[] Capture(int x, int y, int width, int height, byte[]? buffer = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Capture dimensions must be positive.");
        int size = checked(checked(width * height) * 4);
        if (buffer == null || buffer.Length != size) buffer = new byte[size];
        Array.Clear(buffer, 0, size);
        for (int i = 3; i < size; i += 4) buffer[i] = 255;

        long left = Math.Max((long)x, GetSystemMetrics(SmXVirtualScreen));
        long top = Math.Max((long)y, GetSystemMetrics(SmYVirtualScreen));
        long right = Math.Min((long)x + width, (long)GetSystemMetrics(SmXVirtualScreen) + GetSystemMetrics(SmCxVirtualScreen));
        long bottom = Math.Min((long)y + height, (long)GetSystemMetrics(SmYVirtualScreen) + GetSystemMetrics(SmCyVirtualScreen));
        if (right <= left || bottom <= top) return buffer;

        IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 } };
            bitmap = CreateDIBSection(screen, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            previous = SelectObject(memory, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(buffer, 0, bits, size);
            if (!BitBlt(memory, checked((int)(left - x)), checked((int)(top - y)), checked((int)(right - left)), checked((int)(bottom - top)), screen, checked((int)left), checked((int)top), Srccopy))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            GdiFlush();
            Marshal.Copy(bits, buffer, 0, size);
            for (int i = 3; i < size; i += 4) buffer[i] = 255;
            return buffer;
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter; internal uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { internal BitmapInfoHeader Header; internal uint Colors; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
}
