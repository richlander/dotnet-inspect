using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// The ordered Package candidate tier that produced one assembly-reference
/// evaluation.
/// </summary>
public enum PackageAssemblyReferenceSupplierTier
{
    ExactPackageId,
    PackageFamilyPrefix,
    SelectedFileName,
}

/// <summary>
/// One decoded namesake member evaluated for an exact reachable PackageRef.
/// </summary>
public sealed record PackageAssemblyReferenceSupplierCandidateEvidence(
    PackageDependencyEdgeRealizationSubject Route,
    PackageHouseFileList FileList,
    PackageCompileAsset? CompileAsset,
    PackageCompileAsset PayloadAsset,
    PackageContentEntry Entry,
    AssemblyReferenceIdentity Identity,
    PackageAssemblyReferenceSupplierTier Tier,
    bool OwnsSimpleName,
    bool MatchesRequest);

/// <summary>
/// One Package-owned assembly selected from an ordered reachable tier.
/// </summary>
public sealed record PackageAssemblyReferenceSupplierSelection(
    PackageAssemblyReferenceSupplierCandidateEvidence Evidence,
    ResolvedAssemblyReference Assembly);

/// <summary>Finite-work limits for Package assembly supplier association.</summary>
public sealed record PackageAssemblyReferenceSupplierLimits
{
    public const long DefaultMaxAssemblyBytes = 64L * 1024 * 1024;

    public long MaxAssemblyBytes { get; init; } =
        DefaultMaxAssemblyBytes;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            MaxAssemblyBytes,
            int.MaxValue);
    }
}

/// <summary>
/// Closed ordinary-Package stage outcome for one external assembly reference.
/// Platform continuation is intentionally owned by an adjacent composition.
/// </summary>
public abstract record PackageAssemblyReferenceSupplierOutcome
{
    private protected PackageAssemblyReferenceSupplierOutcome(
        AssemblyBindingRequest request,
        ImmutableArray<PackageAssemblyReferenceSupplierCandidateEvidence>
            evaluatedCandidates)
    {
        Request = request;
        EvaluatedCandidates = evaluatedCandidates;
    }

    public AssemblyBindingRequest Request { get; }

    public ImmutableArray<PackageAssemblyReferenceSupplierCandidateEvidence>
        EvaluatedCandidates
    { get; }

    public sealed record Selected :
        PackageAssemblyReferenceSupplierOutcome
    {
        internal Selected(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierSelection selection,
            ImmutableArray<
                PackageAssemblyReferenceSupplierCandidateEvidence>
                evaluatedCandidates,
            bool observedNameOwnedMiss)
            : base(request, evaluatedCandidates)
        {
            Selection = selection;
            ObservedNameOwnedMiss = observedNameOwnedMiss;
        }

        public PackageAssemblyReferenceSupplierSelection Selection { get; }

        public bool ObservedNameOwnedMiss { get; }
    }

    public sealed record Missing :
        PackageAssemblyReferenceSupplierOutcome
    {
        internal Missing(
            AssemblyBindingRequest request,
            AssemblyBindingMissDisposition disposition,
            ImmutableArray<
                PackageAssemblyReferenceSupplierCandidateEvidence>
                evaluatedCandidates)
            : base(request, evaluatedCandidates) =>
            Disposition = disposition;

        public AssemblyBindingMissDisposition Disposition { get; }
    }

    public sealed record Ambiguous :
        PackageAssemblyReferenceSupplierOutcome
    {
        internal Ambiguous(
            AssemblyBindingRequest request,
            PackageAssemblyReferenceSupplierTier tier,
            ImmutableArray<PackageAssemblyReferenceSupplierSelection>
                candidates,
            ImmutableArray<
                PackageAssemblyReferenceSupplierCandidateEvidence>
                evaluatedCandidates)
            : base(request, evaluatedCandidates)
        {
            Tier = tier;
            Candidates = candidates;
        }

        public PackageAssemblyReferenceSupplierTier Tier { get; }

        public ImmutableArray<PackageAssemblyReferenceSupplierSelection>
            Candidates
        { get; }
    }

    public sealed record Unavailable :
        PackageAssemblyReferenceSupplierOutcome
    {
        internal Unavailable(
            AssemblyBindingRequest request,
            PackageDependencyEdgeRealizationSubject route,
            PackageHouseResult result,
            ImmutableArray<
                PackageAssemblyReferenceSupplierCandidateEvidence>
                evaluatedCandidates)
            : base(request, evaluatedCandidates)
        {
            Route = route;
            Result = result;
        }

        public PackageDependencyEdgeRealizationSubject Route { get; }

        public PackageHouseResult Result { get; }
    }

