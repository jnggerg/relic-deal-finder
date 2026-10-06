using RelicDealFinder.Enums.Refresh;

namespace RelicDealFinder.Models.Refresh;

public record LogEntry(DateTime Time, LogEntryLevel Level, string Message);
