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

        merged.WindowX = -1800;
        merged.WindowY = 120;
        SettingsStore.Save(path, merged);
        var restored = SettingsStore.Load(path);
        Equal<int?>(-1800, restored.WindowX, "saved negative monitor coordinate");
        Equal<int?>(120, restored.WindowY, "saved vertical position");
        var overridden = SettingsStore.ApplyCliOverrides(restored, cli);
        Equal(restored.WindowX, overridden.WindowX, "CLI preserves position");

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