    public sealed record Incomplete :
        PackageAssemblyReferenceSupplierOutcome
    {
        internal Incomplete(
            AssemblyBindingRequest request,
            string reason,
            PackageDependencyEdgeRealizationSubject? route,
            PackageHouseResult? result,
            ImmutableArray<
                PackageAssemblyReferenceSupplierCandidateEvidence>
                evaluatedCandidates)
            : base(request, evaluatedCandidates)
        {
            Reason = reason;
            Route = route;
            Result = result;
        }

        public string Reason { get; }

        public PackageDependencyEdgeRealizationSubject? Route { get; }

        public PackageHouseResult? Result { get; }
    }

    public sealed record Failed :
        PackageAssemblyReferenceSupplierOutcome
    {
        internal Failed(
            AssemblyBindingRequest request,
            string reason,
            PackageDependencyEdgeRealizationSubject? route,
            PackageHouseResult? result,
            ImmutableArray<
                PackageAssemblyReferenceSupplierCandidateEvidence>
                evaluatedCandidates)
            : base(request, evaluatedCandidates)
        {
            Reason = reason;
            Route = route;
            Result = result;
        }

        public string Reason { get; }

        public PackageDependencyEdgeRealizationSubject? Route { get; }

        public PackageHouseResult? Result { get; }
    }
}

/// <summary>
/// Immutable reachable-Package input for one reusable assembly-reference
/// supplier association.
/// </summary>
public sealed record PackageAssemblyReferenceSupplierAssociationRequest
{
    public PackageAssemblyReferenceSupplierAssociationRequest(
        PackageDependencyTraversalOutcome traversal,
        int rootOccurrenceIndex,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            edgeExecutions,
        PackageAssemblyReferenceSupplierLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentOutOfRangeException.ThrowIfNegative(rootOccurrenceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            rootOccurrenceIndex,
            traversal.Roots.Length);
        if (edgeExecutions.IsDefault
            || edgeExecutions.Any(static execution => execution is null))
        {
            throw new ArgumentException(
                "Edge executions must be a non-default collection.",
                nameof(edgeExecutions));
        }

        Traversal = traversal;
        RootOccurrenceIndex = rootOccurrenceIndex;
        EdgeExecutions = edgeExecutions;
        Limits = limits ?? new();
        Limits.Validate();
    }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public int RootOccurrenceIndex { get; }

    public ImmutableArray<PackageDependencyEdgeRealizationExecution>
        EdgeExecutions
    { get; }

    public PackageAssemblyReferenceSupplierLimits Limits { get; }
}

/// <summary>
/// Reusable Package-first association over one immutable reachable closure.
/// Resolve requests sequentially. The session retains generation-bound TFM
/// File Lists and decoded namesake descriptors for later requests.
/// </summary>
public sealed class PackageAssemblyReferenceSupplierAssociation
{
    readonly ImmutableArray<CandidateState> _candidates;
    readonly PackageHouseOperation _acquireOperation;
    readonly PackageAssemblyReferenceSupplierLimits _limits;
    readonly string? _incompleteReason;
    Dictionary<string, ImmutableArray<MemberCandidate>>?
        _completeNamesakeIndex;

    private PackageAssemblyReferenceSupplierAssociation(
        ImmutableArray<CandidateState> candidates,
        PackageHouseOperation acquireOperation,
        PackageAssemblyReferenceSupplierLimits limits,
        string? incompleteReason)
    {
        _candidates = candidates;
        _acquireOperation = acquireOperation;
        _limits = limits;
        _incompleteReason = incompleteReason;
    }

