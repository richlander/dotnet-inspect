using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Workspace-resident reverse declaration locator over explicitly loaded
/// contexts. Successful Metadata inventories are reused across queries.
/// </summary>
public sealed class WorkspaceTypeDeclarationLocator
{
    readonly InspectionWorkspace _workspace;
    readonly object _gate = new();
    readonly CancellationTokenSource _close = new();
    readonly Dictionary<WorkspaceDeclarationOccurrence, InventoryEntry> _entries = [];
    readonly TaskCompletionSource<Exception?> _closeCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _active;
    bool _sealed;
    bool _cancelIssued;
    bool _unbounded;
    int _maximumInventoryReads;
    int _activeQueries;
    int _activeWork;
    ExceptionDispatchInfo? _fault;
    Exception? _cancellationFailure;

    internal WorkspaceTypeDeclarationLocator(InspectionWorkspace workspace) =>
        _workspace = workspace;

    /// <summary>
    /// Locates every request against the exact Workspace population captured
    /// at admission. Caller cancellation detaches only this request.
    /// </summary>
    public async Task<TypeDeclarationLocatorResult> LocateAsync(
        ImmutableArray<TypeDeclarationLocatorRequest> requests,
        bool includeAll = false,
        int? maxInventoryReads = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TypeDeclarationLocatorQuery.ValidateArguments(
                requests,
                maxInventoryReads) is { } rejected)
        {
            return rejected;
        }

        WorkspaceDeclarationPopulationCapture capture =
            _workspace.ActivateAndCaptureDeclarationPopulation(this);
        if (capture is WorkspaceDeclarationPopulationCapture.Rejected unavailable)
        {
            return new TypeDeclarationLocatorResult.Rejected(
                TypeDeclarationLocatorRejectionKind.PopulationUnavailable,
                populationFailure: unavailable.Failure);
        }
        var population = ((WorkspaceDeclarationPopulationCapture.Captured)capture)
            .Population;

        List<InventoryWork> starts = [];
        List<InventoryReservation> reservations;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (_sealed)
            {
                return new TypeDeclarationLocatorResult.Rejected(
                    TypeDeclarationLocatorRejectionKind.PopulationUnavailable,
                    populationFailure:
                        WorkspaceDeclarationPopulationFailure.WorkspaceClosing);
            }

