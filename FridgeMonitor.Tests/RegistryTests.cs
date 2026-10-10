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

    [Fact]
    public void Sample_registry_file_loads()
    {
        var r = LoggerRegistry.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "sample-data", "loggers.json")));
        Assert.Equal(TempUnit.F, r.Find("TL-0231")!.Unit);
        Assert.Equal(2, r.Find("TL-0417")!.Assignments.Count);
    }
}