    public static PackageAssemblyReferenceSupplierAssociation Create(
        PackageAssemblyReferenceSupplierAssociationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackageDependencyTraversalOutcome traversal = request.Traversal;
        if (traversal.RootReachability.Length != traversal.Roots.Length
            || traversal.Roots[request.RootOccurrenceIndex].OccurrenceIndex
                != request.RootOccurrenceIndex)
        {
            throw new ArgumentException(
                "Traversal reachability must align with its root occurrences.",
                nameof(request));
        }

        var provided =
            new Dictionary<int, PackageDependencyEdgeRealizationExecution>();
        PackageHouseOperation? operation = null;
        PackageHouseTargetContext? target = null;
        foreach (PackageDependencyEdgeRealizationExecution execution
            in request.EdgeExecutions)
        {
            PackageDependencyEdgeRealizationSubject subject =
                execution.Subject;
            if (!ReferenceEquals(subject.Traversal, traversal)
                || subject.RootOccurrenceIndex
                    != request.RootOccurrenceIndex
                || !provided.TryAdd(subject.EdgeIndex, execution))
            {
                throw new ArgumentException(
                    "Each edge execution must belong to one unique admitted edge of the requested traversal root.",
                    nameof(request));
            }

            PackageHouseTargetContext executionTarget =
                execution.Request.TargetContext
                ?? throw new ArgumentException(
                    "Assembly supplier candidates require one exact PackageHouse target.",
                    nameof(request));
            operation ??= execution.Request.Operation;
            target ??= executionTarget;
            if (execution.Request.Operation.RequestTimeout
                    != operation.RequestTimeout
                || execution.Request.Operation.OperationTimeout
                    != operation.OperationTimeout
                || !TargetMatches(
                    executionTarget,
                    target))
            {
                throw new ArgumentException(
                    "Every edge execution must use one exact target and shared PackageHouse deadlines.",
                    nameof(request));
            }
        }

        if (operation is null || target is null)
        {
            operation = PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire);
        }

        var candidates = ImmutableArray.CreateBuilder<CandidateState>();
        var retained =
            new Dictionary<
                PackageAcquisitionCandidateCorrespondence,
                CandidateState>();
        string? incompleteReason = traversal.Roots[
            request.RootOccurrenceIndex].Completion
            == PackageDependencyTraversalRootCompletion.Complete
                ? null
                : "The reachable PackageRef snapshot is not complete.";
        PackageDependencyTraversalReachability reachability =
            traversal.RootReachability[request.RootOccurrenceIndex];
        for (int edgeIndex = 0;
            edgeIndex < traversal.Edges.Length;
            edgeIndex++)
        {
            if (!reachability.IsEdgeAdmitted(edgeIndex, out _))
                continue;

            PackageDependencyTraversalEdge edge =
                traversal.Edges[edgeIndex];
            if (edge.Authority
                != PackageDependencyTraversalEdgeEmissionAuthority
                    .ResolvedCandidate)
            {
                incompleteReason ??=
                    "The reachable PackageRef snapshot contains an admitted edge without exact candidate evidence.";
                continue;
            }

            if (!provided.Remove(
                    edgeIndex,
                    out PackageDependencyEdgeRealizationExecution?
                        execution))
            {
                throw new ArgumentException(
                    "Every admitted resolved-candidate edge requires one exact prepared execution.",
                    nameof(request));
            }

            if (execution.DelegatesToPlatform)
                continue;

            PackageAcquisitionCandidate candidate =
                execution.Subject.Candidate;
            if (!retained.TryGetValue(
                    candidate.Correspondence,
                    out CandidateState? state))
            {
                state = new CandidateState(
                    execution,
                    target!,
                    PackageHouseRequestAssociation.Create());
                retained.Add(candidate.Correspondence, state);
                candidates.Add(state);
            }
        }
        if (provided.Count != 0)
        {
            throw new ArgumentException(
                "Edge executions may name only admitted resolved-candidate edges for the requested root.",
                nameof(request));
        }

