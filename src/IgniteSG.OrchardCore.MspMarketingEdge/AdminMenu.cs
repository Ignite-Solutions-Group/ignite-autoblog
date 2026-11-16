using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using OrchardCore.Navigation;

namespace IgniteSG.OrchardCore.MspMarketingEdge;

public sealed class AdminMenu : AdminNavigationProvider
{
    private static readonly RouteValueDictionary s_routeValues = new()
    {
        { "area", "OrchardCore.Settings" },
        { "groupId", Drivers.AutoBlogSettingsDisplayDriver.GroupId },
    };

    private readonly IStringLocalizer S;

    public AdminMenu(IStringLocalizer<AdminMenu> localizer)
    {
        S = localizer;
    }

    protected override ValueTask BuildAsync(NavigationBuilder builder)
    {
        // Orchard Core 4 has a single top-level "Settings" menu; the older "Configuration" menu is gone.
        builder
            .Add(S["Settings"], settings => settings
                .Add(S["MSP Marketing Edge AutoBlog"], S["MSP Marketing Edge AutoBlog"].PrefixPosition(), autoblog => autoblog
                    .AddClass("autoblog")
                    .Id("autoblog")
                    .Action("Index", "Admin", s_routeValues)
                    .Permission(Permissions.ManageAutoBlogSettings)
                    .LocalNav()
                )
            );

        return ValueTask.CompletedTask;
    }
}
