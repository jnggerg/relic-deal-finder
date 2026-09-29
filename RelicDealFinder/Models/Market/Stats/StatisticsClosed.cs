namespace RelicDealFinder.Models.Market.Stats;

public class StatisticsClosed
{
    // we use the 90 days array of entries to get our own 7 day price.
    // the array contains one entry for each day, assuming that there has been a sale in every day
    public List<Entry> NinetyDays { get; set; }
}
