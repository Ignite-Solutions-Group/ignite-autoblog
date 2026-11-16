// Verifies the AutoBlog module end to end on a real, disposable Orchard Core site.
//
//   dotnet run --project tests/VerifyEditorial -c Release
//       A new site: setup, migrations, imports, permissions, templates and media.
//
//   dotnet run --project tests/VerifyEditorial -c Release -- --azure-media
//       The same new-site run with media kept in Azure Blob Storage instead of on disk. It connects to the
//       Azurite emulator unless AUTOBLOG_FIXTURE_AZURE_STORAGE holds another connection string.
//
//   dotnet run --project tests/VerifyEditorial -c Release -- --upgrade-from <folder>
//       An existing site: opens a copy of the Orchard Core 3 database written by tests/SeedOrchard3 and
//       checks that installed content, settings and identities carry over and keep working.
//
// Nothing leaves the machine: the vendor endpoints, image downloads and notification email are in-process
// stand-ins, and the site lives in a temporary SQLite database that is deleted afterwards.
// Set AUTOBLOG_FIXTURE_STARTUP_MINUTES to change how long the first request may take (default 30).

using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;

var startupBudget = TimeSpan.FromMinutes(
    double.TryParse(Environment.GetEnvironmentVariable("AUTOBLOG_FIXTURE_STARTUP_MINUTES"), out var minutes) && minutes > 0 ? minutes : 30);

string? upgradeFrom = null;
var azureMedia = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--upgrade-from" && i + 1 < args.Length) upgradeFrom = Path.GetFullPath(args[++i]);
    else if (args[i] == "--azure-media") azureMedia = true;
    else
    {
        Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: VerifyEditorial [--azure-media | --upgrade-from <seeded folder>]");
        return 2;
    }
}

var checks = new Checks();
var dataPath = Path.Combine(Path.GetTempPath(), "ignite-editorial-" + Guid.NewGuid().ToString("N"));

if (upgradeFrom is null)
{
    var adminPassword = "LocalOnly9!" + Guid.NewGuid().ToString("N");
    var blobs = azureMedia ? AzureMedia.Prepare() : null;
    await using var site = await FixtureHost.StartAsync(dataPath, blobs?.Recipe ?? "EditorialVerification", adminPassword, startupBudget, blobs?.Configuration);
    var scenario = new Scenario();

    await checks.GroupAsync("Setup and migration", async () =>
    {
        await ImportChecks.SetupAsync(site, checks);
        await scenario.CreateBlogAsync(site);
        await scenario.CreateUsersAsync(site);
    });
    await checks.GroupAsync("Vendor and licence failures", () => ImportChecks.FailurePathsAsync(site, checks, scenario));
    await checks.GroupAsync("First import, repeat import and notification", () => ImportChecks.DraftImportAsync(site, checks, scenario));
    await checks.GroupAsync("Allowed and protected updates", () => ImportChecks.UpdatesAsync(site, checks, scenario));
    await checks.GroupAsync("Index recovery and adoption", () => ImportChecks.RecoveryAsync(site, checks, scenario));
    await checks.GroupAsync("Publishing, scheduling and the background task", () => ImportChecks.PublishingAsync(site, checks, scenario));
    await checks.GroupAsync("Settings permissions, saving and manual import", () => AccessChecks.RunAsync(site, checks, scenario));
    await checks.GroupAsync("Notification link", () => AccessChecks.NotificationLinkAsync(site, checks, scenario.NotificationBody));
    await checks.GroupAsync("Detail and summary templates", () => RenderingChecks.TemplatesAsync(site, checks, scenario));
    await checks.GroupAsync("Media names and URLs", () => RenderingChecks.MediaAsync(site, checks, scenario));
    if (blobs != null) await checks.GroupAsync("Azure Blob Storage holds the media", () => blobs.VerifyAsync(site, checks));
    SiteLog(site);
    if (blobs != null) await blobs.RemoveAsync();
}
else
{
    // Work on a copy, so the seeded folder can be reused and is never modified.
    CopyDirectory(upgradeFrom, dataPath);
    var before = SeedManifest.Load(dataPath);
    await using var site = await FixtureHost.StartAsync(dataPath, "EditorialVerification", before.AdminPassword, startupBudget);

    await checks.GroupAsync("Stored content, settings and identities", () => UpgradeChecks.StoredStateAsync(site, checks, before));
    await checks.GroupAsync("Existing accounts and the settings permission", () => UpgradeChecks.AccessAsync(site, checks, before));
    await checks.GroupAsync("Existing posts on the current templates", () => UpgradeChecks.RenderingAsync(site, checks, before));
    await checks.GroupAsync("Imports continue on the upgraded site", () => UpgradeChecks.ContinuedImportsAsync(site, checks, before));
    SiteLog(site);
}

Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
try { Directory.Delete(dataPath, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }

var mode = upgradeFrom is not null ? "upgrade from Orchard Core 3" : azureMedia ? "new site, Azure Blob media" : "new site";
if (checks.Failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"AutoBlog verification ({mode}) FAILED: {checks.Failures.Count} of {checks.Passed + checks.Failures.Count} checks failed.");
    foreach (var failure in checks.Failures) Console.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine();
Console.WriteLine($"AutoBlog verification ({mode}) passed: {checks.Passed} checks (native SQLite, no external calls).");
return 0;

void SiteLog(FixtureHost site)
{
    checks.Section("Site log");
    checks.That(site.Errors.Count == site.ExpectedErrors, "The site logged no unexpected errors",
        string.Join(" | ", site.Errors.Entries.Select(e => e.Length > 300 ? e[..300] : e)));
}

static void CopyDirectory(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
    foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
}
