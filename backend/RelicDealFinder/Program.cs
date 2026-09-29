using System.Text.Json.Serialization;
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
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var marketBaseUrl =
    builder.Configuration["ExternalApiUrls:MarketBaseUrl"]
    ?? throw new InvalidOperationException("ExternalApiUrls:MarketBaseUrl is not configured");
var wfcdRelicsUrl =
    builder.Configuration["ExternalApiUrls:WfcdRelicsUrl"]
    ?? throw new InvalidOperationException("ExternalApiUrls:WfcdRelicsUrl is not configured");

builder.Services.AddHttpClient<WfcdService>(client => client.BaseAddress = new Uri(wfcdRelicsUrl));
builder.Services.AddHttpClient<MarketService>(client =>
    client.BaseAddress = new Uri(marketBaseUrl)
);

builder.Services.AddControllers();

var app = builder.Build();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
