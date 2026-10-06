using System.Text.Json;
using System.Text.Json.Serialization;
using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.WFCD;

namespace RelicDealFinder.Services;

public class WfcdService(
    HttpClient wfcdClient,
    ILogger<WfcdService> logger
)
{
    /* Currently, there is a typo in WFMs Item database in the slug for one item:
    * "Kompressa Prime Receiver" is spelled as "Reciever".
    */
    private static readonly Dictionary<string, string> SlugOverrides = new()
    {
        ["kompressa_prime_receiver"] = "kompressa_prime_reciever",
    };

    private static readonly JsonSerializerOptions WfcdJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<List<Relic>?> GetAllWfcdRelics(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching relics from WFCD...");
        var relics = await wfcdClient.GetFromJsonAsync<RelicsResponse>(
            "https://raw.githubusercontent.com/WFCD/warframe-drop-data/main/data/relics.json",
            WfcdJsonOptions,
            ct
        );
        return NormalizeRarity(relics?.Relics);
    }

    public List<MarketRelic>? MatchMarketIdsToRelicRewards(
        List<Relic> normalizedIntactRelics,
        List<MarketItem> rawMarketRelics,
        List<PrimePart> primeParts
    )
    {
        if (primeParts.Count == 0)
        {
            logger.LogInformation("Primeparts table empty");
            return null;
        }

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

                var partSlug = primeParts
                    .Where(i => i.Slug == ToSlug(reward.ItemName))
                    .Select(i => i.Slug)
                    .FirstOrDefault();
                if (partSlug is null)
                    continue;
                switch (reward.RelicRewardRarity)
                {
                    case RewardRarity.Common:
                        commonRewards.Add(partSlug);
                        break;
                    case RewardRarity.Uncommon:
                        unCommonRewards.Add(partSlug);
                        break;
                    case RewardRarity.Rare:
                        rareReward = partSlug;
                        break;
                }
            }

            var relicSlug = rawMarketRelics
                .Where(x => x.Slug == ConvertRelicWfcdNameToMarketSlug(relic.Tier, relic.RelicName))
                .Select(x => x.Slug)
                .FirstOrDefault();

            if (relicSlug is null)
                continue;
            marketRelics.Add(
                new MarketRelic
                {
                    Slug = relicSlug,
                    CommonRewardSlugs = commonRewards,
                    UncommonRewardSlugs = unCommonRewards,
                    RareRewardSlug = rareReward,
                }
            );
        }

        return marketRelics;
    }

    /*  Often times WFCD stores relic rarity based on its rarity in other relics,
     *  e.g. something that's a Rare reward in relic X can be uncommon in Y (65 ducat parts)
     *  We need to normalize for this based on the actual drop chance in the relic accordingly.
     */
    private List<Relic>? NormalizeRarity(List<Relic>? relics)
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

        logger.LogInformation(
            "Normalized relic rarity. ALl intact relics: {intactRelics}",
            intactRelics
        );
        return intactRelics;
    }

    private static string ToSlug(string name)
    {
        var slug = string.Join(
            '_',
            name.Replace("&", "and")
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );

        return SlugOverrides.GetValueOrDefault(slug, slug);
    }

    /*  WFCD stores Relic identifiers as "tier": "axi | meso | etc.", and "relicName": "A11",
     *  while Market slugs are snake case strings like "axi_a11_relic"
     */
    private static string ConvertRelicWfcdNameToMarketSlug(RelicTier tier, string name) =>
        ToSlug($"{tier} {name} relic");
}
