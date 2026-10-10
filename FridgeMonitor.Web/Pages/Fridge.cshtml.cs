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

    public async Task<IActionResult> OnGetAsync()
    {
        var key = registry.AllFridges.FirstOrDefault(f => Names.Same(f.Branch, Branch) && Names.Same(f.Fridge, Fridge));
        if (key is null) return NotFound();
        Key = key;

        Range = await DateRange.ResolveAsync(fleet, From, To);
        var (report, readings) = await fleet.GetFridgeAsync(key, Range.Start, Range.End);
        Report = report;

        var valid = readings.Where(r => !r.IsError).ToList();
        var gapThreshold = FridgeAnalyzer.ExpectedInterval(valid) * fleet.Options.GapFactor;
        Chart = TempChart.Render(readings, Range.Start, Range.End, LimitC, gapThreshold);
        return Page();
    }
}
