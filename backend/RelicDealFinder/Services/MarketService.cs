using RelicDealFinder.Data;
using RelicDealFinder.Models.Market;

namespace RelicDealFinder.Services;

public class MarketService(HttpClient marketClient, AppDbContext db)
{
    // Returns a list of all Items on WarframeMarket
    public async Task<List<MarketItem>?> GetAllMarketItems()
    {
        var items = await marketClient.GetFromJsonAsync<List<MarketItem>>("items");
        if (items is null)
        {
            return null;
        }
        db.PrimeParts.AddRange(FilterForPrimeParts(items));
        await db.SaveChangesAsync();
        return items;
    }

    public List<MarketItem> FilterForRelics(List<MarketItem> items)
    {
        // filter for all relics, minus requiems
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
                .Where(i => i.Tags.Contains("prime") && i.Tags.Contains("component"))
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
}
