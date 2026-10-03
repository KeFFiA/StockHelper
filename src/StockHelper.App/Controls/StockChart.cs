using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StockHelper.App.Infrastructure;
using StockHelper.App.Resources;
using StockHelper.Core.Services;

namespace StockHelper.App.Controls;

/// <summary>
/// Step chart of an item's stock over time: filled area, dashed minimum line, dots at stock-takes,
/// hover tooltip with the value at the pointer. Colors come from brushes set in XAML (theme aware).
/// </summary>
public sealed class StockChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<StockPoint>), typeof(StockChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(decimal), typeof(StockChart), new FrameworkPropertyMetadata(0m, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(StockChart), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(StockChart), new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumBrushProperty = DependencyProperty.Register(
        nameof(MinimumBrush), typeof(Brush), typeof(StockChart), new FrameworkPropertyMetadata(Brushes.IndianRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(StockChart), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(StockChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LabelHeight = 16;
    private readonly ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Relative };

    public StockChart()
    {
        ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(this, 0);
        SnapsToDevicePixels = true;
    }

    public IReadOnlyList<StockPoint>? Points
    {
        get => (IReadOnlyList<StockPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public decimal Minimum
    {
        get => (decimal)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush MinimumBrush
    {
        get => (Brush)GetValue(MinimumBrushProperty);
        set => SetValue(MinimumBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent background so the whole area reacts to the mouse.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (Points is not { Count: > 1 } points || !TryGetScale(points, out var scale))
        {
            return;
        }

        var plot = scale.Plot;
        var typeface = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Horizontal grid: zero and max.
        var gridPen = new Pen(GridBrush, 1);
        dc.DrawLine(gridPen, new Point(plot.Left, plot.Bottom + 0.5), new Point(plot.Right, plot.Bottom + 0.5));
        dc.DrawLine(gridPen, new Point(plot.Left, plot.Top + 0.5), new Point(plot.Right, plot.Top + 0.5));
        DrawText(dc, NumberInput.Format(scale.Max), new Point(plot.Left, plot.Top - LabelHeight + 2), typeface, dpi);

        // Step line and area.
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var l = line.Open())
        using (var a = area.Open())
        {
            var first = scale.Map(points[0]);
            l.BeginFigure(first, false, false);
            a.BeginFigure(new Point(first.X, plot.Bottom), true, true);
            a.LineTo(first, false, false);
            var previous = first;
            foreach (var point in points.Skip(1))
            {
                var p = scale.Map(point);
                l.LineTo(new Point(p.X, previous.Y), true, true);
                l.LineTo(p, true, true);
                a.LineTo(new Point(p.X, previous.Y), false, false);
                a.LineTo(p, false, false);
                previous = p;
            }

            a.LineTo(new Point(previous.X, plot.Bottom), false, false);
        }

        var fill = LineBrush.Clone();
        fill.Opacity = 0.15;
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round }, line);

        // Minimum stock.
        if (Minimum > 0 && Minimum <= scale.Max)
        {
            var y = scale.Y(Minimum);
            var pen = new Pen(MinimumBrush, 1.2) { DashStyle = new DashStyle([4, 3], 0) };
            dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawText(dc, string.Format(Strings.Chart_Minimum, NumberInput.Format(Minimum)), new Point(plot.Left + 4, y - LabelHeight), typeface, dpi, MinimumBrush);
        }

        // Stock-takes as dots.
        foreach (var point in points.Where(p => p.Kind == StockPointKind.Count))
        {
            dc.DrawEllipse(LineBrush, new Pen(Brushes.White, 1.5), scale.Map(point), 3.5, 3.5);
        }

        // Time axis: start and end dates.
        DrawText(dc, points[0].AtUtc.ToLocalTime().ToString("d MMM"), new Point(plot.Left, plot.Bottom + 3), typeface, dpi);
        var end = FormatText(points[^1].AtUtc.ToLocalTime().ToString("d MMM"), typeface, dpi, LabelBrush);
        dc.DrawText(end, new Point(plot.Right - end.Width, plot.Bottom + 3));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Points is not { Count: > 1 } points || !TryGetScale(points, out var scale))
        {
            return;
        }

        var x = e.GetPosition(this).X;
        var at = scale.Time(x);
        var current = points.LastOrDefault(p => p.AtUtc <= at) ?? points[0];
        _tip.Content = string.Format(Strings.Chart_StockAt, at.ToLocalTime(), NumberInput.Format(current.Stock), Unit);
        _tip.HorizontalOffset = x + 12;
        _tip.VerticalOffset = e.GetPosition(this).Y + 12;
        _tip.IsOpen = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _tip.IsOpen = false;
    }

    private bool TryGetScale(IReadOnlyList<StockPoint> points, out Scale scale)
    {
        var plot = new Rect(0, LabelHeight, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight - LabelHeight * 2));
        var max = Math.Max(points.Max(p => p.Stock), Minimum) * 1.15m;
        scale = new Scale(plot, points[0].AtUtc, points[^1].AtUtc, max <= 0 ? 1 : max);
        return plot.Width > 10 && plot.Height > 10 && scale.To > scale.From;
    }

    private void DrawText(DrawingContext dc, string text, Point origin, Typeface typeface, double dpi, Brush? brush = null) =>
        dc.DrawText(FormatText(text, typeface, dpi, brush ?? LabelBrush), origin);

    private static FormattedText FormatText(string text, Typeface typeface, double dpi, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11, brush, dpi);

    private readonly record struct Scale(Rect Plot, DateTime From, DateTime To, decimal Max)
    {
        public double Y(decimal value) => Plot.Bottom - (double)(Math.Max(0, value) / Max) * Plot.Height;

        public Point Map(StockPoint point) =>
            new(Plot.Left + (point.AtUtc - From).TotalSeconds / (To - From).TotalSeconds * Plot.Width, Y(point.Stock));

        public DateTime Time(double x) =>
            From.AddSeconds(Math.Clamp((x - Plot.Left) / Plot.Width, 0, 1) * (To - From).TotalSeconds);
    }
}
