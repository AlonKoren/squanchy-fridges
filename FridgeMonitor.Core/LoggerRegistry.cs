using System.Text.Json;
using System.Text.Json.Serialization;

namespace FridgeMonitor.Core;

/// <summary>A logger sat in this fridge from <see cref="From"/> (inclusive) until <see cref="To"/> (exclusive, null = still there).</summary>
public sealed record Assignment(string Branch, string Fridge, DateTime From, DateTime? To = null)
{
    public FridgeKey Key => new(Branch, Fridge);
    public bool Covers(DateTime t) => t >= From && (To is null || t < To);
}

public sealed record LoggerInfo(string Id, TempUnit Unit, string DateFormat, IReadOnlyList<Assignment> Assignments)
{
    public Assignment? AssignmentAt(DateTime t) => Assignments.FirstOrDefault(a => a.Covers(t));
}

/// <summary>
/// Which logger is where, its unit and its date format. Replaces the logger/branch/fridge
/// columns Summer types by hand, and is the only place units come from (never guessed from values).
/// </summary>
public sealed class LoggerRegistry
{
    readonly Dictionary<string, LoggerInfo> _byId;

    public LoggerRegistry(IEnumerable<LoggerInfo> loggers) =>
        _byId = loggers.ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<LoggerInfo> Loggers => _byId.Values;

    public LoggerInfo? Find(string id) => _byId.GetValueOrDefault(id.Trim());

    /// <summary>The fridge a logger was in at time t, or null (unknown logger, or in transit between fridges).</summary>
    public FridgeKey? FridgeAt(string loggerId, DateTime t) => Find(loggerId)?.AssignmentAt(t)?.Key;

    public IEnumerable<FridgeKey> AllFridges =>
        Loggers.SelectMany(l => l.Assignments).Select(a => a.Key).Distinct();

    /// <summary>When the last logger left this fridge, or null if a logger is in it at <paramref name="at"/>.</summary>
    public DateTime? UnmonitoredSince(FridgeKey fridge, DateTime at)
    {
        var assignments = Loggers.SelectMany(l => l.Assignments).Where(a => a.Key == fridge).ToList();
        if (assignments.Any(a => a.Covers(at))) return null;
        return assignments.Where(a => a.To <= at).Max(a => a.To);
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads loggers.json and refuses to start on a mistake, listing every problem found.</summary>
    public static LoggerRegistry FromJson(string json)
    {
        var loggers = JsonSerializer.Deserialize<List<LoggerInfo>>(json, JsonOptions) ?? [];
        var problems = Validate(loggers);
        if (problems.Count > 0)
            throw new InvalidDataException("loggers.json has problems:\n- " + string.Join("\n- ", problems));
        return new LoggerRegistry(loggers);
    }

    /// <summary>
    /// The registry is edited by hand, and a wrong entry silently puts readings in the wrong fridge.
    /// These are the mistakes that would otherwise go unnoticed.
    /// </summary>
    public static List<string> Validate(IReadOnlyList<LoggerInfo> loggers)
    {
        var problems = new List<string>();

        foreach (var dup in loggers.GroupBy(l => l.Id?.Trim() ?? "", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"{dup.Key}: listed {dup.Count()} times.");

        foreach (var l in loggers)
        {
            string id = string.IsNullOrWhiteSpace(l.Id) ? "(logger without an id)" : l.Id;
            if (!IsUsableDateFormat(l.DateFormat))
                problems.Add($"{id}: dateFormat \"{l.DateFormat}\" can't be used to read times.");
            if (l.Assignments is null || l.Assignments.Count == 0)
            {
                problems.Add($"{id}: no assignments, so its readings belong to no fridge.");
                continue;
            }
            foreach (var a in l.Assignments)
            {
                if (string.IsNullOrWhiteSpace(a.Branch) || string.IsNullOrWhiteSpace(a.Fridge))
                    problems.Add($"{id}: an assignment is missing its branch or fridge.");
                if (a.To <= a.From)
                    problems.Add($"{id}: assignment to {a.Key} ends ({a.To:yyyy-MM-dd HH:mm}) before it starts ({a.From:yyyy-MM-dd HH:mm}).");
            }
            foreach (var (a, b) in OverlappingPairs(l.Assignments))
                problems.Add($"{id}: in {a.Key} and {b.Key} at the same time (from {b.From:yyyy-MM-dd HH:mm}).");
        }

        // Two loggers in one fridge at once would merge their readings into one confusing line.
        var byFridge = loggers.Where(l => l.Assignments is not null)
            .SelectMany(l => l.Assignments.Select(a => (Logger: l.Id, Assignment: a)))
            .GroupBy(x => x.Assignment.Key);
        foreach (var fridge in byFridge)
        {
            var list = fridge.OrderBy(x => x.Assignment.From).ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (list[i].Logger != list[j].Logger && Overlap(list[i].Assignment, list[j].Assignment))
                        problems.Add($"{fridge.Key}: {list[i].Logger} and {list[j].Logger} assigned at the same time (from {list[j].Assignment.From:yyyy-MM-dd HH:mm}).");
        }

        return problems;
    }

    static IEnumerable<(Assignment, Assignment)> OverlappingPairs(IReadOnlyList<Assignment> assignments)
    {
        var sorted = assignments.OrderBy(a => a.From).ToList();
        for (int i = 0; i < sorted.Count; i++)
            for (int j = i + 1; j < sorted.Count; j++)
                if (Overlap(sorted[i], sorted[j])) yield return (sorted[i], sorted[j]);
    }

    static bool Overlap(Assignment a, Assignment b) =>
        (a.To is null || b.From < a.To) && (b.To is null || a.From < b.To);

    /// <summary>A format is usable if a time written with it can be read back with it.</summary>
    static bool IsUsableDateFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return false;
        var sample = new DateTime(2026, 9, 14, 6, 15, 0);
        try
        {
            var text = sample.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            return DateTime.TryParseExact(text, format, System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None, out var back) && back == sample;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class Names
{
    /// <summary>"tel aviv" and "Tel  Aviv " are the same branch.</summary>
    public static bool Same(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    static string Normalize(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
