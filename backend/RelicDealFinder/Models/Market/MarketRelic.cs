namespace RelicDealFinder.Models.Market;

public class MarketRelic
{
    /*  This class is for the Market version of a Relic,
     *  only containing its own slug and reward prime part slugs
     *  for market API lookups. A single relic always has:
     *  3 common, 2 uncommon rewards and 1 rare.
     *  sidenote: Forma rewards not yet handled
     */
    public string Slug { get; set; } = null!;
    public List<string> CommonRewardSlugs { get; set; } = [];
    public List<string> UncommonRewardSlugs { get; set; } = [];
    public string RareRewardSlug { get; set; } = null!;
}
