using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.RateLimiting;
using RelicDealFinder.Components;
using RelicDealFinder.Data;
using RelicDealFinder.Services;
using RelicDealFinder.Services.Refresh;

// Uniform number/date formatting (dot decimals, comma thousands) regardless of the host's locale
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Factory for short-lived contexts (circuit-scoped components); also registers AppDbContext as scoped
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default"),
        x => x.MaxBatchSize(100)
    )
);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddScoped<IRelicDashboardService, RelicDashboardService>();

builder.Services.AddSingleton<RefreshCoordinator>();
builder.Services.AddScoped<IRefreshPipeline, RefreshPipeline>();
builder.Services.AddHostedService<RefreshWorker>();

/*
 * WFM api v2 rate limits at 3req/s. The stats endpoint is available only on v1,
 * but we should still assume the same rate limit and respect it.
 */
builder.Services.AddResiliencePipeline(
    "wfm",
    pipeline =>
    {
        var limiter = new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = int.MaxValue,
            }
        );

        pipeline.AddRateLimiter(
            new RateLimiterStrategyOptions
            {
                RateLimiter = args => limiter.AcquireAsync(1, args.Context.CancellationToken),
            }
        );
    }
);

var marketBaseUrl =
    builder.Configuration["ExternalApiUrls:MarketBaseUrl"]
    ?? throw new InvalidOperationException("ExternalApiUrls:MarketBaseUrl is not configured");

var marketv1BaseUrl =
    builder.Configuration["ExternalApiUrls:MarketV1BaseUrl"]
    ?? throw new InvalidOperationException("ExternalApiUrls:MarketV1BaseUrl is not configured");

var wfcdRelicsUrl =
    builder.Configuration["ExternalApiUrls:WfcdRelicsUrl"]
    ?? throw new InvalidOperationException("ExternalApiUrls:WfcdRelicsUrl is not configured");

builder.Services.AddHttpClient<WfcdService>(client => client.BaseAddress = new Uri(wfcdRelicsUrl));
builder.Services.AddHttpClient<MarketService>(client =>
    client.BaseAddress = new Uri(marketBaseUrl)
);
builder.Services.AddHttpClient<StatisticsService>(client =>
    client.BaseAddress = new Uri(marketv1BaseUrl)
);

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
