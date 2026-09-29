using System.Runtime.InteropServices.Swift;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
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

    // The drop chances hardcoded from https://warframe.fandom.com/wiki/Void_Relic/Math#Drop_Chances
    private static readonly Dictionary<string, Dictionary<string, double>> RelicDropChances = new()
    {
        ["common"] = new() { ["int"] = 25.33, ["rad"] = 16.67 },
        ["uncommon"] = new() { ["int"] = 11, ["rad"] = 20 },
        ["rare"] = new() { ["int"] = 2, ["rad"] = 10 },
    };

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
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private async Task<StatisticsClosed?> GetItemStats(string itemSlug, CancellationToken ct)
    {
        try
        {
            var statsResponse = await _statsClient.GetFromJsonAsync<StatsResponse>(
                $"items/{itemSlug}/statistics",
                StatsJsonOptions,
                ct
            );

            return statsResponse?.Payload?.StatisticsClosed;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "HTTP error fetching stats for {Slug}: {StatusCode} - {Message}",
                itemSlug,
                ex.StatusCode,
                ex.Message
            );
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "JSON parsing failed for {Slug}: {Message}",
                itemSlug,
                ex.Message
            );
        }

        return null;
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

    public async Task<List<T>?> AddPriceToItems<T>(List<T> items)
        where T : MarketItem
    {
        var tasks = items.Select(async e =>
        {
            return await _rateLimiter.ExecuteAsync(async ct => await ComputeItemPrice(e, ct));
        });

        double[] newPrices = await Task.WhenAll(tasks);

        List<T> pricedItems = new(items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            var pricedItem = items[i];
            pricedItem.Price = newPrices[i];
            pricedItems.Add(pricedItem);
        }

        return pricedItems;
    }

    /*
     * Calculates potential platinum profit on a Relic based on Prime Part 7 day weighted avg Price
     * This does not account for Formas, so on certain relics the math may result in a total multiplier < 100, which is normal
     */
    private MarketRelic ComputeRelicProfit(MarketRelic relic, Dictionary<string, double?> prices)
    {
        var allSlugs = relic.AllItemSlugs();

        var expectedIntValue = 0.0;
        var expectedRadValue = 0.0;
        foreach (var p in allSlugs)
        {
            if (!prices.TryGetValue(p, out var price) || price is null)
            {
                _logger.LogWarning($"Missing price for {p}");
                continue;
            }

            if (relic.UncommonRewardSlugs.Contains(p))
            {
                expectedIntValue += price.Value * RelicDropChances["uncommon"]["int"];
                expectedRadValue += price.Value * RelicDropChances["uncommon"]["rad"];
            }
            else if (relic.CommonRewardSlugs.Contains(p))
            {
                expectedIntValue += price.Value * RelicDropChances["common"]["int"];
                expectedRadValue += price.Value * RelicDropChances["common"]["rad"];
            }
            else
            {
                expectedIntValue += price.Value * RelicDropChances["rare"]["int"];
                expectedRadValue += price.Value * RelicDropChances["rare"]["rad"];
            }
        }

        relic.IntPotentialPlat = expectedIntValue / 100;
        relic.RadPotentialPlat = expectedRadValue / 100;
        return relic;
    }

    public async Task<List<MarketRelic>> ComputeAllRelicValues(List<MarketRelic> relics)
    {
        _logger.LogInformation("Calculating relic profits...");

        // We query all Prime parts and prices that are in our relics at once instead of per-relic calls to reduce roundtrips

        var allSlugs = relics.SelectMany(r => r.AllItemSlugs()).ToHashSet(); // We flatmap all affected prime slugs, then HashSet to ignore duplicates
        var prices = await _db
            .PrimeParts.Where(p => allSlugs.Contains(p.Slug))
            .ToDictionaryAsync(p => p.Slug, p => p.Price);

        foreach (var relic in relics)
        {
            ComputeRelicProfit(relic, prices);
        }

        return relics;
    }
}
