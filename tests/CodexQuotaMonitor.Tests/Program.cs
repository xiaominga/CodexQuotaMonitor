using System.IO;
using System.Text.Json;
using CodexQuotaMonitor.Wpf;

var tests = new (string Name, Action Body)[]
{
    ("quota JSON-RPC response parsing", TestQuotaParsing),
    ("weekly-only quota is not mislabeled as 5H", TestWeeklyOnlyQuotaParsing),
    ("settings defaults, JSON load, CLI override, corrupt fallback", TestSettings),
    ("formatting helpers", TestFormatting),
    ("taskbar overlay placement", TestTaskbarPlacement),
    ("floating placement and adaptive columns", TestFloatingLayout),
    ("transparent overlay corners", TestTransparentCorners),
    ("dynamic tray rings", () => RunSta(TestTrayRings)),
    ("rounded frame geometry", () => RunSta(TestRoundedFrame)),
    ("argument handling", TestArguments)
};

var passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {test.Name}: {ex}");
        return 1;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed");
return 0;

static void TestQuotaParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "test-limit",
            "limitName": "Test Limit",
            "planType": "plus",
            "primary": {
              "usedPercent": 57.25,
              "windowDurationMins": 300,
              "resetsAt": 4102444800
            },
            "secondary": {
              "usedPercent": 21,
              "windowDurationMins": 10080,
              "resetsAt": 4102448400
            }
          }
        }
        """);

    var snapshot = QuotaReader.ParseRateLimitResult(document.RootElement);
    Equal(null, snapshot.Error, "quota error");
    Equal("test-limit", snapshot.LimitId, "limit id");
    Near(42.75, snapshot.FiveHour!.RemainingPercent!.Value, 0.001, "5h remaining");
    Near(79.0, snapshot.Weekly!.RemainingPercent!.Value, 0.001, "weekly remaining");
}

static void TestWeeklyOnlyQuotaParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "codex",
            "primary": {
              "usedPercent": 25,
              "windowDurationMins": 10080,
              "resetsAt": 4102448400
            },
            "secondary": null
          }
        }
        """);

    var snapshot = QuotaReader.ParseRateLimitResult(document.RootElement);
    Equal<LimitWindow?>(null, snapshot.FiveHour, "disabled 5h window");
    Near(75.0, snapshot.Weekly!.RemainingPercent!.Value, 0.001, "weekly-only remaining");
    Equal(10080, snapshot.Weekly.WindowMins, "weekly-only duration");
}

