using OrchardCore.Localization;
using OrchardCore.Security.Permissions;

namespace IgniteSG.OrchardCore.MspMarketingEdge;

public class Permissions : IPermissionProvider
{
    // The name is the stored identity that roles reference; only the description is localizable.
    public static readonly Permission ManageAutoBlogSettings = new(
        "ManageAutoBlogSettings",
        LocalizationSource.Create("Manage MSP Marketing Edge AutoBlog settings", typeof(Permissions)));

    public Task<IEnumerable<Permission>> GetPermissionsAsync()
    {
        return Task.FromResult<IEnumerable<Permission>>(new[]
        {
            ManageAutoBlogSettings
        });
    }

    public IEnumerable<PermissionStereotype> GetDefaultStereotypes()
    {
        return new[]
        {
            new PermissionStereotype
            {
                Name = "Administrator",
                Permissions = new[] { ManageAutoBlogSettings }
            }
        };
    }
}
