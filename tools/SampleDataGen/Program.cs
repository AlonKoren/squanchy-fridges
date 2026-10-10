using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

// Generates one week (Mon 14 – Sun 20 Sep 2026) of logger files for the Squanchy fleet,
// plus the logger registry and Summer's pasted sheet. Fixed seed, so output is reproducible.
// Usage: dotnet run --project tools/SampleDataGen [repoRoot]

var inv = CultureInfo.InvariantCulture;
var rng = new Random(20260914);
var weekStart = new DateTime(2026, 9, 14);
var weekEnd = weekStart.AddDays(7);
var step = TimeSpan.FromMinutes(15);

// TL-0417 is unplugged from the Walk-in at 10:00 and clipped into Display 2 at 10:30.
var moveOut = new DateTime(2026, 9, 16, 10, 0, 0);
var moveIn = new DateTime(2026, 9, 16, 10, 30, 0);

var fleet = new Logger[]
{
    new("TL-0512", "Jerusalem", "Dairy", FileFormat.Standard, Scenario.SummerJerusalem),
    new("TL-0513", "Jerusalem", "Cream cakes", FileFormat.Standard),
    new("TL-0514", "Jerusalem", "Display 1", FileFormat.Standard),
    new("TL-0417", "Tel Aviv", "Walk-in", FileFormat.Swapped, Scenario.SummerTelAvivMoved),
    new("TL-0418", "Tel Aviv", "Cream cakes", FileFormat.Swapped),
    new("TL-0231", "Haifa", "Dairy", FileFormat.OldHaifa, Scenario.SummerHaifa),
    new("TL-0232", "Haifa", "Cream cakes", FileFormat.Standard),
    new("TL-0233", "Haifa", "Display 1", FileFormat.Standard),
    new("TL-0388", "Rishon LeZion", "Cream cakes", FileFormat.Standard, Scenario.SummerRishonWarming),
    new("TL-0389", "Rishon LeZion", "Dairy", FileFormat.Standard),
    new("TL-0390", "Rishon LeZion", "Walk-in", FileFormat.Standard),
    new("TL-0601", "Petah Tikva", "Dairy", FileFormat.Standard),
    new("TL-0602", "Petah Tikva", "Cream cakes", FileFormat.Standard),
    new("TL-0611", "Ashdod", "Dairy", FileFormat.Standard),
    new("TL-0612", "Ashdod", "Walk-in", FileFormat.Standard, Scenario.StopsMidWeek),
    new("TL-0621", "Netanya", "Dairy", FileFormat.Standard, Scenario.Gaps),
    new("TL-0622", "Netanya", "Display 1", FileFormat.Standard),
    new("TL-0631", "Be'er Sheva", "Dairy", FileFormat.Standard),
    new("TL-0632", "Be'er Sheva", "Display 1", FileFormat.Standard, Scenario.WarmingTrend),
    new("TL-0641", "Holon", "Dairy", FileFormat.Standard),
    new("TL-0642", "Holon", "Walk-in", FileFormat.Standard, Scenario.OngoingExcursion),
    new("TL-0651", "Ramat Gan", "Dairy", FileFormat.Standard, Scenario.ShortExcursion),
    new("TL-0652", "Ramat Gan", "Cream cakes", FileFormat.Standard),
    new("TL-0661", "Herzliya", "Dairy", FileFormat.Swapped),
    new("TL-0662", "Herzliya", "Display 1", FileFormat.Swapped),
    new("TL-0671", "Kfar Saba", "Dairy", FileFormat.Standard),
    new("TL-0672", "Kfar Saba", "Display 1", FileFormat.Standard, Scenario.NoFile),
};

var repoRoot = args.Length > 0 ? args[0] : FindRepoRoot();
var outDir = Path.Combine(repoRoot, "sample-data");
var loggerDir = Path.Combine(outDir, "logger-files");
Directory.CreateDirectory(loggerDir);

foreach (var logger in fleet)
{
    if (logger.Scenario == Scenario.NoFile) continue;   // branch never sent this file
    WriteLoggerFile(logger, Generate(logger));
}
WriteRegistry();
WriteSummerSheet();
Console.WriteLine($"Wrote sample data to {outDir}");