static void TestSettings()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "codex-quota-native-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var path = Path.Combine(tempDir, "settings.json");
    try
    {
        var defaults = SettingsStore.Load(path);
        Equal(180, defaults.QuotaInterval, "default quota interval");
        Equal(false, defaults.GlassEffect, "normal palette by default");

        File.WriteAllText(path, """
            {
              "quota_interval": 60,
              "no_tray": true,
              "glass_effect": true,
              "window_width": 336,
              "red_threshold": 10,
              "amber_threshold": 25
            }
            """);
        var loaded = SettingsStore.Load(path);
        Equal(60, loaded.QuotaInterval, "loaded quota interval");
        Equal(true, loaded.NoTray, "loaded no tray");
        Equal(260, loaded.WindowWidth, "floating-card width migration");

        var cli = CliOptions.Parse(["--quota-interval", "300", "--tray"]);
        var merged = SettingsStore.ApplyCliOverrides(loaded, cli);
        Equal(300, merged.QuotaInterval, "cli quota override");
        Equal(false, merged.NoTray, "cli tray override");
        Equal(true, merged.GlassEffect, "CLI clone preserves glass setting");

        merged.WindowScale = 1.5;
        merged.WindowX = -1800;
        merged.WindowY = 120;
        SettingsStore.Save(path, merged);
        var restored = SettingsStore.Load(path);
        Equal(true, restored.GlassEffect, "glass setting survives save and reload");
        Equal<int?>(-1800, restored.WindowX, "saved negative monitor coordinate");
        Equal<int?>(120, restored.WindowY, "saved vertical position");
        var overridden = SettingsStore.ApplyCliOverrides(restored, cli);
        Equal(restored.WindowX, overridden.WindowX, "CLI preserves position");
        Equal(1.5, restored.WindowScale, "saved scale");
        Equal(1.5, overridden.WindowScale, "CLI preserves scale");
        var invalidScale = new AppSettings { WindowScale = double.NaN };
        invalidScale.Normalize();
        Equal(1.0, invalidScale.WindowScale, "invalid scale fallback");

        File.WriteAllText(path, "{ broken json");
        var fallback = SettingsStore.Load(path);
        Equal(180, fallback.QuotaInterval, "corrupt JSON fallback");
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

static void TestFormatting()
{
    Equal("abc", Formatting.Truncate("abc", 10), "truncate short");
    Equal("abcdefg...", Formatting.Truncate("abcdefghijk", 10), "truncate long");
    Equal("--", Formatting.RemainingText(null), "remaining missing");
    Equal("43", Formatting.RemainingText(42.75), "remaining percent");
}

static void TestTaskbarPlacement()
{
    var bottomRect = new NativeMethods.RECT { Left = 0, Top = 1032, Right = 1920, Bottom = 1080 };
    var bottom = TaskbarPlacementCalculator.Compute(3, bottomRect, 260, 48, 1920, 1080);
    Equal(new TaskbarPlacement(0, 1032, 260, 48), bottom, "bottom taskbar placement");

    var topRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 40 };
    var top = TaskbarPlacementCalculator.Compute(1, topRect, 260, 48, 1920, 1080);
    Equal(new TaskbarPlacement(0, 0, 260, 40), top, "top taskbar height");

    var verticalRect = new NativeMethods.RECT { Left = 0, Top = 0, Right = 48, Bottom = 1080 };
    var vertical = TaskbarPlacementCalculator.Compute(0, verticalRect, 260, 48, 1920, 1080);
    Equal(new TaskbarPlacement(0, 1032, 260, 48), vertical, "vertical taskbar fallback");
}

static void TestFloatingLayout()
{
    var area = new System.Drawing.Rectangle(0, 0, 1920, 1032);
    Equal(new TaskbarPlacement(1650, 974, 260, 48),
        TaskbarPlacementCalculator.InWorkingArea(area, 260, 48), "default avoids taskbar");
    Equal(new TaskbarPlacement(100, 200, 260, 48),
        TaskbarPlacementCalculator.InWorkingArea(area, 260, 48, 100, 200), "custom location");
    Equal(new TaskbarPlacement(1660, 984, 260, 48),
        TaskbarPlacementCalculator.InWorkingArea(area, 260, 48, 4000, 2000), "removed monitor recovery");
    Equal(new TaskbarPlacement(-270, 974, 260, 48),
        TaskbarPlacementCalculator.InWorkingArea(new(-1920, 0, 1920, 1032), 260, 48),
        "negative monitor origin");
    Equal(false, TaskbarPlacementCalculator.ShowFiveHour(
        new QuotaSnapshot(PlanType: "prolite", Weekly: new LimitWindow("Week", 86, 14, 10080))),
        "weekly-only hides 5H");
    Equal(true, TaskbarPlacementCalculator.ShowFiveHour(null), "initial loading is not absence");
    Equal(true, TaskbarPlacementCalculator.ShowFiveHour(new QuotaSnapshot(Error: "timeout")),
        "initial failure is not absence");
    Equal(true, TaskbarPlacementCalculator.ShowFiveHour(
        new QuotaSnapshot(FiveHour: new LimitWindow("5h"))), "5H returns");
    Equal(70, TaskbarPlacementCalculator.DisplayWidth(260, false), "compact width");
    Equal(124, TaskbarPlacementCalculator.DisplayWidth(260, true), "full width");
    Equal(new TaskbarPlacement(1520, 950, 390, 72),
        TaskbarPlacementCalculator.ScaledInWorkingArea(area, 260, 48, 1.5), "150 percent");
    Equal(new TaskbarPlacement(1715, 986, 195, 36),
        TaskbarPlacementCalculator.ScaledInWorkingArea(area, 260, 48, .75), "75 percent");
    Equal(new TaskbarPlacement(1382, 878, 528, 144),
        TaskbarPlacementCalculator.ScaledInWorkingArea(area, 176, 48, 3), "weekly-only 300 percent");
    var fitted = TaskbarPlacementCalculator.ScaledInWorkingArea(new(0, 0, 300, 100), 260, 48, 3);
    Equal(300, fitted.Width, "fit small screen width");
    Equal(55, fitted.Height, "fit small screen proportional height");
}

