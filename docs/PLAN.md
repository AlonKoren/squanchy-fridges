# Plan

## Goal

Summer uploads the weekly logger files and sees, on her phone, how every fridge is doing.
She can answer the inspector: "when did this fridge go above 5°C, and for how long?"

## Traps in Summer's sample table

Each row of the sample hides a problem. Decision for each:

| What's in the data | What it really means | Decision |
|---|---|---|
| Haifa TL-0231 shows 38.3, 39.0 | Logger reports Fahrenheit (38.3°F ≈ 3.5°C) | Unit per logger in a registry. Never guess from the value. Warn if a value is implausible for its unit. |
| Haifa uses `14/09/2026` | Different date format | Date format per logger, parsed with `TryParseExact` + invariant culture. No culture guessing. |
| Haifa `ERR` | Sensor error | Keep as an invalid reading (evidence). Never 0. Counts as missing data. |
| Jerusalem 06:15 twice | Duplicate row | Deduplicate on (logger, time). |
| Tel Aviv 05:45 after 06:30 | Out of order | Always sort by time before analysis. |
| Tel Aviv 9.4 once, then 4.3 | Door opened for a delivery | Single reading above limit = "brief", not an alarm. Still listed in the inspector report, labelled. |
| Rishon 4.6 → 5.4 → 6.3 → 7.1 | Slow warming (the lost dairy) | Must go red. Also warn on a rising trend before it crosses 5°C. |
| Jerusalem 06:15 → 08:30 | Gap in the data | Gap = step > 2× the normal interval. Show it. The file can't say why, so don't pretend. |
| TL-0417 Walk-in → Display 2 on the 17th | Logger moved | Logger→fridge assignments have date ranges. A reading belongs to the fridge the logger was in at that time. |
| `tel aviv` vs `Tel Aviv` | Hand-typed names | Branches/fridges live in a registry. Normalize names on import of the legacy sheet. |
| "Columns move around" | Inconsistent files | Find columns by header name, not position. Detect separator. |
| "Files only have time and temp" | Summer types the metadata | Logger ID from filename (or header line); registry supplies branch/fridge. |
| "On my phone" | Mobile | Mobile-first UI. |
| Inspector question | Excursion report | Per-fridge excursions (start, end, duration, peak), filter by date, export CSV. |

## Decisions from building the sample data

- Logger ID comes from the file name (`TL-0512_2026-09-14.csv`). The old Haifa model has no ID in the name, so fall back to its preamble line `Logger ID;TL-0231`.
- When a logger moves, the registry has a hole between "unplugged" and "clipped in" (TL-0417: 10:00–10:30). Readings in that hole belong to no fridge. Report them as "unassigned", never count them against either fridge. Otherwise the 21°C readings while carrying it would show as an alarm.
- After TL-0417 moved, the Tel Aviv Walk-in has no logger. The dashboard should say so, not quietly drop the fridge.
- "Now" for status = the latest reading across all uploads, not the wall clock. Files arrive weekly, so wall-clock "now" would make everything look stale.
- Summer says ~3000 rows a week. 12 branches × ~3 fridges × every 15 min is ~24,000. Either the interval is longer or she pastes only part. Question for Summer. Sample data uses 15 min, as in her sample.

## Decisions: logger registry

- Readings are stored per logger, not per fridge. The fridge is looked up from the registry by time when analysing, so fixing a wrong assignment later fixes the history too.
- Assignment `from` is inclusive, `to` exclusive. A time covered by no assignment (logger in transit) belongs to no fridge.

## Decisions: parser

- Dates: logger's configured format first, then only year-first ISO formats. A day-first/month-first guess never happens.
- Unit comes only from the registry. If the file header names a unit that disagrees, or the median reading is above 15°C, the upload shows a warning. No silent auto-conversion.
- Duplicate (logger, time) rows: keep the first. Same time with a different value → warning.
- One bad row is reported with its line number; the rest of the file is still imported.

## Architecture

- `FridgeMonitor.Core`: parsing, normalization, analysis. No web/DB dependencies. Fully unit tested.
- `FridgeMonitor.Web`: Razor Pages UI + EF Core SQLite storage.
- `FridgeMonitor.Tests`: xUnit, Summer's sample table as the main fixture.

Why: Razor Pages is server-rendered and light on phones. SQLite needs no install or account.
Core separate so the logic is testable without the web layer.

## Status rules (to confirm with Summer; configurable in appsettings.json)

- Red / Alarm: 2+ consecutive valid readings above 5°C (~30 min), or an ongoing excursion.
- Orange / Warning: rising trend over the last 3h (slope > 0.5°C/h) while under 5°C; recent gap; error readings.
- Green / OK: otherwise. Brief spikes are shown but don't change the colour.
- Grey / No data: no readings in the last 7 days.

## Order of work

1. Repo, docs, sample data
2. Domain model + parser + tests (protect this time)
3. Analysis (excursions, gaps, status) + tests
4. Storage (SQLite, unique index on LoggerId+Time so re-uploads are harmless)
5. UI: dashboard, upload, fridge detail, inspector report
6. README + NOTES
