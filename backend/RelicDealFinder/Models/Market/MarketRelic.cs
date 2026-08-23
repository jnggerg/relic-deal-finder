namespace RelicDealFinder.Models.Market;

public class MarketRelic
{
    public string Id { get; set; } = null!;
    public string[] CommonRewardIds { get; set; } = [];
    public string[] UncommonRewardIds { get; set; } = [];
    public string RareRewardId { get; set; } = null!;
}
