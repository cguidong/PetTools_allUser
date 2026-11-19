using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace OffWorkWidget.WinUI;

public sealed partial class MainWindow
{
    const int FishShopEffect = 16;
    const double FishFrameInterval = 1d / 60;
    readonly List<SwimmingFish> shopFish = new();
    readonly List<(Ellipse Shape, Point Origin, double Age, double Reach)> fishRipples = new();
    EffectParameters fishParameters;
    double lastFishFrame, nextFishFrame, fishRippleDelay, fishStartle, fishHuntDelay;
    SwimmingFish? fishPrey;

    sealed class SwimmingFish(Image image, CompositeTransform transform, BitmapImage[] frames)
    {
        internal readonly Image Image = image;
        internal readonly CompositeTransform Transform = transform;
        internal readonly BitmapImage[] Frames = frames;
        internal double X, Y, Heading, Phase, Respawn;
        internal int Frame;
    }

    void StartFishEffect(EffectParameters parameters, double width, double height)
    {
        if (shopFish.Count != 0) return;
        fishParameters = parameters;
        fishHuntDelay = Random.Shared.Next(7, 12);
        fishRippleDelay = 0;
        fishStartle = 0;
        BitmapImage[] LoadFrames(string species)
        {
            var frames = new BitmapImage[8];
            for (int i = 0; i < frames.Length; i++)
            {
                string name = $"fish-{species}-{i}";
                if (!ambientSprites.TryGetValue(name, out var frame))
                    ambientSprites[name] = frame = new BitmapImage(new Uri($"ms-appx:///assets/shop-effects/{name}.png"));
                frames[i] = frame;
            }
            return frames;
        }
        var koi = LoadFrames("koi");
        var gold = LoadFrames("gold");
        // Add the koi last so it stays above the school.
        for (int i = parameters.Count - 1; i >= 0; i--)
        {
            bool hero = i == 0;
            double size = (hero ? 70 : Random.Shared.Next(22, 31)) * parameters.Size;
            var transform = new CompositeTransform();
            var frames = hero ? koi : gold;
            var image = new Image
            {
                Width = size, Height = size / 1.7, Source = frames[0],
                Opacity = parameters.Opacity / 100d * (hero ? 1 : .85),
                IsHitTestVisible = false, RenderTransform = transform, RenderTransformOrigin = new Point(.5, .5)
            };
            var fish = new SwimmingFish(image, transform, frames)
            {
                X = width * (.2 + Random.Shared.NextDouble() * .6),
                Y = height * (.22 + Random.Shared.NextDouble() * .56),
                Heading = Random.Shared.NextDouble() * Math.Tau,
                Phase = Random.Shared.NextDouble()
            };
            PlaceFish(fish);
            shopFish.Add(fish);
            ambientEffects.Children.Add(image);
        }
        lastFishFrame = elapsed.Elapsed.TotalSeconds;
        nextFishFrame = lastFishFrame + FishFrameInterval;
        CompositionTarget.Rendering += AnimateFishEffect;
    }

    void StopFishEffect()
    {
        CompositionTarget.Rendering -= AnimateFishEffect;
        shopFish.Clear();
        fishRipples.Clear();
        fishPrey = null;
    }

