using FridgeMonitor.Core;
using FridgeMonitor.Web.Data;
using FridgeMonitor.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static FridgeMonitor.Tests.Fixtures;

namespace FridgeMonitor.Tests;

/// <summary>Import + storage against a real (in-memory) SQLite database.</summary>
public sealed class StorageTests : IDisposable
{
    readonly SqliteConnection connection = new("Data Source=:memory:");
    readonly FridgeDb db;
    readonly LoggerRegistry registry = Registry();
    readonly ImportService import;

    const string JerusalemFile = "Time,Temp\n2026-09-14 06:00,3.8\n2026-09-14 06:15,3.9\n2026-09-14 08:30,4.0\n";

    public StorageTests()
    {
        connection.Open();   // in-memory DB lives as long as the connection
        db = new FridgeDb(new DbContextOptionsBuilder<FridgeDb>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        import = new ImportService(db, registry);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task Uploading_the_same_file_twice_stores_it_once()
    {
        var first = await import.ImportAsync("TL-0512_2026-09-14.csv", JerusalemFile);
        var second = await import.ImportAsync("TL-0512_2026-09-14.csv", JerusalemFile);

        Assert.Equal(3, first.Imported);
        Assert.Equal(0, second.Imported);
        Assert.Equal(3, second.AlreadyStored);
        Assert.Equal(3, await db.Readings.CountAsync());
        Assert.Equal(2, await db.Uploads.CountAsync());   // both uploads are still recorded
    }

    [Fact]
    public async Task Same_file_uploaded_twice_at_once_does_not_fail()
    {
        // Make the race deterministic: just before this import saves, another one saves the same readings.
        var rival = new FridgeDb(new DbContextOptionsBuilder<FridgeDb>().UseSqlite(connection).Options);
        var racing = new FridgeDb(new DbContextOptionsBuilder<FridgeDb>().UseSqlite(connection)
            .AddInterceptors(new BeforeFirstSave(() => new ImportService(rival, registry).ImportAsync("TL-0512.csv", JerusalemFile)))
            .Options);

        var upload = await new ImportService(racing, registry).ImportAsync("TL-0512.csv", JerusalemFile);

        Assert.Equal(0, upload.Imported);
        Assert.Equal(3, upload.AlreadyStored);
        Assert.Equal(3, await db.Readings.CountAsync());
        Assert.Equal(2, await db.Uploads.CountAsync());   // the rival's and the retried one, not the failed attempt
    }

    /// <summary>Runs an action once, right before the first SaveChanges on the context it's attached to.</summary>
    sealed class BeforeFirstSave(Func<Task> action) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        bool done;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!done)
            {
                done = true;
                await action();
            }
            return result;
        }
    }

    [Fact]
    public async Task Summer_sheet_is_detected_and_overlaps_with_logger_files()
    {
        var sheet = await import.ImportAsync("summer.csv",
            File.ReadAllText(Path.Combine(RepoRoot(), "sample-data", "summer-sheet-2026-09-14.csv")));
        var file = await import.ImportAsync("TL-0512_2026-09-14.csv", JerusalemFile);

        Assert.Null(sheet.LoggerId);
        Assert.Equal(15, sheet.Imported);
        Assert.Equal(0, file.Imported);   // the sheet already had these three Jerusalem rows
        Assert.Equal(3, file.AlreadyStored);
    }

    [Fact]
    public async Task Stored_reading_with_a_different_value_is_kept_and_warned()
    {
        await import.ImportAsync("TL-0512.csv", JerusalemFile);
        var changed = await import.ImportAsync("TL-0512.csv", "Time,Temp\n2026-09-14 06:00,7.5\n");

        Assert.Equal(0, changed.Imported);
        Assert.Contains(changed.Issues, i => i.Message.Contains("different value"));
        Assert.Equal(3.8, (await db.Readings.SingleAsync(r => r.Time == At(6, 0))).TempC);
    }

    [Fact]
    public async Task Bad_file_is_recorded_with_its_issues_and_stores_nothing()
    {
        var upload = await import.ImportAsync("notes.txt", "hello\nworld\n");

        Assert.Equal(0, upload.Imported);
        Assert.Contains(upload.Issues, i => i.Severity == IssueSeverity.Error);
        Assert.Equal(0, await db.Readings.CountAsync());
        Assert.Equal(1, await db.Uploads.Include(u => u.Issues).Where(u => u.Issues.Count > 0).CountAsync());
    }

    [Fact]
    public async Task Fleet_report_is_built_from_stored_readings()
    {
        var sample = Path.Combine(RepoRoot(), "sample-data");
        var fullRegistry = LoggerRegistry.FromJson(File.ReadAllText(Path.Combine(sample, "loggers.json")));
        var fullImport = new ImportService(db, fullRegistry);
        foreach (var f in Directory.GetFiles(Path.Combine(sample, "logger-files")))
            await fullImport.ImportAsync(Path.GetFileName(f), File.ReadAllText(f));

        var fleet = await new FleetService(db, fullRegistry, Microsoft.Extensions.Options.Options.Create(new AnalysisOptions()))
            .GetFleetAsync();

        Assert.NotNull(fleet);
        Assert.Equal(new DateTime(2026, 9, 20, 23, 45, 0), fleet.AsOf);
        Assert.Equal(FridgeStatus.Alarm, fleet.Fridges[0].Status);   // problems sorted first
        Assert.Equal(FridgeStatus.Alarm, fleet.Fridges.Single(f => f.Fridge == new FridgeKey("Rishon LeZion", "Cream cakes")).Status);
    }

    [Fact]
    public async Task Inspector_report_for_one_day_shows_when_rishon_actually_recovered()
    {
        var sample = Path.Combine(RepoRoot(), "sample-data");
        var fullRegistry = LoggerRegistry.FromJson(File.ReadAllText(Path.Combine(sample, "loggers.json")));
        await new ImportService(db, fullRegistry).ImportAsync("TL-0388_2026-09-14.csv",
            File.ReadAllText(Path.Combine(sample, "logger-files", "TL-0388_2026-09-14.csv")));
        var service = new FleetService(db, fullRegistry, Microsoft.Extensions.Options.Options.Create(new AnalysisOptions()));

        var (report, readings) = await service.GetFridgeAsync(
            new FridgeKey("Rishon LeZion", "Cream cakes"), Mon, Mon.AddDays(1).AddSeconds(-1));

        var e = report.Excursions.Single(x => !x.IsBrief);
        Assert.Equal(At(6, 15), e.Start);
        Assert.False(e.Ongoing);                                  // was "still above" before the fix
        Assert.True(e.ContinuedAfterPeriod);
        Assert.Equal(new DateTime(2026, 9, 15), e.End.Date);     // recovered on Tuesday
        Assert.All(readings, r => Assert.Equal(Mon, r.Time.Date)); // chart still shows only Monday
    }

    [Fact]
    public async Task Fridge_report_for_a_date_range_follows_the_moved_logger()
    {
        await import.ImportAsync("TL-0417_2026-09-14.csv",
            "Temperature (°C),Timestamp\n4.1,2026-09-15T06:00:00\n21.0,2026-09-16T10:15:00\n3.7,2026-09-17T06:00:00\n");
        var service = new FleetService(db, registry, Microsoft.Extensions.Options.Options.Create(new AnalysisOptions()));

        var walkIn = await service.GetReadingsAsync(new FridgeKey("Tel Aviv", "Walk-in"), Mon, Mon.AddDays(7));
        var display = await service.GetReadingsAsync(new FridgeKey("Tel Aviv", "Display 2"), Mon, Mon.AddDays(7));

        Assert.Equal(4.1, Assert.Single(walkIn).TempC);
        Assert.Equal(3.7, Assert.Single(display).TempC);   // the 21.0 in transit is in neither
    }
}
