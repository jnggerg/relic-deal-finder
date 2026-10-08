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

// Factory for short-lived contexts
// also registers AppDbContext as scoped
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
// Limiter and its options are registered on their own (not just inside the pipeline),
// so the UI can read GetStatistics() and show the configured limit.
// Token bucket with no burst (1 token) spaces requests evenly instead of firing 3 at each window start.
// 350ms instead of 333ms, so we cant accidentally send 4 in a second because of minor jitters
var wfmLimiterOptions = new TokenBucketRateLimiterOptions
{
    TokenLimit = 1,
    TokensPerPeriod = 1,
    ReplenishmentPeriod = TimeSpan.FromMilliseconds(350),
    QueueLimit = int.MaxValue,
};
builder.Services.AddSingleton(wfmLimiterOptions);
builder.Services.AddSingleton<RateLimiter>(_ => new TokenBucketRateLimiter(wfmLimiterOptions));

builder.Services.AddResiliencePipeline(
    "wfm",
    (pipeline, context) =>
    {
        var limiter = context.ServiceProvider.GetRequiredService<RateLimiter>();

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

var marketV1BaseUrl =
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
    client.BaseAddress = new Uri(marketV1BaseUrl)
);

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
