using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Registry;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.Market.Order;

namespace RelicDealFinder.Services;

public class MarketService(
    HttpClient marketClient,
    AppDbContext db,
    WfcdService wfcdService,
    ILogger<MarketService> logger,
    StatisticsService statService,
    ResiliencePipelineProvider<string> pipelines
)
{
    private readonly ResiliencePipeline _rateLimiter = pipelines.GetPipeline("wfm");

    private async Task<List<MarketItem>?> GetAllMarketItems()
    {
        logger.LogInformation("Fetching all market items...");
        var response = await marketClient.GetFromJsonAsync<MarketItemsResponse>("items");
        return response?.Data;
    }

    /*
     *  Returns top 5 orders, ordered by seller / buyer POV (so .sell is ordered by cheapest and vice versa)
     *  Generic so it can also be used with relics, not just Prime parts
     */
    private async Task<List<MarketOrder>?> GetMarketOrders<T>(T item)
        where T : MarketItem
    {
        var orders = await _rateLimiter.ExecuteAsync(async ct =>
            await marketClient.GetFromJsonAsync<MarketTopOrderResponse>(
                $"orders/item/{item.Slug}/top",
                ct
            )
        );

        return orders?.data.sell;
    }

    private static List<MarketItem> FilterForRelics(List<MarketItem> items)
    {
        // drop requiem relics, as we only care about prime part rewards
        return [.. items.Where(i => i.Tags.Contains("relic") && !i.Tags.Contains("requiem"))];
    }

    /*  Currently, there is no point in persisting anything other than Prime components,
     *  as we only evaluate based on individual prime parts.
     *  By filtering out before actually persisting, we can reduce the IO operations needed,
     *  since there is a great amount of items that are irrelevant to us.
     */
    private static List<PrimePart> FilterForPrimeParts(List<MarketItem> items)
    {
        return
        [
            .. items
                .Where(i => i.Tags.Contains("prime") && !i.Tags.Contains("set"))
                .Select(i => new PrimePart
                {
                    Id = i.Id,
                    Slug = i.Slug,
                    Tags = i.Tags,
                    Vaulted = i.Vaulted,
                    GameRef = i.GameRef,
                }),
        ];
    }

    // Invoked from UI
    public async Task<bool> RefreshDatabase()
    {
        logger.LogInformation("Deleting all records from all tables...");

        await db.PrimeParts.ExecuteDeleteAsync();
        await db.Relics.ExecuteDeleteAsync();
        logger.LogInformation("Database clear success.");

        var items = await GetAllMarketItems();
        if (items is null)
        {
            logger.LogInformation("Market items not found, aborting.");
            return false;
        }

        var pricedParts = await statService.AddPriceToItems(FilterForPrimeParts(items));

        db.PrimeParts.AddRange(pricedParts);
        await db.SaveChangesAsync();
        await wfcdService.PersistRelics(FilterForRelics(items));
        return true;
    }

    public async Task<List<MarketOrder>?> GetListingsForValuableRelics(int n, bool rad = true)
    {
        var relics = new List<MarketRelic>();
        if (rad)
        {
            relics = db.Relics.OrderByDescending(x => x.RadPotentialPlat).Take(n).ToList();
        }
        else
        {
            relics = db.Relics.OrderByDescending(x => x.IntPotentialPlat).Take(n).ToList();
        }

        // TODO: foreach relic get top5 orders; Truncate to 1(?); Check if it only returns active / offline users
        return null;
    }
}
