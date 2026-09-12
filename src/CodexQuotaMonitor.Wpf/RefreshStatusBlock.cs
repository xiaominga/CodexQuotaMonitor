using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace CodexQuotaMonitor.Wpf;

public sealed class RefreshStatusBlock : FrameworkElement
{
    private DateTimeOffset? _refreshedAt;
    private DateTimeOffset _currentTime = DateTimeOffset.Now;
    private string _status = "WAIT";
    private Color _accentColor = Formatting.ColorFromHex("#8794A2");

    public RefreshStatusBlock()
    {
        SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
    }

    public void SetStatus(DateTimeOffset? refreshedAt, DateTimeOffset currentTime, string status, string accentHex)
    {
        _refreshedAt = refreshedAt;
        _currentTime = currentTime;
        _status = status;
        _accentColor = Formatting.ColorFromHex(accentHex);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = Math.Max(34.0, ActualWidth);
        var height = Math.Max(22.0, ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var accent = new SolidColorBrush(_accentColor);
        var current = new SolidColorBrush(Formatting.ColorFromHex("#F4F6F8"));
        var muted = new SolidColorBrush(Formatting.ColorFromHex("#8F9BA8"));
        var refresh = new SolidColorBrush(Formatting.ColorFromHex("#58B8FF"));
        // Two balanced rows give both times enough room without a cramped slash.
        var rowHeight = height / 2;
        DrawTimeRow(dc, string.IsNullOrWhiteSpace(_status) ? "SYNC" : _status,
            FormatTime(_refreshedAt), new Rect(0, 0, width, rowHeight),
            string.IsNullOrWhiteSpace(_status) ? muted : accent, refresh, dpi);
        DrawTimeRow(dc, "NOW", FormatTime(_currentTime),
            new Rect(0, rowHeight, width, rowHeight), muted, current, dpi);
    }

    private static string FormatTime(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToString("HH:mm", CultureInfo.CurrentCulture) : "--:--";

    private static void DrawTimeRow(DrawingContext dc, string label, string time,
        Rect row, Brush labelBrush, Brush timeBrush, double dpi)
    {
        var labelWidth = Math.Max(22, row.Width * 0.29);
        DrawCenteredText(dc, label, new Rect(0, row.Top, labelWidth, row.Height),
            6.8, FontWeights.SemiBold, labelBrush, dpi);
        DrawCenteredText(dc, time, new Rect(labelWidth, row.Top, row.Width - labelWidth - 2, row.Height),
            Math.Min(16, row.Height * 0.79), FontWeights.SemiBold, timeBrush, dpi);
    }
    private static void DrawText(DrawingContext dc, string text, double x, double y, double size, FontWeight weight, Brush brush, double dpi)
    {
        dc.DrawText(MakeText(text, size, weight, brush, dpi), new Point(x, y));
    }

    private static void DrawCenteredText(DrawingContext dc, string text, Rect bounds, double size, FontWeight weight, Brush brush, double dpi)
    {
        var formatted = MakeText(text, size, weight, brush, dpi);
        var textBounds = formatted.BuildGeometry(new Point(0, 0)).Bounds;
        var fit = Math.Min(1.0, (bounds.Width - 2) / Math.Max(1, textBounds.Width));
        if (fit < 1)
        {
            formatted = MakeText(text, size * fit, weight, brush, dpi);
            textBounds = formatted.BuildGeometry(new Point(0, 0)).Bounds;
        }
        var x = bounds.Left + (bounds.Width - textBounds.Width) / 2.0 - textBounds.Left;
        var y = bounds.Top + (bounds.Height - textBounds.Height) / 2.0 - textBounds.Top;
        dc.DrawText(formatted, new Point(x, y));
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Brush brush, double dpi)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            dpi);
    }
}
