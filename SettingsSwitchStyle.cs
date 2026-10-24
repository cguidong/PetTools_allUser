using System;
using Microsoft.UI.Xaml.Controls.Primitives;
using LiquidGlassWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace OffWorkWidget.WinUI;

// Retain the native ToggleSwitch thumb, keyboard and automation behavior.
internal static class SettingsSwitchStyle
{
    internal static ToggleSwitch Create(bool isOn)
    {
        var resources = (ResourceDictionary)XamlReader.Load(TemplateResources);
        var toggle = new ToggleSwitch
        {
            IsOn = isOn, OnContent = "", OffContent = "", MinWidth = 0,
            Width = 54, Height = 32, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Resources = resources,
            Template = (ControlTemplate)resources["SettingsSwitchTemplate"],
            FocusVisualMargin = new Thickness(-3), UseSystemFocusVisuals = true
        };
        TranslateTransform? position = null;
        Microsoft.UI.Xaml.Shapes.Rectangle? track = null;
        Thumb? thumb = null;
        long positionToken = 0;
        bool dragging = false;
        var fade = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(150) };
        void UpdateTrack()
        {
            if (track == null || position == null) return;
            track.OpacityTransition = dragging ? null : fade;
            track.Opacity = dragging ? Math.Clamp(position.X / 20, 0, 1) : toggle.IsOn ? 1 : 0;
        }
        void Started(object sender, DragStartedEventArgs args) { dragging = true; UpdateTrack(); }
        void Completed(object sender, DragCompletedEventArgs args)
        {
            dragging = false;
            UpdateTrack();
            // Native release/cancel finishes in this input turn; use its final IsOn.
            toggle.DispatcherQueue.TryEnqueue(() => { if (!dragging) UpdateTrack(); });
        }
        void Detach()
        {
            if (position != null) position.UnregisterPropertyChangedCallback(TranslateTransform.XProperty, positionToken);
            if (thumb != null) { thumb.DragStarted -= Started; thumb.DragCompleted -= Completed; }
            position = null; track = null; thumb = null; dragging = false;
        }
        toggle.Toggled += (_, __) => { if (!dragging) UpdateTrack(); };
        toggle.Unloaded += (_, __) => Detach();
        toggle.Loaded += (_, __) =>
        {
            toggle.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(toggle) == 0) return;
            var root = (FrameworkElement)VisualTreeHelper.GetChild(toggle, 0);
            Detach();
            position = root.FindName("KnobTranslateTransform") as TranslateTransform;
            track = root.FindName("SwitchKnobBounds") as Microsoft.UI.Xaml.Shapes.Rectangle;
            thumb = root.FindName("SwitchThumb") as Thumb;
            if (position != null) positionToken = position.RegisterPropertyChangedCallback(TranslateTransform.XProperty, (_, __) => { if (dragging) UpdateTrack(); });
            if (thumb != null) { thumb.DragStarted += Started; thumb.DragCompleted += Completed; }
            UpdateTrack();
            if (root.FindName("GlassKnob") is Border glass && glass.Background is not LiquidGlassBrush)
                glass.Background = new LiquidGlassBrush
                {
                    BlurAmount = 0, RefThickness = 6, RefFactor = 1.3, RefDispersion = 0,
                    ShapeRadius = 1, ShapeRoundness = 2, GlareFactor = 45, GlareRange = 65, GlareHardness = 0, GlareConvergence = 25, GlareOppositeFactor = 55,
                    RefFresnelRange = 32, RefFresnelHardness = 0, RefFresnelFactor = 8, TintA = 0
                };
        };
        return toggle;
    }

    const string TemplateResources = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <ResourceDictionary.ThemeDictionaries>
    <ResourceDictionary x:Key="Light"><SolidColorBrush x:Key="SettingsSwitchOff" Color="#FFC5C5C8" /></ResourceDictionary>
    <ResourceDictionary x:Key="Dark"><SolidColorBrush x:Key="SettingsSwitchOff" Color="#FF5C5C60" /></ResourceDictionary>
    <ResourceDictionary x:Key="Default"><SolidColorBrush x:Key="SettingsSwitchOff" Color="#FF5C5C60" /></ResourceDictionary>
  </ResourceDictionary.ThemeDictionaries>
  <ControlTemplate x:Key="SettingsSwitchTemplate" TargetType="ToggleSwitch">
    <Grid x:Name="SwitchRoot" Width="54" Height="32" Background="Transparent">
      <VisualStateManager.VisualStateGroups>
        <VisualStateGroup x:Name="CommonStates">
          <VisualStateGroup.Transitions><VisualTransition GeneratedDuration="0:0:0.16" /></VisualStateGroup.Transitions>
          <VisualState x:Name="Normal" />
          <VisualState x:Name="PointerOver" />
          <VisualState x:Name="Pressed">
            <VisualState.Setters>
              <Setter Target="WhiteKnob.Opacity" Value="0" />
              <Setter Target="GlassKnob.Opacity" Value="1" />
              <Setter Target="KnobShadow.Opacity" Value="0.3" />
              <Setter Target="KnobStretch.ScaleX" Value="1.5" />
              <Setter Target="KnobStretch.ScaleY" Value="1.6" />
            </VisualState.Setters>
          </VisualState>
          <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="SwitchRoot.Opacity" Value="0.45" /></VisualState.Setters></VisualState>
        </VisualStateGroup>
        <VisualStateGroup x:Name="ToggleStates">
          <VisualStateGroup.Transitions>
            <VisualTransition From="Off" To="On" GeneratedDuration="0">
              <Storyboard><RepositionThemeAnimation TargetName="SwitchKnob" FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobOffToOnOffset}" /></Storyboard>
            </VisualTransition>
            <VisualTransition From="On" To="Off" GeneratedDuration="0">
              <Storyboard><RepositionThemeAnimation TargetName="SwitchKnob" FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobOnToOffOffset}" /></Storyboard>
            </VisualTransition>
            <VisualTransition From="Dragging" To="On" GeneratedDuration="0">
              <Storyboard><RepositionThemeAnimation TargetName="SwitchKnob" FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobCurrentToOnOffset}" /></Storyboard>
            </VisualTransition>
            <VisualTransition From="Dragging" To="Off" GeneratedDuration="0">
              <Storyboard><RepositionThemeAnimation TargetName="SwitchKnob" FromHorizontalOffset="{Binding RelativeSource={RelativeSource TemplatedParent}, Path=TemplateSettings.KnobCurrentToOffOffset}" /></Storyboard>
            </VisualTransition>
            <VisualTransition To="Dragging" GeneratedDuration="0" />
          </VisualStateGroup.Transitions>
          <VisualState x:Name="Off" />
          <VisualState x:Name="Dragging" />
          <VisualState x:Name="On">
            <Storyboard>
              <DoubleAnimation Storyboard.TargetName="KnobTranslateTransform" Storyboard.TargetProperty="X" To="20" Duration="0" />
            </Storyboard>
          </VisualState>
        </VisualStateGroup>
      </VisualStateManager.VisualStateGroups>
      <Grid x:Name="SwitchAreaGrid" Height="24" VerticalAlignment="Center" Background="Transparent" Control.IsTemplateFocusTarget="True">
        <Border x:Name="OuterBorder" Width="54" Height="24" CornerRadius="12" Background="{ThemeResource SettingsSwitchOff}" />
        <Rectangle x:Name="SwitchKnobBounds" Width="54" Height="24" RadiusX="12" RadiusY="12" Fill="#FF34C759" Opacity="0" />
        <Grid x:Name="SwitchKnob" Width="34" Height="24" HorizontalAlignment="Left">
          <Grid.RenderTransform><TranslateTransform x:Name="KnobTranslateTransform" /></Grid.RenderTransform>
          <Grid Width="30" Height="20" RenderTransformOrigin="0.5,0.5">
            <Grid.RenderTransform><ScaleTransform x:Name="KnobStretch" /></Grid.RenderTransform>
            <Border x:Name="KnobShadow" CornerRadius="10" Background="#18000000" Margin="0,1,0,-1" />
            <Border x:Name="GlassKnob" CornerRadius="10" Opacity="0" />
            <Border x:Name="WhiteKnob" CornerRadius="10" Background="White" />
          </Grid>
        </Grid>
        <Thumb x:Name="SwitchThumb" AutomationProperties.AccessibilityView="Raw">
          <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent" /></ControlTemplate></Thumb.Template>
        </Thumb>
      </Grid>
    </Grid>
  </ControlTemplate>
</ResourceDictionary>
""";
}