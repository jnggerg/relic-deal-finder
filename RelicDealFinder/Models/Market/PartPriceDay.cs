namespace RelicDealFinder.Models.Market;

/*  One completed UTC day of closed trades for a prime part, used for the trend chart.
 *  Days without trades are stored too, with Volume 0 and a null price,
 *  so they don't drag averages towards 0.
 */
public class PartPriceDay
{
    public string Slug { get; set; } = null!;
    public DateOnly Date { get; set; }
    public double? WaPrice { get; set; }
    public int Volume { get; set; }
}
