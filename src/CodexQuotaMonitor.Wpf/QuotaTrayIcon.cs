using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Point = System.Windows.Point;

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
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 16.0, size / 16.0));
            DrawRing(dc, weekly, settings, 6.5, 1.65);
            DrawRing(dc, fiveHour, settings, 3.6, 1.55);
            dc.Pop();
        }
        // The shell needs an HICON; rasterize once at its requested size using WPF.
        var source = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        source.Render(visual);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        try
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, size, size),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { source.CopyPixels(Int32Rect.Empty, data.Scan0, data.Stride * size, data.Stride); }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void DrawRing(DrawingContext dc, double? remaining, AppSettings settings, double radius, double stroke)
    {
        var center = new Point(8, 8);
        var track = new Pen(new SolidColorBrush(Color.FromArgb(150, 111, 124, 141)), stroke);
        if (!remaining.HasValue || !double.IsFinite(remaining.Value))
        {
            track.DashStyle = DashStyles.Dot;
            dc.DrawEllipse(null, track, center, radius, radius);
            return;
        }
        dc.DrawEllipse(null, track, center, radius, radius);
        var arc = new Pen(new SolidColorBrush(Formatting.ColorForRemaining(remaining, settings)), stroke)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var percent = Math.Clamp(remaining.Value, 0, 100);
        if (percent >= 100) dc.DrawEllipse(null, arc, center, radius, radius);
        else if (percent > 0)
        {
            var angle = percent / 100 * 2 * Math.PI;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(8, 8 - radius), isFilled: false, isClosed: false);
                path.ArcTo(new Point(8 + radius * Math.Sin(angle), 8 - radius * Math.Cos(angle)),
                    new System.Windows.Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise,
                    isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, arc, geometry);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
