using System.Globalization;
using System.Text;
using FridgeMonitor.Core;
using FridgeMonitor.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FridgeMonitor.Web.Pages;

/// <summary>The inspector's question: when did this fridge go above 5°C, and for how long?</summary>
public class ReportModel(FleetService fleet, LoggerRegistry registry) : PageModel
{
    /// <summary>"Branch|Fridge", or empty for every fridge.</summary>
    [BindProperty(SupportsGet = true)] public string? F { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public List<FridgeKey> AllFridges { get; } = registry.AllFridges.OrderBy(f => f.Branch).ThenBy(f => f.Fridge).ToList();
    public DateRange Range { get; private set; } = null!;
    public List<FridgeReport> Reports { get; } = new();
    public double LimitC => fleet.Options.LimitC;
    public int MinExcursionMinutes => fleet.Options.MinExcursionMinutes;

    public static string Value(FridgeKey f) => $"{f.Branch}|{f.Fridge}";

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetCsvAsync()
    {
        await LoadAsync();
        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder("Branch,Fridge,Start,End,DurationMinutes,PeakC,Readings,Type,StillAbove,IncludesMissingData\r\n");
        foreach (var report in Reports)
            foreach (var e in report.Excursions)
                csv.AppendJoin(',',
                        Escape(report.Fridge.Branch), Escape(report.Fridge.Fridge),
                        e.Start.ToString("yyyy-MM-dd HH:mm", inv), e.End.ToString("yyyy-MM-dd HH:mm", inv),
                        ((int)e.Duration.TotalMinutes).ToString(inv), e.PeakC.ToString("0.0", inv), e.ReadingCount.ToString(inv),
                        e.IsBrief ? "Brief (door opening?)" : "Excursion", e.Ongoing ? "yes" : "no", e.IncludesGap ? "yes" : "no")
                   .Append("\r\n");

        // BOM so Excel opens it as UTF-8 (branch names, °).
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", $"excursions_{Range.FirstIso}_{Range.LastIso}.csv");
    }

    async Task LoadAsync()
    {
        Range = await DateRange.ResolveAsync(fleet, From, To);
        var selected = string.IsNullOrEmpty(F) ? AllFridges : AllFridges.Where(f => Value(f) == F).ToList();
        foreach (var fridge in selected)
            Reports.Add((await fleet.GetFridgeAsync(fridge, Range.Start, Range.End)).Report);
    }

    static string Escape(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
