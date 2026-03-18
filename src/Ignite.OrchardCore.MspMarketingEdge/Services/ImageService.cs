using Microsoft.Extensions.Logging;
using OrchardCore.Media;

namespace Ignite.OrchardCore.MspMarketingEdge.Services;

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

        try
        {
            var client = _httpClientFactory.CreateClient("EdgeApi");
            var response = await client.GetAsync(imageUrl);
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

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", fileName.Select(c => invalid.Contains(c) ? '-' : c));
        return sanitized.Trim('-');
    }
}
