using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace CodexQuotaMonitor.Wpf;

// Keep vector drawing commands so WPF rasterizes at the final display scale.
public sealed class QuotaCardFrame : FrameworkElement
{
    public static readonly DependencyProperty GlassEnabledProperty = DependencyProperty.Register(
        nameof(GlassEnabled), typeof(bool), typeof(QuotaCardFrame),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool GlassEnabled { get => (bool)GetValue(GlassEnabledProperty); set => SetValue(GlassEnabledProperty, value); }

    private static readonly Brush BorderBrush = FrozenBrush(Color.FromRgb(91, 107, 123));
    private static readonly Brush BaseBrush = FrozenBrush(Color.FromRgb(26, 32, 41));
    private static readonly Brush GlassBrush = CreateGlassBrush();

    public QuotaCardFrame()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 3.3 || ActualHeight <= 3.3) return;

        // An opaque underlay prevents a transparent seam between the border and body.
        dc.DrawRoundedRectangle(BorderBrush, null,
            new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 7, 7);
        dc.DrawRoundedRectangle(GlassEnabled ? GlassBrush : BaseBrush, null,
            new Rect(1.65, 1.65, ActualWidth - 3.3, ActualHeight - 3.3), 6.35, 6.35);
    }

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush CreateGlassBrush()
    {
        var tint = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(160, 37, 53, 70), 0),
                new(Color.FromArgb(176, 18, 29, 42), .48),
                new(Color.FromArgb(184, 11, 20, 32), 1)
            }
        };
        var highlight = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, .4),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(28, 255, 255, 255), 0),
                new(Color.FromArgb(0, 255, 255, 255), 1)
            }
        };
        var reflection = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(.65, .52),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(10, 235, 247, 255), 0),
                new(Color.FromArgb(0, 235, 247, 255), 1)
            }
        };
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var bounds = new Rect(0, 0, 1, 1);
            dc.DrawRectangle(BaseBrush, null, bounds);
            dc.DrawRectangle(tint, null, bounds);
            dc.DrawRectangle(highlight, null, bounds);
            dc.DrawRectangle(reflection, null, bounds);
        }
        var brush = new DrawingBrush(drawing);
        brush.Freeze();
        return brush;
    }
}
