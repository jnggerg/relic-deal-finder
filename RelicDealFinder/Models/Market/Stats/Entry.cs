namespace RelicDealFinder.Models.Market.Stats;

public class Entry
{
    public DateTime Datetime { get; set; } // ISO 8601 timestamps, UTC
    public int Volume { get; set; } // needed to get our own weighted avg
    public double WaPrice { get; set; } // WDF already gives a weighted average price in a single entry
}
