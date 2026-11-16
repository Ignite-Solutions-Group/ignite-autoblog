using System.Diagnostics;
using IgniteSG.OrchardCore.MspMarketingEdge;
using IgniteSG.OrchardCore.MspMarketingEdge.Fixtures;
using IgniteSG.OrchardCore.MspMarketingEdge.Indexes;
using IgniteSG.OrchardCore.MspMarketingEdge.Models;
using IgniteSG.OrchardCore.MspMarketingEdge.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using OrchardCore.Email;
using OrchardCore.Entities;
using OrchardCore.Environment.Shell;
using OrchardCore.Infrastructure;
using OrchardCore.Settings;
using YesSql;
using ISession = YesSql.ISession;

/// <summary>
/// A real Orchard Core site on a disposable SQLite database. The module runs unmodified; only its outbound
/// HTTP, outbound email and (on request) image storage are replaced with in-process stand-ins.
/// </summary>
sealed class FixtureHost : IAsyncDisposable
{
    public const string TenantName = "Default";
    public const string AdminUser = "fixture";
    public const string SiteBaseUrl = "https://www.fixture.invalid";
    public const string SiteTimeZone = "Pacific/Honolulu";

    private readonly WebApplication _app;
    private readonly IShellHost _shells;

    public FakeEdgeServer Edge { get; }
    public CapturedEmail Email { get; }
    public ErrorCapture Errors { get; }
    public ImageFault ImageFault { get; }
    public Uri BaseAddress { get; }
    public string AdminPassword { get; }
    public string DataPath { get; }
    /// <summary>Error-level log entries the checks provoked on purpose.</summary>
    public int ExpectedErrors { get; set; }

    private FixtureHost(WebApplication app, FakeEdgeServer edge, CapturedEmail email, ErrorCapture errors,
        ImageFault imageFault, Uri baseAddress, string adminPassword, string dataPath)
    {
        _app = app;
        _shells = app.Services.GetRequiredService<IShellHost>();
        Edge = edge;
        Email = email;
        Errors = errors;
        ImageFault = imageFault;
        BaseAddress = baseAddress;
        AdminPassword = adminPassword;
        DataPath = dataPath;
    }

