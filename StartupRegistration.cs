using System;
using System.IO;
using Microsoft.Win32;

namespace OffWorkWidget.WinUI;

internal static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "OffWorkWidget";

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string command
            && string.Equals(command, Command(), StringComparison.OrdinalIgnoreCase);
    }

    static string Command()
    {
        string path = Environment.ProcessPath ?? throw new IOException("无法确定当前程序的位置。");
        if (!File.Exists(path)) throw new FileNotFoundException("当前程序文件不存在。", path);
        string command = "\"" + path + "\"";
        if (command.Length > 260) throw new IOException("程序路径过长，请将程序移到较短的目录后再开启开机启动。");
        return command;
    }

    internal static void RepairPathIfNeeded()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command) return;
        // Only heal our own value; leave unrelated startup entries untouched.
        if (command.IndexOf("OffWorkWidget.WinUI.exe", StringComparison.OrdinalIgnoreCase) < 0) return;
        string expected = Command();
        if (!string.Equals(command, expected, StringComparison.OrdinalIgnoreCase))
            key.SetValue(ValueName, expected, RegistryValueKind.String);
    }

    internal static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true)
                ?? throw new IOException("无法写入当前用户的启动项。");
            key.SetValue(ValueName, Command(), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
    }
}