using FridgeMonitor.Core;
using static FridgeMonitor.Tests.Fixtures;

namespace FridgeMonitor.Tests;

public class AnalysisTests
{
    static readonly AnalysisOptions Options = new();
    static readonly FridgeKey Fridge = new("Test", "Dairy");

    static FridgeReport Analyze(List<Reading> readings, DateTime? asOf = null) =>
        FridgeAnalyzer.Analyze(Fridge, readings, asOf ?? readings.Max(r => r.Time), Options);

    [Fact]
    public void Rishon_sample_is_an_alarm()
    {
        var report = Analyze(Series(At(6, 0), 4.6, 5.4, 6.3, 7.1));
        Assert.Equal(FridgeStatus.Alarm, report.Status);
        var e = Assert.Single(report.Excursions);
        Assert.True(e.Ongoing);
        Assert.Equal(At(6, 15), e.Start);
        Assert.Equal(7.1, e.PeakC);
    }

    [Fact]
    public void TelAviv_single_spike_is_brief_not_an_alarm()
    {
        var report = Analyze(Series(At(5, 45), 4.0, 4.1, 9.4, 4.3));
        Assert.Equal(FridgeStatus.Ok, report.Status);
        var e = Assert.Single(report.Excursions);   // still listed for the inspector
        Assert.True(e.IsBrief);
        Assert.Equal(9.4, e.PeakC);
    }

    [Fact]
    public void Excursion_runs_until_the_first_reading_back_under_the_limit()
    {
        var report = Analyze(Series(At(12, 45), 3.1, 5.8, 6.4, 6.1, 5.3, 3.9));
        var e = Assert.Single(report.Excursions);
        Assert.Equal(At(13, 0), e.Start);
        Assert.Equal(At(14, 0), e.End);
        Assert.Equal(TimeSpan.FromHours(1), e.Duration);
        Assert.Equal(4, e.ReadingCount);
        Assert.Equal(FridgeStatus.Alarm, report.Status);
        Assert.Contains("Was above", report.Reasons[0].Text);
    }

    [Fact]
    public void Exactly_five_degrees_is_not_above_five()
    {
        Assert.Empty(Analyze(Series(At(6, 0), 5.0, 5.0, 5.0, 5.0)).Excursions);
    }

    [Fact]
    public void Jerusalem_gap_is_detected()
    {
        var readings = Series(At(5, 0), 3.8, 3.9, 3.8, 3.9, 3.8, 3.9)        // 05:00 – 06:15
            .Concat(Series(At(8, 30), 4.0, 3.9, 3.8, 3.9)).ToList();         // 08:30 – 09:15
        var report = Analyze(readings);
        var gap = Assert.Single(report.Gaps);
        Assert.Equal(At(6, 15), gap.From);
        Assert.Equal(At(8, 30), gap.To);
        Assert.Equal(FridgeStatus.Warning, report.Status);
    }

    [Fact]
    public void A_single_err_is_reported_but_is_not_a_gap()
    {
        var report = Analyze(Series(At(6, 0), 3.5, 3.6, null, 3.5, 3.6));
        Assert.Empty(report.Gaps);
        Assert.Equal(1, report.ErrorReadings);
        Assert.Contains(report.Reasons, r => r.Text.Contains("ERR"));
    }

    [Fact]
    public void Warming_trend_under_the_limit_warns_before_it_crosses()
    {
        var temps = Enumerable.Range(0, 13).Select(i => (double?)(2.6 + 0.55 * i / 4)).ToArray();   // +0.55 °C/h
        var report = Analyze(Series(At(20, 45), temps));
        Assert.Empty(report.Excursions);
        Assert.Equal(FridgeStatus.Warning, report.Status);
        Assert.Equal(0.55, report.TrendCPerHour!.Value, 2);
    }

    [Fact]
    public void A_door_spike_does_not_look_like_warming()
    {
        // Least squares would give ~0.6 °C/h here and warn. Theil–Sen gives 0.
        var temps = Enumerable.Repeat((double?)3.5, 11).Append(9.0).Append(3.5).ToArray();
        var report = Analyze(Series(At(20, 45), temps));
        Assert.Equal(0, report.TrendCPerHour!.Value, 2);
        Assert.Equal(FridgeStatus.Ok, report.Status);
    }

