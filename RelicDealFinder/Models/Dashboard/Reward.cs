using RelicDealFinder.Enums.WFCD;

namespace RelicDealFinder.Models.Dashboard;

// Price: the part's market price from the last refresh; null for Forma and unpriced parts
public record Reward(string Name, RewardRarity Rarity, double? Price);
