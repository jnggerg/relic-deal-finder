using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services;

public interface IRelicDashboardService
{
    Task<DashboardStats> GetStatsAsync();
    Task<IReadOnlyList<Deal>> GetTopDealsAsync(int count);
    Task<IReadOnlyList<EvEntry>> GetHighestRadiantEvAsync(int count);

    // latest refreshRun
    Task<RefreshRun?> GetLastRefreshAsync();
}
