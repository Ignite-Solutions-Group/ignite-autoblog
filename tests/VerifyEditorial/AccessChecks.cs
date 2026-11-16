using System.Net;
using System.Text.RegularExpressions;
using IgniteSG.OrchardCore.MspMarketingEdge;
using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;

/// <summary>
/// Who can see, open, save and refresh the AutoBlog settings. Three signed-in users and an anonymous visitor
/// go through the real login form, admin routes and antiforgery validation.
/// </summary>
static class AccessChecks
{
    private const string SettingsPath = "/Admin/Settings/AutoBlog";

    private const string Token = "__RequestVerificationToken";

    public static async Task RunAsync(FixtureHost site, Checks c, Scenario s)
    {
        var edge = site.Edge;
        edge.Articles.Clear();

        using var admin = site.CreateBrowser();
        using var manager = site.CreateBrowser();
        using var visitor = site.CreateBrowser();
        using var anonymous = site.CreateBrowser();
        c.That(await admin.SignInAsync(FixtureHost.AdminUser, site.AdminPassword), "The administrator can sign in");
        c.That(await manager.SignInAsync(Scenario.ManagerUser, s.UserPassword), "The AutoBlog manager can sign in");
        c.That(await visitor.SignInAsync(Scenario.VisitorUser, s.UserPassword), "The user without the permission can sign in");

        // Opening the settings.
        var adminPage = await admin.GetAsync(SettingsPath);
        var managerPage = await manager.GetAsync(SettingsPath);
        c.That(adminPage.IsOk && HasSettingsForm(adminPage.Body), "An administrator can open the AutoBlog settings", $"{(int)adminPage.Status}");
        c.That(managerPage.IsOk && HasSettingsForm(managerPage.Body), "A role holding only ManageAutoBlogSettings can open the AutoBlog settings", $"{(int)managerPage.Status}");
        var visitorPage = await visitor.GetAsync(SettingsPath);
        c.That(visitorPage.IsDenied || visitorPage.Status == HttpStatusCode.NotFound, "A user without the permission cannot open the AutoBlog settings", $"{(int)visitorPage.Status} {visitorPage.Location}");
        c.That(!HasSettingsForm(visitorPage.Body), "A user without the permission is shown no settings fields");
        c.That((await anonymous.GetAsync(SettingsPath)).IsDenied, "An anonymous visitor is sent away from the AutoBlog settings");

        c.Contains(managerPage.Body, "Run Import Now", "The settings page offers the manual import");
        c.Contains(managerPage.Body, "Sync Status", "The settings page shows the sync status");
        c.Contains(managerPage.Body, "v" + Constants.Version, "The settings page shows the module version");

        // The narrow permission must not open any other settings group.
        c.That((await admin.GetAsync("/Admin/Settings/general")).IsOk, "An administrator can open the general settings");
        var managerGeneral = await manager.GetAsync("/Admin/Settings/general");
        c.That(managerGeneral.IsDenied || managerGeneral.Status == HttpStatusCode.NotFound, "ManageAutoBlogSettings does not open the general settings", $"{(int)managerGeneral.Status}");

        // Menu visibility.
        var link = $"href=\"{SettingsPath}\"";
        c.Contains((await admin.GetAsync("/Admin")).Body, link, "The administrator's menu links to the AutoBlog settings");
        c.Contains((await manager.GetAsync("/Admin")).Body, link, "The AutoBlog manager's menu links to the AutoBlog settings");
        var visitorAdmin = await visitor.GetAsync("/Admin");
        c.That(visitorAdmin.IsOk, "The user without the permission can still reach the admin", $"{(int)visitorAdmin.Status}");
        c.Lacks(visitorAdmin.Body, link, "The menu hides the AutoBlog settings from a user without the permission");

        // Saving.
        var before = await site.SettingsAsync();
        var form = Browser.FormFields(managerPage.Body);
        Set(form, "PublisherName", "Saved by the manager");
        var saved = await manager.PostAsync(SettingsPath, form);
        var after = await site.SettingsAsync();
        c.That(saved.Status == HttpStatusCode.Redirect && !saved.IsDenied, "The AutoBlog manager can save the settings", $"{(int)saved.Status} {saved.Location}");
        c.Equal("Saved by the manager", after.PublisherName, "The saved value is stored");
        c.That(after.LicensedEmail == before.LicensedEmail && after.FeedId == before.FeedId && after.DirectlyPublish == before.DirectlyPublish
            && after.NotificationEmail == before.NotificationEmail && after.TwitterHandle == before.TwitterHandle
            && after.EnableGoogleStructuredData == before.EnableGoogleStructuredData && after.BlogContainerContentItemId == before.BlogContainerContentItemId,
            "Saving round-trips every other setting unchanged");
        c.That(after.LastSyncUtc == before.LastSyncUtc && after.LastSyncSummary == before.LastSyncSummary && after.LastSyncSuccess == before.LastSyncSuccess,
            "Saving does not overwrite the sync status");

        var withoutToken = Browser.FormFields((await manager.GetAsync(SettingsPath)).Body);
        Set(withoutToken, "PublisherName", "Saved without a token");
        withoutToken.Remove(Token);
        c.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync(SettingsPath, withoutToken)).Status, "Saving without an antiforgery token is rejected");

        var visitorForm = Browser.FormFields((await manager.GetAsync(SettingsPath)).Body);
        Set(visitorForm, "PublisherName", "Saved by the visitor");
        visitorForm[Token] = await TokenAsync(visitor, "/Admin");
        var visitorSave = await visitor.PostAsync(SettingsPath, visitorForm);
        c.That(visitorSave.IsDenied || visitorSave.Status == HttpStatusCode.NotFound, "A user without the permission cannot save the settings", $"{(int)visitorSave.Status}");
        c.Equal("Saved by the manager", (await site.SettingsAsync()).PublisherName, "Rejected saves change nothing");
        await site.ConfigureAsync(x => x.PublisherName = "Fixture Publisher");

        // Manual import, posted to the address the page's own button uses.
        var refreshPath = Regex.Match(managerPage.Body, @"form\.action\s*=\s*'(?<url>[^']+)'").Groups["url"].Value;
        c.Equal("/admin/autoblog/force-refresh", refreshPath, "The Run Import Now button posts to the manual import route");
        edge.ClearRequests();
        var refresh = await manager.PostAsync(refreshPath, [new(Token, await TokenAsync(manager, SettingsPath))]);
        c.That(refresh.Status == HttpStatusCode.Redirect && refresh.Location?.Contains(SettingsPath, StringComparison.OrdinalIgnoreCase) == true,
            "The AutoBlog manager can run the import and returns to the settings", $"{(int)refresh.Status} {refresh.Location}");
        c.Equal(1, edge.Count(FakeEdgeServer.LicenseHost), "The manual import contacts the vendor once");
        c.Contains((await manager.GetAsync(SettingsPath)).Body, "no new or updated posts were found", "The manual import reports its result on the settings page");

        edge.ClearRequests();
        refresh = await admin.PostAsync(refreshPath, [new(Token, await TokenAsync(admin, SettingsPath))]);
        c.That(refresh.Status == HttpStatusCode.Redirect && edge.Count(FakeEdgeServer.LicenseHost) == 1, "An administrator can run the import");

        edge.ClearRequests();
        c.Equal(HttpStatusCode.BadRequest, (await manager.PostAsync(refreshPath, [])).Status, "A manual import without an antiforgery token is rejected");
        var visitorRefresh = await visitor.PostAsync(refreshPath, [new(Token, await TokenAsync(visitor, "/Admin"))]);
        c.That(visitorRefresh.IsDenied, "A user without the permission cannot run the import", $"{(int)visitorRefresh.Status} {visitorRefresh.Location}");
        var anonymousRefresh = await anonymous.PostAsync(refreshPath, [new(Token, await TokenAsync(anonymous, "/Login"))]);
        c.That(anonymousRefresh.IsDenied, "An anonymous visitor cannot run the import", $"{(int)anonymousRefresh.Status} {anonymousRefresh.Location}");
        var asLink = await manager.GetAsync(refreshPath);
        c.That(asLink.Status is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, "The manual import cannot be triggered by a link", $"{(int)asLink.Status}");
        c.Equal(0, edge.Requests.Count, "No rejected attempt reaches the vendor");
    }

    /// <summary>The link in the notification email must open the settings page for someone allowed to use it.</summary>
    public static async Task NotificationLinkAsync(FixtureHost site, Checks c, string emailBody)
    {
        var href = Regex.Match(emailBody, "href=\"(?<url>[^\"]+)\"").Groups["url"].Value;
        c.That(href.StartsWith(FixtureHost.SiteBaseUrl + "/", StringComparison.Ordinal), "The notification links to the site's own address", href);
        if (!Uri.TryCreate(WebUtility.HtmlDecode(href), UriKind.Absolute, out var url)) return;

        using var admin = site.CreateBrowser();
        await admin.SignInAsync(FixtureHost.AdminUser, site.AdminPassword);
        var page = await admin.GetAsync(url.PathAndQuery);
        c.That(page.IsOk && HasSettingsForm(page.Body), "The notification's settings link opens the AutoBlog settings", $"{url.PathAndQuery} answered {(int)page.Status}");
    }

    private static bool HasSettingsForm(string html) =>
        Browser.FormFields(html).Keys.Any(k => k.EndsWith("LicensedEmail", StringComparison.Ordinal));

    private static void Set(Dictionary<string, string> form, string field, string value)
    {
        var key = form.Keys.FirstOrDefault(k => k == field || k.EndsWith("." + field, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The settings form has no {field} field.");
        form[key] = value;
    }

    private static async Task<string> TokenAsync(Browser browser, string path)
    {
        var page = await browser.GetAsync(path);
        return Browser.FormFields(page.Body).TryGetValue(Token, out var token)
            ? token
            : throw new InvalidOperationException($"{path} ({(int)page.Status}) carries no antiforgery token.");
    }
}
