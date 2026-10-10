using FridgeMonitor.Core;
using FridgeMonitor.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FridgeMonitor.Web.Services;

/// <summary>Loads stored readings and runs the Core analysis on them.</summary>
public sealed class FleetService(FridgeDb db, LoggerRegistry registry, IOptions<AnalysisOptions> options)
{
    public AnalysisOptions Options => options.Value;

    /// <summary>Latest reading time in the database: the "now" files are judged against.</summary>
    public Task<DateTime?> LatestReadingAsync() => db.Readings.MaxAsync(r => (DateTime?)r.Time);

    /// <summary>Every registered fridge over the last <paramref name="days"/> days up to the latest reading. Null if nothing uploaded yet.</summary>
    public async Task<FleetReport?> GetFleetAsync(int days = 7)
    {
        var latest = await LatestReadingAsync();
        if (latest is null) return null;

        var from = latest.Value.AddDays(-days);
        var readings = await db.Readings.Where(r => r.Time > from).ToListAsync();
        return FleetAnalyzer.Analyze(readings.Select(r => r.ToReading()), registry, Options, latest);
    }

    /// <summary>One fridge between two times, e.g. for the inspector. Uses whichever loggers were in it then.</summary>
    public async Task<(FridgeReport Report, List<Reading> Readings)> GetFridgeAsync(FridgeKey fridge, DateTime from, DateTime to)
    {
        var readings = await GetReadingsAsync(fridge, from, to);
        var latest = await LatestReadingAsync() ?? to;
        var asOf = latest < to ? latest : to;
        return (FridgeAnalyzer.Analyze(fridge, readings, asOf, Options, registry.UnmonitoredSince(fridge, asOf)), readings);
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
