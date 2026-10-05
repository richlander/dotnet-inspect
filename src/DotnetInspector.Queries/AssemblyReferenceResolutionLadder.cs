using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum AssemblyReferenceResolutionRung
{
    ReferencingContext,
    ExternalSupplier,
}

public enum AssemblyReferenceResolutionWorkKind
{
    PackageRouteOccurrence,
    PackageCandidateOperation,
    SourceOperation,
    Acquisition,
    RealizedAssembly,
    TransferBytes,
    RetainedAssemblyBytes,
    WorkspaceReplacement,
    Deadline,
}

public sealed record AssemblyReferenceResolutionWorkBudget
{
    public AssemblyReferenceResolutionWorkBudget(
        int maxPackageRouteOccurrences,
        int maxPackageCandidateOperations,
        int maxSourceOperations,
        int maxAcquisitions,
        int maxRealizedAssemblies,
        long maxTransferBytes,
        long maxRetainedAssemblyBytes,
        int maxWorkspaceReplacements,
        DateTimeOffset deadline)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxPackageRouteOccurrences);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxPackageCandidateOperations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSourceOperations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAcquisitions);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRealizedAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTransferBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxRetainedAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxWorkspaceReplacements);

        MaxPackageRouteOccurrences = maxPackageRouteOccurrences;
        MaxPackageCandidateOperations = maxPackageCandidateOperations;
        MaxSourceOperations = maxSourceOperations;
        MaxAcquisitions = maxAcquisitions;
        MaxRealizedAssemblies = maxRealizedAssemblies;
        MaxTransferBytes = maxTransferBytes;
        MaxRetainedAssemblyBytes = maxRetainedAssemblyBytes;
        MaxWorkspaceReplacements = maxWorkspaceReplacements;
        Deadline = deadline;
    }

    public int MaxPackageRouteOccurrences { get; }

    public int MaxPackageCandidateOperations { get; }

    public int MaxSourceOperations { get; }

    public int MaxAcquisitions { get; }

    public int MaxRealizedAssemblies { get; }

    public long MaxTransferBytes { get; }

    public long MaxRetainedAssemblyBytes { get; }

    public int MaxWorkspaceReplacements { get; }

    public DateTimeOffset Deadline { get; }
}

public sealed record AssemblyReferenceResolutionWorkExhaustion(
    AssemblyReferenceResolutionWorkKind Kind,
    long? ConfiguredMaximum,
    long? Consumed,
    long? Requested,
    DateTimeOffset? Deadline,
    DateTimeOffset? ObservedAt);

public sealed class AssemblyReferenceResolutionWorkExhaustedException :
    Exception
{
    public AssemblyReferenceResolutionWorkExhaustedException(
        AssemblyReferenceResolutionWorkExhaustion exhaustion)
        : base(
            $"Assembly-reference resolution work was exhausted ({exhaustion?.Kind}).")
    {
        Exhaustion = exhaustion
            ?? throw new ArgumentNullException(nameof(exhaustion));
    }

    public AssemblyReferenceResolutionWorkExhaustion Exhaustion { get; }
}

public sealed record AssemblyReferenceResolutionWorkReceipt(
    AssemblyReferenceResolutionWorkBudget Budget,
    long PackageRouteOccurrences,
    long PackageCandidateOperations,
    long SourceOperations,
    long Acquisitions,
    long RealizedAssemblies,
    long TransferBytes,
    long RetainedAssemblyBytes,
    long WorkspaceReplacements,
    AssemblyReferenceResolutionWorkExhaustion? Exhaustion);

internal sealed class AssemblyReferenceResolutionDeadlineCancellation
    : IDisposable
{
    static readonly TimeSpan MaximumTimerInterval =
        TimeSpan.FromDays(30);

    readonly object _gate = new();
    readonly TimeProvider _timeProvider;
    readonly DateTimeOffset _deadline;
    readonly CancellationTokenSource _cancellation = new();
    ITimer? _timer;
    bool _disposed;

    internal AssemblyReferenceResolutionDeadlineCancellation(
        TimeProvider timeProvider,
        DateTimeOffset deadline)
    {
        _timeProvider = timeProvider;
        _deadline = deadline;
        _timer = _timeProvider.CreateTimer(
            static state =>
                ((AssemblyReferenceResolutionDeadlineCancellation)state!)
                    .OnTimer(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _timer.Change(
            NextInterval(),
            Timeout.InfiniteTimeSpan);
    }

    internal CancellationToken Token => _cancellation.Token;

    internal bool IsCancellationRequested =>
        _cancellation.IsCancellationRequested;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _cancellation.Dispose();
        }
    }

    TimeSpan NextInterval()
    {
        TimeSpan remaining =
            _deadline - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            return TimeSpan.Zero;

        return remaining <= MaximumTimerInterval
            ? remaining
            : MaximumTimerInterval;
    }

    void OnTimer()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            TimeSpan next = NextInterval();
            if (next <= TimeSpan.Zero)
            {
                _cancellation.Cancel();
                return;
            }

            _timer!.Change(
                next,
                Timeout.InfiniteTimeSpan);
        }
    }
}

/// <summary>
/// Shared finite-work ledger for one assembly-reference resolution attempt.
/// Work is charged before the operation that can consume it.
/// </summary>
public sealed class AssemblyReferenceResolutionWorkLedger
{
    readonly object _gate = new();
    readonly TimeProvider _timeProvider;
    long _packageRouteOccurrences;
    long _packageCandidateOperations;
    long _sourceOperations;
    long _acquisitions;
    long _realizedAssemblies;
    long _transferBytes;
    long _retainedAssemblyBytes;
    long _workspaceReplacements;
    AssemblyReferenceResolutionWorkExhaustion? _exhaustion;

