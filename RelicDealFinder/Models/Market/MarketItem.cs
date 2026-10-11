using System.ComponentModel.DataAnnotations.Schema;

namespace RelicDealFinder.Models.Market;

public class MarketItem
{
    public string Id { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public bool Vaulted { get; set; } = false;
    public string GameRef { get; set; } = null!;
    public double? Price { get; set; }

    // only used to fill the name, localised to en
    [NotMapped]
    public Dictionary<string, MarketItemI18n> I18n { get; set; } = [];
}
