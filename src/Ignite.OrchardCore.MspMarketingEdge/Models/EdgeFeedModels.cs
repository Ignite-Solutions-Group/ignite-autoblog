using System.Text.Json.Serialization;

namespace Ignite.OrchardCore.MspMarketingEdge.Models;

public class EdgeLicenseResponse
{
    [JsonPropertyName("license_activated")]
    public string LicenseActivated { get; set; } = string.Empty;

    public bool IsLicensed => string.Equals(LicenseActivated, "true", StringComparison.OrdinalIgnoreCase);
}

public class EdgeFeedArticle
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public EdgeRendered Title { get; set; } = new();

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public EdgeRendered Content { get; set; } = new();

    [JsonPropertyName("excerpt")]
    public EdgeRendered Excerpt { get; set; } = new();

    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;

    [JsonPropertyName("guid")]
    public EdgeRendered Guid { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("yoast_head_json")]
    public EdgeYoastHead? YoastHeadJson { get; set; }

    [JsonPropertyName("acf")]
    public EdgeAcf? Acf { get; set; }
}

public class EdgeRendered
{
    [JsonPropertyName("rendered")]
    public string Rendered { get; set; } = string.Empty;
}

public class EdgeYoastHead
{
    [JsonPropertyName("og_image")]
    public List<EdgeOgImage>? OgImage { get; set; }
}

public class EdgeOgImage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public class EdgeAcf
{
    [JsonPropertyName("week_commencing")]
    public string? WeekCommencing { get; set; }

    [JsonPropertyName("send_update")]
    public bool SendUpdate { get; set; }

    [JsonPropertyName("update_version")]
    public int UpdateVersion { get; set; }

    [JsonPropertyName("parent_page_url")]
    public int? ParentPageUrl { get; set; }
}

public class EdgeParentPostResponse
{
    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;
}
