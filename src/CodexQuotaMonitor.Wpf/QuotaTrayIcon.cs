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
    private static readonly Color ExhaustedColor = Color.FromRgb(207, 119, 131);

    public static bool UsesLightTaskbar()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static Icon Create(double? fiveHour, double? weekly, AppSettings settings, int size, bool lightTaskbar = false)
    {
        using var bitmap = RenderBitmap(fiveHour, weekly, settings, size, lightTaskbar);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public static Bitmap RenderBitmap(double? fiveHour, double? weekly, AppSettings settings, int size, bool lightTaskbar = false)
    {
        size = Math.Clamp(size, 16, 256);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 16.0, size / 16.0));
            // The outer stroke stays within the tray slot: (6.5 + 1.65 / 2) * 1.09 < 8.
            dc.PushTransform(new ScaleTransform(1.09, 1.09, 8, 8));
            var trackColor = lightTaskbar ? Color.FromRgb(52, 65, 84) : Color.FromRgb(98, 110, 128);
            DrawRing(dc, weekly, settings, 6.5, 1.65, trackColor, lightTaskbar);
            DrawRing(dc, fiveHour, settings, 3.6, 1.55, trackColor, lightTaskbar);
            if (IsExhausted(fiveHour) || IsExhausted(weekly))
            {
                var marker = new Pen(new SolidColorBrush(ExhaustedColor), 1)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(marker, new Point(6.8, 8), new Point(9.2, 8));
            }
            dc.Pop();
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

    private static bool IsExhausted(double? remaining) =>
        remaining.HasValue && double.IsFinite(remaining.Value) && remaining.Value <= 0;

    private static Color ProgressColor(double remaining, AppSettings settings, bool lightTaskbar)
    {
        // Keep the existing threshold semantics; tray-sized strokes need their own contrast palette.
        if (remaining < settings.RedThreshold)
            return Formatting.ColorFromHex(lightTaskbar ? "#FF4059" : "#FFB2BC");
        if (remaining < settings.AmberThreshold)
            return Formatting.ColorFromHex(lightTaskbar ? "#FFC400" : "#FFF36A");
        return Formatting.ColorFromHex(lightTaskbar ? "#00AD68" : "#55F2A5");
    }

    private static void DrawRing(DrawingContext dc, double? remaining, AppSettings settings, double radius, double stroke, Color trackColor, bool lightTaskbar)
    {
        var center = new Point(8, 8);
        // All states share these dimensions; only their color and arc coverage differ.
        var ringStroke = stroke * 1.4;
        var ringRadius = radius - (ringStroke - stroke) / 2;
        var track = new Pen(new SolidColorBrush(IsExhausted(remaining) ? ExhaustedColor : trackColor), ringStroke);
        if (!remaining.HasValue || !double.IsFinite(remaining.Value))
        {
            // Four equal 70-degree arcs centered at top, right, bottom and left.
            // Fixed angles leave 20-degree diagonal gaps regardless of stroke width.
            var segments = new StreamGeometry();
            using (var path = segments.Open())
            {
                for (var i = 0; i < 4; i++)
                {
                    var start = (i * 90 - 35) * Math.PI / 180;
                    var end = (i * 90 + 35) * Math.PI / 180;
                    path.BeginFigure(new Point(8 + ringRadius * Math.Sin(start), 8 - ringRadius * Math.Cos(start)),
                        isFilled: false, isClosed: false);
                    path.ArcTo(new Point(8 + ringRadius * Math.Sin(end), 8 - ringRadius * Math.Cos(end)),
                        new System.Windows.Size(ringRadius, ringRadius), 0, false, SweepDirection.Clockwise,
                        isStroked: true, isSmoothJoin: false);
                }
            }
            segments.Freeze();
            dc.DrawGeometry(null, track, segments);
            return;
        }
        var arc = new Pen(new SolidColorBrush(ProgressColor(remaining.Value, settings, lightTaskbar)), ringStroke)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var percent = Math.Clamp(remaining.Value, 0, 100);
        if (percent >= 100) dc.DrawEllipse(null, arc, center, ringRadius, ringRadius);
        else if (percent <= 0) dc.DrawEllipse(null, track, center, ringRadius, ringRadius);
        else if (percent > 0)
        {
            var angle = percent / 100 * 2 * Math.PI;
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(8, 8 - ringRadius), isFilled: false, isClosed: false);
                path.ArcTo(new Point(8 + ringRadius * Math.Sin(angle), 8 - ringRadius * Math.Cos(angle)),
                    new System.Windows.Size(ringRadius, ringRadius), 0, percent > 50, SweepDirection.Clockwise,
                    isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze();
            // Subtract the complete rounded stroke before drawing progress. Painting a
            // full track underneath lets its gray antialiasing show through colored edges.
            const double tolerance = 0.001;
            var progressShape = geometry.GetWidenedPathGeometry(arc, tolerance, ToleranceType.Absolute);
            var ringShape = new EllipseGeometry(center, ringRadius, ringRadius)
                .GetWidenedPathGeometry(track, tolerance, ToleranceType.Absolute);
            var uncoveredTrack = Geometry.Combine(ringShape, progressShape, GeometryCombineMode.Exclude,
                null, tolerance, ToleranceType.Absolute);
            dc.DrawGeometry(track.Brush, null, uncoveredTrack);
            dc.DrawGeometry(arc.Brush, null, progressShape);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
