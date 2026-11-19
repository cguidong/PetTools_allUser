using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OffWorkWidget.WinUI;

internal sealed class InputActivityService : IDisposable
{
    internal const uint InputMessage = 0x00FF, DeviceChangeMessage = 0x00FE;
    readonly IntPtr window;
    readonly HashSet<(IntPtr Device, int Scan)> pressed = new();
    internal bool Listening { get; private set; }

    internal InputActivityService(IntPtr window) => this.window = window;

    internal bool SetListening(bool enabled)
    {
        if (Listening == enabled) return true;
        const uint remove = 1, inputSink = 0x100, deviceNotify = 0x2000;
        uint flags = enabled ? inputSink | deviceNotify : remove;
        IntPtr target = enabled ? window : IntPtr.Zero;
        RawDevice[] devices = { new(1, 2, flags, target), new(1, 6, flags, target) };
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawDevice>())) return false;
        pressed.Clear();
        Listening = enabled;
        return true;
    }

    internal void ForgetDevice(IntPtr device) => pressed.RemoveWhere(key => key.Device == device);

    internal unsafe (int Count, bool Keyboard) ReadActivity(IntPtr handle)
    {
        if (!Listening) return default;
        const int headerSize = 24; // RAWINPUTHEADER on x64.
        const int payloadSize = 24; // RAWMOUSE is the largest registered payload.
        byte* packet = stackalloc byte[headerSize + payloadSize];
        uint size = headerSize + payloadSize;
        uint received = GetRawInputData(handle, 0x10000003 /* RID_INPUT */, packet, ref size, headerSize);
        if (received == uint.MaxValue || received < headerSize + 8) return default;
        return CountPacket(packet, received);
    }

    internal unsafe (int Count, bool Keyboard) CountPacket(byte* packet, uint received)
    {
        const int headerSize = 24;
        if (received < headerSize + 8) return default;
        uint type = *(uint*)packet;
        byte* payload = packet + headerSize;
        if (type == 0 /* RIM_TYPEMOUSE */)
        {
            ushort buttons = *(ushort*)(payload + 4);
            int count = ((buttons & 1) != 0 ? 1 : 0) + ((buttons & 4) != 0 ? 1 : 0);
            return (count, false);
        }
        if (type != 1 /* RIM_TYPEKEYBOARD */ || received < headerSize + 16) return default;
        ushort scan = *(ushort*)payload;
        ushort flags = *(ushort*)(payload + 2);
        if (scan == 0 || scan == 0xff) return default;
        // Retain only physical key identity while held. Never read the virtual
        // key or translate an event into text.
        var key = (*(IntPtr*)(packet + 8), scan | ((flags & 6) << 16));
        if ((flags & 1) != 0)
        {
            pressed.Remove(key);
            return default;
        }
        return pressed.Add(key) ? (1, true) : default;
    }

    public void Dispose() => SetListening(false);

    [StructLayout(LayoutKind.Sequential)]
    readonly struct RawDevice(ushort page, ushort usage, uint flags, IntPtr target)
    {
        readonly ushort UsagePage = page, Usage = usage;
        readonly uint Flags = flags;
        readonly IntPtr Target = target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    static extern unsafe uint GetRawInputData(IntPtr input, uint command, byte* data, ref uint size, uint headerSize);
}
