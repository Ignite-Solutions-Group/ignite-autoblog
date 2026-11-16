using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using IgniteSG.OrchardCore.MspMarketingEdge;
using IgniteSG.OrchardCore.MspMarketingEdge.BackgroundTasks;
using IgniteSG.OrchardCore.MspMarketingEdge.Drivers;
using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;
using IgniteSG.OrchardCore.MspMarketingEdge.Indexes;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using OrchardCore.Autoroute.Models;
using OrchardCore.BackgroundTasks;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.Data.Migration.Records;
using OrchardCore.Html.Models;
using OrchardCore.Lists.Models;
using OrchardCore.Media;
using OrchardCore.Media.Fields;
using OrchardCore.PublishLater.Models;
using OrchardCore.Security.Permissions;
using OrchardCore.Seo.Models;
using OrchardCore.Title.Models;
using YesSql;
using ISession = YesSql.ISession;

/// <summary>Setup, migration and the import itself, driven through the module's real services.</summary>
static class ImportChecks
{
    private const string MigrationClass = "IgniteSG.OrchardCore.MspMarketingEdge.Migrations.AutoBlogMigrations";
    private const string ParentCanonical = "https://www.yourtechupdates.com/parent-canonical/";

    public static Task<int?> MigrationVersionAsync(FixtureHost site) => site.ScopeAsync(async sp =>
    {
        var record = await sp.GetRequiredService<ISession>().Query<DataMigrationRecord>().FirstOrDefaultAsync();
        return record?.DataMigrations.FirstOrDefault(m => m.DataMigrationClass == MigrationClass)?.Version;
    });

