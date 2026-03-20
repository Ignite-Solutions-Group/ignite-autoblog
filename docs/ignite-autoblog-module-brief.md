# IgniteSG.AutoBlog — Orchard Core Module Brief

## Purpose

Build a custom Orchard Core module that automatically imports weekly blog posts from the MSP Marketing Edge content syndication service into the Orchard site as native Blog Post content items.

This replaces the functionality of MSP Marketing Edge's WordPress AutoBlog plugin. We are not porting the PHP code — we are reimplementing the same behavior as a proper Orchard Core module using framework-native patterns.

## Target Platform

- Orchard Core 2.2.1
- .NET 8
- MySQL backend
- The site uses a custom Liquid + Bootstrap theme

## Upstream API Contract

The MSP Marketing Edge service exposes two relevant endpoints. Both are simple unauthenticated GET requests (auth is via a licensed email address passed as a query parameter).

### License Validation

```
GET https://www.mspmarketingedge.com/wp-content/keap/msp_edge_license.php
  ?email={licensedEmail}
  &tool=techupdatesplugin
  &time={unixTimestamp}
```

Returns JSON with a `license_activated` field (`"true"` / `"false"` as strings).

### Content Feed

```
GET https://www.yourtechupdates.com/wp-json/tech/videos/
  ?categories={feedId}
  &uid={licensedEmail}
  &time={unixTimestamp}
```

Returns a JSON array of WordPress-style post objects. Each article contains:

| Field Path | Description |
|---|---|
| `id` | Upstream post ID |
| `title.rendered` | Post title (HTML-encoded) |
| `slug` | URL slug |
| `date` | Publish date (ISO format) |
| `content.rendered` | Full post body as HTML |
| `excerpt.rendered` | Short excerpt (may contain HTML tags) |
| `link` | Canonical URL on the source site |
| `guid.rendered` | Globally unique identifier for deduplication |
| `status` | Post status; may be `"publish"` or `"future"` (scheduled) |
| `yoast_head_json.og_image[0].url` | Featured image URL |
| `acf.week_commencing` | Week-commencing date string |
| `acf.send_update` | Boolean; if true, existing posts should be updated |
| `acf.update_version` | Integer version for change detection on updates |
| `acf.parent_page_url` | Optional; upstream parent post ID for canonical URL resolution |

### Feed ID Values

The `categories` parameter selects the content feed:

| Value | Feed |
|---|---|
| 0 | Feed Off |
| 8 | Blog Only — US |
| 9 | Blog Only — UK/AU |
| 6 | Blog + Video — US |
| 5 | Blog + Video — UK |
| 7 | Blog + Video — AU |

## Module Behavior

### Settings

