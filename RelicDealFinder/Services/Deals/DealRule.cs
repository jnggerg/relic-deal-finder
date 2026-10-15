using RelicDealFinder.Enums.Market;
using RelicDealFinder.Enums.WFCD;
using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Deals;
using RelicDealFinder.Models.Market.Order;

namespace RelicDealFinder.Services.Deals;

/*
 * Every order is valued as if it was refined to Radiant: non-Radiant asks get IntactToRadiantPlat added.
 * Exceptional and Flawless count as Intact, which is conservative.
 * This is also why one /top call (mixed refinements, 5 orders) per relic is enough:
 * a Radiant deal can only be hidden behind 5 cheaper non-Radiant orders, which are then deals too.
 */
public static class DealRule
{
    public static IReadOnlyList<Deal> FindDeals(
        TrackedRelic relic,
        IEnumerable<MarketOrder> orders,
        DealOptions options
    ) =>
        [
            .. orders
                .Where(o => o is { Type: OrderType.Sell, Visible: true })
                .Select(o => ToDeal(relic, o, options))
                .Where(d => d.Profit >= options.MinProfit),
        ];

    public static Deal ToDeal(TrackedRelic relic, MarketOrder order, DealOptions options)
    {
        // a missing subtype is treated as Intact
        var refinement = order.Subtype ?? RelicState.Intact;
        var cost =
            refinement == RelicState.Radiant
                ? order.Platinum
                : order.Platinum + options.IntactToRadiantPlat;

        return new Deal(
            order.Id,
            relic.Slug,
            relic.Name,
            refinement,
            relic.Tier,
            order.User.InGameName,
            order.User.Status == "ingame",
            relic.RadEv,
            order.Platinum,
            cost,
            order.Quantity,
            relic.Rewards
        );
    }
}
