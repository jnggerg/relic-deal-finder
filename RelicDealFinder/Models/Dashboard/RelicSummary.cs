using RelicDealFinder.Enums.WFCD;

namespace RelicDealFinder.Models.Dashboard;

// IntEv / RadEv: squad (4-player radshare) EVs from the last refresh, 0 when unpriced
// Rewards: rare first, commons last
public record RelicSummary(
    string Slug,
    string Name,
    RelicTier Tier,
    double IntEv,
    double RadEv,
    IReadOnlyList<Reward> Rewards
)
{
    public Reward Rare => Rewards[0];
}
