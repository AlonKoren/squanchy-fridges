# Instructions for AI tools

Project: fridge temperature monitor for Squanchy Bakery (Triolla take-home). Spec = Summer's email in the assignment; decisions in `docs/PLAN.md`.

## Stack

- .NET 8, C#. `FridgeMonitor.Core` (logic, no web/DB), `FridgeMonitor.Web` (Razor Pages, EF Core SQLite), `FridgeMonitor.Tests` (xUnit).
- No accounts, no paid services, no external APIs. Must run with `dotnet run --project FridgeMonitor.Web`.

## Rules

- Never guess temperature units from the value. Unit comes from the logger registry.
- Never parse dates with the current culture. Use `TryParseExact` + `CultureInfo.InvariantCulture` with the logger's configured format.
- `ERR` / blank readings are errors, never 0. Keep them.
- Find CSV columns by header name, never by position.
- Above the limit but back under within 30 min (config) is a brief spike, not an alarm. Decide by duration, not reading count.
- Logger→fridge assignment is time-ranged. Resolve by reading timestamp.
- One bad row must not fail an upload. Collect per-line errors.
- Thresholds are config (`appsettings.json`), not literals in code.
- Every behaviour change in Core gets a test. Use Summer's sample rows as fixtures.
- Keep code small and readable; I must be able to explain every line.
- When I reject a suggestion, I log it in `docs/AI-LOG.md`.
- UI work: use the `ui-ux-pro-max` skill (`.claude/skills/ui-ux-pro-max`), but check its output against Summer's context. No external fonts, CDNs or JS libraries: the app must run offline.
