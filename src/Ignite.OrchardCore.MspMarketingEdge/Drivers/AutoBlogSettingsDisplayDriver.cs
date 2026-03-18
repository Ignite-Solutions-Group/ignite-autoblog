using Cronos;
using Ignite.OrchardCore.MspMarketingEdge.Models;
using Ignite.OrchardCore.MspMarketingEdge.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OrchardCore.DisplayManagement.Entities;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;
using OrchardCore.Modules;
using OrchardCore.Settings;

namespace Ignite.OrchardCore.MspMarketingEdge.Drivers;

public class AutoBlogSettingsDisplayDriver : SiteDisplayDriver<AutoBlogSettings>
{
    public const string GroupId = "AutoBlog";
    private const string BackgroundTaskCron = "0 */6 * * *";

    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILocalClock _localClock;

    public AutoBlogSettingsDisplayDriver(
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor,
        ILocalClock localClock)
    {
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
        _localClock = localClock;
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
        var lastSyncLocal = settings.LastSyncUtc.HasValue
            ? await _localClock.ConvertToLocalAsync(new DateTimeOffset(DateTime.SpecifyKind(settings.LastSyncUtc.Value, DateTimeKind.Utc)))
            : (DateTimeOffset?)null;

        var nextSyncUtc = CalculateNextSync(settings.LastSyncUtc);
        var nextSyncLocal = nextSyncUtc.HasValue
            ? await _localClock.ConvertToLocalAsync(new DateTimeOffset(DateTime.SpecifyKind(nextSyncUtc.Value, DateTimeKind.Utc)))
            : (DateTimeOffset?)null;

        return Initialize<AutoBlogSettingsViewModel>("AutoBlogSettings_Edit", model =>
        {
            model.LicensedEmail = settings.LicensedEmail;
            model.FeedId = settings.FeedId;
            model.DirectlyPublish = settings.DirectlyPublish;
            model.NotificationEmail = settings.NotificationEmail;
            model.TwitterHandle = settings.TwitterHandle;
            model.PublisherName = settings.PublisherName;
            model.EnableGoogleStructuredData = settings.EnableGoogleStructuredData;

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

        // Don't overwrite sync status fields — those are managed by the import service

        return await EditAsync(site, settings, context);
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
        catch
        {
            return null;
        }
    }
}
