using System.ComponentModel.DataAnnotations;

namespace Ignite.OrchardCore.MspMarketingEdge.ViewModels;

public class AutoBlogSettingsViewModel
{
    [Display(Name = "Licensed Email")]
    public string LicensedEmail { get; set; } = string.Empty;

    [Display(Name = "Feed Selection")]
    public int FeedId { get; set; }

    [Display(Name = "Directly Publish")]
    public bool DirectlyPublish { get; set; }

    [Display(Name = "Notification Email")]
    public string NotificationEmail { get; set; } = string.Empty;

    [Display(Name = "Twitter/X Handle")]
    public string TwitterHandle { get; set; } = string.Empty;

    [Display(Name = "Publisher Name")]
    public string PublisherName { get; set; } = string.Empty;

    [Display(Name = "Enable Google Structured Data")]
    public bool EnableGoogleStructuredData { get; set; } = true;

    // Sync status (read-only, populated by the driver)
    public DateTime? LastSyncLocal { get; set; }
    public bool LastSyncSuccess { get; set; }
    public string LastSyncSummary { get; set; } = string.Empty;
    public string? LastSyncError { get; set; }
    public DateTime? NextSyncLocal { get; set; }
}
