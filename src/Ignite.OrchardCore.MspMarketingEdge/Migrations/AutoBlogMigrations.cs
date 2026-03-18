using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Data.Migration;
using OrchardCore.Autoroute.Models;

namespace Ignite.OrchardCore.MspMarketingEdge.Migrations;

public class AutoBlogMigrations : DataMigration
{
    private readonly IContentDefinitionManager _contentDefinitionManager;

    public AutoBlogMigrations(IContentDefinitionManager contentDefinitionManager)
    {
        _contentDefinitionManager = contentDefinitionManager;
    }

    public async Task<int> CreateAsync()
    {
        await _contentDefinitionManager.AlterPartDefinitionAsync(Constants.AutoBlogPartName, part => part
            .WithDescription("Stores MSP Marketing Edge metadata for imported blog posts.")
            .WithField("EdgeGuid", field => field
                .OfType("TextField")
                .WithDisplayName("Edge GUID")
                .WithDescription("Upstream GUID for deduplication.")
                .WithSettings(new { Hint = "Do not modify. Used for deduplication." })
            )
            .WithField("EdgePostId", field => field
                .OfType("NumericField")
                .WithDisplayName("Edge Post ID")
                .WithDescription("Upstream post ID.")
            )
            .WithField("EdgeUrl", field => field
                .OfType("TextField")
                .WithDisplayName("Edge URL")
                .WithDescription("Canonical URL from the upstream source.")
            )
            .WithField("EdgeUpdateVersion", field => field
                .OfType("NumericField")
                .WithDisplayName("Edge Update Version")
                .WithDescription("Version number for change detection.")
            )
            .WithField("EdgeWeekCommencing", field => field
                .OfType("TextField")
                .WithDisplayName("Week Commencing")
                .WithDescription("Week commencing date from upstream.")
            )
            .WithField("EdgeHasVideo", field => field
                .OfType("BooleanField")
                .WithDisplayName("Has Video")
                .WithDescription("Whether this post is from a Blog + Video feed.")
            )
        );

        await _contentDefinitionManager.AlterTypeDefinitionAsync(Constants.ContentType, type => type
            .DisplayedAs("AutoBlog Post")
            .Creatable()
            .Listable()
            .Draftable()
            .Versionable()
            .WithPart("TitlePart", part => part
                .WithPosition("0")
            )
            .WithPart("AutoroutePart", part => part
                .WithPosition("1")
                .WithSettings(new AutoroutePartSettings
                {
                    Pattern = "{{ ContentItem | display_text | slugify }}",
                    AllowCustomPath = true,
                    ShowHomepageOption = false
                })
            )
            .WithPart("HtmlBodyPart", part => part
                .WithDisplayName("Body")
                .WithPosition("2")
                .WithEditor("Wysiwyg")
            )
            .WithPart(Constants.AutoBlogPartName, part => part
                .WithPosition("3")
            )
            .WithPart("SeoMetaPart", part => part
                .WithPosition("4")
            )
            .WithPart("PublishLaterPart", part => part
                .WithPosition("5")
            )
        );

        return 1;
    }
}
