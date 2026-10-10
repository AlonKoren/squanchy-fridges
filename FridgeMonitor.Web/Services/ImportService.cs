using FridgeMonitor.Core;
using FridgeMonitor.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FridgeMonitor.Web.Services;

public sealed class ImportService(FridgeDb db, LoggerRegistry registry)
{
    /// <summary>
    /// Parses a logger export or Summer's combined sheet (detected from the header) and stores
    /// readings not already in the database. Never throws on bad content: problems go in Upload.Issues.
    /// </summary>
    public async Task<Upload> ImportAsync(string fileName, string content)
    {
        var result = SheetParser.LooksLikeSheet(content)
            ? SheetParser.Parse(content, registry)
            : LoggerFileParser.Parse(fileName, content, registry);

        var upload = new Upload
        {
            FileName = fileName,
            UploadedAt = DateTime.Now,
            LoggerId = result.LoggerId,
            DuplicatesInFile = result.DuplicatesSkipped,
            Issues = result.Issues.Select(i => new UploadIssue { Severity = i.Severity, Line = i.Line, Message = i.Message }).ToList(),
        };
        db.Uploads.Add(upload);

        if (result.Readings.Count > 0)
        {
            var stored = await StoredValuesAsync(result.Readings);
            int conflicts = 0;
            foreach (var r in result.Readings)
            {
                if (stored.TryGetValue((r.LoggerId, r.Time), out var raw))
                {
                    upload.AlreadyStored++;
                    if (raw != r.RawValue) conflicts++;
                    continue;
                }
                upload.Imported++;
                db.Readings.Add(new StoredReading
                {
                    LoggerId = r.LoggerId, Time = r.Time, TempC = r.TempC, RawValue = r.RawValue, Upload = upload,
                });
            }
            if (conflicts > 0)
                upload.Issues.Add(new UploadIssue
                {
                    Severity = IssueSeverity.Warning,
                    Message = $"{conflicts} reading(s) were already stored with a different value. Kept the stored value.",
                });
        }

        await db.SaveChangesAsync();
        return upload;
    }

    /// <summary>Imports every .csv in a folder (the "Load sample week" button).</summary>
    public async Task<List<Upload>> ImportFolderAsync(string folder)
    {
        var uploads = new List<Upload>();
        foreach (var path in Directory.GetFiles(folder, "*.csv").Order())
            uploads.Add(await ImportAsync(Path.GetFileName(path), await File.ReadAllTextAsync(path)));
        return uploads;
    }

    /// <summary>Raw values already stored for these loggers in this time range, keyed by (logger, time).</summary>
    async Task<Dictionary<(string, DateTime), string>> StoredValuesAsync(List<Reading> readings)
    {
        var loggers = readings.Select(r => r.LoggerId).Distinct().ToList();
        var from = readings.Min(r => r.Time);
        var to = readings.Max(r => r.Time);
        var rows = await db.Readings
            .Where(r => loggers.Contains(r.LoggerId) && r.Time >= from && r.Time <= to)
            .Select(r => new { r.LoggerId, r.Time, r.RawValue })
            .ToListAsync();
        return rows.ToDictionary(r => (r.LoggerId, r.Time), r => r.RawValue);
    }
}
