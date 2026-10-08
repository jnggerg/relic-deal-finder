using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Registry;
using RelicDealFinder.Data;
using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.Market.Stats;

namespace RelicDealFinder.Services;

public class StatisticsService(
    HttpClient statsClient,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<StatisticsService> logger,
    ResiliencePipelineProvider<string> pipelines
)
{
    private readonly ResiliencePipeline _rateLimiter = pipelines.GetPipeline("wfm");

    // The drop chances hardcoded from https://warframe.fandom.com/wiki/Void_Relic/Math#Drop_Chances
    private static readonly Dictionary<string, Dictionary<string, double>> RelicDropChances = new()
    {
        ["common"] = new() { ["int"] = 25.33, ["rad"] = 16.67 },
        ["uncommon"] = new() { ["int"] = 11, ["rad"] = 20 },
        ["rare"] = new() { ["int"] = 2, ["rad"] = 10 },
    };

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
            var statsResponse = await statsClient.GetFromJsonAsync<StatsResponse>(
                $"items/{itemSlug}/statistics",
                StatsJsonOptions,
                ct
            );

            return statsResponse?.Payload?.StatisticsClosed;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "HTTP error fetching stats for {Slug}: {StatusCode} - {Message}",
                itemSlug,
                ex.StatusCode,
                ex.Message
            );
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "JSON parsing failed for {Slug}: {Message}",
                itemSlug,
                ex.Message
            );
        }

        return null;
    }

    // We calculate the Volume Weighted Average Price for each item over the last 7 days
    private async Task<double> ComputeItemPrice(MarketItem item, CancellationToken ct)
    {
        var stats = await GetItemStats(item.Slug, ct);

        if (stats == null)
        {
            logger.LogWarning("Item {Slug} stats not found", item.Slug);
            return 0;
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

        return volume > 0 ? sum / volume : 0;
    }

    public async Task AddPriceToItems<T>(
        List<T> items,
        Action<T>? onItemPriced = null,
        CancellationToken cancellationToken = default
    )
        where T : MarketItem
    {
        var tasks = items.Select(async e =>
        {
            e.Price = await _rateLimiter.ExecuteAsync(
                async ct => await ComputeItemPrice(e, ct),
                cancellationToken
            );
            onItemPriced?.Invoke(e);
        });

        await Task.WhenAll(tasks);
    }

    /*
     * Calculates potential platinum profit on a Relic based on Prime Part 7 day weighted avg Price
     * This does not account for Formas, so on certain relics the math may result in a total multiplier < 100, which is normal
     */
    private void ComputeRelicProfit(MarketRelic relic, Dictionary<string, double?> prices)
    {
        var allSlugs = relic.AllItemSlugs();

        var expectedIntValue = 0.0;
        var expectedRadValue = 0.0;
        foreach (var p in allSlugs)
        {
            if (!prices.TryGetValue(p, out var price) || price is null)
            {
                logger.LogWarning($"Missing price for {p}");
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
    }

    // Takes the freshly priced parts instead of reading the DB, since the refresh builds into a staging database
    public void ComputeAllRelicValues(List<MarketRelic> relics, List<PrimePart> primeParts)
    {
        logger.LogInformation("Calculating relic profits...");

        var prices = primeParts.ToDictionary(p => p.Slug, p => p.Price);

        foreach (var relic in relics)
        {
            ComputeRelicProfit(relic, prices);
        }
    }

    public async Task<IReadOnlyList<EvEntry>> GetHighestEvRelics(int n)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db
            .Relics.AsNoTracking()
            .OrderByDescending(x => x.RadPotentialPlat)
            .Take(n)
            .Select(y => new EvEntry(y.Slug, y.Tier, y.RadPotentialPlat ?? 0))
            .ToListAsync();
    }
}
