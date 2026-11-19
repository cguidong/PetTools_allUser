using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace OffWorkWidget.WinUI;

internal sealed class StreamingText : Grid
{
    readonly TextBlock text;
    readonly Canvas overlay = new() { IsHitTestVisible = false };
    readonly Windows.UI.ViewManagement.UISettings animationSettings = new();
    RichTextBlock? active;

    internal StreamingText(TextBlock text)
    {
        this.text = text;
        Children.Add(text);
        Children.Add(overlay);
        Unloaded += (_, __) => Stop();
        SizeChanged += (_, __) => { if (active != null) active.Width = Math.Max(0, TextWidth); };
    }

    double TextWidth => ActualWidth - text.Margin.Left - text.Margin.Right;

    internal void Show(string value, bool animate, Action? prepareLayout = null)
    {
        if (text.Text == value) return;
        Stop();
        text.Text = value;
        prepareLayout?.Invoke();
        if (!animate || !animationSettings.AnimationsEnabled || !IsLoaded || TextWidth <= 0 ||
            string.IsNullOrWhiteSpace(value) || text.Foreground is not SolidColorBrush ink) return;

        var line = new RichTextBlock
        {
            Width = TextWidth, TextWrapping = text.TextWrapping, IsTextSelectionEnabled = false,
            IsHitTestVisible = false, IsTextScaleFactorEnabled = text.IsTextScaleFactorEnabled, Opacity = 0
        };
        var paragraph = new Paragraph();
        var glyphs = new List<(Grid Host, TextBlock Letter)>();
        var characters = StringInfo.GetTextElementEnumerator(value);
        while (characters.MoveNext())
        {
            var letter = new TextBlock
            {
                Text = characters.GetTextElement(), FontFamily = text.FontFamily, FontSize = text.FontSize,
                FontWeight = text.FontWeight, Foreground = ink, CharacterSpacing = text.CharacterSpacing,
                IsTextScaleFactorEnabled = text.IsTextScaleFactorEnabled
            };
            Typography.SetNumeralAlignment(letter, FontNumeralAlignment.Tabular);
            var host = new Grid();
            host.Children.Add(letter);
            paragraph.Inlines.Add(new InlineUIContainer { Child = host });
            glyphs.Add((host, letter));
        }
        line.Blocks.Add(paragraph);
        Canvas.SetLeft(line, text.Margin.Left);
        Canvas.SetTop(line, text.Margin.Top);
        active = line;
        line.Loaded += (_, __) =>
        {
            if (active != line) return;
            try { Play(line, glyphs, ink.Color); }
            catch (Exception ex) when (ex is COMException || ex is InvalidOperationException || ex is ArgumentException) { Stop(); }
        };
        text.Opacity = 0;
        overlay.Children.Add(line);
    }

    void Play(RichTextBlock line, List<(Grid Host, TextBlock Letter)> glyphs, Windows.UI.Color color)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(line).Compositor;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, __) =>
        {
            if (active == line) Stop();
            batch.Dispose();
        };
        try
        {
            for (int index = 0; index < glyphs.Count; index++)
            {
                var (host, letter) = glyphs[index];
                if (string.IsNullOrWhiteSpace(letter.Text)) continue;
                var visual = ElementCompositionPreview.GetElementVisual(letter);
                var shadow = compositor.CreateDropShadow();
                shadow.Color = color;
                shadow.Mask = letter.GetAlphaMask();
                var blur = compositor.CreateSpriteVisual();
                blur.RelativeSizeAdjustment = Vector2.One;
                blur.Shadow = shadow;
                ElementCompositionPreview.SetElementChildVisual(host, blur);

                using var ramp = compositor.CreateScalarKeyFrameAnimation();
                ramp.Duration = TimeSpan.FromMilliseconds(120);
                ramp.DelayTime = TimeSpan.FromMilliseconds(index * 30);
                ramp.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                ramp.InsertKeyFrame(0, 0);
                ramp.InsertKeyFrame(1, 1);
                visual.StartAnimation("Opacity", ramp);

                ramp.InsertKeyFrame(0, 6);
                ramp.InsertKeyFrame(1, 0);
                shadow.StartAnimation("BlurRadius", ramp);
                ramp.InsertKeyFrame(0, 0);
                ramp.InsertKeyFrame(.35f, .65f);
                shadow.StartAnimation("Opacity", ramp);
            }
            line.Opacity = 1;
        }
        finally { batch.End(); }
    }

    void Stop()
    {
        if (active == null) return;
        active = null;
        text.Opacity = 1;
        overlay.Children.Clear();
    }
}
