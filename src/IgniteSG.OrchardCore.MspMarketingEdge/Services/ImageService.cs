using Microsoft.Extensions.Logging;
using OrchardCore.Media;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Services;

public class ImageService : IImageService
{
    private readonly IMediaFileStore _mediaFileStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ImageService> _logger;

    private static readonly Dictionary<string, string> MimeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        { "image/jpeg", ".jpeg" },
        { "image/jpg", ".jpeg" },
        { "image/png", ".png" },
        { "image/gif", ".gif" },
        { "image/webp", ".webp" },
        { "image/svg+xml", ".svg" }
    };

    public ImageService(
        IMediaFileStore mediaFileStore,
        IHttpClientFactory httpClientFactory,
        ILogger<ImageService> logger)
    {
        _mediaFileStore = mediaFileStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> DownloadAndStoreImageAsync(string imageUrl, string slug)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        // Validate URL scheme to prevent SSRF
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            _logger.LogWarning("Rejected image URL with invalid scheme: {ImageUrl}", imageUrl);
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            using var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var extension = MimeToExtension.GetValueOrDefault(contentType, ".jpeg");

            var sanitizedSlug = SanitizeFileName(slug);
            var mediaPath = $"{Constants.MediaFolder}/{sanitizedSlug}{extension}";

            await using var stream = await response.Content.ReadAsStreamAsync();
            await _mediaFileStore.CreateFileFromStreamAsync(mediaPath, stream, overwrite: true);

            _logger.LogInformation("Stored featured image at {MediaPath} for slug '{Slug}'.", mediaPath, slug);
            return mediaPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download featured image from {ImageUrl} for slug '{Slug}'.", imageUrl, slug);
            return null;
        }
    }

    // A fixed set rather than Path.GetInvalidFileNameChars(), which differs by host OS: it lists these
    // characters on Windows but only '/' and NUL on Linux, so the same article was stored under different
    // media paths per platform, and a '?' kept on Linux is read as a query string by asset_url.
    private static readonly HashSet<char> s_invalidFileNameChars = ['"', '<', '>', '|', ':', '*', '?', '\\', '/'];

    private static string SanitizeFileName(string fileName)
    {
        // Also strip directory separators and dot-dot sequences to prevent path traversal
        var sanitized = string.Join("", fileName
            .Replace("..", "")
            .Select(c => c < ' ' || s_invalidFileNameChars.Contains(c) ? '-' : c));
        return sanitized.Trim('-');
    }
}