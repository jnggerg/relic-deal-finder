using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services.Refresh;

// Resolved from a fresh DI scope per run, so implementations can
// depend on scoped services like the DbContext. Throwing marks the refresh as failed.
public interface IRefreshPipeline
{
    Task<RefreshTotals> RunAsync(IRefreshReporter reporter, CancellationToken cancellationToken);
}
