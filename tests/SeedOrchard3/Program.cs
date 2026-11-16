// Builds the "before" site for the upgrade check: Orchard Core 3.0.1 with the released module from nuget.org.
//
//   dotnet run --project tests/SeedOrchard3 -c Release -- <empty output folder>
//
// It sets the site up, imports a representative set of posts through the released module against in-process
// stand-ins for the vendor, and writes a manifest of exactly what was stored. tests/VerifyEditorial then
// opens a copy of that folder on the current build (--upgrade-from) and compares.
// This program is deliberately frozen on Orchard Core 3; do not move it to the repository's Orchard version.

using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using IgniteSG.OrchardCore.MspMarketingEdge;
using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;
using IgniteSG.OrchardCore.MspMarketingEdge.Indexes;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using IgniteSG.OrchardCore.MspMarketingEdge.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Http;
using OrchardCore.Autoroute.Models;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.ContentManagement.Records;
using OrchardCore.Data.Migration.Records;
using OrchardCore.Entities;
using OrchardCore.Environment.Shell;
using OrchardCore.Html.Models;
using OrchardCore.Lists.Models;
using OrchardCore.Seo.Models;
using OrchardCore.Settings;
using OrchardCore.Title.Models;
using OrchardCore.Users.Models;
using OrchardCore.Users.Services;
using YesSql;
using ISession = YesSql.ISession;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: SeedOrchard3 <empty output folder>");
    return 2;
}
var dataPath = Path.GetFullPath(args[0]);
if (Directory.Exists(dataPath) && Directory.EnumerateFileSystemEntries(dataPath).Any())
{
    Console.Error.WriteLine($"The output folder is not empty: {dataPath}");
    return 2;
}
Directory.CreateDirectory(dataPath);
Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "wwwroot"));

var startupBudget = TimeSpan.FromMinutes(
    double.TryParse(Environment.GetEnvironmentVariable("AUTOBLOG_FIXTURE_STARTUP_MINUTES"), out var minutes) && minutes > 0 ? minutes : 30);

var manifest = new SeedManifest
{
    OrchardVersion = "3.0.1",
    ModuleVersion = Constants.Version,
    AdminPassword = "LocalOnly9!" + Guid.NewGuid().ToString("N"),
    UserPassword = "LocalOnly7!" + Guid.NewGuid().ToString("N"),
};
var edge = new FakeEdgeServer();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, EnvironmentName = Environments.Development });
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:ShellName"] = "Default",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:SiteName"] = "Editorial fixture",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:SiteTimeZone"] = "Pacific/Honolulu",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminUsername"] = "fixture",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminEmail"] = "fixture@example.invalid",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminPassword"] = manifest.AdminPassword,
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:DatabaseProvider"] = "Sqlite",
    ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:RecipeName"] = "EditorialVerification",
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddOrchardCms()
    .AddSetupFeatures("OrchardCore.AutoSetup")
    .ConfigureServices(services => services.Configure<HttpClientFactoryOptions>("EdgeApi", o =>
        o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = edge.CreateHandler())), order: 10_000);
builder.Services.Configure<ShellOptions>(o => o.ShellsApplicationDataPath = dataPath);
builder.WebHost.UseUrls("http://127.0.0.1:0");

var app = builder.Build();
app.UseOrchardCore();
await app.StartAsync();
var baseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
var shells = app.Services.GetRequiredService<IShellHost>();

