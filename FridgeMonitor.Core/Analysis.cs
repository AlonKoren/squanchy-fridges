namespace FridgeMonitor.Core;

/// <summary>Business rules Summer should confirm. Bound from appsettings.json in the web app.</summary>
public sealed class AnalysisOptions
{
    public double LimitC { get; set; } = 5.0;

    /// <summary>Above the limit for at least this long = alarm. Shorter = brief (door opened).</summary>
    public int MinExcursionMinutes { get; set; } = 30;

    public double TrendWindowHours { get; set; } = 3;
    public double TrendWarnCPerHour { get; set; } = 0.5;

    /// <summary>A step longer than this many times the logger's usual interval is a gap.</summary>
    public double GapFactor { get; set; } = 2.0;
}

public sealed record Gap(DateTime From, DateTime To)
{
    public TimeSpan Length => To - From;
}

/// <summary>
/// A run of readings above the limit. Start = first reading above. End = first reading back under
/// (or the last reading if it's still going). With 15-minute readings that's the honest answer
/// to "for how long": we don't know what happened between readings.
/// </summary>
public sealed record Excursion(
    DateTime Start, DateTime End, double PeakC, int ReadingCount,
    bool Ongoing, bool IncludesGap, bool IsBrief,
    bool StartedBeforePeriod = false, bool ContinuedAfterPeriod = false)
{
    public TimeSpan Duration => End - Start;

    /// <summary>The part of this excursion between from and to, for totals over chosen dates.</summary>
    public TimeSpan DurationWithin(DateTime from, DateTime to)
    {
        var start = Start > from ? Start : from;
        var end = End < to ? End : to;
        return end > start ? end - start : TimeSpan.Zero;
    }
}

/// <summary>The time range being reported on, both ends inclusive.</summary>
public sealed record Period(DateTime Start, DateTime End)
{
    public bool Contains(DateTime t) => t >= Start && t <= End;
}

// Declared worst first, so sorting by status puts problems at the top.
public enum FridgeStatus { Alarm, Warning, NoData, Ok }

public sealed record StatusReason(FridgeStatus Level, string Text);

public sealed record FridgeReport(
    FridgeKey Fridge,
    IReadOnlyList<StatusReason> Reasons,
    Reading? LastValid,
    IReadOnlyList<Excursion> Excursions,
    IReadOnlyList<Gap> Gaps,
    int ErrorReadings,
    double? TrendCPerHour)
{
    public FridgeStatus Status => Reasons.Count == 0 ? FridgeStatus.Ok : Reasons.Min(r => r.Level);
}

public sealed record FleetReport(DateTime AsOf, IReadOnlyList<FridgeReport> Fridges, IReadOnlyList<Reading> Unassigned);

public static class FleetAnalyzer
{
    /// <summary>
    /// Groups readings by the fridge their logger was in at the time and analyses every registered fridge.
    /// "Now" defaults to the latest reading, not the wall clock: files arrive weekly.
    /// </summary>
    public static FleetReport Analyze(IEnumerable<Reading> readings, LoggerRegistry registry, AnalysisOptions options,
                                      DateTime? asOf = null, Period? period = null)
    {
        var list = readings.ToList();
        var now = asOf ?? (list.Count > 0 ? list.Max(r => r.Time) : DateTime.Now);

        var byFridge = new Dictionary<FridgeKey, List<Reading>>();
        var unassigned = new List<Reading>();
        foreach (var r in list)
        {
            var fridge = registry.FridgeAt(r.LoggerId, r.Time);
            if (fridge is null)
            {
                if (period is null || period.Contains(r.Time)) unassigned.Add(r);
                continue;
            }
            if (!byFridge.TryGetValue(fridge, out var bucket)) byFridge[fridge] = bucket = new();
            bucket.Add(r);
        }

        var reports = registry.AllFridges
            .Select(f => FridgeAnalyzer.Analyze(f, byFridge.GetValueOrDefault(f) ?? [], now, options, registry.UnmonitoredSince(f, now), period))
            .OrderBy(r => r.Status).ThenBy(r => r.Fridge.Branch).ThenBy(r => r.Fridge.Fridge)
            .ToList();
        return new FleetReport(now, reports, unassigned);
    }
}