    public static async Task SetupAsync(FixtureHost site, Checks c)
    {
        c.Equal(4, await MigrationVersionAsync(site), "A new site runs the migration chain through version 4");

        await site.ScopeAsync(async sp =>
        {
            var type = await sp.GetRequiredService<IContentDefinitionManager>().GetTypeDefinitionAsync(Constants.ContentType);
            c.That(type != null, "The AutoBlogPost content type is defined");
            if (type is null) return;

            var parts = type.Parts.Select(p => p.Name).ToHashSet();
            foreach (var part in new[] { "TitlePart", "AutoroutePart", "HtmlBodyPart", "AutoBlogPart", "SeoMetaPart", "PublishLaterPart", "AutoBlogPost", "CommonPart" })
                c.That(parts.Contains(part), $"AutoBlogPost carries {part}");

            var route = type.Parts.First(p => p.Name == "AutoroutePart").GetSettings<AutoroutePartSettings>();
            c.Equal("{{ Model.ContentItem | container | display_text | slugify }}/{{ Model.ContentItem | display_text | slugify }}",
                route.Pattern, "Article routes stay relative to the blog container");

            var image = type.Parts.First(p => p.Name == "AutoBlogPost").PartDefinition.Fields.FirstOrDefault(f => f.Name == "FeaturedImage");
            c.Equal("MediaField", image?.FieldDefinition.Name, "AutoBlogPost.FeaturedImage is a media field");
        });

        c.Equal(0, (await site.IndexedGuidsAsync()).Count, "The AutoBlogPostIndex table exists and is queryable");

        await site.ScopeAsync(async sp =>
        {
            var task = sp.GetServices<IBackgroundTask>().OfType<AutoBlogBackgroundTask>().SingleOrDefault();
            c.That(task != null, "The import background task is registered");
            c.Equal("0 */6 * * *", typeof(AutoBlogBackgroundTask).GetCustomAttribute<BackgroundTaskAttribute>()?.Schedule,
                "The background task keeps its six-hour schedule");

            var permissions = new List<Permission>();
            foreach (var provider in sp.GetServices<IPermissionProvider>())
                permissions.AddRange(await provider.GetPermissionsAsync());
            var permission = permissions.SingleOrDefault(p => p.Name == "ManageAutoBlogSettings");
            c.That(permission != null, "ManageAutoBlogSettings keeps its permission name");
            c.Equal("Manage MSP Marketing Edge AutoBlog settings", permission?.Description?.Value, "The permission description is retained");
        });

        // Orchard runs the six-hour schedule on the site's clock, so the page predicts the next run on it too.
        var nextSync = typeof(AutoBlogSettingsDisplayDriver).GetMethod("CalculateNextSync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var year = DateTime.UtcNow.Year + 1;
        foreach (var (from, expected) in new[]
        {
            (new DateTime(year, 4, 30, 23, 59, 59, DateTimeKind.Utc), new DateTime(year, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            (new DateTime(year, 5, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(year, 5, 1, 6, 0, 0, DateTimeKind.Utc)),
            (new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc), new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        })
            c.Equal((DateTime?)expected, (DateTime?)nextSync.Invoke(null, [from, TimeZoneInfo.Utc]), "On a UTC site the next sync falls on six-hour UTC boundaries");

        // Honolulu is UTC-10 all year: midnight, 6 AM, noon and 6 PM there are 10:00, 16:00, 22:00 and 04:00 UTC.
        var honolulu = TimeZoneInfo.FindSystemTimeZoneById(FixtureHost.SiteTimeZone);
        foreach (var (from, expected) in new[]
        {
            (new DateTime(year, 4, 30, 23, 59, 59, DateTimeKind.Utc), new DateTime(year, 5, 1, 4, 0, 0, DateTimeKind.Utc)),
            (new DateTime(year, 5, 1, 4, 0, 0, DateTimeKind.Utc), new DateTime(year, 5, 1, 10, 0, 0, DateTimeKind.Utc)),
        })
            c.Equal((DateTime?)expected, (DateTime?)nextSync.Invoke(null, [from, honolulu]), "On a Honolulu site the next sync falls on six-hour local boundaries");
    }

    public static async Task FailurePathsAsync(FixtureHost site, Checks c, Scenario s)
    {
        var edge = site.Edge;
        await site.ConfigureAsync(x =>
        {
            x.LicensedEmail = string.Empty;
            x.FeedId = 8;
            x.DirectlyPublish = false;
            x.NotificationEmail = Scenario.NotificationEmails;
            x.TwitterHandle = "@fixture";
            x.PublisherName = "Fixture Publisher";
            x.EnableGoogleStructuredData = true;
            x.BlogContainerContentItemId = s.BlogContentItemId;
        });
        edge.Articles.AddRange([s.Roundup, s.Passwords, s.NoImage]);

        var result = await site.ImportAsync();
        c.That(result.MissingEmail && edge.Requests.Count == 0, "A blank licensed email skips the run without contacting the vendor");

        // Feed Off is a connection test, not silence: it still validates the licence and counts articles.
        await site.ConfigureAsync(x => { x.LicensedEmail = Scenario.LicensedEmail; x.FeedId = 0; });
        result = await site.ImportAsync();
        c.That(result.FeedDisabled && result.LicenseValid && result.ApiArticleCount == 3, "Feed Off reports the connection test result and article count");
        c.That(edge.Count(FakeEdgeServer.LicenseHost) == 1 && edge.FeedRequests == 1, "Feed Off still makes its diagnostic requests");
        var license = edge.Requests.First(r => r.Host == FakeEdgeServer.LicenseHost);
        c.Contains(license.Query, "email=" + Uri.EscapeDataString(Scenario.LicensedEmail), "The licence check sends the licensed email, URL-encoded");
        c.Contains(license.Query, "tool=techupdatesplugin", "The licence check identifies the tool");
        c.Contains(edge.Requests.First(r => r.Host == FakeEdgeServer.FeedHost).Query, "categories=8", "The Feed Off diagnostic reads the US blog feed");
        c.Equal(0, (await site.PostsAsync()).Count, "Feed Off creates no content");
        c.That((await site.SettingsAsync()).LastSyncUtc is null, "The Feed Off diagnostic does not overwrite the sync status");

        await site.ConfigureAsync(x => x.FeedId = 8);

        edge.ClearRequests();
        edge.LicenseActivated = "false";
        result = await site.ImportAsync();
        var status = await site.SettingsAsync();
        c.That(!result.LicenseValid && !result.LicenseTransientError, "A rejected licence is reported as invalid");
        c.Equal("License validation failed", status.LastSyncSummary, "A rejected licence is recorded in the sync status");
        c.That(!status.LastSyncSuccess && status.LastSyncUtc.HasValue, "A rejected licence marks the sync as failed");
        c.Equal(0, edge.FeedRequests, "A rejected licence does not request the feed");
        edge.LicenseActivated = "true";

        edge.LicenseStatus = HttpStatusCode.ServiceUnavailable;
        result = await site.ImportAsync();
        status = await site.SettingsAsync();
        c.That(result.LicenseTransientError, "A vendor server error is treated as temporary");
        c.That(status.LastSyncSummary.StartsWith("MSP Marketing Edge server unreachable", StringComparison.Ordinal) && !status.LastSyncSuccess,
            "A temporary vendor failure is recorded as retryable", status.LastSyncSummary);
        edge.LicenseStatus = HttpStatusCode.OK;

        edge.LicenseUnreachable = true;
        result = await site.ImportAsync();
        c.That(result.LicenseTransientError, "A connection failure is treated as temporary");
        edge.LicenseUnreachable = false;

        var sentinel = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await site.ConfigureAsync(x => x.LastSyncUtc = sentinel);
        edge.FeedStatus = HttpStatusCode.InternalServerError;
        result = await site.ImportAsync();
        c.That(result.ApiArticleCount == 0 && result.ErrorCount == 0, "A failed feed request yields no articles and no exception");
        edge.FeedStatus = HttpStatusCode.OK;
        edge.FeedBody = "<html>not json</html>";
        result = await site.ImportAsync();
        c.Equal(0, result.ApiArticleCount, "A malformed feed response yields no articles");
        edge.FeedBody = null;
        c.That((await site.SettingsAsync()).LastSyncUtc > sentinel, "A failed feed request still records when the sync ran");
        c.Equal(0, (await site.PostsAsync()).Count, "No failure path creates content");
        c.Equal(0, site.Email.Messages.Count, "No failure path sends a notification");
    }

    public static async Task DraftImportAsync(FixtureHost site, Checks c, Scenario s)
    {
        var edge = site.Edge;
        edge.ClearRequests();
        edge.MissingImages.Add("missing.png");
        edge.ParentLinks[9001] = ParentCanonical;

        var result = await site.ImportAsync();
        c.That(result.CreatedPosts.Count == 3 && result.UpdatedPosts.Count == 0 && result.ErrorCount == 0, "The first import creates every article",
            $"{result.CreatedPosts.Count} created, {result.ErrorCount} failed: {result.ErrorMessage}");
        c.That(result.CreatedPosts.All(p => p.Status == "Draft"), "Imports are drafts unless Directly Publish is on");

        var status = await site.SettingsAsync();
        c.Equal("3 created, 0 updated, 0 skipped", status.LastSyncSummary, "The sync status summarises the run");
        c.That(status.LastSyncSuccess && status.LastSyncError is null, "A clean run is recorded as successful");

        var roundup = (await site.PostAsync(s.Roundup))!;
        var passwords = (await site.PostAsync(s.Passwords))!;
        var noImage = (await site.PostAsync(s.NoImage))!;
        c.That(roundup != null && passwords != null && noImage != null, "Every article is stored with its vendor GUID");
        if (roundup is null || passwords is null || noImage is null) return;

        c.That(!roundup.Published && roundup.Latest, "A draft import is not published");
        c.Equal("Weekly security roundup & tips", roundup.DisplayText, "The title is HTML-decoded");
        c.Equal(roundup.DisplayText, roundup.Get<TitlePart>(nameof(TitlePart))?.Title, "The title part matches the display text");
        c.Equal(new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc), roundup.CreatedUtc, "The created date is the vendor's publication date");
        c.Equal(s.BlogContentItemId, roundup.Get<ContainedPart>(nameof(ContainedPart))?.ListContentItemId, "The post joins the configured blog list");
        c.Equal("blog/weekly-security-roundup-tips", roundup.Get<AutoroutePart>(nameof(AutoroutePart))?.Path, "The route is the blog path plus the title slug");

        var html = roundup.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html ?? string.Empty;
        c.Contains(html, "<p>Vendor body A</p>", "The body is stored in HtmlBodyPart");
        c.That(!html.Contains("<script", StringComparison.OrdinalIgnoreCase) && !html.Contains("onerror", StringComparison.OrdinalIgnoreCase),
            "Imported HTML is sanitized", html);
        c.That(((JsonObject)roundup.Content)["LiquidPart"] is null, "Vendor HTML is not converted into a Liquid part");

        var part = roundup.Get<AutoBlogPart>(nameof(AutoBlogPart))!;
        c.That(part.EdgeGuid == s.Roundup.Guid && part.EdgePostId == 101 && part.EdgeUpdateVersion == 1 && part.EdgeWeekCommencing == "2026-09-28"
            && part.EdgeUrl == s.Roundup.Link && !part.EdgeHasVideo && !part.PreserveLocalEdits, "AutoBlogPart records the vendor metadata");

        var featured = roundup.Get<ContentPart>(Constants.ContentType)?.Get<MediaField>("FeaturedImage");
        c.Equal("/autoblog/weekly-security-roundup.png", featured?.Paths?.FirstOrDefault(), "The featured image is stored under the autoblog media folder");
        c.Equal(roundup.DisplayText, featured?.MediaTexts?.FirstOrDefault(), "The featured image text is the title");
        c.Equal("/autoblog/weekly-security-roundup.png", ((JsonObject)roundup.Content)["SeoMetaPart"]?["OpenGraphImage"]?["Paths"]?[0]?.GetValue<string>(),
            "The stored Open Graph image is a Paths array holding the same media path");
        await site.ScopeAsync(async sp =>
        {
            var file = await sp.GetRequiredService<IMediaFileStore>().GetFileInfoAsync("autoblog/weekly-security-roundup.png");
            c.Equal((long?)FakeEdgeServer.PngBytes.Length, file?.Length, "The downloaded image is saved through the media store");
        });

        var seo = roundup.Get<SeoMetaPart>(nameof(SeoMetaPart))!;
        c.That(seo.Render && seo.PageTitle == roundup.DisplayText && seo.MetaDescription == "Excerpt A with markup.", "SEO title and description come from the article");
        c.Equal(s.Roundup.Link, seo.Canonical, "The canonical URL points at the vendor's original");
        c.Equal("noindex, follow", seo.MetaRobots, "Syndicated copies are noindex, follow");
        c.That(seo.OpenGraphType == "article" && seo.OpenGraphTitle == roundup.DisplayText, "Open Graph metadata is populated");
        c.That(seo.TwitterCard == "summary_large_image" && seo.TwitterCreator == "@fixture" && seo.TwitterSite == "@fixture", "Twitter card metadata is populated");

        var schema = JsonNode.Parse(seo.GoogleSchema)!;
        c.That(schema["@context"]?.GetValue<string>() == "https://schema.org" && schema["context"] is null, "JSON-LD uses the @context keyword");
        c.That(schema["@type"]?.GetValue<string>() == "Article" && schema["type"] is null, "JSON-LD uses the @type keyword");
        c.That(schema["publisher"]?["@type"]?.GetValue<string>() == "Organization" && schema["publisher"]?["name"]?.GetValue<string>() == "Fixture Publisher",
            "JSON-LD names the configured publisher");
        c.Equal(FixtureHost.SiteBaseUrl + "/media/autoblog/weekly-security-roundup.png", schema["image"]?.GetValue<string>(), "JSON-LD uses the absolute public image URL");
        c.That(DateTime.Parse(schema["datePublished"]!.GetValue<string>(), null, DateTimeStyles.RoundtripKind) == roundup.CreatedUtc, "JSON-LD carries the source publication date");
        c.Equal(roundup.DisplayText, schema["headline"]?.GetValue<string>(), "JSON-LD carries the headline");

        c.Equal(ParentCanonical, passwords.Get<SeoMetaPart>(nameof(SeoMetaPart))?.Canonical, "A parent post supplies the canonical URL");
        c.Equal(ParentCanonical, passwords.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgeUrl, "The resolved canonical is stored as EdgeUrl");
        var passwordSeo = passwords.Get<SeoMetaPart>(nameof(SeoMetaPart))!;
        c.That(passwordSeo.MetaDescription.Length == 160 && passwordSeo.MetaDescription.EndsWith("...", StringComparison.Ordinal), "The meta description is trimmed to 160 characters");
        c.That(passwordSeo.OpenGraphDescription.Length == 300 && passwordSeo.TwitterDescription.Length == 200, "Social descriptions keep their own length limits");
        c.That(passwordSeo.TwitterCard == "summary" && (passwordSeo.OpenGraphImage?.Paths?.Length ?? 0) == 0, "An image that cannot be downloaded leaves the post without one");
        c.That(JsonNode.Parse(passwordSeo.GoogleSchema)!["image"] is null, "JSON-LD omits an image that was not stored");
        c.That(noImage.Get<ContentPart>(Constants.ContentType)?.Get<MediaField>("FeaturedImage")?.Paths is not { Length: > 0 }, "An article without an image is still imported");

        c.Equal(3, (await site.IndexedGuidsAsync()).Count, "Each post has a GUID index row");

        var mail = site.Email.Messages;
        c.Equal(2, mail.Count, "Each notification address receives one message");
        c.That(mail.All(m => m.Subject == "AutoBlog — new DRAFT blog post(s)"), "Draft imports use the draft notification subject");
        c.That(mail.Select(m => m.To).Order().SequenceEqual(["editor@example.invalid", "second@example.invalid"]), "Notifications go to the configured addresses");
        var body = mail.FirstOrDefault()?.HtmlBody ?? string.Empty;
        c.Contains(body, "Weekly security roundup &amp; tips", "The notification lists new titles, HTML-encoded");
        c.Contains(body, "DRAFT mode", "The notification says the posts are drafts");
        s.NotificationBody = body;
        site.Email.Clear();

        // The vendor feed is a rolling window, so the same articles come back on every run.
        var imageRequests = edge.Count(FakeEdgeServer.ImageHost);
        result = await site.ImportAsync();
        c.That(result.CreatedPosts.Count == 0 && result.UpdatedPosts.Count == 0 && result.SkippedCount == 3, "Repeating the import changes nothing");
        c.Equal(3, (await site.PostsAsync()).Count, "Repeating the import creates no duplicates");
        c.Equal(imageRequests, edge.Count(FakeEdgeServer.ImageHost), "Repeating the import downloads no images");
        c.Equal(0, site.Email.Messages.Count, "A run with no changes sends no notification");
    }

    public static async Task UpdatesAsync(FixtureHost site, Checks c, Scenario s)
    {
        var edge = site.Edge;
        var original = (await site.PostAsync(s.Roundup))!;

        // An allowed update: the vendor asks for it and the version moved.
        s.Roundup.Title = "Weekly security roundup, revised &lt;/script&gt;&lt;script&gt;alert(1)&lt;/script&gt;";
        s.Roundup.Content = "<p>Revised vendor body A</p>";
        s.Roundup.Excerpt = "Revised excerpt A";
        s.Roundup.UpdateVersion = 2;
        // Not flagged for update, so its newer version must be ignored.
        s.NoImage.UpdateVersion = 5;
        s.NoImage.Content = "<p>Vendor body C that must not arrive</p>";

        var result = await site.ImportAsync();
        c.That(result.UpdatedPosts.Count == 1 && result.CreatedPosts.Count == 0 && result.SkippedCount == 2, "Only the flagged, re-versioned article updates");

        var updated = (await site.PostAsync(s.Roundup))!;
        c.Equal(original.ContentItemId, updated.ContentItemId, "An update keeps the content item identity");
        c.Equal("Weekly security roundup, revised </script><script>alert(1)</script>", updated.DisplayText, "An update replaces the title");
        c.Equal("<p>Revised vendor body A</p>", updated.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html, "An update replaces the body");
        c.Equal("blog/weekly-security-roundup-tips", updated.Get<AutoroutePart>(nameof(AutoroutePart))?.Path, "The article URL stays stable when the title changes");
        c.Equal(2, updated.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgeUpdateVersion, "An update records the new vendor version");
        c.Equal(original.CreatedUtc, updated.CreatedUtc, "An update keeps the original publication date");
        c.That(!updated.Published, "An updated draft stays a draft while Directly Publish is off");

        var seo = updated.Get<SeoMetaPart>(nameof(SeoMetaPart))!;
        c.That(seo.MetaRobots == "noindex, follow" && seo.Canonical == s.Roundup.Link, "An update leaves robots and canonical unchanged");
        c.Equal("Revised excerpt A", seo.MetaDescription, "An update refreshes the description");
        c.That(!seo.GoogleSchema.Contains("</script>", StringComparison.OrdinalIgnoreCase), "JSON-LD cannot close its own script element");
        var schema = JsonNode.Parse(seo.GoogleSchema)!;
        c.Equal(updated.DisplayText, schema["headline"]?.GetValue<string>(), "Script escaping preserves the headline wording");
        c.That(DateTime.Parse(schema["datePublished"]!.GetValue<string>(), null, DateTimeStyles.RoundtripKind) == updated.CreatedUtc, "JSON-LD keeps the original publication date after an update");

        var untouched = (await site.PostAsync(s.NoImage))!;
        c.That(untouched.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgeUpdateVersion == 1
            && untouched.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html == "<p>Vendor body C</p>", "An article not flagged for update is left alone");

        var mail = site.Email.Messages;
        c.That(mail.Count == 2 && mail[0].HtmlBody.Contains("Updated Posts", StringComparison.Ordinal), "An update is announced to the notification addresses");
        site.Email.Clear();

        // A protected update: an editor has taken the article over locally.
        await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var item = await content.GetAsync(((await site.PostAsync(s.Passwords))!).ContentItemId, VersionOptions.Latest);
            item.Alter<HtmlBodyPart>(p => p.Html = "<p>Locally corrected body</p>");
            item.Alter<SeoMetaPart>(p => p.MetaDescription = "Locally corrected description");
            item.Alter<AutoBlogPart>(p => p.PreserveLocalEdits = true);
            await content.UpdateAsync(item);
        });
        var protectedContent = ((JsonObject)(await site.PostAsync(s.Passwords))!.Content).DeepClone();

        s.Passwords.Title = "Password habits from the vendor";
        s.Passwords.Content = "<p>New vendor body B</p>";
        s.Passwords.ImageUrl = FakeEdgeServer.ImageUrl("b-new.png");
        s.Passwords.UpdateVersion = 2;
        var imageRequests = edge.Count(FakeEdgeServer.ImageHost);

        result = await site.ImportAsync();
        c.That(result.UpdatedPosts.Count == 0 && result.CreatedPosts.Count == 0 && result.SkippedCount == 3, "A protected article is skipped");
        c.Equal(imageRequests, edge.Count(FakeEdgeServer.ImageHost), "A protected article downloads no image");
        var kept = (await site.PostAsync(s.Passwords))!;
        c.That(JsonNode.DeepEquals(protectedContent, (JsonObject)kept.Content), "Protected body, images and SEO are stored unchanged");
        c.Equal(1, kept.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgeUpdateVersion, "The pending vendor version is not consumed while protected");

        await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var item = await content.GetAsync(kept.ContentItemId, VersionOptions.Latest);
            item.Alter<AutoBlogPart>(p => p.PreserveLocalEdits = false);
            await content.UpdateAsync(item);
        });
        result = await site.ImportAsync();
        c.That(result.UpdatedPosts.Count == 1 && result.SkippedCount == 2, "Clearing protection applies the pending vendor update");
        var resumed = (await site.PostAsync(s.Passwords))!;
        c.That(resumed.DisplayText == "Password habits from the vendor"
            && resumed.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html == "<p>New vendor body B</p>"
            && resumed.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgeUpdateVersion == 2, "The resumed update replaces title, body and version");
        c.That(resumed.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgeUrl == ParentCanonical
            && resumed.Get<SeoMetaPart>(nameof(SeoMetaPart))?.Canonical == ParentCanonical, "An update keeps the stored canonical URL");
        c.Equal("/autoblog/blog-password-habits.png",
            resumed.Get<ContentPart>(Constants.ContentType)?.Get<MediaField>("FeaturedImage")?.Paths?.FirstOrDefault(), "An update stores the refreshed image");
        site.Email.Clear();