        return new(
            candidates.ToImmutable(),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire,
                operation.RequestTimeout,
                operation.OperationTimeout),
            request.Limits,
            incompleteReason);
    }

    public async ValueTask<PackageAssemblyReferenceSupplierOutcome>
        ResolveAsync(
            AssemblyBindingRequest request,
            AssemblyBindingSelection referencingContextSelection,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(referencingContextSelection);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        sourceOperation.CancellationToken.ThrowIfCancellationRequested();
        if (request.Target
            is not AssemblyBindingTarget.AssemblyReference target)
        {
            throw new ArgumentException(
                "Package supplier association requires one exact external AssemblyRef target.",
                nameof(request));
        }
        if (referencingContextSelection
                is not AssemblyBindingSelection.Missing
                {
                    Disposition:
                        AssemblyBindingMissDisposition.NoNameOwner,
                })
        {
            throw new ArgumentException(
                "Package supplier association begins only after the referencing context completes with NoNameOwner.",
                nameof(referencingContextSelection));
        }
        if (_candidates.Length != 0
            && (sourceOperation.RequestTimeout
                    != _acquireOperation.RequestTimeout
                || sourceOperation.OperationTimeout
                    != _acquireOperation.OperationTimeout))
        {
            throw new ArgumentException(
                "The Package Source operation must match the association's PackageHouse deadlines.",
                nameof(sourceOperation));
        }

        if (_candidates.Length != 0)
        {
            sourceOperation.ValidateCandidateOwnership(
                [
                    .. _candidates.Select(static state => state.Candidate),
                ]);
        }

        var evaluated =
            ImmutableArray.CreateBuilder<
                PackageAssemblyReferenceSupplierCandidateEvidence>();
        if (_incompleteReason is not null)
        {
            return new PackageAssemblyReferenceSupplierOutcome.Incomplete(
                request,
                _incompleteReason,
                route: null,
                result: null,
                evaluated.ToImmutable());
        }

        string name = target.Identity.Name;
        bool observedNameOwnedMiss = false;
        CandidateState[] exact =
        [
            .. _candidates.Where(state =>
                state.Candidate.Coordinate.PackageId.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        TierResult exactResult =
            await EvaluateTierAsync(
                    exact,
                    name,
                    target.Identity,
                    request,
                    PackageAssemblyReferenceSupplierTier.ExactPackageId,
                    house,
                    sourceOperation,
                    evaluated)
                .ConfigureAwait(false);
        if (exactResult.Terminal is not null)
            return exactResult.Terminal;
        observedNameOwnedMiss |= exactResult.ObservedNameOwnedMiss;
        if (exactResult.Selections.Length > 1)
        {
            return new PackageAssemblyReferenceSupplierOutcome.Ambiguous(
                request,
                PackageAssemblyReferenceSupplierTier.ExactPackageId,
                exactResult.Selections,
                evaluated.ToImmutable());
        }
        if (exactResult.Selections.Length == 1)
        {
            return new PackageAssemblyReferenceSupplierOutcome.Selected(
                request,
                exactResult.Selections[0],
                evaluated.ToImmutable(),
                observedNameOwnedMiss);
        }

        PackageAssemblyReferenceSupplierOutcome? inventoryTerminal =
            await EnsureCompleteNamesakeIndexAsync(
                    request,
                    house,
                    sourceOperation,
                    evaluated)
                .ConfigureAwait(false);
        if (inventoryTerminal is not null)
            return inventoryTerminal;

        ImmutableArray<MemberCandidate> namesake =
            _completeNamesakeIndex!.TryGetValue(
                name,
                out ImmutableArray<MemberCandidate> indexed)
                ? indexed
                : [];
        ImmutableHashSet<CandidateState> exactSet =
            exact.ToImmutableHashSet();
        CandidateState[] prefix =
        [
            .. namesake
                .Select(static member => member.State)
                .Distinct()
                .Where(state =>
                    !exactSet.Contains(state)
                    && HasPackageFamilyAffinity(
                        state.Candidate.Coordinate.PackageId,
                        name)),
        ];
        TierResult prefixResult =
            await EvaluateTierAsync(
                    prefix,
                    name,
                    target.Identity,
                    request,
                    PackageAssemblyReferenceSupplierTier.PackageFamilyPrefix,
                    house,
                    sourceOperation,
                    evaluated)
                .ConfigureAwait(false);
        if (prefixResult.Terminal is not null)
            return prefixResult.Terminal;
        observedNameOwnedMiss |= prefixResult.ObservedNameOwnedMiss;
        if (prefixResult.Selections.Length > 1)
        {
            return new PackageAssemblyReferenceSupplierOutcome.Ambiguous(
                request,
                PackageAssemblyReferenceSupplierTier.PackageFamilyPrefix,
                prefixResult.Selections,
                evaluated.ToImmutable());
        }
        if (prefixResult.Selections.Length == 1)
        {
            return new PackageAssemblyReferenceSupplierOutcome.Selected(
                request,
                prefixResult.Selections[0],
                evaluated.ToImmutable(),
                observedNameOwnedMiss);
        }

        ImmutableHashSet<CandidateState> prefixSet =
            prefix.ToImmutableHashSet();
        CandidateState[] remaining =
        [
            .. namesake
                .Select(static member => member.State)
                .Distinct()
                .Where(state =>
                    !exactSet.Contains(state)
                    && !prefixSet.Contains(state)),
        ];
        TierResult remainingResult =
            await EvaluateTierAsync(
                    remaining,
                    name,
                    target.Identity,
                    request,
                    PackageAssemblyReferenceSupplierTier.SelectedFileName,
                    house,
                    sourceOperation,
                    evaluated)
                .ConfigureAwait(false);
        if (remainingResult.Terminal is not null)
            return remainingResult.Terminal;
        observedNameOwnedMiss |= remainingResult.ObservedNameOwnedMiss;
        if (remainingResult.Selections.Length > 1)
        {
            return new PackageAssemblyReferenceSupplierOutcome.Ambiguous(
                request,
                PackageAssemblyReferenceSupplierTier.SelectedFileName,
                remainingResult.Selections,
                evaluated.ToImmutable());
        }
        if (remainingResult.Selections.Length == 1)
        {
            return new PackageAssemblyReferenceSupplierOutcome.Selected(
                request,
                remainingResult.Selections[0],
                evaluated.ToImmutable(),
                observedNameOwnedMiss);
        }

        return new PackageAssemblyReferenceSupplierOutcome.Missing(
            request,
            observedNameOwnedMiss
                ? AssemblyBindingMissDisposition.NameOwnedNoMatch
                : AssemblyBindingMissDisposition.NoNameOwner,
            evaluated.ToImmutable());
    }

    async ValueTask<TierResult> EvaluateTierAsync(
        IEnumerable<CandidateState> states,
        string name,
        AssemblyReferenceIdentity requestedIdentity,
        AssemblyBindingRequest request,
        PackageAssemblyReferenceSupplierTier tier,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        ImmutableArray<
            PackageAssemblyReferenceSupplierCandidateEvidence>.Builder
            evaluated)
    {
        var selections =
            ImmutableArray.CreateBuilder<
                PackageAssemblyReferenceSupplierSelection>();
        bool observedNameOwnedMiss = false;
        foreach (CandidateState state in states)
        {
            sourceOperation.CancellationToken.ThrowIfCancellationRequested();
            CandidateInventoryResult inventory =
                await EnsureInventoryAsync(
                        state,
                        request,
                        house,
                        sourceOperation,
                        evaluated)
                    .ConfigureAwait(false);
            if (inventory.Terminal is not null)
            {
                return new(
                    selections.ToImmutable(),
                    observedNameOwnedMiss,
                    inventory.Terminal);
            }

            foreach (MemberCandidate member
                in state.MembersFor(name))
            {
                sourceOperation.CancellationToken
                    .ThrowIfCancellationRequested();
                MemberEvaluationResult memberResult =
                    await EvaluateMemberAsync(
                            member,
                            requestedIdentity,
                            request,
                            tier,
                            house,
                            sourceOperation,
                            evaluated)
                        .ConfigureAwait(false);
                if (memberResult.Terminal is not null)
                {
                    return new(
                        selections.ToImmutable(),
                        observedNameOwnedMiss,
                        memberResult.Terminal);
                }
                observedNameOwnedMiss |=
                    memberResult.ObservedNameOwnedMiss;
                if (memberResult.Selection is not null)
                    selections.Add(memberResult.Selection);
            }
        }

        return new(
            selections.ToImmutable(),
            observedNameOwnedMiss,
            Terminal: null);
    }

    async ValueTask<PackageAssemblyReferenceSupplierOutcome?>
        EnsureCompleteNamesakeIndexAsync(
            AssemblyBindingRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation,
            ImmutableArray<
                PackageAssemblyReferenceSupplierCandidateEvidence>.Builder
                evaluated)
    {
        if (_completeNamesakeIndex is not null)
            return null;

        foreach (CandidateState state in _candidates)
        {
            sourceOperation.CancellationToken.ThrowIfCancellationRequested();
            CandidateInventoryResult inventory =
                await EnsureInventoryAsync(
                        state,
                        request,
                        house,
                        sourceOperation,
                        evaluated)
                    .ConfigureAwait(false);
            if (inventory.Terminal is not null)
                return inventory.Terminal;
        }

        var index =
            new Dictionary<
                string,
                ImmutableArray<MemberCandidate>.Builder>(
                StringComparer.OrdinalIgnoreCase);
        foreach (CandidateState state in _candidates)
        {
            foreach ((string name, ImmutableArray<MemberCandidate> members)
                in state.MembersByName)
            {
                if (!index.TryGetValue(name, out var values))
                {
                    values =
                        ImmutableArray.CreateBuilder<MemberCandidate>();
                    index.Add(name, values);
                }
                values.AddRange(members);
            }
        }

        _completeNamesakeIndex = index.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToImmutable(),
            StringComparer.OrdinalIgnoreCase);
        return null;
    }

    async ValueTask<CandidateInventoryResult> EnsureInventoryAsync(
        CandidateState state,
        AssemblyBindingRequest request,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        ImmutableArray<
            PackageAssemblyReferenceSupplierCandidateEvidence>.Builder
            evaluated)
    {
        if (state.InventoryInitialized)
        {
            return state.InventoryFailure is null
                ? new(Terminal: null)
                : new(CreateTerminal(
                    request,
                    state,
                    state.InventoryFailure,
                    evaluated.ToImmutable()));
        }

        var query = PackageHouseContentQuery.TfmFileList(state.Target);
        var packageRequest = new PackageHouseRequest(
            new PackageHouseDemand.Candidate(state.Candidate),
            _acquireOperation,
            state.Target,
            association: state.Association,
            contentQuery: query);
        PackageHouseSettlement settlement =
            await house.ExecuteStepAsync(
                    packageRequest,
                    sourceOperation)
                .ConfigureAwait(false);
        if (settlement is not PackageHouseSettlement.Acquired acquired
            || acquired.Result.Evidence.FileList is not { } fileList
            || acquired.Result.Evidence.ContentNarrowing?.TargetSelection
                is not { } selectionReceipt)
        {
            state.SetInventoryFailure(settlement.Result);
            return new(CreateTerminal(
                request,
                state,
                settlement.Result,
                evaluated.ToImmutable()));
        }

        PackageCompileAssetSelection selection =
            selectionReceipt.Selection;
        if (selection.Status
            == PackageCompileAssetSelectionStatus
                .InvalidImplementationAssets)
        {
            state.SetInventoryFailure(acquired.Result);
            return new(
                new PackageAssemblyReferenceSupplierOutcome.Failed(
                    request,
                    "PackageHouse rejected the selected implementation-asset correspondence.",
                    state.Execution.Subject,
                    acquired.Result,
                    evaluated.ToImmutable()));
        }

        var entriesByPath =
            fileList.Entries
                .GroupBy(
                    static entry => entry.Path,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);
        var members =
            new Dictionary<
                string,
                ImmutableArray<MemberCandidate>.Builder>(
                StringComparer.OrdinalIgnoreCase);
        var selectedPayloads =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool invalidSelectedMember = false;
        foreach (PackageCompileAsset compileAsset
            in selection.Assets)
        {
            PackageCompileAsset payloadAsset =
                selection.FindImplementationAsset(compileAsset)
                ?? compileAsset;
            AddMember(compileAsset, payloadAsset);
        }
        foreach (PackageCompileAsset implementationAsset
            in selection.ImplementationAssets)
        {
            if (!selection.Assets.Any(asset =>
                    ReferenceEquals(
                        selection.FindImplementationAsset(asset),
                        implementationAsset)))
            {
                AddMember(
                    compileAsset: null,
                    implementationAsset);
            }
        }
        if (invalidSelectedMember)
        {
            state.SetInventoryFailure(acquired.Result);
            return new(
                new PackageAssemblyReferenceSupplierOutcome.Failed(
                    request,
                    "A selected Package assembly does not correspond to one exact TFM File List entry.",
                    state.Execution.Subject,
                    acquired.Result,
                    evaluated.ToImmutable()));
        }

        state.SetInventory(
            fileList,
            members.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToImmutable(),
                StringComparer.OrdinalIgnoreCase));
        return new(Terminal: null);

        void AddMember(
            PackageCompileAsset? compileAsset,
            PackageCompileAsset payloadAsset)
        {
            if (!selectedPayloads.Add(payloadAsset.Path))
                return;
            if (!entriesByPath.TryGetValue(
                    payloadAsset.Path,
                    out PackageContentEntry[]? entries)
                || entries.Length != 1)
            {
                invalidSelectedMember = true;
                return;
            }

            string assemblyName = AssemblySimpleName(payloadAsset);
            if (!members.TryGetValue(
                    assemblyName,
                    out var values))
            {
                values =
                    ImmutableArray.CreateBuilder<MemberCandidate>();
                members.Add(assemblyName, values);
            }
            values.Add(
                new MemberCandidate(
                    state,
                    compileAsset,
                    payloadAsset,
                    entries[0]));
        }
    }

    async ValueTask<MemberEvaluationResult> EvaluateMemberAsync(
        MemberCandidate member,
        AssemblyReferenceIdentity requestedIdentity,
        AssemblyBindingRequest request,
        PackageAssemblyReferenceSupplierTier tier,
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        ImmutableArray<
            PackageAssemblyReferenceSupplierCandidateEvidence>.Builder
            evaluated)
    {
        sourceOperation.CancellationToken.ThrowIfCancellationRequested();
        if (!member.State.DecodedMembers.TryGetValue(
                member.PayloadAsset.Path,
                out DecodedMember? decoded))
        {
            if (member.Entry.Length > _limits.MaxAssemblyBytes)
            {
                return new(
                    Selection: null,
                    ObservedNameOwnedMiss: false,
                    new PackageAssemblyReferenceSupplierOutcome.Incomplete(
                        request,
                        "A selected namesake Package member exceeds the assembly-byte limit.",
                        member.State.Execution.Subject,
                        result: null,
                        evaluated.ToImmutable()));
            }

            PackageHouseContentQuery query =
                member.State.FileList!.CreateFilesQuery([member.Entry]);
            var packageRequest = new PackageHouseRequest(
                new PackageHouseDemand.Candidate(member.State.Candidate),
                _acquireOperation,
                member.State.Target,
                association: member.State.Association,
                contentQuery: query);
            PackageHouseSettlement settlement =
                await house.ExecuteStepAsync(
                        packageRequest,
                        sourceOperation)
                    .ConfigureAwait(false);
            if (settlement is not PackageHouseSettlement.Acquired acquired
                || acquired.Result is not PackageHouseResult.Settled)
            {
                return new(
                    Selection: null,
                    ObservedNameOwnedMiss: false,
                    CreateTerminal(
                        request,
                        member.State,
                        settlement.Result,
                        evaluated.ToImmutable()));
            }

            try
            {
                byte[] image;
                await using (
                    PackageHousePayloadRead payload =
                        acquired.OpenPayloadRead(
                            member.Entry.Path,
                            member.Entry.Length))
                {
                    using var buffer = new MemoryStream(
                        checked((int)member.Entry.Length));
                    await payload.CopyToAsync(
                            buffer,
                            sourceOperation.CancellationToken)
                        .ConfigureAwait(false);
                    image = buffer.ToArray();
                }
                using AssemblyInspectionSession session =
                    AssemblyInspectionSession.OpenPrefetched(
                        new MemoryStream(
                            image,
                            writable: false));
                AssemblyReferenceIdentity identity =
                    session.AssemblyIdentity();
                var assembly = ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () => new MemoryStream(
                        image,
                        writable: false),
                    AssemblyResolutionProvenance.Package(
                        member.State.Candidate.Coordinate.PackageId,
                        member.State.Candidate.Coordinate.Version,
                        member.State.Target.RequestedFramework,
                        member.State.Target.RuntimeIdentifier,
                        member.Entry.Path));
                decoded = new(identity, assembly);
                member.State.DecodedMembers.Add(
                    member.PayloadAsset.Path,
                    decoded);
            }
            catch (BadImageFormatException)
            {
                return new(
                    Selection: null,
                    ObservedNameOwnedMiss: false,
                    new PackageAssemblyReferenceSupplierOutcome.Failed(
                        request,
                        "A selected namesake Package member is not a supported ECMA-335 assembly.",
                        member.State.Execution.Subject,
                        acquired.Result,
                        evaluated.ToImmutable()));
            }
            catch (IOException)
            {
                return new(
                    Selection: null,
                    ObservedNameOwnedMiss: false,
                    new PackageAssemblyReferenceSupplierOutcome.Unavailable(
                        request,
                        member.State.Execution.Subject,
                        acquired.Result,
                        evaluated.ToImmutable()));
            }
        }

        bool ownsSimpleName = decoded.Identity.Name.Equals(
            requestedIdentity.Name,
            StringComparison.OrdinalIgnoreCase);
        bool matches =
            requestedIdentity.MatchesCandidate(decoded.Identity);
        var evidence =
            new PackageAssemblyReferenceSupplierCandidateEvidence(
                member.State.Execution.Subject,
                member.State.FileList!,
                member.CompileAsset,
                member.PayloadAsset,
                member.Entry,
                decoded.Identity,
                tier,
                ownsSimpleName,
                matches);
        evaluated.Add(evidence);
        return new(
            matches
                ? new PackageAssemblyReferenceSupplierSelection(
                    evidence,
                    decoded.Assembly)
                : null,
            ObservedNameOwnedMiss: ownsSimpleName && !matches,
            Terminal: null);
    }

    static PackageAssemblyReferenceSupplierOutcome CreateTerminal(
        AssemblyBindingRequest request,
        CandidateState state,
        PackageHouseResult result,
        ImmutableArray<PackageAssemblyReferenceSupplierCandidateEvidence>
            evaluated) =>
        result switch
        {
            PackageHouseResult.Incomplete =>
                new PackageAssemblyReferenceSupplierOutcome.Incomplete(
                    request,
                    "PackageHouse could not complete required supplier evidence.",
                    state.Execution.Subject,
                    result,
                    evaluated),
            PackageHouseResult.Unavailable or PackageHouseResult.NoMatch =>
                new PackageAssemblyReferenceSupplierOutcome.Unavailable(
                    request,
                    state.Execution.Subject,
                    result,
                    evaluated),
            _ => new PackageAssemblyReferenceSupplierOutcome.Failed(
                request,
                "PackageHouse rejected or failed required supplier evidence.",
                state.Execution.Subject,
                result,
                evaluated),
        };

    static bool HasPackageFamilyAffinity(
        string packageId,
        string assemblyName) =>
        IsBoundaryPrefix(packageId, assemblyName)
        || IsBoundaryPrefix(assemblyName, packageId);

    static bool IsBoundaryPrefix(string prefix, string value) =>
        value.Length > prefix.Length
        && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && value[prefix.Length] == '.';

    static string AssemblySimpleName(PackageCompileAsset asset) =>
        asset.AssemblyName.EndsWith(
            ".dll",
            StringComparison.OrdinalIgnoreCase)
            ? asset.AssemblyName[..^4]
            : asset.AssemblyName;

    static bool TargetMatches(
        PackageHouseTargetContext left,
        PackageHouseTargetContext right) =>
        left.Mode == right.Mode
        && string.Equals(
            left.RequestedFramework,
            right.RequestedFramework,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            left.RuntimeIdentifier,
            right.RuntimeIdentifier,
            StringComparison.OrdinalIgnoreCase)
        && Equals(left.PlatformTarget, right.PlatformTarget);

    sealed class CandidateState
    {
        public CandidateState(
            PackageDependencyEdgeRealizationExecution execution,
            PackageHouseTargetContext target,
            PackageHouseRequestAssociation association)
        {
            Execution = execution;
            Target = target;
            Association = association;
        }

        public PackageDependencyEdgeRealizationExecution Execution
        {
            get;
        }

        public PackageAcquisitionCandidate Candidate =>
            Execution.Subject.Candidate;

        public PackageHouseTargetContext Target { get; }

        public PackageHouseRequestAssociation Association { get; }

        public bool InventoryInitialized { get; private set; }

        public PackageHouseResult? InventoryFailure { get; private set; }

        public PackageHouseFileList? FileList { get; private set; }

        public IReadOnlyDictionary<
            string,
            ImmutableArray<MemberCandidate>> MembersByName
        { get; private set; } =
            ImmutableDictionary<
                string,
                ImmutableArray<MemberCandidate>>.Empty;

        public Dictionary<string, DecodedMember> DecodedMembers { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public ImmutableArray<MemberCandidate> MembersFor(string name) =>
            MembersByName.TryGetValue(
                name,
                out ImmutableArray<MemberCandidate> members)
                ? members
                : [];

        public void SetInventory(
            PackageHouseFileList fileList,
            IReadOnlyDictionary<
                string,
                ImmutableArray<MemberCandidate>> members)
        {
            InventoryInitialized = true;
            FileList = fileList;
            MembersByName = members;
        }

        public void SetInventoryFailure(PackageHouseResult result)
        {
            InventoryInitialized = true;
            InventoryFailure = result;
        }
    }

    sealed record MemberCandidate(
        CandidateState State,
        PackageCompileAsset? CompileAsset,
        PackageCompileAsset PayloadAsset,
        PackageContentEntry Entry);

    sealed record DecodedMember(
        AssemblyReferenceIdentity Identity,
        ResolvedAssemblyReference Assembly);

    readonly record struct CandidateInventoryResult(
        PackageAssemblyReferenceSupplierOutcome? Terminal);

    readonly record struct MemberEvaluationResult(
        PackageAssemblyReferenceSupplierSelection? Selection,
        bool ObservedNameOwnedMiss,
        PackageAssemblyReferenceSupplierOutcome? Terminal);

    readonly record struct TierResult(
        ImmutableArray<PackageAssemblyReferenceSupplierSelection> Selections,
        bool ObservedNameOwnedMiss,
        PackageAssemblyReferenceSupplierOutcome? Terminal);
}
