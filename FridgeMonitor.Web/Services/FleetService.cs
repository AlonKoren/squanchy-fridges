using FridgeMonitor.Core;
using FridgeMonitor.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FridgeMonitor.Web.Services;

/// <summary>Loads stored readings and runs the Core analysis on them.</summary>
public sealed class FleetService(FridgeDb db, LoggerRegistry registry, IOptions<AnalysisOptions> options)
{
    public AnalysisOptions Options => options.Value;

    // Readings this far either side of a period are loaded as context, so an excursion crossing the edge
    // keeps its real start and end. Only an excursion longer than this would still be cut off.
    static readonly TimeSpan Context = TimeSpan.FromDays(7);

    /// <summary>Latest reading time in the database: the "now" files are judged against.</summary>
    public Task<DateTime?> LatestReadingAsync() => db.Readings.MaxAsync(r => (DateTime?)r.Time);

    /// <summary>Every registered fridge over the last <paramref name="days"/> days up to the latest reading. Null if nothing uploaded yet.</summary>
    public async Task<FleetReport?> GetFleetAsync(int days = 7)
    {
        var latest = await LatestReadingAsync();
        if (latest is null) return null;

        var period = new Period(latest.Value.AddDays(-days), latest.Value);
        var contextStart = period.Start - Context;
        var readings = await db.Readings.Where(r => r.Time >= contextStart).ToListAsync();
        return FleetAnalyzer.Analyze(readings.Select(r => r.ToReading()), registry, Options, latest, period);
    }

    /// <summary>One fridge between two times, e.g. for the inspector. Uses whichever loggers were in it then.</summary>
    public async Task<(FridgeReport Report, List<Reading> Readings)> GetFridgeAsync(FridgeKey fridge, DateTime from, DateTime to)
    {
        var period = new Period(from, to);
        var withContext = await GetReadingsAsync(fridge, from - Context, to + Context);
        var latest = await LatestReadingAsync() ?? to;
        var asOf = latest < to ? latest : to;
        var report = FridgeAnalyzer.Analyze(fridge, withContext, asOf, Options, registry.UnmonitoredSince(fridge, asOf), period);
        return (report, withContext.Where(r => period.Contains(r.Time)).ToList());
    }

    public async Task<List<Reading>> GetReadingsAsync(FridgeKey fridge, DateTime from, DateTime to)
    {
        var loggers = registry.Loggers.Where(l => l.Assignments.Any(a => a.Key == fridge)).Select(l => l.Id).ToList();
        var rows = await db.Readings
            .Where(r => loggers.Contains(r.LoggerId) && r.Time >= from && r.Time <= to)
            .OrderBy(r => r.Time)
            .ToListAsync();
        return rows.Select(r => r.ToReading()).Where(r => registry.FridgeAt(r.LoggerId, r.Time) == fridge).ToList();
    }
}
