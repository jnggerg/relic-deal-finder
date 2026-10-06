namespace RelicDealFinder.Enums.Refresh;

// Not Microsoft.Extensions.Logging.LogLevel, which is globally imported and would clash
public enum LogEntryLevel
{
    Info,
    Warning,
    Skip,
    Error,
}
