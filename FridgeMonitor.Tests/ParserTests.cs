using FridgeMonitor.Core;
using static FridgeMonitor.Tests.Fixtures;

namespace FridgeMonitor.Tests;

public class LoggerFileParserTests
{
    readonly LoggerRegistry registry = Registry();

    // Summer's Haifa rows, as the old logger would export them.
    const string HaifaFile =
        "Logger ID;TL-0231\r\nModel;ThermoLog 200\r\nDate;Temperature(F)\r\n" +
        "14/09/2026 06:00;38.3\r\n14/09/2026 06:15;39.0\r\n14/09/2026 06:30;ERR\r\n";

    // Summer's Tel Aviv rows: columns swapped, 05:45 out of order.
    const string TelAvivFile =
        "Temperature (°C),Timestamp\n4.1,2026-09-14T06:00:00\n9.4,2026-09-14T06:15:00\n" +
        "4.3,2026-09-14T06:30:00\n4.0,2026-09-14T05:45:00\n";

    // Summer's Jerusalem rows: 06:15 twice.
    const string JerusalemFile =
        "Time,Temp\n2026-09-14 06:00,3.8\n2026-09-14 06:15,3.9\n2026-09-14 06:15,3.9\n2026-09-14 08:30,4.0\n";

    [Fact]
    public void Haifa_fahrenheit_is_converted_to_celsius()
    {
        var r = LoggerFileParser.Parse("haifa_dairy_export.csv", HaifaFile, registry);
        Assert.Equal(3.5, r.Readings[0].TempC!.Value, 2);
        Assert.Equal(3.89, r.Readings[1].TempC!.Value, 2);
    }

    [Fact]
    public void Logger_id_comes_from_the_preamble_when_the_file_name_has_none()
    {
        var r = LoggerFileParser.Parse("haifa_dairy_export.csv", HaifaFile, registry);
        Assert.Equal("TL-0231", r.LoggerId);
    }

    [Fact]
    public void Err_is_kept_as_an_error_reading_not_zero()
    {
        var err = LoggerFileParser.Parse("haifa_dairy_export.csv", HaifaFile, registry).Readings[2];
        Assert.True(err.IsError);
        Assert.Null(err.TempC);
        Assert.Equal("ERR", err.RawValue);
    }

    [Fact]
    public void Day_first_dates_are_read_day_first()
    {
        var r = LoggerFileParser.Parse("x.csv", "Logger ID;TL-0231\nDate;Temperature(F)\n01/02/2026 06:00;38.3\n", registry);
        Assert.Equal(new DateTime(2026, 2, 1, 6, 0, 0), r.Readings[0].Time);   // 1 Feb, not 2 Jan
    }

    [Fact]
    public void Columns_are_found_by_name_not_position()
    {
        var r = LoggerFileParser.Parse("TL-0417_2026-09-14.csv", TelAvivFile, registry);
        Assert.Equal(9.4, r.Readings.Single(x => x.Time == At(6, 15)).TempC);
    }

    [Fact]
    public void Out_of_order_rows_are_sorted()
    {
        var r = LoggerFileParser.Parse("TL-0417_2026-09-14.csv", TelAvivFile, registry);
        Assert.Equal(new[] { At(5, 45), At(6, 0), At(6, 15), At(6, 30) }, r.Readings.Select(x => x.Time));
    }

    [Fact]
    public void Duplicate_rows_are_dropped_and_counted()
    {
        var r = LoggerFileParser.Parse("TL-0512_2026-09-14.csv", JerusalemFile, registry);
        Assert.Equal(3, r.Readings.Count);
        Assert.Equal(1, r.DuplicatesSkipped);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Same_time_with_a_different_value_is_warned()
    {
        var r = LoggerFileParser.Parse("TL-0512.csv", "Time,Temp\n2026-09-14 06:15,3.9\n2026-09-14 06:15,4.4\n", registry);
        Assert.Single(r.Readings);
        Assert.Contains(r.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("different values"));
    }

    [Fact]
    public void A_bad_line_is_reported_and_the_rest_imported()
    {
        var r = LoggerFileParser.Parse("TL-0512.csv", "Time,Temp\n2026-09-14 06:00,3.8\nnot a date,4.0\n2026-09-14 06:30,3.9\n", registry);
        Assert.Equal(2, r.Readings.Count);
        var issue = Assert.Single(r.Issues);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(3, issue.Line);
    }

    [Fact]
    public void Unknown_logger_is_rejected_with_a_clear_message()
    {
        var r = LoggerFileParser.Parse("TL-9999_2026-09-14.csv", "Time,Temp\n2026-09-14 06:00,3.8\n", registry);
        Assert.Empty(r.Readings);
        Assert.Contains(r.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("loggers.json"));
    }

    [Fact]
    public void Celsius_logger_with_fahrenheit_looking_values_is_flagged_not_converted()
    {
        var r = LoggerFileParser.Parse("TL-0512.csv", "Time,Temp\n2026-09-14 06:00,38.3\n2026-09-14 06:15,39.0\n", registry);
        Assert.Equal(38.3, r.Readings[0].TempC);
        Assert.Contains(r.Issues, i => i.Message.Contains("not a fridge temperature"));
    }

    [Fact]
    public void Header_unit_disagreeing_with_the_registry_is_flagged()
    {
        var r = LoggerFileParser.Parse("TL-0512.csv", "Time,Temp (F)\n2026-09-14 06:00,3.8\n", registry);
        Assert.Contains(r.Issues, i => i.Message.Contains("File header says °F"));
    }

    [Fact]
    public void Decimal_comma_is_accepted_when_semicolon_separates_columns()
    {
        var r = LoggerFileParser.Parse("TL-0512.csv", "Time;Temp\n2026-09-14 06:00;3,8\n", registry);
        Assert.Equal(3.8, r.Readings[0].TempC);
    }
}

public class SheetParserTests
{
    readonly LoggerRegistry registry = Registry();

    static string SummerSheet() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "sample-data", "summer-sheet-2026-09-14.csv"));

    [Fact]
    public void Summer_sheet_imports_cleanly_with_the_duplicate_removed()
    {
        var r = SheetParser.Parse(SummerSheet(), registry);
        Assert.Equal(15, r.Readings.Count);
        Assert.Equal(1, r.DuplicatesSkipped);
        Assert.Empty(r.Issues);   // includes: "tel aviv" matches "Tel Aviv" without complaint
    }

    [Fact]
    public void Haifa_rows_in_the_sheet_are_converted()
    {
        var r = SheetParser.Parse(SummerSheet(), registry);
        Assert.Equal(3.5, r.Readings.First(x => x.LoggerId == "TL-0231").TempC!.Value, 2);
    }

    [Fact]
    public void Typed_fridge_that_disagrees_with_the_registry_is_warned()
    {
        var r = SheetParser.Parse("Logger,Branch,Fridge,Time,Temp\nTL-0417,Tel Aviv,Walk-in,2026-09-17 06:00,3.7\n", registry);
        Assert.Single(r.Readings);
        Assert.Contains(r.Issues, i => i.Message.Contains("Display 2"));
    }
}