public static class FridgeAnalyzer
{
    static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    /// <param name="period">
    /// If set, report on this period only. Readings outside it can be passed in as context, so an excursion
    /// crossing the edge keeps its real start and end instead of being cut off at the edge.
    /// </param>
    public static FridgeReport Analyze(FridgeKey fridge, IEnumerable<Reading> readings, DateTime asOf,
                                       AnalysisOptions o, DateTime? unmonitoredSince = null, Period? period = null)
    {
        var everything = readings.OrderBy(r => r.Time).ToList();
        var all = period is null ? everything : everything.Where(r => period.Contains(r.Time)).ToList();
        var valid = all.Where(r => !r.IsError).ToList();
        int errors = all.Count - valid.Count;
        var reasons = new List<StatusReason>();
        string limit = Fmt.Temp(o.LimitC);

        if (unmonitoredSince is { } removed)
            reasons.Add(new(FridgeStatus.Warning, $"No logger in this fridge since {Fmt.Time(removed)}"));

        if (valid.Count == 0)
        {
            if (unmonitoredSince is null)
                reasons.Add(new(FridgeStatus.NoData, errors > 0 ? $"Only error readings ({errors})" : "No readings received"));
            return new FridgeReport(fridge, reasons, null, [], [], errors, null);
        }

        var gapThreshold = ExpectedInterval(valid) * o.GapFactor;
        var excursions = period is null
            ? FindExcursions(valid, o, gapThreshold)
            : ClipToPeriod(FindExcursions(everything.Where(r => !r.IsError).ToList(), o, gapThreshold), period);
        var gaps = FindGaps(valid, gapThreshold);
        var trend = TrendCPerHour(valid, o.TrendWindowHours);
        var last = valid[^1];
        var minExcursion = TimeSpan.FromMinutes(o.MinExcursionMinutes);

        // "Now" only if it's still going at the end of the data and the period reaches that far.
        var current = excursions.LastOrDefault(e => e.Ongoing && !e.ContinuedAfterPeriod);
        if (current is not null)
            reasons.Add(current.Duration >= minExcursion
                ? new(FridgeStatus.Alarm, $"Above {limit} now, for {Fmt.Duration(current.Duration)} (peak {Fmt.Temp(current.PeakC)})")
                : new(FridgeStatus.Warning, $"Above {limit} at the last reading ({Fmt.Temp(last.TempC!.Value)})"));

        var past = excursions.Where(e => !ReferenceEquals(e, current) && !e.IsBrief).ToList();
        if (past.Count > 0)
        {
            var longest = past.MaxBy(e => e.Duration)!;
            reasons.Add(new(FridgeStatus.Alarm, past.Count == 1
                ? $"Was above {limit} for {Fmt.Duration(longest.Duration)} from {Fmt.Time(longest.Start)}"
                : $"Above {limit} {past.Count} times, longest {Fmt.Duration(longest.Duration)} from {Fmt.Time(longest.Start)}"));
        }

        if (current is null && trend > o.TrendWarnCPerHour)
            reasons.Add(new(FridgeStatus.Warning,
                $"Warming {Fmt.Number(trend.Value)}°C/h over the last {o.TrendWindowHours:0}h, {Fmt.Temp(last.TempC!.Value)} now"));

        if (unmonitoredSince is null && asOf - all[^1].Time > gapThreshold)
            reasons.Add(new(FridgeStatus.Warning, $"No readings since {Fmt.Time(all[^1].Time)}"));

        if (gaps.Count > 0)
        {
            var longest = gaps.MaxBy(g => g.Length)!;
            reasons.Add(new(FridgeStatus.Warning, gaps.Count == 1
                ? $"Gap in the data: {Fmt.Duration(longest.Length)} from {Fmt.Time(longest.From)}"
                : $"{gaps.Count} gaps in the data, longest {Fmt.Duration(longest.Length)} from {Fmt.Time(longest.From)}"));
        }

        if (errors > 0)
            reasons.Add(new(FridgeStatus.Warning, $"{errors} error reading{(errors == 1 ? "" : "s")} (ERR)"));

        return new FridgeReport(fridge, reasons, last, excursions, gaps, errors, trend);
    }

