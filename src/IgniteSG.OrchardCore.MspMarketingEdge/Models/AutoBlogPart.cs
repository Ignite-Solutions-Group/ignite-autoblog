using OrchardCore.ContentManagement;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Models;

public class AutoBlogPart : ContentPart
{
    /// <summary>Skip vendor updates while an editor maintains this article locally.</summary>
    public bool PreserveLocalEdits { get; set; }
    public string EdgeGuid { get; set; } = string.Empty;
    public int EdgePostId { get; set; }
    public string EdgeUrl { get; set; } = string.Empty;
    public int EdgeUpdateVersion { get; set; }
    public string EdgeWeekCommencing { get; set; } = string.Empty;
    public bool EdgeHasVideo { get; set; }
}
