using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Components;
using RelicDealFinder.Data;
using RelicDealFinder.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default"),
        x => x.MaxBatchSize(100)
    )
);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

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
