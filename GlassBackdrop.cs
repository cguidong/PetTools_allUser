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
    private readonly Stopwatch interactionClock = Stopwatch.StartNew();
    private double lastInteractionTime, glareAngle = -55, glareTargetAngle = -55;
    private double glareFactor = 4, glareTargetFactor = 4;
    private double opticalScale = -1;
    internal LiquidGlassBrush Brush { get; }
    internal bool IsSupported => true;
    internal string? LastError => LiquidGlassBrush.LastError;

    private GlassBackdrop(Window window, Border card)
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
            GlareFactor = 4,
            GlareAngle = -55,
            GlareConvergence = 40,
            GlareOppositeFactor = 36,
            RefFresnelRange = 56,
            RefFresnelHardness = 0,
            RefFresnelFactor = 2,
            ShapeRadius = 0.38,
            ShapeRoundness = 2,
            TintA = 0
        };
        card.Background = Brush;
        // Use transparent black, matching the native zero-alpha backing. The
        // named Transparent color carries white RGB, which can bleed at edges.
        window.SystemBackdrop = new WinUIEx.TransparentTintBackdrop(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    internal static GlassBackdrop Apply(Window window, Border card)
    {
        return new GlassBackdrop(window, card);
    }

    internal void SetInteraction(double x, double y, bool active)
    {
        glareTargetAngle = active ? -55 + Math.Clamp(x, -1, 1) * 10 - Math.Clamp(y, -1, 1) * 5 : -55;
        glareTargetFactor = active ? 5 : 4;
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

    internal void SynchronizeScale(double scale)
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
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (window.SystemBackdrop is WinUIEx.TransparentTintBackdrop) window.SystemBackdrop = null;
    }
}
