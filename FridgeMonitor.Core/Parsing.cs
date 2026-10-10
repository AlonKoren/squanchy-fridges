using System.Globalization;
using System.Text.RegularExpressions;

namespace FridgeMonitor.Core;

public sealed class ImportResult
{
    public List<Reading> Readings { get; } = new();
    public List<ImportIssue> Issues { get; } = new();
    public int DuplicatesSkipped { get; private set; }

    /// <summary>Set for single-logger files.</summary>
    public string? LoggerId { get; internal set; }

    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

    internal void Error(string message, int? line = null) => Issues.Add(new(IssueSeverity.Error, message, line));
    internal void Warn(string message, int? line = null) => Issues.Add(new(IssueSeverity.Warning, message, line));

    // Typical fridge readings are 0–8 °C. A median above this means the unit is almost certainly wrong.
    const double PlausibleMaxMedianC = 15;

    /// <summary>Drop duplicate (logger, time) rows, sort by time, and sanity-check the values.</summary>
    internal void Normalize()
    {
        var kept = new List<Reading>();
        foreach (var group in Readings.GroupBy(r => (r.LoggerId, r.Time)))
        {
            var first = group.First();
            kept.Add(first);
            foreach (var dup in group.Skip(1))
            {
                DuplicatesSkipped++;
                if (dup.RawValue != first.RawValue)
                    Warn($"{first.LoggerId} at {Fmt.Time(first.Time)} appears twice with different values " +
                         $"({first.RawValue} and {dup.RawValue}). Kept the first.");
            }
        }
        Readings.Clear();
        Readings.AddRange(kept.OrderBy(r => r.LoggerId).ThenBy(r => r.Time));

        foreach (var logger in Readings.Where(r => !r.IsError).GroupBy(r => r.LoggerId))
        {
            var median = Stats.Median(logger.Select(r => r.TempC!.Value).ToList());
            if (median > PlausibleMaxMedianC)
                Warn($"{logger.Key}: typical reading is {Fmt.Temp(median)}, which is not a fridge temperature. " +
                     "Is the unit for this logger in loggers.json right?");
        }
    }
}

/// <summary>
/// Minimal delimited-text helpers. Logger exports and CSV saves of this data never contain quoted
/// separators, so a plain split is enough and keeps the parser easy to follow.
/// </summary>
internal static class Csv
{
    static readonly char[] Separators = { ',', ';', '\t' };

    public static string[] Lines(string content) =>
        content.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    public static string[] Split(string line, char separator) =>
        line.Split(separator).Select(c => c.Trim().Trim('"').Trim()).ToArray();

