using RelicDealFinder.Enums.Refresh;

namespace RelicDealFinder.Models.Refresh;

// Immutable snapshot of the refresh
// the coordinator swaps in a new one on every change.
public record RefreshState(
    RefreshPhase Phase,
    IReadOnlyList<RefreshStep> Steps,
    RateLimiterState RateLimiter,
    int Errors,
    int Retries,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    bool CancelRequested = false
)
{
    public static RefreshState Idle { get; } =
        new(RefreshPhase.Idle, [], RateLimiterState.Empty, 0, 0, null, null);

    public bool IsRunning => Phase == RefreshPhase.Running;

    public int CurrentIndex
    {
        get
        {
            foreach (
                var status in (StepStatus[])
                    [StepStatus.Active, StepStatus.Failed, StepStatus.Queued]
            )
            {
                for (var i = 0; i < Steps.Count; i++)
                {
                    if (Steps[i].Status == status)
                        return i;
                }
            }
            return -1;
        }
    }

    public RefreshStep? CurrentStep => CurrentIndex is var i and >= 0 ? Steps[i] : null;

    // Overall progress across all steps, weighted by each steps cost
    public double Progress
    {
        get
        {
            if (Phase == RefreshPhase.Completed)
                return 1;
            var total = Steps.Sum(s => s.Definition.Weight);
            return total <= 0 ? 0 : Steps.Sum(s => s.Definition.Weight * s.Fraction) / total;
        }
    }

    public TimeSpan Elapsed(DateTime now) =>
        StartedAt is { } start ? (FinishedAt ?? now) - start : TimeSpan.Zero;

    public TimeSpan? EstimateRemaining(DateTime now)
    {
        var progress = Progress;
        var elapsed = Elapsed(now);
        if (!IsRunning || progress < 0.01 || elapsed < TimeSpan.FromSeconds(3))
            return null;
        return elapsed * ((1 - progress) / progress);
    }
}
