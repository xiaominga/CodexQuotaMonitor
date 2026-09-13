using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace CodexQuotaMonitor.Wpf;

public sealed class QuotaMetricBlock : FrameworkElement
{
    private readonly string _title;
    private string _detail = "wait";
    private double? _remaining;
    private AppSettings _settings;

    public QuotaMetricBlock(string title, AppSettings settings)
    {
        _title = title;
        _settings = settings;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void SetMetric(double? remaining, string detail, AppSettings settings)
    {
        _remaining = remaining;
        _detail = Formatting.Truncate(detail, 8);
        _settings = settings;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var x = _title == "5H" ? 6.35 : 8.35;
        var muted = new SolidColorBrush(Formatting.ColorFromHex("#A2ACB7"));
        dc.DrawText(Text(_title, 8, FontWeights.SemiBold, muted, dpi), new Point(x, 13.35));
        var number = Text(Formatting.RemainingText(_remaining), 20, FontWeights.SemiBold,
            new SolidColorBrush(Formatting.ColorForRemaining(_remaining, _settings)), dpi);
        var bounds = number.BuildGeometry(new Point()).Bounds;
        // Keep the card fixed when 5H changes between one, two and three digits.
        var slotWidth = Math.Min(_title == "5H" ? 26 : 34, Math.Max(1, ActualWidth - x - 22));
        var fit = Math.Min(1, slotWidth / Math.Max(1, bounds.Width));
        // Center weekly glyph outlines in their fixed slot, including two-digit values.
        var offset = _title == "WK" ? (slotWidth - bounds.Width * fit) / 2 : 0;
        dc.PushTransform(new TranslateTransform(x + 18 + offset, 4.35));
        dc.PushTransform(new ScaleTransform(fit, 1));
        dc.DrawText(number, new Point(-bounds.Left, 0));
        dc.Pop(); dc.Pop();
        dc.DrawText(Text(_detail, 8, FontWeights.Normal, muted, dpi), new Point(x, 30.35));
    }

    private static FormattedText Text(string value, double size, FontWeight weight, System.Windows.Media.Brush brush, double dpi) =>
        new(value, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, dpi);
}
