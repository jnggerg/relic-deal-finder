namespace RelicDealFinder.Models.Refresh;

public class RefreshRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    public int PriceLookups { get; set; }
    public int RelicCount { get; set; }
    public int Errors { get; set; }
    public int Retries { get; set; }

    public TimeSpan Duration => FinishedAt - StartedAt;
}
