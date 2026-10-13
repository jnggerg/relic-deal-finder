using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Dashboard;

namespace RelicDealFinder.Models.Deals;

// a relic the deal scanner polls, with the Radiant EV its orders are judged against
public record TrackedRelic(
    string Slug,
    string Name,
    RelicTier Tier,
    double RadEv,
    IReadOnlyList<Reward> Rewards
);