    public AssemblyReferenceResolutionWorkLedger(
        AssemblyReferenceResolutionWorkBudget budget,
        TimeProvider? timeProvider = null)
    {
        Budget = budget
            ?? throw new ArgumentNullException(nameof(budget));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AssemblyReferenceResolutionWorkBudget Budget { get; }

    public long GetRemainingAllowance(
        AssemblyReferenceResolutionWorkKind kind)
    {
        if (!Enum.IsDefined(kind)
            || kind == AssemblyReferenceResolutionWorkKind.Deadline)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        lock (_gate)
        {
            if (_exhaustion is not null
                || ObserveDeadlineLocked() is not null)
            {
                return 0;
            }

            (long consumed, long maximum) = GetConsumption(kind);
            return maximum - consumed;
        }
    }

    public void Charge(
        AssemblyReferenceResolutionWorkKind kind,
        long amount)
    {
        if (!TryCharge(kind, amount, out var exhaustion))
            throw new AssemblyReferenceResolutionWorkExhaustedException(
                exhaustion!);
    }

    public bool TryCharge(
        AssemblyReferenceResolutionWorkKind kind,
        long amount,
        out AssemblyReferenceResolutionWorkExhaustion? exhaustion)
    {
        if (!Enum.IsDefined(kind)
            || kind == AssemblyReferenceResolutionWorkKind.Deadline)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        lock (_gate)
        {
            if (_exhaustion is not null)
            {
                exhaustion = _exhaustion;
                return false;
            }
            if (ObserveDeadlineLocked() is { } deadlineExhaustion)
            {
                exhaustion = deadlineExhaustion;
                return false;
            }

            (long consumed, long maximum) = GetConsumption(kind);

            long next;
            bool overflowed = false;
            try
            {
                next = checked(consumed + amount);
            }
            catch (OverflowException)
            {
                next = long.MaxValue;
                overflowed = true;
            }

            if (overflowed || next > maximum)
            {
                _exhaustion =
                    new AssemblyReferenceResolutionWorkExhaustion(
                        kind,
                        maximum,
                        consumed,
                        amount,
                        Deadline: null,
                        ObservedAt: null);
                exhaustion = _exhaustion;
                return false;
            }

            switch (kind)
            {
                case AssemblyReferenceResolutionWorkKind
                    .PackageRouteOccurrence:
                    _packageRouteOccurrences = next;
                    break;
                case AssemblyReferenceResolutionWorkKind
                    .PackageCandidateOperation:
                    _packageCandidateOperations = next;
                    break;
                case AssemblyReferenceResolutionWorkKind.SourceOperation:
                    _sourceOperations = next;
                    break;
                case AssemblyReferenceResolutionWorkKind.Acquisition:
                    _acquisitions = next;
                    break;
                case AssemblyReferenceResolutionWorkKind.RealizedAssembly:
                    _realizedAssemblies = next;
                    break;
                case AssemblyReferenceResolutionWorkKind.TransferBytes:
                    _transferBytes = next;
                    break;
                case AssemblyReferenceResolutionWorkKind
                    .RetainedAssemblyBytes:
                    _retainedAssemblyBytes = next;
                    break;
                case AssemblyReferenceResolutionWorkKind
                    .WorkspaceReplacement:
                    _workspaceReplacements = next;
                    break;
            }

            exhaustion = null;
            return true;
        }
    }

    (long Consumed, long Maximum) GetConsumption(
        AssemblyReferenceResolutionWorkKind kind) =>
        kind switch
        {
            AssemblyReferenceResolutionWorkKind
                .PackageRouteOccurrence =>
                (_packageRouteOccurrences,
                    Budget.MaxPackageRouteOccurrences),
            AssemblyReferenceResolutionWorkKind
                .PackageCandidateOperation =>
                (_packageCandidateOperations,
                    Budget.MaxPackageCandidateOperations),
            AssemblyReferenceResolutionWorkKind.SourceOperation =>
                (_sourceOperations, Budget.MaxSourceOperations),
            AssemblyReferenceResolutionWorkKind.Acquisition =>
                (_acquisitions, Budget.MaxAcquisitions),
            AssemblyReferenceResolutionWorkKind.RealizedAssembly =>
                (_realizedAssemblies, Budget.MaxRealizedAssemblies),
            AssemblyReferenceResolutionWorkKind.TransferBytes =>
                (_transferBytes, Budget.MaxTransferBytes),
            AssemblyReferenceResolutionWorkKind.RetainedAssemblyBytes =>
                (_retainedAssemblyBytes,
                    Budget.MaxRetainedAssemblyBytes),
            AssemblyReferenceResolutionWorkKind.WorkspaceReplacement =>
                (_workspaceReplacements,
                    Budget.MaxWorkspaceReplacements),
            _ => throw new InvalidOperationException(
                "Unknown assembly-reference resolution work kind."),
        };

    internal AssemblyReferenceResolutionWorkExhaustion?
        ObserveExhaustion()
    {
        lock (_gate)
        {
            if (_exhaustion is not null)
                return _exhaustion;

            return ObserveDeadlineLocked();
        }
    }

    internal AssemblyReferenceResolutionDeadlineCancellation
        CreateDeadlineCancellation() =>
        new(_timeProvider, Budget.Deadline);

    internal AssemblyReferenceResolutionWorkExhaustion
        RecordDeadlineExhaustion()
    {
        lock (_gate)
        {
            if (_exhaustion is not null)
                return _exhaustion;

            _exhaustion =
                new AssemblyReferenceResolutionWorkExhaustion(
                    AssemblyReferenceResolutionWorkKind.Deadline,
                    ConfiguredMaximum: null,
                    Consumed: null,
                    Requested: null,
                    Budget.Deadline,
                    _timeProvider.GetUtcNow());
            return _exhaustion;
        }
    }

    internal AssemblyReferenceResolutionWorkReceipt Capture()
    {
        lock (_gate)
        {
            return new(
                Budget,
                _packageRouteOccurrences,
                _packageCandidateOperations,
                _sourceOperations,
                _acquisitions,
                _realizedAssemblies,
                _transferBytes,
                _retainedAssemblyBytes,
                _workspaceReplacements,
                _exhaustion);
        }
    }

    AssemblyReferenceResolutionWorkExhaustion?
        ObserveDeadlineLocked()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (now < Budget.Deadline)
            return null;

        _exhaustion =
            new AssemblyReferenceResolutionWorkExhaustion(
                AssemblyReferenceResolutionWorkKind.Deadline,
                ConfiguredMaximum: null,
                Consumed: null,
                Requested: null,
                Budget.Deadline,
                now);
        return _exhaustion;
    }
}

/// <summary>
/// Exact immutable Workspace generation used by one ladder attempt.
/// </summary>
public sealed class AssemblyReferenceResolutionGenerationReceipt
{
    internal AssemblyReferenceResolutionGenerationReceipt(
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopeRevisionIdentity scopeRevision,
        WorkspaceRegistrationRevisionIdentity registrationRevision,
        ArtifactRootCompositionGenerationIdentity physicalComposition)
    {
        Workspace = workspace;
        ScopeRevision = scopeRevision;
        RegistrationRevision = registrationRevision;
        PhysicalComposition = physicalComposition;
    }

    public InspectionWorkspaceIdentity Workspace { get; }

    public WorkspaceScopeRevisionIdentity ScopeRevision { get; }

    public WorkspaceRegistrationRevisionIdentity RegistrationRevision
    { get; }

    public ArtifactRootCompositionGenerationIdentity PhysicalComposition
    { get; }

    public static AssemblyReferenceResolutionGenerationReceipt Capture(
        WorkspaceScopeSnapshot scope,
        MemberCallGraphFocalScopeReceipt focalScope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(focalScope);
        if (!ReferenceEquals(
                scope.Revision.Identity,
                focalScope.ScopeRevision))
        {
            throw new ArgumentException(
                "The focal scope must retain the exact Workspace Scope revision.",
                nameof(focalScope));
        }

        return new(
            scope.Revision.Workspace,
            scope.Revision.Identity,
            focalScope.RegistrationRevision,
            scope.PhysicalComposition);
    }
}

