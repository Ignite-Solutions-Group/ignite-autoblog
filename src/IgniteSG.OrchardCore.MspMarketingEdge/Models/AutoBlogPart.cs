using OrchardCore.ContentManagement;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Models;

public class AutoBlogPart : ContentPart
{
    public string EdgeGuid { get; set; } = string.Empty;
    public int EdgePostId { get; set; }
    public string EdgeUrl { get; set; } = string.Empty;
    public int EdgeUpdateVersion { get; set; }
    public string EdgeWeekCommencing { get; set; } = string.Empty;
    public bool EdgeHasVideo { get; set; }
}
