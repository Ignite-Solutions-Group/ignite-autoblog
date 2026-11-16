# IgniteSG.OrchardCore.MspMarketingEdge

An Orchard Core module that automatically imports weekly blog posts from [MSP Marketing Edge](https://www.mspmarketingedge.com/) into your Orchard CMS site.

## Features

- **Scheduled import** — A background task polls the MSP Marketing Edge content feed every 6 hours and imports new blog posts as `AutoBlogPost` content items.
- **Deduplication** — Each imported post is tracked by its upstream GUID in a dedicated index. A slug-based fallback lookup catches posts that may have been created manually or survived an index rebuild, and backfills the GUID automatically.
- **Index reconciliation** — On each run the module reconciles the GUID index against existing content, so database recovery or index rebuilds don't cause duplicate imports.
- **Update detection** — When the upstream service flags a post for update with a new version number, the module updates the existing content item in place.
- **Featured images** — Downloads and stores featured images in the Orchard media library under `/autoblog/`.
- **Draft or publish** — Choose whether imported posts go live immediately or are created as drafts for manual review.
- **Scheduled publishing** — Future-dated posts from the feed are created as drafts with a scheduled publish date via `PublishLaterPart`, so Orchard auto-publishes them when the date arrives.
- **Full SEO metadata** — Populates `SeoMetaPart` on every imported post: canonical URL (pointing to the original source), `noindex, follow` robots directive, Open Graph tags, Twitter Card tags, and optional Google JSON-LD structured data.
- **HTML sanitization** — All external HTML from the feed is sanitized before storage to prevent stored XSS attacks.
- **Blog container support** — Imported posts can be linked to a Blog container via `ContainedPart`, so they automatically appear in List-based blog pages.
- **Estimated reading time** — Templates calculate and display an estimated reading time (words / 200, minimum 1 minute).
- **Video feed detection** — Posts from video feeds automatically hide the featured image in the detail template, since the video is embedded in the body.
- **Editable created date** — The original publication date is preserved from the feed and exposed in the admin editor for manual adjustment.
- **Email notifications** — Sends a summary email when new posts are imported or updated.
- **Sync status dashboard** — The admin settings page displays the last sync time, success/failure status, and a summary of what was imported.
- **Force refresh** — An admin button triggers an immediate import outside of the background schedule.

## Requirements

- Orchard Core **4.0.0-preview-19227** (this exact build - see below)
- .NET 11
- An active MSP Marketing Edge subscription and its licensed email address
- The following Orchard Core features must be enabled:
  - `OrchardCore.Seo`
  - `OrchardCore.PublishLater`
  - `OrchardCore.Email` (with SMTP configured)
  - `OrchardCore.Media`

### Version compatibility

| Module version | Orchard Core | .NET runtime |
|---|---|---|
| 2.0.0-preview.1 | 4.0.0-preview-19227 (exact) | .NET 11 |
| 1.7.2 | 3.0.1 | .NET 10 |
| 1.6.6 | 2.2.1 | .NET 8 |

Each line keeps its own compatibility promise. The 2.x line is for Orchard Core 4; the 1.7.x line stays the Orchard Core 3 line and is not replaced by it. Applications on Orchard Core 3 and .NET 10 should keep module 1.7.2, and applications on Orchard Core 2 and .NET 8 should keep module 1.6.6. Upgrade the host application's Orchard Core packages and .NET target before moving to a newer module line.

#### Orchard Core 4 is a preview

Orchard Core 4 has not been released. Module 2.0.0-preview.1 is built and verified against one nightly build, `4.0.0-preview-19227`, and its package dependencies name that build exactly rather than "or later". A host on a different nightly gets a NuGet restore conflict instead of a module that loads against API it was never tested with. Moving to another Orchard Core 4 build means a new module prerelease.

That build is published only on Orchard Core's preview feed, so the host needs it as a package source next to nuget.org:

```xml
<packageSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="orchard-preview" value="https://nuget.cloudsmith.io/orchardcore/preview/v3/index.json" />
</packageSources>
```

Nightly packages are not kept on that feed forever. A site that depends on one should keep its own copy of the packages it restores.

The module stores images through Orchard's media store as the files it downloaded and does no image processing of its own, so it adds no ImageSharp dependency and works with Orchard Core 4's native media pipeline.

#### Upgrading a site from Orchard Core 3

Existing content is used as it is. Content items, the `AutoBlogPost` type, `AutoBlogPart` data, settings, the `ManageAutoBlogSettings` permission, media paths and the GUID index keep their names and stored values; there is no new data migration and nothing is re-imported. Things that do change:

- The settings page moves from **Configuration** to **Settings → MSP Marketing Edge AutoBlog**, following Orchard Core 4's admin menu.
- A role that holds only `ManageAutoBlogSettings` can open that page without the broader site-settings permission.
- The default detail template now hides the featured image on video-feed posts as documented. Earlier versions always showed it.

Building from source requires .NET SDK **11.0.100** or a later stable .NET 11 feature band, as selected by `global.json`.

## Installation

### NuGet (recommended)

For Orchard Core 4.0.0-preview-19227 / .NET 11, install the 2.0.0-preview.1 prerelease once it is available on NuGet:

```shell
dotnet add package IgniteSG.OrchardCore.MspMarketingEdge --version 2.0.0-preview.1
```

Or via the Package Manager Console:

```powershell
Install-Package IgniteSG.OrchardCore.MspMarketingEdge -Version 2.0.0-preview.1
```

For an Orchard Core 3.0.1 / .NET 10 host, use version `1.7.2` instead, and for an Orchard Core 2.2.1 / .NET 8 host use `1.6.6`.

### Project reference

1. Add the `IgniteSG.OrchardCore.MspMarketingEdge` project to your Orchard Core solution.
2. Reference it from your web project.

To build the module from the repository root:

```shell
dotnet build src/IgniteSG.OrchardCore.sln --configuration Release
```

### Enable the feature

1. In the Orchard admin, open the **Features** page and enable **MSP Marketing Edge AutoBlog**.
2. Configure the module under **Settings → MSP Marketing Edge AutoBlog**.
3. Use the **Run Import Now** button to verify the connection and trigger an initial import.

## Permissions

The module registers a single permission:

| Permission | Description |
|---|---|
| `ManageAutoBlogSettings` | Access the AutoBlog settings page and trigger manual imports. Granted to the Administrator role by default. |

## Configuration

After enabling the module, navigate to **Settings → MSP Marketing Edge AutoBlog** in the Orchard admin. The settings page provides:

| Setting | Description |
|---|---|
| **Licensed Email** | The email used to register with MSP Marketing Edge. Used for license validation and feed authentication. |
| **Feed Selection** | Choose the content feed (see table below). Set to "Feed Off" to disable imports. |
| **Directly Publish** | When enabled, imported posts are published immediately. When disabled, they are created as drafts. Default: disabled. |
| **Notification Email** | Comma-separated addresses to notify when posts are imported. Leave blank to disable. |
| **Twitter/X Handle** | Your organization's @handle for Twitter Card metadata. Leave blank to omit. |
| **Publisher Name** | Organization name for Google structured data. Falls back to the Orchard site name if blank. |
| **Enable Google Structured Data** | Toggle JSON-LD Article schema generation on imported posts. Default: enabled. |
| **Blog Container** | An existing content item with a ListPart to contain imported posts. Leave unselected to import posts without a container. |

### Feed options

| Option | Description |
|---|---|
| Feed Off | Disables the import |
| Blog Only — US | Blog articles only, US English |
| Blog Only — UK/AU | Blog articles only, UK/AU English |
| Blog + Video — US | Blog articles and video posts, US English |
| Blog + Video — UK | Blog articles and video posts, UK English |
| Blog + Video — AU | Blog articles and video posts, AU English |

### Recommended site configuration

For proper date and time display (localized format with AM/PM), enable **OrchardCore.Localization** and set a default culture (e.g. `en-US`, `en-GB`, or `en-AU`) in the site's localization settings. Also verify your site timezone in the general settings. The module works without this, but dates will display in UTC with invariant formatting.

## How It Works

1. The background task runs every 6 hours (configurable via Orchard's Background Tasks admin).
2. It validates the license email against the MSP Marketing Edge license API.
3. It fetches the content feed (the API returns ~6 posts: a mix of recent published and upcoming scheduled posts).
4. For each article, it checks for an existing `AutoBlogPost` by GUID index, falling back to a slug-based lookup if needed.
5. New posts are created with title, body, slug, featured image, and full SEO metadata. External HTML is sanitized before storage.
6. Existing posts are updated only when the upstream `send_update` flag is set and the `update_version` has changed.
7. A notification email is sent if any posts were created or updated.

## Templates

The module includes default Liquid templates for rendering `AutoBlogPost` content items:

- **`Content-AutoBlogPost.liquid`** — Detail view (single article page). Renders the title, publish date, estimated reading time, featured image (hidden automatically for video feeds where the video is embedded in the body), full article body, and a link back to the original source.
- **`Content_Summary-AutoBlogPost.liquid`** — Summary view (article lists). Renders a Bootstrap card with featured image thumbnail, title, date, estimated reading time, excerpt, and a "Read more" link.

These templates use Bootstrap 5 classes and work out of the box. To customize them, copy the template file into your theme's `Views/` folder and modify it there — Orchard's shape resolution will pick up your theme's version over the module's default.

### Listing AutoBlog Posts

To display a list of imported posts on a page, you can use Orchard's built-in Liquid query support. For example, in a Liquid widget or template:

```liquid
{% assign posts = ContentItem | query: type: 'AutoBlogPost', sort: 'PublishedUtc', order: 'desc', count: 10 %}
{% for post in posts %}
    {{ post | shape_build_display: 'Summary' | shape_render }}
{% endfor %}
```

Alternatively, create a **List** content item in the Orchard admin that queries the `AutoBlogPost` content type — this gives you pagination and filtering without any template changes.

### Combining with existing Blog Posts

If your site already has a blog page that lists `BlogPost` items and you want imported `AutoBlogPost` items to appear alongside them, update your blog list template to query both content types.

## Verifying changes

`tests/VerifyEditorial` runs the module on a real, disposable Orchard Core site with a SQLite database. The vendor endpoints, image downloads and notification email are in-process stand-ins, so nothing leaves the machine and no licence is needed.

```shell
# A new site: setup, migrations, imports, permissions, templates and media
dotnet run --project tests/VerifyEditorial -c Release

# The same run with media in Azure Blob Storage (expects the Azurite emulator, or a connection
# string in AUTOBLOG_FIXTURE_AZURE_STORAGE)
dotnet run --project tests/VerifyEditorial -c Release -- --azure-media

# An existing site: write an Orchard Core 3 database with the released module, then open it on this build
dotnet run --project tests/SeedOrchard3 -c Release -- ./artifacts/seed-orchard3
dotnet run --project tests/VerifyEditorial -c Release -- --upgrade-from ./artifacts/seed-orchard3
```

The first run after a build can take several minutes to start on a workstation whose security policy inspects each newly built assembly as it loads; later runs start in seconds. The fixture waits on the site rather than on a fixed timeout, and `AUTOBLOG_FIXTURE_STARTUP_MINUTES` (default 30) changes how long it waits. Pull requests and releases run all of the above.

## Changelog

### v2.0.0-preview.1
- Targets Orchard Core 4.0.0-preview-19227 and .NET 11, with exact Orchard Core package versions
- Registers the settings group permission, so a role holding only `ManageAutoBlogSettings` can manage the settings
- Moves the settings page to Orchard Core 4's **Settings** menu and updates the permission to the localizable description API
- Fixes the default detail template so video-feed posts no longer show the featured image
- Fixes the settings link in notification emails, which pointed at a page that does not exist
- Fixes the slug fallback for posts inside a blog container, so a matching hand-made post is adopted instead of duplicated
- Stores downloaded images under the same file name on Windows and Linux hosts
- Takes the manifest version from `Constants.Version`, so the two can no longer disagree
- Verifies every pull request and release against a new site, Azure Blob media and an upgraded Orchard Core 3 database

### v1.7.2
- Generates valid `Article` structured data (`@context`, `@type`, publisher type), encoded so it cannot close its own script element
- Keeps the original publication date and uses the absolute public image URL in structured data
- Updates Cronos and patches a transitive cryptography dependency

### v1.7.1
- Adds **Preserve local editorial content**, which holds vendor updates back from an article an editor has taken over

### v1.7.0
- Targets Orchard Core 3.0.1 and .NET 10
- Updates HTML notification emails to the Orchard Core 3 email API
- Migrates obsolete content-part, site-settings, and display-name APIs
- Updates build and release workflows to use the stable .NET 10 SDK
- Documents version compatibility and installation requirements

### v1.6.0
- Added estimated reading time to blog post templates
- Exposed editable created date on AutoBlogPost content type
- Fixed MySQL syntax error in slug lookup and misleading error messages
- Fixed slug fallback to correctly match existing posts during import

### v1.5.0
- Added slug-based fallback lookup for deduplication resilience
- Added GUID index reconciliation on each import run

### v1.1.0
- Initial public release with scheduled import, SEO metadata, email notifications, and template support

## Disclaimer

This is an independent community project. It is not affiliated with or endorsed by MSP Marketing Edge. MSP Marketing Edge is a trademark of its respective owner.

## License

[MIT](https://opensource.org/licenses/MIT)