    /// <summary>Index of the first header cell containing any keyword (case-insensitive), or -1.</summary>
    public static int Column(string[] header, params string[] keywords) =>
        Array.FindIndex(header, h => keywords.Any(k => h.Contains(k, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The header is the first line (within the first few) that, split by some separator, has all
    /// the columns we need. Lines above it are preamble (e.g. "Logger ID;TL-0231").
    /// </summary>
    public static (int Index, char Separator, string[] Cells)? FindHeader(string[] lines, Func<string[], bool> isHeader)
    {
        for (int i = 0; i < Math.Min(lines.Length, 20); i++)
            foreach (var sep in Separators)
            {
                var cells = Split(lines[i], sep);
                if (cells.Length >= 2 && isHeader(cells)) return (i, sep, cells);
            }
        return null;
    }
}

internal static class Values
{
    // Tried after the logger's configured format. All year-first, so never a dd/MM vs MM/dd guess.
    static readonly string[] IsoFormats =
        { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" };

    public static bool TryParseTime(string raw, string configuredFormat, out DateTime time) =>
        DateTime.TryParseExact(raw.Trim(), IsoFormats.Prepend(configuredFormat).ToArray(),
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>°C, or null for ERR / blank / anything that isn't a number. Never 0.</summary>
    public static double? ParseTempC(string raw, char separator, TempUnit unit)
    {
        var s = raw.Trim();
        if (separator != ',') s = s.Replace(',', '.');   // European loggers write 3,8 when ; separates columns
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            return null;
        return unit == TempUnit.F ? Units.FToC(v) : v;
    }
}

/// <summary>Parses one logger's export file: only time and temperature, columns in any order.</summary>
public static class LoggerFileParser
{
    public static ImportResult Parse(string fileName, string content, LoggerRegistry registry)
    {
        var result = new ImportResult();
        var lines = Csv.Lines(content);

        var header = Csv.FindHeader(lines, IsHeader);
        if (header is null)
        {
            result.Error("Couldn't find a header row with a time column and a temperature column.");
            return result;
        }
        var (headerIndex, sep, headerCells) = header.Value;

        var logger = FindLogger(fileName, lines.Take(headerIndex), registry);
        if (logger is null)
        {
            result.Error($"No registered logger ID in the file name \"{fileName}\" or above the header. " +
                         "Add the logger to loggers.json first.");
            return result;
        }
        result.LoggerId = logger.Id;

        int timeCol = Csv.Column(headerCells, "time", "date");
        int tempCol = Csv.Column(headerCells, "temp");
        CheckHeaderUnit(headerCells[tempCol], logger, result);

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            int lineNo = i + 1;
            var cells = Csv.Split(lines[i], sep);
            if (cells.Length <= Math.Max(timeCol, tempCol))
            {
                result.Error($"Too few columns: \"{lines[i]}\".", lineNo);
                continue;
            }
            if (!Values.TryParseTime(cells[timeCol], logger.DateFormat, out var time))
            {
                result.Error($"Can't read the time \"{cells[timeCol]}\" (expected {logger.DateFormat}).", lineNo);
                continue;
            }
            var raw = cells[tempCol];
            result.Readings.Add(new Reading(logger.Id, time, Values.ParseTempC(raw, sep, logger.Unit), raw));
        }

        result.Normalize();
        return result;
    }

    static bool IsHeader(string[] cells)
    {
        int time = Csv.Column(cells, "time", "date"), temp = Csv.Column(cells, "temp");
        return time >= 0 && temp >= 0 && time != temp;
    }

    /// <summary>Logger ID from the file name, else from a preamble line (the old Haifa model).</summary>
    static LoggerInfo? FindLogger(string fileName, IEnumerable<string> preamble, LoggerRegistry registry)
    {
        foreach (var source in preamble.Prepend(Path.GetFileNameWithoutExtension(fileName)))
            foreach (var logger in registry.Loggers)
                if (Regex.IsMatch(source, $@"(?<![A-Za-z0-9]){Regex.Escape(logger.Id)}(?![0-9])", RegexOptions.IgnoreCase))
                    return logger;
        return null;
    }

    /// <summary>If the header names a unit, it must match the registry. The registry wins, loudly.</summary>
    static void CheckHeaderUnit(string tempHeader, LoggerInfo logger, ImportResult result)
    {
        TempUnit? declared =
            Regex.IsMatch(tempHeader, @"\(\s*°?F\s*\)|°F", RegexOptions.IgnoreCase) ? TempUnit.F :
            Regex.IsMatch(tempHeader, @"\(\s*°?C\s*\)|°C", RegexOptions.IgnoreCase) ? TempUnit.C : null;
        if (declared is not null && declared != logger.Unit)
            result.Warn($"File header says °{declared} but loggers.json says °{logger.Unit} for {logger.Id}. " +
                        "Converted using loggers.json; please check which is right.");
    }
}

/// <summary>
/// Parses Summer's existing combined sheet (Logger, Branch, Fridge, Time, Temp) so her history can
/// be imported. The registry decides the fridge; typed branch/fridge are only checked against it.
/// </summary>
public static class SheetParser
{
    /// <summary>True if the file has a Logger column next to Time and Temp, i.e. it's Summer's sheet, not a logger export.</summary>
    public static bool LooksLikeSheet(string content) => Csv.FindHeader(Csv.Lines(content), IsHeader) is not null;

    static bool IsHeader(string[] c) =>
        Csv.Column(c, "logger") >= 0 && Csv.Column(c, "time", "date") >= 0 && Csv.Column(c, "temp") >= 0;

    public static ImportResult Parse(string content, LoggerRegistry registry)
    {
        var result = new ImportResult();
        var lines = Csv.Lines(content);
        var header = Csv.FindHeader(lines, IsHeader);
        if (header is null)
        {
            result.Error("Couldn't find a header row with Logger, Time and Temp columns.");
            return result;
        }
        var (headerIndex, sep, h) = header.Value;
        int loggerCol = Csv.Column(h, "logger"), timeCol = Csv.Column(h, "time", "date"), tempCol = Csv.Column(h, "temp");
        int branchCol = Csv.Column(h, "branch"), fridgeCol = Csv.Column(h, "fridge");
        int needed = new[] { loggerCol, timeCol, tempCol, branchCol, fridgeCol }.Max();

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            int lineNo = i + 1;
            var cells = Csv.Split(lines[i], sep);
            if (cells.Length <= needed)
            {
                result.Error($"Too few columns: \"{lines[i]}\".", lineNo);
                continue;
            }
            var logger = registry.Find(cells[loggerCol]);
            if (logger is null)
            {
                result.Error($"Unknown logger \"{cells[loggerCol]}\". Add it to loggers.json.", lineNo);
                continue;
            }
            if (!Values.TryParseTime(cells[timeCol], logger.DateFormat, out var time))
            {
                result.Error($"Can't read the time \"{cells[timeCol]}\" (expected {logger.DateFormat}).", lineNo);
                continue;
            }
            if (branchCol >= 0 && fridgeCol >= 0)
                CheckTypedFridge(cells[branchCol], cells[fridgeCol], logger, time, result, lineNo);

            var raw = cells[tempCol];
            result.Readings.Add(new Reading(logger.Id, time, Values.ParseTempC(raw, sep, logger.Unit), raw));
        }

        result.Normalize();
        return result;
    }

    static void CheckTypedFridge(string branch, string fridge, LoggerInfo logger, DateTime time, ImportResult result, int line)
    {
        var actual = logger.AssignmentAt(time);
        if (actual is null)
            result.Warn($"{logger.Id} wasn't in any fridge at {Fmt.Time(time)} according to loggers.json.", line);
        else if (!Names.Same(branch, actual.Branch) || !Names.Same(fridge, actual.Fridge))
            result.Warn($"Typed \"{branch} / {fridge}\" but loggers.json says {logger.Id} was in {actual.Key} " +
                        $"at {Fmt.Time(time)}. Used loggers.json.", line);
    }
}

internal static class Stats
{
    public static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
