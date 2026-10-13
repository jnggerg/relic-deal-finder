using Microsoft.Extensions.Options;
using RelicDealFinder.Enums.Deals;
using RelicDealFinder.Enums.Refresh;
using RelicDealFinder.Models.Dashboard;
using RelicDealFinder.Models.Deals;
using RelicDealFinder.Services.Refresh;

namespace RelicDealFinder.Services.Deals;

public sealed class DealCoordinator : IDisposable
{
    /*
     * This singleton owns the deal scanner's state and the in-memory deal store.
     * DealPollWorker does the polling, and asks this class when it's allowed to scan.
     *
     * Scanning pauses while a refresh runs, so the refresh gets the shared rate limiter to itself
     * and no deals are judged against EVs that are about to change.
     * Changes are raised straight away instead of through a notify timer like RefreshCoordinator's,
     * since the worker changes state at most once per rate-limited request.
     */
    private readonly Lock _lock = new();
    private readonly RefreshCoordinator _refresh;
    private readonly DealOptions _options;

    private readonly Dictionary<string, IReadOnlyList<Deal>> _deals = []; // relicSlug -> deals
    private bool _refreshRunning;
    private DateTime? _dataRefreshedAt;
    private CancellationTokenSource _scanCts = new();

    // completed and swapped whenever the refresh state changes, to wake the waiting worker
    private TaskCompletionSource _refreshChanged = NewSignal();

    public DealCoordinator(RefreshCoordinator refresh, IOptions<DealOptions> options)
    {
        _refresh = refresh;
        _options = options.Value;
        _refreshRunning = refresh.State.IsRunning;
        refresh.StateChanged += OnRefreshChanged;
    }

    public DealScanState State { get; private set; } = DealScanState.Initial;

    public event Action? StateChanged;

    // flattened, best profit first
    public IReadOnlyList<Deal> GetDeals()
    {
        lock (_lock)
            return [.. _deals.Values.SelectMany(d => d).OrderByDescending(d => d.Profit)];
    }

    // the finish time of the latest successful refresh, loaded from the db on startup
    internal void Initialize(DateTime? dataRefreshedAt)
    {
        lock (_lock)
            _dataRefreshedAt ??= dataRefreshedAt;
    }

    /*
     * Waits until no refresh is running and the data is fresh, then returns a token
     * that is cancelled when the next refresh starts.
     */
    internal async Task<CancellationToken> WaitUntilScanAllowedAsync(CancellationToken ct)
    {
        while (true)
        {
            Task signal;
            lock (_lock)
            {
                if (!_refreshRunning && IsFresh())
                    return _scanCts.Token;

                if (!_refreshRunning)
                    _deals.Clear(); // the EVs they were judged against are too old
                State = DealScanState.Initial with
                {
                    Phase = _refreshRunning
                        ? DealScanPhase.Paused
                        : DealScanPhase.WaitingForRefresh,
                };
                signal = _refreshChanged.Task;
            }

            Raise();
            await signal.WaitAsync(ct);
        }
    }

    internal void StartCycle(int total) =>
        UpdateWhileAllowed(() =>
            State = new DealScanState(DealScanPhase.Scanning, 0, total, null)
        );

    // swaps in the deals from the latest poll of this relic.
    // null means the request failed: the old deals are kept, and the relic is retried next cycle
    internal void ReplaceDeals(string slug, IReadOnlyList<Deal>? deals) =>
        UpdateWhileAllowed(() =>
        {
            if (deals is { Count: 0 })
                _deals.Remove(slug);
            else if (deals is not null)
                _deals[slug] = deals;
            State = State with { Scanned = State.Scanned + 1 };
        });

    internal void EnterIdle(DateTime nextCycleAt) =>
        UpdateWhileAllowed(() =>
            State = State with { Phase = DealScanPhase.Idle, NextCycleAt = nextCycleAt }
        );

    public void Dispose()
    {
        _refresh.StateChanged -= OnRefreshChanged;
        _scanCts.Dispose();
    }

    // ignores updates from a cycle that a refresh has just paused
    private void UpdateWhileAllowed(Action change)
    {
        lock (_lock)
        {
            if (_refreshRunning)
                return;
            change();
        }

        Raise();
    }

    private void OnRefreshChanged()
    {
        var state = _refresh.State;
        lock (_lock)
        {
            if (state.IsRunning == _refreshRunning)
                return;
            _refreshRunning = state.IsRunning;

            if (_refreshRunning)
            {
                // cancel the in-flight poll; the next scan gets a fresh token
                _scanCts.Cancel();
                _scanCts.Dispose();
                _scanCts = new CancellationTokenSource();
            }
            else if (state.Phase == RefreshPhase.Completed)
            {
                _dataRefreshedAt = state.FinishedAt;
                _deals.Clear();
            }
            // a failed or cancelled refresh keeps the old data and deals, if they are still fresh

            _refreshChanged.TrySetResult();
            _refreshChanged = NewSignal();
        }
    }

    // assumes caller to hold the lock
    private bool IsFresh() => DateTime.Now - _dataRefreshedAt < _options.MaxDataAge;

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Raise()
    {
        if (StateChanged is not { } handlers)
            return;

        // a broken subscriber must not stop the others
        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch
            {
                // ignored, same as RefreshCoordinator
            }
        }
    }
}
