using System.Net;
using System.Text.RegularExpressions;

/// <summary>
/// A cookie-keeping HTTP client that signs in through the site's real login form and posts real forms with
/// their antiforgery tokens, so permission checks exercise the same pipeline a person's browser does.
/// </summary>
sealed partial class Browser : IDisposable
{
    private readonly HttpClient _http;

    public Browser(Uri baseAddress)
    {
        _http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    public sealed record Page(HttpStatusCode Status, string Body, string? Location, string? ContentType, byte[] Bytes)
    {
        public bool IsOk => Status == HttpStatusCode.OK;
        /// <summary>A refusal: a 401/403 status, or a redirect to the sign-in or 403 error page.</summary>
        public bool IsDenied => Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || (Status == HttpStatusCode.Redirect && Location != null
                && (Location.Contains("/Login", StringComparison.OrdinalIgnoreCase)
                    || Location.Contains("/Error/403", StringComparison.OrdinalIgnoreCase)
                    || Location.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<Page> GetAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        return await ReadAsync(response);
    }

    public async Task<Page> PostAsync(string path, IEnumerable<KeyValuePair<string, string>> fields)
    {
        using var response = await _http.PostAsync(path, new FormUrlEncodedContent(fields));
        return await ReadAsync(response);
    }

    /// <summary>Signs in through the login form. Returns false when the site refuses the credentials.</summary>
    public async Task<bool> SignInAsync(string userName, string password)
    {
        var login = await GetAsync("/Login");
        if (!login.IsOk) throw new InvalidOperationException($"The login page answered {(int)login.Status}.");

        var fields = FormFields(login.Body);
        var userField = fields.Keys.FirstOrDefault(k => k.EndsWith("UserName", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The login form has no user name field.");
        var passwordField = fields.Keys.FirstOrDefault(k => k.EndsWith("Password", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The login form has no password field.");
        fields[userField] = userName;
        fields[passwordField] = password;

        var result = await PostAsync("/Login", fields);
        return result.Status == HttpStatusCode.Redirect && result.Location?.Contains("/Login", StringComparison.OrdinalIgnoreCase) != true;
    }

    /// <summary>
    /// The named fields of every form control on the page, with the values a browser would submit: unchecked
    /// boxes are omitted and a select contributes its selected option.
    /// </summary>
    public static Dictionary<string, string> FormFields(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match input in InputTag().Matches(html))
        {
            var tag = input.Value;
            var name = Attribute(tag, "name");
            if (name is null) continue;
            var type = Attribute(tag, "type")?.ToLowerInvariant() ?? "text";
            if (type is "submit" or "button" or "file") continue;
            if (type is "checkbox" or "radio")
            {
                if (CheckedAttribute().IsMatch(tag)) fields[name] = Attribute(tag, "value") ?? "on";
                // An unchecked box is paired with a hidden "false" input of the same name; keep whichever is first.
                continue;
            }
            // The hidden companion of a checkbox must not overwrite a checked box that came before it.
            if (type == "hidden" && fields.ContainsKey(name) && Attribute(tag, "value") == "false") continue;
            fields[name] = WebUtility.HtmlDecode(Attribute(tag, "value") ?? string.Empty);
        }

        foreach (Match select in SelectTag().Matches(html))
        {
            var name = Attribute(select.Groups["open"].Value, "name");
            if (name is null) continue;
            var options = OptionTag().Matches(select.Groups["body"].Value);
            var chosen = options.FirstOrDefault(o => SelectedAttribute().IsMatch(o.Value)) ?? options.FirstOrDefault();
            fields[name] = chosen is null ? string.Empty : WebUtility.HtmlDecode(Attribute(chosen.Value, "value") ?? string.Empty);
        }

        return fields;
    }

    public static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"""\s{Regex.Escape(name)}\s*=\s*(?:"(?<v>[^"]*)"|'(?<v>[^']*)')""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value : null;
    }

    private static async Task<Page> ReadAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var body = System.Text.Encoding.UTF8.GetString(bytes);
        var location = response.Headers.Location?.ToString();
        return new Page(response.StatusCode, body, location, response.Content.Headers.ContentType?.MediaType, bytes);
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex("<input\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTag();
    [GeneratedRegex("(?<open><select\\b[^>]*>)(?<body>.*?)</select>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SelectTag();
    [GeneratedRegex("<option\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex OptionTag();
    [GeneratedRegex("\\schecked(\\s|=|>|/)", RegexOptions.IgnoreCase)]
    private static partial Regex CheckedAttribute();
    [GeneratedRegex("\\sselected(\\s|=|>|/)", RegexOptions.IgnoreCase)]
    private static partial Regex SelectedAttribute();
}
