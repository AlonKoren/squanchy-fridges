# Sample data

Made up. Summer's 16 sample rows are the only real data; everything else is generated around them
by `tools/SampleDataGen` (fixed seed, so re-running gives identical files):

```
dotnet run --project tools/SampleDataGen
```

Week covered: Mon 14 Sep – Sun 20 Sep 2026, one reading every 15 minutes.

## Files

- `loggers.json`: the logger registry. Which logger is in which branch/fridge (with date ranges), its unit and its date format. Replaces the columns Summer types by hand.
- `logger-files/`: one file per logger, as a branch manager would email it.
- `summer-sheet-2026-09-14.csv`: Summer's combined sheet, exactly as in her email.

## Three logger file formats (invented)

| Format | Loggers | Looks like |
|---|---|---|
| Standard | most | `Time,Temp` / `2026-09-14 06:00,3.8` |
| Newer model, columns swapped | Tel Aviv, Herzliya | `Temperature (°C),Timestamp` / `4.1,2026-09-14T06:00:00` |
| Old Haifa model | TL-0231 | `;` separator, preamble with `Logger ID;TL-0231`, `14/09/2026 06:00;38.3` in °F, `ERR` rows, file name has no logger ID |

## What each file is meant to show

| Logger | Branch / fridge | Scenario |
|---|---|---|
| TL-0512 | Jerusalem / Dairy | Summer's rows: duplicate 06:15, gap 06:15–08:30 |
| TL-0417 | Tel Aviv / Walk-in → Display 2 | Summer's rows: door spike 9.4, 05:45 row out of order. Moved Wed 16 Sep 10:00–10:30 (2 readings at room temp while carried over) |
| TL-0231 | Haifa / Dairy | Summer's rows: °F, dd/MM/yyyy, ERR (plus a few random ERR) |
| TL-0388 | Rishon LeZion / Cream cakes | Summer's rows: creeps up overnight, 7.1 by 06:45, ~9.5 until repaired Tue 15:00 |
| TL-0632 | Be'er Sheva / Display 1 | Rising ~0.55 °C/h on Sunday evening, still under 5 at the end (should warn) |
| TL-0642 | Holon / Walk-in | Above 5 when the file ends (ongoing excursion) |
| TL-0651 | Ramat Gan / Dairy | One hour above 5 on Thu 13:00, recovered |
| TL-0612 | Ashdod / Walk-in | Stops Thu 14:30 (battery) |
| TL-0621 | Netanya / Dairy | Two gaps (3h Tue night, 2h Fri) |
| TL-0672 | Kfar Saba / Display 1 | Registered, but no file sent |
| others | | Normal, with 2–4 single-reading door spikes on delivery mornings |
