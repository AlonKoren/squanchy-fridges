using FridgeMonitor.Core;
using static FridgeMonitor.Tests.Fixtures;

namespace FridgeMonitor.Tests;

public class RegistryTests
{
    readonly LoggerRegistry registry = Registry();
    static readonly FridgeKey WalkIn = new("Tel Aviv", "Walk-in");
    static readonly FridgeKey Display2 = new("Tel Aviv", "Display 2");

    [Fact]
    public void Logger_move_assigns_readings_by_time()
    {
        Assert.Equal(WalkIn, registry.FridgeAt("TL-0417", At(6, 0)));
        Assert.Equal(Display2, registry.FridgeAt("TL-0417", new DateTime(2026, 9, 17, 6, 0, 0)));
    }

    [Fact]
    public void Readings_while_the_logger_is_being_moved_belong_to_no_fridge()
    {
        Assert.Null(registry.FridgeAt("TL-0417", new DateTime(2026, 9, 16, 10, 0, 0)));
        Assert.Null(registry.FridgeAt("TL-0417", new DateTime(2026, 9, 16, 10, 15, 0)));
        Assert.Equal(Display2, registry.FridgeAt("TL-0417", new DateTime(2026, 9, 16, 10, 30, 0)));
    }

    [Fact]
    public void Fridge_left_without_a_logger_is_known()
    {
        Assert.Equal(new DateTime(2026, 9, 16, 10, 0, 0), registry.UnmonitoredSince(WalkIn, new DateTime(2026, 9, 20)));
        Assert.Null(registry.UnmonitoredSince(WalkIn, Mon));
    }

    static string Logger(string id, string assignments, string format = "yyyy-MM-dd HH:mm") =>
        $$"""{ "id": "{{id}}", "unit": "C", "dateFormat": "{{format}}", "assignments": [ {{assignments}} ] }""";

    static string Assign(string fridge, string from, string? to = null) =>
        $$"""{ "branch": "Tel Aviv", "fridge": "{{fridge}}", "from": "{{from}}"{{(to is null ? "" : $", \"to\": \"{to}\"")}} }""";

    static string Problems(params string[] loggers) =>
        Assert.Throws<InvalidDataException>(() => LoggerRegistry.FromJson($"[{string.Join(",", loggers)}]")).Message;

    [Fact]
    public void Overlapping_assignments_for_one_logger_are_rejected()
    {
        var message = Problems(Logger("TL-0417",
            Assign("Walk-in", "2024-01-01T00:00:00", "2026-09-17T00:00:00") + "," + Assign("Display 2", "2026-09-16T10:30:00")));
        Assert.Contains("TL-0417: in Tel Aviv / Walk-in and Tel Aviv / Display 2 at the same time", message);
    }

    [Fact]
    public void Assignment_ending_before_it_starts_is_rejected()
    {
        Assert.Contains("ends", Problems(Logger("TL-0417", Assign("Walk-in", "2026-09-17T00:00:00", "2026-09-16T00:00:00"))));
    }

    [Fact]
    public void Two_loggers_in_one_fridge_at_once_are_rejected()
    {
        var message = Problems(
            Logger("TL-0417", Assign("Walk-in", "2024-01-01T00:00:00")),
            Logger("TL-0418", Assign("Walk-in", "2026-01-01T00:00:00")));
        Assert.Contains("TL-0417 and TL-0418 assigned at the same time", message);
    }

    [Fact]
    public void Duplicate_id_missing_assignments_and_bad_date_format_are_all_listed()
    {
        var message = Problems(
            Logger("TL-0001", Assign("Walk-in", "2024-01-01T00:00:00")),
            Logger("TL-0001", Assign("Dairy", "2024-01-01T00:00:00")),
            Logger("TL-0002", "", "dd/MM/yyyy"));
        Assert.Contains("TL-0001: listed 2 times", message);
        Assert.Contains("TL-0002: no assignments", message);
        Assert.Contains("TL-0002: dateFormat \"dd/MM/yyyy\" can't be used", message);   // no time of day
    }

    [Fact]
    public void A_move_with_a_gap_in_between_is_valid()
    {
        LoggerRegistry.FromJson($"[{Logger("TL-0417",
            Assign("Walk-in", "2024-01-01T00:00:00", "2026-09-16T10:00:00") + "," + Assign("Display 2", "2026-09-16T10:30:00"))}]");
    }

    [Fact]
    public void Sample_registry_file_loads()
    {
        var r = LoggerRegistry.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "sample-data", "loggers.json")));
        Assert.Equal(TempUnit.F, r.Find("TL-0231")!.Unit);
        Assert.Equal(2, r.Find("TL-0417")!.Assignments.Count);
    }
}
