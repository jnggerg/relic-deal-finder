using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Dashboard;

namespace RelicDealFinder.Services;

/*  Builds the "Top parts" rows from the last refresh.
 *  Everything is small (~700 parts × 7 days, a few hundred relics), so it's loaded in three
 *  queries and joined in memory; relic rewards are slug lists, which SQLite can't search anyway.
 */
public class TopPartsService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<IReadOnlyList<PrimePart>> GetTopPartsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var parts = await db.PrimeParts.AsNoTracking().Where(p => p.Price > 0).ToListAsync();
        var days = (await db.PartPriceDays.AsNoTracking().OrderBy(d => d.Date).ToListAsync())
            .ToLookup(d => d.Slug);
        var relics = await db.Relics.AsNoTracking().ToListAsync();

        // part slug -> every relic that drops it
        var drops = relics
            .SelectMany(r =>
                r.CommonRewardSlugs.Select(s => (Slug: s, Rarity: RewardRarity.Common))
                    .Concat(r.UncommonRewardSlugs.Select(s => (Slug: s, Rarity: RewardRarity.Uncommon)))
                    .Append((Slug: r.RareRewardSlug, Rarity: RewardRarity.Rare))
                    .Select(x => (x.Slug, Drop: new RelicDrop(r.Name(), r.Tier, x.Rarity)))
            )
            .ToLookup(x => x.Slug, x => x.Drop);

        return
        [
            .. parts.Select(p =>
            {
                var partDays = days[p.Slug].ToList();
                return new PrimePart(
                    p.Name,
                    p.Slug,
                    p.Price!.Value,
                    partDays.Sum(d => d.Volume),
                    Change(partDays.Select(d => d.WaPrice)),
                    [.. partDays.Select(d => d.WaPrice)],
                    [.. drops[p.Slug].OrderBy(d => d.Tier).ThenBy(d => d.RelicName)]
                );
            }),
        ];
    }

    // first traded day -> last traded day; null when there aren't two days to compare
    private static double? Change(IEnumerable<double?> prices)
    {
        var traded = prices.OfType<double>().ToList();
        if (traded.Count < 2 || traded[0] <= 0)
            return null;
        return (traded[^1] - traded[0]) / traded[0] * 100;
    }
}
