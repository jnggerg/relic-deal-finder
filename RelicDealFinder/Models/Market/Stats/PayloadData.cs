namespace RelicDealFinder.Models.Market.Stats;

public class PayloadData
{
    // Closed statistics are based on actual sales, while the "live" object would contain live listings,
    // so closed results in a more accurate price weighted avarage
    public StatisticsClosed Statistics { get; set; }
}
