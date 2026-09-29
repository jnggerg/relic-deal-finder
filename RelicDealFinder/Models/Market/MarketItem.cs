namespace RelicDealFinder.Models.Market;

public class MarketItem
{
    public string Id { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string[] Tags { get; set; } = [];
    public bool Vaulted { get; set; } = false;
    public string GameRef { get; set; } = null!;
}
