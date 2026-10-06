namespace RelicDealFinder.Models.Refresh;

// "Used" permits taken in the current window.
// Queued Requests still waiting to be sent.
// TODO: Tie this into Polly used by outward api calls to show status on UI
public record RateLimiterState(int Used, int Capacity, TimeSpan Window, int Queued)
{
    public static RateLimiterState Empty { get; } = new(0, 0, TimeSpan.Zero, 0);
}
