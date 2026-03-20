namespace Ignite.OrchardCore.MspMarketingEdge;

public static class Constants
{
    public const string Version = "1.6.0";
    public const string RepositoryUrl = "https://github.com/dsghi/ignite-autoblog";

    public const string ModuleId = "Ignite.OrchardCore.MspMarketingEdge";
    public const string ContentType = "AutoBlogPost";
    public const string AutoBlogPartName = "AutoBlogPart";
    public const string MediaFolder = "autoblog";

    public const string LicenseEndpoint = "https://www.mspmarketingedge.com/wp-content/keap/msp_edge_license.php";
    public const string FeedEndpoint = "https://www.yourtechupdates.com/wp-json/tech/videos/";
    public const string ParentPostEndpoint = "https://www.yourtechupdates.com/wp-json/wp/v2/posts/";

    public static readonly Dictionary<int, string> FeedOptions = new()
    {
        { 0, "Feed Off" },
        { 8, "Blog Only — US" },
        { 9, "Blog Only — UK/AU" },
        { 6, "Blog + Video — US" },
        { 5, "Blog + Video — UK" },
        { 7, "Blog + Video — AU" }
    };

    public static readonly HashSet<int> VideoFeeds = new() { 5, 6, 7 };
}
