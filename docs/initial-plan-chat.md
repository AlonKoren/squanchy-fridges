# Initial plan (Claude chat, before any code)

The first plan, from a Claude chat where I uploaded ASSIGNMENT.pdf and asked for a step-by-step C# plan.
Kept as-is so the history shows where the work started. Where it turned out wrong it was changed during
the build; each change is in `docs/AI-LOG.md` (e.g. the linear-regression trend and "brief = 1 reading").

---

Here's a plan for building this in C#. The coding is the easy part. What Triolla is mostly grading is whether you spot the hidden problems in the email, make a clear decision about each one, and write it down. So the plan starts there.

## Step 0: Read the email as a spec and list the traps

Summer's sample table contains every problem on purpose. Find them before writing code:

| What's in the data | What it really means | Decision to make |
|---|---|---|
| Haifa TL-0231 shows 38.3, 39.0 | That logger reports Fahrenheit (38.3°F ≈ 3.5°C) | Store a unit per logger in config. Don't guess from the value. Warn if a value looks wrong for its unit. |
| Haifa uses 14/09/2026 | Different date format | Date format per logger, parsed with TryParseExact. Never use culture guessing. |
| Haifa ERR | Sensor error | Keep it as an "invalid reading" (it's evidence). Never treat it as 0. Count it as missing data. |
| Jerusalem 06:15 appears twice | Duplicate rows | Deduplicate on (logger, time). |
| Tel Aviv 05:45 after 06:30 | Out of order | Always sort by time before analysing. |
| Tel Aviv 9.4 once, then 4.3 | Door opening spike | One reading above the limit = "brief", not an alarm. Still show it in the inspector report, labelled. |
| Rishon 4.6 → 5.4 → 6.3 → 7.1 | Slow warming (the dairy disaster) | This must turn red. Also warn on a rising trend before it crosses 5°C. |
| Jerusalem 06:15 → 08:30 | Gap in the data | Detect gaps longer than about 2× the normal interval and show them. The file can't tell you why, so don't pretend it can. |
| TL-0417 Walk-in → Display 2 on the 17th | Logger moved | Logger→fridge assignments have date ranges. A reading belongs to whichever fridge the logger was in at that time. |
| tel aviv vs Tel Aviv | Hand-typed names | Normalize names. Better still, keep branches and fridges in a registry so nobody types them. |
| "Columns move around" | Inconsistent files | Find columns by header name, not by position. |
| "Logger files only have time and temp" | Summer types the metadata | The app should know which logger is which (from the filename or a header line) so she stops typing it. |
| "On my phone" | Mobile | Mobile-first UI. |
| Inspector question | "When above 5°, how long?" | A per-fridge excursion report (start, end, duration, peak) she can filter by date and export. |

Put this table, cleaned up, into your NOTES.md under "Decisions Summer didn't ask for". That alone covers half the deliverable.

## Step 1: Set up the repo and work in it from the start

They want to see the real history, so commit as you go.

```bash
mkdir squanchy-fridges && cd squanchy-fridges
git init
dotnet new sln -n FridgeMonitor
dotnet new classlib -n FridgeMonitor.Core      # parsing + analysis, no web stuff
dotnet new webapp   -n FridgeMonitor.Web       # Razor Pages UI
dotnet new xunit    -n FridgeMonitor.Tests
dotnet sln add FridgeMonitor.Core FridgeMonitor.Web FridgeMonitor.Tests
dotnet add FridgeMonitor.Web   reference FridgeMonitor.Core
dotnet add FridgeMonitor.Tests reference FridgeMonitor.Core
dotnet add FridgeMonitor.Web package Microsoft.EntityFrameworkCore.Sqlite
dotnet add FridgeMonitor.Core package CsvHelper
```

Also create a docs/ folder with:

- PLAN.md: the trap table above plus your plan.
- AI-LOG.md: short notes every time the AI suggests something wrong. You need one example for NOTES.md, and you'll find several.
- Any instructions file you give your AI tool (e.g. CLAUDE.md). They explicitly asked for this.

Why these choices (you'll be asked):

- Razor Pages is simple, server-rendered, and works well on phones.
- SQLite needs no install and no accounts, which meets their one constraint.
- A separate Core library keeps the logic testable without the web layer.

## Step 2: Make up the sample data

Create sample-data/ with realistic logger files and make them inconsistent on purpose:

```
TL-0512_2026-09-14.csv     Time,Temp                 (yyyy-MM-dd, °C)
TL-0231_2026-09-14.csv     Date;Temperature(F)       (dd/MM/yyyy, °F, ERR rows, ; separator)
TL-0417_2026-09-14.csv     Temp,Timestamp            (columns swapped)
TL-0388_2026-09-14.csv     slow warming to 7°C over 2 days
```

Also create loggers.json, the registry that replaces Summer's typing:

```json
[
  { "id": "TL-0231", "unit": "F", "dateFormat": "dd/MM/yyyy HH:mm",
    "assignments": [ { "branch": "Haifa", "fridge": "Dairy", "from": "2024-01-01" } ] },
  { "id": "TL-0417", "unit": "C", "dateFormat": "yyyy-MM-dd HH:mm",
    "assignments": [
      { "branch": "Tel Aviv", "fridge": "Walk-in",   "from": "2024-01-01", "to": "2026-09-17T00:00" },
      { "branch": "Tel Aviv", "fridge": "Display 2", "from": "2026-09-17T00:00" } ] }
]
```

A small script that generates a week of data for 12 branches × 3 fridges gives you a realistic dashboard. Also accept Summer's existing combined Excel/CSV paste format as a second import option, since that's what she has today.

## Step 3: Domain model (Core)

```csharp
public enum TempUnit { C, F }

public record RawReading(string LoggerId, DateTime Time, double? Value, string RawText);

public record Reading(string LoggerId, string Branch, string Fridge,
                      DateTime Time, double? TempC, bool IsError);

public record Gap(DateTime From, DateTime To) { public TimeSpan Length => To - From; }

public record Excursion(DateTime Start, DateTime End, double PeakC,
                        int ReadingCount, bool Ongoing)
{
    public TimeSpan Duration => End - Start;
    public bool IsBrief => ReadingCount == 1;   // door opening
}

public enum FridgeStatus { Ok, Warning, Alarm, NoData }
```

## Step 4: Parser and normalizer (the core work, test it heavily)

- Detect the separator (, ; or tab).
- Find the time and temperature columns by header name (case-insensitive, match on "time"/"date" and "temp").
- Take the logger ID from the filename, falling back to a header line.
- Parse the time with the logger's configured format using `DateTime.TryParseExact(..., CultureInfo.InvariantCulture, ...)`.
- Parse the value. ERR or blank becomes IsError = true with TempC = null.
- Convert units: `(f - 32) * 5 / 9`.
- Look up branch and fridge from the assignment active at that timestamp.
- Sanity check: if a "°C" logger reports 38, mark the file with a warning. Don't silently convert.
- Deduplicate on (LoggerId, Time) and sort.
- Return parse errors per line instead of throwing, so one bad row doesn't kill the upload.

Write the tests first, using Summer's exact sample table as the fixture. For example:

```csharp
[Fact] public void Haifa_fahrenheit_is_converted()
    => Assert.Equal(3.5, Parse("TL-0231", "14/09/2026 06:00", "38.3").TempC!.Value, 1);

[Fact] public void Err_is_kept_as_error_not_zero() { ... }
[Fact] public void Duplicate_rows_are_removed() { ... }
[Fact] public void Logger_move_assigns_readings_to_new_fridge_after_move_date() { ... }
[Fact] public void Rishon_sample_is_an_alarm() { ... }
[Fact] public void TelAviv_single_spike_is_brief_not_alarm() { ... }
```

These tests are your proof in the interview.

## Step 5: Analysis

Excursions (the inspector's question), run on valid readings sorted by time:

```csharp
public static List<Excursion> FindExcursions(IReadOnlyList<Reading> rs, double limitC = 5.0)
{
    var result = new List<Excursion>();
    int i = 0;
    while (i < rs.Count)
    {
        if (rs[i].TempC > limitC)
        {
            int start = i;
            while (i < rs.Count && rs[i].TempC > limitC) i++;
            bool ongoing = i == rs.Count;
            // ends at the first reading back under the limit (or last known reading)
            var end = ongoing ? rs[i - 1].Time : rs[i].Time;
            var peak = rs.Skip(start).Take(i - start).Max(r => r.TempC!.Value);
            result.Add(new Excursion(rs[start].Time, end, peak, i - start, ongoing));
        }
        else i++;
    }
    return result;
}
```

Decisions to document:

- Duration runs from the first reading above 5 to the first reading back below. That's honest given readings every 15 minutes.
- If a gap falls inside an excursion, flag it as "includes missing data" rather than guessing.

Gaps: the expected interval is the median time between readings (15 min). Any step larger than 2× that is a gap.

Status per fridge (traffic light):

- 🔴 Alarm: 2 or more consecutive readings above 5°C (about 30 min or more), or an ongoing excursion.
- 🟠 Warning: rising trend over the last 3h (linear regression slope > 0.5°C/h) while still under 5; a recent gap; or error readings.
- 🟢 OK: otherwise. Brief spikes are shown, but they don't change the colour.
- ⚪ No data: no file received this week.

Put the thresholds (5°C, 30 min, slope) in appsettings.json, because they're business rules Summer should confirm.

## Step 6: Storage

EF Core + SQLite with tables Loggers, Assignments, Readings, Uploads. Use a unique index on (LoggerId, Time) so re-uploading the same file is harmless. That matters because managers will email the same file twice. Call `db.Database.EnsureCreated()` at startup and seed from loggers.json, so cloning the repo and running it just works.

## Step 7: UI (mobile-first Razor Pages)

- Dashboard `/`: one card per fridge, grouped by branch, sorted problems first (red, then orange, then green). Each card shows the current temperature, last reading time, and a one-line reason ("Above 5°C for 2h 15m", "Warming 0.8°C/h", "No data since Tue").
- Upload `/Upload`: multi-file input (works from a phone), then a summary: rows imported, duplicates skipped, errors per file.
- Fridge detail `/Fridge/{id}`: temperature chart with a line at 5°C and gaps shown as breaks. Chart.js from a CDN is free and needs no account. Below the chart, the excursion list and the gap list.
- Inspector report `/Report`: pick a fridge and date range to get a table of every excursion (start, end, duration, peak, brief/door flag), with an Export CSV button. Also offer print-friendly CSS.

Check it in your browser's mobile view (F12, then device toolbar).

## Step 8: README.md

Keep it to clone-to-running in a few minutes:

```
Requirements: .NET 8 SDK (or newer)
git clone ... && cd squanchy-fridges
dotnet run --project FridgeMonitor.Web
Open http://localhost:5xxx → Upload → select files from /sample-data
dotnet test   # runs the tests
```

Test this on a clean clone before submitting.

## Step 9: NOTES.md

Cover the five points they asked for:

- Time spent: be honest.
- Decisions: the trap table from Step 0.
- Questions for Summer, for example:
  - Is 5°C the limit for every fridge, including cream cakes and display units?
  - How long above 5 is actually a violation?
  - Who should get alerts, and how (WhatsApp, SMS)?
  - Are logger clocks in Israel time, and what happens at the DST change (end of October)?
  - What does the inspector need: a printed sheet or a file?
  - How long do records need to be kept?
- Not done / one more hour: e.g. automatic alerts, a logger-move editing UI, authentication, auto-import from email.
- AI story: one concrete case from docs/AI-LOG.md, linked to the commit or test that fixed it. Likely candidates:
  - The AI auto-detected Fahrenheit with "if value > 30".
  - It treated ERR as 0.
  - It parsed 14/09 with the default culture.
  - It flagged the Tel Aviv door spike as an alarm.

## Suggested time budget (~8–10 hours)

| Step | Time |
|---|---|
| 0: Traps and decisions | 45 min |
| 1–2: Repo and sample data | 1 h |
| 3–5: Parser, analysis, tests | 3 h (protect this time) |
| 6: Storage | 45 min |
| 7: UI | 2 h |
| 8–9: README and NOTES | 1 h |

If you run short on time, cut UI polish, never the tests or NOTES. They said a few decisions made well beats many half-done features.

Finally, make sure you understand every line you submit. The follow-up interview will be about why, and "the AI wrote it" isn't an answer.
