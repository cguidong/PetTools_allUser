using System;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace OffWorkWidget.WinUI;

internal static class SystemTheme
{
    // Follow the Windows app theme; widget glass follows wallpaper luminance instead.
    internal static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int light) return light == 0;
        }
        catch (Exception) { }
        try
        {
            var settings = new UISettings();
            return settings.GetColorValue(UIColorType.Background).R < 128;
        }
        catch (Exception) { return false; }
    }
}
