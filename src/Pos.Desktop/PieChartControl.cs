using System.Windows;
using System.Windows.Media;

namespace Pos.Desktop;

public sealed record PieSlice(string Label, decimal Value, string Brush)
{
    public string PercentageText { get; init; } = string.Empty;
    public string TotalText { get; init; } = string.Empty;
}

public sealed class PieChartControl : FrameworkElement
{
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(
        nameof(Slices),
        typeof(IReadOnlyList<PieSlice>),
        typeof(PieChartControl),
        new FrameworkPropertyMetadata(Array.Empty<PieSlice>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<PieSlice> Slices
    {
        get => (IReadOnlyList<PieSlice>)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var slices = Slices?.Where(slice => slice.Value > 0m).ToList() ?? [];
        if (slices.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        var radius = Math.Max(1d, Math.Min(ActualWidth, ActualHeight) / 2d - 6d);
        var center = new Point(ActualWidth / 2d, ActualHeight / 2d);
        var total = slices.Sum(slice => slice.Value);
        var startAngle = -90d;

        foreach (var slice in slices)
        {
            var sweepAngle = (double)(slice.Value / total * 360m);
            if (sweepAngle >= 359.999d)
            {
                drawingContext.DrawEllipse(CreateBrush(slice.Brush), new Pen(Brushes.White, 2), center, radius, radius);
                startAngle += sweepAngle;
                continue;
            }

            var geometry = CreateSliceGeometry(center, radius, startAngle, sweepAngle);
            drawingContext.DrawGeometry(CreateBrush(slice.Brush), new Pen(Brushes.White, 2), geometry);
            startAngle += sweepAngle;
        }

        drawingContext.DrawEllipse(Brushes.White, null, center, radius * 0.43d, radius * 0.43d);
        drawingContext.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(25, 23, 43, 58)), 1), center, radius * 0.43d, radius * 0.43d);
    }

    private static StreamGeometry CreateSliceGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(center, true, true);
        context.LineTo(start, true, true);
        context.ArcTo(end, new Size(radius, radius), 0, sweepAngle > 180d, SweepDirection.Clockwise, true, true);
        context.LineTo(center, true, true);
        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static Brush CreateBrush(string value)
    {
        try
        {
            var brush = new BrushConverter().ConvertFromString(value) as Brush ?? Brushes.Gray;
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Gray;
        }
    }
}
