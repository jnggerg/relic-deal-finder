using RelicDealFinder.Enums.WFCD;

namespace RelicDealFinder.Models.WFCD;

public class Relic
{
    public RelicTier Tier { get; set; }
    public string RelicName { get; set; } = null!;
    public RelicReward[] Rewards { get; set; } = [];
    public RelicState State { get; set; }
}
