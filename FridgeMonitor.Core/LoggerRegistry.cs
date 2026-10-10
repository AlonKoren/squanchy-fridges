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

    public static LoggerRegistry FromJson(string json) =>
        new(JsonSerializer.Deserialize<List<LoggerInfo>>(json, JsonOptions) ?? []);
}

public static class Names
{
    /// <summary>"tel aviv" and "Tel  Aviv " are the same branch.</summary>
    public static bool Same(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    static string Normalize(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
