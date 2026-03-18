using System.Text.Json;
using Ignite.OrchardCore.MspMarketingEdge.Models;
using Microsoft.Extensions.Logging;

namespace Ignite.OrchardCore.MspMarketingEdge.Services;

public class EdgeApiService : IEdgeApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EdgeApiService> _logger;

    public EdgeApiService(IHttpClientFactory httpClientFactory, ILogger<EdgeApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> CheckLicenseAsync(string email)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var url = $"{Constants.LicenseEndpoint}?email={Uri.EscapeDataString(email)}&tool=techupdatesplugin&time={timestamp}";

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EdgeLicenseResponse>(json);

            return result?.IsLicensed ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate MSP Marketing Edge license for {Email}.", email);
            return false;
        }
    }

    public async Task<List<EdgeFeedArticle>> GetFeedArticlesAsync(int feedId, string email)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var url = $"{Constants.FeedEndpoint}?categories={feedId}&uid={Uri.EscapeDataString(email)}&time={timestamp}";

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var articles = JsonSerializer.Deserialize<List<EdgeFeedArticle>>(json);

            return articles ?? new List<EdgeFeedArticle>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feed articles from MSP Marketing Edge (FeedId={FeedId}).", feedId);
            return new List<EdgeFeedArticle>();
        }
    }

    public async Task<string?> ResolveParentPostUrlAsync(int parentPostId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            var url = $"{Constants.ParentPostEndpoint}{parentPostId}";

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EdgeParentPostResponse>(json);

            return string.IsNullOrWhiteSpace(result?.Link) ? null : result.Link;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve parent post URL for post ID {ParentPostId}.", parentPostId);
            return null;
        }
    }
}