static void TestTransparentCorners()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var xml = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "OverlayTemplate.xaml"));
            var root = xml.Root!;
            // Load the production visual tree without code-behind, icons or live quota queries.
            foreach (var attribute in root.Attributes().ToArray())
            {
                if (attribute.Name.LocalName is "Class" or "Loaded" or "SourceInitialized" or
                    "Closing" or "MouseRightButtonUp" or "Icon")
                    attribute.Remove();
            }
            var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Parse(xml.ToString().Replace("clr-namespace:CodexQuotaMonitor.Wpf", "clr-namespace:CodexQuotaMonitor.Wpf;assembly=CodexQuotaMonitor.Wpf"));
            try
            {
                Equal(true, window.AllowsTransparency, "per-pixel window transparency");
                Equal((byte)0, ((System.Windows.Media.SolidColorBrush)window.Background).Color.A,
                    "window background alpha");
                var content = (System.Windows.FrameworkElement)window.Content;
                var viewbox = (System.Windows.Controls.Viewbox)content;
                Equal(System.Windows.Media.Stretch.Uniform, viewbox.Stretch, "uniform vector scaling");
                var frame = (QuotaCardFrame)window.FindName("CardFrame");
                Equal(true, frame.CacheMode is null, "no WPF bitmap cache");
                Equal(false, frame.GlassEnabled, "glass defaults off");
                Equal(false, frame.IsHitTestVisible, "frame does not block input");
                foreach (var glassEnabled in new[] { false, true })
                foreach (var baseWidth in new[] { 124, 70 })
                foreach (var scale in new[] { .5, .75, 1.0, 1.25, 1.5, 2.0, 3.0 })
                foreach (double? remaining in new double?[] { 14, 96, 100, 0, null })
                {
                    frame.GlassEnabled = glassEnabled;
                    ((System.Windows.FrameworkElement)viewbox.Child).Width = baseWidth;
                    var grid = (System.Windows.Controls.Grid)window.FindName("RootGrid");
                    grid.Children.Clear();
                    grid.ColumnDefinitions.Clear();
                    var labels = baseWidth == 124 ? new[] { "5H", "WK" } : new[] { "WK" };
                    foreach (var label in labels)
                    {
                        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(label == "5H" ? 54.35 : 66.35, System.Windows.GridUnitType.Star) });
                        var gauge = new QuotaMetricBlock(label, new AppSettings())
                        { Margin = new System.Windows.Thickness(0) };
                        gauge.SetMetric(label == "WK" && baseWidth == 124 ? 100 : remaining, label == "WK" ? "6d 23h" : "2h 15m", new AppSettings());
                        System.Windows.Controls.Grid.SetColumn(gauge, grid.ColumnDefinitions.Count - 1);
                        grid.Children.Add(gauge);
                        var divider = new System.Windows.Controls.Border
                        {
                            Width = 0.5, Margin = new System.Windows.Thickness(0, 10.35, 0, 9.35),
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                            Background = new System.Windows.Media.SolidColorBrush(Formatting.ColorFromHex("#2C3440"))
                        };
                        System.Windows.Controls.Grid.SetColumn(divider, grid.ColumnDefinitions.Count - 1);
                        if (label == "5H") grid.Children.Add(divider);
                    }
                    var refresh = new RefreshStatusBlock { Margin = new System.Windows.Thickness(0, 0.35, 1.35, 0) };
                    refresh.SetStatus(new DateTimeOffset(2026, 9, 12, 16, 55, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 9, 12, 16, 57, 0, TimeSpan.Zero), "", "#2DD4A8");
                    System.Windows.Controls.Grid.SetColumn(refresh, grid.ColumnDefinitions.Count - 1);
                    grid.Children.Add(refresh);
                    var width = (int)Math.Round(baseWidth * scale);
                    var height = (int)Math.Round(48 * scale);
                    content.Measure(new System.Windows.Size(width, height));
                    content.Arrange(new System.Windows.Rect(0, 0, width, height));
                    content.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var previewDirectory = Environment.GetEnvironmentVariable("QUOTA_PREVIEW_DIRECTORY");
                    if (!string.IsNullOrWhiteSpace(previewDirectory))
                    {
                        Directory.CreateDirectory(previewDirectory);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(previewDirectory, $"overlay-{(glassEnabled ? "glass" : "normal")}-{baseWidth}-{scale * 100:0}-{remaining?.ToString() ?? "unknown"}.png"));
                        encoder.Save(output);
                    }
                    var pixels = new byte[width * height * 4];
                    bitmap.CopyPixels(pixels, width * 4, 0);
                    if (scale == 3.0)
                    {
                        // The inner border seam must not expose the desktop through antialiasing.
                        for (var inset = 5; inset <= 9; inset++)
                        {
                            Equal((byte)255, pixels[(inset * width + width / 2) * 4 + 3], "opaque top border join");
                            Equal((byte)255, pixels[((height / 2) * width + inset) * 4 + 3], "opaque left border join");
                        }
                    }
                    foreach (var (x, y) in new[] { (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1) })
                        Equal((byte)0, pixels[(y * width + x) * 4 + 3], "scaled transparent corner");
                    if (pixels[((height / 2) * width + width / 2) * 4 + 3] == 0)
                        throw new InvalidOperationException("Scaled card body must remain visible");
                }
            }
            finally { window.Close(); }
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw new InvalidOperationException("WPF transparency regression", failure);
}

