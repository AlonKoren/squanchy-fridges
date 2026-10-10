using FridgeMonitor.Core;
using FridgeMonitor.Web.Data;
using FridgeMonitor.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FridgeMonitor.Web.Pages;

public class UploadModel(ImportService import, FridgeDb db, SampleDataFolder sample) : PageModel
{
    // A week from one logger is ~20 KB; anything this big isn't a logger file.
    const long MaxFileBytes = 5 * 1024 * 1024;

    public List<Upload> Results { get; private set; } = new();
    public List<Upload> Recent { get; private set; } = new();

    public async Task OnGetAsync() => Recent = await RecentAsync();

    public async Task<IActionResult> OnPostAsync(List<IFormFile> files)
    {
        foreach (var file in files)
        {
            if (file.Length > MaxFileBytes)
            {
                Results.Add(Rejected(file.FileName, "File is larger than 5 MB, so it isn't a logger export. Nothing imported."));
                continue;
            }
            using var reader = new StreamReader(file.OpenReadStream());
            Results.Add(await import.ImportAsync(file.FileName, await reader.ReadToEndAsync()));
        }
        Recent = await RecentAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSampleAsync()
    {
        Results = await import.ImportFolderAsync(sample.Path);
        Recent = await RecentAsync();
        return Page();
    }

    static Upload Rejected(string fileName, string message) => new()
    {
        FileName = fileName,
        Issues = { new UploadIssue { Severity = IssueSeverity.Error, Message = message } },
    };

    Task<List<Upload>> RecentAsync() => db.Uploads.OrderByDescending(u => u.Id).Take(20).ToListAsync();
}
