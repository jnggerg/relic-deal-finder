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
        List<Relic> normalizedIntactRelics,
        List<MarketItem> rawMarketRelics
    )
    {
        var primeParts = await db.PrimeParts.ToListAsync();

        if (primeParts.Count == 0 || normalizedIntactRelics.Count == 0)
            return null;

        List<MarketRelic> marketRelics = [];
        foreach (var relic in normalizedIntactRelics)
        {
            List<string> commonRewards = [];
            List<string> unCommonRewards = [];
            var rareReward = "";

            foreach (var reward in relic.Rewards)
            {
                /*
                * Forma blueprints are untradeable and should not be accounted for, so we skip the reward if it's a forma.
                * note: this will never be an issue with the respective reward var initialization, since 1 relic may only contain 1 forma reward at most,
                * with that being either in uncommon or common rewards list, never the singular rare.
                */
                if (reward.ItemName == "Forma Blueprint" || reward.ItemName == "2X Forma Blueprint")
                    continue;

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
                        rareReward = partMarketId;
                        break;
                }
            }

            var relicMarketId = rawMarketRelics
                .Where(x => x.Slug == ConvertRelicWfcdNameToMarketSlug(relic.Tier, relic.RelicName))
                .Select(x => x.Id)
                .FirstOrDefault();

            if (relicMarketId is null)
                continue;
            marketRelics.Add(
                new MarketRelic
                {
                    Id = relicMarketId,
                    CommonRewardIds = commonRewards,
                    UncommonRewardIds = unCommonRewards,
                    RareRewardId = rareReward,
                }
            );
        }

        return marketRelics;
    }

    public async Task PersistRelics(List<MarketItem> rawMarketRelics)
    {
        var normalizedIntactRelics = await GetAllWfcdRelics();
        if (normalizedIntactRelics is null)
            return;

        var marketRelics = await MatchMarketIdsToRelicRewards(
            normalizedIntactRelics,
            rawMarketRelics
        );

        if (marketRelics is null)
            return;

        db.Relics.AddRange(marketRelics);
        await db.SaveChangesAsync();
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

    /*  WFCD stores Relic identifiers as "tier": "axi | meso | etc.", and "relicName": "A11",
     *  while Market slugs are snake case strings like "axi_a11_relic"
     */
    private static string ConvertRelicWfcdNameToMarketSlug(RelicTier tier, string name) =>
        ToSlug($"{tier} {name} relic");
}
