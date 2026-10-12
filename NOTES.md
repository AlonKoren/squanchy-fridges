# Notes

## Time spent

- About 1¼ hours building in Claude Code (first commit 17:48, README at ~19:00), plus planning beforehand in a Claude chat: reading the brief and listing the traps in Summer's sample.

## Decisions Summer didn't ask for, and why

Full list with reasoning in [docs/PLAN.md](docs/PLAN.md). The main ones:

- **A logger registry (`sample-data/loggers.json`) replaces the columns Summer types by hand.** Each logger has its unit, date format and which fridge it's in. Units are never guessed from values: Haifa's 38.3 is °F because the registry says so, and a °C logger reporting 38 gets a warning instead of a silent conversion.
- **Logger moves have dates.** A reading belongs to whichever fridge the logger was in at that time. The two readings taken while TL-0417 was carried to Display 2 (~21°C) belong to no fridge, so they don't raise a false alarm, and the Walk-in shows "no logger in this fridge since Wed 16 Sep".
- **`ERR` is kept as an error reading, never 0.** It's evidence and it counts as missing data.
- **Duplicates and re-uploads are harmless.** Unique (logger, time) in the database. If a re-upload disagrees with a stored value, the stored value wins and the upload shows a warning, so an inspector-facing record never changes silently.
- **"How long above 5°C" = first reading above to first reading back under.** Strictly above 5.0. With readings every 15 minutes that's the honest answer; a gap inside an excursion is flagged "includes missing data".
- **A door opening is not an alarm.** Back under within 30 minutes = brief. Decided by duration, not reading count, so it still works for a logger that reads every 5 minutes. Brief spikes are still listed in the inspector report, labelled.
- **Early warning for the slow death in Rishon.** A fridge warming faster than 0.5°C/h over the last 3 hours turns orange before it crosses 5°C.
- **Status is the worst thing in the week, not just right now.** Summer reviews weekly; Rishon was above 5°C for 34 hours on Mon–Tue and must not look green on Sunday. The text says "now" vs "was".
- **Gaps are shown, not explained.** A step longer than 2× the logger's usual interval. The file can't say whether the battery died, so the app doesn't pretend.
- **"Now" is the latest reading, not the wall clock.** Files arrive weekly; wall-clock "now" would make every fridge look stale.
- **Thresholds are settings** (`appsettings.json`), because they are business rules Summer should confirm.
- **Summer's existing sheet can be imported too**, and hand-typed names are checked against the registry (`tel aviv` matches; a wrong fridge gets a warning).
- **Phone first, works offline.** Server-rendered pages, chart drawn as SVG on the server, no CDN, fonts or JS libraries.
- **SQLite created on first run**, no migrations, so clone-and-run needs no setup.

## What I'd ask Summer before this goes live

- Is 5°C the limit for every fridge (cream cakes, display, walk-in)? Any freezers?
- How long above 5°C does the Ministry treat as a violation? I used 30 minutes.
- When exactly was TL-0417 moved, and does the Tel Aviv Walk-in have a logger now? (The move time in the registry is made up.)
- Is the Haifa logger definitely °F, and is it being replaced?
- You mention ~3,000 rows a week, but 12 branches × ~3 fridges × every 15 minutes is ~24,000. What interval do the loggers record at, or which rows do you paste?
- Will you keep using the combined sheet? The import reads each row with that logger's own date format, which matches your sample. If Excel re-saves the sheet it may rewrite every date in one local format (e.g. `14/09/2026 06:00`), and those rows would be rejected with a clear error, not misread. If you'll keep using it, I'd add a date-format setting for the sheet.
- Are logger clocks in Israel time? When DST ends at the end of October one hour repeats, and the app would treat the second 01:30 as a duplicate.
- Who should be told when a fridge goes red, and how (WhatsApp, SMS, email)? At night too?
- What does the inspector want: a printout, a file? How far back, and how long must records be kept?
- Will this run on your laptop, or should branch managers upload directly? Seeing it on your phone away from the office needs hosting and a login.
- If a logger dies mid-week, is finding out on Sunday good enough?

## What's not done, and the next hour

Not done:

- No screen to edit the registry. Adding or moving a logger means editing `loggers.json` and restarting.
- No alerts; still weekly upload, so a fridge dying on Monday is seen on Sunday.
- No login. Fine on a laptop, not on a network.
- Excel `.xlsx` isn't read; save as CSV.
- DST's repeated hour isn't handled (see questions).
- Logger files with date and time in separate columns, or quoted fields containing separators, aren't supported.
- No database migrations; schema changes mean deleting `fridges.db`.

With one more hour: a page to add and move loggers (with dates), because editing JSON by hand is now the most error-prone step and a wrong move date silently attributes readings to the wrong fridge.

## Working with AI tools

- Planned in a Claude chat, built in Claude Code. Rules for the tool are in [CLAUDE.md](CLAUDE.md) (never guess units, never parse dates with the current culture, ERR is never 0, thresholds are config, every Core change gets a test). Every suggestion I rejected is in [docs/AI-LOG.md](docs/AI-LOG.md).
- **The main one: the warming detector.** The first plan used a linear-regression slope over the last 3 hours (> 0.5°C/h = warning). A single door-opening spike drags a least-squares line: 11 readings at 3.5°C, one at 9.0, one back at 3.5 gives ~0.6°C/h, a "warming" warning on a healthy fridge, the exact noise Summer said is fine. It came up while writing the analysis tests for the door-spike case. Replaced with Theil–Sen (median of all pairwise slopes), which ignores one outlier. See the test `A_door_spike_does_not_look_like_warming` in `FridgeMonitor.Tests/AnalysisTests.cs`, `FridgeAnalyzer.TrendCPerHour` in `FridgeMonitor.Core/Analysis.cs`, commit `9c79fe9`.
- Others in the log: "brief = exactly one reading" (tied to a 15-minute interval, changed to a duration); a test expecting 17 normal fridges when there are 18 (the moved logger counts for two fridges; the failing test caught it); parts of the `ui-ux-pro-max` design output rejected (marketing landing pattern, dark default, Google Fonts, GSAP) because they don't fit an offline internal tool used on a phone in daylight.

## Anything else

- Traps first, then code: [docs/PLAN.md](docs/PLAN.md) starts with every problem hidden in Summer's 16 rows and the decision for each, before any code was written.
- Sample data is invented around Summer's 16 rows by a seeded generator (`tools/SampleDataGen`), so it's reproducible. [sample-data/README.md](sample-data/README.md) says which file shows which problem, and an end-to-end test checks every one.
- The history is as worked, with two exceptions: steps 3–5 (model, parser, analysis) were first committed together and then split into three commits, each of which builds and passes its tests; and a second UI redesign (dashboard as a branch-by-branch ops sheet) was committed, then reverted with a reset, so it isn't in the history. It's in [docs/AI-LOG.md](docs/AI-LOG.md).
