using FridgeMonitor.Core;
using FridgeMonitor.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FridgeMonitor.Web.Pages;

public class FridgeModel(FleetService fleet, LoggerRegistry registry) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Branch { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Fridge { get; set; } = "";
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public FridgeKey Key { get; private set; } = null!;
    public DateRange Range { get; private set; } = null!;
    public FridgeReport Report { get; private set; } = null!;
    public HtmlString Chart { get; private set; } = HtmlString.Empty;
    public double LimitC => fleet.Options.LimitC;

    /// <summary>Text alternative for the chart: lowest, highest, average and time above the limit.</summary>
    public (double Min, double Max, double Avg, TimeSpan Above)? Stats { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var key = registry.AllFridges.FirstOrDefault(f => Names.Same(f.Branch, Branch) && Names.Same(f.Fridge, Fridge));
        if (key is null) return NotFound();
        Key = key;

        Range = await DateRange.ResolveAsync(fleet, From, To);
        var (report, readings) = await fleet.GetFridgeAsync(key, Range.Start, Range.End);
        Report = report;

        var valid = readings.Where(r => !r.IsError).ToList();
        string summary = $"Temperature chart, {Range}: no readings.";
        if (valid.Count > 0)
        {
            var temps = valid.Select(r => r.TempC!.Value).ToList();
            // Only the part inside the chosen dates, like the inspector report: the chart shows only these dates.
            var periodEnd = Range.LastDay.AddDays(1);   // midnight after the last day
            var above = TimeSpan.FromTicks(report.Excursions.Where(e => !e.IsBrief)
                .Sum(e => e.DurationWithin(Range.Start, periodEnd).Ticks));
            Stats = (temps.Min(), temps.Max(), temps.Average(), above);
            summary = $"Temperature chart, {Range}: lowest {Fmt.Temp(temps.Min())}, highest {Fmt.Temp(temps.Max())}, " +
                      $"average {Fmt.Temp(temps.Average())}, {Fmt.Duration(above)} above {Fmt.Temp(LimitC)}.";
        }
        var gapThreshold = FridgeAnalyzer.ExpectedInterval(valid) * fleet.Options.GapFactor;
        Chart = TempChart.Render(readings, Range.Start, Range.End, LimitC, gapThreshold, summary);
        return Page();
    }
}