List<Row> Generate(Logger logger)
{
    double setpoint = 3.0 + rng.NextDouble();   // 3.0–4.0 °C
    var rows = new List<Row>();
    for (var t = weekStart; t < weekEnd; t += step)
    {
        double minutes = (t - weekStart).TotalMinutes;
        // Compressor cycling plus sensor noise around the setpoint.
        double temp = setpoint + 0.3 * Math.Sin(2 * Math.PI * minutes / 50) + Noise(0.1);
        rows.Add(new Row(t, temp));
    }
    AddDoorOpenings(rows);
    ApplyScenario(logger, rows, setpoint);
    return rows;
}

// A few single-reading jumps per week on delivery mornings (15–20 Sep). Never two in a row,
// so they stay "brief" by construction; real excursions come only from scenarios.
void AddDoorOpenings(List<Row> rows)
{
    var used = new HashSet<int>();
    int count = rng.Next(2, 5);
    for (int n = 0; n < count; n++)
    {
        var t = weekStart.AddDays(rng.Next(1, 7)).AddHours(rng.Next(5, 8)).AddMinutes(15 * rng.Next(0, 4));
        int i = rows.FindIndex(r => r.Time == t);
        if (used.Contains(i - 1) || used.Contains(i) || used.Contains(i + 1)) continue;
        used.Add(i);
        rows[i] = rows[i] with { TempC = rows[i].TempC + 3 + 3 * rng.NextDouble() };
    }
}

void ApplyScenario(Logger logger, List<Row> rows, double setpoint)
{
    switch (logger.Scenario)
    {
        case Scenario.SummerJerusalem:
            // Summer's rows: 06:15 appears twice, then nothing until 08:30.
            Set(rows, At(6, 0), 3.8);
            Set(rows, At(6, 15), 3.9);
            rows.RemoveAll(r => r.Time > At(6, 15) && r.Time < At(8, 30));
            Set(rows, At(8, 30), 4.0);
            rows.Insert(rows.FindIndex(r => r.Time == At(6, 15)) + 1, new Row(At(6, 15), 3.9));
            break;

        case Scenario.SummerTelAvivMoved:
            Set(rows, At(5, 45), 4.0);
            Set(rows, At(6, 0), 4.1);
            Set(rows, At(6, 15), 9.4);   // door opened for a delivery
            Set(rows, At(6, 30), 4.3);
            // Carried across the shop at room temperature, then into the slightly warmer display fridge.
            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i].Time;
                if (t >= moveOut && t < moveIn) rows[i] = rows[i] with { TempC = 21 + Noise(1) };
                else if (t >= moveIn) rows[i] = rows[i] with { TempC = rows[i].TempC + 0.5 };
            }
            Set(rows, new DateTime(2026, 9, 17, 6, 0, 0), 3.7);
            // Summer's sheet has 05:45 after 06:30; keep that order in the file.
            var early = rows.Find(r => r.Time == At(5, 45))!;
            rows.Remove(early);
            rows.Insert(rows.FindIndex(r => r.Time == At(6, 30)) + 1, early);
            break;

        case Scenario.SummerHaifa:
            // The old logger fails a reading now and then.
            for (int i = 0; i < rows.Count; i++)
                if (rng.NextDouble() < 0.005) rows[i] = rows[i] with { TempC = null };
            Set(rows, At(6, 0), FToC(38.3));
            Set(rows, At(6, 15), FToC(39.0));
            Set(rows, At(6, 30), null);
            break;

        case Scenario.SummerRishonWarming:
            // Compressor failing: creeps up overnight, gives out around 06:00, repaired Tue 15:00.
            var repaired = new DateTime(2026, 9, 15, 15, 0, 0);
            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i].Time;
                if (t < At(6, 0))
                    rows[i] = rows[i] with { TempC = 3.9 + 0.7 * (t - weekStart).TotalHours / 6 + Noise(0.05) };
                else if (t < repaired)
                    rows[i] = rows[i] with { TempC = 9.5 - 2.4 * Math.Exp(-(t - At(6, 45)).TotalHours / 3) + Noise(0.1) };
                else
                    rows[i] = rows[i] with { TempC = rows[i].TempC + 6 * Math.Exp(-(t - repaired).TotalHours / 0.7) };
            }
            Set(rows, At(6, 0), 4.6);
            Set(rows, At(6, 15), 5.4);
            Set(rows, At(6, 30), 6.3);
            Set(rows, At(6, 45), 7.1);
            break;

        case Scenario.WarmingTrend:
            // Sunday evening: rising ~0.55 °C/h, still under 5 when the file ends. Should warn, not alarm.
            var trendStart = weekEnd.AddHours(-4);
            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i].Time;
                rows[i] = t < trendStart
                    ? rows[i] with { TempC = rows[i].TempC + 2.6 - setpoint }
                    : rows[i] with { TempC = 2.6 + 0.55 * (t - trendStart).TotalHours + Noise(0.05) };
            }
            break;

        case Scenario.StopsMidWeek:
            // Battery dies Thursday afternoon.
            rows.RemoveAll(r => r.Time > new DateTime(2026, 9, 17, 14, 30, 0));
            break;

        case Scenario.Gaps:
            rows.RemoveAll(r => r.Time >= new DateTime(2026, 9, 15, 1, 0, 0) && r.Time < new DateTime(2026, 9, 15, 4, 0, 0));
            rows.RemoveAll(r => r.Time >= new DateTime(2026, 9, 18, 13, 15, 0) && r.Time < new DateTime(2026, 9, 18, 15, 15, 0));
            break;

        case Scenario.OngoingExcursion:
            // Warms through Sunday evening and is still above 5 when the file ends.
            var rampStart = weekEnd.AddHours(-4);
            var over = weekEnd.AddHours(-2);
            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i].Time;
                if (t >= rampStart && t < over)
                    rows[i] = rows[i] with { TempC = setpoint + (5.6 - setpoint) * (t - rampStart).TotalHours / 2 + Noise(0.05) };
                else if (t >= over)
                    rows[i] = rows[i] with { TempC = 5.6 + 0.3 * (t - over).TotalHours + Noise(0.05) };
            }
            break;

        case Scenario.ShortExcursion:
            // An hour above 5 on Thursday afternoon, then recovers. The inspector would ask about this one.
            var start = new DateTime(2026, 9, 17, 13, 0, 0);
            double[] values = { 5.8, 6.4, 6.1, 5.3 };
            for (int k = 0; k < values.Length; k++) Set(rows, start.AddMinutes(15 * k), values[k]);
            break;
    }
}

