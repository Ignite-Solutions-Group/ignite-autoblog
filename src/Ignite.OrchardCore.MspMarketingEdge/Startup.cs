using Ignite.OrchardCore.MspMarketingEdge.Drivers;
using Ignite.OrchardCore.MspMarketingEdge.Migrations;
using Ignite.OrchardCore.MspMarketingEdge.Models;
using Ignite.OrchardCore.MspMarketingEdge.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.BackgroundTasks;
using OrchardCore.ContentManagement;
using OrchardCore.Data.Migration;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.Modules;
using OrchardCore.Navigation;
using OrchardCore.Security.Permissions;
using OrchardCore.Settings;

namespace Ignite.OrchardCore.MspMarketingEdge;

public class Startup : StartupBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        // Content part
        services.AddContentPart<AutoBlogPart>();

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

        // HttpClient
        services.AddHttpClient("EdgeApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Ignite.OrchardCore.MspMarketingEdge/1.0");
        });

        // Background task
        services.AddSingleton<IBackgroundTask, BackgroundTasks.AutoBlogBackgroundTask>();
    }

    public override void Configure(IApplicationBuilder app, IEndpointRouteBuilder routes, IServiceProvider serviceProvider)
    {
        routes.MapAreaControllerRoute(
            name: "AutoBlogForceRefresh",
            areaName: "Ignite.OrchardCore.MspMarketingEdge",
            pattern: "admin/autoblog/force-refresh",
            defaults: new { controller = "Admin", action = "ForceRefresh" }
        );
    }
}