    /// <param name="dataPath">An empty folder for a new site, or an existing App_Data folder to open as-is.</param>
    /// <param name="recipe">The setup recipe for a new site; ignored when the site already exists.</param>
    /// <param name="adminPassword">The administrator password (set for a new site, already stored for an existing one).</param>
    /// <param name="startupBudget">How long the first request may take before the run is abandoned.</param>
    /// <param name="extraConfiguration">Additional host configuration, such as a media storage provider.</param>
    public static async Task<FixtureHost> StartAsync(string dataPath, string recipe, string adminPassword,
        TimeSpan startupBudget, IDictionary<string, string?>? extraConfiguration = null)
    {
        Directory.CreateDirectory(dataPath);
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "wwwroot"));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
        });

        // AutoSetup only acts on a tenant that has not been set up, so an existing site is opened untouched.
        var configuration = new Dictionary<string, string?>
        {
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:ShellName"] = TenantName,
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:SiteName"] = "Editorial fixture",
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:SiteTimeZone"] = SiteTimeZone,
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminUsername"] = AdminUser,
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminEmail"] = "fixture@example.invalid",
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:AdminPassword"] = adminPassword,
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:DatabaseProvider"] = "Sqlite",
            ["OrchardCore:OrchardCore_AutoSetup:Tenants:0:RecipeName"] = recipe,
        };
        foreach (var pair in extraConfiguration ?? new Dictionary<string, string?>())
            configuration[pair.Key] = pair.Value;
        builder.Configuration.AddInMemoryCollection(configuration);

        var edge = new FakeEdgeServer();
        var email = new CapturedEmail();
        var errors = new ErrorCapture();
        var imageFault = new ImageFault();

        builder.Logging.AddProvider(errors);
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

        builder.Services.AddOrchardCms()
            .AddSetupFeatures("OrchardCore.AutoSetup")
            // A high order runs after every module's own registrations, so these replace rather than precede them.
            .ConfigureServices(services =>
            {
                services.Configure<HttpClientFactoryOptions>("EdgeApi", o =>
                    o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = edge.CreateHandler()));

                services.Replace(ServiceDescriptor.Scoped<IEmailService>(_ => email));

                if (services.Any(d => d.ServiceType == typeof(IImageService)))
                {
                    services.AddScoped<ImageService>();
                    services.Replace(ServiceDescriptor.Scoped<IImageService>(sp =>
                        new FaultableImages(sp.GetRequiredService<ImageService>(), imageFault)));
                }
            }, order: 10_000);

        builder.Services.Configure<ShellOptions>(o => o.ShellsApplicationDataPath = dataPath);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.UseOrchardCore();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var host = new FixtureHost(app, edge, email, errors, imageFault, new Uri(address), adminPassword, dataPath);
        await host.WaitUntilReadyAsync(startupBudget);
        return host;
    }

    /// <summary>
    /// The first request builds the tenant, and on a new site also runs setup and every migration. That takes
    /// seconds on a build agent but can take many minutes on a workstation whose security policy inspects each
    /// assembly as it loads, so this waits on the site itself instead of assuming a fixed HTTP timeout.
    /// </summary>
    private async Task WaitUntilReadyAsync(TimeSpan budget)
    {
        Console.WriteLine($"Starting the site at {BaseAddress} (waiting up to {budget.TotalMinutes:0} minutes for the first request)...");
        var clock = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var timeout = new CancellationTokenSource(budget);
        using var heartbeat = new Timer(_ => Console.WriteLine($"  ...still starting after {clock.Elapsed:m\\:ss}"),
            null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        try
        {
            using var response = await http.GetAsync(BaseAddress, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"The site did not answer its first request within {budget.TotalMinutes:0} minutes. " +
                "This is a startup window, not an import failure; raise AUTOBLOG_FIXTURE_STARTUP_MINUTES to wait longer.");
        }

        if (!_shells.TryGetSettings(TenantName, out var settings) || !settings.IsRunning())
            throw new InvalidOperationException("The site answered, but the tenant is not running. Setup did not complete.");

        Console.WriteLine($"Site ready after {clock.Elapsed:m\\:ss}.");
    }

    /// <summary>Runs inside a tenant scope with a request context, and commits the session when it completes.</summary>
    public async Task ScopeAsync(Func<IServiceProvider, Task> action)
    {
        var scope = await _shells.GetScopeAsync(TenantName);
        await scope.UsingAsync(async s =>
        {
            var accessor = s.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            var context = new DefaultHttpContext { RequestServices = s.ServiceProvider };
            context.Request.Scheme = BaseAddress.Scheme;
            context.Request.Host = new HostString(BaseAddress.Host, BaseAddress.Port);
            accessor.HttpContext = context;
            try { await action(s.ServiceProvider); }
            finally { accessor.HttpContext = previous; }
        });
    }

    public async Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        T result = default!;
        // A block body, so this binds to the non-generic overload instead of calling itself.
        await ScopeAsync(async sp => { result = await action(sp); });
        return result;
    }

    /// <summary>The import as the background task and the admin button run it.</summary>
    public Task<ImportResult> ImportAsync() =>
        ScopeAsync(sp => sp.GetRequiredService<IAutoBlogImportService>().RunImportAsync());

    public Task ConfigureAsync(Action<AutoBlogSettings> change) => ScopeAsync(async sp =>
    {
        var sites = sp.GetRequiredService<ISiteService>();
        var site = await sites.LoadSiteSettingsAsync();
        var settings = site.GetOrCreate<AutoBlogSettings>();
        change(settings);
        site.Put(settings);
        await sites.UpdateSiteSettingsAsync(site);
    });

    public Task<AutoBlogSettings> SettingsAsync() => ScopeAsync(async sp =>
        (await sp.GetRequiredService<ISiteService>().GetSiteSettingsAsync()).GetOrCreate<AutoBlogSettings>());

    /// <summary>The latest version of every AutoBlog post, read from Orchard's own content index.</summary>
    public Task<List<ContentItem>> PostsAsync() => ScopeAsync(async sp =>
        (await sp.GetRequiredService<ISession>()
            .Query<ContentItem, ContentItemIndex>(i => i.ContentType == Constants.ContentType && i.Latest)
            .ListAsync()).ToList());

    /// <summary>Finds a post by its stored vendor GUID without using the module's own index.</summary>
    public async Task<ContentItem?> PostAsync(FakeArticle article) =>
        (await PostsAsync()).SingleOrDefault(p => p.Get<AutoBlogPart>(nameof(AutoBlogPart))?.EdgeGuid == article.Guid);

    public Task<List<string>> IndexedGuidsAsync() => ScopeAsync(async sp =>
        (await sp.GetRequiredService<ISession>().QueryIndex<AutoBlogPostIndex>().ListAsync())
            .Select(i => i.EdgeGuid ?? string.Empty).ToList());

    public Browser CreateBrowser() => new(BaseAddress);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Records notification email instead of sending it.</summary>
