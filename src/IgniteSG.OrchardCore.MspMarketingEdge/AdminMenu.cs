using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;

namespace IgniteSG.OrchardCore.MspMarketingEdge;

public class AdminMenu : INavigationProvider
{
    private readonly IStringLocalizer S;

    public AdminMenu(IStringLocalizer<AdminMenu> localizer)
    {
        S = localizer;
    }

    public ValueTask BuildNavigationAsync(string name, NavigationBuilder builder)
    {
        if (!string.Equals(name, "admin", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.CompletedTask;
        }

        builder
            .Add(S["Configuration"], configuration => configuration
                .Add(S["MSP Marketing Edge AutoBlog"], S["MSP Marketing Edge AutoBlog"].PrefixPosition(), autoblog => autoblog
                    .AddClass("autoblog")
                    .Id("autoblog")
                    .Permission(Permissions.ManageAutoBlogSettings)
                    .Action("Index", "Admin", new RouteValueDictionary
                    {
                        { "area", "OrchardCore.Settings" },
                        { "groupId", Drivers.AutoBlogSettingsDisplayDriver.GroupId }
                    })
                    .LocalNav()
                )
            );

        return ValueTask.CompletedTask;
    }
}
