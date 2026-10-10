using System.Globalization;

namespace FridgeMonitor.Core;

public enum TempUnit { C, F }

/// <summary>One row from a logger. TempC is null when the logger wrote ERR or anything unreadable.</summary>
/// <remarks>
/// A reading belongs to a logger, not a fridge. Which fridge it counts for is looked up in the
/// registry by time, so fixing a wrong assignment later re-attributes old readings automatically.
/// </remarks>
public sealed record Reading(string LoggerId, DateTime Time, double? TempC, string RawValue)
{
    public bool IsError => TempC is null;
}

public sealed record FridgeKey(string Branch, string Fridge)
{
    public override string ToString() => $"{Branch} / {Fridge}";
}

public enum IssueSeverity { Warning, Error }

public sealed record ImportIssue(IssueSeverity Severity, string Message, int? Line = null);

public static class Units
{
    public static double FToC(double f) => (f - 32) * 5 / 9;
}

/// <summary>Invariant formatting for messages shown to Summer.</summary>
public static class Fmt
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Time(DateTime t) => t.ToString("ddd d MMM HH:mm", Inv);
    public static string Temp(double c) => c.ToString("0.0", Inv) + "°C";
    public static string Number(double v) => v.ToString("0.0", Inv);

    public static string Duration(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes:00}m" : $"{d.Minutes}m";
}