try
{
    Console.WriteLine($"Starting the Orchard Core 3 site at {baseAddress} (waiting up to {startupBudget.TotalMinutes:0} minutes)...");
    var clock = Stopwatch.StartNew();
    using (var first = new HttpClient { Timeout = startupBudget })
        using (await first.GetAsync(baseAddress)) { }
    if (!shells.TryGetSettings("Default", out var shellSettings) || !shellSettings.IsRunning())
        throw new InvalidOperationException("The Orchard Core 3 site did not finish setup.");
    Console.WriteLine($"Site ready after {clock.Elapsed:m\\:ss}.");

    async Task Scope(Func<IServiceProvider, Task> action)
    {
        var scope = await shells.GetScopeAsync("Default");
        await scope.UsingAsync(async s =>
        {
            var accessor = s.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            var context = new DefaultHttpContext { RequestServices = s.ServiceProvider };
            context.Request.Scheme = baseAddress.Scheme;
            context.Request.Host = new HostString(baseAddress.Host, baseAddress.Port);
            accessor.HttpContext = context;
            try { await action(s.ServiceProvider); }
            finally { accessor.HttpContext = previous; }
        });
    }
    Task Configure(Action<AutoBlogSettings> change) => Scope(async sp =>
    {
        var sites = sp.GetRequiredService<ISiteService>();
        var site = await sites.LoadSiteSettingsAsync();
        var settings = site.GetOrCreate<AutoBlogSettings>();
        change(settings);
        site.Put(settings);
        await sites.UpdateSiteSettingsAsync(site);
    });
    async Task<ImportResult> Import(params FakeArticle[] articles)
    {
        edge.Articles.Clear();
        edge.Articles.AddRange(articles);
        foreach (var article in articles.Where(a => manifest.Articles.All(m => m.Id != a.Id))) manifest.Articles.Add(article);
        ImportResult result = null!;
        await Scope(async sp => result = await sp.GetRequiredService<IAutoBlogImportService>().RunImportAsync());
        if (result.ErrorCount > 0) throw new InvalidOperationException("Seeding import failed: " + result.ErrorMessage);
        return result;
    }

    // A blog list, the site address and the two narrow users.
    await Scope(async sp => await sp.GetRequiredService<IContentDefinitionManager>().AlterTypeDefinitionAsync("Blog", type => type
        .Creatable().Listable().Draftable()
        .WithPart("TitlePart", part => part.WithPosition("0"))
        .WithPart("AutoroutePart", part => part.WithPosition("1")
            .WithSettings(new AutoroutePartSettings { AllowCustomPath = true, Pattern = "{{ Model.ContentItem | display_text | slugify }}" }))
        .WithPart("ListPart", part => part.WithPosition("2")
            .WithSettings(new ListPartSettings { PageSize = 20, ContainedContentTypes = ["AutoBlogPost"] }))));
    await Scope(async sp =>
    {
        var content = sp.GetRequiredService<IContentManager>();
        var blog = await content.NewAsync("Blog");
        blog.DisplayText = "Blog";
        blog.Alter<TitlePart>(part => part.Title = "Blog");
        blog.Alter<AutoroutePart>(part => part.Path = "blog");
        await content.CreateAsync(blog, VersionOptions.Published);
        manifest.BlogContentItemId = blog.ContentItemId;
    });
    await Scope(async sp =>
    {
        var sites = sp.GetRequiredService<ISiteService>();
        var site = await sites.LoadSiteSettingsAsync();
        site.BaseUrl = "https://www.fixture.invalid";
        await sites.UpdateSiteSettingsAsync(site);
    });
    await Scope(async sp =>
    {
        var users = sp.GetRequiredService<IUserService>();
        foreach (var (name, role) in new[] { ("blogmanager", "AutoBlogManager"), ("visitor", "AdminVisitor") })
        {
            var problems = new List<string>();
            var user = new User { UserName = name, Email = name + "@example.invalid", EmailConfirmed = true, IsEnabled = true, RoleNames = [role] };
            if (await users.CreateUserAsync(user, manifest.UserPassword, (_, message) => problems.Add(message)) is null || problems.Count > 0)
                throw new InvalidOperationException($"Could not create '{name}': {string.Join("; ", problems)}");
        }
    });

    // Notification email stays blank here: this site has no mail provider and must not try to send.
    await Configure(x =>
    {
        x.LicensedEmail = "licensed+fixture@example.invalid";
        x.FeedId = 8;
        x.DirectlyPublish = false;
        x.TwitterHandle = "@fixture";
        x.PublisherName = "Fixture Publisher";
        x.EnableGoogleStructuredData = true;
        x.BlogContainerContentItemId = manifest.BlogContentItemId;
    });

    var draft = new FakeArticle
    {
        Id = 201, Slug = "seeded-draft", Title = "Seeded draft &amp; more", Content = "<p>Seeded draft body</p>", Excerpt = "<p>Seeded draft excerpt</p>",
        Date = "2026-09-01T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("seed-draft.png"), WeekCommencing = "2026-08-31",
    };
    var edited = new FakeArticle
    {
        Id = 202, Slug = "seeded-edited", Title = "Seeded edited", Content = "<p>Vendor body before local edits</p>", Excerpt = "Seeded edited excerpt",
        Date = "2026-09-02T08:30:00Z", ParentPageUrl = 9002,
    };
    var live = new FakeArticle
    {
        Id = 203, Slug = "seeded-live", Title = "Seeded live", Content = "<p>Seeded live body</p><p>{{ Site.SiteName }} and {{ 'liquid' | append: '-ran' }}</p>",
        Excerpt = "Seeded live excerpt", Date = "2026-09-03T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("seed-live.png"),
    };
    var scheduled = new FakeArticle
    {
        Id = 204, Slug = "seeded-scheduled", Title = "Seeded scheduled", Content = "<p>Seeded scheduled body</p>", Excerpt = "Seeded scheduled excerpt",
        Date = "2031-03-01T09:00:00Z", Status = "future",
    };
    var video = new FakeArticle
    {
        Id = 205, Slug = "seeded-video", Title = "Seeded video", Content = "<p>Seeded video body</p>", Excerpt = "Seeded video excerpt",
        Date = "2026-09-05T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("seed-video.png"),
    };
    var adopted = new FakeArticle
    {
        Id = 206, Slug = "seeded-manual", Title = "Seeded manual", Content = "<p>Vendor body for the hand-made post</p>", Excerpt = "Seeded manual excerpt",
        Date = "2026-09-06T08:30:00Z",
    };
    edge.ParentLinks[9002] = "https://www.yourtechupdates.com/seeded-parent/";

    await Import(draft, edited);
    await Scope(async sp =>
    {
        var content = sp.GetRequiredService<IContentManager>();
        var item = (await sp.GetRequiredService<ISession>()
            .Query<ContentItem, ContentItemIndex>(i => i.ContentType == Constants.ContentType && i.Latest).ListAsync())
            .Single(i => i.Get<AutoBlogPart>(nameof(AutoBlogPart))!.EdgeGuid == edited.Guid);
        item.Alter<HtmlBodyPart>(p => p.Html = "<p>Locally corrected body</p>");
        item.Alter<SeoMetaPart>(p => p.MetaDescription = "Locally corrected description");
        item.Alter<AutoBlogPart>(p => p.PreserveLocalEdits = true);
        await content.UpdateAsync(item);
    });

    // A post an editor made by hand on the route the vendor article would use, to record how the released
    // module treats it.
    string manualId = string.Empty;
    await Scope(async sp =>
    {
        var content = sp.GetRequiredService<IContentManager>();
        var item = await content.NewAsync(Constants.ContentType);
        item.DisplayText = "Seeded manual";
        item.Alter<TitlePart>(p => p.Title = "Seeded manual");
        item.Alter<HtmlBodyPart>(p => p.Html = "<p>Written by hand</p>");
        item.Weld<ContainedPart>();
        item.Alter<ContainedPart>(p => p.ListContentItemId = manifest.BlogContentItemId);
        await content.CreateAsync(item, VersionOptions.Draft);
        manualId = item.ContentItemId;
    });
    var adoption = await Import(adopted);
    manifest.Observations["ReleasedModuleAdoptsHandMadePost"] = (adoption.CreatedPosts.Count == 0).ToString();

    await Configure(x => x.DirectlyPublish = true);
    await Import(live, scheduled);
    await Configure(x => x.FeedId = 6);
    await Import(video);
    await Configure(x => { x.FeedId = 8; x.NotificationEmail = "seeded@example.invalid"; });

    // What the released templates and admin produced on Orchard Core 3, for comparison after the upgrade.
    var cookies = new CookieContainer();
    using (var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = baseAddress })
    {
        manifest.Pages["/blog/seeded-live"] = await (await http.GetAsync("/blog/seeded-live")).Content.ReadAsStringAsync();
        manifest.Pages["/blog/seeded-video"] = await (await http.GetAsync("/blog/seeded-video")).Content.ReadAsStringAsync();
        manifest.Pages["/blog"] = await (await http.GetAsync("/blog")).Content.ReadAsStringAsync();

        var login = await (await http.GetAsync("/Login")).Content.ReadAsStringAsync();
        var fields = Regex.Matches(login, "<input\\b[^>]*>").Select(m => m.Value)
            .Select(tag => (Name: Regex.Match(tag, "\\sname=\"([^\"]*)\"").Groups[1].Value, Value: WebUtility.HtmlDecode(Regex.Match(tag, "\\svalue=\"([^\"]*)\"").Groups[1].Value)))
            .Where(f => f.Name.Length > 0).GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First().Value);
        fields[fields.Keys.First(k => k.EndsWith("UserName", StringComparison.OrdinalIgnoreCase))] = "fixture";
        fields[fields.Keys.First(k => k.EndsWith("Password", StringComparison.OrdinalIgnoreCase))] = manifest.AdminPassword;
        using var signedIn = await http.PostAsync("/Login", new FormUrlEncodedContent(fields));
        manifest.Observations["AdminSignInStatus"] = ((int)signedIn.StatusCode).ToString();
        manifest.Observations["NotificationLinkStatus"] = ((int)(await http.GetAsync("/Admin/Settings/Index?groupId=AutoBlog")).StatusCode).ToString();
        manifest.Observations["SettingsRouteStatus"] = ((int)(await http.GetAsync("/Admin/Settings/AutoBlog")).StatusCode).ToString();
    }

    await Scope(async sp =>
    {
        var session = sp.GetRequiredService<ISession>();
        var posts = await session.Query<ContentItem, ContentItemIndex>(i => i.ContentType == Constants.ContentType && i.Latest).ListAsync();
        manifest.Posts = posts.OrderBy(p => p.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgePostId).Select(SeedPost.From).ToList();
        manifest.IndexedGuids = (await session.QueryIndex<AutoBlogPostIndex>().ListAsync()).Select(i => i.EdgeGuid ?? string.Empty).Order().ToList();
        manifest.Settings = SeedSettings.From((await sp.GetRequiredService<ISiteService>().GetSiteSettingsAsync()).GetOrCreate<AutoBlogSettings>());
        var record = await session.Query<DataMigrationRecord>().FirstOrDefaultAsync();
        manifest.MigrationVersion = record?.DataMigrations
            .FirstOrDefault(m => m.DataMigrationClass == "IgniteSG.OrchardCore.MspMarketingEdge.Migrations.AutoBlogMigrations")?.Version ?? 0;
    });
    manifest.Observations["HandMadePostId"] = manualId;
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

// Released connections can leave SQLite's write-ahead files behind; clearing the pool lets them fold in.
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
manifest.Save(dataPath);
Console.WriteLine($"Seeded Orchard Core {manifest.OrchardVersion} with module {manifest.ModuleVersion}: {manifest.Posts.Count} posts, migration version {manifest.MigrationVersion}.");
foreach (var observation in manifest.Observations) Console.WriteLine($"  {observation.Key}: {observation.Value}");
Console.WriteLine($"Manifest written to {Path.Combine(dataPath, SeedManifest.FileName)}");
return 0;
