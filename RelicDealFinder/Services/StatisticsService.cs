using System.Runtime.InteropServices.Swift;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Polly;
using Polly.RateLimiting;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.Market.Stats;

namespace RelicDealFinder.Services;

public class StatisticsService
{
    private readonly HttpClient _statsClient;
    private readonly AppDbContext _db;
    private readonly ILogger<StatisticsService> _logger;
    private readonly ResiliencePipeline _rateLimiter;

    public StatisticsService(
        HttpClient statsClient,
        AppDbContext db,
        ILogger<StatisticsService> logger
    )
    {
        _statsClient = statsClient;
        _db = db;
        _logger = logger;

        /*
         * WFM api v2 rate limits at 3req/s. The stats endpoint is available only on v1,
         * but we should still assume the same rate limit and respect it.
         */
        var limiter = new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions()
            {
                PermitLimit = 3,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = int.MaxValue,
            }
        );

        _rateLimiter = new ResiliencePipelineBuilder()
            .AddRateLimiter(
                new RateLimiterStrategyOptions
                {
                    RateLimiter = args => limiter.AcquireAsync(1, args.Context.CancellationToken),
                }
            )
            .Build();
    }

    private static readonly JsonSerializerOptions StatsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private async Task<StatisticsClosed?> GetItemStats(String itemSlug, CancellationToken ct)
    {
        var statsResponse = await _statsClient.GetFromJsonAsync<StatsResponse>(
            $"items/{itemSlug}/statistics",
            StatsJsonOptions,
            ct
        );

        return statsResponse?.Payload.Statistics;
    }

    // We calculate the Volume Weighted Average Price for each item over the last 7 days
    private async Task<Double> ComputeItemPrice(MarketItem item, CancellationToken ct)
    {
        var stats = await GetItemStats(item.Slug, ct);

        if (stats == null)
        {
            _logger.LogWarning($"Item {item.Slug} stats not found");
            return -1;
        }

        var timeWithDelta = DateTime.UtcNow.AddDays(-7);
        var sevenDayStats = stats.NinetyDays.Where(e => e.Datetime >= timeWithDelta);

        var volume = 0;
        var sum = 0.0;
        foreach (var d in sevenDayStats)
        {
            sum += d.Volume * d.WaPrice;
            volume += d.Volume;
        }

        return volume > 0 ? sum / volume : -1;
    }

    public async Task<List<MarketItem>?> AddPriceToItems(List<MarketItem> items)
    {
        var tasks = items.Select(async e =>
        {
            return await _rateLimiter.ExecuteAsync(async ct => await ComputeItemPrice(e, ct));
        });

        double[] newPrices = await Task.WhenAll(tasks);

        List<MarketItem> pricedItems = new(items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            var pricedItem = items[i];
            pricedItem.Price = newPrices[i];
            pricedItems.Add(pricedItem);
        }

        return pricedItems;
    }
}
