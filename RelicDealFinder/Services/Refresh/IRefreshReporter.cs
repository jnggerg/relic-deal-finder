using RelicDealFinder.Enums.Refresh;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services.Refresh;

public interface IRefreshReporter
{
    void Begin(IReadOnlyList<RefreshStepDefinition> steps);
    void StartStep(int index, int total);
    void Advance(int count = 1);
    void CompleteStep();
    void ReportRetry();
    void ReportError();
    void Log(LogEntryLevel level, string message);
}
