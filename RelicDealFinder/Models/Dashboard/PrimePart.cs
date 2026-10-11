namespace RelicDealFinder.Models.Dashboard;

// DailyPrices: the last 7 completed days, oldest first; null on days without trades
// Change7dPct: null when there aren't two traded days to compare
public record PrimePart(
    string Name,
    string Slug,
    double Vwap,
    int Volume7d,
    double? Change7dPct,
    IReadOnlyList<double?> DailyPrices,
    IReadOnlyList<RelicDrop> DroppedBy
);
