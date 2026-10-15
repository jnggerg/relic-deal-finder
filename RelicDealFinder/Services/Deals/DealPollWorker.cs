using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RelicDealFinder.Data;
using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Deals;
using RelicDealFinder.Models.Market;

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

    // relicSlug -> order ids already notified, replaced each poll, so a deal pings once while it stays listed
    private readonly Dictionary<string, HashSet<string>> _notified = [];

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
        var notifier = scope.ServiceProvider.GetRequiredService<DiscordNotifier>();

        foreach (var relic in relics)
        {
            var orders = await market.GetTopSellOrders(relic.Slug, ct);
            var deals = orders is null ? null : DealRule.FindDeals(relic, orders, _options);
            coordinator.ReplaceDeals(relic.Slug, deals);
            if (deals is not null)
                await NotifyNewDealsAsync(notifier, relic.Slug, deals, ct);
        }
    }

    private async Task NotifyNewDealsAsync(
        DiscordNotifier notifier,
        string slug,
        IReadOnlyList<Deal> deals,
        CancellationToken ct
    )
    {
        var good = deals.Where(d => d.Profit >= _options.NotificationProfitThreshold).ToList();
        var seen = _notified.GetValueOrDefault(slug) ?? [];
        var fresh = good.Where(d => !seen.Contains(d.OrderId)).ToList();
        _notified[slug] = [.. good.Select(d => d.OrderId)];

        if (fresh.Count > 0)
            await notifier.SendDealsAsync(fresh, ct);
    }

    private async Task<List<TrackedRelic>> LoadTrackedRelicsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var relics = await db
            .Relics.AsNoTracking()
            .Where(r => r.RadPotentialPlat >= _options.RadEvThreshold)
            .OrderByDescending(r => r.RadPotentialPlat)
            .ToListAsync(ct);
        var parts = await db
            .PrimeParts.AsNoTracking()
            .ToDictionaryAsync(p => p.Slug, p => (p.Name, p.Price), ct);

        return
        [
            .. relics.Select(r => new TrackedRelic(
                r.Slug,
                r.Name(),
                r.Tier,
                r.RadPotentialPlat!.Value,
                Rewards(r, parts)
            )),
        ];
    }

    // rare first; Forma isn't stored in the reward slugs, so missing common/uncommon slots are Forma
    internal static IReadOnlyList<Reward> Rewards(
        MarketRelic relic,
        IReadOnlyDictionary<string, (string Name, double? Price)> parts
    )
    {
        Reward ToReward(string slug, RewardRarity rarity) =>
            parts.TryGetValue(slug, out var p)
                ? new Reward(p.Name, rarity, p.Price)
                : new Reward(slug, rarity, null);

        IEnumerable<Reward> Slots(
            List<string> slugs,
            int count,
            RewardRarity rarity,
            string forma
        ) =>
            slugs
                .Select(s => ToReward(s, rarity))
                .Concat(
                    Enumerable.Repeat(
                        new Reward(forma, rarity, null),
                        Math.Max(0, count - slugs.Count)
                    )
                );

        return
        [
            ToReward(relic.RareRewardSlug, RewardRarity.Rare),
            .. Slots(relic.UncommonRewardSlugs, 2, RewardRarity.Uncommon, "2X Forma Blueprint"),
            .. Slots(relic.CommonRewardSlugs, 3, RewardRarity.Common, "Forma Blueprint"),
        ];
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
