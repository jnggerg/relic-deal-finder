using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services;

public class RelicDashboardService(
    IDbContextFactory<AppDbContext> dbFactory,
    StatisticsService statService
) : IRelicDashboardService
{
    //TODO: REPLACE MOCK DATA
    private static readonly Deal[] Deals =
    [
        new("Axi V10", RelicState.Radiant, RelicTier.Axi, "TestUser1", 28.1, 17),
        new("Neo S13", RelicState.Radiant, RelicTier.Neo, "TestUser2", 16.3, 11),
        new("Lith V8", RelicState.Radiant, RelicTier.Lith, "TestUser3", 16.7, 13),
        new("Meso N16", RelicState.Radiant, RelicTier.Meso, "TestUser4", 11.4, 8),
        new("Axi A15", RelicState.Intact, RelicTier.Axi, "TestUser5", 5.5, 3),
    ];

    public Task<DashboardStats> GetStatsAsync() =>
        Task.FromResult(new DashboardStats(8, 5, 11.1, "Axi V10 Radiant", 32, 1204, 0));

    public Task<IReadOnlyList<Deal>> GetTopDealsAsync(int count) =>
        Task.FromResult<IReadOnlyList<Deal>>(
            [.. Deals.OrderByDescending(d => d.Profit).Take(count)]
        );

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
