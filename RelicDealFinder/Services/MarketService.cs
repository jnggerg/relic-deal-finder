using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Market;

namespace RelicDealFinder.Services;

public class MarketService(
    HttpClient marketClient,
    AppDbContext db,
    WfcdService wfcdService,
    ILogger<MarketService> logger,
    StatisticsService statService
)
{
    private async Task<List<MarketItem>?> GetAllMarketItems()
    {
        logger.LogInformation("Fetching all market items...");
        var response = await marketClient.GetFromJsonAsync<MarketItemsResponse>("items");
        return response?.Data;
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
        if (pricedParts != null)
        {
            db.PrimeParts.AddRange(pricedParts);
            await db.SaveChangesAsync();
        }
        await wfcdService.PersistRelics(FilterForRelics(items));
        return true;
    }
}
