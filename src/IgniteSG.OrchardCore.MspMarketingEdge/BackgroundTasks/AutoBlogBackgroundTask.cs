using IgniteSG.OrchardCore.MspMarketingEdge.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrchardCore.BackgroundTasks;

namespace IgniteSG.OrchardCore.MspMarketingEdge.BackgroundTasks;

[BackgroundTask(
    Schedule = "0 */6 * * *",
    Description = "Imports blog posts from MSP Marketing Edge every 6 hours.")]
public class AutoBlogBackgroundTask : IBackgroundTask
{
    public async Task DoWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<AutoBlogBackgroundTask>>();
        var importService = serviceProvider.GetRequiredService<IAutoBlogImportService>();

        try
        {
            logger.LogInformation("AutoBlog background task starting.");
            await importService.RunImportAsync();
            logger.LogInformation("AutoBlog background task completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutoBlog background task failed.");
        }
    }
}
