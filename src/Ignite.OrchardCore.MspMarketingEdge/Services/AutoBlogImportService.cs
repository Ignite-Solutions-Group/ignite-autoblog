using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ignite.OrchardCore.MspMarketingEdge.Models;
using Microsoft.Extensions.Logging;
using OrchardCore.Media.Fields;
using OrchardCore.ResourceManagement;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using OrchardCore.Email;
using OrchardCore.Entities;
using OrchardCore.Settings;
using OrchardCore.Title.Models;
using OrchardCore.Html.Models;
using OrchardCore.Autoroute.Models;
using OrchardCore.PublishLater.Models;
using OrchardCore.Seo.Models;
using YesSql;

namespace Ignite.OrchardCore.MspMarketingEdge.Services;

public partial class AutoBlogImportService : IAutoBlogImportService
{
    private readonly ISiteService _siteService;
    private readonly IContentManager _contentManager;
    private readonly ISession _session;
    private readonly IEdgeApiService _edgeApiService;
    private readonly IImageService _imageService;
    private readonly IEmailService _emailService;
    private readonly ILogger<AutoBlogImportService> _logger;

    public AutoBlogImportService(
        ISiteService siteService,
        IContentManager contentManager,
        ISession session,
        IEdgeApiService edgeApiService,
        IImageService imageService,
        IEmailService emailService,
        ILogger<AutoBlogImportService> logger)
    {
        _siteService = siteService;
        _contentManager = contentManager;
        _session = session;
        _edgeApiService = edgeApiService;
        _imageService = imageService;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ImportResult> RunImportAsync()
    {
        var result = new ImportResult();

        var settings = await GetSettingsAsync();
        if (settings == null || settings.FeedId == 0 || string.IsNullOrWhiteSpace(settings.LicensedEmail))
        {
            _logger.LogInformation("AutoBlog import skipped: feed is off or license email is blank.");
            return result;
        }

        var licensed = await _edgeApiService.CheckLicenseAsync(settings.LicensedEmail);
        if (!licensed)
        {
            _logger.LogWarning("AutoBlog import skipped: license not valid for {Email}.", settings.LicensedEmail);
            result.LicenseValid = false;
            await UpdateSyncStatusAsync(result);
            return result;
        }

        var articles = await _edgeApiService.GetFeedArticlesAsync(settings.FeedId, settings.LicensedEmail);
        if (articles.Count == 0)
        {
            _logger.LogInformation("AutoBlog import: no articles returned from feed.");
            await UpdateSyncStatusAsync(result);
            return result;
        }

        _logger.LogInformation("AutoBlog import: processing {Count} articles from feed.", articles.Count);

        foreach (var article in articles)
        {
            try
            {
                await ProcessArticleAsync(article, settings, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoBlog import: error processing article '{Title}' (GUID: {Guid}).",
                    article.Title.Rendered, article.Guid.Rendered);
            }
        }

        if (result.CreatedPosts.Count > 0 || result.UpdatedPosts.Count > 0)
        {
            await SendNotificationEmailAsync(settings, result);
        }

        _logger.LogInformation(
            "AutoBlog import complete: {Created} created, {Updated} updated, {Skipped} skipped.",
            result.CreatedPosts.Count, result.UpdatedPosts.Count, result.SkippedCount);

        await UpdateSyncStatusAsync(result);

        return result;
    }

    private async Task UpdateSyncStatusAsync(ImportResult result)
    {
        try
        {
            var site = await _siteService.LoadSiteSettingsAsync();
            var settings = site.As<AutoBlogSettings>();

            settings.LastSyncUtc = DateTime.UtcNow;
            settings.LastSyncSuccess = result.LicenseValid && result.ErrorMessage == null;
            settings.LastSyncSummary = $"{result.CreatedPosts.Count} created, {result.UpdatedPosts.Count} updated, {result.SkippedCount} skipped";

            if (!result.LicenseValid)
            {
                settings.LastSyncSummary = "License validation failed";
                settings.LastSyncSuccess = false;
            }

            site.Put(settings);
            await _siteService.UpdateSiteSettingsAsync(site);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update sync status in site settings.");
        }
    }

    private async Task ProcessArticleAsync(EdgeFeedArticle article, AutoBlogSettings settings, ImportResult result)
    {
        var guid = article.Guid.Rendered;
        if (string.IsNullOrWhiteSpace(guid))
        {
            _logger.LogWarning("AutoBlog import: skipping article with empty GUID.");
            result.SkippedCount++;
            return;
        }

        var existingItem = await FindExistingPostByGuidAsync(guid);

        if (existingItem == null)
        {
            var created = await CreatePostAsync(article, settings);
            if (created != null)
            {
                result.CreatedPosts.Add(created);
            }
        }
        else if (article.Acf?.SendUpdate == true)
        {
            var existingPart = existingItem.As<AutoBlogPart>();
            if (existingPart != null && existingPart.EdgeUpdateVersion != article.Acf.UpdateVersion)
            {
                var updated = await UpdatePostAsync(existingItem, article, settings);
                if (updated != null)
                {
                    result.UpdatedPosts.Add(updated);
                }
            }
            else
            {
                result.SkippedCount++;
            }
        }
        else
        {
            result.SkippedCount++;
        }
    }

    private async Task<ImportedPostInfo?> CreatePostAsync(EdgeFeedArticle article, AutoBlogSettings settings)
    {
        var contentItem = await _contentManager.NewAsync(Constants.ContentType);

        var title = HtmlDecode(article.Title.Rendered);
        var excerpt = StripHtml(article.Excerpt.Rendered);
        var canonicalUrl = await ResolveCanonicalUrlAsync(article);
        var isVideoFeed = Constants.VideoFeeds.Contains(settings.FeedId);

        // TitlePart
        contentItem.Alter<TitlePart>(part =>
        {
            part.Title = title;
        });
        contentItem.DisplayText = title;

        // HtmlBodyPart
        contentItem.Alter<HtmlBodyPart>(part =>
        {
            part.Html = article.Content.Rendered;
        });

        // AutoroutePart
        contentItem.Alter<AutoroutePart>(part =>
        {
            part.Path = article.Slug;
        });

        // AutoBlogPart
        contentItem.Alter<AutoBlogPart>(part =>
        {
            part.EdgeGuid = article.Guid.Rendered;
            part.EdgePostId = article.Id;
            part.EdgeUrl = canonicalUrl;
            part.EdgeUpdateVersion = article.Acf?.UpdateVersion ?? 0;
            part.EdgeWeekCommencing = article.Acf?.WeekCommencing ?? string.Empty;
            part.EdgeHasVideo = isVideoFeed;
        });

        // Featured image
        var imageUrl = article.YoastHeadJson?.OgImage?.FirstOrDefault()?.Url;
        var mediaPath = await _imageService.DownloadAndStoreImageAsync(imageUrl ?? string.Empty, article.Slug);

        // SeoMetaPart
        await PopulateSeoMetaPartAsync(contentItem, title, excerpt, canonicalUrl, mediaPath, settings);

        // Determine publish status
        var isFutureDated = string.Equals(article.Status, "future", StringComparison.OrdinalIgnoreCase);
        var publishStatus = "Draft";

        if (settings.DirectlyPublish)
        {
            if (isFutureDated)
            {
                // Future-dated + DirectlyPublish: create as draft with scheduled publish date
                if (DateTime.TryParse(article.Date, out var publishDate))
                {
                    contentItem.Alter<PublishLaterPart>(part =>
                    {
                        part.ScheduledPublishUtc = publishDate.ToUniversalTime();
                    });
                }
                publishStatus = "Scheduled";
                await _contentManager.CreateAsync(contentItem, VersionOptions.Draft);
            }
            else
            {
                publishStatus = "Published";
                await _contentManager.CreateAsync(contentItem, VersionOptions.Published);
            }
        }
        else
        {
            publishStatus = "Draft";
            await _contentManager.CreateAsync(contentItem, VersionOptions.Draft);
        }

        _logger.LogInformation("AutoBlog: created post '{Title}' as {Status}.", title, publishStatus);

        return new ImportedPostInfo
        {
            Title = title,
            ContentItemId = contentItem.ContentItemId,
            Status = publishStatus
        };
    }

    private async Task<ImportedPostInfo?> UpdatePostAsync(ContentItem contentItem, EdgeFeedArticle article, AutoBlogSettings settings)
    {
        var title = HtmlDecode(article.Title.Rendered);
        var excerpt = StripHtml(article.Excerpt.Rendered);
        var isVideoFeed = Constants.VideoFeeds.Contains(settings.FeedId);

        // Update title
        contentItem.Alter<TitlePart>(part =>
        {
            part.Title = title;
        });
        contentItem.DisplayText = title;

        // Update body
        contentItem.Alter<HtmlBodyPart>(part =>
        {
            part.Html = article.Content.Rendered;
        });

        // Update AutoBlogPart (keep existing EdgeGuid, EdgeUrl, don't change slug)
        var existingPart = contentItem.As<AutoBlogPart>();
        var canonicalUrl = existingPart?.EdgeUrl ?? await ResolveCanonicalUrlAsync(article);

        contentItem.Alter<AutoBlogPart>(part =>
        {
            part.EdgeUpdateVersion = article.Acf?.UpdateVersion ?? 0;
            part.EdgeWeekCommencing = article.Acf?.WeekCommencing ?? string.Empty;
            part.EdgeHasVideo = isVideoFeed;
        });

        // Re-download featured image
        var imageUrl = article.YoastHeadJson?.OgImage?.FirstOrDefault()?.Url;
        var slug = contentItem.As<AutoroutePart>()?.Path ?? article.Slug;
        var mediaPath = await _imageService.DownloadAndStoreImageAsync(imageUrl ?? string.Empty, slug);

        // Refresh SeoMetaPart (canonical and robots unchanged)
        await PopulateSeoMetaPartAsync(contentItem, title, excerpt, canonicalUrl, mediaPath, settings);

        var publishStatus = "Draft";
        if (settings.DirectlyPublish)
        {
            publishStatus = "Published";
            await _contentManager.PublishAsync(contentItem);
        }
        else
        {
            await _contentManager.SaveDraftAsync(contentItem);
        }

        _logger.LogInformation("AutoBlog: updated post '{Title}' (version {Version}) as {Status}.",
            title, article.Acf?.UpdateVersion, publishStatus);

        return new ImportedPostInfo
        {
            Title = title,
            ContentItemId = contentItem.ContentItemId,
            Status = publishStatus
        };
    }

    private async Task PopulateSeoMetaPartAsync(
        ContentItem contentItem,
        string title,
        string excerpt,
        string canonicalUrl,
        string? mediaPath,
        AutoBlogSettings settings)
    {
        var metaDescription = Truncate(excerpt, 160);
        var ogDescription = Truncate(excerpt, 300);
        var twitterDescription = Truncate(excerpt, 200);

        var imagePaths = mediaPath != null ? new[] { "/" + mediaPath } : Array.Empty<string>();
        var imageTexts = mediaPath != null ? new[] { title } : Array.Empty<string>();
        var twitterHandle = string.IsNullOrWhiteSpace(settings.TwitterHandle) ? null : settings.TwitterHandle;

        string? googleSchema = null;
        if (settings.EnableGoogleStructuredData)
        {
            googleSchema = await BuildGoogleSchemaAsync(contentItem, title, excerpt, mediaPath, settings);
        }

        contentItem.Alter<SeoMetaPart>(seo =>
        {
            seo.Render = true;
            seo.PageTitle = title;
            seo.MetaDescription = metaDescription;
            seo.MetaKeywords = null;
            seo.Canonical = canonicalUrl;
            seo.MetaRobots = "noindex, follow";

            seo.OpenGraphType = "article";
            seo.OpenGraphTitle = title;
            seo.OpenGraphDescription = ogDescription;
            seo.OpenGraphImage = new MediaField { Paths = imagePaths, MediaTexts = imageTexts };

            seo.TwitterCard = mediaPath != null ? "summary_large_image" : "summary";
            seo.TwitterTitle = title;
            seo.TwitterDescription = twitterDescription;
            seo.TwitterImage = new MediaField { Paths = imagePaths, MediaTexts = imageTexts };
            seo.TwitterCreator = twitterHandle;
            seo.TwitterSite = twitterHandle;

            seo.DefaultSocialImage = new MediaField { Paths = imagePaths, MediaTexts = imageTexts };
            seo.GoogleSchema = googleSchema;
            seo.CustomMetaTags = Array.Empty<MetaEntry>();
        });
    }

    private async Task<string> BuildGoogleSchemaAsync(
        ContentItem contentItem,
        string title,
        string excerpt,
        string? mediaPath,
        AutoBlogSettings settings)
    {
        var publisherName = settings.PublisherName;
        if (string.IsNullOrWhiteSpace(publisherName))
        {
            var site = await _siteService.GetSiteSettingsAsync();
            publisherName = site.SiteName ?? "Organization";
        }

        var schema = new
        {
            @context = "https://schema.org",
            @type = "Article",
            headline = title,
            description = Truncate(excerpt, 300),
            image = mediaPath != null ? "/" + mediaPath : (string?)null,
            datePublished = contentItem.CreatedUtc?.ToString("o"),
            publisher = new
            {
                @type = "Organization",
                name = publisherName
            }
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = false });
    }

