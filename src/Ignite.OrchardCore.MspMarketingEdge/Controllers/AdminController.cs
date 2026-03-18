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

        if (!result.LicenseValid)
        {
            await _notifier.WarningAsync(H["License validation failed. Please check your licensed email address."]);
        }
        else if (result.ErrorCount > 0)
        {
            var message = $"Import encountered {result.ErrorCount} error(s): {result.ErrorMessage} — check the logs for details.";
            await _notifier.ErrorAsync(H[message]);
        }
        else if (result.CreatedPosts.Count == 0 && result.UpdatedPosts.Count == 0)
        {
            await _notifier.InformationAsync(H["No new or updated posts found."]);
        }
        else
        {
            var message = $"Import complete: {result.CreatedPosts.Count} post(s) created, {result.UpdatedPosts.Count} post(s) updated.";
            await _notifier.SuccessAsync(H[message]);
        }

        return RedirectToAction("Index", "Admin", new
        {
            area = "OrchardCore.Settings",
            groupId = Drivers.AutoBlogSettingsDisplayDriver.GroupId
        });
    }
}
