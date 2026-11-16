using System.Text.RegularExpressions;
using IgniteSG.OrchardCore.MspMarketingEdge.BackgroundTasks;
using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using OrchardCore.Autoroute.Models;
using OrchardCore.BackgroundTasks;
using OrchardCore.ContentManagement;
using OrchardCore.Html.Models;
using OrchardCore.Lists.Models;
using OrchardCore.PublishLater.Models;

/// <summary>
/// Opens a database written by the released module on Orchard Core 3 and checks that the current build finds
/// everything where it was left, then keeps working on it without duplicating or rewriting installed content.
/// </summary>
static partial class UpgradeChecks
{
    public static async Task StoredStateAsync(FixtureHost site, Checks c, SeedManifest before)
    {
        Console.WriteLine($"   Seeded by module {before.ModuleVersion} on Orchard Core {before.OrchardVersion}: {before.Posts.Count} posts.");

        c.Equal((int?)before.MigrationVersion, await ImportChecks.MigrationVersionAsync(site), "The migration record carries over at the same version");
        c.Equal(4, before.MigrationVersion, "The seeded site had completed the migration chain");

        var settings = SeedSettings.From(await site.SettingsAsync()).DifferencesFrom(before.Settings);
        c.That(settings.Count == 0, "Every stored setting reads back unchanged", string.Join(", ", settings));

        var posts = await site.PostsAsync();
        c.Equal(before.Posts.Count, posts.Count, "The site holds the same number of posts");
        foreach (var expected in before.Posts)
        {
            var actual = posts.SingleOrDefault(p => p.ContentItemId == expected.ContentItemId);
            c.That(actual != null, $"'{expected.DisplayText}' keeps its content item id");
            if (actual is null) continue;
            var differences = SeedPost.From(actual).DifferencesFrom(expected);
            c.That(differences.Count == 0, $"'{expected.DisplayText}' is stored exactly as Orchard Core 3 left it", string.Join(", ", differences));
        }

        c.That((await site.IndexedGuidsAsync()).Order().SequenceEqual(before.IndexedGuids), "The GUID index holds the same rows");

        var blog = await site.ScopeAsync(sp => sp.GetRequiredService<IContentManager>().GetAsync(before.BlogContentItemId));
        c.That(blog?.Published == true && blog.Get<AutoroutePart>(nameof(AutoroutePart))?.Path == "blog", "The blog list the posts belong to is intact");
    }

    public static async Task AccessAsync(FixtureHost site, Checks c, SeedManifest before)
    {
        // Accounts and roles created on Orchard Core 3, including the role that holds only the AutoBlog permission.
        using var admin = site.CreateBrowser();
        using var manager = site.CreateBrowser();
        using var visitor = site.CreateBrowser();
        c.That(await admin.SignInAsync(FixtureHost.AdminUser, before.AdminPassword), "The existing administrator can sign in");
        c.That(await manager.SignInAsync(Scenario.ManagerUser, before.UserPassword), "The existing AutoBlog manager can sign in");
        c.That(await visitor.SignInAsync(Scenario.VisitorUser, before.UserPassword), "The existing user without the permission can sign in");

        var managerPage = await manager.GetAsync("/Admin/Settings/AutoBlog");
        c.That(managerPage.IsOk && Browser.FormFields(managerPage.Body).Keys.Any(k => k.EndsWith("LicensedEmail", StringComparison.Ordinal)),
            "A role granted ManageAutoBlogSettings before the upgrade still opens the settings", $"{(int)managerPage.Status} {managerPage.Location}");
        c.Contains(managerPage.Body, before.Settings.LastSyncSummary, "The settings page shows the sync status recorded before the upgrade");
        c.That((await admin.GetAsync("/Admin/Settings/AutoBlog")).IsOk, "The administrator still opens the settings");
        c.That((await visitor.GetAsync("/Admin/Settings/AutoBlog")).IsDenied, "A user without the permission is still refused");
    }

