using System.Globalization;
using FridgeMonitor.Core;
using FridgeMonitor.Web.Services;

namespace FridgeMonitor.Web;

/// <summary>Folder the "Load sample week" button imports from.</summary>
public sealed record SampleDataFolder(string Path);

public static class StatusUi
{
    public static string Css(FridgeStatus s) => s switch
    {
        FridgeStatus.Alarm => "alarm",
        FridgeStatus.Warning => "warning",
        FridgeStatus.NoData => "nodata",
        _ => "ok",
    };

    public static string Label(FridgeStatus s) => s switch
    {
        FridgeStatus.Alarm => "Alarm",
        FridgeStatus.Warning => "Check",
        FridgeStatus.NoData => "No data",
        _ => "OK",
    };
}

/// <summary>Whole days, first to last inclusive. Defaults to the 7 days ending on the latest reading.</summary>
public sealed record DateRange(DateTime FirstDay, DateTime LastDay)
{
    public DateTime Start => FirstDay;
    public DateTime End => LastDay.AddDays(1).AddSeconds(-1);

    // Invariant, so a server culture with another calendar can't break the date inputs.
    public string FirstIso => FirstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string LastIso => LastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public override string ToString() =>
        $"{FirstDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)} – {LastDay.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";

    public static async Task<DateRange> ResolveAsync(FleetService fleet, DateTime? from, DateTime? to)
    {
        var last = (to ?? await fleet.LatestReadingAsync() ?? DateTime.Today).Date;
        var first = (from ?? last.AddDays(-6)).Date;
        if (first > last) (first, last) = (last, first);
        return new DateRange(first, last);
    }
}
