using RelicDealFinder.Enums.WFCD;

namespace RelicDealFinder.Models.WFCD;
public class RelicReward
{
    public string Id { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public RewardRarity RelicRewardRarity { get; set; }
    public double Chance { get; set; }
}