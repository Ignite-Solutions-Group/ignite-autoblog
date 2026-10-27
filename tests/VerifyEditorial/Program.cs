using System.Reflection;
using System.Text.Json.Nodes;
using IgniteSG.OrchardCore.MspMarketingEdge;
using IgniteSG.OrchardCore.MspMarketingEdge.Drivers;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using IgniteSG.OrchardCore.MspMarketingEdge.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using OrchardCore.Email;
using OrchardCore.Environment.Shell;
using OrchardCore.Html.Models;
using OrchardCore.Infrastructure.Html;
using OrchardCore.Seo.Models;
using OrchardCore.Media;
using OrchardCore.Settings;
using YesSql;
using ISession = YesSql.ISession;

Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory, EnvironmentName = Environments.Development });
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:ShellName"] = "Default",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:SiteName"] = "Editorial fixture",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:SiteTimeZone"] = "Pacific/Honolulu",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminUsername"] = "fixture",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminEmail"] = "fixture@example.invalid",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminPassword"] = "LocalOnly9!" + Guid.NewGuid().ToString("N"),
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:DatabaseProvider"] = "Sqlite",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:RecipeName"] = "EditorialVerification"
});
builder.Services.AddOrchardCms().AddSetupFeatures("OrchardCore.AutoSetup");
var errors = new ErrorCapture();
builder.Logging.AddProvider(errors);
var dataPath = Path.Combine(Path.GetTempPath(), "ignite-editorial-" + Guid.NewGuid().ToString("N"));
builder.Services.Configure<ShellOptions>(o => o.ShellsApplicationDataPath = dataPath);
builder.WebHost.UseUrls("http://127.0.0.1:5278");
var app = builder.Build();
app.UseOrchardCore();
await app.StartAsync();
try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    using var setup = await http.GetAsync("http://127.0.0.1:5278/");
    var host = app.Services.GetRequiredService<IShellHost>();
    var settings = new AutoBlogSettings { FeedId = 8, DirectlyPublish = false, EnableGoogleStructuredData = true, PublisherName = "Fixture Publisher" };
    var article = new EdgeFeedArticle
    {
        Id = 123, Slug = "editorial-fixture", Date = "2026-10-01T08:30:00Z", Guid = new() { Rendered = "https://example.invalid/?p=123" },
        Link = "https://example.invalid/editorial-fixture", Title = new() { Rendered = "Vendor title" },
        Content = new() { Rendered = "<p>Vendor body</p>" }, Excerpt = new() { Rendered = "Vendor excerpt" },
        Acf = new() { SendUpdate = true, UpdateVersion = 1 }
    };
    var images = new NoNetworkImages();
    var process = typeof(AutoBlogImportService).GetMethod("ProcessArticleAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var assertions = 0;
    void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        assertions++;
    }
    var nextSync = typeof(AutoBlogSettingsDisplayDriver).GetMethod("CalculateNextSync", BindingFlags.Static | BindingFlags.NonPublic)!;
    var futureYear = DateTime.UtcNow.Year + 1;
    foreach (var (from, expected) in new[]
    {
        (new DateTime(futureYear, 4, 30, 23, 59, 59, DateTimeKind.Utc), new DateTime(futureYear, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
        (new DateTime(futureYear, 5, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(futureYear, 5, 1, 6, 0, 0, DateTimeKind.Utc)),
        (new DateTime(futureYear, 12, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(futureYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc))
    })
        Check((DateTime?)nextSync.Invoke(null, [from]) == expected, "Six-hour UTC schedule retains boundary behavior");
    async Task Scope(Func<IServiceProvider, Task> action)
    {
        var scope = await host.GetScopeAsync("Default");
        await scope.UsingAsync(async s =>
        {
            var accessor = s.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            var context = new DefaultHttpContext { RequestServices = s.ServiceProvider };
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("127.0.0.1", 5278);
            accessor.HttpContext = context;
            try { await action(s.ServiceProvider); }
            finally { accessor.HttpContext = previous; }
        });
    }
    async Task<ImportResult> Process(IServiceProvider sp)
    {
        var service = new AutoBlogImportService(sp.GetRequiredService<ISiteService>(), sp.GetRequiredService<IContentManager>(),
            sp.GetRequiredService<ISession>(), new NoNetworkFeed(), images, sp.GetRequiredService<IEmailService>(),
            sp.GetRequiredService<IHtmlSanitizerService>(), NullLogger<AutoBlogImportService>.Instance,
            sp.GetRequiredService<IMediaFileStore>());
        var result = new ImportResult();
        await (Task)process.Invoke(service, [article, settings, result])!;
        return result;
    }
    async Task<ContentItem> Read(IServiceProvider sp) => (await sp.GetRequiredService<ISession>()
        .Query<ContentItem, ContentItemIndex>(i => i.ContentType == Constants.ContentType && i.Latest).FirstOrDefaultAsync())!;

    await Scope(async sp =>
    {
        var sites = sp.GetRequiredService<ISiteService>();
        var site = await sites.LoadSiteSettingsAsync();
        site.BaseUrl = "https://www.fixture.invalid";
        await sites.UpdateSiteSettingsAsync(site);
    });
    await Scope(async sp => Check((await Process(sp)).CreatedPosts.Count == 1, "Initial import creates draft"));
    JsonNode? protectedContent = null;
    await Scope(async sp =>
    {
        var item = await Read(sp);
        var seo = item.Get<SeoMetaPart>(nameof(SeoMetaPart));
        var schema = JsonNode.Parse(seo.GoogleSchema)!;
        Check(schema["@context"]!.GetValue<string>() == "https://schema.org" && schema["context"] is null, "JSON-LD context keyword");
        Check(schema["@type"]!.GetValue<string>() == "Article" && schema["type"] is null, "JSON-LD article type keyword");
        Check(schema["publisher"]!["@type"]!.GetValue<string>() == "Organization", "JSON-LD publisher type keyword");
        Check(schema["image"]!.GetValue<string>() == "https://www.fixture.invalid/media/autoblog/editorial-fixture.jpeg", "Schema uses the native public media URL");
        Check(DateTime.Parse(schema["datePublished"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind) == item.CreatedUtc,
            "Source publication date is populated before import persistence");
        Check(schema["headline"]!.GetValue<string>() == item.DisplayText, "Schema retains the actual headline");
        Check(!item.Get<AutoBlogPart>(nameof(AutoBlogPart)).PreserveLocalEdits, "Existing imports default to vendor updates");
        item.Alter<HtmlBodyPart>(p => p.Html = "<p>Locally corrected body</p>");
        item.Alter<SeoMetaPart>(p => p.MetaDescription = "Locally corrected description");
        item.Alter<AutoBlogPart>(p => p.PreserveLocalEdits = true);
        await sp.GetRequiredService<IContentManager>().UpdateAsync(item);
    });
    await Scope(async sp => protectedContent = ((JsonObject)(await Read(sp)).Content).DeepClone());
    images.Calls = 0;
    article.Title.Rendered = "New vendor title &lt;/script&gt;&lt;script&gt;alert(1)&lt;/script&gt;";
    article.Content.Rendered = "<p>New vendor body</p>";
    article.Excerpt.Rendered = "New vendor excerpt";
    article.Acf.UpdateVersion = 2;
    await Scope(async sp =>
    {
        var result = await Process(sp);
        Check(result.SkippedCount == 1 && result.UpdatedPosts.Count == 0 && result.CreatedPosts.Count == 0, "Protected update is skipped");
        Check(images.Calls == 0, "Protected update does not download images");
    });
    await Scope(async sp =>
    {
        var item = await Read(sp);
        Check(JsonNode.DeepEquals(protectedContent, (JsonObject)item.Content), "Protected content and SEO persist unchanged");
        Check(item.Get<AutoBlogPart>(nameof(AutoBlogPart)).EdgeUpdateVersion == 1, "Pending vendor version is retained");
        item.Alter<AutoBlogPart>(p => p.PreserveLocalEdits = false);
        await sp.GetRequiredService<IContentManager>().UpdateAsync(item);
    });
    await Scope(async sp => Check((await Process(sp)).UpdatedPosts.Count == 1, "Clearing flag resumes pending update"));
    await Scope(async sp =>
    {
        var item = await Read(sp);
        Check(item.DisplayText == "New vendor title </script><script>alert(1)</script>", "Normal title update persists");
        Check(item.Get<HtmlBodyPart>(nameof(HtmlBodyPart)).Html == "<p>New vendor body</p>", "Normal body update persists");
        Check(item.Get<AutoBlogPart>(nameof(AutoBlogPart)).EdgeUpdateVersion == 2, "Normal version update persists");
        Check(item.Get<SeoMetaPart>(nameof(SeoMetaPart)).MetaRobots == "noindex, follow", "Existing robots policy retained");
        Check(item.Get<SeoMetaPart>(nameof(SeoMetaPart)).Canonical == article.Link, "Publisher canonical retained");
        var seo = item.Get<SeoMetaPart>(nameof(SeoMetaPart));
        Check(!seo.GoogleSchema.Contains("</script>", StringComparison.OrdinalIgnoreCase), "JSON-LD cannot break out of its script element");
        var schema = JsonNode.Parse(seo.GoogleSchema)!;
        Check(schema["headline"]!.GetValue<string>() == item.DisplayText, "Script escaping preserves decoded headline wording");
        Check(DateTime.Parse(schema["datePublished"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind) == item.CreatedUtc,
            "Updates retain the original publication date");
    });
    await Scope(async sp => Check((await Process(sp)).SkippedCount == 1, "Already applied version skips"));
    Check(errors.Count == 0, "Native content handlers logged an error");
    Console.WriteLine($"Editorial import verification passed: {assertions} assertions (native SQLite, no external calls).");
}
finally { await app.StopAsync(); }

sealed class NoNetworkImages : IImageService
{
    public int Calls;
    public Task<string?> DownloadAndStoreImageAsync(string imageUrl, string slug) { Calls++; return Task.FromResult<string?>("autoblog/editorial-fixture.jpeg"); }
}
sealed class NoNetworkFeed : IEdgeApiService
{
    public Task<LicenseCheckResult> CheckLicenseAsync(string email) => throw new InvalidOperationException("Unexpected external request");
    public Task<List<EdgeFeedArticle>> GetFeedArticlesAsync(int feedId, string email) => throw new InvalidOperationException("Unexpected external request");
    public Task<string?> ResolveParentPostUrlAsync(int id) => throw new InvalidOperationException("Unexpected external request");
}
sealed class ErrorCapture : ILoggerProvider
{
    public int Count;
    public ILogger CreateLogger(string name) => new Capture(this);
    public void Dispose() { }
    sealed class Capture(ErrorCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(level)) Interlocked.Increment(ref owner.Count); }
    }
}





