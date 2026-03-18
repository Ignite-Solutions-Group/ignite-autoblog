using Ignite.OrchardCore.MspMarketingEdge.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using OrchardCore.DisplayManagement.Notify;

namespace Ignite.OrchardCore.MspMarketingEdge.Controllers;

[Route("admin/autoblog")]
public class AdminController : Controller
{
    private readonly IAutoBlogImportService _importService;
    private readonly IAuthorizationService _authorizationService;
    private readonly INotifier _notifier;
    private readonly IHtmlLocalizer H;

    public AdminController(
        IAutoBlogImportService importService,
        IAuthorizationService authorizationService,
        INotifier notifier,
        IHtmlLocalizer<AdminController> htmlLocalizer)
    {
        _importService = importService;
        _authorizationService = authorizationService;
        _notifier = notifier;
        H = htmlLocalizer;
    }

    [HttpPost("force-refresh")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForceRefresh()
    {
        if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageAutoBlogSettings))
        {
            return Forbid();
        }

        var result = await _importService.RunImportAsync();

        if (result.FeedDisabled && result.LicenseValid)
        {
            await _notifier.SuccessAsync(H["API test passed — the API returned {0} article(s). Choose a feed option below to enable content creation.", result.ApiArticleCount]);
        }
        else if (result.FeedDisabled && !result.LicenseValid)
        {
            await _notifier.ErrorAsync(H["API test failed — license validation did not succeed. Please check your licensed email address below."]);
        }
        else if (result.MissingEmail)
        {
            await _notifier.WarningAsync(H["Import skipped: no licensed email address configured. Please enter your MSP Marketing Edge email below."]);
        }
        else if (!result.LicenseValid)
        {
            await _notifier.WarningAsync(H["License validation failed. Please check your licensed email address."]);
        }
        else if (result.ErrorCount > 0)
        {
            await _notifier.ErrorAsync(H["Import failed for {0} article(s). See the error details in the Sync Status card below.", result.ErrorCount]);
        }
        else if (result.CreatedPosts.Count == 0 && result.UpdatedPosts.Count == 0)
        {
            await _notifier.InformationAsync(H["The API returned {0} article(s), but no new or updated posts were found.", result.ApiArticleCount]);
        }
        else
        {
            await _notifier.SuccessAsync(H["Import complete: {0} post(s) created, {1} post(s) updated.", result.CreatedPosts.Count, result.UpdatedPosts.Count]);
        }

        return RedirectToAction("Index", "Admin", new
        {
            area = "OrchardCore.Settings",
            groupId = Drivers.AutoBlogSettingsDisplayDriver.GroupId
        });
    }
}