Provide an admin settings screen (under Orchard's Configuration area) with the following fields:

- **Licensed Email** — the email used to register with MSP Marketing Edge. Used for both license validation and feed authentication.
- **Feed Selection** — dropdown matching the feed ID values above.
- **Directly Publish** — boolean toggle. When enabled, imported posts are created as Published content items (live on the site immediately). When disabled, imported posts are created as Drafts for manual review before publishing. Default to disabled (drafts).
- **Notification Email** — optional; one or more comma-separated addresses to notify when new posts are imported. Leave blank to disable.
- **Twitter/X Handle** — optional; the site's @handle (e.g., `@ignitesolutions`). Used to populate `TwitterSite` and `TwitterCreator` on imported posts. Leave blank to omit.
- **Publisher Name** — optional; the organization name used in Google structured data (JSON-LD `publisher.name`). If blank, fall back to the Orchard site name.
- **Enable Google Structured Data** — boolean toggle; whether to generate a JSON-LD `Article` schema on imported posts. Default to enabled.

Store these as site-level settings using Orchard's `ISiteService` / site settings pattern.

### Scheduled Background Task

Implement an Orchard `IBackgroundTask` that runs periodically (approximately every 6 hours, matching the WordPress plugin's 4x/day schedule).

On each run:

1. Read the module settings. If the feed is set to "off" or the license email is blank, do nothing.
2. Call the license validation endpoint. If not licensed, do nothing.
3. Call the content feed endpoint with the configured feed ID and license email.
4. For each article in the response:
   - Check whether a content item with this GUID already exists (deduplication).
   - If **new**: create a Blog Post content item.
   - If **existing** and the upstream `send_update` flag is true and the `update_version` has changed: update the existing content item.
   - Otherwise: skip.

### Initialization / Lookback Behavior

The upstream API returns exactly **6 posts** regardless of parameters. Testing confirmed:

- Default response: 6 posts
- With `per_page=100`: still 6 posts
- With `page=2`: returns the same 6 posts (pagination is ignored server-side)

The API serves a rolling window that includes both **published and future-dated (scheduled) posts** — typically the most recent ~2 published posts plus ~4 upcoming weeks of scheduled content. This means the module will regularly encounter posts with `status: "future"` and dates weeks into the future.

On first activation, the module will import all 6 posts the API provides. Subsequent runs will only create new posts (via GUID deduplication) or update existing ones. There is no need for a configurable lookback depth, initialization setting, or pagination logic — just process whatever the API returns and let deduplication handle the rest.

### Content Item Creation

For each new article, create an Orchard Blog Post content item populated as follows:

- **Title**: from `title.rendered` (HTML-decode it)
- **Body**: from `content.rendered` (this is already HTML; store it in the HtmlBody part)
- **Excerpt/Summary**: from `excerpt.rendered` (strip HTML tags)
- **Slug**: from `slug`
- **Publish Date**: from `date`
- **Status**: If the **Directly Publish** toggle is enabled, create as Published. If disabled, create as Draft. Special case for future-dated posts: if the upstream `status` is `"future"` and the Directly Publish toggle is on, create the content item as a Draft but set the scheduled publish date on the `PublishLaterPart` to the upstream `date` value — Orchard's `PublishLater` background task will auto-publish it when that date arrives. This mirrors how WordPress handles its `"future"` post status natively. If the Directly Publish toggle is off, future-dated posts should just be created as Drafts with no scheduled date (the user wants full manual control). This is a common scenario; testing shows the API routinely returns ~4 future-dated posts alongside ~2 current/recent posts.
- **Comments**: disabled

### Metadata / Custom Fields

Store the following as custom fields on each imported content item (these support deduplication, update detection, and SEO behavior):

- `EdgeGuid` — the `guid.rendered` value; primary deduplication key
- `EdgePostId` — the upstream `id`
- `EdgeUrl` — the canonical URL (resolved from `acf.parent_page_url` if present, otherwise `link`)
- `EdgeUpdateVersion` — the `update_version` integer for change detection
- `EdgeWeekCommencing` — the `week_commencing` value
- `EdgeHasVideo` — boolean; true when the feed selection is one of the Blog + Video feeds (IDs 5, 6, 7)

### Canonical URL Resolution

When `acf.parent_page_url` is present and non-zero, the plugin resolves the real canonical URL by calling:

```
GET https://www.yourtechupdates.com/wp-json/tech/videos/{parentPostId}
```

This returns a JSON object with a `link` field. Use that as the canonical URL; fall back to the article's own `link` if this call fails or the field is absent.

### Featured Image

Download the image from `yoast_head_json.og_image[0].url` and store it in Orchard's media library via `IMediaFileStore`. Attach it to the content item as the featured/hero image. Handle missing or failed image downloads gracefully — don't fail the whole import over it.

### SEO Handling

Imported posts are syndicated content. To avoid duplicate content penalties, the module must set appropriate SEO metadata on each imported post. Orchard Core has a built-in SEO module (`OrchardCore.Seo`) that provides an `SeoMetaPart` — an attachable content part that gives per-content-item control over page title, meta description, canonical URL, meta robots, Open Graph, Twitter cards, and Google schema. The content type used for autoblog posts **must** have the `SeoMetaPart` attached.

The `SeoMetaPart` has the following JSON schema (from a real content item export):

```json
"SeoMetaPart": {
    "Render": true,
    "CustomMetaTags": [],
    "DefaultSocialImage": {
      "Paths": [],
      "MediaTexts": []
    },
    "OpenGraphImage": {
      "Paths": [],
      "MediaTexts": []
    },
    "TwitterImage": {
      "Paths": [],
      "MediaTexts": []
    },
    "PageTitle": null,
    "MetaDescription": null,
    "MetaKeywords": null,
    "Canonical": null,
    "MetaRobots": null,
    "OpenGraphType": null,
    "OpenGraphTitle": null,
    "OpenGraphDescription": null,
    "TwitterTitle": null,
    "TwitterDescription": null,
    "TwitterCard": null,
    "TwitterCreator": null,
    "TwitterSite": null,
    "GoogleSchema": null
}
```

When creating or updating an imported post, populate **all** `SeoMetaPart` fields programmatically. The upstream feed provides enough data to fill everything meaningfully.

#### Core SEO Fields

- **`Render`**: Set to `true` (required for the part to output anything).
- **`Canonical`**: Set to the resolved `EdgeUrl` value (the original source URL on yourtechupdates.com). This tells search engines where the original content lives.
- **`MetaRobots`**: Set to `"noindex, follow"`. This prevents search engines from indexing the syndicated copy while still allowing them to follow links within the content.
- **`MetaDescription`**: Populate from the post excerpt (strip HTML tags, trim to a reasonable length — 155-160 characters is the practical limit for search engine display).
- **`MetaKeywords`**: Can be left null. Keywords meta tags have no SEO value in modern search engines, but if the upstream content ever provides keywords, this is where they'd go.
- **`PageTitle`**: Set from the upstream `title.rendered` (HTML-decoded). Even though Orchard falls back to TitlePart, explicitly setting this ensures the SEO title is clean and independent of any display title formatting the theme might apply.

#### Open Graph Fields

These control how the post appears when shared on Facebook, LinkedIn, and other platforms that consume Open Graph tags.

- **`OpenGraphType`**: Set to `"article"`.
- **`OpenGraphTitle`**: Set from the upstream `title.rendered` (HTML-decoded).
- **`OpenGraphDescription`**: Set from the post excerpt (stripped of HTML, can be longer than MetaDescription — up to ~300 characters is fine for OG).
- **`OpenGraphImage`**: Set `Paths` to the Orchard media library path of the downloaded featured image (e.g., `["/autoblog/my-post-image.jpeg"]`), and set `MediaTexts` to a corresponding alt text array (e.g., the post title). If no featured image was successfully downloaded, leave both arrays empty.

#### Twitter Card Fields

These control how the post appears when shared on X/Twitter.

- **`TwitterCard`**: Set to `"summary_large_image"` when a featured image is available, or `"summary"` if no image was downloaded.
- **`TwitterTitle`**: Set from the upstream `title.rendered` (HTML-decoded).
- **`TwitterDescription`**: Set from the post excerpt (stripped of HTML, trimmed to ~200 characters).
- **`TwitterImage`**: Same as `OpenGraphImage` — set `Paths` and `MediaTexts` to the featured image in the media library if available, empty arrays otherwise.
- **`TwitterCreator`**: Set from the **Twitter/X Handle** module setting. Leave null if the setting is blank.
- **`TwitterSite`**: Set from the **Twitter/X Handle** module setting. Leave null if the setting is blank.

#### Social Image Fields

- **`DefaultSocialImage`**: Set `Paths` and `MediaTexts` to the featured image in the media library. This serves as the fallback social image when platform-specific images (OG, Twitter) are not set. Since we're setting all three, this is redundant but good practice for completeness.

#### Google Schema

- **`GoogleSchema`**: If the **Enable Google Structured Data** setting is on, set to a JSON-LD `Article` schema string built from the available upstream data:

```json
{
  "@context": "https://schema.org",
  "@type": "Article",
  "headline": "{title}",
  "description": "{excerpt}",
  "image": "{featured image URL on your site}",
  "datePublished": "{upstream date in ISO 8601}",
  "publisher": {
    "@type": "Organization",
    "name": "{from Publisher Name setting, or fall back to Orchard site name}"
  }
}
```

Store this as a serialized JSON string in the `GoogleSchema` field. If the setting is off, leave the field null.

#### Custom Meta Tags

- **`CustomMetaTags`**: Leave as an empty array `[]` unless there's a specific need. The canonical and robots directives already handle the syndication concerns.

By using the `SeoMetaPart`, all SEO behavior is handled through Orchard's built-in rendering pipeline — the module does not need to inject any custom HTML, override Liquid templates, or manipulate the `<head>` tag directly. The `OrchardCore.Seo` feature must be enabled on the site for this to work (it likely already is, but the module should document this as a dependency).

**Important**: The `SeoMetaPart` renders canonical and meta tags automatically when present on a content item. Do NOT also inject canonical/noindex tags through the theme or through custom resource manager calls — that would produce duplicate tags.

### Email Notification

After each import run that creates or updates one or more posts, send a summary email to the configured notification address(es). The email should list what was imported/updated with titles and edit links, and indicate whether posts were published live or created as drafts (based on the **Directly Publish** toggle). Keep it simple.

### Force Refresh

Provide an admin action (button on the settings page) that triggers an immediate import run outside of the background schedule. This is useful for initial setup and troubleshooting.

### Update Behavior

When updating an existing post (because `send_update` is true and `update_version` has changed):

- Update the title, body, excerpt, and featured image.
- Update the `EdgeUpdateVersion` metadata.
- Refresh all `SeoMetaPart` fields (meta description, OG title/description, Twitter title/description, images, Google schema) from the updated content. The canonical URL and meta robots should remain unchanged.
- Do not change the slug or publish date of the existing post.
- Respect the **Directly Publish** toggle for whether the updated post should be published or remain as a draft.

## Content Type / Data Migration

Create a code-based migration that:

- Defines a new content part (e.g., `AutoBlogPart`) to hold the Edge-specific metadata fields.
- Attaches it to the existing Blog Post content type, or creates a new content type if attaching to Blog Post would be disruptive — use judgment here based on how Blog Post is currently configured in the site. A separate `AutoBlogPost` content type that mirrors Blog Post but includes the AutoBlog metadata may be cleaner.
- Ensures the `SeoMetaPart` (from `OrchardCore.Seo`) is attached to the content type used for autoblog posts. This is required for the canonical URL and noindex behavior.
- Ensures the `PublishLaterPart` (from `OrchardCore.PublishLater`) is attached to the content type. This enables scheduled publishing for future-dated posts — when the Directly Publish toggle is on and a post has a future date, the module sets the scheduled publish date via this part so Orchard auto-publishes it when the date arrives.
- The module manifest should declare feature dependencies on both `OrchardCore.Seo` and `OrchardCore.PublishLater`.

## What NOT to Build

- No SocialTools integration. The WordPress plugin had a push-to-social feature that's been disabled (`upload_social_feed` returns immediately). Ignore it entirely.
- No self-update mechanism. The WordPress plugin had `meta_data_updater.php` for auto-updating itself. Orchard modules don't work that way. Skip it.
- No WordPress-specific SEO plugin workarounds (Yoast, Rank Math canonical overrides, OG image hacks). Those are WordPress concerns. We use Orchard's native `SeoMetaPart` instead.
- No custom `<head>` tag injection for SEO. The `SeoMetaPart` handles canonical and meta robots rendering automatically.

## Project Structure

The repo is `ignite-autoblog`. The solution is `IgniteSG.OrchardCore`. The module project is `IgniteSG.OrchardCore.MspMarketingEdge`.

Follow Orchard Core module conventions. The module should be a self-contained, cleanly organized project that can be enabled/disabled through the Orchard admin features screen.

Expected layout:

```
ignite-autoblog/
├── README.md
├── LICENSE (MIT)
├── .gitignore (VisualStudio template + ref/)
├── docs/
│   └── ignite-autoblog-module-brief.md
├── ref/                                        ← gitignored, local only
│   └── msp_marketing_edge_autoblog/
│       ├── meta_data.php
│       ├── meta_data_options.php
│       ├── meta_data_updater.php
│       └── readme.txt
└── src/
    ├── IgniteSG.OrchardCore.sln
    └── IgniteSG.OrchardCore.MspMarketingEdge/
        ├── IgniteSG.OrchardCore.MspMarketingEdge.csproj
        ├── Manifest.cs
        ├── Startup.cs
        └── ...
```

## README

Generate a `README.md` for the repo root that includes:

- **Project title**: IgniteSG.OrchardCore.MspMarketingEdge
- **One-liner**: An Orchard Core module that automatically imports weekly blog posts from MSP Marketing Edge into your Orchard CMS site.
- **What it does**: Brief description of the autoblog functionality — scheduled import, deduplication, featured images, draft/publish/scheduled publishing, full SEO metadata via SeoMetaPart.
- **Requirements**: Orchard Core 2.2.x, .NET 8, `OrchardCore.Seo` and `OrchardCore.PublishLater` features enabled.
- **Configuration**: Summary of the admin settings (licensed email, feed selection, directly publish toggle, notification email, Twitter handle, publisher name, structured data toggle).
- **How it works**: Brief explanation of the background task, API polling, content creation, and scheduled publishing for future-dated posts.
- **Installation**: Standard Orchard Core module installation steps (add project reference, enable feature in admin).
- **Disclaimer**: "This is an independent community project. It is not affiliated with or endorsed by MSP Marketing Edge. MSP Marketing Edge is a trademark of its respective owner."
- **License**: MIT

## Testing Considerations

- The feed endpoint is live and can be called with a valid licensed email. Use the settings UI to configure and trigger a force refresh to validate end-to-end.
- Handle upstream API failures gracefully — timeouts, malformed JSON, missing fields. Log warnings but don't throw. The background task should be resilient.
- Consider that the featured image URL may point to a large file or may 404. Handle both cases.

## Reference

The original WordPress plugin source files are available locally in `ref/msp_marketing_edge_autoblog/` (the `ref/` directory is gitignored — local development reference only, not distributed with the repo). The relevant files are:

- `meta_data.php` — core logic: API calls, content creation, image handling, scheduling
- `meta_data_options.php` — settings UI: feed selection, license email, post status, notifications
- `meta_data_updater.php` — self-updater for the WordPress plugin (ignore this one)
- `readme.txt` — WordPress plugin metadata and changelog

These files are GPL-licensed and are not part of this project — they are kept locally as API contract reference during development only.
