using OrchardCore.ContentManagement;
using YesSql.Indexes;

namespace Ignite.OrchardCore.MspMarketingEdge.Indexes;

public class AutoBlogPostIndex : MapIndex
{
    public string? EdgeGuid { get; set; }
}

public class AutoBlogPostIndexProvider : IndexProvider<ContentItem>
{
    public override void Describe(DescribeContext<ContentItem> context)
    {
        context.For<AutoBlogPostIndex>()
            .When(contentItem => contentItem.ContentType == Constants.ContentType)
            .Map(contentItem =>
            {
                var part = contentItem.As<Models.AutoBlogPart>();
                if (part == null || string.IsNullOrWhiteSpace(part.EdgeGuid))
                {
                    return null!;
                }

                return new AutoBlogPostIndex
                {
                    EdgeGuid = part.EdgeGuid
                };
            });
    }
}
