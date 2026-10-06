namespace RelicDealFinder.Models.Dashboard;

public record DashboardStats(
    int DealsBelowEv,
    int SellersInGame,
    decimal BestMargin,
    string BestMarginRelic,
    decimal CombinedUpside,
    int PartsPriced,
    int StaleParts
);