void WriteLoggerFile(Logger logger, List<Row> rows)
{
    var lines = new List<string>();
    string fileName = $"{logger.Id}_{weekStart.ToString("yyyy-MM-dd", inv)}.csv";
    switch (logger.Format)
    {
        case FileFormat.Standard:
            lines.Add("Time,Temp");
            lines.AddRange(rows.Select(r => $"{r.Time.ToString("yyyy-MM-dd HH:mm", inv)},{Celsius(r.TempC)}"));
            break;
        case FileFormat.Swapped:
            lines.Add("Temperature (°C),Timestamp");
            lines.AddRange(rows.Select(r => $"{Celsius(r.TempC)},{r.Time.ToString("yyyy-MM-dd'T'HH:mm:ss", inv)}"));
            break;
        case FileFormat.OldHaifa:
            // Old model: no ID in the file name, ID in a preamble line instead.
            fileName = "haifa_dairy_export.csv";
            lines.Add($"Logger ID;{logger.Id}");
            lines.Add("Model;ThermoLog 200");
            lines.Add("Date;Temperature(F)");
            lines.AddRange(rows.Select(r => $"{r.Time.ToString("dd/MM/yyyy HH:mm", inv)};{Fahrenheit(r.TempC)}"));
            break;
    }
    File.WriteAllText(Path.Combine(loggerDir, fileName), string.Join("\r\n", lines) + "\r\n");
}

