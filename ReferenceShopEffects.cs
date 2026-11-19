using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using OffWorkWidget;
using Windows.Foundation;
using Windows.UI;

namespace OffWorkWidget.WinUI;

public sealed partial class MainWindow
{
    const int GroundBinCount = 56;
    readonly List<ReferenceParticle> referenceParticles = new();
    readonly List<ReferenceParticle> settledParticles = new();
    readonly List<(Ellipse Shape, double X, double Y, double At)> referenceRipples = new();
    readonly double[] groundHeight = new double[GroundBinCount];
    readonly double[] waterFlux = new double[GroundBinCount - 1];
    Path? groundShape;
    PathFigure? groundOutline;
    int referenceEffect;
    EffectParameters referenceParameters;
    double referenceFrame, groundFrame;
    Point previousReferencePointer;
    bool referencePointerInside, referenceLight;

    sealed class ReferenceParticle(Image image, CompositeTransform motion, string sprite, double phase, double homeX, double homeY)
    {
        internal readonly Image Image = image;
        internal readonly CompositeTransform Motion = motion;
        internal readonly string Sprite = sprite;
        internal readonly double Phase = phase, HomeX = homeX, HomeY = homeY;
        internal double X = homeX, Y = homeY, Vx, Vy, LandedAt, LandedOpacity;
        internal bool Lifted;
    }

    static bool UsesReferenceRenderer(int effect) => effect is 3 or 5 or 7 or 10 or 13 or 14 or 19 or 20 or 21 or 22;

    BitmapImage ReferenceSprite(string name, bool light)
    {
        if (light && name is not ("maple-red" or "maple-amber" or "ginkgo")) name += "-day";
        if (!ambientSprites.TryGetValue(name, out var image))
            ambientSprites[name] = image = new BitmapImage(new Uri("ms-appx:///assets/shop-effects/" + name + ".png"));
        return image;
    }

    void StartReferenceEffect(int effect, EffectParameters parameters, double width, double height)
    {
        if (referenceEffect == effect) return;
        referenceEffect = effect;
        referenceParameters = parameters;
        referenceLight = UsesDayEffectPalette();
        referenceFrame = elapsed.Elapsed.TotalSeconds;
        groundFrame = 0;
        if (effect is 3 or 10)
        {
            groundOutline = new PathFigure { IsClosed = true, IsFilled = true };
            groundOutline.Segments.Add(new LineSegment());
            for (int i = 1; i < GroundBinCount; i++) groundOutline.Segments.Add(new QuadraticBezierSegment());
            groundOutline.Segments.Add(new LineSegment());
            var geometry = new PathGeometry();
            geometry.Figures.Add(groundOutline);
            groundShape = new Path { Data = geometry, IsHitTestVisible = false };
            SetGroundPalette();
            ambientEffects.Children.Insert(0, groundShape);
        }
        int count = parameters.Count;
        for (int i = 0; i < count; i++)
            referenceParticles.Add(NewReferenceParticle(width, height, initial: true));
        CompositionTarget.Rendering += RenderReferenceEffect;
    }

    void StopReferenceEffect()
    {
        if (referenceEffect == 0) return;
        CompositionTarget.Rendering -= RenderReferenceEffect;
        referenceEffect = 0;
        referenceParticles.Clear();
        settledParticles.Clear();
        referenceRipples.Clear();
        Array.Clear(groundHeight);
        Array.Clear(waterFlux);
        groundShape = null;
        groundOutline = null;
        referencePointerInside = false;
    }

