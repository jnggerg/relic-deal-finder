using RelicDealFinder.Enums.WFCD;

namespace RelicDealFinder.Models.Dashboard;

public record Deal(
    string OrderId,
    string Slug,
    string RelicName,
    RelicState Refinement,
    RelicTier Tier,
    string Seller,
    bool SellerInGame,
    double Ev,
    int Ask, // raw listed price
    int EffectiveCost, // Ask, plus the refinement cost to Radiant
    int Quantity,
    IReadOnlyList<Reward> Rewards // rare first, commons last
)
{
    public double Profit => Ev - EffectiveCost;
}
