using FridgeMonitor.Core;
using FridgeMonitor.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FridgeMonitor.Web.Pages;

public class IndexModel(FleetService fleet, ImportService import, SampleDataFolder sample) : PageModel
{
    public FleetReport? Fleet { get; private set; }

    public async Task OnGetAsync() => Fleet = await fleet.GetFleetAsync();

    public async Task<IActionResult> OnPostLoadSampleAsync()
    {
        await import.ImportFolderAsync(sample.Path);
        return RedirectToPage();
    }
}
