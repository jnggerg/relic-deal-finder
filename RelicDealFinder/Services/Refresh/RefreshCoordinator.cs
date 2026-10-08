using System.Threading.Channels;
using RelicDealFinder.Enums.Refresh;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services.Refresh;

public sealed class RefreshCoordinator : IRefreshReporter, IDisposable
{
    /*
     * This singleton class is responsible for coordinating the refresh pipeline, including:
     * Handling UI interactions (start, cancel), Updating UI, keeping track of refresh State
     *
     * The class is implemented thread-safe using a lock and atomic swaps (on dirty flag), since
     * the workers run on separate threads.
     *
     * The class using a dirty flag with a 250ms NotifyInterval, so we dont re-render on every single change in the pipeline
     * and flood our SignalR connection (this is mostly effected by the price calculation, which logs every price calculated for relics).
     * The changes only raise a flag (with MarkDirty, an atomic swap on the flag), and every 250ms the timer checks if it was raised,
     * then clears the flag and signals the UI.
     * For some important events, we can circumvent the timer using the NotifyNow() method.
     */
    private const int MaxLogEntries = 200;
    private static readonly TimeSpan NotifyInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(1);
    private readonly Queue<LogEntry> _log = new();
    private readonly Timer _notifyTimer;
    private int _dirty;

    public RefreshCoordinator()
    {
        _notifyTimer = new Timer(_ => FlushPendingChange(), null, NotifyInterval, NotifyInterval);
    }

    public RefreshState State { get; private set; } = RefreshState.Idle;

    // last run since appStart, else last stored RefreshRun
    public RefreshRun? LastRun { get; private set; }

    public bool LastRunCancelled => LastRun is { Succeeded: false, FailureReason: CancelledReason };

    public event Action? StateChanged;

    internal ChannelReader<bool> Requests => _requests.Reader;

    public const string CancelledReason = "Cancelled by user";

    private CancellationTokenSource _runCts = new();

    // internal, since its used outside of this class, however only this module should read it
    internal CancellationToken RunToken
    {
        get
        {
            lock (_lock)
                return _runCts.Token;
        }
    }

    public bool TryStart()
    {
        lock (_lock)
        {
            if (State.IsRunning)
                return false;

            State = RefreshState.Idle with
            {
                Phase = RefreshPhase.Running,
                Steps = State.Steps.Select(s => RefreshStep.Queued(s.Definition)).ToList(),
                StartedAt = DateTime.Now,
            };
            // the previous run's linked token source is disposed by now
            _runCts.Dispose();
            _runCts = new CancellationTokenSource();
            _log.Clear();
            AppendLog(LogEntryLevel.Info, "refresh started");
            _requests.Writer.TryWrite(true);
        }

        NotifyNow();
        return true;
    }

    // Asks running refresh to stop
    public bool Cancel()
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (!State.IsRunning || State.CancelRequested)
                return false;
            State = State with { CancelRequested = true };
            AppendLog(LogEntryLevel.Warning, "cancel requested");
            cts = _runCts;
        }

        cts.Cancel();
        NotifyNow();
        return true;
    }

    public IReadOnlyList<LogEntry> GetLog()
    {
        lock (_lock)
            return _log.ToArray();
    }

    // build a record of the Current run, with totals if successful
    internal RefreshRun CreateRun(RefreshTotals? totals, string? failureReason)
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            return new RefreshRun
            {
                StartedAt = State.StartedAt ?? now,
                FinishedAt = now,
                Succeeded = failureReason is null,
                FailureReason = failureReason,
                PriceLookups = totals?.PriceLookups ?? 0,
                RelicCount = totals?.RelicCount ?? 0,
                Errors = State.Errors,
                Retries = State.Retries,
            };
        }
    }

    // ends the run and updates the State (after it has already been persisted into db)
    internal void Finish(RefreshRun run)
    {
        lock (_lock)
        {
            if (run.Succeeded)
            {
                State = State with { Phase = RefreshPhase.Completed, FinishedAt = run.FinishedAt };
                AppendLog(LogEntryLevel.Info, "refresh complete");
            }
            else
            {
                var index = State.CurrentIndex;
                var steps =
                    index < 0
                        ? State.Steps
                        : Replace(
                            State.Steps,
                            index,
                            step =>
                                step with
                                {
                                    Status = StepStatus.Failed,
                                    StartedAt = step.StartedAt ?? run.FinishedAt,
                                    FinishedAt = run.FinishedAt,
                                    FailureReason = run.FailureReason,
                                }
                        );
                State = State with
                {
                    Phase = RefreshPhase.Failed,
                    Steps = steps,
                    FinishedAt = run.FinishedAt,
                };
                AppendLog(LogEntryLevel.Error, $"refresh failed: {run.FailureReason}");
            }
            LastRun = run;
        }

        NotifyNow();
    }

    public void Begin(IReadOnlyList<RefreshStepDefinition> steps) =>
        Update(s => s with { Steps = steps.Select(RefreshStep.Queued).ToList() });

    public void StartStep(int index, int total) =>
        Update(s =>
            s with
            {
                Steps = Replace(
                    s.Steps,
                    index,
                    step =>
                        step with
                        {
                            Status = StepStatus.Active,
                            Completed = 0,
                            Total = total,
                            StartedAt = DateTime.Now,
                        }
                ),
            }
        );

    public void Advance(int count = 1) =>
        UpdateActive(step =>
            step with
            {
                Completed = Math.Min(step.Total, step.Completed + count),
            }
        );

    public void CompleteStep() =>
        UpdateActive(step =>
            step with
            {
                Status = StepStatus.Done,
                Completed = step.Total,
                FinishedAt = DateTime.Now,
            }
        );

    public void ReportRetry() => Update(s => s with { Retries = s.Retries + 1 });

    public void ReportError() => Update(s => s with { Errors = s.Errors + 1 });

    public void Log(LogEntryLevel level, string message)
    {
        lock (_lock)
            AppendLog(level, message);
        MarkDirty();
    }

    public void Dispose() => _notifyTimer.Dispose();

    private void Update(Func<RefreshState, RefreshState> change)
    {
        lock (_lock)
            State = change(State);
        MarkDirty();
    }

    private void UpdateActive(Func<RefreshStep, RefreshStep> change) =>
        Update(s =>
        {
            for (var i = 0; i < s.Steps.Count; i++)
            {
                if (s.Steps[i].Status == StepStatus.Active)
                    return s with { Steps = Replace(s.Steps, i, change) };
            }
            return s;
        });

    private static RefreshStep[] Replace(
        IReadOnlyList<RefreshStep> steps,
        int index,
        Func<RefreshStep, RefreshStep> change
    )
    {
        var copy = steps.ToArray();
        copy[index] = change(copy[index]);
        return copy;
    }

    // assumes caller to always hold the lock
    private void AppendLog(LogEntryLevel level, string message)
    {
        _log.Enqueue(new LogEntry(DateTime.Now, level, message));
        while (_log.Count > MaxLogEntries)
            _log.Dequeue();
    }

    // atomic swap operation
    private void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    private void FlushPendingChange()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 1)
            Raise();
    }

    private void NotifyNow()
    {
        Interlocked.Exchange(ref _dirty, 0);
        Raise();
    }

    private void Raise()
    {
        // if no subscribers, return
        if (StateChanged is not { } handlers)
            return;

        // we execute statechanged subscriber callbacks like this instead of StateChanged?.Invoke(), since
        // invoke stops on exception, and subscribers after the failing one dont update.
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // a broken subscriber must not stop the others
                // TODO: inject logger and log here
                // note: this does not catch render exceptions
            }
        }
    }
}
