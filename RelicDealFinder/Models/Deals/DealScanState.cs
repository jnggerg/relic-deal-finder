using RelicDealFinder.Enums.Deals;

namespace RelicDealFinder.Models.Deals;

// Immutable snapshot of the deal scanner, the coordinator swaps in a new one on every change
public record DealScanState(DealScanPhase Phase, int Scanned, int Total, DateTime? NextCycleAt)
{
    public static DealScanState Initial { get; } = new(DealScanPhase.WaitingForRefresh, 0, 0, null);
}
