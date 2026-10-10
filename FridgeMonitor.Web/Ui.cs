using System.Globalization;
using FridgeMonitor.Core;
using FridgeMonitor.Web.Services;
using Microsoft.AspNetCore.Html;

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

    /// <summary>Status icon (Lucide paths, ISC licence). Decorative: the text label next to it carries the meaning.</summary>
    public static HtmlString Icon(FridgeStatus s) => Svg(s switch
    {
        FridgeStatus.Alarm => "<path d=\"m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3\"/><path d=\"M12 9v4\"/><path d=\"M12 17h.01\"/>",
        FridgeStatus.Warning => "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M12 8v4\"/><path d=\"M12 16h.01\"/>",
        FridgeStatus.NoData => "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"M8 12h8\"/>",
        _ => "<circle cx=\"12\" cy=\"12\" r=\"10\"/><path d=\"m9 12 2 2 4-4\"/>",
    });

    public static HtmlString Thermometer => Svg("<path d=\"M14 4v10.54a4 4 0 1 1-4 0V4a2 2 0 0 1 4 0Z\"/>");

    static HtmlString Svg(string paths) => new(
        "<svg class=\"icon\" aria-hidden=\"true\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
        $"stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">{paths}</svg>");
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
