using Ignite.OrchardCore.MspMarketingEdge.Models;

namespace Ignite.OrchardCore.MspMarketingEdge.Services;

public class ImportResult
{
    public List<ImportedPostInfo> CreatedPosts { get; set; } = new();
    public List<ImportedPostInfo> UpdatedPosts { get; set; } = new();
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public bool LicenseValid { get; set; } = true;
    public string? ErrorMessage { get; set; }
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
