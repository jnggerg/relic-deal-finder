using System.Text.Json.Serialization;

namespace RelicDealFinder.Models.Market.Stats;

public class StatisticsClosed
{
    // we use the 90 days array of entries to get our own 7 day price.
    // the array contains one entry for each day, assuming that there has been a sale in every day
    // the key starts with a digit, so the snake_case naming policy can't map it ("ninety_days")
    [JsonPropertyName("90days")]
    public List<Entry> NinetyDays { get; set; } = [];
}
