# Ignite.OrchardCore.MspMarketingEdge

An Orchard Core module that automatically imports weekly blog posts from MSP Marketing Edge into your Orchard CMS site.

## What It Does

- **Scheduled import** — A background task polls the MSP Marketing Edge content feed every 6 hours and imports new blog posts as `AutoBlogPost` content items.
- **Deduplication** — Each imported post is tracked by its upstream GUID to prevent duplicates.
- **Update detection** — When the upstream service flags a post for update with a new version number, the module updates the existing content item.
- **Featured images** — Downloads and stores featured images in the Orchard media library under `/autoblog/`.
- **Draft or publish** — Choose whether imported posts go live immediately or are created as drafts for manual review.
- **Scheduled publishing** — Future-dated posts from the feed are created as drafts with a scheduled publish date via `PublishLaterPart`, so Orchard auto-publishes them when the date arrives.
- **Full SEO metadata** — Populates `SeoMetaPart` on every imported post: canonical URL (pointing to the original source), `noindex, follow` robots directive, Open Graph tags, Twitter Card tags, and optional Google JSON-LD structured data.
- **Email notifications** — Sends a summary email when new posts are imported or updated.
- **Force refresh** — An admin button triggers an immediate import outside of the background schedule.

## Requirements

- Orchard Core 2.2.x
- .NET 8
- The following Orchard Core features must be enabled:
  - `OrchardCore.Seo`
  - `OrchardCore.PublishLater`
  - `OrchardCore.Email` (with SMTP configured)
  - `OrchardCore.Media`

## Configuration

After enabling the module, navigate to **Configuration → MSP Marketing Edge AutoBlog** in the Orchard admin. The settings page provides:

| Setting | Description |
|---|---|
| **Licensed Email** | The email used to register with MSP Marketing Edge. Used for license validation and feed authentication. |
| **Feed Selection** | Choose the content feed: Blog Only (US/UK/AU) or Blog + Video (US/UK/AU). Set to "Feed Off" to disable. |
| **Directly Publish** | When enabled, imported posts are published immediately. When disabled, they are created as drafts. Default: disabled. |
| **Notification Email** | Comma-separated addresses to notify when posts are imported. Leave blank to disable. |
| **Twitter/X Handle** | Your organization's @handle for Twitter Card metadata. Leave blank to omit. |
| **Publisher Name** | Organization name for Google structured data. Falls back to the Orchard site name if blank. |
| **Enable Google Structured Data** | Toggle JSON-LD Article schema generation on imported posts. Default: enabled. |

## How It Works

1. The background task runs every 6 hours (configurable via Orchard's Background Tasks admin).
2. It validates the license email against the MSP Marketing Edge license API.
3. It fetches the content feed (the API returns ~6 posts: a mix of recent published and upcoming scheduled posts).
4. For each article, it checks for an existing `AutoBlogPost` with a matching GUID.
5. New posts are created with title, body, slug, featured image, and full SEO metadata.
6. Existing posts are updated only when the upstream `send_update` flag is set and the `update_version` has changed.
7. A notification email is sent if any posts were created or updated.

## Installation

1. Add the `Ignite.OrchardCore.MspMarketingEdge` project to your Orchard Core solution.
2. Reference it from your web project.
3. Enable the **MSP Marketing Edge AutoBlog** feature in the Orchard admin (Configuration → Features).
4. Configure the module settings under Configuration → MSP Marketing Edge AutoBlog.
5. Use the **Run Import Now** button to verify the connection and trigger an initial import.

## Disclaimer

This is an independent community project. It is not affiliated with or endorsed by MSP Marketing Edge. MSP Marketing Edge is a trademark of its respective owner.

## License

MIT
