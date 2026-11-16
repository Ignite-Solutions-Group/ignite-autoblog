using System.Text.Json;
using System.Text.Json.Nodes;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using OrchardCore.Autoroute.Models;
using OrchardCore.ContentManagement;
using OrchardCore.Html.Models;
using OrchardCore.Lists.Models;
using OrchardCore.Media.Fields;
using OrchardCore.PublishLater.Models;
using OrchardCore.Seo.Models;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;

/// <summary>
/// What the Orchard Core 3 site looked like when the released module finished writing to it. The upgrade
/// check opens the same database on the current build and compares what it finds against this record.
/// This file compiles against both Orchard versions, so it only uses API that exists in each.
/// </summary>
public sealed class SeedManifest
{
    public const string FileName = "autoblog-seed-manifest.json";

    public string OrchardVersion { get; set; } = string.Empty;
    public string ModuleVersion { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
    public string UserPassword { get; set; } = string.Empty;
    public string BlogContentItemId { get; set; } = string.Empty;
    public int MigrationVersion { get; set; }
    public SeedSettings Settings { get; set; } = new();
    public List<SeedPost> Posts { get; set; } = [];
    public List<FakeArticle> Articles { get; set; } = [];
    public List<string> IndexedGuids { get; set; } = [];
    /// <summary>Pages as the released templates rendered them, keyed by route.</summary>
    public Dictionary<string, string> Pages { get; set; } = [];
    /// <summary>How the released module behaved for cases the upgrade check wants to compare.</summary>
    public Dictionary<string, string> Observations { get; set; } = [];

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public void Save(string folder) => File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, s_json));

    public static SeedManifest Load(string folder) =>
        JsonSerializer.Deserialize<SeedManifest>(File.ReadAllText(Path.Combine(folder, FileName)), s_json)
        ?? throw new InvalidOperationException("The seed manifest is empty.");
}

public sealed class SeedSettings
{
    public string LicensedEmail { get; set; } = string.Empty;
    public int FeedId { get; set; }
    public bool DirectlyPublish { get; set; }
    public string NotificationEmail { get; set; } = string.Empty;
    public string TwitterHandle { get; set; } = string.Empty;
    public string PublisherName { get; set; } = string.Empty;
    public bool EnableGoogleStructuredData { get; set; }
    public string BlogContainerContentItemId { get; set; } = string.Empty;
    public DateTime? LastSyncUtc { get; set; }
    public bool LastSyncSuccess { get; set; }
    public string LastSyncSummary { get; set; } = string.Empty;
    public string? LastSyncError { get; set; }

    public static SeedSettings From(AutoBlogSettings s) => new()
    {
        LicensedEmail = s.LicensedEmail,
        FeedId = s.FeedId,
        DirectlyPublish = s.DirectlyPublish,
        NotificationEmail = s.NotificationEmail,
        TwitterHandle = s.TwitterHandle,
        PublisherName = s.PublisherName,
        EnableGoogleStructuredData = s.EnableGoogleStructuredData,
        BlogContainerContentItemId = s.BlogContainerContentItemId,
        LastSyncUtc = s.LastSyncUtc,
        LastSyncSuccess = s.LastSyncSuccess,
        LastSyncSummary = s.LastSyncSummary,
        LastSyncError = s.LastSyncError,
    };

    /// <summary>The names of the settings that differ, empty when the two are the same.</summary>
    public List<string> DifferencesFrom(SeedSettings other)
    {
        var mine = JsonSerializer.SerializeToNode(this)!.AsObject();
        var theirs = JsonSerializer.SerializeToNode(other)!.AsObject();
        return mine.Where(p => !JsonNode.DeepEquals(p.Value, theirs[p.Key])).Select(p => p.Key).ToList();
    }
}

public sealed class SeedPost
{
    public string Guid { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string ContentItemVersionId { get; set; } = string.Empty;
    public bool Published { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Html { get; set; }
    public string? ListContentItemId { get; set; }
    public bool PreserveLocalEdits { get; set; }
    public int EdgePostId { get; set; }
    public string EdgeUrl { get; set; } = string.Empty;
    public int EdgeUpdateVersion { get; set; }
    public string EdgeWeekCommencing { get; set; } = string.Empty;
    public bool EdgeHasVideo { get; set; }
    public string? Canonical { get; set; }
    public string? MetaRobots { get; set; }
    public string? MetaDescription { get; set; }
    public string? GoogleSchema { get; set; }
    public string? FeaturedImagePath { get; set; }
    public string? OpenGraphImagePath { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? PublishedUtc { get; set; }
    public DateTime? ScheduledPublishUtc { get; set; }
    /// <summary>The whole stored content document, for an exact before-and-after comparison.</summary>
    public string ContentJson { get; set; } = string.Empty;

    public static SeedPost From(ContentItem item)
    {
        var part = item.Get<AutoBlogPart>(nameof(AutoBlogPart)) ?? new AutoBlogPart();
        var seo = item.Get<SeoMetaPart>(nameof(SeoMetaPart));
        var content = (JsonObject)item.Content;
        return new SeedPost
        {
            Guid = part.EdgeGuid,
            ContentItemId = item.ContentItemId,
            ContentItemVersionId = item.ContentItemVersionId,
            Published = item.Published,
            DisplayText = item.DisplayText,
            Path = item.Get<AutoroutePart>(nameof(AutoroutePart))?.Path,
            Html = item.Get<HtmlBodyPart>(nameof(HtmlBodyPart))?.Html,
            ListContentItemId = item.Get<ContainedPart>(nameof(ContainedPart))?.ListContentItemId,
            PreserveLocalEdits = part.PreserveLocalEdits,
            EdgePostId = part.EdgePostId,
            EdgeUrl = part.EdgeUrl,
            EdgeUpdateVersion = part.EdgeUpdateVersion,
            EdgeWeekCommencing = part.EdgeWeekCommencing,
            EdgeHasVideo = part.EdgeHasVideo,
            Canonical = seo?.Canonical,
            MetaRobots = seo?.MetaRobots,
            MetaDescription = seo?.MetaDescription,
            GoogleSchema = seo?.GoogleSchema,
            FeaturedImagePath = item.Get<ContentPart>("AutoBlogPost")?.Get<MediaField>("FeaturedImage")?.Paths?.FirstOrDefault(),
            OpenGraphImagePath = seo?.OpenGraphImage?.Paths?.FirstOrDefault(),
            CreatedUtc = item.CreatedUtc,
            PublishedUtc = item.PublishedUtc,
            ScheduledPublishUtc = item.Get<PublishLaterPart>(nameof(PublishLaterPart))?.ScheduledPublishUtc,
            ContentJson = content.ToJsonString(),
        };
    }

    /// <summary>The names of the stored values that differ, empty when the two are the same.</summary>
    public List<string> DifferencesFrom(SeedPost other)
    {
        var mine = JsonSerializer.SerializeToNode(this)!.AsObject();
        var theirs = JsonSerializer.SerializeToNode(other)!.AsObject();
        var different = mine.Where(p => p.Key != nameof(ContentJson) && !JsonNode.DeepEquals(p.Value, theirs[p.Key])).Select(p => p.Key).ToList();
        if (!JsonNode.DeepEquals(JsonNode.Parse(ContentJson), JsonNode.Parse(other.ContentJson))) different.Add(nameof(ContentJson));
        return different;
    }
}
