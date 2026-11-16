using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;
using Microsoft.AspNetCore.Identity;
using OrchardCore.Autoroute.Models;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Lists.Models;
using OrchardCore.Settings;
using OrchardCore.Title.Models;
using OrchardCore.Users;
using OrchardCore.Users.Models;
using OrchardCore.Users.Services;

/// <summary>The disposable site content and the vendor articles every group of checks refers to.</summary>
sealed class Scenario
{
    public const string LicensedEmail = "licensed+fixture@example.invalid";
    public const string NotificationEmails = "editor@example.invalid, second@example.invalid";
    public const string ManagerUser = "blogmanager";
    public const string VisitorUser = "visitor";
    public const string BlogPath = "blog";

    public string BlogContentItemId { get; set; } = string.Empty;
    public string UserPassword { get; } = "LocalOnly7!" + Guid.NewGuid().ToString("N");
    /// <summary>The body of the first notification, kept so its link can be followed later.</summary>
    public string NotificationBody { get; set; } = string.Empty;

    // Imported as drafts.
    public FakeArticle Roundup { get; } = new()
    {
        Id = 101, Slug = "weekly-security-roundup", Title = "Weekly security roundup &amp; tips",
        Content = "<p>Vendor body A</p><script>alert('x')</script><p><img src=\"https://images.fixture.invalid/inline.png\" onerror=\"alert(1)\"></p>",
        Excerpt = "<p>Excerpt A with <strong>markup</strong>.</p>",
        Date = "2026-10-01T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("a.png"), WeekCommencing = "2026-09-28",
    };
    public FakeArticle Passwords { get; } = new()
    {
        Id = 102, Slug = "password-habits", Title = "Password habits", Content = "<p>Vendor body B</p>",
        Excerpt = "<p>" + string.Join(' ', Enumerable.Repeat("Long excerpt text.", 30)) + "</p>",
        Date = "2026-10-08T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("missing.png"), ParentPageUrl = 9001,
    };
    public FakeArticle NoImage { get; } = new()
    {
        Id = 103, Slug = "no-image-post", Title = "No image post", Content = "<p>Vendor body C</p>",
        Excerpt = "Excerpt C", Date = "2026-10-09T08:30:00Z", SendUpdate = false,
    };
    public FakeArticle Manual { get; } = new()
    {
        Id = 110, Slug = "manual-entry", Title = "Manual entry", Content = "<p>Vendor body for the manual entry</p>",
        Excerpt = "Excerpt M", Date = "2026-10-10T08:30:00Z",
    };
    /// <summary>A different vendor article whose slug equals the route of the "No image post" article.</summary>
    public FakeArticle RouteCollision { get; } = new()
    {
        Id = 120, Slug = "no-image-post", Title = "A later article on the same slug", Content = "<p>Vendor body for the later article</p>",
        Excerpt = "Excerpt for the later article", Date = "2026-10-11T08:30:00Z",
    };
    public FakeArticle FutureDraft { get; } = new()
    {
        Id = 107, Slug = "future-draft", Title = "Future draft", Content = "<p>Vendor body F</p>",
        Excerpt = "Excerpt F", Date = "2031-02-01T09:00:00Z", Status = "future",
    };

