using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using DotnetInspector.LibraryMetadata;

namespace DotnetInspector.Queries;

/// <summary>Whole-inventory limits, independent of result-row selection.</summary>
public sealed record WorkspaceDeclarationLocatorOptions
{
    public int MaxInventoryReadsPerAttempt { get; init; } = 256;
    public int MaxRetainedInventories { get; init; } = 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxInventoryReadsPerAttempt);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRetainedInventories);
    }
}

/// <summary>
/// Workspace-owned, lazy declaration maintenance over explicitly loaded declaration contexts.
/// </summary>
public sealed class WorkspaceDeclarationLocator
{
    readonly InspectionWorkspace _workspace;
    readonly Func<ValueTask> _yieldAsync;
    readonly object _gate = new();
    readonly Dictionary<WorkspaceDeclarationOccurrence, Entry> _entries = [];
    readonly Queue<Work> _pending = [];
    readonly CancellationTokenSource _stop = new();
    readonly CancellationToken _stopToken;
    Task _worker = Task.CompletedTask;
    ExceptionDispatchInfo? _failure;
    bool _running;
    bool _active;
    bool _closed;
    int _retained;
    int _reserved;
    long _reads;

    internal WorkspaceDeclarationLocator(
        InspectionWorkspace workspace,
        WorkspaceDeclarationLocatorOptions options,
        Func<ValueTask> yieldAsync)
    {
        _workspace = workspace;
        Options = options;
        _yieldAsync = yieldAsync;
        _stopToken = _stop.Token;
    }

    public WorkspaceDeclarationLocatorOptions Options { get; }
    public bool IsActive { get { lock (_gate) return _active; } }
    public long InventoryReadCount { get { lock (_gate) return _reads; } }
    public int RetainedInventoryCount { get { lock (_gate) return _retained; } }

    /// <summary>
    /// Completion of currently scheduled maintenance, including any terminal worker failure.
    /// Reading this property neither activates nor retries maintenance.
    /// </summary>
    public Task Maintenance { get { lock (_gate) return _worker; } }

