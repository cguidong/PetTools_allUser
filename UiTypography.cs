using System;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.UI.Text;

namespace OffWorkWidget.WinUI;

internal static class UiTypography
{
    sealed class WeightState
    {
        internal ushort Original, Applied;
    }

    static readonly ConditionalWeakTable<DependencyObject, WeightState> weights = new();
    static readonly ConditionalWeakTable<DependencyObject, object> preservedElements = new();

    internal static void Preserve(DependencyObject element) => preservedElements.GetValue(element, _ => new object());

    static FontWeight Weight(FontWeight weight, bool dark) =>
        dark ? new FontWeight { Weight = (ushort)Math.Max(1, weight.Weight - 40) } : weight;

    internal static void SetWeight(TextBlock text, FontWeight original, bool dark)
    {
        var state = weights.GetValue(text, _ => new WeightState());
        state.Original = original.Weight;
        var weight = Weight(original, dark);
        state.Applied = weight.Weight;
        if (text.FontWeight.Weight != weight.Weight) text.FontWeight = weight;
    }

    internal static void Apply(DependencyObject? element, bool dark)
    {
        if (element == null || preservedElements.TryGetValue(element, out _)) return;
        switch (element)
        {
            case TextBlock text:
                var textWeight = ApplyWeight(text, text.FontWeight, dark);
                if (text.FontWeight.Weight != textWeight.Weight) text.FontWeight = textWeight;
                break;
            case Control control:
                var controlWeight = ApplyWeight(control, control.FontWeight, dark);
                if (control.FontWeight.Weight != controlWeight.Weight) control.FontWeight = controlWeight;
                break;
        }

        // Walk authored content so control template text inherits its owner's weight once.
        switch (element)
        {
            case Panel panel:
                foreach (var child in panel.Children) Apply(child, dark);
                break;
            case Border border: Apply(border.Child, dark); break;
            case Viewbox viewbox: Apply(viewbox.Child, dark); break;
            case ContentControl content: Apply(content.Content as DependencyObject, dark); break;
        }
    }

    static FontWeight ApplyWeight(DependencyObject element, FontWeight current, bool dark)
    {
        var state = weights.GetValue(element, _ => new WeightState { Original = current.Weight, Applied = current.Weight });
        if (current.Weight != state.Applied) state.Original = current.Weight;
        var weight = Weight(new FontWeight { Weight = state.Original }, dark);
        state.Applied = weight.Weight;
        if (Typography.GetNumeralAlignment(element) != FontNumeralAlignment.Tabular)
            Typography.SetNumeralAlignment(element, FontNumeralAlignment.Tabular);
        return weight;
    }
}