    // Imported with Directly Publish on.
    public FakeArticle Published { get; } = new()
    {
        Id = 104, Slug = "patch-tuesday", Title = "Patch Tuesday",
        Content = "<p>Vendor body D</p><p>{{ Site.SiteName }} and {{ 'liquid' | append: '-ran' }}</p>",
        Excerpt = "Excerpt D", Date = "2026-10-15T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("d.png"),
    };
    public FakeArticle PublishedPlain { get; } = new()
    {
        Id = 105, Slug = "plain-update", Title = "Plain update", Content = "<p>Vendor body N</p>",
        Excerpt = "Excerpt N", Date = "2026-10-16T08:30:00Z",
    };
    public FakeArticle Scheduled { get; } = new()
    {
        Id = 106, Slug = "scheduled-post", Title = "Scheduled post", Content = "<p>Vendor body E</p>",
        Excerpt = "Excerpt E", Date = "2031-01-15T09:00:00Z", Status = "future", ImageUrl = FakeEdgeServer.ImageUrl("e.png"),
    };
    public FakeArticle Video { get; } = new()
    {
        Id = 108, Slug = "video-briefing", Title = "Video briefing", Content = "<p>Vendor body V with an embedded video</p>",
        Excerpt = "Excerpt V", Date = "2026-10-17T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("v.png"),
    };
    public FakeArticle Broken { get; } = new()
    {
        Id = 111, Slug = "broken-article", Title = "Broken article", Content = "<p>Vendor body X</p>",
        Excerpt = "Excerpt X", Date = "2026-10-18T08:30:00Z", ImageUrl = FakeEdgeServer.ImageUrl("x.png"),
    };
    public FakeArticle AfterFailure { get; } = new()
    {
        Id = 112, Slug = "after-the-failure", Title = "After the failure", Content = "<p>Vendor body G</p>",
        Excerpt = "Excerpt G", Date = "2026-10-19T08:30:00Z",
    };
    public FakeArticle FromBackgroundTask { get; } = new()
    {
        Id = 113, Slug = "from-background-task", Title = "From background task", Content = "<p>Vendor body H</p>",
        Excerpt = "Excerpt H", Date = "2026-10-20T08:30:00Z",
    };

    /// <summary>Creates a blog list for imported posts to join, the way a site with a Blog would have one.</summary>
    public async Task CreateBlogAsync(FixtureHost site)
    {
        await site.ScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IContentDefinitionManager>().AlterTypeDefinitionAsync("Blog", type => type
                .WithDisplayName("Blog")
                .Creatable()
                .Listable()
                .Draftable()
                .WithPart("TitlePart", part => part.WithPosition("0"))
                .WithPart("AutoroutePart", part => part
                    .WithPosition("1")
                    .WithSettings(new AutoroutePartSettings
                    {
                        AllowCustomPath = true,
                        Pattern = "{{ Model.ContentItem | display_text | slugify }}",
                    }))
                .WithPart("ListPart", part => part
                    .WithPosition("2")
                    .WithSettings(new ListPartSettings { PageSize = 20, ContainedContentTypes = ["AutoBlogPost"] })));
        });

        BlogContentItemId = await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var blog = await content.NewAsync("Blog");
            blog.DisplayText = "Blog";
            blog.Alter<TitlePart>(part => part.Title = "Blog");
            blog.Alter<AutoroutePart>(part => part.Path = BlogPath);
            await content.CreateAsync(blog, VersionOptions.Published);
            return blog.ContentItemId;
        });

        await site.ScopeAsync(async sp =>
        {
            var sites = sp.GetRequiredService<ISiteService>();
            var settings = await sites.LoadSiteSettingsAsync();
            settings.BaseUrl = FixtureHost.SiteBaseUrl;
            await sites.UpdateSiteSettingsAsync(settings);
        });
    }

    /// <summary>Creates one user per narrow role defined by the setup recipe.</summary>
    public Task CreateUsersAsync(FixtureHost site) => site.ScopeAsync(async sp =>
    {
        var users = sp.GetRequiredService<IUserService>();
        foreach (var (name, role) in new[] { (ManagerUser, "AutoBlogManager"), (VisitorUser, "AdminVisitor") })
        {
            var problems = new List<string>();
            var user = new User
            {
                UserName = name,
                Email = name + "@example.invalid",
                EmailConfirmed = true,
                IsEnabled = true,
                RoleNames = [role],
            };
            var created = await users.CreateUserAsync(user, UserPassword, (_, message) => problems.Add(message));
            if (created is null || problems.Count > 0)
                throw new InvalidOperationException($"Could not create fixture user '{name}': {string.Join("; ", problems)}");
        }
    });
}
