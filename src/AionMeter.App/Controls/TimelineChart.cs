using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AionMeter.Core;

namespace AionMeter.App.Controls;

public sealed record ChartSeries(string Name, IReadOnlyList<long> PerSecond, Color Color, bool Primary);

/// <summary>
/// DPS-over-time chart: per-second damage smoothed with a rolling window. The primary series is drawn as a filled
/// area, the rest of the party as thin lines behind it.
/// </summary>
public sealed class TimelineChart : FrameworkElement
{
    private const double PadLeft = 44, PadRight = 8, PadTop = 10, PadBottom = 20;
    private static readonly Typeface Face = new("Bahnschrift");

    private IReadOnlyList<ChartSeries> _series = [];
    private int _window = 3;

    public void SetData(IReadOnlyList<ChartSeries> series, int smoothingSeconds = 3)
    {
        _series = series;
        _window = Math.Max(1, smoothingSeconds);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (w < 80 || h < 50) return;

        var plot = new Rect(PadLeft, PadTop, w - PadLeft - PadRight, h - PadTop - PadBottom);
        var smoothed = _series.Select(s => (s, values: Smooth(s.PerSecond, _window))).ToList();
        var seconds = Math.Max(2, smoothed.Count == 0 ? 0 : smoothed.Max(x => x.values.Length));
        var max = Math.Max(1, smoothed.Count == 0 ? 0 : smoothed.Max(x => x.values.Length == 0 ? 0 : x.values.Max()));
        max = NiceCeiling(max);

        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x75, 0x90));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var text = new FormattedText(Format.Compact(max * i / 4), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10, labelBrush, dpi);
            dc.DrawText(text, new Point(plot.Left - text.Width - 6, y - text.Height / 2));
        }

        var step = seconds switch { <= 30 => 5, <= 90 => 15, <= 300 => 30, <= 900 => 60, _ => 300 };
        for (var s = 0; s <= seconds; s += step)
        {
            var x = plot.Left + plot.Width * s / (seconds - 1.0);
            if (x > plot.Right + 0.5) break;
            var text = new FormattedText(Format.Clock(s * 1000L), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10, labelBrush, dpi);
            dc.DrawText(text, new Point(Math.Min(x - text.Width / 2, plot.Right - text.Width), plot.Bottom + 4));
        }

        foreach (var (s, values) in smoothed.OrderBy(x => x.s.Primary))
        {
            if (values.Length == 0) continue;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Pt(0, values[0]), s.Primary, false);
                for (var i = 1; i < values.Length; i++) ctx.LineTo(Pt(i, values[i]), true, true);
                if (s.Primary)
                {
                    ctx.LineTo(new Point(Pt(values.Length - 1, 0).X, plot.Bottom), false, false);
                    ctx.LineTo(new Point(plot.Left, plot.Bottom), false, false);
                }
            }
            geo.Freeze();

            if (s.Primary)
            {
                var fill = new LinearGradientBrush(Color.FromArgb(0x70, s.Color.R, s.Color.G, s.Color.B),
                    Color.FromArgb(0x05, s.Color.R, s.Color.G, s.Color.B), 90);
                dc.DrawGeometry(fill, new Pen(new SolidColorBrush(s.Color), 2) { LineJoin = PenLineJoin.Round }, geo);
            }
            else
            {
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x80, s.Color.R, s.Color.G, s.Color.B)), 1.2), geo);
            }
        }

        Point Pt(int i, double v) => new(
            plot.Left + plot.Width * i / (seconds - 1.0),
            plot.Bottom - plot.Height * Math.Min(1, v / max));
    }

    private static double[] Smooth(IReadOnlyList<long> data, int window)
    {
        var result = new double[data.Count];
        double sum = 0;
        for (var i = 0; i < data.Count; i++)
        {
            sum += data[i];
            if (i >= window) sum -= data[i - window];
            result[i] = sum / Math.Min(window, i + 1);
        }
        return result;
    }

    private static double NiceCeiling(double v)
    {
        var exp = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in (ReadOnlySpan<double>)[1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10])
            if (m * exp >= v) return m * exp;
        return 10 * exp;
    }
}
