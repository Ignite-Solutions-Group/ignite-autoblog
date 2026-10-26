using IgniteSG.OrchardCore.MspMarketingEdge.Drivers;
using IgniteSG.OrchardCore.MspMarketingEdge.Indexes;
using IgniteSG.OrchardCore.MspMarketingEdge.Migrations;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using IgniteSG.OrchardCore.MspMarketingEdge.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.BackgroundTasks;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Display.ContentDisplay;
using OrchardCore.Data.Migration;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.Security.Permissions;
using OrchardCore.Settings;
using YesSql.Indexes;

namespace IgniteSG.OrchardCore.MspMarketingEdge;

public class Startup : StartupBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // Content part
        services.AddContentPart<AutoBlogPart>().UseDisplayDriver<AutoBlogPartDisplayDriver>();

        // Data migration
        services.AddScoped<IDataMigration, AutoBlogMigrations>();

        // Site settings
        services.AddScoped<IDisplayDriver<ISite>, AutoBlogSettingsDisplayDriver>();

        // Navigation
        services.AddScoped<INavigationProvider, AdminMenu>();

        // Permissions
        services.AddScoped<IPermissionProvider, Permissions>();

        // Services
        services.AddScoped<IEdgeApiService, EdgeApiService>();
        services.AddScoped<IImageService, ImageService>();
        services.AddScoped<IAutoBlogImportService, AutoBlogImportService>();

        // Indexes
        services.AddSingleton<IIndexProvider, AutoBlogPostIndexProvider>();

        // HttpClient
        services.AddHttpClient("EdgeApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "IgniteSG.OrchardCore.MspMarketingEdge/1.0");
        });

        // Background task
        services.AddSingleton<IBackgroundTask, BackgroundTasks.AutoBlogBackgroundTask>();
    }

    public override void Configure(IApplicationBuilder app, IEndpointRouteBuilder routes, IServiceProvider serviceProvider)
    {
        routes.MapAreaControllerRoute(
            name: "AutoBlogForceRefresh",
            areaName: "IgniteSG.OrchardCore.MspMarketingEdge",
            pattern: "admin/autoblog/force-refresh",
            defaults: new { controller = "Admin", action = "ForceRefresh" }
        );
    }
}
