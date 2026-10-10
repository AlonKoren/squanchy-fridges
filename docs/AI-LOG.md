# AI log

Each time an AI tool suggests something wrong or I reject a suggestion: what, how I caught it, where the fix is.

| Date | What the AI suggested | Why it's wrong / how I caught it | Fix (commit / test) |
|---|---|---|---|
| 2026-10-10 | Original plan: parse CSV with CsvHelper | Files have preamble lines and unknown separators, so we split lines ourselves anyway; CsvHelper added a dependency without removing code. Dropped it. | `Csv` in `Parsing.cs` |
