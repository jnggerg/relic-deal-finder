namespace RelicDealFinder.Models.Market;

public class MarketRelic
{
    /*  This class is for the Market version of a Relic,
     *  only containing its own id and reward prime part Ids
     *  for market API lookups. A single relic always has:
     *  3 common, 2 uncommon rewards and 1 rare.
     *  sidenote: Forma rewards not yet handled
     */
    public string Id { get; set; } = null!;
    public List<string> CommonRewardIds { get; set; } = [];
    public List<string> UncommonRewardIds { get; set; } = [];
    public string RareRewardId { get; set; } = null!;
}