    void AnimateFishEffect(object? sender, object args)
    {
        if (disposed || exiting || !uiSettings.AnimationsEnabled || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            ClearAmbientShopEffects();
            return;
        }
        double now = elapsed.Elapsed.TotalSeconds;
        if (now < nextFishFrame) return;
        double dt = Math.Min(now - lastFishFrame, .05);
        lastFishFrame = now;
        // Carry the deadline forward so slightly early 60 Hz frames do not repeatedly halve the update rate.
        nextFishFrame = Math.Max(nextFishFrame + FishFrameInterval, now + FishFrameInterval / 2);
        double speed = fishParameters.Speed, size = fishParameters.Size;
        double width = surface.Width / PhysicalScale, height = surface.Height / PhysicalScale;
        if (width < 32 || height < 32) return;
        var hero = shopFish[^1];
        Point pointer = default;
        bool hasPointer = !isDragging && TryGetHoverPoint(out pointer);
        double cursorX = hasPointer ? pointer.X / PhysicalScale : -1000;
        double cursorY = hasPointer ? pointer.Y / PhysicalScale : -1000;
        if (cursorX >= 0 && cursorX <= width && cursorY >= 0 && cursorY <= height &&
            DistanceSquared(hero.X, hero.Y, cursorX, cursorY) < Math.Pow(hero.Image.Width * .36, 2))
        {
            if (fishStartle <= 0) AddFishRipple(hero.X, hero.Y, 46 * size);
            fishStartle = .9;
            fishPrey = null;
            fishHuntDelay = 5;
        }
        fishStartle -= dt;
        fishHuntDelay -= dt * speed;
        if (fishHuntDelay <= 0 && fishPrey == null)
        {
            double nearest = double.PositiveInfinity;
            foreach (var fish in shopFish)
            {
                if (fish == hero || fish.Respawn > 0) continue;
                double distance = DistanceSquared(hero.X, hero.Y, fish.X, fish.Y);
                if (distance >= nearest) continue;
                nearest = distance;
                fishPrey = fish;
            }
            fishHuntDelay = Random.Shared.Next(9, 15);
        }
        if (fishPrey is { } prey && DistanceSquared(hero.X, hero.Y, prey.X, prey.Y) < Math.Pow(12 * size, 2))
        {
            AddFishRipple(prey.X, prey.Y, 38 * size);
            prey.Respawn = 1.6;
            prey.Image.Visibility = Visibility.Collapsed;
            fishPrey = null;
        }
        if (fishHuntDelay < -4) fishPrey = null;

        foreach (var fish in shopFish)
        {
            if (fish.Respawn > 0)
            {
                fish.Respawn -= dt;
                if (fish.Respawn > 0) continue;
                fish.X = width * (.15 + Random.Shared.NextDouble() * .7);
                fish.Y = height * (.2 + Random.Shared.NextDouble() * .6);
                fish.Image.Visibility = Visibility.Visible;
            }
            bool isHero = fish == hero;
            bool fleeing = isHero && fishStartle > 0;
            double swimSpeed = isHero ? 32 : 37;
            double targetAngle = fish.Heading + Math.Sin(now * .8 * speed + fish.Phase) * .38;
            if (fleeing && hasPointer)
            {
                targetAngle = Math.Atan2(fish.Y - cursorY, fish.X - cursorX);
                swimSpeed *= 3.6;
            }
            else if (isHero && fishPrey is { } target)
            {
                targetAngle = Math.Atan2(target.Y - fish.Y, target.X - fish.X);
                swimSpeed *= 1.9;
            }
            else if (!isHero && DistanceSquared(fish.X, fish.Y, hero.X, hero.Y) < Math.Pow(37 * size, 2))
            {
                targetAngle = Math.Atan2(fish.Y - hero.Y, fish.X - hero.X);
                swimSpeed *= 1.25;
            }
            double edge = Math.Min(fish.Image.Width * .42 + 5, Math.Min(width, height) * .32);
            if (fish.X < edge || fish.X > width - edge || fish.Y < edge || fish.Y > height - edge)
                targetAngle = Math.Atan2(height / 2 - fish.Y, width / 2 - fish.X);

            double turn = Math.IEEERemainder(targetAngle - fish.Heading, Math.Tau);
            double maxTurn = (fleeing ? 6.8 : 3.1) * speed * dt;
            fish.Heading += Math.Clamp(turn, -maxTurn, maxTurn);
            fish.X = Math.Clamp(fish.X + (Math.Cos(fish.Heading) * swimSpeed + fishParameters.Wind * 12) * dt * speed, 2, width - 2);
            fish.Y = Math.Clamp(fish.Y + Math.Sin(fish.Heading) * swimSpeed * dt * speed, 2, height - 2);
            fish.Phase = (fish.Phase + dt * speed * (fleeing ? 3.6 : isHero ? 1.65 : 2.3)) % 1;
            int frame = (int)(fish.Phase * fish.Frames.Length);
            if (frame != fish.Frame)
            {
                fish.Frame = frame;
                fish.Image.Source = fish.Frames[frame];
            }
            PlaceFish(fish);
        }
        fishRippleDelay -= dt * speed;
        if (fishRippleDelay <= 0)
        {
            AddFishRipple(hero.X - Math.Cos(hero.Heading) * hero.Image.Width * .25,
                hero.Y - Math.Sin(hero.Heading) * hero.Image.Width * .25, (fishStartle > 0 ? 40 : 28) * size);
            fishRippleDelay = fishStartle > 0 ? .18 : .65;
        }
        for (int i = fishRipples.Count - 1; i >= 0; i--)
        {
            var ripple = fishRipples[i];
            ripple.Age += dt * speed;
            if (ripple.Age >= .85)
            {
                ambientEffects.Children.Remove(ripple.Shape);
                fishRipples.RemoveAt(i);
                continue;
            }
            double progress = ripple.Age / .85;
            double diameter = ripple.Reach * (.2 + .8 * progress);
            ripple.Shape.Width = diameter;
            ripple.Shape.Height = diameter * .56;
            ripple.Shape.Opacity = .60 * (1 - progress) * fishParameters.Opacity / 100;
            Canvas.SetLeft(ripple.Shape, ripple.Origin.X - diameter / 2);
            Canvas.SetTop(ripple.Shape, ripple.Origin.Y - ripple.Shape.Height / 2);
            fishRipples[i] = ripple;
        }
    }

    static void PlaceFish(SwimmingFish fish)
    {
        fish.Transform.TranslateX = fish.X - fish.Image.Width / 2;
        fish.Transform.TranslateY = fish.Y - fish.Image.Height / 2;
        fish.Transform.Rotation = fish.Heading * 180 / Math.PI;
    }

    void AddFishRipple(double x, double y, double reach)
    {
        var tint = UsesDayEffectPalette() ? Windows.UI.Color.FromArgb(255, 2, 132, 199) :
            Windows.UI.Color.FromArgb(255, 169, 225, 250);
        var ring = new Ellipse { StrokeThickness = .8, Stroke = new SolidColorBrush(tint), IsHitTestVisible = false };
        ambientEffects.Children.Insert(0, ring);
        fishRipples.Add((ring, new Point(x, y), 0, reach));
    }

    static double DistanceSquared(double x, double y, double otherX, double otherY) =>
        (x - otherX) * (x - otherX) + (y - otherY) * (y - otherY);
}
