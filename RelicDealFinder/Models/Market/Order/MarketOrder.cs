namespace RelicDealFinder.Models.Market.Order;

using RelicDealFinder.Enums.Market;
using RelicDealFinder.Enums.WFCD;

public class MarketOrder
{
    public string Id { get; set; } = null!;
    public OrderType Type { get; set; }
    public int Platinum { get; set; } // price
    public int Quantity { get; set; }
    public int PerTrade { get; set; }
    public string ItemId { get; set; } = null!;
    public UserShort User { get; set; } = null!;
    public bool Visible { get; set; } = true;

    // relic refinement; null for items without subtypes
    public RelicState? Subtype { get; set; }
}
