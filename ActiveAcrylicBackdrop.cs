using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
namespace OffWorkWidget.WinUI;
internal sealed class ActiveAcrylicBackdrop : SystemBackdrop
{
    DesktopAcrylicController? controller;
    SystemBackdropConfiguration? configuration;
    bool isDark;
    internal void UpdateTheme(bool dark) { isDark=dark; if (configuration != null) configuration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light; }
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light };
        SyncSystemAccessibility(target, root);
        controller = new DesktopAcrylicController();
        controller.AddSystemBackdropTarget(target);
        controller.SetSystemBackdropConfiguration(configuration);
    }
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, root);
        SyncSystemAccessibility(target, root);
    }
    void SyncSystemAccessibility(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        if (configuration == null) return;
        var system = GetDefaultSystemBackdropConfiguration(target, root);
        configuration.HighContrastBackgroundColor = system.HighContrastBackgroundColor;
        configuration.IsHighContrast = system.IsHighContrast;
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        controller?.RemoveSystemBackdropTarget(target);
        controller = null;
        configuration = null;
    }
}
