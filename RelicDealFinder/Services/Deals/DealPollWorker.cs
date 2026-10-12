using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Deals;

namespace RelicDealFinder.Services.Deals;

/*
 * Polls the top sell orders of every relic above RadEvThreshold, one request at a time,
 * then waits CycleGap and starts over. Requests are awaited one by one, so a refresh only ever
 * waits behind a single poll request in the shared rate limiter's queue.
 */
public sealed class DealPollWorker(
    DealCoordinator coordinator,
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<DealOptions> options,
    ILogger<DealPollWorker> logger
) : BackgroundService
{
    private readonly DealOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        coordinator.Initialize(await LoadLastRefreshAsync(stoppingToken));

        while (!stoppingToken.IsCancellationRequested)
        {
            var scanToken = await coordinator.WaitUntilScanAllowedAsync(stoppingToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken,
                scanToken
            );

            try
            {
                await RunCycleAsync(cancellation.Token);
                coordinator.EnterIdle(DateTime.Now + _options.CycleGap);
                await Task.Delay(_options.CycleGap, cancellation.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // paused by a refresh, wait for it to end
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Deal scan cycle failed.");
                coordinator.EnterIdle(DateTime.Now + _options.CycleGap);
                await Task.Delay(_options.CycleGap, stoppingToken);
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var relics = await LoadTrackedRelicsAsync(ct);
        coordinator.StartCycle(relics.Count);

        // fresh scope per cycle, so the typed HttpClient isn't held for the app's lifetime
        await using var scope = scopeFactory.CreateAsyncScope();
        var market = scope.ServiceProvider.GetRequiredService<MarketService>();

        foreach (var relic in relics)
        {
            var orders = await market.GetTopSellOrders(relic.Slug, ct);
            coordinator.ReplaceDeals(
                relic.Slug,
                orders is null ? null : DealRule.FindDeals(relic, orders, _options)
            );
        }
    }

    private async Task<List<TrackedRelic>> LoadTrackedRelicsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var relics = await db
            .Relics.AsNoTracking()
            .Where(r => r.RadPotentialPlat >= _options.RadEvThreshold)
            .OrderByDescending(r => r.RadPotentialPlat)
            .ToListAsync(ct);

        return [.. relics.Select(r => new TrackedRelic(r.Slug, r.Name(), r.Tier, r.RadPotentialPlat!.Value))];
    }

    private async Task<DateTime?> LoadLastRefreshAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .RefreshRuns.AsNoTracking()
            .Where(r => r.Succeeded)
            .OrderByDescending(r => r.Id) // id is auto-increment, so latest is highest id
            .Select(r => (DateTime?)r.FinishedAt)
            .FirstOrDefaultAsync(ct);
    }
}