/// <summary>
/// Workspace-owned proof that one active realization was atomically replaced
/// by a fresh realization of the same logical Workspace definition.
/// </summary>
public sealed class AssemblyReferenceResolutionWorkspaceReplacementReceipt
{
    internal AssemblyReferenceResolutionWorkspaceReplacementReceipt(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationOperationLease predecessor,
        WorkspaceRealizationOperationLease successor,
        AssemblyReferenceResolutionGenerationReceipt predecessorGeneration,
        AssemblyReferenceResolutionGenerationReceipt successorGeneration,
        object demandEvidence)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(successor);
        ArgumentNullException.ThrowIfNull(predecessorGeneration);
        ArgumentNullException.ThrowIfNull(successorGeneration);
        ArgumentNullException.ThrowIfNull(demandEvidence);
        if (!predecessor.IsOwnedBy(coordinator)
            || !successor.IsOwnedBy(coordinator)
            || !ReferenceEquals(
                predecessorGeneration.Workspace,
                predecessor.Realization)
            || !ReferenceEquals(
                predecessorGeneration.ScopeRevision,
                predecessor.Scope.Revision.Identity)
            || !ReferenceEquals(
                predecessorGeneration.RegistrationRevision,
                predecessor.Definition.Registrations.Identity)
            || !ReferenceEquals(
                predecessorGeneration.PhysicalComposition,
                predecessor.Scope.PhysicalComposition)
            || !ReferenceEquals(
                successorGeneration.Workspace,
                successor.Realization)
            || !ReferenceEquals(
                successorGeneration.ScopeRevision,
                successor.Scope.Revision.Identity)
            || !ReferenceEquals(
                successorGeneration.RegistrationRevision,
                successor.Definition.Registrations.Identity)
            || !ReferenceEquals(
                successorGeneration.PhysicalComposition,
                successor.Scope.PhysicalComposition))
        {
            throw new ArgumentException(
                "A replacement receipt requires exact active predecessor and successor generation evidence.");
        }
        if (ReferenceEquals(
                predecessor.Realization,
                successor.Realization)
            || ReferenceEquals(
                predecessor.Definition.Scope.Identity,
                successor.Definition.Scope.Identity)
            || ReferenceEquals(
                predecessor.Definition.Registrations.Identity,
                successor.Definition.Registrations.Identity)
            || ReferenceEquals(
                predecessorGeneration.PhysicalComposition,
                successorGeneration.PhysicalComposition)
            || !ReferenceEquals(
                predecessor.Definition.Plan,
                successor.Definition.Plan)
            || !WorkspaceLogicalScopeCorrespondence.Matches(
                predecessor.Scope.Revision,
                successor.Scope.Revision))
        {
            throw new ArgumentException(
                "A replacement receipt requires a fresh realization of the same logical Workspace definition.",
                nameof(successor));
        }

        Predecessor = predecessorGeneration;
        Successor = successorGeneration;
        PredecessorDefinition = predecessor.Definition;
        SuccessorDefinition = successor.Definition;
        DemandEvidence = demandEvidence;
    }

    public AssemblyReferenceResolutionGenerationReceipt Predecessor
    { get; }

    public AssemblyReferenceResolutionGenerationReceipt Successor
    { get; }

    public WorkspaceDefinitionSnapshot PredecessorDefinition { get; }

    public WorkspaceDefinitionSnapshot SuccessorDefinition { get; }

    public object DemandEvidence { get; }
}

/// <summary>
/// Owner-issued correspondence between predecessor and successor requests
/// across one immutable Workspace replacement.
/// </summary>
public sealed class AssemblyReferenceResolutionContinuationReceipt
{
    public AssemblyReferenceResolutionContinuationReceipt(
        AssemblyBindingRequest predecessorRequest,
        AssemblyReferenceResolutionGenerationReceipt predecessorGeneration,
        AssemblyBindingPolicyVersion predecessorPolicyVersion,
        AssemblyBindingRequest successorRequest,
        AssemblyReferenceResolutionGenerationReceipt successorGeneration,
        AssemblyBindingPolicyVersion successorPolicyVersion,
        AssemblyReferenceResolutionWorkspaceReplacementReceipt replacement)
    {
        ArgumentNullException.ThrowIfNull(predecessorRequest);
        ArgumentNullException.ThrowIfNull(predecessorGeneration);
        ArgumentNullException.ThrowIfNull(predecessorPolicyVersion);
        ArgumentNullException.ThrowIfNull(successorRequest);
        ArgumentNullException.ThrowIfNull(successorGeneration);
        ArgumentNullException.ThrowIfNull(successorPolicyVersion);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!Equals(
                predecessorRequest.Target,
                successorRequest.Target)
            || predecessorRequest.Scope != successorRequest.Scope)
        {
            throw new ArgumentException(
                "A continuation must preserve the exact binding target and resolution scope.",
                nameof(successorRequest));
        }
        if (ReferenceEquals(predecessorRequest, successorRequest)
            || ReferenceEquals(
                predecessorRequest.Origin,
                successorRequest.Origin))
        {
            throw new ArgumentException(
                "A continuation must identify a fresh binding request and origin.",
                nameof(successorRequest));
        }
        if (ReferenceEquals(
                predecessorGeneration,
                successorGeneration)
            || ReferenceEquals(
                predecessorGeneration.Workspace,
                successorGeneration.Workspace)
            || ReferenceEquals(
                predecessorGeneration.ScopeRevision,
                successorGeneration.ScopeRevision)
            || ReferenceEquals(
                predecessorGeneration.RegistrationRevision,
                successorGeneration.RegistrationRevision)
            || !ReferenceEquals(
                replacement.Predecessor,
                predecessorGeneration)
            || !ReferenceEquals(
                replacement.Successor,
                successorGeneration))
        {
            throw new ArgumentException(
                "A continuation must identify the exact fresh Workspace replacement generation.",
                nameof(successorGeneration));
        }
        if (ReferenceEquals(
                predecessorPolicyVersion,
                successorPolicyVersion))
        {
            throw new ArgumentException(
                "A continuation must identify the successor generation's fresh binding-policy version.",
                nameof(successorPolicyVersion));
        }

        PredecessorRequest = predecessorRequest;
        PredecessorGeneration = predecessorGeneration;
        PredecessorPolicyVersion = predecessorPolicyVersion;
        SuccessorRequest = successorRequest;
        SuccessorGeneration = successorGeneration;
        SuccessorPolicyVersion = successorPolicyVersion;
        Replacement = replacement;
    }

    public AssemblyBindingRequest PredecessorRequest { get; }

    public AssemblyReferenceResolutionGenerationReceipt PredecessorGeneration
    { get; }

    public AssemblyBindingPolicyVersion PredecessorPolicyVersion { get; }

    public AssemblyBindingRequest SuccessorRequest { get; }

    public AssemblyReferenceResolutionGenerationReceipt SuccessorGeneration
    { get; }

    public AssemblyBindingPolicyVersion SuccessorPolicyVersion { get; }

    public AssemblyReferenceResolutionWorkspaceReplacementReceipt Replacement
    { get; }

    public object OwnerEvidence => Replacement.DemandEvidence;
}

public abstract record AssemblyReferenceResolutionContextOutcome
{
    private protected AssemblyReferenceResolutionContextOutcome(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation)
    {
        Request = request
            ?? throw new ArgumentNullException(nameof(request));
        Generation = generation
            ?? throw new ArgumentNullException(nameof(generation));
    }

