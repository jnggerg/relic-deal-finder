using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Enums;
using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.WFCD;

namespace RelicDealFinder.Services;

public class WfcdService(HttpClient wfcdClient, AppDbContext db)
{
    private async Task<List<Relic>?> GetAllWfcdRelics()
    {
        var relics = await wfcdClient.GetFromJsonAsync<List<Relic>>(
            "https://raw.githubusercontent.com/WFCD/warframe-drop-data/main/data/relics.json"
        );
        return NormalizeRarity(relics);
    }

    private async Task<List<MarketRelic>?> MatchMarketIdsToRelicRewards(
        List<Relic> normalizedIntactRelics
    )
    {
        var primeParts = await db.PrimeParts.ToListAsync();

        if (primeParts.Count != normalizedIntactRelics.Count)
            return null;

        List<MarketRelic> marketRelics = [];
        foreach (var relic in normalizedIntactRelics)
        {
            List<string> commonRewards = [];
            List<string> unCommonRewards = [];
            List<string> rareRewards = [];

            foreach (var reward in relic.Rewards)
            {
                var partMarketId = primeParts
                    .Where(i => i.Slug == ToSlug(reward.ItemName))
                    .Select(i => i.Id)
                    .FirstOrDefault();
                if (partMarketId is null)
                    continue;
                switch (reward.RelicRewardRarity)
                {
                    case RewardRarity.Common:
                        commonRewards.Add(partMarketId);
                        break;
                    case RewardRarity.Uncommon:
                        unCommonRewards.Add(partMarketId);
                        break;
                    case RewardRarity.Rare:
                        rareRewards.Add(partMarketId);
                        break;
                }
            }
        }

        return marketRelics;
    }

    public List<MarketRelic> PersistRelics(List<MarketItem> relics)
    {
        var normalizedIntactRelics = GetAllWfcdRelics();
    }

    /*  Often times WFCD stores relic rarity based on its rarity in other relics,
     *  e.g. something that's a Rare reward in relic X can be uncommon in Y (65 ducat parts)
     *  We need to normalize for this based on the actual drop chance in the relic accordingly.
     */
    private static List<Relic>? NormalizeRarity(List<Relic>? relics)
    {
        if (relics is null)
            return null;

        // we only need the data from Intact relics, so normalizing based on drop chance is consistent
        List<Relic> intactRelics = [.. relics.Where(i => i.State == RelicState.Intact)];

        foreach (var reward in intactRelics.SelectMany(i => i.Rewards))
        {
            /* Common chance should be 25.33 on intact relics,
             *  Uncommon 11 and Rare 2. Using these ranges in a switch
             *  is safer than comparing doubles or strings in a Dictionary
             */
            reward.RelicRewardRarity = reward.Chance switch
            {
                >= 20 => RewardRarity.Common,
                >= 10 => RewardRarity.Uncommon,
                _ => RewardRarity.Rare,
            };
        }

        return intactRelics;
    }

    private static string ToSlug(string name) =>
        string.Join('_', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
