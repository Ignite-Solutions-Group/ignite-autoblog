namespace Ignite.OrchardCore.MspMarketingEdge.Models;

public class AutoBlogSettings
{
    public string LicensedEmail { get; set; } = string.Empty;
    public int FeedId { get; set; } = 0;
    public bool DirectlyPublish { get; set; } = false;
    public string NotificationEmail { get; set; } = string.Empty;
    public string TwitterHandle { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;
    public bool EnableGoogleStructuredData { get; set; } = true;

    // Sync status (written by the import service, read-only in the UI)
    public DateTime? LastSyncUtc { get; set; }
    public bool LastSyncSuccess { get; set; }
    public string LastSyncSummary { get; set; } = string.Empty;
}
