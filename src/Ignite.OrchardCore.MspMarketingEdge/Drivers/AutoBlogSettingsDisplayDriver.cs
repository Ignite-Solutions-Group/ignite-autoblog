using Cronos;
using Ignite.OrchardCore.MspMarketingEdge.Models;
using Ignite.OrchardCore.MspMarketingEdge.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Records;
using OrchardCore.DisplayManagement.Entities;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;
using OrchardCore.Lists.Models;
using OrchardCore.Settings;
using YesSql;

namespace Ignite.OrchardCore.MspMarketingEdge.Drivers;

public class AutoBlogSettingsDisplayDriver : SiteDisplayDriver<AutoBlogSettings>
{
    public const string GroupId = "AutoBlog";
    private const string BackgroundTaskCron = "0 */6 * * *";

    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IContentDefinitionManager _contentDefinitionManager;
    private readonly YesSql.ISession _session;

    public AutoBlogSettingsDisplayDriver(
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor,
        IContentDefinitionManager contentDefinitionManager,
        YesSql.ISession session)
    {
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
        _contentDefinitionManager = contentDefinitionManager;
        _session = session;
    }

    protected override string SettingsGroupId => GroupId;

    public override async Task<IDisplayResult?> EditAsync(ISite site, AutoBlogSettings settings, BuildEditorContext context)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !await _authorizationService.AuthorizeAsync(user, Permissions.ManageAutoBlogSettings))
        {
            return null;
        }

        // Convert UTC timestamps to the site's configured timezone
        var timeZone = GetSiteTimeZone(site);

        var lastSyncLocal = settings.LastSyncUtc.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(settings.LastSyncUtc.Value, DateTimeKind.Utc), timeZone)
            : (DateTime?)null;

        var nextSyncUtc = CalculateNextSync(settings.LastSyncUtc);
        var nextSyncLocal = nextSyncUtc.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nextSyncUtc.Value, DateTimeKind.Utc), timeZone)
            : (DateTime?)null;

        var blogContainerOptions = await GetBlogContainerOptionsAsync();

        return Initialize<AutoBlogSettingsViewModel>("AutoBlogSettings_Edit", model =>
        {
            model.LicensedEmail = settings.LicensedEmail;
            model.FeedId = settings.FeedId;
            model.DirectlyPublish = settings.DirectlyPublish;
            model.NotificationEmail = settings.NotificationEmail;
            model.TwitterHandle = settings.TwitterHandle;
            model.PublisherName = settings.PublisherName;
            model.EnableGoogleStructuredData = settings.EnableGoogleStructuredData;
            model.BlogContainerContentItemId = settings.BlogContainerContentItemId;
            model.BlogContainerOptions = blogContainerOptions;

            // Sync status (local times)
            model.LastSyncLocal = lastSyncLocal;
            model.LastSyncSuccess = settings.LastSyncSuccess;
            model.LastSyncSummary = settings.LastSyncSummary;
            model.LastSyncError = settings.LastSyncError;
            model.NextSyncLocal = nextSyncLocal;
        }).Location("Content:1")
          .OnGroup(SettingsGroupId);
    }

    public override async Task<IDisplayResult?> UpdateAsync(ISite site, AutoBlogSettings settings, UpdateEditorContext context)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !await _authorizationService.AuthorizeAsync(user, Permissions.ManageAutoBlogSettings))
        {
            return null;
        }

        var model = new AutoBlogSettingsViewModel();
        await context.Updater.TryUpdateModelAsync(model, Prefix);

        settings.LicensedEmail = model.LicensedEmail?.Trim() ?? string.Empty;
        settings.FeedId = model.FeedId;
        settings.DirectlyPublish = model.DirectlyPublish;
        settings.NotificationEmail = model.NotificationEmail?.Trim() ?? string.Empty;
        settings.TwitterHandle = model.TwitterHandle?.Trim() ?? string.Empty;
        settings.PublisherName = model.PublisherName?.Trim() ?? string.Empty;
        settings.EnableGoogleStructuredData = model.EnableGoogleStructuredData;
        settings.BlogContainerContentItemId = model.BlogContainerContentItemId?.Trim() ?? string.Empty;

        // Don't overwrite sync status fields — those are managed by the import service

        return await EditAsync(site, settings, context);
    }

    private static TimeZoneInfo GetSiteTimeZone(ISite site)
    {
        if (!string.IsNullOrEmpty(site.TimeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(site.TimeZoneId);
            }
            catch (TimeZoneNotFoundException) { }
        }

        return TimeZoneInfo.Utc;
    }

    private async Task<List<SelectListItem>> GetBlogContainerOptionsAsync()
    {
        // Find all content types that have a ListPart attached.
        var listContentTypes = (await _contentDefinitionManager.ListTypeDefinitionsAsync())
            .Where(t => t.Parts.Any(p => p.PartDefinition.Name == nameof(ListPart)))
            .Select(t => t.Name)
            .ToList();

        var options = new List<SelectListItem>
        {
            new("— Select a container —", string.Empty),
        };

        if (listContentTypes.Count == 0)
        {
            return options;
        }

        // Query published content items of those types.
        var containers = await _session
            .Query<ContentItem, ContentItemIndex>(x =>
                x.Published && listContentTypes.Contains(x.ContentType))
            .ListAsync();

        options.AddRange(
            containers
                .OrderBy(c => c.DisplayText)
                .Select(c => new SelectListItem(
                    $"{c.DisplayText} ({c.ContentType})",
                    c.ContentItemId)));

        return options;
    }

    private static DateTime? CalculateNextSync(DateTime? lastSyncUtc)
    {
        try
        {
            var cron = CronExpression.Parse(BackgroundTaskCron);
            var baseTime = lastSyncUtc ?? DateTime.UtcNow;

            // If lastSync is in the past, calculate next occurrence from now
            if (baseTime < DateTime.UtcNow)
            {
                baseTime = DateTime.UtcNow;
            }

            return cron.GetNextOccurrence(baseTime, TimeZoneInfo.Utc);
        }
        catch (CronFormatException)
        {
            return null;
        }
    }
}