    public AssemblyBindingRequest Request { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }

    public sealed record Selected :
        AssemblyReferenceResolutionContextOutcome
    {
        public Selected(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyBindingSelectionSnapshot selection)
            : base(request, generation)
        {
            Selection = selection
                ?? throw new ArgumentNullException(nameof(selection));
        }

        public AssemblyBindingSelectionSnapshot Selection { get; }
    }

    public sealed record NonParticipating :
        AssemblyReferenceResolutionContextOutcome
    {
        public NonParticipating(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            MemberCallGraphFocalScopeReceipt focalScope,
            object ownerEvidence)
            : base(request, generation)
        {
            ArgumentNullException.ThrowIfNull(focalScope);
            ArgumentNullException.ThrowIfNull(ownerEvidence);
            if (request.Target
                is not AssemblyBindingTarget.IntrinsicCoreLibrary)
            {
                throw new ArgumentException(
                    "Only an intrinsic CoreLib request may carry context non-participation.",
                    nameof(request));
            }

            FocalScope = focalScope;
            OwnerEvidence = ownerEvidence;
        }

        public MemberCallGraphFocalScopeReceipt FocalScope { get; }

        public object OwnerEvidence { get; }
    }
}

public abstract record AssemblyReferenceResolutionContextAdvancement
{
    private protected AssemblyReferenceResolutionContextAdvancement(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation)
    {
        Request = request;
        Generation = generation;
    }

    public AssemblyBindingRequest Request { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }

    public sealed record NoNameOwner :
        AssemblyReferenceResolutionContextAdvancement
    {
        internal NoNameOwner(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyBindingSelectionSnapshot selection)
            : base(request, generation) =>
            Selection = selection;

        public AssemblyBindingSelectionSnapshot Selection { get; }
    }

    public sealed record NonParticipating :
        AssemblyReferenceResolutionContextAdvancement
    {
        internal NonParticipating(
            AssemblyReferenceResolutionContextOutcome.NonParticipating
                context)
            : base(context.Request, context.Generation) =>
            Context = context;

        public AssemblyReferenceResolutionContextOutcome.NonParticipating
            Context
        { get; }
    }
}

/// <summary>
/// Owner-specific route evidence bound to one exact request and generation.
/// Package and Platform adapters derive their typed routes from this base.
/// </summary>
public abstract class AssemblyReferenceExternalRoute
{
    protected AssemblyReferenceExternalRoute(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation)
    {
        Request = request
            ?? throw new ArgumentNullException(nameof(request));
        Generation = generation
            ?? throw new ArgumentNullException(nameof(generation));
    }

    public AssemblyBindingRequest Request { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }
}

public abstract record AssemblyReferenceExternalRouteSetFormationOutcome
{
    private protected AssemblyReferenceExternalRouteSetFormationOutcome()
    {
    }

    public sealed record Completed(
        AssemblyReferenceExternalRouteSet RouteSet)
        : AssemblyReferenceExternalRouteSetFormationOutcome;

    public sealed record Unavailable(object Evidence)
        : AssemblyReferenceExternalRouteSetFormationOutcome;

    public sealed record Rejected(object Evidence)
        : AssemblyReferenceExternalRouteSetFormationOutcome;

    public sealed record Incomplete(object Evidence)
        : AssemblyReferenceExternalRouteSetFormationOutcome;
}

public abstract record AssemblyReferenceExternalRouteOutcome
{
    private protected AssemblyReferenceExternalRouteOutcome(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation,
        AssemblyReferenceExternalRouteSet routeSet,
        AssemblyReferenceResolutionContinuationReceipt? continuation)
    {
        Request = request;
        Generation = generation;
        RouteSet = routeSet;
        Continuation = continuation;
    }

    public AssemblyBindingRequest Request { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }

    public AssemblyReferenceExternalRouteSet RouteSet { get; }

    public AssemblyReferenceResolutionContinuationReceipt? Continuation
    { get; }

    public sealed record Completed :
        AssemblyReferenceExternalRouteOutcome
    {
        public Completed(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceExternalRouteSet routeSet,
            AssemblyBindingSelectionSnapshot selection,
            AssemblyReferenceExternalRoute? selectedRoute = null,
            AssemblyReferenceResolutionContinuationReceipt?
                continuation = null)
            : base(request, generation, routeSet, continuation)
        {
            Selection = selection
                ?? throw new ArgumentNullException(nameof(selection));
            SelectedRoute = selectedRoute;
        }

        public AssemblyBindingSelectionSnapshot Selection { get; }

        public AssemblyReferenceExternalRoute? SelectedRoute { get; }
    }

    public sealed record AcquisitionRequired :
        AssemblyReferenceExternalRouteOutcome
    {
        public AcquisitionRequired(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceExternalRouteSet routeSet,
            AssemblyReferenceExternalRoute selectedRoute,
            object ownerEvidence)
            : base(request, generation, routeSet, continuation: null)
        {
            ArgumentNullException.ThrowIfNull(selectedRoute);
            ArgumentNullException.ThrowIfNull(ownerEvidence);
            if (!routeSet.Routes.Any(
                    route => ReferenceEquals(route, selectedRoute)))
            {
                throw new ArgumentException(
                    "An acquisition demand must select one route from its exact route set.",
                    nameof(selectedRoute));
            }

            SelectedRoute = selectedRoute;
            OwnerEvidence = ownerEvidence;
        }

        public AssemblyReferenceExternalRoute SelectedRoute { get; }

        public object OwnerEvidence { get; }
    }

    public sealed record Unavailable :
        AssemblyReferenceExternalRouteOutcome
    {
        public Unavailable(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceExternalRouteSet routeSet,
            object evidence,
            AssemblyReferenceResolutionContinuationReceipt?
                continuation = null)
            : base(request, generation, routeSet, continuation) =>
            Evidence = evidence
                ?? throw new ArgumentNullException(nameof(evidence));

        public object Evidence { get; }
    }

    public sealed record Rejected :
        AssemblyReferenceExternalRouteOutcome
    {
        public Rejected(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceExternalRouteSet routeSet,
            object evidence,
            AssemblyReferenceResolutionContinuationReceipt?
                continuation = null)
            : base(request, generation, routeSet, continuation) =>
            Evidence = evidence
                ?? throw new ArgumentNullException(nameof(evidence));

        public object Evidence { get; }
    }

    public sealed record Incomplete :
        AssemblyReferenceExternalRouteOutcome
    {
        public Incomplete(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceExternalRouteSet routeSet,
            object evidence,
            AssemblyReferenceResolutionContinuationReceipt?
                continuation = null)
            : base(request, generation, routeSet, continuation) =>
            Evidence = evidence
                ?? throw new ArgumentNullException(nameof(evidence));

        public object Evidence { get; }
    }
}

/// <summary>
/// Complete deferred external route set for one context advancement.
/// </summary>
public sealed class AssemblyReferenceExternalRouteSet
{
    readonly Func<
        AssemblyReferenceResolutionWorkLedger,
        CancellationToken,
        ValueTask<AssemblyReferenceExternalRouteOutcome>> _execute;