    private async Task<string> ResolveCanonicalUrlAsync(EdgeFeedArticle article)
    {
        var parentPostId = article.Acf?.ParentPageUrl;
        if (parentPostId.HasValue && parentPostId.Value > 0)
        {
            var parentUrl = await _edgeApiService.ResolveParentPostUrlAsync(parentPostId.Value);
            if (!string.IsNullOrWhiteSpace(parentUrl))
            {
                return parentUrl;
            }
        }

        return article.Link;
    }

    private async Task<ContentItem?> FindExistingPostByGuidAsync(string guid)
    {
        var query = _session.Query<ContentItem, ContentItemIndex>(x =>
            x.ContentType == Constants.ContentType && x.Published);

        var items = await query.ListAsync();

        foreach (var item in items)
        {
            var part = item.As<AutoBlogPart>();
            if (part != null && string.Equals(part.EdgeGuid, guid, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        // Also check drafts
        var draftQuery = _session.Query<ContentItem, ContentItemIndex>(x =>
            x.ContentType == Constants.ContentType && x.Latest && !x.Published);

        var draftItems = await draftQuery.ListAsync();

        foreach (var item in draftItems)
        {
            var part = item.As<AutoBlogPart>();
            if (part != null && string.Equals(part.EdgeGuid, guid, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private async Task SendNotificationEmailAsync(AutoBlogSettings settings, ImportResult result)
    {
        if (string.IsNullOrWhiteSpace(settings.NotificationEmail))
        {
            return;
        }

        var emails = settings.NotificationEmail
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (emails.Length == 0)
        {
            return;
        }

        var isDraft = !settings.DirectlyPublish;
        var subject = isDraft
            ? "MSP Marketing Edge AutoBlog — new DRAFT blog post(s)"
            : "MSP Marketing Edge AutoBlog — new blog post(s)";

        var body = BuildEmailBody(result, isDraft);

        foreach (var email in emails)
        {
            var message = new MailMessage
            {
                To = email,
                Subject = subject,
                Body = body,
                IsHtmlBody = true
            };

            var sendResult = await _emailService.SendAsync(message);
            if (!sendResult.Succeeded)
            {
                _logger.LogWarning("Failed to send AutoBlog notification email to {Email}.", email);
            }
        }
    }

    private static string BuildEmailBody(ImportResult result, bool isDraft)
    {
        var body = "<p>This is an automatic notification from the MSP Marketing Edge AutoBlog module.</p>";

        if (isDraft)
        {
            body += "<p>You have the following new post(s) in DRAFT mode to review and publish.</p>";
        }
        else
        {
            body += "<p>The following new post(s) have been published on your website.</p>";
        }

        if (result.CreatedPosts.Count > 0)
        {
            body += "<h3>New Posts</h3><ul>";
            foreach (var post in result.CreatedPosts)
            {
                body += $"<li><strong>{WebUtility.HtmlEncode(post.Title)}</strong> — {post.Status}</li>";
            }
            body += "</ul>";
        }

        if (result.UpdatedPosts.Count > 0)
        {
            body += "<h3>Updated Posts</h3><ul>";
            foreach (var post in result.UpdatedPosts)
            {
                body += $"<li><strong>{WebUtility.HtmlEncode(post.Title)}</strong> — {post.Status}</li>";
            }
            body += "</ul>";
        }

        body += "<p>You can manage your AutoBlog settings in the Orchard admin under Configuration &gt; MSP Marketing Edge AutoBlog.</p>";
        body += "<p>The MSP Marketing Edge team.</p>";

        return body;
    }

    private async Task<AutoBlogSettings?> GetSettingsAsync()
    {
        var site = await _siteService.GetSiteSettingsAsync();
        return site.As<AutoBlogSettings>();
    }

    private static string HtmlDecode(string html)
    {
        return WebUtility.HtmlDecode(html ?? string.Empty);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = StripHtmlRegex().Replace(html, string.Empty);
        return WebUtility.HtmlDecode(text).Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 3)] + "...";
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex StripHtmlRegex();
}
