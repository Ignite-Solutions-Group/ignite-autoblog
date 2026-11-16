using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IgniteSG.OrchardCore.MspMarketingEdge;
using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using IgniteSG.OrchardCore.MspMarketingEdge.Services;
using OrchardCore.ContentManagement;
using OrchardCore.Media;
using OrchardCore.Media.Fields;
using OrchardCore.Seo.Models;

/// <summary>
/// The module's own detail and summary templates, rendered by a stock Orchard theme that does not override
/// them, plus the media paths those templates turn into URLs.
/// </summary>
static partial class RenderingChecks
{
    public static async Task TemplatesAsync(FixtureHost site, Checks c, Scenario s)
    {
        using var visitor = site.CreateBrowser();

        var detail = await visitor.GetAsync("/blog/patch-tuesday");
        if (Environment.GetEnvironmentVariable("AUTOBLOG_FIXTURE_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            File.WriteAllText(Path.Combine(dump, "detail.html"), detail.Body);
            File.WriteAllText(Path.Combine(dump, "list.html"), (await visitor.GetAsync("/" + Scenario.BlogPath)).Body);
        }
        c.That(detail.IsOk, "A published article renders", $"{(int)detail.Status}");
        var html = detail.Body;
        c.Contains(html, "<article class=\"autoblog-post\">", "The module's detail template is the one rendered");
        c.Contains(html, "<h1>Patch Tuesday</h1>", "The detail template shows the title");

        // The templates print the stored UTC value as it is, exactly as the Orchard Core 3 line does (the
        // upgrade check compares the two renderings); the site time zone is not applied.
        const string machine = "2026-10-15T08:30:00", text = "15 October 2026";
        c.Contains(html, $"<time datetime=\"{machine}\">", "The detail template carries the vendor publication time");
        c.Contains(html, text, "The detail template shows the publication date");
        c.Contains(html, "min read", "The detail template shows a reading time");

        c.Contains(html, "<div class=\"autoblog-featured-image", "A blog-only article shows its featured image");
        var image = ImageSource(html, "autoblog-featured-image");
        c.Equal("/media/autoblog/patch-tuesday.png", image, "The featured image uses the public media URL");
        c.Contains(html, "alt=\"Patch Tuesday\"", "The featured image carries its text");
        if (image != null)
        {
            var file = await visitor.GetAsync(image);
            c.That(file.IsOk && file.Bytes.AsSpan().SequenceEqual(FakeEdgeServer.PngBytes), "The featured image URL serves the stored file", $"{(int)file.Status}");
        }

        c.Contains(html, "<p>Vendor body D</p>", "The detail template renders the stored body");
        c.Contains(html, "{{ Site.SiteName }}", "Liquid-looking vendor text is shown as written");
        c.Lacks(html, "liquid-ran", "Vendor text is never executed as a template");

        c.Contains(html, $"<a href=\"{s.Published.Link}\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">", "The detail template credits and links the original");
        c.That(Regex.IsMatch(html, $"<link[^>]+rel=\"canonical\"[^>]+href=\"{Regex.Escape(s.Published.Link)}\"|<link[^>]+href=\"{Regex.Escape(s.Published.Link)}\"[^>]+rel=\"canonical\""),
            "The page declares the vendor's original as canonical");
        c.That(Regex.IsMatch(html, "<meta[^>]+name=\"robots\"[^>]+content=\"noindex, follow\"|<meta[^>]+content=\"noindex, follow\"[^>]+name=\"robots\""),
            "The page is marked noindex, follow");
        var jsonLd = Regex.Match(html, "<script type=\"application/ld\\+json\">(?<json>.*?)</script>", RegexOptions.Singleline);
        c.That(jsonLd.Success && JsonNode.Parse(jsonLd.Groups["json"].Value)?["@type"]?.GetValue<string>() == "Article", "The page carries valid Article structured data");

        var video = await visitor.GetAsync("/blog/video-briefing");
        c.That(video.IsOk, "A video article renders", $"{(int)video.Status}");
        c.Lacks(video.Body, "autoblog-featured-image", "A video article leaves its featured image to the embedded video");
        c.Contains(video.Body, "Vendor body V with an embedded video", "A video article still renders its body");

        var plain = await visitor.GetAsync("/blog/plain-update");
        c.That(plain.IsOk && !plain.Body.Contains("autoblog-featured-image", StringComparison.Ordinal), "An article without an image renders without an image block");

        var list = await visitor.GetAsync("/" + Scenario.BlogPath);
        c.That(list.IsOk, "The blog list renders", $"{(int)list.Status}");
        var summary = Regex.Match(list.Body, "<article class=\"autoblog-summary.*?</article>", RegexOptions.Singleline);
        c.That(summary.Success, "The module's summary template is the one rendered in the list");
        var card = Card(list.Body, "/blog/patch-tuesday");
        c.That(card != null, "The list links each summary to its article");
        if (card != null)
        {
            c.Contains(card, "<img src=\"/media/autoblog/patch-tuesday.png\"", "A summary shows the article image");
            c.Contains(card, "Excerpt D", "A summary shows the excerpt");
            c.Contains(card, text, "A summary shows the publication date");
            c.Contains(card, "Read more", "A summary links onward");
        }
        var plainCard = Card(list.Body, "/blog/plain-update");
        c.That(plainCard != null && !plainCard.Contains("<img", StringComparison.Ordinal), "A summary without an image has no image element");
        c.Lacks(list.Body, "/blog/future-draft", "Drafts are not listed");
    }

    /// <summary>
    /// Media names with spaces and punctuation, both as the module names its own downloads and as an editor
    /// might name a file picked from the media library.
    /// </summary>
    public static async Task MediaAsync(FixtureHost site, Checks c, Scenario s)
    {
        using var visitor = site.CreateBrowser();

        // Names the module derives from a vendor slug or an article route are the same on every host OS.
        var derived = new (string Slug, string Expected)[]
        {
            ("it's a test & more", "autoblog/it's a test & more.png"),
            ("100% off #1 (really) + more", "autoblog/100% off #1 (really) + more.png"),
            ("what? now: yes*", "autoblog/what- now- yes.png"),
            ("blog/nested\\route", "autoblog/blog-nested-route.png"),
            ("..hidden..dots", "autoblog/hiddendots.png"),
            ("quoted \"name\" <tag> |pipe|", "autoblog/quoted -name- -tag- -pipe.png"),
        };
        foreach (var (slug, expected) in derived)
        {
            var stored = await site.ScopeAsync(sp => sp.GetRequiredService<IImageService>().DownloadAndStoreImageAsync(FakeEdgeServer.ImageUrl("m.png"), slug));
            c.Equal(expected, stored, $"The image for '{slug}' is stored under a portable name");
        }

        // Files an editor could have uploaded, including a nested folder.
        var uploaded = new[] { "autoblog/2026/Q4 picks/it's here & now #2 (final) 50%.png", "editor uploads/What's new, really; today!.png" };
        await site.ScopeAsync(async sp =>
        {
            var store = sp.GetRequiredService<IMediaFileStore>();
            foreach (var path in uploaded)
            {
                using var stream = new MemoryStream(FakeEdgeServer.PngBytes);
                await store.CreateFileFromStreamAsync(path, stream, overwrite: true);
            }
        });

        var postId = (await site.PostAsync(s.PublishedPlain))!.ContentItemId;
        foreach (var path in derived.Select(d => d.Expected).Concat(uploaded))
        {
            var expectedUrl = await site.ScopeAsync(async sp =>
            {
                var content = sp.GetRequiredService<IContentManager>();
                var item = await content.GetAsync(postId, VersionOptions.Latest);
                item.Alter<SeoMetaPart>(seo => seo.OpenGraphImage = new MediaField { Paths = ["/" + path], MediaTexts = ["Fixture image"] });
                await content.UpdateAsync(item);
                return sp.GetRequiredService<IMediaFileStore>().MapPathToPublicUrl(path);
            });

            var page = await visitor.GetAsync("/blog/plain-update");
            var source = ImageSource(page.Body, "autoblog-featured-image");
            c.Equal(expectedUrl, source, $"The template URL for '{path}' is the media store's public URL");
            if (source is null) continue;

            c.Equal("/media/" + path, Uri.UnescapeDataString(source), $"The URL for '{path}' decodes back to the stored path exactly once");
            var file = await visitor.GetAsync(source);
            c.That(file.IsOk && file.Bytes.AsSpan().SequenceEqual(FakeEdgeServer.PngBytes), $"The URL for '{path}' serves the stored file", $"{source} answered {(int)file.Status}");
        }

        await site.ScopeAsync(async sp =>
        {
            var content = sp.GetRequiredService<IContentManager>();
            var item = await content.GetAsync(postId, VersionOptions.Latest);
            item.Alter<SeoMetaPart>(seo => seo.OpenGraphImage = new MediaField { Paths = [], MediaTexts = [] });
            await content.UpdateAsync(item);
        });
    }

    /// <summary>The decoded src of the image inside the named block, or null when the block has no image.</summary>
    private static string? ImageSource(string html, string blockClass)
    {
        var block = Regex.Match(html, $"<div class=\"{Regex.Escape(blockClass)}[^\"]*\">(?<body>.*?)</div>", RegexOptions.Singleline);
        if (!block.Success) return null;
        var source = Browser.Attribute(ImageTag().Match(block.Groups["body"].Value).Value, "src");
        return source is null ? null : WebUtility.HtmlDecode(source);
    }

    private static string? Card(string html, string route) =>
        Regex.Matches(html, "<article class=\"autoblog-summary.*?</article>", RegexOptions.Singleline)
            .Select(m => m.Value).FirstOrDefault(card => card.Contains($"href=\"{route}\"", StringComparison.Ordinal));

    [GeneratedRegex("<img\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTag();
}
