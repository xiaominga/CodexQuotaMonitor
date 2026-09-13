using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexQuotaMonitor.Wpf;

public static class QuotaTrayIcon
{
    public static Icon Create(double? fiveHour, double? weekly, AppSettings settings, int size)
    {
        using var bitmap = RenderBitmap(fiveHour, weekly, settings, size);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public static Bitmap RenderBitmap(double? fiveHour, double? weekly, AppSettings settings, int size)
    {
        size = Math.Clamp(size, 16, 256);
        const int supersampling = 4;
        using var large = new Bitmap(size * supersampling, size * supersampling, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(large))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size * supersampling / 16f, size * supersampling / 16f);
            DrawRing(graphics, weekly, settings, 6.5f, 1.65f);
            DrawRing(graphics, fiveHour, settings, 3.6f, 1.55f);
        }
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(large, new Rectangle(0, 0, size, size));
        }
        return bitmap;
    }

    private static void DrawRing(Graphics graphics, double? remaining, AppSettings settings, float radius, float stroke)
    {
        var bounds = new RectangleF(8 - radius, 8 - radius, radius * 2, radius * 2);
        using var track = new Pen(System.Drawing.Color.FromArgb(150, 111, 124, 141), stroke);
        if (!remaining.HasValue || !double.IsFinite(remaining.Value))
        {
            track.DashStyle = DashStyle.Dot;
            graphics.DrawEllipse(track, bounds);
            return;
        }
        graphics.DrawEllipse(track, bounds);
        var color = Formatting.ColorForRemaining(remaining, settings);
        using var arc = new Pen(System.Drawing.Color.FromArgb(color.R, color.G, color.B), stroke)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var percent = Math.Clamp(remaining.Value, 0, 100);
        if (percent >= 100) graphics.DrawEllipse(arc, bounds);
        else if (percent > 0) graphics.DrawArc(arc, bounds, -90, (float)(percent * 3.6));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
