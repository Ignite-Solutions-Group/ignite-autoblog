using Ignite.OrchardCore.MspMarketingEdge.Models;

namespace Ignite.OrchardCore.MspMarketingEdge.Services;

public enum LicenseCheckResult
{
    Valid,
    Invalid,
    TransientError
}

public interface IEdgeApiService
{
    Task<LicenseCheckResult> CheckLicenseAsync(string email);
    Task<List<EdgeFeedArticle>> GetFeedArticlesAsync(int feedId, string email);
    Task<string?> ResolveParentPostUrlAsync(int parentPostId);
}
