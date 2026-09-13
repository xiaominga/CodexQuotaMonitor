using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexQuotaMonitor.Wpf;

// Rasterize the rounded silhouette analytically, independent of WPF's Border/cache tessellation.
public sealed class QuotaCardFrame : FrameworkElement
{
    public static readonly DependencyProperty GlassEnabledProperty = DependencyProperty.Register(
        nameof(GlassEnabled), typeof(bool), typeof(QuotaCardFrame),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool GlassEnabled { get => (bool)GetValue(GlassEnabledProperty); set => SetValue(GlassEnabledProperty, value); }
    private (int Width, int Height, bool Glass)? _key;
    private BitmapSource? _bitmap;

    public QuotaCardFrame()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 2 || ActualHeight <= 2) return;
        var key = ((int)Math.Ceiling(ActualWidth * 4), (int)Math.Ceiling(ActualHeight * 4), GlassEnabled);
        if (_key != key)
        {
            _bitmap = RenderFrame(ActualWidth, ActualHeight, GlassEnabled);
            _key = key;
        }
        dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    public static BitmapSource RenderFrame(double width, double height, bool glass)
    {
        const int sampling = 4;
        var pixelsWide = (int)Math.Ceiling(width * sampling);
        var pixelsHigh = (int)Math.Ceiling(height * sampling);
        var pixels = new byte[pixelsWide * pixelsHigh * 4];
        for (var y = 0; y < pixelsHigh; y++)
        for (var x = 0; x < pixelsWide; x++)
        {
            var px = (x + .5) / sampling;
            var py = (y + .5) / sampling;
            var outside = Coverage(Distance(px, py, width, height, 1, 7));
            if (outside <= 0) continue;
            double r = 26, g = 32, b = 41;
            if (glass)
            {
                var t = Math.Clamp((py - 1) / (height - 2), 0, 1);
                // Optional decorative tint and reflection; the normal base is identical in both modes.
                var lower = t >= .48;
                var blend = lower ? (t - .48) / .52 : t / .48;
                Mix(ref r, ref g, ref b,
                    Lerp(lower ? 18 : 37, lower ? 11 : 18, blend),
                    Lerp(lower ? 29 : 53, lower ? 20 : 29, blend),
                    Lerp(lower ? 42 : 70, lower ? 32 : 42, blend),
                    Lerp(lower ? 176 : 160, lower ? 184 : 176, blend) / 255);
                Mix(ref r, ref g, ref b, 255, 255, 255, 28.0 / 255 * Math.Max(0, 1 - t / .4));
                var diagonal = Math.Clamp((px / width + .8 * t) / 1.64, 0, 1);
                Mix(ref r, ref g, ref b, 235, 247, 255, 10.0 / 255 * Math.Max(0, 1 - diagonal / .65));
            }
            var inside = Coverage(Distance(px, py, width, height, 1.65, 6.35));
            var stroke = Math.Clamp((outside - inside) / outside, 0, 1);
            Mix(ref r, ref g, ref b, 91, 107, 123, stroke);
            var offset = (y * pixelsWide + x) * 4;
            pixels[offset] = (byte)Math.Round(b * outside);
            pixels[offset + 1] = (byte)Math.Round(g * outside);
            pixels[offset + 2] = (byte)Math.Round(r * outside);
            pixels[offset + 3] = (byte)Math.Round(255 * outside);
        }
        var image = BitmapSource.Create(pixelsWide, pixelsHigh, 96 * sampling, 96 * sampling,
            PixelFormats.Pbgra32, null, pixels, pixelsWide * 4);
        image.Freeze();
        return image;
    }

    private static double Coverage(double distance) => Math.Clamp(.5 - distance * 4, 0, 1);
    private static double Distance(double x, double y, double width, double height, double inset, double radius)
    {
        var qx = Math.Abs(x - width / 2) - (width / 2 - inset - radius);
        var qy = Math.Abs(y - height / 2) - (height / 2 - inset - radius);
        return Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2))
            + Math.Min(Math.Max(qx, qy), 0) - radius;
    }
    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
    private static void Mix(ref double r, ref double g, ref double b, double red, double green, double blue, double alpha)
    {
        r = Lerp(r, red, alpha); g = Lerp(g, green, alpha); b = Lerp(b, blue, alpha);
    }
}
