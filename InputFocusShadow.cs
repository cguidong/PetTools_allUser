using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace OffWorkWidget.WinUI;

internal static class InputFocusShadow
{
    internal static Grid Wrap(Control input)
    {
        input.UseSystemFocusVisuals = false;
        input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        input.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var host = new Grid();
        var shadowHost = new Canvas { IsHitTestVisible = false };
        host.Children.Add(shadowHost);
        host.Children.Add(input);

        void Clear() => ElementCompositionPreview.SetElementChildVisual(shadowHost, null);

        void Show()
        {
            if (!input.IsLoaded || input.FocusState == FocusState.Unfocused || input.ActualWidth <= 0 || input.ActualHeight <= 0) return;
            var compositor = ElementCompositionPreview.GetElementVisual(shadowHost).Compositor;
            var size = new Vector2((float)input.ActualWidth, (float)input.ActualHeight);
            var origin = input.TransformToVisual(shadowHost).TransformPoint(default);
            var rounded = compositor.CreateRoundedRectangleGeometry();
            rounded.Size = size;
            rounded.CornerRadius = new Vector2((float)input.CornerRadius.TopLeft);
            var outline = compositor.CreateSpriteShape(rounded);
            outline.FillBrush = compositor.CreateColorBrush(Microsoft.UI.Colors.White);
            var shape = compositor.CreateShapeVisual();
            shape.Size = size;
            shape.Shapes.Add(outline);
            var surface = compositor.CreateVisualSurface();
            surface.SourceVisual = shape;
            surface.SourceSize = size;
            var mask = compositor.CreateSurfaceBrush(surface);
            var layers = compositor.CreateContainerVisual();
            layers.Offset = new Vector3((float)origin.X, (float)origin.Y, 0);
            bool dark = input.ActualTheme == ElementTheme.Dark;

            void AddRing(float radius, float opacity, float offset)
            {
                var shadow = compositor.CreateDropShadow();
                shadow.Mask = mask;
                shadow.Color = Microsoft.UI.Colors.Black;
                shadow.BlurRadius = radius;
                shadow.Opacity = opacity;
                shadow.Offset = new Vector3(0, offset, 0);
                var sprite = compositor.CreateSpriteVisual();
                sprite.Size = size;
                sprite.Shadow = shadow;
                layers.Children.InsertAtTop(sprite);
            }
            AddRing(12, dark ? .70f : .35f, 2);
            AddRing(4, dark ? .78f : .42f, 0);
            ElementCompositionPreview.SetElementChildVisual(shadowHost, layers);
        }

        input.GotFocus += (_, __) => Show();
        input.LostFocus += (_, __) => Clear();
        input.SizeChanged += (_, __) => Show();
        input.ActualThemeChanged += (_, __) => Show();
        host.Unloaded += (_, __) => Clear();
        return host;
    }
}
