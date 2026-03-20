using OrchardCore.Security.Permissions;

namespace IgniteSG.OrchardCore.MspMarketingEdge;

public class Permissions : IPermissionProvider
{
    public static readonly Permission ManageAutoBlogSettings = new(
        "ManageAutoBlogSettings",
        "Manage MSP Marketing Edge AutoBlog settings");

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
