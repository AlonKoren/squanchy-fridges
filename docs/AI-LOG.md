# AI log

Each time an AI tool suggests something wrong or I reject a suggestion: what, how I caught it, where the fix is.
"Original plan" = the Claude chat plan in [initial-plan-chat.md](initial-plan-chat.md).

| Date | What the AI suggested | Why it's wrong / how I caught it | Fix (commit / test) |
|---|---|---|---|
| 2026-10-10 | Original plan: parse CSV with CsvHelper | Files have preamble lines and unknown separators, so we split lines ourselves anyway; CsvHelper added a dependency without removing code. Dropped it. | `Csv` in `Parsing.cs` |
| 2026-10-10 | Original plan (Claude chat): warning on rising trend via linear regression slope > 0.5°C/h | Least squares is dragged by one door-opening spike: 11 × 3.5°C, one 9.0, one 3.5 gives ~0.6°C/h → false "warming" warning. Replaced with Theil–Sen (median of pairwise slopes). | `FridgeAnalyzer.TrendCPerHour`, test `A_door_spike_does_not_look_like_warming` |
| 2026-10-10 | Original plan: `IsBrief => ReadingCount == 1` (door spike = one reading) | Ties the rule to a 15-min interval. A logger reading every 5 min would raise an alarm for a 10-min door opening. Changed to duration: brief if back under within `MinExcursionMinutes` (30). | `FindExcursions`, tests `TelAviv_single_spike_is_brief_not_an_alarm`, `Excursion_runs_until_the_first_reading_back_under_the_limit` |
| 2026-10-10 | Claude Code wrote the sample-week test expecting 17 normal fridges | Forgot TL-0417 counts for two fridges after the move (28 fridges, not 27). Caught by the test failing; checked the registry, the test was wrong, not the code. | `SampleWeekTests.Normal_fridges_are_ok_despite_door_spikes` |
| 2026-10-10 | ui-ux-pro-max skill `--design-system` output: landing-page pattern with hero + "Start trial" CTA, dark theme default, Fira Code via Google Fonts, GSAP scroll reveal | Checked each against Summer's context: internal tool not a marketing site; phone in daylight; must run offline with no external services; motion adds nothing. Kept the palette, style and accessibility checklist only. | `docs/PLAN.md` "Decisions: UI", `wwwroot/css/site.css` header comment |
| 2026-10-10 | Claude Code's first phone screenshot (headless Edge, 390px) looked cut off on the right | Headless Chromium has a ~500px minimum window width, so the shot cropped a wider page. Re-shot inside a 390px iframe: layout was fine. Real issues found that way instead: chart labels ~5px on phone, invisible dark-mode button. Fixed in CSS. | `site.css` `@media (max-width: 600px)` chart rule, `--btn-*` tokens |
