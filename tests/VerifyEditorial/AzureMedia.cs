using System.Text.Json.Nodes;
using Azure.Storage.Blobs;

/// <summary>
/// Runs the site with Orchard's Azure Media Storage feature, so every image the module stores and every
/// media URL its templates produce goes through Blob Storage instead of the local disk.
/// </summary>
sealed class AzureMedia
{
    private const string BasePath = "media";
    private readonly BlobContainerClient _container;

    public string Recipe { get; }
    public Dictionary<string, string?> Configuration { get; }

    private AzureMedia(string connectionString, string containerName, string recipe)
    {
        _container = new BlobContainerClient(connectionString, containerName);
        Recipe = recipe;
        Configuration = new Dictionary<string, string?>
        {
            ["OrchardCore:Media:Azure:ConnectionString"] = connectionString,
            ["OrchardCore:Media:Azure:ContainerName"] = containerName,
            ["OrchardCore:Media:Azure:BasePath"] = BasePath,
            ["OrchardCore:Media:Azure:CreateContainer"] = "true",
            // The emulator has no account-level namespace probe; say so instead of letting detection fail.
            ["OrchardCore:Media:Azure:UseHierarchicalNamespace"] = "false",
        };
    }

    public static AzureMedia Prepare()
    {
        var connectionString = Environment.GetEnvironmentVariable("AUTOBLOG_FIXTURE_AZURE_STORAGE") is { Length: > 0 } configured
            ? configured
            : "UseDevelopmentStorage=true";

        // The same setup recipe with the storage feature added, written beside it so the two cannot drift.
        const string name = "EditorialVerificationAzure";
        var folder = Path.Combine(AppContext.BaseDirectory, "Recipes");
        var recipe = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "EditorialVerification.recipe.json")))!;
        recipe["name"] = name;
        recipe["steps"]!.AsArray().First(step => step?["name"]?.GetValue<string>() == "feature")!["enable"]!.AsArray().Add("OrchardCore.Media.Azure.Storage");
        File.WriteAllText(Path.Combine(folder, name + ".recipe.json"), recipe.ToJsonString());

        return new AzureMedia(connectionString, "autoblog-fixture-" + Guid.NewGuid().ToString("N")[..12], name);
    }

    public async Task VerifyAsync(FixtureHost site, Checks c)
    {
        var names = new List<string>();
        await foreach (var blob in _container.GetBlobsAsync()) names.Add(blob.Name);
        Console.WriteLine($"   Container '{_container.Name}' holds {names.Count} blobs.");

        foreach (var path in new[]
        {
            "autoblog/weekly-security-roundup.png",
            "autoblog/patch-tuesday.png",
            "autoblog/it's a test & more.png",
            "autoblog/100% off #1 (really) + more.png",
            "autoblog/2026/Q4 picks/it's here & now #2 (final) 50%.png",
        })
            c.That(names.Contains($"{BasePath}/{path}"), $"'{path}' is stored as a blob under its own name");

        var local = Path.Combine(site.DataPath, "Sites", FixtureHost.TenantName, "Media", "autoblog");
        c.That(!Directory.Exists(local), "No AutoBlog media was written to the local media folder");
    }

    public async Task RemoveAsync() => await _container.DeleteIfExistsAsync();
}
