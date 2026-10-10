using FridgeMonitor.Core;
using FridgeMonitor.Web.Data;
using FridgeMonitor.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Paths in appsettings.json are relative to the project folder, so `dotnet run` works from anywhere.
string FromProject(string path) => Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, path));

var registryPath = FromProject(builder.Configuration["Registry:Path"]!);
if (!File.Exists(registryPath))
    throw new FileNotFoundException($"Logger registry not found at {registryPath}. Set Registry:Path in appsettings.json.");

builder.Services.AddRazorPages();
builder.Services.AddSingleton(LoggerRegistry.FromJson(File.ReadAllText(registryPath)));
builder.Services.Configure<AnalysisOptions>(builder.Configuration.GetSection("Analysis"));
builder.Services.AddDbContext<FridgeDb>(o =>
    o.UseSqlite($"Data Source={FromProject(builder.Configuration["Database:Path"]!)}"));
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<FleetService>();

var app = builder.Build();

// No migrations: the schema is created on first run, so clone-and-run needs no setup step.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<FridgeDb>().Database.EnsureCreated();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// No HTTPS redirect: this runs on a laptop over plain http (README), so there is no https port to redirect to.
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