    public static async Task RenderingAsync(FixtureHost site, Checks c, SeedManifest before)
    {
        using var visitor = site.CreateBrowser();

        var live = await visitor.GetAsync("/blog/seeded-live");
        c.That(live.IsOk, "A post published before the upgrade is served at the same URL", $"{(int)live.Status}");
        c.Contains(live.Body, "<p>Seeded live body</p>", "Its stored body renders");
        c.Equal(Normalize(TimeElement().Match(before.Pages["/blog/seeded-live"]).Value), Normalize(TimeElement().Match(live.Body).Value),
            "The publication date renders exactly as it did on Orchard Core 3");

        var imageBefore = FeaturedImage().Match(before.Pages["/blog/seeded-live"]).Groups["src"].Value;
        var imageNow = FeaturedImage().Match(live.Body).Groups["src"].Value;
        c.That(imageNow.Length > 0 && imageNow == imageBefore, "The featured image keeps the same public URL", $"before '{imageBefore}', now '{imageNow}'");
        if (imageNow.Length > 0)
        {
            var file = await visitor.GetAsync(imageNow);
            c.That(file.IsOk && file.Bytes.AsSpan().SequenceEqual(FakeEdgeServer.PngBytes), "An image stored before the upgrade is still served", $"{(int)file.Status}");
        }
        c.Lacks(live.Body, "liquid-ran", "Vendor text is not executed as a template");
        c.Contains(live.Body, "{{ Site.SiteName }}", "Liquid-looking vendor text is shown as written");

        var video = await visitor.GetAsync("/blog/seeded-video");
        c.That(video.IsOk, "A video post published before the upgrade is served", $"{(int)video.Status}");
        c.Lacks(video.Body, "autoblog-featured-image", "A video post leaves its featured image to the embedded video");

        var list = await visitor.GetAsync("/blog");
        c.That(list.IsOk && list.Body.Contains("href=\"/blog/seeded-live\"", StringComparison.Ordinal) && list.Body.Contains("href=\"/blog/seeded-video\"", StringComparison.Ordinal),
            "The blog list still shows the published posts");
        c.Lacks(list.Body, "href=\"/blog/seeded-scheduled\"", "A post scheduled before the upgrade is still not public");

        // Not assertions: how the released templates behaved, for the record of what changed.
        Console.WriteLine($"   Orchard Core 3 executed Liquid found in vendor HTML: {before.Pages["/blog/seeded-live"].Contains("liquid-ran", StringComparison.Ordinal)}");
        Console.WriteLine($"   Orchard Core 3 showed the featured image on a video post: {before.Pages["/blog/seeded-video"].Contains("autoblog-featured-image", StringComparison.Ordinal)}");
        foreach (var observation in before.Observations) Console.WriteLine($"   Orchard Core 3 observation {observation.Key}: {observation.Value}");
    }

