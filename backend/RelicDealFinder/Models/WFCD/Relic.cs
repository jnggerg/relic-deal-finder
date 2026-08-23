namespace RelicDealFinder.Models.WFCD;

using Enums;

public class Relic
{
    public RelicTier Tier { get; set; }
    public string RelicName { get; set; } = null!;
    public RelicState RelicState { get; set; }
    public RelicReward[] Rewards { get; set; } = [];
}