void WriteRegistry()
{
    var since = new DateTime(2024, 1, 1);
    var entries = fleet.Select(l => new RegistryEntry(
        l.Id,
        l.Format == FileFormat.OldHaifa ? "F" : "C",
        l.Format switch
        {
            FileFormat.Swapped => "yyyy-MM-dd'T'HH:mm:ss",
            FileFormat.OldHaifa => "dd/MM/yyyy HH:mm",
            _ => "yyyy-MM-dd HH:mm",
        },
        l.Scenario == Scenario.SummerTelAvivMoved
            ? new[] { new Assignment(l.Branch, "Walk-in", since, moveOut), new Assignment(l.Branch, "Display 2", moveIn, null) }
            : new[] { new Assignment(l.Branch, l.Fridge, since, null) }));

    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // keep ' and ° readable
    };
    File.WriteAllText(Path.Combine(outDir, "loggers.json"), JsonSerializer.Serialize(entries, options) + "\n");
}

// Summer's combined sheet exactly as in her email, for the "import my old Excel paste" path.
void WriteSummerSheet()
{
    const string sheet = """
        Logger,Branch,Fridge,Time,Temp
        TL-0512,Jerusalem,Dairy,2026-09-14 06:00,3.8
        TL-0417,Tel Aviv,Walk-in,2026-09-14 06:00,4.1
        TL-0231,Haifa,Dairy,14/09/2026 06:00,38.3
        TL-0512,Jerusalem,Dairy,2026-09-14 06:15,3.9
        TL-0417,Tel Aviv,Walk-in,2026-09-14 06:15,9.4
        TL-0388,Rishon LeZion,Cream cakes,2026-09-14 06:00,4.6
        TL-0417,Tel Aviv,Walk-in,2026-09-14 06:30,4.3
        TL-0231,Haifa,Dairy,14/09/2026 06:15,39.0
        TL-0512,Jerusalem,Dairy,2026-09-14 06:15,3.9
        TL-0388,Rishon LeZion,Cream cakes,2026-09-14 06:15,5.4
        TL-0417,Tel Aviv,Walk-in,2026-09-14 05:45,4.0
        TL-0388,Rishon LeZion,Cream cakes,2026-09-14 06:30,6.3
        TL-0231,Haifa,Dairy,14/09/2026 06:30,ERR
        TL-0388,Rishon LeZion,Cream cakes,2026-09-14 06:45,7.1
        TL-0512,Jerusalem,Dairy,2026-09-14 08:30,4.0
        TL-0417,tel aviv,Display 2,2026-09-17 06:00,3.7
        """;
    File.WriteAllText(Path.Combine(outDir, "summer-sheet-2026-09-14.csv"), sheet.ReplaceLineEndings("\r\n") + "\r\n");
}

DateTime At(int hour, int minute) => weekStart.AddHours(hour).AddMinutes(minute);   // on Mon 14 Sep

void Set(List<Row> rows, DateTime t, double? tempC)
{
    int i = rows.FindIndex(r => r.Time == t);
    rows[i] = rows[i] with { TempC = tempC };
}

double Noise(double amplitude) => (rng.NextDouble() * 2 - 1) * amplitude;

string Celsius(double? c) => c is null ? "ERR" : c.Value.ToString("0.0", inv);
string Fahrenheit(double? c) => c is null ? "ERR" : (c.Value * 9 / 5 + 32).ToString("0.0", inv);

static double FToC(double f) => (f - 32) * 5 / 9;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FridgeMonitor.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repo or pass the repo root.");
}

record Logger(string Id, string Branch, string Fridge, FileFormat Format, Scenario Scenario = Scenario.Normal);
record Row(DateTime Time, double? TempC);
record Assignment(string Branch, string Fridge, DateTime From, DateTime? To);
record RegistryEntry(string Id, string Unit, string DateFormat, Assignment[] Assignments);

enum FileFormat { Standard, Swapped, OldHaifa }

enum Scenario
{
    Normal,
    SummerJerusalem,       // duplicate row + 2h gap (Summer's sample)
    SummerTelAvivMoved,    // door spike, out-of-order row, moved Walk-in -> Display 2 (Summer's sample)
    SummerHaifa,           // Fahrenheit, dd/MM/yyyy, ERR rows (Summer's sample)
    SummerRishonWarming,   // slow warming then failure (Summer's sample)
    WarmingTrend,          // rising but still under 5 at end of file
    StopsMidWeek,          // logger dies Thursday
    Gaps,                  // two multi-hour gaps
    OngoingExcursion,      // above 5 when file ends
    ShortExcursion,        // one hour above 5, recovered
    NoFile,                // registered but no file sent
}