    [Fact]
    public void One_reading_above_at_the_end_is_a_warning_until_it_lasts()
    {
        var report = Analyze(Series(At(6, 0), 3.5, 3.6, 3.5, 6.0));
        Assert.Equal(FridgeStatus.Warning, report.Status);
        Assert.Contains("at the last reading", report.Reasons[0].Text);
    }

    [Fact]
    public void Gap_inside_an_excursion_is_flagged()
    {
        var readings = Series(At(5, 0), 3.5, 3.6, 3.5, 3.6, 6.0, 6.2)       // above from 06:00
            .Concat(Series(At(9, 0), 3.5, 3.6)).ToList();                    // back under at 09:00
        var e = Assert.Single(Analyze(readings).Excursions);
        Assert.True(e.IncludesGap);
    }

    [Fact]
    public void Logger_that_stopped_sending_is_warned()
    {
        var readings = Series(At(6, 0), 3.5, 3.6, 3.5, 3.6);
        var report = Analyze(readings, asOf: Mon.AddDays(3));
        Assert.Contains(report.Reasons, r => r.Text.StartsWith("No readings since"));
    }

    // Mon 00:00 – Tue 23:45 every 15 min; above 5°C from Mon 06:15 until back under at Tue 16:00 (like Rishon).
    static List<Reading> TwoDaysWithLongExcursion() =>
        Series(Mon, Enumerable.Range(0, 192).Select(i => (double?)(i >= 25 && i < 160 ? 7.0 : 3.5)).ToArray());

    static readonly Period Monday = new(Mon, Mon.AddDays(1).AddSeconds(-1));
    static readonly Period Tuesday = new(Mon.AddDays(1), Mon.AddDays(2).AddSeconds(-1));

    [Fact]
    public void Report_for_a_day_shows_the_real_end_not_still_above()
    {
        var report = FridgeAnalyzer.Analyze(Fridge, TwoDaysWithLongExcursion(), Monday.End, Options, period: Monday);

        var e = Assert.Single(report.Excursions);
        Assert.False(e.Ongoing);
        Assert.True(e.ContinuedAfterPeriod);
        Assert.Equal(Mon.AddDays(1).AddHours(16), e.End);
        Assert.Equal(FridgeStatus.Alarm, report.Status);
        Assert.DoesNotContain(report.Reasons, r => r.Text.Contains("now"));
    }

    [Fact]
    public void Excursion_that_started_before_the_period_keeps_its_real_start()
    {
        var report = FridgeAnalyzer.Analyze(Fridge, TwoDaysWithLongExcursion(), Tuesday.End, Options, period: Tuesday);

        var e = Assert.Single(report.Excursions);
        Assert.True(e.StartedBeforePeriod);
        Assert.Equal(At(6, 15), e.Start);
    }

    [Fact]
    public void Time_above_within_a_day_counts_only_that_day()
    {
        var e = Assert.Single(FridgeAnalyzer.Analyze(Fridge, TwoDaysWithLongExcursion(), Monday.End, Options, period: Monday).Excursions);

        Assert.Equal(TimeSpan.FromHours(17.75), e.DurationWithin(Mon, Mon.AddDays(1)));                 // 06:15 to midnight
        Assert.Equal(TimeSpan.FromHours(16), e.DurationWithin(Mon.AddDays(1), Mon.AddDays(2)));         // midnight to 16:00
        Assert.Equal(TimeSpan.Zero, e.DurationWithin(Mon.AddDays(2), Mon.AddDays(3)));
    }

    [Fact]
    public void Excursion_entirely_outside_the_period_is_left_out()
    {
        var evening = new Period(Mon.AddDays(1).AddHours(18), Tuesday.End);
        Assert.Empty(FridgeAnalyzer.Analyze(Fridge, TwoDaysWithLongExcursion(), evening.End, Options, period: evening).Excursions);
    }

    [Fact]
    public void Gaps_and_errors_outside_the_period_are_not_reported()
    {
        var readings = TwoDaysWithLongExcursion()
            .Where(r => r.Time < At(2, 0) || r.Time >= At(5, 0))                     // 3h gap on Monday night
            .Select(r => r.Time == At(10, 0) ? r with { TempC = null } : r)          // ERR on Monday
            .ToList();
        var report = FridgeAnalyzer.Analyze(Fridge, readings, Tuesday.End, Options, period: Tuesday);
        Assert.Empty(report.Gaps);
        Assert.Equal(0, report.ErrorReadings);
    }

