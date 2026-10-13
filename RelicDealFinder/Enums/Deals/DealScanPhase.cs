namespace RelicDealFinder.Enums.Deals;

public enum DealScanPhase
{
    WaitingForRefresh, // no successful refresh, or its data is older than MaxDataAge
    Paused, // a refresh is running
    Scanning,
    Idle, // waiting CycleGap before the next cycle
}
