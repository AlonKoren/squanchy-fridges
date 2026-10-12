# Notes

## Time spent

About **6 hours** in total.

| When | Time | What |
|---|---|---|
| Before the build | ~45 min | Read the assignment and Summer's email, then planned in a Claude chat: listed the traps in her sample and a decision for each (now in [docs/PLAN.md](docs/PLAN.md)). |
| Sat 10 Oct, 17:48–19:40 | ~2 h | First working version in Claude Code: sample data, parser, analysis, storage, UI, README, NOTES. Then registry validation, the inspector-report edge fix, and a UI redesign I reverted. |
| Mon 12 Oct, ~02:00–02:30 | ~30 min | Code review and fixes: double upload, culture in registry errors. |
| Mon 12 Oct, ~03:50–04:25 | ~30 min | Second review: fridge-page total fix, docs restructured. |
| Mon 12 Oct, ~09:00–09:30 | ~30 min | Final pass on the docs. |
| Between sessions | ~1¾ h | Checking: reading the code, clicking through the app, testing uploads and the inspector report. |

Most of the time went into finding the traps in Summer's data and deciding what to do about each one. With an AI tool, writing the code was the fast part.

## Decisions Summer didn't ask for, and why

The full list, with reasoning, is in [docs/PLAN.md](docs/PLAN.md). The main ones:

**Data in**

- **A logger registry replaces the columns Summer types by hand.** `sample-data/loggers.json` gives each logger its unit, date format and which fridge it's in. Units are never guessed from the values: Haifa's 38.3 is °F because the registry says so. A °C logger reporting 38 gets a warning, not a silent conversion.
- **Logger moves have dates.** A reading belongs to whichever fridge the logger was in at that time. The two readings taken while TL-0417 was carried to Display 2 (~21°C) belong to no fridge, so they don't raise a false alarm. The Walk-in shows "no logger in this fridge since Wed 16 Sep".
- **`ERR` is kept as an error reading, never 0.** It's evidence, and it counts as missing data.
- **Duplicates and re-uploads are harmless.** (logger, time) is unique in the database. If a re-upload disagrees with a stored value, the stored value wins and the upload shows a warning, so a record the inspector sees never changes silently.
- **Summer's existing sheet can be imported too.** Hand-typed names are checked against the registry: `tel aviv` matches, and a wrong fridge gets a warning.

**Analysis**

- **"How long above 5°C" = first reading above to first reading back under.** Strictly above 5.0. With readings every 15 minutes that's the honest answer. A gap inside an excursion is flagged "includes missing data".
- **A door opening is not an alarm.** Back under within 30 minutes = brief. This is decided by duration, not reading count, so it still works for a logger that reads every 5 minutes. Brief spikes are still listed in the inspector report, labelled.
- **Early warning for the slow death in Rishon.** A fridge warming faster than 0.5°C/h over the last 3 hours turns orange before it crosses 5°C.
- **Status is the worst thing in the week, not just right now.** Summer reviews weekly. Rishon was above 5°C for 34 hours on Mon–Tue and must not look green on Sunday. The text says "now" or "was".
- **Gaps are shown, not explained.** A gap is a step longer than 2× the logger's usual interval. The file can't say whether the battery died, so the app doesn't pretend.
- **"Now" is the latest reading, not the wall clock.** Files arrive weekly, so wall-clock "now" would make every fridge look stale.
- **Thresholds are settings** in `appsettings.json`, because they are business rules Summer should confirm.

**Running it**

- **Phone first, works offline.** Pages are rendered on the server, and the chart is SVG drawn on the server. No CDN, fonts or JS libraries.
- **SQLite is created on first run.** No migrations, so clone-and-run needs no setup.

## What I'd ask Summer before this goes live

**The rules**

- Is 5°C the limit for every fridge (cream cakes, display, walk-in)? Any freezers?
- How long above 5°C does the Ministry treat as a violation? I used 30 minutes.
- What does the inspector want: a printout or a file? How far back, and how long must records be kept?

**The loggers**

- When exactly was TL-0417 moved, and does the Tel Aviv Walk-in have a logger now? (The move time in the registry is made up.)
- Is the Haifa logger definitely °F, and is it being replaced?
- You mention ~3,000 rows a week, but 12 branches × ~3 fridges × every 15 minutes is ~24,000. What interval do the loggers record at, or which rows do you paste?
- Are logger clocks in Israel time? When summer time ends at the end of October, one hour repeats, and the app would treat the second 01:30 as a duplicate.

