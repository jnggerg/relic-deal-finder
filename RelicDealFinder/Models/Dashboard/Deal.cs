using RelicDealFinder.Enums.WFCD;

namespace RelicDealFinder.Models.Dashboard;

public record Deal(string RelicName, RelicState Refinement, RelicTier Tier, string Seller, decimal Ev, int Ask)
{
    public decimal Profit => Ev - Ask;
}
