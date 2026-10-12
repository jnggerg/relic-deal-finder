using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Refresh;
using RelicDealFinder.Services.Deals;

namespace RelicDealFinder.Services;

public class RelicDashboardService(
    IDbContextFactory<AppDbContext> dbFactory,
    StatisticsService statService,
    TopPartsService topPartsService,
    DealCoordinator dealCoordinator
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
