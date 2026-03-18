using OrchardCore.Modules.Manifest;

[assembly: Module(
    Name = "MSP Marketing Edge AutoBlog",
    Author = "Ignite Solutions",
    Website = "https://ignitesolutions.co.uk",
    Version = "1.0.0",
    Description = "Automatically imports weekly blog posts from MSP Marketing Edge into your Orchard CMS site.",
    Category = "Content"
)]

[assembly: Feature(
    Id = "Ignite.OrchardCore.MspMarketingEdge",
    Name = "MSP Marketing Edge AutoBlog",
    Description = "Scheduled import of syndicated blog posts from MSP Marketing Edge with full SEO metadata.",
    Dependencies = new[]
    {
        "OrchardCore.ContentManagement",
        "OrchardCore.Title",
        "OrchardCore.Html",
        "OrchardCore.Autoroute",
        "OrchardCore.Seo",
        "OrchardCore.PublishLater",
        "OrchardCore.Media",
        "OrchardCore.Email"
    },
    Category = "Content"
)]
