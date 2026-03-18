using Ignite.OrchardCore.MspMarketingEdge.Models;

namespace Ignite.OrchardCore.MspMarketingEdge.Services;

public interface IEdgeApiService
{
    Task<bool> CheckLicenseAsync(string email);
    Task<List<EdgeFeedArticle>> GetFeedArticlesAsync(int feedId, string email);
    Task<string?> ResolveParentPostUrlAsync(int parentPostId);
}
