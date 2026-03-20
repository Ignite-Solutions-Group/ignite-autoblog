using IgniteSG.OrchardCore.MspMarketingEdge.Models;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Services;

public class ImportResult
{
    public List<ImportedPostInfo> CreatedPosts { get; set; } = new();
    public List<ImportedPostInfo> UpdatedPosts { get; set; } = new();
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public bool LicenseValid { get; set; } = true;
    public bool LicenseTransientError { get; set; }
    public string? ErrorMessage { get; set; }
    public bool FeedDisabled { get; set; }
    public bool MissingEmail { get; set; }
    public int ApiArticleCount { get; set; }
}

public class ImportedPostInfo
{
    public string Title { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public interface IAutoBlogImportService
{
    Task<ImportResult> RunImportAsync();
}