    public AssemblyReferenceExternalRouteSet(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation,
        AssemblyReferenceResolutionContextAdvancement advancement,
        ImmutableArray<AssemblyReferenceExternalRoute> routes,
        Func<
            AssemblyReferenceResolutionWorkLedger,
            CancellationToken,
            ValueTask<AssemblyReferenceExternalRouteOutcome>> execute)
    {
        Request = request
            ?? throw new ArgumentNullException(nameof(request));
        Generation = generation
            ?? throw new ArgumentNullException(nameof(generation));
        Advancement = advancement
            ?? throw new ArgumentNullException(nameof(advancement));
        if (routes.IsDefault
            || routes.Any(static route => route is null))
        {
            throw new ArgumentException(
                "External routes must be a non-default collection without null entries.",
                nameof(routes));
        }

        var routeIdentities =
            new HashSet<AssemblyReferenceExternalRoute>(
                ReferenceEqualityComparer.Instance);
        foreach (AssemblyReferenceExternalRoute route in routes)
        {
            if (!routeIdentities.Add(route))
            {
                throw new ArgumentException(
                    "External routes must not repeat one route instance.",
                    nameof(routes));
            }
        }

        Routes = routes;
        _execute = execute
            ?? throw new ArgumentNullException(nameof(execute));
    }

    public AssemblyBindingRequest Request { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }

    public AssemblyReferenceResolutionContextAdvancement Advancement
    { get; }

    public ImmutableArray<AssemblyReferenceExternalRoute> Routes { get; }

    internal ValueTask<AssemblyReferenceExternalRouteOutcome> ExecuteAsync(
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken) =>
        _execute(work, cancellationToken);
}

/// <summary>
/// Exact immutable rung plan with lazy external route formation.
/// </summary>
public sealed class AssemblyReferenceResolutionRoutePlan
{
    readonly Func<
        AssemblyReferenceResolutionWorkLedger,
        CancellationToken,
        ValueTask<AssemblyReferenceResolutionContextOutcome>>
        _evaluateContext;
    readonly Func<
        AssemblyReferenceResolutionContextAdvancement,
        AssemblyReferenceResolutionWorkLedger,
        CancellationToken,
        ValueTask<AssemblyReferenceExternalRouteSetFormationOutcome>>
        _formExternalRoutes;

    public AssemblyReferenceResolutionRoutePlan(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation,
        MemberCallGraphFocalScopeReceipt focalScope,
        AssemblyBindingPolicyVersion policyVersion,
        Func<
            AssemblyReferenceResolutionWorkLedger,
            CancellationToken,
            ValueTask<AssemblyReferenceResolutionContextOutcome>>
            evaluateContext,
        Func<
            AssemblyReferenceResolutionContextAdvancement,
            AssemblyReferenceResolutionWorkLedger,
            CancellationToken,
            ValueTask<AssemblyReferenceExternalRouteSetFormationOutcome>>
            formExternalRoutes)
    {
        Request = request
            ?? throw new ArgumentNullException(nameof(request));
        Generation = generation
            ?? throw new ArgumentNullException(nameof(generation));
        FocalScope = focalScope
            ?? throw new ArgumentNullException(nameof(focalScope));
        PolicyVersion = policyVersion
            ?? throw new ArgumentNullException(nameof(policyVersion));
        if (!ReferenceEquals(
                generation.ScopeRevision,
                focalScope.ScopeRevision)
            || !ReferenceEquals(
                generation.RegistrationRevision,
                focalScope.RegistrationRevision))
        {
            throw new ArgumentException(
                "The route plan must retain the generation's exact focal Scope and registration revisions.",
                nameof(focalScope));
        }

        _evaluateContext = evaluateContext
            ?? throw new ArgumentNullException(nameof(evaluateContext));
        _formExternalRoutes = formExternalRoutes
            ?? throw new ArgumentNullException(nameof(formExternalRoutes));
    }

    public AssemblyBindingRequest Request { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }

    public AssemblyBindingPolicyVersion PolicyVersion { get; }

    internal ValueTask<AssemblyReferenceResolutionContextOutcome>
        EvaluateContextAsync(
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken) =>
        _evaluateContext(work, cancellationToken);

    internal ValueTask<
        AssemblyReferenceExternalRouteSetFormationOutcome>
        FormExternalRoutesAsync(
        AssemblyReferenceResolutionContextAdvancement advancement,
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken) =>
        _formExternalRoutes(advancement, work, cancellationToken);
}

public sealed class AssemblyReferenceResolutionRequest
{
    public AssemblyReferenceResolutionRequest(
        AssemblyReferenceResolutionRoutePlan routePlan,
        AssemblyReferenceResolutionWorkLedger work)
    {
        RoutePlan = routePlan
            ?? throw new ArgumentNullException(nameof(routePlan));
        Work = work
            ?? throw new ArgumentNullException(nameof(work));
    }

    public AssemblyReferenceResolutionRoutePlan RoutePlan { get; }

    public AssemblyBindingRequest BindingRequest => RoutePlan.Request;

    public AssemblyReferenceResolutionGenerationReceipt Generation =>
        RoutePlan.Generation;

    public MemberCallGraphFocalScopeReceipt FocalScope =>
        RoutePlan.FocalScope;

    public AssemblyBindingPolicyVersion PolicyVersion =>
        RoutePlan.PolicyVersion;

    public AssemblyReferenceResolutionWorkLedger Work { get; }
}

public sealed record AssemblyReferenceResolutionRungAttempt(
    AssemblyReferenceResolutionRung Rung,
    object Evidence);

public abstract record AssemblyReferenceResolutionOutcome
{
    private protected AssemblyReferenceResolutionOutcome(
        AssemblyBindingRequest request,
        AssemblyBindingRequest finalRequest,
        AssemblyReferenceResolutionGenerationReceipt generation,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
        AssemblyReferenceResolutionWorkReceipt work)
    {
        Request = request;
        FinalRequest = finalRequest;
        Generation = generation;
        Trace = trace;
        Work = work;
    }

    public AssemblyBindingRequest Request { get; }

    public AssemblyBindingRequest FinalRequest { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }

    public ImmutableArray<AssemblyReferenceResolutionRungAttempt> Trace
    { get; }

    public AssemblyReferenceResolutionWorkReceipt Work { get; }

    public sealed record Resolved :
        AssemblyReferenceResolutionOutcome
    {
        internal Resolved(
            AssemblyBindingRequest request,
            AssemblyBindingRequest finalRequest,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceResolutionRung rung,
            AssemblyBindingSelectionSnapshot selection,
            AssemblyReferenceExternalRoute? route,
            ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
            AssemblyReferenceResolutionWorkReceipt work)
            : base(request, finalRequest, generation, trace, work)
        {
            Rung = rung;
            Selection = selection;
            Route = route;
        }

        public AssemblyReferenceResolutionRung Rung { get; }

        public AssemblyBindingSelectionSnapshot Selection { get; }

        public AssemblyBindingSelection.Selected Selected =>
            (AssemblyBindingSelection.Selected)Selection.Selection;

        public AssemblyReferenceExternalRoute? Route { get; }
    }