    /// <summary>
    /// Captures one population and evaluates the cold query over its shared inventory outcomes.
    /// A new request may retry unfinished work; successful Metadata outcomes are not rescanned.
    /// </summary>
    public async Task<TypeDeclarationLocatorResult> ExecuteAsync(
        ImmutableArray<TypeDeclarationLocatorRequest> requests,
        bool includeAll = false,
        CancellationToken cancellationToken = default)
    {
        if (TypeDeclarationLocatorQuery.ValidateRequests(requests, null, cancellationToken) is { } invalid)
            return invalid;
        WorkspaceDeclarationPopulationCapture capture =
            _workspace.ObserveDeclarationPopulation(this);
        if (capture is WorkspaceDeclarationPopulationCapture.Rejected unavailable)
        {
            return new TypeDeclarationLocatorResult.Rejected(
                TypeDeclarationLocatorRejectionKind.PopulationUnavailable,
                populationFailure: unavailable.Failure);
        }
        var population = ((WorkspaceDeclarationPopulationCapture.Captured)capture).Population;
        Dictionary<WorkspaceDeclarationOccurrence, Task<WorkspaceDeclarationInventoryOutcome>> attempt;
        lock (_gate)
        {
            _failure?.Throw();
            if (_closed)
                throw new OperationCanceledException(_stopToken);
            _stopToken.ThrowIfCancellationRequested();
            _active = true;
            attempt = Prepare(population, retry: true);
        }
        await Task.WhenAll(attempt.Values)
            .WaitAsync(_stopToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _stopToken.ThrowIfCancellationRequested();
        return TypeDeclarationLocatorQuery.Execute(
            population, requests, includeAll, maxInventoryReads: null,
            (occurrence, _) => attempt[occurrence].GetAwaiter().GetResult(),
            cancellationToken);
    }

    internal void PopulationChanged()
    {
        WorkspaceDeclarationPopulationCapture capture =
            _workspace.CaptureObservedDeclarationPopulation();
        if (capture is not WorkspaceDeclarationPopulationCapture.Captured captured)
            return;
        lock (_gate)
        {
            if (_closed || _failure is not null)
                return;
            // Observation is installed before the first request schedules its captured receipt.
            _active = true;
            Prepare(captured.Population, retry: false);
        }
    }

    Dictionary<WorkspaceDeclarationOccurrence, Task<WorkspaceDeclarationInventoryOutcome>> Prepare(
        WorkspaceDeclarationPopulation population, bool retry)
    {
        var attempt = new Dictionary<
            WorkspaceDeclarationOccurrence, Task<WorkspaceDeclarationInventoryOutcome>>();
        int reads = 0;
        foreach (WorkspaceDeclarationMember member in population.Receipt.Members)
        {
            if (member.Coordinate is null)
                continue;
            WorkspaceDeclarationOccurrence occurrence = member.Occurrence;
            if (_entries.TryGetValue(occurrence, out Entry? existing)
                && (!retry || existing.Reusable || !existing.Completion.Task.IsCompleted))
            {
                attempt.Add(occurrence, existing.Completion.Task);
                continue;
            }
            var entry = new Entry();
            _entries[occurrence] = entry;
            attempt.Add(occurrence, entry.Completion.Task);
            if (_retained + _reserved >= Options.MaxRetainedInventories)
            {
                entry.Completion.SetResult(new WorkspaceDeclarationInventoryOutcome.NotEvaluated(
                    WorkspaceDeclarationInventoryBound.RetainedInventories));
            }
            else if (reads >= Options.MaxInventoryReadsPerAttempt)
            {
                entry.Completion.SetResult(new WorkspaceDeclarationInventoryOutcome.NotEvaluated(
                    WorkspaceDeclarationInventoryBound.ReadAttempts));
            }
            else
            {
                reads++;
                _reserved++;
                _pending.Enqueue(new(population, occurrence, entry));
            }
        }
        if (!_running && _pending.Count != 0)
        {
            _running = true;
            _worker = MaintainAsync();
        }
        return attempt;
    }

    async Task MaintainAsync()
    {
        Work? current = null;
        try
        {
            await Task.Yield();
            while (true)
            {
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    current = _pending.Dequeue();
                }
                await _yieldAsync().ConfigureAwait(false);
                _stopToken.ThrowIfCancellationRequested();
                lock (_gate)
                    _reads++;
                WorkspaceDeclarationInventoryOutcome outcome =
                    current.Population.ReadDeclarations(current.Occurrence, _stopToken);
                lock (_gate)
                {
                    _reserved--;
                    current.Entry.Reusable =
                        outcome
                            is WorkspaceDeclarationInventoryOutcome.Inspected
                            or WorkspaceDeclarationInventoryOutcome
                                .LibraryInspected
                                {
                                    Outcome:
                                        LibraryTypeDeclarationInventoryInspectionOutcome
                                            .Completed,
                                };
                    if (current.Entry.Reusable)
                        _retained++;
                    current.Entry.Completion.SetResult(outcome);
                    current = null;
                }
            }
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
        {
            lock (_gate)
                SettleStopped(current, failure: null);
        }
        catch (Exception failure)
        {
            lock (_gate)
            {
                _failure = ExceptionDispatchInfo.Capture(failure);
                SettleStopped(current, failure);
            }
            throw;
        }
    }

    void SettleStopped(Work? current, Exception? failure)
    {
        if (current is not null)
            Settle(current);
        while (_pending.TryDequeue(out Work? pending))
            Settle(pending);
        _running = false;

        void Settle(Work work)
        {
            _reserved--;
            if (failure is null)
                work.Entry.Completion.TrySetCanceled(_stopToken);
            else
                work.Entry.Completion.TrySetException(failure);
        }
    }

    internal async Task<Exception?> CloseAsync()
    {
        Task worker;
        lock (_gate)
        {
            _closed = true;
            worker = _worker;
        }
        await _stop.CancelAsync().ConfigureAwait(false);
        Exception? failure = null;
        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        lock (_gate)
        {
            _entries.Clear();
            _retained = 0;
            _active = false;
        }
        _stop.Dispose();
        return failure;
    }

    internal static async ValueTask YieldAsync() => await Task.Yield();

    sealed class Entry
    {
        internal TaskCompletionSource<WorkspaceDeclarationInventoryOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Reusable { get; set; }
    }

    sealed record Work(
        WorkspaceDeclarationPopulation Population,
        WorkspaceDeclarationOccurrence Occurrence,
        Entry Entry);
}
