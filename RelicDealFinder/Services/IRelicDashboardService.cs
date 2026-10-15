using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services;

public interface IRelicDashboardService
{
    Task<DashboardStats> GetStatsAsync();
    Task<IReadOnlyList<Deal>> GetTopDealsAsync(int count);
    Task<IReadOnlyList<EvEntry>> GetHighestRadiantEvAsync(int count);
    Task<IReadOnlyList<PrimePart>> GetTopPartsAsync();
    Task<IReadOnlyList<RelicSummary>> GetRelicsAsync();

    // live top sell orders valued as deals against the Radiant EV; null if the request failed
    Task<IReadOnlyList<Deal>?> GetSellOrdersAsync(RelicSummary relic, CancellationToken ct);

    // latest refreshRun
    Task<RefreshRun?> GetLastRefreshAsync();
}
