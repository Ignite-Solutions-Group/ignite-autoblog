namespace Ignite.OrchardCore.MspMarketingEdge.Services;

public interface IImageService
{
    /// <summary>
    /// Downloads an image from the given URL and stores it in the Orchard media library.
    /// Returns the media library path (e.g., "/autoblog/my-post-slug.jpeg"), or null if the download failed.
    /// </summary>
    Task<string?> DownloadAndStoreImageAsync(string imageUrl, string slug);
}
