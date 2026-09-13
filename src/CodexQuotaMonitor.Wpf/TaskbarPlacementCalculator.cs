namespace CodexQuotaMonitor.Wpf;

public static class TaskbarPlacementCalculator
{
    public static TaskbarPlacement ScaledInWorkingArea(
        System.Drawing.Rectangle area, int width, int height, double scale, int? x = null, int? y = null)
    {
        scale = double.IsFinite(scale) ? Math.Clamp(scale, 0.5, 3.0) : 1.0;
        // Fit both dimensions together so a small screen never distorts the card.
        scale = Math.Min(scale, Math.Min((double)area.Width / width, (double)area.Height / height));
        return InWorkingArea(area, Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)), x, y);
    }

    // Coordinates and sizes are physical pixels, including negative monitor origins.
    public static TaskbarPlacement InWorkingArea(
        System.Drawing.Rectangle area, int width, int height, int? x = null, int? y = null)
    {
        width = Math.Clamp(width, 1, Math.Max(1, area.Width));
        height = Math.Clamp(height, 1, Math.Max(1, area.Height));
        return new TaskbarPlacement(
            Math.Clamp(x ?? area.Right - width - 10, area.Left, area.Right - width),
            Math.Clamp(y ?? area.Bottom - height - 10, area.Top, area.Bottom - height),
            width, height);
    }

    public static bool ShowFiveHour(QuotaSnapshot? lastValidQuota) =>
        lastValidQuota is null || lastValidQuota.Error is not null || lastValidQuota.FiveHour is not null;

    public static int DisplayWidth(int fullWidth, bool showFiveHour) =>
        (int)Math.Round(fullWidth * (showFiveHour ? 124.0 : 70.0) / Constants.DefaultWidth);

    private const uint EdgeLeft = 0;
    private const uint EdgeTop = 1;
    private const uint EdgeRight = 2;
    private const uint EdgeBottom = 3;

    public static TaskbarPlacement Compute(
        uint edge,
        NativeMethods.RECT taskbar,
        int preferredWidth,
        int fallbackHeight,
        int screenWidth,
        int screenHeight)
    {
        if (edge is EdgeTop or EdgeBottom && taskbar.Width > 0 && taskbar.Height > 0)
        {
            var width = Math.Min(Math.Max(1, preferredWidth), taskbar.Width);
            return Clamp(new TaskbarPlacement(taskbar.Left, taskbar.Top, width, taskbar.Height), screenWidth, screenHeight);
        }

        // A vertical taskbar has no meaningful horizontal "taskbar height". Keep the
        // compact overlay at the lower-left of the primary screen in that layout.
        if (edge is EdgeLeft or EdgeRight)
        {
            return Fallback(preferredWidth, fallbackHeight, screenWidth, screenHeight);
        }

        return Fallback(preferredWidth, fallbackHeight, screenWidth, screenHeight);
    }

    public static TaskbarPlacement Fallback(int preferredWidth, int height, int screenWidth, int screenHeight)
    {
        return Clamp(
            new TaskbarPlacement(0, Math.Max(0, screenHeight - height), preferredWidth, height),
            screenWidth,
            screenHeight);
    }

    private static TaskbarPlacement Clamp(TaskbarPlacement placement, int screenWidth, int screenHeight)
    {
        var width = Math.Clamp(placement.Width, 1, Math.Max(1, screenWidth));
        var height = Math.Clamp(placement.Height, 1, Math.Max(1, screenHeight));
        var x = Math.Clamp(placement.X, 0, Math.Max(0, screenWidth - width));
        var y = Math.Clamp(placement.Y, 0, Math.Max(0, screenHeight - height));
        return new TaskbarPlacement(x, y, width, height);
    }
}