    public sealed record Unbound :
        AssemblyReferenceResolutionOutcome
    {
        internal Unbound(
            AssemblyBindingRequest request,
            AssemblyBindingRequest finalRequest,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceResolutionRung rung,
            AssemblyBindingSelectionSnapshot selection,
            ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
            AssemblyReferenceResolutionWorkReceipt work)
            : base(request, finalRequest, generation, trace, work)
        {
            Rung = rung;
            Selection = selection;
        }

        public AssemblyReferenceResolutionRung Rung { get; }

        public AssemblyBindingSelectionSnapshot Selection { get; }

        public AssemblyBindingMissDisposition Disposition =>
            ((AssemblyBindingSelection.Missing)Selection.Selection)
                .Disposition;
    }

    public sealed record AcquisitionRequired :
        AssemblyReferenceResolutionOutcome
    {
        internal AcquisitionRequired(
            AssemblyBindingRequest request,
            AssemblyBindingRequest finalRequest,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceExternalRouteSet routeSet,
            AssemblyReferenceExternalRoute selectedRoute,
            object ownerEvidence,
            ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
            AssemblyReferenceResolutionWorkReceipt work)
            : base(
                request,
                finalRequest,
                generation,
                trace,
                work)
        {
            RouteSet = routeSet;
            SelectedRoute = selectedRoute;
            OwnerEvidence = ownerEvidence;
        }

        public AssemblyReferenceExternalRouteSet RouteSet { get; }

        public AssemblyReferenceExternalRoute SelectedRoute { get; }

        public object OwnerEvidence { get; }
    }

    public sealed record Ambiguous :
        AssemblyReferenceResolutionOutcome
    {
        internal Ambiguous(
            AssemblyBindingRequest request,
            AssemblyBindingRequest finalRequest,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceResolutionRung rung,
            AssemblyBindingSelectionSnapshot selection,
            ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
            AssemblyReferenceResolutionWorkReceipt work)
            : base(request, finalRequest, generation, trace, work)
        {
            Rung = rung;
            Selection = selection;
        }

        public AssemblyReferenceResolutionRung Rung { get; }

        public AssemblyBindingSelectionSnapshot Selection { get; }

        public AssemblyBindingSelection.Ambiguous Candidates =>
            (AssemblyBindingSelection.Ambiguous)Selection.Selection;
    }

    public sealed record Unavailable :
        AssemblyReferenceResolutionOutcome
    {
        internal Unavailable(
            AssemblyBindingRequest request,
            AssemblyBindingRequest finalRequest,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceResolutionRung rung,
            object evidence,
            ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
            AssemblyReferenceResolutionWorkReceipt work)
            : base(request, finalRequest, generation, trace, work)
        {
            Rung = rung;
            Evidence = evidence;
        }

        public AssemblyReferenceResolutionRung Rung { get; }

        public object Evidence { get; }
    }

    public sealed record Rejected :
        AssemblyReferenceResolutionOutcome
    {
        internal Rejected(
            AssemblyBindingRequest request,
            AssemblyBindingRequest finalRequest,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceResolutionRung rung,
            object evidence,
            ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
            AssemblyReferenceResolutionWorkReceipt work)
            : base(request, finalRequest, generation, trace, work)
        {
            Rung = rung;
            Evidence = evidence;
        }

        public AssemblyReferenceResolutionRung Rung { get; }

        public object Evidence { get; }
    }

    public sealed record Incomplete :
        AssemblyReferenceResolutionOutcome
    {
        internal Incomplete(
            AssemblyBindingRequest request,
            AssemblyBindingRequest finalRequest,
            AssemblyReferenceResolutionGenerationReceipt generation,
            AssemblyReferenceResolutionRung rung,
            object evidence,
            ImmutableArray<AssemblyReferenceResolutionRungAttempt> trace,
            AssemblyReferenceResolutionWorkReceipt work)
            : base(request, finalRequest, generation, trace, work)
        {
            Rung = rung;
            Evidence = evidence;
        }

        public AssemblyReferenceResolutionRung Rung { get; }

        public object Evidence { get; }
    }
}

