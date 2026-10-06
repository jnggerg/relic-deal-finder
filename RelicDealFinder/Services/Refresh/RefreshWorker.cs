using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services.Refresh;

// runs the RefreshPipeline in the background whenever the coordinator requests it
public sealed class RefreshWorker(
    RefreshCoordinator coordinator,
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<RefreshWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var _ in coordinator.Requests.ReadAllAsync(stoppingToken))
        {
            var runToken = coordinator.RunToken;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, runToken);

            RefreshRun run;
            try
            {
                // fresh scope per refresh, so scoped services (DbContext) don't outlive the run
                await using var scope = scopeFactory.CreateAsyncScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<IRefreshPipeline>();
                var totals = await pipeline.RunAsync(coordinator, cancellation.Token);
                run = coordinator.CreateRun(totals, null);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                run = coordinator.CreateRun(null, RefreshCoordinator.CancelledReason);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Database refresh failed.");
                run = coordinator.CreateRun(null, e.Message);
            }

            await SaveRunAsync(run, stoppingToken);
            coordinator.Finish(run);
        }
    }

    // own context, since the pipeline's may be left in a broken state by a failure
    private async Task SaveRunAsync(RefreshRun run, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.RefreshRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Saving the refresh run failed.");
        }
    }
}
