# AI log

Each time an AI tool suggests something wrong or I reject a suggestion: what, how I caught it, where the fix is.

| Date | What the AI suggested | Why it's wrong / how I caught it | Fix (commit / test) |
|---|---|---|---|
| 2026-10-10 | Original plan: parse CSV with CsvHelper | Files have preamble lines and unknown separators, so we split lines ourselves anyway; CsvHelper added a dependency without removing code. Dropped it. | `Csv` in `Parsing.cs` |
| 2026-10-10 | Original plan (Claude chat): warning on rising trend via linear regression slope > 0.5°C/h | Least squares is dragged by one door-opening spike: 11 × 3.5°C, one 9.0, one 3.5 gives ~0.6°C/h → false "warming" warning. Replaced with Theil–Sen (median of pairwise slopes). | `FridgeAnalyzer.TrendCPerHour`, test `A_door_spike_does_not_look_like_warming` |
| 2026-10-10 | Original plan: `IsBrief => ReadingCount == 1` (door spike = one reading) | Ties the rule to a 15-min interval. A logger reading every 5 min would raise an alarm for a 10-min door opening. Changed to duration: brief if back under within `MinExcursionMinutes` (30). | `FindExcursions`, tests `TelAviv_single_spike_is_brief_not_an_alarm`, `Excursion_runs_until_the_first_reading_back_under_the_limit` |
| 2026-10-10 | Claude Code wrote the sample-week test expecting 17 normal fridges | Forgot TL-0417 counts for two fridges after the move (28 fridges, not 27). Caught by the test failing; checked the registry, the test was wrong, not the code. | `SampleWeekTests.Normal_fridges_are_ok_despite_door_spikes` |
