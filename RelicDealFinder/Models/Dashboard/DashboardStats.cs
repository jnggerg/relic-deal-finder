namespace RelicDealFinder.Models.Dashboard;

public record DashboardStats(
    int DealsBelowEv,
    int SellersInGame,
    double BestMargin,
    string BestMarginRelic,
    double CombinedUpside,
    int PartsPriced,
    int StaleParts
);