    ReferenceParticle NewReferenceParticle(double width, double height, bool initial)
    {
        double variant = Random.Shared.NextDouble();
        string sprite = referenceEffect switch
        {
            3 => variant < .42 ? "snow-soft" : "snow-crystal",
            5 => variant < .34 ? "maple-red" : variant < .70 ? "maple-amber" : "ginkgo",
            7 => variant < .5 ? "sakura-pink" : "sakura-pale",
            10 => "rain-drop",
            14 => variant < .23 ? "gold-glint" : "gold-flake",
            20 => "butterfly",
            21 => "origami",
            22 => "feather",
            _ => variant < .5 ? "dust-blue" : "dust-violet"
        };
        bool magnetic = referenceEffect is 13 or 19;
        double size = (magnetic ? 5 + variant * 6 : referenceEffect == 10 ? 13 + variant * 9 :
            referenceEffect == 3 ? 7 + variant * 8 : referenceEffect is >= 20 ? 23 + variant * 14 : 13 + variant * 8) * referenceParameters.Size;
        var motion = new CompositeTransform();
        var image = new Image
        {
            Width = size, Height = size, Source = ReferenceSprite(sprite, referenceLight),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = motion, IsHitTestVisible = false, Opacity = 0
        };
        double x = width * (.05 + Random.Shared.NextDouble() * .90);
        double y = initial || magnetic ? height * Random.Shared.NextDouble() : -size * (.5 + Random.Shared.NextDouble() * 3);
        var p = new ReferenceParticle(image, motion, sprite, variant * Math.Tau, x, y)
        {
            Vy = referenceEffect == 10 ? 140 + variant * 100 : 17 + variant * 24,
            Vx = referenceEffect == 10 ? 15 + referenceParameters.Wind * 32 : 0
        };
        PlaceReferenceParticle(p);
        ambientEffects.Children.Add(image);
        return p;
    }

    static void PlaceReferenceParticle(ReferenceParticle p)
    {
        Canvas.SetLeft(p.Image, p.X - p.Image.Width / 2);
        Canvas.SetTop(p.Image, p.Y - p.Image.Height / 2);
    }

    void RenderReferenceEffect(object? sender, object args)
    {
        if (disposed || exiting || !uiSettings.AnimationsEnabled || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            ClearAmbientShopEffects();
            return;
        }

        double now = elapsed.Elapsed.TotalSeconds;
        double dt = now - referenceFrame;
        if (dt < 1d / 100) return;
        dt = Math.Min(.045, dt);
        referenceFrame = now;

        double width = surface.Width / PhysicalScale, height = surface.Height / PhysicalScale;
        if (width < 32 || height < 32) return;
        Point pointer = default;
        bool inside = !isDragging && TryGetHoverPoint(out pointer);
        pointer = new Point(pointer.X / PhysicalScale, pointer.Y / PhysicalScale);
        inside &= pointer.X >= 0 && pointer.Y >= 0 && pointer.X <= width && pointer.Y <= height;
        double wind = inside && referencePointerInside ? Math.Clamp((pointer.X - previousReferencePointer.X) / dt, -650, 650) : 0;
        previousReferencePointer = pointer;
        referencePointerInside = inside;

        bool light = UsesDayEffectPalette();
        if (light != referenceLight)
        {
            referenceLight = light;
            foreach (var p in referenceParticles) p.Image.Source = ReferenceSprite(p.Sprite, light);
            foreach (var p in settledParticles) p.Image.Source = ReferenceSprite(p.Sprite, light);
            SetGroundPalette();
        }
        if (referenceEffect is 13 or 19)
        {
            foreach (var p in referenceParticles) MoveMagneticDust(p, pointer, inside, now, dt, width, height);
            return;
        }

        if (referenceEffect >= 20)
        {
            foreach (var p in referenceParticles) MoveAirborne(p, pointer, inside, wind, now, dt, width, height);
            return;
        }

        UpdateSettledParticles(pointer, inside, wind, width, height, now);
        if (groundShape != null) UpdateGroundField(pointer, inside, wind, width, height, dt, now);
        double speed = referenceParameters.Speed, opacity = referenceParameters.Opacity / 100d;
        for (int i = referenceParticles.Count - 1; i >= 0; i--)
        {
            var p = referenceParticles[i];
            if (p.Lifted)
            {
                p.Vy += 155 * dt;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
            }
            else
            {
                double sway = Math.Sin(now * 1.9 + p.Phase) * (referenceEffect == 10 ? 0 : 11);
                if (inside && Math.Abs(p.X - pointer.X) < 22 && Math.Abs(p.Y - pointer.Y) < 30)
                {
                    if (referenceEffect == 10 && p.Y <= pointer.Y + 7)
                        sway += p.X < pointer.X ? -110 : 110;
                    else if (referenceEffect != 10)
                        sway += wind * .32;
                }
                p.X += (p.Vx + sway + referenceParameters.Wind * 14) * speed * dt;
                p.Y += p.Vy * speed * dt;
            }
            if (p.X < -p.Image.Width) p.X = width + p.Image.Width;
            if (p.X > width + p.Image.Width) p.X = -p.Image.Width;
            p.Motion.Rotation = referenceEffect == 10 ? -14 :
                Math.Sin(now * 2.2 * speed + p.Phase) * (referenceEffect == 14 ? 88 : 34);
            p.Motion.ScaleX = referenceEffect is 5 or 7 or 14 ? .3 + .7 * Math.Abs(Math.Sin(now * 2 * speed + p.Phase)) : 1;
            p.Image.Opacity = opacity * (referenceEffect == 10 ? .73 : .83) *
                Math.Clamp((p.Y + p.Image.Height / 3) / Math.Max(1, p.Image.Height), 0, 1);

            double floor = height - 3 - p.Image.Height * .2;
            if (groundShape != null) floor -= groundHeight[GroundIndex(p.X, width)];
            if (p.Y < floor || p.Lifted && p.Vy < 0)
            {
                PlaceReferenceParticle(p);
                continue;
            }

            bool wasLifted = p.Lifted;
            p.Y = floor;
            PlaceReferenceParticle(p);
            if (groundShape != null)
            {
                int bin = GroundIndex(p.X, width);
                groundHeight[bin] = Math.Min(referenceEffect == 3 ? height * .085 : height * .055,
                    groundHeight[bin] + (referenceEffect == 3 ? .64 : .43));
                if (referenceEffect == 10) AddReferenceRipple(p.X, floor, now);
                ambientEffects.Children.Remove(p.Image);
            }
            else
            {
                p.LandedAt = now;
                p.LandedOpacity = p.Image.Opacity;
                p.Lifted = false;
                settledParticles.Add(p);
            }
            if (wasLifted) referenceParticles.RemoveAt(i);
            else referenceParticles[i] = NewReferenceParticle(width, height, initial: false);
        }
        UpdateReferenceRipples(now, opacity);
    }

