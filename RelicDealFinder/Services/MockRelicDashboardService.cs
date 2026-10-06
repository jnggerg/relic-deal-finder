using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Dashboard;
using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services;

// Everything is mock data except GetLastRefreshAsync, which reads the RefreshRuns table
public class MockRelicDashboardService(IDbContextFactory<AppDbContext> dbFactory) : IRelicDashboardService
{
    //TODO: REPLACE MOCK DATA
    private static readonly Deal[] Deals =
    [
        new("Axi V10", RelicState.Radiant, RelicTier.Axi, "TestUser1", 28.1m, 17),
        new("Neo S13", RelicState.Radiant, RelicTier.Neo, "TestUser2", 16.3m, 11),
        new("Neo V8", RelicState.Radiant, RelicTier.Neo, "TestUser3", 16.7m, 13),
        new("Meso N16", RelicState.Radiant, RelicTier.Meso, "TestUser4", 11.4m, 8),
        new("Axi A15", RelicState.Intact, RelicTier.Axi, "TestUser5", 5.5m, 3),
    ];

    private static readonly EvEntry[] RadiantEv =
    [
        new("Axi V10", RelicTier.Axi, 28.1m),
        new("Neo V8", RelicTier.Neo, 16.7m),
        new("Neo S13", RelicTier.Neo, 16.3m),
        new("Axi A15", RelicTier.Axi, 12.4m),
        new("Meso N16", RelicTier.Meso, 11.4m),
    ];

    public Task<DashboardStats> GetStatsAsync() =>
        Task.FromResult(new DashboardStats(8, 5, 11.1m, "Axi V10 Radiant", 32m, 1204, 0));

    public Task<IReadOnlyList<Deal>> GetTopDealsAsync(int count) =>
        Task.FromResult<IReadOnlyList<Deal>>(
            Deals.OrderByDescending(d => d.Profit).Take(count).ToList()
        );

    public Task<IReadOnlyList<EvEntry>> GetHighestRadiantEvAsync(int count) =>
        Task.FromResult<IReadOnlyList<EvEntry>>(
            RadiantEv.OrderByDescending(e => e.Value).Take(count).ToList()
        );

    // Context per call: this service lives for the whole circuit and components may call it concurrently
    public async Task<RefreshRun?> GetLastRefreshAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db
            .RefreshRuns.AsNoTracking()
            .Where(r => r.Succeeded)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync();
    }
}
