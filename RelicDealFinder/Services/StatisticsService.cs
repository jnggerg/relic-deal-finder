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
using PrimePart = RelicDealFinder.Models.Market.PrimePart;

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

    public const int PriceWindowDays = 7;

    /*  The 90 days array only contains completed UTC days (the latest entry is yesterday),
     *  so the window is the last 7 completed days rather than "now - 7 days"
     */
    private static IEnumerable<DateOnly> PriceWindow()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        return Enumerable
            .Range(0, PriceWindowDays)
            .Select(i => yesterday.AddDays(i - PriceWindowDays + 1));
    }

    // One row per day in the window; days without trades are missing from the API, so they get a null price
    private async Task<List<PartPriceDay>> GetPriceDays(MarketItem item, CancellationToken ct)
    {
        var stats = await GetItemStats(item.Slug, ct);

        if (stats == null)
            logger.LogWarning("Item {Slug} stats not found", item.Slug);

        // the timestamps carry a +00:00 offset, which System.Text.Json turns into local time
        var entries = (stats?.NinetyDays ?? [])
            .GroupBy(e => DateOnly.FromDateTime(e.Datetime.ToUniversalTime()))
            .ToDictionary(g => g.Key, g => g.First());

        return
        [
            .. PriceWindow()
                .Select(date =>
                    entries.TryGetValue(date, out var e) && e.Volume > 0
                        ? new PartPriceDay
                        {
                            Slug = item.Slug,
                            Date = date,
                            WaPrice = e.WaPrice,
                            Volume = e.Volume,
                        }
                        : new PartPriceDay
                        {
                            Slug = item.Slug,
                            Date = date,
                            WaPrice = null,
                            Volume = 0,
                        }
                ),
        ];
    }

    // We calculate the Volume Weighted Average Price for each item over the price window
    private static double ComputeVwap(List<PartPriceDay> days)
    {
        var volume = 0;
        var sum = 0.0;
        foreach (var d in days.Where(d => d.WaPrice is not null))
        {
            sum += d.Volume * d.WaPrice!.Value;
            volume += d.Volume;
        }

        return volume > 0 ? sum / volume : 0;
    }

    // Sets Price on every item and returns the daily rows behind it, for the trend table
    public async Task<List<PartPriceDay>> AddPriceToItems<T>(
        List<T> items,
        Action<T>? onItemPriced = null,
        CancellationToken cancellationToken = default
    )
        where T : MarketItem
    {
        var tasks = items.Select(async e =>
        {
            var days = await _rateLimiter.ExecuteAsync(
                async ct => await GetPriceDays(e, ct),
                cancellationToken
            );
            e.Price = ComputeVwap(days);
            onItemPriced?.Invoke(e);
            return days;
        });

        return [.. (await Task.WhenAll(tasks)).SelectMany(d => d)];
    }

    // We assume a full radshare, since that's the most realistic form of relic opening
    public const int SquadSize = 4;

    /*
     * Calculates the squad EV of a Relic based on Prime Part 7 day weighted avg Price:
     * the expected price of the best reward out of SquadSize rolls, not of a single roll.
     * Formas (and rewards without a price) count as worthless, so they are only ever the last pick.
     */
    private void ComputeRelicProfit(MarketRelic relic, Dictionary<string, double?> prices)
    {
        var rewards = new List<(double Price, string Rarity)>();
        foreach (var p in relic.AllItemSlugs())
        {
            if (!prices.TryGetValue(p, out var price) || price is null)
                logger.LogWarning($"Missing price for {p}");

            var rarity =
                relic.UncommonRewardSlugs.Contains(p) ? "uncommon"
                : relic.CommonRewardSlugs.Contains(p) ? "common"
                : "rare";
            rewards.Add((price ?? 0, rarity));
        }

        relic.IntPotentialPlat = SquadEv(rewards, "int", SquadSize);
        relic.RadPotentialPlat = SquadEv(rewards, "rad", SquadSize);
    }

    /*
     * Walks the rewards from most to least valuable. The best of n rolls is this reward when
     * no roll beats it, minus when no roll beats it or is it: (1 - c)^n - (1 - c - q)^n,
     * where c is the chance of one roll beating it and q its own drop chance.
     * With n = 1 this is the plain single relic EV.
     */
    public static double SquadEv(
        IEnumerable<(double Price, string Rarity)> rewards,
        string refinement,
        int squadSize
    )
    {
        var ev = 0.0;
        var better = 0.0;
        foreach (var (price, rarity) in rewards.OrderByDescending(r => r.Price))
        {
            var chance = RelicDropChances[rarity][refinement] / 100;
            ev +=
                price
                * (Math.Pow(1 - better, squadSize) - Math.Pow(1 - better - chance, squadSize));
            better += chance;
        }

        return ev;
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
            .Select(y => new EvEntry(y.Name(), y.Tier, y.RadPotentialPlat ?? 0))
            .ToListAsync();
    }
}
