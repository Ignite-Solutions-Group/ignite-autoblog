using Ignite.OrchardCore.MspMarketingEdge.Indexes;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Settings;
using OrchardCore.Data.Migration;
using OrchardCore.Autoroute.Models;
using OrchardCore.Media.Settings;

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
        // AutoBlogPart — code-only properties, no content fields
        await _contentDefinitionManager.AlterPartDefinitionAsync(Constants.AutoBlogPartName, part => part
            .WithDescription("Stores MSP Marketing Edge metadata for imported blog posts.")
        );

        // Implicit part for content fields (FeaturedImage)
        await _contentDefinitionManager.AlterPartDefinitionAsync(Constants.ContentType, part => part
            .WithField("FeaturedImage", field => field
                .OfType("MediaField")
                .WithDisplayName("Featured Image")
                .WithSettings(new MediaFieldSettings
                {
                    Multiple = false,
                    AllowMediaText = true
                })
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
            .WithPart(Constants.ContentType, part => part
                .WithPosition("6")
            )
        );

        // YesSql index for efficient GUID lookups
        await SchemaBuilder.CreateMapIndexTableAsync(typeof(AutoBlogPostIndex), table => table
            .Column<string>(nameof(AutoBlogPostIndex.EdgeGuid), col => col.WithLength(255))
        , collection: null);

        await SchemaBuilder.AlterIndexTableAsync(typeof(AutoBlogPostIndex), table => table
            .CreateIndex("IDX_AutoBlogPostIndex_EdgeGuid",
                nameof(AutoBlogPostIndex.EdgeGuid))
        , collection: null);

        return 1;
    }
}