    /// <summary>Excursions that overlap the period, flagged where they cross its edges.</summary>
    static List<Excursion> ClipToPeriod(List<Excursion> excursions, Period period) =>
        excursions
            .Where(e => e.End > period.Start && e.Start <= period.End)
            .Select(e => e with { StartedBeforePeriod = e.Start < period.Start, ContinuedAfterPeriod = e.End > period.End })
            .ToList();

    /// <summary>The logger's usual interval: median step between readings (robust to the odd gap).</summary>
    public static TimeSpan ExpectedInterval(IReadOnlyList<Reading> sorted)
    {
        var steps = sorted.Zip(sorted.Skip(1), (a, b) => (b.Time - a.Time).TotalMinutes).Where(m => m > 0).ToList();
        return steps.Count == 0 ? DefaultInterval : TimeSpan.FromMinutes(Stats.Median(steps));
    }

    /// <param name="valid">Valid readings sorted by time.</param>
    public static List<Excursion> FindExcursions(IReadOnlyList<Reading> valid, AnalysisOptions o, TimeSpan gapThreshold)
    {
        var result = new List<Excursion>();
        var minExcursion = TimeSpan.FromMinutes(o.MinExcursionMinutes);
        int i = 0;
        while (i < valid.Count)
        {
            if (valid[i].TempC!.Value <= o.LimitC) { i++; continue; }

            int start = i;
            while (i < valid.Count && valid[i].TempC!.Value > o.LimitC) i++;
            bool ongoing = i == valid.Count;
            int endIndex = ongoing ? i - 1 : i;   // first reading back under, or the last one we have

            bool includesGap = false;
            for (int k = start; k < endIndex; k++)
                if (valid[k + 1].Time - valid[k].Time > gapThreshold) includesGap = true;

            var startTime = valid[start].Time;
            var endTime = valid[endIndex].Time;
            double peak = valid.Skip(start).Take(i - start).Max(r => r.TempC!.Value);
            bool brief = !ongoing && endTime - startTime < minExcursion;
            result.Add(new Excursion(startTime, endTime, peak, i - start, ongoing, includesGap, brief));
        }
        return result;
    }

    /// <summary>Steps between readings longer than the threshold. The file can't say why (battery, crash, not saved).</summary>
    public static List<Gap> FindGaps(IReadOnlyList<Reading> sorted, TimeSpan threshold) =>
        sorted.Zip(sorted.Skip(1), (a, b) => new Gap(a.Time, b.Time)).Where(g => g.Length > threshold).ToList();

    /// <summary>
    /// Warming rate over the last window, as the median of all pairwise slopes (Theil–Sen).
    /// A least-squares line would be dragged up by a single door-opening spike; the median isn't.
    /// </summary>
    public static double? TrendCPerHour(IReadOnlyList<Reading> valid, double windowHours)
    {
        if (valid.Count == 0) return null;
        var end = valid[^1].Time;
        var window = valid.Where(r => r.Time >= end.AddHours(-windowHours)).ToList();
        if (window.Count < 4) return null;

        var slopes = new List<double>();
        for (int a = 0; a < window.Count; a++)
            for (int b = a + 1; b < window.Count; b++)
            {
                double hours = (window[b].Time - window[a].Time).TotalHours;
                if (hours > 0) slopes.Add((window[b].TempC!.Value - window[a].TempC!.Value) / hours);
            }
        return slopes.Count == 0 ? null : Stats.Median(slopes);
    }
}
