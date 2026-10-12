# Plan

## Goal

Summer uploads the weekly logger files and sees, on her phone, how every fridge is doing.
She can answer the inspector: "When did this fridge go above 5°C, and for how long?"

## Contents

1. [Traps in Summer's sample](#1-traps-in-summers-sample)
2. [Status rules](#2-status-rules)
3. [Decisions](#3-decisions): sample data, logger registry, parser, analysis, storage, UI
4. [Architecture](#4-architecture)
5. [Order of work](#5-order-of-work)

## 1. Traps in Summer's sample

Every row of Summer's sample hides a problem. The decision for each one:

| In the data | What it means | Decision |
|---|---|---|
| Haifa TL-0231 shows 38.3, 39.0 | The logger reports Fahrenheit (38.3°F ≈ 3.5°C) | Unit per logger, in a registry. Never guess from the value. Warn if a value is implausible for its unit. |
| Haifa writes `14/09/2026` | A different date format | Date format per logger, read with `TryParseExact` and the invariant culture. No culture guessing. |
| Haifa `ERR` | Sensor error | Keep it as an error reading (it's evidence), never 0. It counts as missing data. |
| Jerusalem 06:15 twice | Duplicate row | Remove duplicates on (logger, time). |
| Tel Aviv 05:45 after 06:30 | Out of order | Always sort by time before analysis. |
| Tel Aviv 9.4 once, then 4.3 | Door opened for a delivery | Back under within 30 min = "brief", not an alarm (decided by duration, not reading count). Still listed in the inspector report, labelled. |
| Rishon 4.6 → 5.4 → 6.3 → 7.1 | Slow warming (the lost dairy) | Must go red. Also warn on a rising trend before it crosses 5°C. |
| Jerusalem 06:15 → 08:30 | Gap in the data | Gap = a step longer than 2× the normal interval. Show it. The file can't say why, so don't pretend. |
| TL-0417 Walk-in → Display 2 on the 17th | Logger moved | Logger-to-fridge assignments have date ranges. A reading belongs to the fridge the logger was in at that time. |
| `tel aviv` vs `Tel Aviv` | Hand-typed names | Branches and fridges live in a registry. Normalize names when importing the old sheet. |
| "Columns move around" | Files differ | Find columns by header name, not position. Detect the separator. |
| "Files only have time and temp" | Summer types the rest by hand | Logger ID from the file name (or a header line). The registry supplies branch and fridge. |
| "On my phone" | Mobile | Mobile-first UI. |
| The inspector's question | Excursion report | Per-fridge excursions (start, end, duration, peak), filtered by date, exported as CSV. |

## 2. Status rules

To confirm with Summer. All thresholds are settings in `appsettings.json`.

| Colour | Status | When |
|---|---|---|
| Red | Alarm | Above 5°C for 30 min or more, now or earlier in the period. |
| Orange | Check (Warning) | Warming faster than 0.5°C/h over the last 3h while under 5°C; one reading above 5 at the end; gaps; logger stopped; ERR readings; no logger in the fridge. |
| Green | OK | Anything else. Brief spikes are shown but don't change the colour. |
| Grey | No data | No readings in the period. |

The fridge gets the worst status of all its reasons.

## 3. Decisions

### Sample data

- The logger ID comes from the file name (`TL-0512_2026-09-14.csv`). The old Haifa model has no ID in the name, so the parser falls back to its preamble line `Logger ID;TL-0231`.
- When a logger moves, the registry has a hole between "unplugged" and "clipped in" (TL-0417: 10:00–10:30). Readings in that hole belong to no fridge. They're shown as "unassigned" and never count against either fridge. Otherwise the 21°C readings taken while carrying it would show as an alarm.
- After TL-0417 moved, the Tel Aviv Walk-in has no logger. The dashboard says so instead of quietly dropping the fridge.
- "Now" = the latest reading across all uploads, not the wall clock. Files arrive weekly, so wall-clock "now" would make everything look stale.
- Summer says ~3,000 rows a week. 12 branches × ~3 fridges × every 15 min is ~24,000. Either the interval is longer or she pastes only part of it. This is a question for Summer. The sample data uses 15 minutes, as in her sample.

### Logger registry

- Readings are stored per logger, not per fridge. The fridge is looked up from the registry by time during analysis, so fixing a wrong assignment later also fixes the history.
- An assignment's `from` is inclusive and its `to` is exclusive. A time covered by no assignment (logger in transit) belongs to no fridge.
- `loggers.json` is checked at startup. On a mistake, the app refuses to run and lists every problem:
  - duplicate IDs;
  - unusable date formats;
  - a logger with no assignments;
  - `to` before `from`;
  - one logger in two fridges at once;
  - two loggers in one fridge at once.

  Without these checks, a wrong move date would silently put readings in the wrong fridge.

### Parser

- Dates: the logger's configured format first, then only year-first ISO formats. It never guesses day-first vs month-first.
- The unit comes only from the registry. If the file header names a different unit, or the median reading is above 15°C, the upload shows a warning. No silent conversion.
- Duplicate (logger, time) rows: keep the first. Same time with a different value: warning.
- A bad row is reported with its line number. The rest of the file is still imported.

### Analysis

- **Excursion** = a run of readings strictly above 5.0°C. Duration = first reading above → first reading back under. If a gap falls inside, it's flagged "includes missing data".
- **Brief vs alarm** is decided by duration (under 30 min), not reading count, so it still works for a logger that reads every 5 minutes.
- **Status is the worst thing in the period, not just right now.** Summer reviews weekly. A fridge above 5°C for 30h on Monday must not show green on Sunday. The reason text says "now" or "was".
- **Trend** uses Theil–Sen (the median of pairwise slopes) over the last 3h, so a single door spike doesn't look like warming.
- **Gaps:** a single ERR (a 30-min step) isn't a gap; two in a row is. ERR readings themselves make the fridge orange.
- **Date ranges:** reports load 7 days of readings either side as context. An excursion crossing the edge keeps its real start and end, and is tagged "started before" or "continued after this period". Totals count only the part inside the dates. Gaps, ERR counts and the chart stay inside the dates.

### Storage

- SQLite file `FridgeMonitor.Web/fridges.db`, created on first run with `EnsureCreated()`. No migrations, so clone-and-run needs no setup step. A real deployment would switch to migrations.
- The database stores readings and uploads only. The registry stays in `loggers.json`: it's small, versioned with the code, and has no editing screen yet.
- Unique index on (LoggerId, Time). Re-uploading the same file (managers do email it twice), or a file that overlaps Summer's old sheet, stores nothing new. The upload summary says how many readings were already stored.
- If a re-upload has a different value for a stored reading, the stored value is kept and the upload gets a warning. First write wins, so a record the inspector sees never changes silently.
- Every upload is recorded with its counts and issues, even when nothing was imported, so "why is this fridge empty?" has an answer.
- Two uploads of the same readings at the same moment (a double tap on a slow phone) both pass the "already stored?" check, and the unique index rejects the second. That import is thrown away and run once more, so it reports the readings as already stored instead of showing an error page.
- A file is treated as Summer's combined sheet if it has a Logger column next to Time and Temp. Otherwise it's a single logger's export.
- Analysis loads the last 7 days into memory, plus 7 days before them as context. ~24k rows a week is nothing. Years of data would need per-fridge queries or pre-computed excursions.

### UI

- Razor Pages, rendered on the server, no JavaScript framework. Fast on a phone, nothing to build.
- The template's Bootstrap and jQuery are removed. One hand-written mobile-first stylesheet: one column of cards on a phone, 2–3 on wider screens.
- The visual design comes from the `ui-ux-pro-max` skill (in `.claude/skills/`, MIT licence, upstream commit 50d8a7d). I ran `--design-system` for "operations monitoring dashboard food safety mobile" (dense, minimal, low motion).
  - **Kept:** Swiss/minimal style; slate palette; status colours green/amber/red; the chart's text summary; its checklist (4.5:1 contrast, visible keyboard focus, 44px touch targets, `prefers-reduced-motion`, SVG icons not emoji, colour never the only signal).
  - **Rejected:**
    - the "Operations Landing" pattern (a marketing hero and trial button; this is an internal tool);
    - dark as default (Summer reads it on a phone in daylight; dark follows the OS setting instead);
    - Fira Code and Fira Sans from Google Fonts (needs internet, and a code font is wrong for a bakery manager; system fonts instead);
    - GSAP scroll animations (a JS dependency for motion that adds nothing).
- The chart is SVG drawn on the server (`TempChart`). No CDN or chart library, so it works offline. The line breaks at gaps and ERR instead of drawing across missing data. Every reading above 5°C gets a red dot, so brief spikes are visible.
- Dashboard: count tiles first, then problems (Alarm, Check, No data), then OK fridges collapsed below. Each card shows the current temperature, when it was read, and the reasons in plain words. Colour always comes with a text label.
- "Warning" is labelled "Check" for Summer: it means "look at this", not "something broke".
- Inspector report: pick one fridge or all, and a date range, then print or export CSV. The CSV starts with a BOM so Excel shows ° and Hebrew correctly as UTF-8. Brief spikes are listed and labelled, not hidden: an inspector should see everything.
- An empty dashboard offers "Load sample week", so a reviewer sees a working app in one click.
- Uploads over 5 MB are rejected. A week from one logger is ~20 KB.

## 4. Architecture

| Project | What's in it |
|---|---|
| `FridgeMonitor.Core` | Parsing, normalization, analysis. No web or database code. Fully unit tested. |
| `FridgeMonitor.Web` | Razor Pages UI and EF Core SQLite storage. |
| `FridgeMonitor.Tests` | xUnit, with Summer's sample table as the main fixture. |

- Razor Pages: rendered on the server and light on phones.
- SQLite: no install, no account.
- Core is separate so the logic can be tested without the web layer.

## 5. Order of work

1. Repo, docs, sample data
2. Domain model, parser and tests (protect this time)
3. Analysis (excursions, gaps, status) and tests
4. Storage (SQLite, unique index on LoggerId+Time so re-uploads are harmless)
5. UI: dashboard, upload, fridge detail, inspector report
6. README and NOTES
