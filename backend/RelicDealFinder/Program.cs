using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("Default"),
        x => x.MaxBatchSize(100)
    )
);

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    //app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
