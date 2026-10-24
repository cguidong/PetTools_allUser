using System;
using System.Diagnostics;
using LiquidGlassWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OffWorkWidget.WinUI;

// The host stays transparent; the optical effect is limited to the card.
internal sealed class GlassBackdrop : IDisposable
{
    private readonly Window window;
    private bool disposed;
    private double edgeTone = -1;
    private readonly Stopwatch interactionClock = Stopwatch.StartNew();
    private double lastInteractionTime, glareAngle = -55, glareTargetAngle = -55;
    private double glareFactor = 18, glareTargetFactor = 18;
    private double opticalScale = -1, edgeRadius = -1, edgeDpi = -1;
    private readonly Grid edgeLayer = new Grid { IsHitTestVisible = false };
    private readonly Border[] shadows = { new Border(), new Border() };
    private readonly GradientStop[] shadowEnds = { new GradientStop(), new GradientStop() };

    internal LiquidGlassBrush Brush { get; }
    internal bool IsSupported => true;
    internal string? LastError => LiquidGlassBrush.LastError;

    private GlassBackdrop(Window window, Border card, Grid surface)
    {
        this.window = window;
        Brush = new LiquidGlassBrush
        {
            // The package blur mirrors texture edges, creating repeated edge content.
            // Keep the liquid-glass refraction and its own multi-sample smoothing.
            BlurAmount = 0,
            RefThickness = 24,
            RefFactor = 1.014,
            RefDispersion = .16,
            GlareRange = 60,
            GlareHardness = 0,
            GlareFactor = 18,
            GlareAngle = -55,
            GlareConvergence = 40,
            GlareOppositeFactor = 36,
            RefFresnelRange = 56,
            RefFresnelHardness = 0,
            RefFresnelFactor = 7,
            ShapeRadius = 0.38,
            ShapeRoundness = 2,
            TintA = 0
        };
        card.Background = Brush;
        // Keep the finish inside the existing silhouette, below all foreground content.
        for (int i = 0; i < shadows.Length; i++)
        {
            var shade = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1)
            };
            shade.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = .25 });
            shadowEnds[i].Offset = 1;
            shade.GradientStops.Add(shadowEnds[i]);
            shadows[i].BorderBrush = shade;
            edgeLayer.Children.Add(shadows[i]);
        }
        surface.Children.Insert(surface.Children.IndexOf(card) + 1, edgeLayer);
        SetTone(0);
        // Use transparent black, matching the native zero-alpha backing. The
        // named Transparent color carries white RGB, which can bleed at edges.
        window.SystemBackdrop = new WinUIEx.TransparentTintBackdrop(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    internal static GlassBackdrop Apply(Window window, Border card, Grid surface)
    {
        return new GlassBackdrop(window, card, surface);
    }

    internal void SetTone(double tone)
    {
        tone = Math.Clamp(tone, 0, 1);
        if (Math.Abs(edgeTone - tone) < .001) return;
        edgeTone = tone;
        shadowEnds[0].Color = Windows.UI.Color.FromArgb((byte)Math.Round(9 + 3 * tone), 0, 0, 0);
        shadowEnds[1].Color = Windows.UI.Color.FromArgb((byte)Math.Round(4 + tone), 0, 0, 0);
    }

    internal void SetInteraction(double x, double y, bool active)
    {
        glareTargetAngle = active ? -55 + Math.Clamp(x, -1, 1) * 10 - Math.Clamp(y, -1, 1) * 5 : -55;
        glareTargetFactor = active ? 20 : 18;
    }

    internal void UpdateInteraction(bool animationsEnabled)
    {
        if (disposed) return;
        double now = interactionClock.Elapsed.TotalSeconds;
        double delta = Math.Clamp(now - lastInteractionTime, 0, .05);
        lastInteractionTime = now;
        double blend = animationsEnabled ? 1 - Math.Exp(-delta / .14) : 1;
        glareAngle += (glareTargetAngle - glareAngle) * blend;
        glareFactor += (glareTargetFactor - glareFactor) * blend;
        if (Math.Abs(glareTargetAngle - glareAngle) < .02) glareAngle = glareTargetAngle;
        if (Math.Abs(glareTargetFactor - glareFactor) < .01) glareFactor = glareTargetFactor;
        if (Math.Abs(Brush.GlareAngle - glareAngle) > .01) Brush.GlareAngle = glareAngle;
        if (Math.Abs(Brush.GlareFactor - glareFactor) > .005) Brush.GlareFactor = glareFactor;
    }

    internal void SynchronizeEdge(double radius, double dpi, double scale)
    {
        if (opticalScale != scale)
        {
            opticalScale = scale;
            double rangeScale = Math.Sqrt(scale);
            Brush.RefThickness = 24 * scale;
            Brush.RefFactor = Math.Sqrt(1 + (1.014 * 1.014 - 1) * scale * scale);
            Brush.RefDispersion = .16;
            // The shader's spatial falloff width is proportional to Range squared.
            Brush.GlareRange = 60 * rangeScale;
            Brush.RefFresnelRange = 56 * rangeScale;
        }
        if (edgeRadius == radius && edgeDpi == dpi) return;
        edgeRadius = radius; edgeDpi = dpi;
        double pixel = 1 / Math.Max(1, dpi);
        for (int i = 0; i < shadows.Length; i++)
        {
            double inset = (i == 0 ? .85 : 1.8) * pixel;
            shadows[i].Margin = new Thickness(inset);
            shadows[i].BorderThickness = new Thickness((i == 0 ? 1.2 : 2.5) * pixel);
            shadows[i].CornerRadius = new CornerRadius(Math.Max(0, radius - inset));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (edgeLayer.Parent is Panel parent) parent.Children.Remove(edgeLayer);
        if (window.SystemBackdrop is WinUIEx.TransparentTintBackdrop) window.SystemBackdrop = null;
    }
}
