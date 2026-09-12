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
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
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

        File.WriteAllText(path, """
            {
              "quota_interval": 60,
              "no_tray": true,
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

        merged.WindowScale = 1.5;
        merged.WindowX = -1800;
        merged.WindowY = 120;
        SettingsStore.Save(path, merged);
        var restored = SettingsStore.Load(path);
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
    Equal(176, TaskbarPlacementCalculator.DisplayWidth(260, false), "compact width");
    Equal(260, TaskbarPlacementCalculator.DisplayWidth(260, true), "full width");
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
            var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Parse(xml.ToString());
            try
            {
                Equal(true, window.AllowsTransparency, "per-pixel window transparency");
                Equal((byte)0, ((System.Windows.Media.SolidColorBrush)window.Background).Color.A,
                    "window background alpha");
                var content = (System.Windows.FrameworkElement)window.Content;
                var viewbox = (System.Windows.Controls.Viewbox)content;
                Equal(System.Windows.Media.Stretch.Uniform, viewbox.Stretch, "uniform vector scaling");
                foreach (var baseWidth in new[] { 260, 176 })
                foreach (var scale in new[] { .75, 1.0, 1.5, 3.0 })
                {
                    ((System.Windows.FrameworkElement)viewbox.Child).Width = baseWidth;
                    var grid = (System.Windows.Controls.Grid)window.FindName("RootGrid");
                    grid.Children.Clear();
                    grid.ColumnDefinitions.Clear();
                    var labels = baseWidth == 260 ? new[] { "5H", "WK" } : new[] { "WK" };
                    foreach (var label in labels)
                    {
                        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
                        var gauge = new MetricGaugeBlock(label, new AppSettings())
                        { Margin = new System.Windows.Thickness(0, 0, 2, 0) };
                        gauge.SetMetric(label == "WK" ? 100 : 14, "6d 23h", new AppSettings());
                        System.Windows.Controls.Grid.SetColumn(gauge, grid.ColumnDefinitions.Count - 1);
                        grid.Children.Add(gauge);
                        var divider = new System.Windows.Controls.Border
                        {
                            Width = 1, Margin = new System.Windows.Thickness(0, 5, 0, 5),
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(56, 255, 255, 255))
                        };
                        System.Windows.Controls.Grid.SetColumn(divider, grid.ColumnDefinitions.Count - 1);
                        grid.Children.Add(divider);
                    }
                    grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                    { Width = new System.Windows.GridLength(1.08, System.Windows.GridUnitType.Star) });
                    var refresh = new RefreshStatusBlock { Margin = new System.Windows.Thickness(3, 0, 0, 0) };
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
                        using var output = File.Create(Path.Combine(previewDirectory, $"overlay-{baseWidth}-{scale * 100:0}.png"));
                        encoder.Save(output);
                    }
                    var pixels = new byte[width * height * 4];
                    bitmap.CopyPixels(pixels, width * 4, 0);
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