    void MoveAirborne(ReferenceParticle p, Point pointer, bool inside, double wind, double now, double dt, double width, double height)
    {
        bool butterfly = referenceEffect == 20, feather = referenceEffect == 22;
        double speed = referenceParameters.Speed;
        double dx = p.X - pointer.X, dy = p.Y - pointer.Y;
        double distance = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double force = inside ? Math.Max(0, 1 - distance / 55) : 0;
        p.Vx = (p.Vx + (butterfly ? dx / distance * force * 130 : wind * force * .42) * dt) * Math.Exp(-dt * 2);
        p.Vy = (p.Vy + (butterfly ? dy / distance * force * 130 : -Math.Abs(wind) * force * .33) * dt) * Math.Exp(-dt * 2);
        double direction = p.Phase < Math.PI ? 1 : -1;
        p.X += ((feather ? 6 : 24) * direction + Math.Sin(now * 2 + p.Phase) * 13 + p.Vx +
            referenceParameters.Wind * 15) * speed * dt;
        p.Y += ((feather ? 19 : butterfly ? 0 : -4) + p.Vy +
            Math.Sin(now * 2.5 * speed + p.Phase) * 9) * speed * dt;
        double margin = p.Image.Width * .55;
        if (p.X < -margin) p.X = width + margin;
        if (p.X > width + margin) p.X = -margin;
        if (p.Y < -margin) p.Y = feather ? height + margin : height * .7;
        if (p.Y > height + margin) p.Y = -margin;
        double wave = Math.Sin(now * (butterfly ? 11 : 5.5) * speed + p.Phase);
        p.Motion.ScaleX = feather ? 1 : .55 + Math.Abs(wave) * .45;
        p.Motion.Rotation = Math.Sin(now * 1.8 * speed + p.Phase) * (feather ? 30 : 17) + Math.Clamp(p.Vx / 4, -23, 23);
        p.Image.Opacity = referenceParameters.Opacity / 100d *
            Math.Clamp(Math.Min(p.X + margin, width + margin - p.X) / margin, 0, 1) *
            Math.Clamp(Math.Min(p.Y + margin, height + margin - p.Y) / margin, 0, 1);
        PlaceReferenceParticle(p);
    }

