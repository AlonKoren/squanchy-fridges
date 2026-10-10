using FridgeMonitor.Core;
using Microsoft.EntityFrameworkCore;

namespace FridgeMonitor.Web.Data;

/// <summary>
/// Stores readings and uploads only. The logger registry stays in loggers.json: it's small,
/// versioned with the code, and there's no UI to edit it yet.
/// </summary>
public sealed class FridgeDb(DbContextOptions<FridgeDb> options) : DbContext(options)
{
    public DbSet<StoredReading> Readings => Set<StoredReading>();
    public DbSet<Upload> Uploads => Set<Upload>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Same logger + same time = same reading. Managers do email the same file twice;
        // re-uploading can never create duplicates.
        b.Entity<StoredReading>().HasIndex(r => new { r.LoggerId, r.Time }).IsUnique();
        b.Entity<StoredReading>().HasIndex(r => r.Time);
        b.Entity<UploadIssue>().Property(i => i.Severity).HasConversion<string>();
    }
}

public sealed class StoredReading
{
    public long Id { get; set; }
    public string LoggerId { get; set; } = "";
    public DateTime Time { get; set; }
    public double? TempC { get; set; }        // null = ERR / unreadable
    public string RawValue { get; set; } = "";  // exactly what the logger wrote
    public int UploadId { get; set; }
    public Upload? Upload { get; set; }

    public Reading ToReading() => new(LoggerId, Time, TempC, RawValue);
}

/// <summary>One uploaded file and what happened to it, so Summer can see why rows were skipped.</summary>
public sealed class Upload
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public string? LoggerId { get; set; }      // null for Summer's multi-logger sheet
    public int Imported { get; set; }
    public int DuplicatesInFile { get; set; }
    public int AlreadyStored { get; set; }
    public List<UploadIssue> Issues { get; set; } = new();
}

public sealed class UploadIssue
{
    public int Id { get; set; }
    public int UploadId { get; set; }
    public IssueSeverity Severity { get; set; }
    public int? Line { get; set; }
    public string Message { get; set; } = "";
}
