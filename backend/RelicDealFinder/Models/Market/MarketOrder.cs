using RelicDealFinder.Enums;

namespace RelicDealFinder.Models.Market;

public class MarketOrder
{
    public string Id { get; set; } = null!;
    public OrderType Type { get; set; }
    public int Platinum { get; set; } // price
    public int Quantity { get; set; }
    public string ItemId { get; set; } = null!;
    public UserShort User { get; set; } = null!;
    public bool Visible { get; set; } = true;
    public int Rank { get; set; } // naively assume this is a relics rank for now
}