public static class AssemblyReferenceResolutionLadder
{
    public static async ValueTask<AssemblyReferenceResolutionOutcome>
        ExecuteAsync(
        AssemblyReferenceResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var trace =
            ImmutableArray.CreateBuilder<
                AssemblyReferenceResolutionRungAttempt>();
        if (request.Work.ObserveExhaustion() is { } initialExhaustion)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                initialExhaustion,
                trace);
        }

        using AssemblyReferenceResolutionDeadlineCancellation
            deadlineCancellation =
            request.Work.CreateDeadlineCancellation();
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadlineCancellation.Token);
        CancellationToken operationToken =
            operationCancellation.Token;

        AssemblyReferenceResolutionContextOutcome context;
        try
        {
            context =
                await request.RoutePlan.EvaluateContextAsync(
                        request.Work,
                        operationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (deadlineCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            return DeadlineIncomplete(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                trace);
        }
        catch (AssemblyReferenceResolutionWorkExhaustedException exhausted)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                exhausted.Exhaustion,
                trace);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (context is null)
        {
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                "The context owner returned no outcome.",
                trace);
        }
        if (request.Work.ObserveExhaustion() is { } contextExhaustion)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                contextExhaustion,
                trace);
        }
        if (!Matches(request, context.Request, context.Generation))
        {
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                "The context rung returned evidence for another request or generation.",
                trace);
        }

        trace.Add(
            new(
                AssemblyReferenceResolutionRung.ReferencingContext,
                context));
        AssemblyReferenceResolutionContextAdvancement? advancement;
        if (context
            is AssemblyReferenceResolutionContextOutcome
                .NonParticipating nonParticipating)
        {
            advancement = ValidateNonParticipation(
                request,
                nonParticipating);
        }
        else if (context
            is AssemblyReferenceResolutionContextOutcome.Selected
                selected)
        {
            if (!AdvanceOrCompleteContext(
                    request,
                    selected.Selection,
                    trace,
                    out AssemblyReferenceResolutionOutcome? completed))
            {
                return completed!;
            }

            advancement =
                new AssemblyReferenceResolutionContextAdvancement
                    .NoNameOwner(
                        request.BindingRequest,
                        request.Generation,
                        selected.Selection);
        }
        else
        {
            throw new InvalidOperationException(
                "Unknown assembly-reference context outcome.");
        }
        if (advancement is null)
        {
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                "The context rung did not produce valid advancement evidence.",
                trace);
        }

        if (request.Work.ObserveExhaustion() is { } formationExhaustion)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                formationExhaustion,
                trace);
        }

        AssemblyReferenceExternalRouteSetFormationOutcome formation;
        try
        {
            formation =
                await request.RoutePlan.FormExternalRoutesAsync(
                        advancement,
                        request.Work,
                        operationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (deadlineCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            return DeadlineIncomplete(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                trace);
        }
        catch (AssemblyReferenceResolutionWorkExhaustedException exhausted)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                exhausted.Exhaustion,
                trace);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Work.ObserveExhaustion() is { } routeExhaustion)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                routeExhaustion,
                trace);
        }

        if (formation is null)
        {
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                "The external route owner returned no formation outcome.",
                trace);
        }

        switch (formation)
        {
            case AssemblyReferenceExternalRouteSetFormationOutcome
                .Unavailable unavailable:
                if (unavailable.Evidence is null)
                {
                    return Rejected(
                        request,
                        AssemblyReferenceResolutionRung.ExternalSupplier,
                        "The external route owner returned unavailable without evidence.",
                        trace);
                }
                trace.Add(
                    new(
                        AssemblyReferenceResolutionRung.ExternalSupplier,
                        unavailable));
                return Unavailable(
                    request,
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    unavailable.Evidence,
                    trace);
            case AssemblyReferenceExternalRouteSetFormationOutcome
                .Rejected rejected:
                if (rejected.Evidence is null)
                {
                    return Rejected(
                        request,
                        AssemblyReferenceResolutionRung.ExternalSupplier,
                        "The external route owner returned rejection without evidence.",
                        trace);
                }
                trace.Add(
                    new(
                        AssemblyReferenceResolutionRung.ExternalSupplier,
                        rejected));
                return Rejected(
                    request,
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    rejected.Evidence,
                    trace);
            case AssemblyReferenceExternalRouteSetFormationOutcome
                .Incomplete incomplete:
                if (incomplete.Evidence is null)
                {
                    return Rejected(
                        request,
                        AssemblyReferenceResolutionRung.ExternalSupplier,
                        "The external route owner returned incomplete without evidence.",
                        trace);
                }
                trace.Add(
                    new(
                        AssemblyReferenceResolutionRung.ExternalSupplier,
                        incomplete));
                return Incomplete(
                    request,
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    incomplete.Evidence,
                    trace);
        }

        AssemblyReferenceExternalRouteSet routeSet =
            ((AssemblyReferenceExternalRouteSetFormationOutcome.Completed)
                formation).RouteSet;
        if (routeSet is null)
        {
            trace.Add(
                new(
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    formation));
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                "The external route owner completed without a route set.",
                trace);
        }
        if (!Matches(request, routeSet.Request, routeSet.Generation)
            || !ReferenceEquals(routeSet.Advancement, advancement)
            || routeSet.Routes.Any(route =>
                !Matches(request, route.Request, route.Generation)))
        {
            trace.Add(
                new(
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    formation));
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                "The external route set does not retain its exact request, generation, or context advancement.",
                trace);
        }

        AssemblyReferenceExternalRouteOutcome external;
        try
        {
            external =
                await routeSet.ExecuteAsync(
                        request.Work,
                        operationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (deadlineCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            return DeadlineIncomplete(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                trace);
        }
        catch (AssemblyReferenceResolutionWorkExhaustedException exhausted)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                exhausted.Exhaustion,
                trace);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (external is null)
        {
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                "The external route set returned no outcome.",
                trace);
        }
        if (request.Work.ObserveExhaustion() is { } externalExhaustion)
        {
            return Incomplete(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                externalExhaustion,
                trace);
        }
        if (!MatchesExternal(request, external)
            || !ReferenceEquals(external.RouteSet, routeSet))
        {
            return Rejected(
                request,
                AssemblyReferenceResolutionRung.ExternalSupplier,
                "The external rung returned evidence for another request, generation, or route set.",
                trace);
        }

        trace.Add(
            new(
                AssemblyReferenceResolutionRung.ExternalSupplier,
                external));
        return external switch
        {
            AssemblyReferenceExternalRouteOutcome.Completed completed =>
                CompleteSelection(
                    request,
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    completed.Request,
                    completed.Generation,
                    FinalPolicyVersion(request, completed),
                    completed.Selection,
                    completed.SelectedRoute,
                    routeSet,
                    trace),
            AssemblyReferenceExternalRouteOutcome.AcquisitionRequired
                acquisition =>
                    AcquisitionRequired(
                        request,
                        acquisition,
                        trace),
            AssemblyReferenceExternalRouteOutcome.Unavailable unavailable =>
                Unavailable(
                    request,
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    unavailable.Request,
                    unavailable.Generation,
                    unavailable.Evidence,
                    trace),
            AssemblyReferenceExternalRouteOutcome.Rejected rejected =>
                Rejected(
                    request,
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    rejected.Request,
                    rejected.Generation,
                    rejected.Evidence,
                    trace),
            AssemblyReferenceExternalRouteOutcome.Incomplete incomplete =>
                Incomplete(
                    request,
                    AssemblyReferenceResolutionRung.ExternalSupplier,
                    incomplete.Request,
                    incomplete.Generation,
                    incomplete.Evidence,
                    trace),
            _ => throw new InvalidOperationException(
                "Unknown external assembly-reference outcome."),
        };
    }

    static AssemblyReferenceResolutionContextAdvancement?
        ValidateNonParticipation(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionContextOutcome.NonParticipating context)
    {
        if (request.BindingRequest.Target
                is not AssemblyBindingTarget.IntrinsicCoreLibrary
            || !ReferenceEquals(
                context.FocalScope,
                request.FocalScope))
        {
            return null;
        }

        return new AssemblyReferenceResolutionContextAdvancement
            .NonParticipating(context);
    }

    static bool AdvanceOrCompleteContext(
        AssemblyReferenceResolutionRequest request,
        AssemblyBindingSelectionSnapshot selection,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace,
        out AssemblyReferenceResolutionOutcome? completed)
    {
        if (!ReferenceEquals(
                selection.Version,
                request.PolicyVersion))
        {
            completed = Rejected(
                request,
                AssemblyReferenceResolutionRung.ReferencingContext,
                "The context selection was produced by another binding-policy version.",
                trace);
            return false;
        }

        AssemblyBindingSelection validated =
            AssemblyBindingSelection.ValidateForMetadataRequest(
                request.BindingRequest,
                selection.Selection);
        if (validated is AssemblyBindingSelection.Missing
            {
                Disposition:
                    AssemblyBindingMissDisposition.NoNameOwner,
            })
        {
            completed = null;
            return true;
        }

        completed = CompleteSelection(
            request,
            AssemblyReferenceResolutionRung.ReferencingContext,
            request.BindingRequest,
            request.Generation,
            request.PolicyVersion,
            ReferenceEquals(validated, selection.Selection)
                ? selection
                : new AssemblyBindingSelectionSnapshot(
                    selection.Version,
                    validated),
            route: null,
            routeSet: null,
            trace);
        return false;
    }

    static AssemblyReferenceResolutionOutcome CompleteSelection(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        AssemblyBindingRequest finalRequest,
        AssemblyReferenceResolutionGenerationReceipt finalGeneration,
        AssemblyBindingPolicyVersion expectedPolicyVersion,
        AssemblyBindingSelectionSnapshot selection,
        AssemblyReferenceExternalRoute? route,
        AssemblyReferenceExternalRouteSet? routeSet,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
    {
        if (!ReferenceEquals(
                selection.Version,
                expectedPolicyVersion))
        {
            return Rejected(
                request,
                rung,
                finalRequest,
                finalGeneration,
                "The selection was produced by another binding-policy version.",
                trace);
        }

        AssemblyBindingSelection validated =
            AssemblyBindingSelection.ValidateForMetadataRequest(
                finalRequest,
                selection.Selection);
        AssemblyBindingSelectionSnapshot finalSelection =
            ReferenceEquals(validated, selection.Selection)
                ? selection
                : new AssemblyBindingSelectionSnapshot(
                    selection.Version,
                    validated);
        switch (validated)
        {
            case AssemblyBindingSelection.Selected:
                if (rung
                        == AssemblyReferenceResolutionRung.ExternalSupplier
                    && (route is null
                        || routeSet is null
                        || !routeSet.Routes.Any(
                            candidate =>
                                ReferenceEquals(candidate, route))))
                {
                    return Rejected(
                        request,
                        rung,
                        finalRequest,
                        finalGeneration,
                        "An external selection must retain one route from the completed route set.",
                        trace);
                }
                return new AssemblyReferenceResolutionOutcome.Resolved(
                    request.BindingRequest,
                    finalRequest,
                    finalGeneration,
                    rung,
                    finalSelection,
                    route,
                    trace.ToImmutable(),
                    request.Work.Capture());
            case AssemblyBindingSelection.Missing:
                if (route is not null)
                {
                    return Rejected(
                        request,
                        rung,
                        finalRequest,
                        finalGeneration,
                        "A missing selection cannot retain a selected external route.",
                        trace);
                }
                return new AssemblyReferenceResolutionOutcome.Unbound(
                    request.BindingRequest,
                    finalRequest,
                    finalGeneration,
                    rung,
                    finalSelection,
                    trace.ToImmutable(),
                    request.Work.Capture());
            case AssemblyBindingSelection.Ambiguous:
                if (route is not null)
                {
                    return Rejected(
                        request,
                        rung,
                        finalRequest,
                        finalGeneration,
                        "An ambiguous selection cannot retain one selected external route.",
                        trace);
                }
                return new AssemblyReferenceResolutionOutcome.Ambiguous(
                    request.BindingRequest,
                    finalRequest,
                    finalGeneration,
                    rung,
                    finalSelection,
                    trace.ToImmutable(),
                    request.Work.Capture());
            case AssemblyBindingSelection.Unavailable unavailable:
                return Unavailable(
                    request,
                    rung,
                    finalRequest,
                    finalGeneration,
                    unavailable.Failure,
                    trace);
            case AssemblyBindingSelection.Rejected rejected:
                return Rejected(
                    request,
                    rung,
                    finalRequest,
                    finalGeneration,
                    rejected.Failure,
                    trace);
            default:
                return Rejected(
                    request,
                    rung,
                    finalRequest,
                    finalGeneration,
                    "A ladder rung returned a non-terminal composition result.",
                    trace);
        }
    }

    static bool Matches(
        AssemblyReferenceResolutionRequest expected,
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation) =>
        ReferenceEquals(expected.BindingRequest, request)
        && ReferenceEquals(expected.Generation, generation);

    static bool MatchesExternal(
        AssemblyReferenceResolutionRequest expected,
        AssemblyReferenceExternalRouteOutcome outcome)
    {
        AssemblyReferenceResolutionContinuationReceipt? continuation =
            outcome.Continuation;
        if (ReferenceEquals(
                expected.BindingRequest,
                outcome.Request)
            && ReferenceEquals(
                expected.Generation,
                outcome.Generation))
        {
            return continuation is null;
        }

        return continuation is not null
            && ReferenceEquals(
                continuation.PredecessorRequest,
                expected.BindingRequest)
            && ReferenceEquals(
                continuation.PredecessorGeneration,
                expected.Generation)
            && ReferenceEquals(
                continuation.PredecessorPolicyVersion,
                expected.PolicyVersion)
            && ReferenceEquals(
                continuation.SuccessorRequest,
                outcome.Request)
            && ReferenceEquals(
                continuation.SuccessorGeneration,
                outcome.Generation);
    }

    static AssemblyBindingPolicyVersion FinalPolicyVersion(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceExternalRouteOutcome outcome) =>
        outcome.Continuation?.SuccessorPolicyVersion
            ?? request.PolicyVersion;

    static AssemblyReferenceResolutionOutcome.Incomplete
        DeadlineIncomplete(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
    {
        AssemblyReferenceResolutionWorkExhaustion exhaustion =
            request.Work.RecordDeadlineExhaustion();
        return Incomplete(
            request,
            rung,
            exhaustion,
            trace);
    }

    static AssemblyReferenceResolutionOutcome.AcquisitionRequired
        AcquisitionRequired(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceExternalRouteOutcome.AcquisitionRequired
            acquisition,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
        => new(
            request.BindingRequest,
            acquisition.Request,
            acquisition.Generation,
            acquisition.RouteSet,
            acquisition.SelectedRoute,
            acquisition.OwnerEvidence,
            trace.ToImmutable(),
            request.Work.Capture());

    static AssemblyReferenceResolutionOutcome.Unavailable Unavailable(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        object evidence,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
        => Unavailable(
            request,
            rung,
            request.BindingRequest,
            request.Generation,
            evidence,
            trace);

    static AssemblyReferenceResolutionOutcome.Unavailable Unavailable(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        AssemblyBindingRequest finalRequest,
        AssemblyReferenceResolutionGenerationReceipt finalGeneration,
        object evidence,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
        => new(
            request.BindingRequest,
            finalRequest,
            finalGeneration,
            rung,
            evidence,
            trace.ToImmutable(),
            request.Work.Capture());

    static AssemblyReferenceResolutionOutcome.Rejected Rejected(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        object evidence,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
        => Rejected(
            request,
            rung,
            request.BindingRequest,
            request.Generation,
            evidence,
            trace);

    static AssemblyReferenceResolutionOutcome.Rejected Rejected(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        AssemblyBindingRequest finalRequest,
        AssemblyReferenceResolutionGenerationReceipt finalGeneration,
        object evidence,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
        => new(
            request.BindingRequest,
            finalRequest,
            finalGeneration,
            rung,
            evidence,
            trace.ToImmutable(),
            request.Work.Capture());

    static AssemblyReferenceResolutionOutcome.Incomplete Incomplete(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        object evidence,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
        => Incomplete(
            request,
            rung,
            request.BindingRequest,
            request.Generation,
            evidence,
            trace);

    static AssemblyReferenceResolutionOutcome.Incomplete Incomplete(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceResolutionRung rung,
        AssemblyBindingRequest finalRequest,
        AssemblyReferenceResolutionGenerationReceipt finalGeneration,
        object evidence,
        ImmutableArray<AssemblyReferenceResolutionRungAttempt>.Builder trace)
        => new(
            request.BindingRequest,
            finalRequest,
            finalGeneration,
            rung,
            evidence,
            trace.ToImmutable(),
            request.Work.Capture());

}