static void TestArguments()
{
    var options = CliOptions.Parse([
        "--check",
        "--codex-home", "C:\\CodexHome\\.codex",
        "--codex-exe", "C:\\Tools\\codex.exe",
        "--quota-interval", "600",
        "--no-tray"
    ]);
    Equal(true, options.Check, "check flag");
    Equal(false, options.Once, "once flag");
    Equal("C:\\CodexHome\\.codex", options.CodexHome, "codex home");
    Equal("C:\\Tools\\codex.exe", options.CodexExe, "codex exe");
    Equal(600, options.QuotaInterval, "quota interval");
    Equal(true, options.NoTray, "no tray");

    var tray = CliOptions.Parse(["--tray"]);
    Equal(false, tray.NoTray, "tray override");
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void Near(double expected, double actual, double tolerance, string label)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void TestTrayRings()
{
    foreach (var size in new[] { 16, 20, 24, 32, 128 })
    foreach (var light in new[] { false, true })
    {
        using var missing = QuotaTrayIcon.RenderBitmap(null, double.NaN, new AppSettings(), size, light);
        var quadrants = new long[4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            quadrants[(x >= size / 2 ? 1 : 0) + (y >= size / 2 ? 2 : 0)] += missing.GetPixel(x, y).A;
        Equal(true, quadrants.Max() - quadrants.Min() <= quadrants.Sum() * .02 + 255,
            "unavailable arcs have symmetric quadrant coverage");
        if (size != 128) continue;
        foreach (var radius in new[] { 6.17, 3.29 })
        for (var direction = 0; direction < 8; direction++)
        {
            var angle = direction * Math.PI / 4;
            var x = (int)((8 + radius * 1.09 * Math.Sin(angle)) * size / 16);
            var y = (int)((8 - radius * 1.09 * Math.Cos(angle)) * size / 16);
            var alpha = missing.GetPixel(x, y).A;
            if (direction % 2 == 0) Equal((byte)255, alpha, "unavailable arcs centered on cardinal directions");
            else Equal((byte)0, alpha, "unavailable gaps centered on diagonals");
        }
    }
    // Compare radial extents, independent of segment placement and palette. This catches
    // either state changing thickness or radius without the other following it.
    using (var segmented = QuotaTrayIcon.RenderBitmap(null, null, new AppSettings(), 128))
    using (var solid = QuotaTrayIcon.RenderBitmap(100, 100, new AppSettings(), 128))
    {
        static (double Inner, double Outer) RadialBounds(System.Drawing.Bitmap image, bool outer)
        {
            var min = double.MaxValue; var max = 0.0;
            for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                var dx = (x + .5) * 16 / image.Width - 8;
                var dy = (y + .5) * 16 / image.Height - 8;
                var r = Math.Sqrt(dx * dx + dy * dy);
                if (image.GetPixel(x, y).A < 128 || (r > 5) != outer) continue;
                min = Math.Min(min, r); max = Math.Max(max, r);
            }
            return (min, max);
        }
        foreach (var outer in new[] { false, true })
        {
            var segments = RadialBounds(segmented, outer); var line = RadialBounds(solid, outer);
            Near(line.Inner, segments.Inner, .15, "segmented and solid rings share inner boundary");
            Near(line.Outer, segments.Outer, .15, "segmented and solid rings share outer boundary");
        }
    }
    foreach (var size in new[] { 16, 24, 32, 128 })
    foreach (var light in new[] { false, true })
    {
        var settings = new AppSettings { RedThreshold = 0, AmberThreshold = 0 };
        var expected = Formatting.ColorFromHex(light ? "#00AD68" : "#55F2A5");
        using var quarter = QuotaTrayIcon.RenderBitmap(null, 25, settings, size, light);
        var checkedEdges = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = ((x + .5) * 16 / size - 8) / 1.09;
            var dy = ((y + .5) * 16 / size - 8) / 1.09;
            var pixel = quarter.GetPixel(x, y);
            // Outer arc, away from the rounded endpoints. Its antialiased edges must
            // contain only progress color, not a gray track underneath.
            if (dx <= 1.5 || dy >= -1.5 || dx * dx + dy * dy < 25 || pixel.A < 40) continue;
            Near(expected.R, pixel.R, 8, "progress edge has no gray red-channel contamination");
            Near(expected.G, pixel.G, 8, "progress edge has no gray green-channel contamination");
            Near(expected.B, pixel.B, 8, "progress edge has no gray blue-channel contamination");
            checkedEdges++;
        }
        Equal(true, checkedEdges > 0, "sampled visible progress edge pixels");
        foreach (var value in new[] { 0.0, .01, 1, 99, 99.99, 100 })
        {
            using var image = QuotaTrayIcon.RenderBitmap(value, value, settings, size, light);
            Equal((byte)0, image.GetPixel(0, 0).A, "boundary states preserve transparent corners");
            Equal(value == 0, image.GetPixel(size / 2, size / 2).A > 80,
                "only zero quota has central exhausted marker");
        }
    }
    foreach (var size in new[] { 16, 20, 24, 32 })
    foreach (var lightTaskbar in new[] { false, true })
    {
        using var bitmap = QuotaTrayIcon.RenderBitmap(14, 100, new AppSettings(), size, lightTaskbar);
        using var reversed = QuotaTrayIcon.RenderBitmap(100, 14, new AppSettings(), size, lightTaskbar);
        using var empty = QuotaTrayIcon.RenderBitmap(0, 0, new AppSettings(), size, lightTaskbar);
        using var unavailable = QuotaTrayIcon.RenderBitmap(null, null, new AppSettings(), size, lightTaskbar);
        using var quarter = QuotaTrayIcon.RenderBitmap(0, 25, new AppSettings(), size, lightTaskbar);
        using var threeQuarters = QuotaTrayIcon.RenderBitmap(0, 75, new AppSettings(), size, lightTaskbar);
        using var outerEmpty = QuotaTrayIcon.RenderBitmap(100, 0, new AppSettings(), size, lightTaskbar);
        using var invalid = QuotaTrayIcon.RenderBitmap(double.NaN, double.PositiveInfinity, new AppSettings(), size, lightTaskbar);
        Equal(true, empty.GetPixel(size / 2, size / 2).A > 80, "both exhausted show minus marker");
        Equal(true, quarter.GetPixel(size / 2, size / 2).A > 80, "inner exhausted shows minus marker");
        Equal(true, outerEmpty.GetPixel(size / 2, size / 2).A > 80, "outer exhausted shows minus marker");
        Equal((byte)0, unavailable.GetPixel(size / 2, size / 2).A, "missing data has no exhausted marker");
        Equal((byte)0, invalid.GetPixel(size / 2, size / 2).A, "nonfinite data has no exhausted marker");
        var bottom = threeQuarters.GetPixel(size / 2, (int)(14.5 * size / 16));
        var quarterBottom = quarter.GetPixel(size / 2, (int)(14.5 * size / 16));
        Equal(true, bottom.G > bottom.R + 30, "major arc covers bottom of weekly ring");
        Equal(true, Math.Abs(quarterBottom.G - quarterBottom.R) < 30, "quarter arc leaves bottom as track");
        Equal((byte)0, bitmap.GetPixel(0, 0).A, "transparent tray corner");
        Equal((byte)0, bitmap.GetPixel(size / 2, size / 2).A, "open ring center");
        var innerRed = 0; var outerGreen = 0; var outerRed = 0; var distinct = 0;
        var red = Formatting.ColorFromHex(lightTaskbar ? "#FF4059" : "#FFB2BC");
        var green = Formatting.ColorFromHex(lightTaskbar ? "#00AD68" : "#55F2A5");
        static bool Matches(System.Drawing.Color pixel, System.Windows.Media.Color expected) =>
            pixel.A > 80 && Math.Abs(pixel.R - expected.R) <= 10 &&
            Math.Abs(pixel.G - expected.G) <= 10 && Math.Abs(pixel.B - expected.B) <= 10;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            var distance = Math.Sqrt(Math.Pow((x + .5) * 16 / size - 8, 2) + Math.Pow((y + .5) * 16 / size - 8, 2));
            if (distance < 5 && Matches(pixel, red)) innerRed++;
            if (distance > 5 && Matches(pixel, green)) outerGreen++;
            var other = reversed.GetPixel(x, y);
            if (distance > 5 && Matches(other, red)) outerRed++;
            if (empty.GetPixel(x, y) != unavailable.GetPixel(x, y)) distinct++;
        }
        Equal(true, innerRed > 0 && outerGreen > 0 && outerRed > 0, "inner 5H and outer weekly colors");
        Equal(true, distinct > 0, "unavailable differs from zero quota");
        using var icon = QuotaTrayIcon.Create(14, 100, new AppSettings(), size, lightTaskbar);
        using var iconBitmap = icon.ToBitmap();
        Equal(size, iconBitmap.Width, "owned icon remains valid after source disposal");
    }
    foreach (var lightTaskbar in new[] { false, true })
    {
        var settings = new AppSettings { RedThreshold = 20, AmberThreshold = 60 };
        // Sample well inside strokes at high resolution to avoid edge coverage differences.
        foreach (var remaining in new[] { 19.0, 20.0, 59.0, 60.0, 100.0 })
        {
            using var image = QuotaTrayIcon.RenderBitmap(remaining, remaining, settings, 128, lightTaskbar);
            var expected = Formatting.ColorFromHex(remaining < 20
                ? (lightTaskbar ? "#FF4059" : "#FFB2BC")
                : remaining < 60 ? (lightTaskbar ? "#FFC400" : "#FFF36A")
                : (lightTaskbar ? "#00AD68" : "#55F2A5"));
            foreach (var y in new[] { 12, 35 })
            {
                var actual = image.GetPixel(66, y);
                Equal(expected.R, actual.R, "quota arc red channel respects custom thresholds");
                Equal(expected.G, actual.G, "quota arc green channel respects custom thresholds");
                Equal(expected.B, actual.B, "quota arc blue channel respects custom thresholds");
            }
        }
        using var singleEmpty = QuotaTrayIcon.RenderBitmap(0, 8, settings, 128, lightTaskbar);
        var track = singleEmpty.GetPixel(64, 116);
        Equal((byte)255, track.A, "track is opaque");
        Equal((byte)(lightTaskbar ? 52 : 98), track.R, "track adapts to taskbar theme");
        foreach (var remaining in new[] { 19.0, 20.0, 60.0 })
        {
            using var image = QuotaTrayIcon.RenderBitmap(remaining, remaining, settings, 128, lightTaskbar);
            var progress = image.GetPixel(66, 12);
            static double Luminance(System.Drawing.Color color)
            {
                static double Linear(byte channel)
                {
                    var value = channel / 255.0;
                    return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                }
                return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
            }
            var a = Luminance(progress); var b = Luminance(track);
            Equal(true, (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= (!lightTaskbar && remaining == 20 ? 4.5 : 3),
                "dark-theme yellow retains 4.5:1 contrast; other progress colors retain at least 3:1");
        }
        var exhausted = singleEmpty.GetPixel(64, 92);
        Equal((byte)207, exhausted.R, "only exhausted ring uses muted red");
        Equal((byte)119, exhausted.G, "exhausted track green channel");
        Equal((byte)131, exhausted.B, "exhausted track blue channel");
    }
}
static void RunSta(Action action)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { action(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw new InvalidOperationException("WPF rendering regression", failure);
}

static void TestRoundedFrame()
{
    foreach (var width in new[] { 70, 124 })
    foreach (var glass in new[] { false, true })
    {
        var frame = new QuotaCardFrame { Width = width, Height = 48, GlassEnabled = glass };
        frame.Measure(new System.Windows.Size(width, 48));
        frame.Arrange(new System.Windows.Rect(0, 0, width, 48));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width * 4, 48 * 4, 384, 384, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(frame);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        byte Alpha(int x, int y) => pixels[y * stride + x * 4 + 3];
        Equal((byte)0, Alpha(0, 0), "outside corner transparent");
        // This point is inside a circular radius-7 corner, but outside a 7px diagonal bevel.
        Equal((byte)255, Alpha(14, 14), "round arc is not a chamfer");
        var antialiasedPixels = 0;
        for (var y = 0; y < 32; y++)
        {
            var horizontalCoverageDifference = 0;
            var verticalCoverageDifference = 0;
            for (var x = 0; x < 32; x++)
            {
                var alpha = Alpha(x, y);
                if (alpha is > 0 and < 255) antialiasedPixels++;
                var distance = Math.Sqrt(Math.Pow(x + .5 - 32, 2) + Math.Pow(y + .5 - 32, 2));
                // Radius 7 DIP at 384 DPI is 28 pixels. Only its one-pixel boundary may blend.
                if (distance < 27) Equal((byte)255, alpha, "opaque circular corner interior");
                if (distance > 29) Equal((byte)0, alpha, "transparent circular corner exterior");
                horizontalCoverageDifference += alpha - Alpha(bitmap.PixelWidth - 1 - x, y);
                verticalCoverageDifference += Alpha(y, x) - Alpha(y, bitmap.PixelHeight - 1 - x);
            }
            // Native WPF coverage is quantized; compare silhouette coverage within one pixel,
            // rather than requiring the old analytical rasterizer's identical alpha bytes.
            Near(0, horizontalCoverageDifference, 255, "horizontal corner coverage symmetry");
            Near(0, verticalCoverageDifference, 255, "vertical corner coverage symmetry");
        }
        Equal(true, antialiasedPixels > 0, "native antialiasing blends corner edges");
    }
}