    public static async Task ContinuedImportsAsync(FixtureHost site, Checks c, SeedManifest before)
    {
        var edge = site.Edge;
        edge.ParentLinks[9002] = "https://www.yourtechupdates.com/seeded-parent/";
        edge.Articles.AddRange(before.Articles);
        var count = before.Posts.Count;

        // The same rolling feed arrives again after the upgrade.
        var result = await site.ImportAsync();
        c.That(result.CreatedPosts.Count == 0 && result.UpdatedPosts.Count == 0 && result.ErrorCount == 0, "The first import after the upgrade changes nothing",
            $"{result.CreatedPosts.Count} created, {result.UpdatedPosts.Count} updated, {result.ErrorCount} failed: {result.ErrorMessage}");
        c.Equal(count, (await site.PostsAsync()).Count, "The first import after the upgrade duplicates nothing");
        c.Equal(0, site.Email.Messages.Count, "An unchanged feed sends no notification");

        // A vendor update to a post imported before the upgrade.
        var draft = before.Articles.Single(a => a.Slug == "seeded-draft");
        var edited = before.Articles.Single(a => a.Slug == "seeded-edited");
        var draftBefore = before.Posts.Single(p => p.Guid == draft.Guid);
        var editedBefore = before.Posts.Single(p => p.Guid == edited.Guid);
        draft.Title = "Seeded draft, revised";
        draft.Content = "<p>Revised after the upgrade</p>";
        draft.UpdateVersion = 2;
        edited.Content = "<p>Vendor body that must wait</p>";
        edited.UpdateVersion = 2;

        result = await site.ImportAsync();
        c.That(result.UpdatedPosts.Count == 1 && result.CreatedPosts.Count == 0, "A vendor update reaches the post imported before the upgrade",
            $"{result.UpdatedPosts.Count} updated, {result.CreatedPosts.Count} created");
        var draftNow = (await site.PostAsync(draft))!;
        c.Equal(draftBefore.ContentItemId, draftNow.ContentItemId, "The update is applied to the same content item");
        c.Equal(draftBefore.Path, draftNow.Get<AutoroutePart>(nameof(AutoroutePart))?.Path, "The updated post keeps its URL");
        c.That(draftNow.DisplayText == "Seeded draft, revised" && draftNow.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html == "<p>Revised after the upgrade</p>",
            "The update replaces title and body");
        c.Equal(draftBefore.Canonical, SeedPost.From(draftNow).Canonical, "The updated post keeps its canonical URL");
        c.Equal(draftBefore.CreatedUtc, draftNow.CreatedUtc, "The updated post keeps its original publication date");

        var editedNow = (await site.PostAsync(edited))!;
        c.That(SeedPost.From(editedNow).DifferencesFrom(editedBefore).Count == 0, "A post protected before the upgrade is still left alone",
            string.Join(", ", SeedPost.From(editedNow).DifferencesFrom(editedBefore)));

        await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var item = await content.GetAsync(editedNow.ContentItemId, VersionOptions.Latest);
            item.Alter<AutoBlogPart>(p => p.PreserveLocalEdits = false);
            await content.UpdateAsync(item);
        });
        result = await site.ImportAsync();
        editedNow = (await site.PostAsync(edited))!;
        c.That(result.UpdatedPosts.Count == 1 && editedNow.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html == "<p>Vendor body that must wait</p>"
            && editedNow.ContentItemId == editedBefore.ContentItemId, "Clearing protection applies the update that was pending across the upgrade");
        c.Equal(editedBefore.Canonical, SeedPost.From(editedNow).Canonical, "The parent-resolved canonical URL survives the update");

        // A new article joins the same blog.
        var added = new FakeArticle
        {
            Id = 301, Slug = "after-the-upgrade", Title = "After the upgrade", Content = "<p>Imported on the current build</p>",
            Excerpt = "After the upgrade excerpt", Date = "2026-11-10T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("after.png"),
        };
        edge.Articles.Add(added);
        result = await site.ImportAsync();
        var addedNow = await site.PostAsync(added);
        c.That(result.CreatedPosts.Count == 1 && addedNow?.Published == true, "A new article imports and publishes under the stored settings");
        c.Equal(before.BlogContentItemId, addedNow?.Get<ContainedPart>(nameof(ContainedPart))?.ListContentItemId, "The new post joins the existing blog list");
        c.Equal(count + 1, (await site.PostsAsync()).Count, "Only the new article was added");
        c.Equal(before.IndexedGuids.Count + 1, (await site.IndexedGuidsAsync()).Distinct().Count(), "The GUID index gains exactly one row");
        using (var visitor = site.CreateBrowser())
            c.That((await visitor.GetAsync("/blog/after-the-upgrade")).IsOk, "The new post is served under the blog");
        c.That(site.Email.Messages.Count > 0 && site.Email.Messages.All(m => m.To == before.Settings.NotificationEmail), "Notifications go to the address stored before the upgrade");

        // The schedule set before the upgrade is still honoured by Orchard's publish-later task.
        var scheduled = before.Articles.Single(a => a.Slug == "seeded-scheduled");
        var scheduledBefore = before.Posts.Single(p => p.Guid == scheduled.Guid);
        c.That(scheduledBefore.ScheduledPublishUtc.HasValue && !scheduledBefore.Published, "The seeded site held a scheduled, unpublished post");
        await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var item = await content.GetAsync(scheduledBefore.ContentItemId, VersionOptions.Latest);
            item.Alter<PublishLaterPart>(p => p.ScheduledPublishUtc = DateTime.UtcNow.AddMinutes(-5));
            await content.UpdateAsync(item);
        });
        await site.ScopeAsync(async sp =>
        {
            var task = sp.GetServices<IBackgroundTask>().Single(t => t.GetType().FullName!.Contains("PublishLater", StringComparison.Ordinal));
            await task.DoWorkAsync(sp, CancellationToken.None);
        });
        c.That((await site.PostAsync(scheduled))!.Published, "A post scheduled before the upgrade publishes when its time comes");

        // The module's own schedule still runs the import.
        var later = new FakeArticle
        {
            Id = 302, Slug = "from-the-schedule", Title = "From the schedule", Content = "<p>Imported by the background task</p>",
            Excerpt = "From the schedule excerpt", Date = "2026-11-11T08:30:00Z",
        };
        edge.Articles.Add(later);
        await site.ScopeAsync(async sp =>
        {
            var task = sp.GetServices<IBackgroundTask>().OfType<AutoBlogBackgroundTask>().Single();
            await task.DoWorkAsync(sp, CancellationToken.None);
        });
        c.That(await site.PostAsync(later) is not null, "The background task imports on the upgraded site");
    }

    private static string Normalize(string html) => Regex.Replace(html, "\\s+", " ").Trim();

    [GeneratedRegex("<time\\b.*?</time>", RegexOptions.Singleline)]
    private static partial Regex TimeElement();
    [GeneratedRegex("<div class=\"autoblog-featured-image[^\"]*\">\\s*<img src=\"(?<src>[^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex FeaturedImage();
}