    [Fact]
    public void Fridge_without_readings_has_no_data()
    {
        Assert.Equal(FridgeStatus.NoData, FridgeAnalyzer.Analyze(Fridge, [], Mon, Options).Status);
    }
}

/// <summary>End to end over the committed sample week: every scenario lands where it should.</summary>
public class SampleWeekTests
{
    static readonly FleetReport Fleet = Build(out Imports);
    static readonly List<ImportResult> Imports;

    static FleetReport Build(out List<ImportResult> imports)
    {
        var dir = Path.Combine(RepoRoot(), "sample-data");
        var registry = LoggerRegistry.FromJson(File.ReadAllText(Path.Combine(dir, "loggers.json")));
        imports = Directory.GetFiles(Path.Combine(dir, "logger-files"))
            .Select(f => LoggerFileParser.Parse(Path.GetFileName(f), File.ReadAllText(f), registry))
            .ToList();
        return FleetAnalyzer.Analyze(imports.SelectMany(i => i.Readings), registry, new AnalysisOptions());
    }

    static FridgeReport Get(string branch, string fridge) => Fleet.Fridges.Single(f => f.Fridge == new FridgeKey(branch, fridge));

    [Fact]
    public void All_files_import_without_errors()
    {
        Assert.All(Imports, i => Assert.False(i.HasErrors, string.Join("; ", i.Issues)));
    }

    [Fact]
    public void Now_is_the_latest_reading()
    {
        Assert.Equal(new DateTime(2026, 9, 20, 23, 45, 0), Fleet.AsOf);
    }

    [Theory]
    [InlineData("Rishon LeZion", "Cream cakes", FridgeStatus.Alarm, "Was above")]
    [InlineData("Holon", "Walk-in", FridgeStatus.Alarm, "Above 5.0°C now")]
    [InlineData("Ramat Gan", "Dairy", FridgeStatus.Alarm, "Was above 5.0°C for 1h 00m")]
    [InlineData("Be'er Sheva", "Display 1", FridgeStatus.Warning, "Warming")]
    [InlineData("Ashdod", "Walk-in", FridgeStatus.Warning, "No readings since Thu 17 Sep 14:30")]
    [InlineData("Netanya", "Dairy", FridgeStatus.Warning, "2 gaps")]
    [InlineData("Tel Aviv", "Walk-in", FridgeStatus.Warning, "No logger in this fridge since Wed 16 Sep 10:00")]
    [InlineData("Jerusalem", "Dairy", FridgeStatus.Warning, "Gap in the data: 2h 15m")]
    [InlineData("Haifa", "Dairy", FridgeStatus.Warning, "ERR")]
    [InlineData("Kfar Saba", "Display 1", FridgeStatus.NoData, "No readings received")]
    public void Scenario_fridges_get_the_expected_status(string branch, string fridge, FridgeStatus status, string reason)
    {
        var report = Get(branch, fridge);
        Assert.Equal(status, report.Status);
        Assert.Contains(report.Reasons, r => r.Text.Contains(reason));
    }

    [Fact]
    public void Normal_fridges_are_ok_despite_door_spikes()
    {
        var problems = new[] { "Rishon LeZion/Cream cakes", "Holon/Walk-in", "Ramat Gan/Dairy", "Be'er Sheva/Display 1",
            "Ashdod/Walk-in", "Netanya/Dairy", "Tel Aviv/Walk-in", "Jerusalem/Dairy", "Haifa/Dairy", "Kfar Saba/Display 1" };
        var normal = Fleet.Fridges.Where(f => !problems.Contains($"{f.Fridge.Branch}/{f.Fridge.Fridge}")).ToList();

        Assert.Equal(18, normal.Count);   // 28 fridges (TL-0417 counts for two), 10 with problems
        Assert.All(normal, f => Assert.True(f.Status == FridgeStatus.Ok, $"{f.Fridge}: {string.Join("; ", f.Reasons.Select(r => r.Text))}"));
        Assert.Contains(normal, f => f.Excursions.Any(e => e.IsBrief));
    }

    [Fact]
    public void Readings_taken_while_carrying_the_logger_are_unassigned()
    {
        Assert.Equal(2, Fleet.Unassigned.Count);
        Assert.All(Fleet.Unassigned, r => Assert.Equal("TL-0417", r.LoggerId));
    }
}