sealed class CapturedEmail : IEmailService
{
    private readonly List<MailMessage> _messages = [];
    public IReadOnlyList<MailMessage> Messages { get { lock (_messages) return _messages.ToArray(); } }
    public void Clear() { lock (_messages) _messages.Clear(); }

    public Task<Result> SendAsync(MailMessage message, string? providerName = null, CancellationToken cancellationToken = default)
    {
        lock (_messages) _messages.Add(message);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Lets a check make image storage fail for one article, to prove one bad article does not stop a run.</summary>
sealed class ImageFault
{
    public string? FailForSlug { get; set; }
}

sealed class FaultableImages(ImageService inner, ImageFault fault) : IImageService
{
    public Task<string?> DownloadAndStoreImageAsync(string imageUrl, string slug) =>
        fault.FailForSlug != null && fault.FailForSlug == slug
            ? throw new InvalidOperationException("Simulated media storage failure")
            : inner.DownloadAndStoreImageAsync(imageUrl, slug);
}

/// <summary>Counts error-level log entries, so an error the site swallowed still fails the run.</summary>
sealed class ErrorCapture : ILoggerProvider
{
    private readonly List<string> _entries = [];
    public int Count { get { lock (_entries) return _entries.Count; } }
    public IReadOnlyList<string> Entries { get { lock (_entries) return _entries.ToArray(); } }
    public ILogger CreateLogger(string name) => new Capture(this, name);
    public void Dispose() { }

    sealed class Capture(ErrorCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            lock (owner._entries) owner._entries.Add($"{category}: {formatter(state, exception)}");
        }
    }
}

/// <summary>Collects results instead of stopping at the first failure, because a site start is slow to repeat.</summary>
sealed class Checks
{
    private readonly List<string> _failures = [];
    public int Passed { get; private set; }
    public IReadOnlyList<string> Failures => _failures;

    public void Section(string title) => Console.WriteLine($"== {title}");

    public void That(bool condition, string name, string? detail = null)
    {
        if (condition) { Passed++; return; }
        var text = detail is null ? name : $"{name} [{detail}]";
        _failures.Add(text);
        Console.WriteLine($"   FAILED: {text}");
    }

    public void Equal<T>(T expected, T actual, string name) =>
        That(EqualityComparer<T>.Default.Equals(expected, actual), name, $"expected '{expected}', got '{actual}'");

    public void Contains(string haystack, string needle, string name) =>
        That(haystack.Contains(needle, StringComparison.Ordinal), name, $"missing '{needle}'");

    public void Lacks(string haystack, string needle, string name) =>
        That(!haystack.Contains(needle, StringComparison.Ordinal), name, $"found '{needle}'");

    /// <summary>Runs one group of checks; an unexpected exception fails that group and the run continues.</summary>
    public async Task GroupAsync(string title, Func<Task> group)
    {
        Section(title);
        try { await group(); }
        catch (Exception ex)
        {
            _failures.Add($"{title}: stopped by {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"   FAILED: {title} stopped by {ex}");
        }
    }
}