    void MoveMagneticDust(ReferenceParticle p, Point pointer, bool inside, double now, double dt, double width, double height)
    {
        double speed = referenceParameters.Speed;
        double flow = Math.Sin(p.Y * .018 + now * .55) * 1.4 + Math.Cos(p.X * .014 - now * .45) * 1.2 + p.Phase;
        double ax = Math.Cos(flow) * 18 + referenceParameters.Wind * 26;
        double ay = Math.Sin(flow) * 16;
        if (inside || referenceEffect == 19)
        {
            double dx = (inside ? pointer.X : width * .5) - p.X;
            double dy = (inside ? pointer.Y : height * .5) - p.Y;
            double dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double radius = (referenceEffect == 19 ? 14 + p.Phase * 4 : 12 + p.Phase * 3.2) * referenceParameters.Size;
            double pull = (dist - radius) * (referenceEffect == 19 ? 5.2 : 3.8);
            double spin = (referenceEffect == 19 ? 112 : 42) * Math.Min(1.4, 48 / (dist + 18));
            ax += (dx * pull - dy * spin) / dist;
            ay += (dy * pull + dx * spin) / dist;
        }
        else
        {
            ax += (Math.Clamp(p.HomeX, 4, width - 4) - p.X) * .45;
            ay += (Math.Clamp(p.HomeY, 4, height - 4) - p.Y) * .45;
        }

        double damping = Math.Pow(inside ? .08 : .28, dt);
        p.Vx = (p.Vx + ax * speed * dt) * damping;
        p.Vy = (p.Vy + ay * speed * dt) * damping;
        double velocity = Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
        double max = (inside ? 185 : 48) * speed;
        if (velocity > max) { p.Vx *= max / velocity; p.Vy *= max / velocity; }
        p.X = Math.Clamp(p.X + p.Vx * dt, 4, width - 4);
        p.Y = Math.Clamp(p.Y + p.Vy * dt, 4, height - 4);
        double typing = elapsed.ElapsedMilliseconds - lastActivityBreath < 280 ? .14 : 0;
        p.Image.Opacity = Math.Min(1, (.58 + .36 * Math.Sin(now * 2.4 * speed + p.Phase) + typing) * referenceParameters.Opacity / 100);
        p.Motion.Rotation = Math.Atan2(p.Vy, p.Vx) * 180 / Math.PI;
        p.Motion.ScaleX = 1 + Math.Min(1.1, velocity / 100);
        PlaceReferenceParticle(p);
    }

    void UpdateSettledParticles(Point pointer, bool inside, double wind, double width, double height, double now)
    {
        int limit = referenceEffect == 5 ? 28 : referenceEffect == 14 ? 32 : 38;
        for (int i = settledParticles.Count - 1; i >= 0; i--)
        {
            var p = settledParticles[i];
            if (inside && Math.Abs(wind) > 40 && pointer.Y > height - 32 &&
                Math.Abs(pointer.X - p.X) < 22 && Math.Abs(pointer.Y - p.Y) < 24)
            {
                settledParticles.RemoveAt(i);
                p.Vx = wind * .16;
                p.Vy = -44 - Random.Shared.NextDouble() * 35;
                p.Lifted = true;
                referenceParticles.Add(p);
                continue;
            }
            double fade = i < settledParticles.Count - limit ? Math.Max(0, 1 - (now - settledParticles[i + limit].LandedAt) * 2.4) : 1;
            if (fade <= 0)
            {
                settledParticles.RemoveAt(i);
                ambientEffects.Children.Remove(p.Image);
                continue;
            }
            p.X = Math.Clamp(p.X, 1, width - 1);
            p.Y = height - 3 - p.Image.Height * .2;
            p.Image.Opacity = fade * referenceParameters.Opacity / 100d *
                (referenceEffect == 14 ? .68 + .15 * Math.Sin(now * 3 + p.Phase) : .82);
            PlaceReferenceParticle(p);
        }
    }

    static int GroundIndex(double x, double width) => Math.Clamp((int)(x / width * (GroundBinCount - 1)), 0, GroundBinCount - 1);

