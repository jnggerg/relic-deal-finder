using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Deals;
using RelicDealFinder.Models.Refresh;
using RelicDealFinder.Services.Deals;

namespace RelicDealFinder.Services;

public class RelicDashboardService(
    IDbContextFactory<AppDbContext> dbFactory,
    StatisticsService statService,
    TopPartsService topPartsService,
    DealCoordinator dealCoordinator,
    MarketService marketService,
    IOptions<DealOptions> dealOptions
) : IRelicDashboardService
{
    public Task<DashboardStats> GetStatsAsync()
    {
        var deals = dealCoordinator.GetDeals();
        var best = deals.FirstOrDefault(); // sorted by profit desc

        // TODO: PartsPriced and StaleParts are still mock values
        return Task.FromResult(
            new DashboardStats(
                deals.Count,
                deals.Where(d => d.SellerInGame).Select(d => d.Seller).Distinct().Count(),
                best?.Profit ?? 0,
                best is null ? "no deals yet" : $"{best.RelicName} {best.Refinement}",
                deals.Sum(d => d.Profit * d.Quantity),
                1204,
                0
            )
        );
    }

    public Task<IReadOnlyList<Deal>> GetTopDealsAsync(int count) =>
        Task.FromResult<IReadOnlyList<Deal>>([.. dealCoordinator.GetDeals().Take(count)]);

    public Task<IReadOnlyList<PrimePart>> GetTopPartsAsync() => topPartsService.GetTopPartsAsync();

    public Task<IReadOnlyList<EvEntry>> GetHighestRadiantEvAsync(int count) =>
        statService.GetHighestEvRelics(count);

    public async Task<IReadOnlyList<RelicSummary>> GetRelicsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var relics = await db.Relics.AsNoTracking().ToListAsync();
        var parts = await db.PrimeParts.AsNoTracking().ToDictionaryAsync(p => p.Slug, p => (p.Name, p.Price));

        return
        [
            .. relics.Select(r => new RelicSummary(
                r.Slug,
                r.Name(),
                r.Tier,
                r.IntPotentialPlat ?? 0,
                r.RadPotentialPlat ?? 0,
                DealPollWorker.Rewards(r, parts)
            )),
        ];
    }

    public async Task<IReadOnlyList<Deal>?> GetSellOrdersAsync(RelicSummary relic, CancellationToken ct)
    {
        var orders = await marketService.GetTopSellOrders(relic.Slug, ct);
        if (orders is null)
            return null;

        var tracked = new TrackedRelic(relic.Slug, relic.Name, relic.Tier, relic.RadEv, relic.Rewards);
        return [.. orders.Where(o => o.Visible).Select(o => DealRule.ToDeal(tracked, o, dealOptions.Value)).OrderBy(d => d.EffectiveCost)];
    }

    public async Task<RefreshRun?> GetLastRefreshAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db
            .RefreshRuns.AsNoTracking()
            .Where(r => r.Succeeded)
            .OrderByDescending(r => r.Id) // id is auto-increment, so latest is highest id
            .FirstOrDefaultAsync();
    }
}
