using FridgeMonitor.Core;

namespace FridgeMonitor.Tests;

/// <summary>The four loggers from Summer's email, set up as in sample-data/loggers.json.</summary>
static class Fixtures
{
    public static readonly DateTime Mon = new(2026, 9, 14);

    public static LoggerRegistry Registry() => new(new[]
    {
        new LoggerInfo("TL-0512", TempUnit.C, "yyyy-MM-dd HH:mm", [new("Jerusalem", "Dairy", new(2024, 1, 1))]),
        new LoggerInfo("TL-0417", TempUnit.C, "yyyy-MM-dd'T'HH:mm:ss",
        [
            new("Tel Aviv", "Walk-in", new(2024, 1, 1), new(2026, 9, 16, 10, 0, 0)),
            new("Tel Aviv", "Display 2", new(2026, 9, 16, 10, 30, 0)),
        ]),
        new LoggerInfo("TL-0231", TempUnit.F, "dd/MM/yyyy HH:mm", [new("Haifa", "Dairy", new(2024, 1, 1))]),
        new LoggerInfo("TL-0388", TempUnit.C, "yyyy-MM-dd HH:mm", [new("Rishon LeZion", "Cream cakes", new(2024, 1, 1))]),
    });

    /// <summary>Readings every 15 minutes from <paramref name="start"/>. null = ERR.</summary>
    public static List<Reading> Series(DateTime start, params double?[] temps) =>
        temps.Select((t, i) => new Reading("TL-0001", start.AddMinutes(15 * i), t, t?.ToString() ?? "ERR")).ToList();

    public static DateTime At(int hour, int minute) => Mon.AddHours(hour).AddMinutes(minute);

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FridgeMonitor.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
