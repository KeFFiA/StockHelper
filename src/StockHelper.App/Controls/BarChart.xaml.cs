using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace StockHelper.App.Controls;

/// <summary>A bar of <see cref="BarChart"/>; heights are proportional (star rows), so the chart scales with its container.</summary>
public sealed record BarPoint(string Label, string ValueText, double Ratio, string Tooltip, bool IsHighlighted)
{
    public GridLength BarLength => new(Math.Max(Ratio, 0.002), GridUnitType.Star);

    public GridLength EmptyLength => new(Math.Max(1 - Ratio, 0.002), GridUnitType.Star);
}

public partial class BarChart : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(BarChart));

    public BarChart() => InitializeComponent();

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
}
