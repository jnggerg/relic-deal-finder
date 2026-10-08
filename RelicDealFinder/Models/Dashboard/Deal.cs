using RelicDealFinder.Enums.WFCD;

namespace RelicDealFinder.Models.Dashboard;

public record Deal(string RelicName, RelicState Refinement, RelicTier Tier, string Seller, double Ev, int Ask)
{
    public double Profit => Ev - Ask;
}