    void SetGroundPalette()
    {
        if (groundShape == null) return;
        bool snow = referenceEffect == 3;
        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fill.GradientStops.Add(new GradientStop
        {
            Offset = 0, Color = snow ? Color.FromArgb(215, 219, 242, 255) :
                referenceLight ? Color.FromArgb(184, 41, 144, 189) : Color.FromArgb(174, 153, 217, 248)
        });
        fill.GradientStops.Add(new GradientStop
        {
            Offset = 1, Color = snow ? Color.FromArgb(151, 136, 192, 225) :
                referenceLight ? Color.FromArgb(72, 73, 156, 194) : Color.FromArgb(74, 63, 133, 184)
        });
        groundShape.Fill = fill;
        groundShape.Opacity = referenceParameters.Opacity / 100d;
    }

    void UpdateGroundField(Point pointer, bool inside, double wind, double width, double height, double dt, double now)
    {
        if (inside && pointer.Y > height - 27 && Math.Abs(wind) > 40)
        {
            int center = GroundIndex(pointer.X, width);
            for (int i = Math.Max(0, center - 4); i <= Math.Min(GroundBinCount - 1, center + 4); i++)
            {
                int destination = Math.Clamp(i + (wind > 0 ? 7 : -7), 0, GroundBinCount - 1);
                double transfer = Math.Min(groundHeight[i], dt * 18 * (1 - Math.Abs(i - center) / 5d));
                groundHeight[i] -= transfer;
                groundHeight[destination] += transfer;
            }
        }

        // Every exchange adds to one cell exactly what it subtracts from the other.
        for (int i = 0; i < GroundBinCount - 1; i++)
        {
            double difference = groundHeight[i] - groundHeight[i + 1];
            double flow;
            if (referenceEffect == 3)
                flow = Math.CopySign(Math.Max(0, Math.Abs(difference) - .38) * dt * 5.2, difference);
            else
                flow = waterFlux[i] = (waterFlux[i] + difference * dt * 13) * Math.Exp(-dt * 4.8);
            double moved = Math.Clamp(flow, -groundHeight[i + 1], groundHeight[i]);
            groundHeight[i] -= moved;
            groundHeight[i + 1] += moved;
        }
        if (now - groundFrame < 1d / 30 || groundShape == null) return;
        groundFrame = now;
        if (groundOutline == null) return;
        groundOutline.StartPoint = new Point(0, height);
        double spacing = width / (GroundBinCount - 1);
        double previousY = Math.Clamp(height - groundHeight[0], 0, height);
        ((LineSegment)groundOutline.Segments[0]).Point = new Point(0, previousY);
        for (int i = 1; i < GroundBinCount; i++)
        {
            double y = Math.Clamp(height - groundHeight[i], 0, height);
            var segment = (QuadraticBezierSegment)groundOutline.Segments[i];
            segment.Point1 = new Point((i - .5) * spacing, previousY);
            segment.Point2 = new Point(i * spacing, y);
            previousY = y;
        }
        ((LineSegment)groundOutline.Segments[GroundBinCount]).Point = new Point(width, height);
    }

    void AddReferenceRipple(double x, double y, double now)
    {
        if (referenceRipples.Count >= 14) return;
        var ring = new Ellipse
        {
            Stroke = new SolidColorBrush(referenceLight ? Color.FromArgb(255, 41, 130, 172) : Color.FromArgb(255, 161, 218, 247)),
            StrokeThickness = .8, IsHitTestVisible = false
        };
        ambientEffects.Children.Add(ring);
        referenceRipples.Add((ring, x, y, now));
    }

    void UpdateReferenceRipples(double now, double opacity)
    {
        for (int i = referenceRipples.Count - 1; i >= 0; i--)
        {
            var (shape, x, y, at) = referenceRipples[i];
            double t = (now - at) / .64;
            if (t >= 1)
            {
                ambientEffects.Children.Remove(shape);
                referenceRipples.RemoveAt(i);
                continue;
            }
            shape.Width = 3 + 23 * t;
            shape.Height = shape.Width * .25;
            shape.Opacity = opacity * (1 - t) * .64;
            Canvas.SetLeft(shape, x - shape.Width / 2);
            Canvas.SetTop(shape, y - shape.Height / 2);
        }
    }
}