            UpdateMaintenanceBound(maxInventoryReads);
            _activeQueries++;
            reservations = Reserve(
                population,
                maxInventoryReads,
                query: true,
                starts);
        }
        Start(starts);

        try
        {
            PopulationChanged();
            var outcomes = new Dictionary<
                WorkspaceDeclarationOccurrence,
                WorkspaceDeclarationInventoryOutcome>();
            foreach (InventoryReservation reservation in reservations)
            {
                InventoryWorkResult result = await reservation.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (result.Failure is { } failure)
                    failure.Throw();
                if (result.Closed)
                    throw new OperationCanceledException(_close.Token);
                outcomes.Add(reservation.Member.Occurrence, result.Outcome!);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _close.Token.ThrowIfCancellationRequested();
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _close.Token);
            try
            {
                return TypeDeclarationLocatorQuery.Execute(
                    population.WithInventoryOutcomes(outcomes),
                    requests,
                    includeAll,
                    maxInventoryReads,
                    operation.Token);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (OperationCanceledException)
                when (_close.IsCancellationRequested)
            {
                throw new OperationCanceledException(_close.Token);
            }
        }
        finally
        {
            lock (_gate)
            {
                foreach (InventoryReservation reservation in reservations)
                {
                if (reservation.Work is not { } work)
                    continue;
                work.ReleaseQueryWaiter();
                if (work.QueryWaiters == 0
                    && work.Task.IsCompleted
                    && _entries.TryGetValue(
                        reservation.Member.Occurrence,
                        out InventoryEntry? entry)
                    && ReferenceEquals(entry.Work, work))
                {
                    entry.Work = null;
                }
                }
                _activeQueries--;
                TryCompleteClose();
            }
        }
    }

    internal void ActivateFromWorkspace()
    {
        lock (_gate)
        {
            if (!_sealed)
                _active = true;
        }
    }

    internal void PopulationChanged()
    {
        WorkspaceDeclarationPopulationCapture capture =
            _workspace.CaptureCurrentDeclarationPopulation(this);
        if (capture is not WorkspaceDeclarationPopulationCapture.Captured current)
            return;

        List<InventoryWork> starts = [];
        lock (_gate)
        {
            if (!_active || _sealed || _fault is not null)
                return;
            _ = Reserve(
                current.Population,
                _unbounded ? null : _maximumInventoryReads,
                query: false,
                starts);
        }
        Start(starts);
    }

    internal WorkspaceTypeDeclarationLocatorClose SealFromWorkspace()
    {
        lock (_gate)
        {
            _sealed = true;
            return new WorkspaceTypeDeclarationLocatorClose(this);
        }
    }

    internal Task WaitForCurrentMaintenanceAsync()
    {
        lock (_gate)
        {
            Task[] work = [.. _entries.Values
                .Where(static entry => entry.Work is not null)
                .Select(static entry => entry.Work!.Task)];
            return work.Length == 0 ? Task.CompletedTask : Task.WhenAll(work);
        }
    }

    internal int GetInventoryReadCount(
        WorkspaceDeclarationOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        lock (_gate)
        {
            return _entries.TryGetValue(occurrence, out InventoryEntry? entry)
                ? entry.ReadCount
                : 0;
        }
    }

    void UpdateMaintenanceBound(int? maxInventoryReads)
    {
        if (maxInventoryReads is null)
            _unbounded = true;
        else if (!_unbounded)
            _maximumInventoryReads = Math.Max(
                _maximumInventoryReads,
                maxInventoryReads.Value);
    }

    List<InventoryReservation> Reserve(
        WorkspaceDeclarationPopulation population,
        int? maxInventoryReads,
        bool query,
        List<InventoryWork> starts)
    {
        IEnumerable<WorkspaceDeclarationMember> selected =
            population.Receipt.Members.Where(
                static member => member.Coordinate is not null);
        if (maxInventoryReads is { } limit)
            selected = selected.Take(limit);

        var reservations = new List<InventoryReservation>();
        foreach (WorkspaceDeclarationMember member in selected)
        {
            if (!_entries.TryGetValue(
                    member.Occurrence,
                    out InventoryEntry? entry))
            {
                entry = new InventoryEntry();
                _entries.Add(member.Occurrence, entry);
            }

            if (entry.Inventory is { } inventory)
            {
                if (query)
                {
                    reservations.Add(new(
                        member,
                        Task.FromResult(
                            InventoryWorkResult.Completed(
                                new WorkspaceDeclarationInventoryOutcome
                                    .Inspected(inventory))),
                        Work: null));
                }
                continue;
            }
            if (entry.Work is { } existing)
            {
                if (query)
                {
                    existing.AddQueryWaiter();
                    reservations.Add(new(member, existing.Task, existing));
                }
                continue;
            }
            if (entry.PendingMaintenanceOutcome is { } pending)
            {
                if (query)
                {
                    entry.PendingMaintenanceOutcome = null;
                    reservations.Add(new(
                        member,
                        Task.FromResult(
                            InventoryWorkResult.Completed(pending)),
                        Work: null));
                }
                continue;
            }

            var work = new InventoryWork(
                population,
                member,
                queryWaiters: query ? 1 : 0);
            entry.Work = work;
            _activeWork++;
            starts.Add(work);
            if (query)
                reservations.Add(new(member, work.Task, work));
        }
        return reservations;
    }

    void Start(List<InventoryWork> starts)
    {
        foreach (InventoryWork work in starts)
            _ = RunInventoryWorkAsync(work);
    }

    async Task RunInventoryWorkAsync(InventoryWork work)
    {
        await Task.Yield();
        InventoryWorkResult result;
        try
        {
            _close.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_entries.TryGetValue(
                        work.Member.Occurrence,
                        out InventoryEntry? entry))
                {
                    entry.ReadCount++;
                }
            }
            WorkspaceDeclarationInventoryOutcome outcome =
                work.Population.ReadDeclarations(
                    work.Member.Occurrence,
                    _close.Token);
            result = InventoryWorkResult.Completed(outcome);
        }
        catch (OperationCanceledException)
            when (_close.IsCancellationRequested)
        {
            result = InventoryWorkResult.CloseCancelled;
        }
        catch (Exception exception)
        {
            result = InventoryWorkResult.Faulted(
                ExceptionDispatchInfo.Capture(exception));
        }

        lock (_gate)
        {
            InventoryEntry entry = _entries[work.Member.Occurrence];
            if (result.Outcome is WorkspaceDeclarationInventoryOutcome.Inspected
                { Outcome: { } inventory })
            {
                entry.Inventory = inventory;
                entry.Work = null;
            }
            else if (result.Outcome is not null
                && work.QueryWaiters == 0)
            {
                entry.PendingMaintenanceOutcome = result.Outcome;
                entry.Work = null;
            }
            if (result.Failure is { } failure)
            {
                _fault ??= failure;
                if (work.QueryWaiters == 0)
                    entry.Work = null;
            }
            if (result.Closed && work.QueryWaiters == 0)
                entry.Work = null;
            _activeWork--;
            TryCompleteClose();
        }
        work.Completion.TrySetResult(result);
    }

    void ThrowIfFaulted() => _fault?.Throw();

    void CancelFromWorkspace()
    {
        Exception? failure = null;
        try
        {
            _close.Cancel();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (_gate)
        {
            _cancelIssued = true;
            _cancellationFailure = failure;
            TryCompleteClose();
        }
    }

    void TryCompleteClose()
    {
        if (!_sealed
            || !_cancelIssued
            || _activeQueries != 0
            || _activeWork != 0)
        {
            return;
        }

        _entries.Clear();
        _close.Dispose();
        Exception? failure = (_fault?.SourceException, _cancellationFailure)
            switch
            {
                (null, null) => null,
                ({ } fault, null) => fault,
                (null, { } cancellation) => cancellation,
                ({ } fault, { } cancellation) =>
                    new AggregateException(fault, cancellation),
            };
        _closeCompletion.TrySetResult(failure);
    }

    sealed class InventoryEntry
    {
        internal AssemblyTypeDeclarationInventoryOutcome? Inventory { get; set; }
        internal WorkspaceDeclarationInventoryOutcome?
            PendingMaintenanceOutcome { get; set; }
        internal InventoryWork? Work { get; set; }
        internal int ReadCount { get; set; }
    }

    sealed class InventoryWork(
        WorkspaceDeclarationPopulation population,
        WorkspaceDeclarationMember member,
        int queryWaiters)
    {
        internal WorkspaceDeclarationPopulation Population { get; } =
            population;
        internal WorkspaceDeclarationMember Member { get; } = member;
        internal TaskCompletionSource<InventoryWorkResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<InventoryWorkResult> Task => Completion.Task;
        internal int QueryWaiters { get; private set; } = queryWaiters;

        internal void AddQueryWaiter() => QueryWaiters++;

        internal void ReleaseQueryWaiter()
        {
            if (QueryWaiters <= 0)
            {
                throw new InvalidOperationException(
                    "A declaration inventory query waiter was released without admission.");
            }
            QueryWaiters--;
        }
    }

    sealed record InventoryReservation(
        WorkspaceDeclarationMember Member,
        Task<InventoryWorkResult> Task,
        InventoryWork? Work);

    sealed record InventoryWorkResult(
        WorkspaceDeclarationInventoryOutcome? Outcome,
        ExceptionDispatchInfo? Failure,
        bool Closed)
    {
        internal static InventoryWorkResult Completed(
            WorkspaceDeclarationInventoryOutcome outcome) =>
            new(outcome, Failure: null, Closed: false);

        internal static InventoryWorkResult Faulted(
            ExceptionDispatchInfo failure) =>
            new(Outcome: null, failure, Closed: false);

        internal static InventoryWorkResult CloseCancelled { get; } =
            new(Outcome: null, Failure: null, Closed: true);
    }

    internal sealed class WorkspaceTypeDeclarationLocatorClose(
        WorkspaceTypeDeclarationLocator owner)
    {
        internal Task<Exception?> Completion => owner._closeCompletion.Task;
        internal void Cancel() => owner.CancelFromWorkspace();
    }
}