**Your current sheet**

- Will you keep using the combined sheet? The import reads each row with that logger's own date format, which matches your sample. If Excel re-saves the sheet, it may rewrite every date in one local format (e.g. `14/09/2026 06:00`). Those rows would be rejected with a clear error, not misread. If you'll keep using it, I'd add a date-format setting for the sheet.

**How you'll use it**

- Who should be told when a fridge goes red, and how (WhatsApp, SMS, email)? At night too?
- Will this run on your laptop, or should branch managers upload directly? Seeing it on your phone away from the office needs hosting and a login.
- If a logger dies mid-week, is finding out on Sunday good enough?

## What's not done, and the next hour

**Not done**

- No screen to edit the registry. Adding or moving a logger means editing `loggers.json` and restarting.
- No alerts. Uploads are still weekly, so a fridge dying on Monday is seen on Sunday.
- No login. Fine on a laptop, not on a network.
- Excel `.xlsx` isn't read. Save it as CSV.
- The repeated hour when summer time ends isn't handled (see the questions).
- Not supported: logger files with date and time in separate columns, and quoted fields containing separators.
- No database migrations. A schema change means deleting `fridges.db`.

**With one more hour:** a page to add and move loggers, with dates. Editing JSON by hand is now the most error-prone step, and a wrong move date silently puts readings in the wrong fridge.

## Working with AI tools

- **How:** planned in a Claude chat, built in Claude Code.
- **Rules for the tool** are in [CLAUDE.md](CLAUDE.md): never guess units, never parse dates with the current culture, ERR is never 0, thresholds are config, every Core change gets a test.
- **Every suggestion I rejected** is in [docs/AI-LOG.md](docs/AI-LOG.md), with how it was caught and where the fix is.

**The main story: the warming detector**

- **The plan:** the first plan used a linear-regression slope over the last 3 hours (above 0.5°C/h = warning).
- **The problem:** a single door-opening spike drags a least-squares line. 11 readings at 3.5°C, one at 9.0 and one back at 3.5 give ~0.6°C/h. That's a "warming" warning on a healthy fridge, the exact noise Summer said is fine.
- **How it was caught:** while writing the analysis tests for the door-spike case.
- **The fix:** Theil–Sen (the median of all pairwise slopes), which ignores one outlier.
- **Where to see it:**
  - the test `A_door_spike_does_not_look_like_warming` in `FridgeMonitor.Tests/AnalysisTests.cs`;
  - `FridgeAnalyzer.TrendCPerHour` in `FridgeMonitor.Core/Analysis.cs`;
  - commit `9c79fe9`.

**Others in the log**

- "Brief = exactly one reading" only works for a 15-minute interval. Changed to a duration.
- A test expected 17 normal fridges when there are 18. The moved logger counts for two fridges, and the failing test caught it.
- Parts of the `ui-ux-pro-max` design were rejected (marketing landing pattern, dark default, Google Fonts, GSAP), because they don't fit an offline internal tool used on a phone in daylight.
- The first inspector report loaded only the readings inside the chosen dates, so an excursion crossing the edge was cut: Rishon on Monday showed "still above" although it recovered on Tuesday. I found it when reviewing where the inspector question could be answered wrong. Now it loads context either side.
- Three bugs in AI-written code, found in code reviews I asked for: a double upload could show an error page; registry error dates used the server culture; the fridge-page total counted time outside the chosen dates.

## Anything else

- **Traps first, then code.** [docs/PLAN.md](docs/PLAN.md) starts with every problem hidden in Summer's 16 rows, and the decision for each, before any code was written.
- **Reproducible sample data.** It's invented around Summer's 16 rows by a seeded generator (`tools/SampleDataGen`). [sample-data/README.md](sample-data/README.md) says which file shows which problem, and an end-to-end test checks every one.
- **The history is as worked, with two exceptions:**
  - Steps 3–5 (model, parser, analysis) were first committed together, then split into three commits. Each one builds and passes its tests.
  - A second UI redesign (the dashboard as a branch-by-branch ops sheet) was committed, then reverted with a reset, so it isn't in the history. It's in [docs/AI-LOG.md](docs/AI-LOG.md) (#8).
