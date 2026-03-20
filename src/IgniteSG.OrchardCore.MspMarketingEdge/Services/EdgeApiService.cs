using System.Text.Json;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using Microsoft.Extensions.Logging;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Services;

public class EdgeApiService : IEdgeApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EdgeApiService> _logger;

    public EdgeApiService(IHttpClientFactory httpClientFactory, ILogger<EdgeApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<LicenseCheckResult> CheckLicenseAsync(string email)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var url = $"{Constants.LicenseEndpoint}?email={Uri.EscapeDataString(email)}&tool=techupdatesplugin&time={timestamp}";

            using var response = await client.GetAsync(url);
            if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            {
                _logger.LogWarning(
                    "MSP Marketing Edge license check returned {StatusCode} — transient error, will retry.",
                    (int)response.StatusCode);
                return LicenseCheckResult.TransientError;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EdgeLicenseResponse>(json);

            return result?.IsLicensed == true
                ? LicenseCheckResult.Valid
                : LicenseCheckResult.Invalid;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "MSP Marketing Edge license check failed — transient network error.");
            return LicenseCheckResult.TransientError;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "MSP Marketing Edge license check timed out.");
            return LicenseCheckResult.TransientError;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate MSP Marketing Edge license.");
            return LicenseCheckResult.Invalid;
        }
    }

    public async Task<List<EdgeFeedArticle>> GetFeedArticlesAsync(int feedId, string email)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var url = $"{Constants.FeedEndpoint}?categories={feedId}&uid={Uri.EscapeDataString(email)}&time={timestamp}";

            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var articles = JsonSerializer.Deserialize<List<EdgeFeedArticle>>(json);

            return articles ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch feed articles from MSP Marketing Edge (FeedId={FeedId}).", feedId);
            return [];
        }
    }

    public async Task<string?> ResolveParentPostUrlAsync(int parentPostId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            var url = $"{Constants.ParentPostEndpoint}{parentPostId}";

            using var response = await client.GetAsync(url);
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
