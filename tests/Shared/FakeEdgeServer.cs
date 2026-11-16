using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;

/// <summary>
/// An in-memory stand-in for the MSP Marketing Edge endpoints. It is installed as the primary handler of the
/// module's named HttpClient, so the module's real API and image services run against it and nothing ever
/// leaves the process. A request to any address it does not recognise throws, which fails the fixture.
/// </summary>
public sealed class FakeEdgeServer
{
    public const string LicenseHost = "www.mspmarketingedge.com";
    public const string FeedHost = "www.yourtechupdates.com";
    public const string ImageHost = "images.fixture.invalid";

    // A valid 1x1 PNG, so a stored image is a real image file.
    public static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly object _gate = new();
    private readonly List<Uri> _requests = [];
    private readonly List<string> _unexpected = [];

    /// <summary>The value of license_activated; the real service returns the strings "true" and "false".</summary>
    public string LicenseActivated { get; set; } = "true";
    public HttpStatusCode LicenseStatus { get; set; } = HttpStatusCode.OK;
    /// <summary>Simulates a connection failure before any response.</summary>
    public bool LicenseUnreachable { get; set; }

    public List<FakeArticle> Articles { get; } = [];
    public HttpStatusCode FeedStatus { get; set; } = HttpStatusCode.OK;
    /// <summary>When set, returned verbatim instead of the article list (for malformed-response checks).</summary>
    public string? FeedBody { get; set; }

    /// <summary>Canonical links returned for acf.parent_page_url lookups, keyed by parent post id.</summary>
    public Dictionary<int, string> ParentLinks { get; } = [];
    /// <summary>Image paths (as requested) that answer 404.</summary>
    public HashSet<string> MissingImages { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Uri> Requests { get { lock (_gate) return _requests.ToArray(); } }
    public int Count(string host) => Requests.Count(r => r.Host == host);
    public int FeedRequests => Requests.Count(r => r.Host == FeedHost && r.AbsolutePath == "/wp-json/tech/videos/");
    public void ClearRequests() { lock (_gate) _requests.Clear(); }

    /// <summary>
    /// Requests this server refused. The module catches the exception a refusal throws and only logs a
    /// warning, so the refusal is also kept here, never cleared, for the host to assert on before it passes.
    /// </summary>
    public IReadOnlyList<string> UnexpectedRequests { get { lock (_gate) return _unexpected.ToArray(); } }

    private InvalidOperationException Refuse(string what)
    {
        lock (_gate) _unexpected.Add(what);
        return new InvalidOperationException("Unexpected " + what);
    }

    public static string ImageUrl(string name) => $"https://{ImageHost}/{name}";

    /// <summary>
    /// Each HttpClient the factory builds gets its own thin handler over this shared state, because the factory
    /// disposes the handlers it rotates out.
    /// </summary>
    public HttpMessageHandler CreateHandler() => new Handler(this);

    private Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request without an address");
        lock (_gate) _requests.Add(uri);

        if (request.Method != HttpMethod.Get)
            throw Refuse($"{request.Method} request to {uri.Host}{uri.AbsolutePath}");

        if (uri.Host == LicenseHost && uri.AbsolutePath == "/wp-content/keap/msp_edge_license.php")
        {
            if (LicenseUnreachable) throw new HttpRequestException("Simulated connection failure");
            return Json(LicenseStatus, new JsonObject { ["license_activated"] = LicenseActivated }.ToJsonString());
        }

        if (uri.Host == FeedHost && uri.AbsolutePath == "/wp-json/tech/videos/")
        {
            var body = FeedBody ?? new JsonArray(Articles.Select(a => (JsonNode?)a.ToJson()).ToArray()).ToJsonString();
            return Json(FeedStatus, body);
        }

        const string parentPrefix = "/wp-json/wp/v2/posts/";
        if (uri.Host == FeedHost && uri.AbsolutePath.StartsWith(parentPrefix, StringComparison.Ordinal)
            && int.TryParse(uri.AbsolutePath[parentPrefix.Length..], out var parentId))
        {
            return ParentLinks.TryGetValue(parentId, out var link)
                ? Json(HttpStatusCode.OK, new JsonObject { ["link"] = link }.ToJsonString())
                : Json(HttpStatusCode.NotFound, "{}");
        }

        if (uri.Host == ImageHost)
        {
            if (MissingImages.Contains(uri.AbsolutePath.TrimStart('/')))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var image = new ByteArrayContent(PngBytes);
            image.Headers.ContentType = new("image/png");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = image });
        }

        throw Refuse($"external request to {uri.Host}{uri.AbsolutePath}");
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private sealed class Handler(FakeEdgeServer server) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            server.RespondAsync(request);
    }
}

/// <summary>One feed entry, written out in the WordPress shape the vendor feed uses.</summary>
public sealed class FakeArticle
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Date { get; set; } = "2026-10-01T08:30:00Z";
    /// <summary>"publish", or "future" for a post the vendor has scheduled.</summary>
    public string Status { get; set; } = "publish";
    public string? ImageUrl { get; set; }
    public bool SendUpdate { get; set; } = true;
    public int UpdateVersion { get; set; } = 1;
    public string? WeekCommencing { get; set; }
    public int? ParentPageUrl { get; set; }
    public string? GuidOverride { get; set; }
    public string? LinkOverride { get; set; }

    public string Guid => GuidOverride ?? $"https://{FakeEdgeServer.FeedHost}/?p={Id}";
    public string Link => LinkOverride ?? $"https://{FakeEdgeServer.FeedHost}/{Slug}/";

    public JsonObject ToJson()
    {
        var acf = new JsonObject
        {
            ["week_commencing"] = WeekCommencing,
            ["send_update"] = SendUpdate,
            ["update_version"] = UpdateVersion,
        };
        if (ParentPageUrl.HasValue) acf["parent_page_url"] = ParentPageUrl.Value;

        var article = new JsonObject
        {
            ["id"] = Id,
            ["slug"] = Slug,
            ["date"] = Date,
            ["status"] = Status,
            ["link"] = Link,
            ["guid"] = new JsonObject { ["rendered"] = Guid },
            ["title"] = new JsonObject { ["rendered"] = Title },
            ["content"] = new JsonObject { ["rendered"] = Content },
            ["excerpt"] = new JsonObject { ["rendered"] = Excerpt },
            ["acf"] = acf,
        };
        if (ImageUrl != null)
            article["yoast_head_json"] = new JsonObject { ["og_image"] = new JsonArray(new JsonObject { ["url"] = ImageUrl }) };

        return article;
    }
}
