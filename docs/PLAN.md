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
- `loggers.json` is validated at startup and the app refuses to run on a mistake, listing all of them: duplicate IDs, unusable date formats, no assignments, `to` before `from`, one logger in two fridges at once, two loggers in one fridge at once. A wrong move date would otherwise silently put readings in the wrong fridge.

## Decisions: parser

- Dates: logger's configured format first, then only year-first ISO formats. A day-first/month-first guess never happens.
- Unit comes only from the registry. If the file header names a unit that disagrees, or the median reading is above 15°C, the upload shows a warning. No silent auto-conversion.
- Duplicate (logger, time) rows: keep the first. Same time with a different value → warning.
- One bad row is reported with its line number; the rest of the file is still imported.

## Decisions: analysis

- Excursion = run of readings strictly above 5.0°C. Duration = first reading above → first reading back under. If a gap falls inside, it's flagged "includes missing data".
- Brief vs alarm is by duration (< 30 min), not reading count, so it still works if a logger reads every 5 minutes.
- Status is the worst thing that happened in the period, not just right now. Summer reviews weekly; a fridge that was above 5 for 30h on Monday must not show green on Sunday. The reason text says "now" vs "was".
- Trend uses Theil–Sen (median of pairwise slopes) over the last 3h, so a single door spike doesn't look like warming.
- A single ERR (30-min step) isn't a gap; two in a row is. ERR readings themselves make the fridge orange.
- Reports for a date range load 7 days of readings either side as context. An excursion crossing the edge keeps its real start and end and is tagged "started before / continued after this period"; the total counts only the part inside the dates. Gaps, ERR counts and the chart stay inside the dates.

## Decisions: storage

- SQLite file `FridgeMonitor.Web/fridges.db`, created on first run with `EnsureCreated()`. No migrations, so cloning and running needs no setup step. Real deployment would switch to migrations.
- The DB stores readings and uploads only. The registry stays in `loggers.json`: small, versioned with the code, no editing UI yet.
- Unique index on (LoggerId, Time). Re-uploading the same file (managers do email twice), or a file that overlaps Summer's old sheet, stores nothing new. The upload summary says how many were already stored.
- If a re-upload has a different value for a stored reading, the stored value is kept and the upload gets a warning. First write wins, so an inspector-facing record never silently changes.
- Every upload is recorded with its counts and issues, even when it imported nothing, so "why is this fridge empty?" has an answer.
- Two uploads of the same readings at the same moment (a double tap on a slow phone) both pass the "already stored?" check; the unique index rejects the second. That import is thrown away and run once more, so it reports the readings as already stored instead of showing an error page.
- A file is treated as Summer's combined sheet if it has a Logger column next to Time and Temp; otherwise it's a single logger export.
- Analysis loads the last 7 days into memory. ~24k rows/week is nothing; years of data would need per-fridge queries or pre-computed excursions.

## Decisions: UI

- Razor Pages, server-rendered, no JavaScript framework. Fast on a phone, nothing to build.
- Dropped the template's Bootstrap/jQuery. One hand-written mobile-first stylesheet: one column of cards on a phone, 2–3 on wider screens.
- Visual design from the `ui-ux-pro-max` skill (vendored in `.claude/skills/`, MIT, upstream commit 50d8a7d): ran `--design-system` for "operations monitoring dashboard food safety mobile" (dense, minimal, low motion). Adopted: Swiss/minimal style, slate palette, status green/amber/red, its checklist (4.5:1 contrast, visible keyboard focus, 44px touch targets, `prefers-reduced-motion`, SVG icons not emoji, colour never alone), and the chart text-summary fallback.
- Rejected from the skill's output: its "Operations Landing" pattern (a marketing hero + trial CTA; this is an internal tool), dark as default (Summer reads it on a phone in daylight; dark follows the OS instead), Fira Code/Fira Sans from Google Fonts (needs internet, and a code font is wrong for a bakery manager; system fonts), GSAP scroll animations (a JS dependency for motion that adds nothing).
- Chart is SVG drawn on the server (`TempChart`). No CDN or chart library, so it works offline. The line breaks at gaps and ERR instead of drawing across missing data; every reading above 5 gets a red dot, so brief spikes are visible but labelled.
- Dashboard: count tiles, then problems first (Alarm, Check, No data), OK fridges collapsed below. Each card shows the current temperature, when it was read, and the reasons in plain words. Colour always comes with a text label.
- "Warning" is labelled "Check" for Summer: it means "look at this", not "something broke".
- Inspector report: pick one fridge or all, a date range, then print or export CSV. CSV has a BOM so Excel shows ° and Hebrew-safe UTF-8 correctly. Brief spikes are listed and labelled, not hidden: an inspector should see everything.
- Empty dashboard offers "Load sample week" so a reviewer sees a working app in one click.
- Uploads over 5 MB are rejected (a week from one logger is ~20 KB).

## Architecture

- `FridgeMonitor.Core`: parsing, normalization, analysis. No web/DB dependencies. Fully unit tested.
- `FridgeMonitor.Web`: Razor Pages UI + EF Core SQLite storage.
- `FridgeMonitor.Tests`: xUnit, Summer's sample table as the main fixture.

Why: Razor Pages is server-rendered and light on phones. SQLite needs no install or account.
Core separate so the logic is testable without the web layer.

## Status rules (to confirm with Summer; configurable in appsettings.json)

- Red / Alarm: above 5°C for 30 min or more (now or earlier in the period).
- Orange / Warning: warming > 0.5°C/h over the last 3h while under 5°C; one reading above 5 at the end; gaps; logger stopped; ERR readings; no logger in the fridge.
- Green / OK: otherwise. Brief spikes are shown but don't change the colour.
- Grey / No data: no readings in the period.

## Order of work

1. Repo, docs, sample data
2. Domain model + parser + tests (protect this time)
3. Analysis (excursions, gaps, status) + tests
4. Storage (SQLite, unique index on LoggerId+Time so re-uploads are harmless)
5. UI: dashboard, upload, fridge detail, inspector report
6. README + NOTES
