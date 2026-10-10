# Squanchy fridges

Upload the weekly temperature logger files from every branch and see, on a phone, how every fridge is doing:
what's wrong, since when, and the answer to the inspector's "when did this fridge go above 5°C, and for how long?".

No accounts, no paid services, no internet needed once packages are restored. Data lives in a local SQLite file.

## Run it

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer (`dotnet --version`).

```bash
git clone <this repo>
cd squanchy-fridges
dotnet run --project FridgeMonitor.Web
```

Open **http://localhost:5051** and click **Load sample week** (or go to **Upload** and pick files from `sample-data/logger-files/`).

The first run restores NuGet packages and creates the database (`FridgeMonitor.Web/fridges.db`). Nothing else to set up.

## Run the tests

```bash
dotnet test
```

62 tests: every row of Summer's sample table, the parser and analysis rules, storage against in-memory SQLite,
and an end-to-end run over the whole sample week.

## What's in it

| Page | What it does |
|---|---|
| Dashboard `/` | One card per fridge, problems first: Alarm (above 5°C for 30+ min), Check (warming, gaps, logger stopped, ERR readings, no logger), No data, OK. Each card says why in plain words. |
| Upload `/Upload` | Pick one or more logger files, or Summer's old combined sheet as CSV. Shows per file what was imported, already stored, or wrong (with line numbers). Uploading the same file twice is safe. |
| Fridge `/Fridge?branch=…&fridge=…` | Chart with the 5°C line (breaks where data is missing), every time above 5°C, gaps and ERR readings. |
| Inspector `/Report` | Pick a fridge (or all) and dates: every excursion with start, end, duration, peak. Print or export CSV. |

## Project layout

```
FridgeMonitor.Core/     parsing + analysis, no web or database code
FridgeMonitor.Web/      Razor Pages UI, EF Core + SQLite storage
FridgeMonitor.Tests/    xUnit tests
tools/SampleDataGen/    generates sample-data/ (fixed seed, reproducible)
sample-data/            loggers.json (registry), logger files, Summer's sheet
docs/PLAN.md            the traps in Summer's email and every decision made
docs/AI-LOG.md          where AI tools got it wrong and how it was caught
CLAUDE.md               instructions for the AI coding tool
.claude/skills/         ui-ux-pro-max skill used for the UI design
```

## Configuration

`FridgeMonitor.Web/appsettings.json`. Paths are relative to `FridgeMonitor.Web/`.

| Setting | Default | Meaning |
|---|---|---|
| `Registry:Path` | `../sample-data/loggers.json` | Which logger is in which fridge, its unit and date format |
| `Database:Path` | `fridges.db` | SQLite file, created on first run |
| `SampleData:Path` | `../sample-data/logger-files` | Folder for the "Load sample week" button |
| `Analysis:LimitC` | `5.0` | Temperature limit |
| `Analysis:MinExcursionMinutes` | `30` | Above the limit at least this long = alarm; shorter = brief (door opening) |
| `Analysis:TrendWindowHours` / `TrendWarnCPerHour` | `3` / `0.5` | Warn when warming faster than this, before it crosses the limit |
| `Analysis:GapFactor` | `2.0` | A step longer than this × the logger's usual interval is a gap |

### Adding or moving a logger

Edit `loggers.json` and restart. A move is a new assignment with dates; readings follow the logger by timestamp:

```json
{ "id": "TL-0417", "unit": "C", "dateFormat": "yyyy-MM-dd'T'HH:mm:ss",
  "assignments": [
    { "branch": "Tel Aviv", "fridge": "Walk-in",   "from": "2024-01-01T00:00:00", "to": "2026-09-16T10:00:00" },
    { "branch": "Tel Aviv", "fridge": "Display 2", "from": "2026-09-16T10:30:00" } ] }
```

## Accepted file formats

- Logger exports with a time/date column and a temperature column, found by header name in any order.
  Separator `,` `;` or tab. Preamble lines above the header are fine.
- Logger ID from the file name (`TL-0512_2026-09-14.csv`) or a preamble line (`Logger ID;TL-0231`).
- Unit (°C/°F) and date format come from `loggers.json`, never guessed from the values.
- `ERR` or unreadable values are kept as error readings, never treated as 0.
- Summer's combined sheet (`Logger, Branch, Fridge, Time, Temp`) saved as CSV.

## Troubleshooting

- **Port 5051 in use:** `dotnet run --project FridgeMonitor.Web --urls http://localhost:5099`
- **Start from an empty database:** stop the app and delete `FridgeMonitor.Web/fridges.db`.
- **Open it on a phone on the same Wi-Fi:** `dotnet run --project FridgeMonitor.Web --urls http://0.0.0.0:5051`, then `http://<laptop-ip>:5051`.
- **Regenerate the sample data:** `dotnet run --project tools/SampleDataGen`
