# AI log

Every time an AI tool suggested something wrong, or I rejected a suggestion: what it was, how it was caught, and where the fix is.

"Original plan" means the plan I made in a Claude chat before writing any code. The traps and decisions from it are in [PLAN.md](PLAN.md).

## Summary

| # | Date | What | Caught by |
|---|---|---|---|
| 1 | 10 Oct | CsvHelper dependency dropped | Building the parser |
| 2 | 10 Oct | Warming trend: least squares replaced with Theil–Sen | Writing the door-spike test |
| 3 | 10 Oct | "Brief = one reading" replaced with "brief = under 30 min" | Thinking about other logger intervals |
| 4 | 10 Oct | Test expected 17 normal fridges, the real number is 18 | The failing test |
| 5 | 10 Oct | Parts of the ui-ux-pro-max design rejected | Checking it against Summer's context |
| 6 | 10 Oct | Phone screenshot looked cut off | Re-shooting in a 390px frame |
| 7 | 10 Oct | Inspector report cut excursions at the edge of the dates | Asking for improvement ideas |
| 8 | 10 Oct | Second UI redesign reverted: it looked worse | My own review of the result |
| 9 | 12 Oct | Double upload could show an error page | Code review |
| 10 | 12 Oct | Registry error dates used the server culture | Code review |
| 11 | 12 Oct | Fridge page total counted time outside the chosen dates | Second code review |

## Details

### 1. CsvHelper

- **Suggested (original plan):** parse the files with the CsvHelper library.
- **Problem:** the files have preamble lines and unknown separators, so we split lines ourselves anyway. CsvHelper added a dependency without removing any code.
- **Fix:** dropped it. See `Csv` in `FridgeMonitor.Core/Parsing.cs`.

### 2. Warming trend

- **Suggested (original plan):** warn when the linear-regression (least-squares) slope over 3 hours is above 0.5°C/h.
- **Problem:** one door-opening spike drags a least-squares line. 11 readings at 3.5°C, one at 9.0 and one back at 3.5 give ~0.6°C/h, so a healthy fridge gets a "warming" warning.
- **Caught:** while writing the analysis test for the door-spike case.
- **Fix:** Theil–Sen (the median of all pairwise slopes), which ignores one outlier. See `FridgeAnalyzer.TrendCPerHour` and the test `A_door_spike_does_not_look_like_warming`.

### 3. Brief spike rule

- **Suggested (original plan):** `IsBrief => ReadingCount == 1`, meaning a door spike is exactly one reading.
- **Problem:** this only works for a 15-minute interval. A logger reading every 5 minutes would raise an alarm for a 10-minute door opening.
- **Fix:** decide by duration: brief if back under within `MinExcursionMinutes` (30). See `FindExcursions` and the tests `TelAviv_single_spike_is_brief_not_an_alarm` and `Excursion_runs_until_the_first_reading_back_under_the_limit`.

### 4. Wrong expected count in a test

- **Suggested (Claude Code):** the sample-week test expected 17 normal fridges.
- **Problem:** after the move, TL-0417 counts for two fridges, so there are 28 fridges, not 27, and 18 normal ones.
- **Caught:** the test failed. I checked the registry: the test was wrong, not the code.
- **Fix:** `SampleWeekTests.Normal_fridges_are_ok_despite_door_spikes`.

### 5. ui-ux-pro-max design output

- **Suggested (the skill's `--design-system`):** a landing page with a hero and a "Start trial" button, a dark theme by default, Fira Code from Google Fonts, and GSAP scroll animations.
- **Problem:** this is an internal tool, not a marketing site. Summer reads it on a phone in daylight. It must run offline, with no external services. The animation adds nothing.
- **Fix:** kept only the palette, the style and the accessibility checklist. See "UI" in `docs/PLAN.md` and the header comment in `wwwroot/css/site.css`.

### 6. Phone screenshot

- **Suggested (Claude Code):** the first phone screenshot (headless Edge, 390px) looked cut off on the right.
- **Problem:** headless Chromium has a ~500px minimum window width, so the screenshot cropped a wider page. Re-shot inside a 390px iframe, the layout was fine.
- **Found that way instead:** chart labels were ~5px on a phone, and the dark-mode button was invisible.
- **Fix:** the `@media (max-width: 600px)` chart rule and the `--btn-*` tokens in `site.css`.

### 7. Inspector report cut at the edges

- **Suggested (Claude Code):** the fridge page and the inspector report loaded only the readings inside the chosen dates.
- **Problem:** an excursion crossing the edge was cut. Rishon on Mon 14 Sep showed "still above" although it recovered on Tuesday. One that started the day before looked like it started at midnight.
- **Caught:** when I asked for improvement ideas and reviewed where the inspector question could be answered wrong.
- **Fix:** load 7 days of context either side and tag the edges. See `Period` in `Analysis.cs` and the tests `Report_for_a_day_shows_the_real_end_not_still_above` and `Inspector_report_for_one_day_shows_when_rishon_actually_recovered`.

### 8. Second UI redesign

- **Suggested (Claude Code):** redesign the dashboard as a branch-by-branch "ops sheet".
- **Problem:** when I looked at the result, it looked worse than the design it replaced, so the change wasn't worth it.
- **Decision:** I didn't keep it and went back to the previous design.
- **Fix:** reset to `9833a6c`, so it isn't in the history.

### 9. Double upload

- **Suggested (Claude Code):** `ImportService` checked "already stored?", then saved.
- **Problem:** two uploads of the same file at the same moment (a double tap) both pass the check. The unique index rejects the second, and Summer gets an error page.
- **Caught:** in a code review I asked for.
- **Fix:** retry once on SQLite error 19. See `ImportService.ImportAsync`, the test `Same_file_uploaded_twice_at_once_does_not_fail`, and commit `d710f11`.

### 10. Dates in registry errors

- **Suggested (Claude Code):** the registry validation messages formatted dates with the server culture.
- **Problem:** this breaks the project's own invariant-culture rule. On an ar-SA server the date comes out as the year 1448.
- **Caught:** in the same review.
- **Fix:** `LoggerRegistry.When`, the test `Problem_dates_are_the_same_whatever_the_server_culture`, and commit `3e2aa94`.

### 11. Fridge page total

- **Suggested (Claude Code):** the fridge page added up each excursion's full duration.
- **Problem:** once excursions kept their real start and end (#7), Rishon on a one-day chart said ~34h above 5°C, and disagreed with the inspector report.
- **Caught:** in a second review.
- **Fix:** `Excursion.DurationWithin`, the test `Time_above_within_a_day_counts_only_that_day`, and commit `3ae992a`.
