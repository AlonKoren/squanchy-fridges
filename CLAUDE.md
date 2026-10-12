# Instructions for AI tools

Project: a fridge temperature monitor for Squanchy Bakery (Triolla take-home).
The spec is Summer's email in the assignment. Decisions are in `docs/PLAN.md`.

## Stack

- .NET 8, C#.
- `FridgeMonitor.Core`: logic only, no web or database code.
- `FridgeMonitor.Web`: Razor Pages, EF Core with SQLite.
- `FridgeMonitor.Tests`: xUnit.
- No accounts, no paid services, no external APIs. It must run with `dotnet run --project FridgeMonitor.Web`.

## Data rules

- Never guess the temperature unit from the value. The unit comes from the logger registry.
- Never parse dates with the current culture. Use `TryParseExact` with `CultureInfo.InvariantCulture` and the logger's configured format.
- `ERR` and blank readings are errors, never 0. Keep them.
- Find CSV columns by header name, never by position.
- The logger-to-fridge assignment is time-ranged. Resolve the fridge from the reading's timestamp.
- One bad row must not fail an upload. Collect errors per line.

## Analysis rules

- Above the limit but back under within 30 minutes (configurable) is a brief spike, not an alarm. Decide by duration, not reading count.
- Thresholds are settings in `appsettings.json`, never literals in code.

## Code rules

- Every behaviour change in Core gets a test. Use Summer's sample rows as fixtures.
- Keep code small and readable. I must be able to explain every line.

## Process

- When I reject a suggestion, I log it in `docs/AI-LOG.md`.
- UI work: use the `ui-ux-pro-max` skill (`.claude/skills/ui-ux-pro-max`), but check its output against Summer's context.
- No external fonts, CDNs or JS libraries. The app must run offline.
