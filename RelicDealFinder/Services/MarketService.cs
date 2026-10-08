using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Registry;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.Market.Order;

namespace RelicDealFinder.Services;

public class MarketService(
    HttpClient marketClient,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<MarketService> logger,
    ResiliencePipelineProvider<string> pipelines
)
{
    private readonly ResiliencePipeline _rateLimiter = pipelines.GetPipeline("wfm");

    public async Task<List<MarketItem>?> GetAllMarketItems(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching all market items...");
        var response = await marketClient.GetFromJsonAsync<MarketItemsResponse>("items", ct);
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

    public async Task<List<MarketOrder>?> GetListingsForValuableRelics(int n, bool rad = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Relics.AsNoTracking();
        var relics = await (
            rad
                ? query.OrderByDescending(x => x.RadPotentialPlat)
                : query.OrderByDescending(x => x.IntPotentialPlat)
        )
            .Take(n)
            .ToListAsync();

        // TODO: foreach relic get top5 orders; Truncate to 1(?); Check if it only returns active / offline users
        return null;
    }
}
