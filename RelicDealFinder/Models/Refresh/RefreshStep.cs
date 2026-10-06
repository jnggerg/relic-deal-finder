using RelicDealFinder.Enums.Refresh;

namespace RelicDealFinder.Models.Refresh;

public record RefreshStepDefinition(
    string Title,
    string Description,
    string ActionLabel,
    string ProgressLabel,
    double Weight // expected step duration, in seconds
);

public record RefreshStep(
    RefreshStepDefinition Definition,
    StepStatus Status,
    int Completed,
    int Total,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    string? FailureReason = null
)
{
    public static RefreshStep Queued(RefreshStepDefinition definition) =>
        new(definition, StepStatus.Queued, 0, 0, null, null);

    public double Fraction =>
        Status switch
        {
            StepStatus.Done => 1,
            _ when Total > 0 => Math.Min(1, (double)Completed / Total),
            _ => 0,
        };

    // getter property for calculating Duration:
    // if StartedAt is not null -> stored into 's' && FinishedAt is not null -> stored into f, return f - s, else null
    public TimeSpan? Duration => StartedAt is { } s && FinishedAt is { } f ? f - s : null;
}