        result = await site.ImportAsync();
        c.Equal(3, result.SkippedCount, "An already applied version is skipped");
    }

    public static async Task RecoveryAsync(FixtureHost site, Checks c, Scenario s)
    {
        // The index table is emptied behind the module's back, as after a restore or a rebuilt index.
        await site.ScopeAsync(async sp =>
        {
            var store = sp.GetRequiredService<IStore>();
            var configuration = store.Configuration;
            var table = configuration.SqlDialect.QuoteForTableName(
                configuration.TablePrefix + configuration.TableNameConvention.GetIndexTable(typeof(AutoBlogPostIndex)), configuration.Schema);
            await using var connection = configuration.ConnectionFactory.CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table}";
            await command.ExecuteNonQueryAsync();
        });
        c.Equal(0, (await site.IndexedGuidsAsync()).Count, "The GUID index starts the recovery check empty");

        var result = await site.ImportAsync();
        c.That(result.CreatedPosts.Count == 0 && result.ErrorCount == 0, "An emptied GUID index does not duplicate installed posts",
            $"{result.CreatedPosts.Count} created");
        c.Equal(3, (await site.PostsAsync()).Count, "The installed posts are still the only posts");
        c.Equal(3, (await site.IndexedGuidsAsync()).Count, "The import rebuilds the GUID index");

        // A post an editor created by hand, with no vendor GUID, is adopted through its route instead of duplicated.
        var manualId = await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var item = await content.NewAsync(Constants.ContentType);
            item.DisplayText = "Manual entry";
            item.Alter<TitlePart>(p => p.Title = "Manual entry");
            item.Alter<HtmlBodyPart>(p => p.Html = "<p>Written by hand</p>");
            item.Weld<ContainedPart>();
            item.Alter<ContainedPart>(p => p.ListContentItemId = s.BlogContentItemId);
            await content.CreateAsync(item, VersionOptions.Draft);
            return item.ContentItemId;
        });
        site.Edge.Articles.Add(s.Manual);
        result = await site.ImportAsync();
        c.That(result.CreatedPosts.Count == 0, "A hand-made post on the same route is not duplicated", $"{result.CreatedPosts.Count} created");
        var adopted = await site.PostAsync(s.Manual);
        c.Equal(manualId, adopted?.ContentItemId, "The vendor GUID is backfilled onto the existing post");
        c.Equal(4, (await site.PostsAsync()).Count, "Adopting a post adds no content item");
        c.Equal(4, (await site.IndexedGuidsAsync()).Count, "The adopted post is added to the GUID index");

        // A different vendor article whose slug equals an existing post's route must not take that post over.
        var owner = (await site.PostAsync(s.NoImage))!;
        site.Edge.Articles.Add(s.RouteCollision);
        result = await site.ImportAsync();
        c.That(result.CreatedPosts.Count == 1, "An article that only shares a route with another article is imported as its own post", $"{result.CreatedPosts.Count} created");
        var ownerNow = await site.PostAsync(s.NoImage);
        c.That(ownerNow?.ContentItemId == owner.ContentItemId && ownerNow.DisplayText == "No image post"
            && ownerNow.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html == "<p>Vendor body C</p>", "The post that owns the route keeps its GUID and content");
        var later = await site.PostAsync(s.RouteCollision);
        c.That(later != null && later.ContentItemId != owner.ContentItemId
            && later.Get<AutoroutePart>(nameof(AutoroutePart))?.Path != ownerNow?.Get<AutoroutePart>(nameof(AutoroutePart))?.Path, "The later article gets its own content item and route");
        c.Equal(5, (await site.PostsAsync()).Count, "The collision adds exactly one content item");
        site.Email.Clear();
    }

    public static async Task PublishingAsync(FixtureHost site, Checks c, Scenario s)
    {
        var edge = site.Edge;

        // Directly Publish off: a vendor-scheduled article is an ordinary draft the editor controls.
        edge.Articles.Clear();
        edge.Articles.Add(s.FutureDraft);
        var result = await site.ImportAsync();
        var futureDraft = (await site.PostAsync(s.FutureDraft))!;
        c.That(result.CreatedPosts.SingleOrDefault()?.Status == "Draft" && !futureDraft.Published, "A future article is a plain draft while Directly Publish is off");
        c.That(futureDraft.Get<PublishLaterPart>(nameof(PublishLaterPart))?.ScheduledPublishUtc is null, "A draft import is not scheduled for publication");

        await site.ConfigureAsync(x => x.DirectlyPublish = true);
        site.Email.Clear();
        edge.Articles.Clear();
        edge.Articles.AddRange([s.Published, s.PublishedPlain, s.Scheduled]);
        result = await site.ImportAsync();
        c.That(result.CreatedPosts.Count == 3 && result.ErrorCount == 0, "Directly Publish imports every article", result.ErrorMessage);

        var published = (await site.PostAsync(s.Published))!;
        c.That(published.Published && published.Latest, "Directly Publish makes a current article live");
        c.Equal(new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc), published.PublishedUtc, "The published date is the vendor's publication date");
        c.Equal("Published", result.CreatedPosts.First(p => p.ContentItemId == published.ContentItemId).Status, "The run reports the post as published");
        c.Contains(published.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html ?? string.Empty, "{{ Site.SiteName }}", "Liquid-looking vendor text is stored as plain text");
        c.That(site.Email.Messages.Count == 2 && site.Email.Messages.All(m => m.Subject == "AutoBlog — new blog post(s)"), "Published imports use the published notification subject");

        var scheduled = (await site.PostAsync(s.Scheduled))!;
        var scheduledFor = new DateTime(2031, 1, 15, 9, 0, 0, DateTimeKind.Utc);
        c.That(!scheduled.Published, "A future article is not published early");
        c.Equal((DateTime?)scheduledFor, scheduled.Get<PublishLaterPart>(nameof(PublishLaterPart))?.ScheduledPublishUtc, "A future article is scheduled through PublishLaterPart");
        c.Equal("Scheduled", result.CreatedPosts.First(p => p.ContentItemId == scheduled.ContentItemId).Status, "The run reports the post as scheduled");

        using (var visitor = site.CreateBrowser())
        {
            c.That((await visitor.GetAsync("/blog/patch-tuesday")).IsOk, "A published article is served at its blog route");
            c.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/blog/scheduled-post")).Status, "A scheduled article is not public before its date");
        }

        // When the scheduled moment has passed, Orchard's own publish-later task makes the post live.
        await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var item = await content.GetAsync(scheduled.ContentItemId, VersionOptions.Latest);
            item.Alter<PublishLaterPart>(p => p.ScheduledPublishUtc = DateTime.UtcNow.AddMinutes(-5));
            await content.UpdateAsync(item);
        });
        await site.ScopeAsync(async sp =>
        {
            var task = sp.GetServices<IBackgroundTask>().Single(t => t.GetType().FullName!.Contains("PublishLater", StringComparison.Ordinal));
            await task.DoWorkAsync(sp, CancellationToken.None);
        });
        c.That((await site.PostAsync(s.Scheduled))!.Published, "Orchard's publish-later task publishes the scheduled article");
        using (var visitor = site.CreateBrowser())
            c.That((await visitor.GetAsync("/blog/scheduled-post")).IsOk, "The article is public once its schedule has run");

        // With Directly Publish on, an update publishes the article it updates.
        edge.Articles.Clear();
        s.Roundup.UpdateVersion = 3;
        s.Roundup.Title = "Weekly security roundup";
        edge.Articles.Add(s.Roundup);
        result = await site.ImportAsync();
        var republished = (await site.PostAsync(s.Roundup))!;
        c.That(result.UpdatedPosts.SingleOrDefault()?.Status == "Published" && republished.Published, "Directly Publish publishes an updated article");
        c.Equal("blog/weekly-security-roundup-tips", republished.Get<AutoroutePart>(nameof(AutoroutePart))?.Path, "Publishing an update keeps the article URL");

        // A Blog + Video feed marks its posts so templates can leave the image to the embedded video.
        await site.ConfigureAsync(x => x.FeedId = 6);
        edge.ClearRequests();
        edge.Articles.Clear();
        edge.Articles.Add(s.Video);
        result = await site.ImportAsync();
        c.Contains(edge.Requests.First(r => r.Host == FakeEdgeServer.FeedHost).Query, "categories=6", "The selected feed is the one requested");
        c.That((await site.PostAsync(s.Video))!.Get<AutoBlogPart>(nameof(AutoBlogPart))!.EdgeHasVideo, "Posts from a video feed are marked as video posts");
        c.That(!(await site.PostAsync(s.Published))!.Get<AutoBlogPart>(nameof(AutoBlogPart))!.EdgeHasVideo, "Posts from a blog-only feed are not");
        await site.ConfigureAsync(x => x.FeedId = 8);

        // One article that cannot be stored must not stop the rest of the run.
        var errorsBefore = site.Errors.Count;
        site.ImageFault.FailForSlug = s.Broken.Slug;
        edge.Articles.Clear();
        edge.Articles.AddRange([s.Broken, s.AfterFailure]);
        result = await site.ImportAsync();
        site.ImageFault.FailForSlug = null;
        c.That(result.ErrorCount == 1 && result.CreatedPosts.Count == 1, "A failing article is counted and the run continues");
        c.That(await site.PostAsync(s.Broken) is null && await site.PostAsync(s.AfterFailure) is not null, "Only the failing article is missing");
        var status = await site.SettingsAsync();
        c.Equal("1 created, 0 updated, 0 skipped, 1 failed", status.LastSyncSummary, "The sync status counts the failure");
        c.That(!status.LastSyncSuccess && status.LastSyncError?.Contains("Broken article", StringComparison.Ordinal) == true, "The sync status names the failing article", status.LastSyncError);
        c.Equal(errorsBefore + 1, site.Errors.Count, "The failure is logged once as an error");
        c.That(site.Errors.Entries.LastOrDefault()?.Contains("Broken article", StringComparison.Ordinal) == true, "The logged error names the failing article");
        site.ExpectedErrors++;

        // The scheduled path: the background task runs the same import.
        edge.Articles.Clear();
        edge.Articles.Add(s.FromBackgroundTask);
        await site.ScopeAsync(async sp =>
        {
            var task = sp.GetServices<IBackgroundTask>().OfType<AutoBlogBackgroundTask>().Single();
            await task.DoWorkAsync(sp, CancellationToken.None);
        });
        c.That(await site.PostAsync(s.FromBackgroundTask) is not null, "The background task imports through the same service");
        c.That((await site.SettingsAsync()).LastSyncSuccess, "A later clean run clears the failed status");

        var guids = await site.IndexedGuidsAsync();
        c.Equal(12, (await site.PostsAsync()).Count, "Every imported article exists exactly once");
        c.That(guids.Count == 12 && guids.Distinct().Count() == 12, "Every post has exactly one GUID index row", $"{guids.Count} rows, {guids.Distinct().Count()} distinct");
        site.Email.Clear();
        edge.Articles.Clear();
    }
}
