namespace DotnetInspector.Queries;

/// <summary>
/// One selected external supplier bound to the exact deferred route set that
/// authorized immutable Workspace replacement.
/// </summary>
public sealed class AssemblyReferenceWorkspaceContinuationDemand
{
    public AssemblyReferenceWorkspaceContinuationDemand(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceExternalRouteSet routeSet,
        AssemblyReferenceExternalRoute selectedRoute,
        object ownerEvidence)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(routeSet);
        ArgumentNullException.ThrowIfNull(selectedRoute);
        ArgumentNullException.ThrowIfNull(ownerEvidence);
        if (!ReferenceEquals(request.BindingRequest, routeSet.Request)
            || !ReferenceEquals(request.Generation, routeSet.Generation)
            || !ReferenceEquals(
                request.BindingRequest,
                routeSet.Advancement.Request)
            || !ReferenceEquals(
                request.Generation,
                routeSet.Advancement.Generation)
            || routeSet.Advancement
                is not AssemblyReferenceResolutionContextAdvancement
                    .NoNameOwner noNameOwner
            || !ReferenceEquals(
                noNameOwner.Selection.Version,
                request.PolicyVersion)
            || !ReferenceEquals(selectedRoute.Request, request.BindingRequest)
            || !ReferenceEquals(selectedRoute.Generation, request.Generation)
            || !routeSet.Routes.Any(
                route => ReferenceEquals(route, selectedRoute)))
        {
            throw new ArgumentException(
                "The selected supplier must retain the request's exact external route.",
                nameof(selectedRoute));
        }

        Request = request;
        RouteSet = routeSet;
        SelectedRoute = selectedRoute;
        OwnerEvidence = ownerEvidence;
    }

    public AssemblyReferenceResolutionRequest Request { get; }

    public AssemblyReferenceExternalRouteSet RouteSet { get; }

    public AssemblyReferenceExternalRoute SelectedRoute { get; }

    public object OwnerEvidence { get; }
}

public enum AssemblyReferenceWorkspaceContinuationRejectionReason
{
    PredecessorEvidenceMismatch,
    CandidateUnavailable,
    SuccessorEvidenceMismatch,
    CandidateCompletionRejected,
    CandidateCutoverRejected,
    SuccessorUnavailable,
    SuccessorResolutionNotContextOwned,
}

public sealed class AssemblyReferenceWorkspaceContinuationCleanup
{
    internal AssemblyReferenceWorkspaceContinuationCleanup(
        WorkspaceRealizationSettlement? candidate,
        AssemblyReferenceWorkspaceContinuationPublication? publication =
            null)
    {
        Candidate = candidate;
        Publication = publication;
    }

    public WorkspaceRealizationSettlement? Candidate { get; }

    public AssemblyReferenceWorkspaceContinuationPublication? Publication
    { get; }

    public bool Succeeded => Candidate is null or { Succeeded: true };
}

public sealed record AssemblyReferenceWorkspaceContinuationPublication(
    WorkspaceRealization Successor,
    WorkspaceRealizationRetirement? PredecessorRetirement);

public abstract class AssemblyReferenceWorkspaceContinuationOutcome
{
    private AssemblyReferenceWorkspaceContinuationOutcome()
    {
    }

    public sealed class Published :
        AssemblyReferenceWorkspaceContinuationOutcome,
        IDisposable
    {
        WorkspaceRealizationOperationLease? _successorOperation;

        internal Published(
            AssemblyReferenceExternalRouteOutcome.Completed external,
            AssemblyReferenceResolutionOutcome.Resolved successorResolution,
            WorkspaceRealizationOperationLease successorOperation,
            AssemblyReferenceResolutionWorkspaceReplacementReceipt
                replacement,
            WorkspaceRealizationRetirement? predecessorRetirement)
        {
            External = external;
            SuccessorResolution = successorResolution;
            _successorOperation = successorOperation;
            Replacement = replacement;
            PredecessorRetirement = predecessorRetirement;
        }

        public AssemblyReferenceExternalRouteOutcome.Completed External
        { get; }

        public AssemblyReferenceResolutionOutcome.Resolved SuccessorResolution
        { get; }

        public WorkspaceRealizationOperationLease SuccessorOperation =>
            Volatile.Read(ref _successorOperation)
            ?? throw new ObjectDisposedException(nameof(Published));

        public AssemblyReferenceResolutionWorkspaceReplacementReceipt
            Replacement
        { get; }

        public WorkspaceRealizationRetirement? PredecessorRetirement
        { get; }

        public void Dispose() =>
            Interlocked.Exchange(ref _successorOperation, null)?.Dispose();
    }

    public sealed class Rejected :
        AssemblyReferenceWorkspaceContinuationOutcome
    {
        internal Rejected(
            AssemblyReferenceWorkspaceContinuationRejectionReason reason,
            AssemblyReferenceWorkspaceContinuationCleanup cleanup,
            object? evidence = null,
            WorkspaceRealizationCandidateRejection? candidateRejection =
                null,
            AssemblyReferenceExternalRouteOutcome.Rejected? external = null)
        {
            Reason = reason;
            Cleanup = cleanup;
            Evidence = evidence;
            CandidateRejection = candidateRejection;
            External = external;
        }

        public AssemblyReferenceWorkspaceContinuationRejectionReason Reason
        { get; }

        public AssemblyReferenceWorkspaceContinuationCleanup Cleanup
        { get; }

        public object? Evidence { get; }

        public WorkspaceRealizationCandidateRejection? CandidateRejection
        { get; }

        public AssemblyReferenceExternalRouteOutcome.Rejected? External
        { get; }
    }

    public sealed class Cancelled :
        AssemblyReferenceWorkspaceContinuationOutcome
    {
        internal Cancelled(
            AssemblyReferenceWorkspaceContinuationCleanup cleanup) =>
            Cleanup = cleanup;

        public AssemblyReferenceWorkspaceContinuationCleanup Cleanup
        { get; }
    }

    public sealed class Incomplete :
        AssemblyReferenceWorkspaceContinuationOutcome
    {
        internal Incomplete(
            object evidence,
            AssemblyReferenceWorkspaceContinuationCleanup cleanup,
            AssemblyReferenceExternalRouteOutcome.Incomplete? external =
                null)
        {
            Evidence = evidence
                ?? throw new ArgumentNullException(nameof(evidence));
            Cleanup = cleanup;
            External = external;
        }

        public object Evidence { get; }

        public AssemblyReferenceWorkspaceContinuationCleanup Cleanup
        { get; }

        public AssemblyReferenceExternalRouteOutcome.Incomplete? External
        { get; }
    }

    public sealed class Failed :
        AssemblyReferenceWorkspaceContinuationOutcome
    {
        internal Failed(
            Exception failure,
            AssemblyReferenceWorkspaceContinuationCleanup cleanup,
            AssemblyReferenceExternalRouteOutcome.Rejected? external = null)
        {
            Failure = failure;
            Cleanup = cleanup;
            External = external;
        }

        public Exception Failure { get; }

        public AssemblyReferenceWorkspaceContinuationCleanup Cleanup
        { get; }

        public AssemblyReferenceExternalRouteOutcome.Rejected? External
        { get; }
    }
}

/// <summary>
/// Prepares, publishes, and validates one selected external supplier through
/// the Workspace owner's immutable-generation lifecycle.
/// </summary>
public static class AssemblyReferenceWorkspaceContinuationOperation
{
    public static async ValueTask<
        AssemblyReferenceWorkspaceContinuationOutcome> ExecuteAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationOperationLease predecessor,
        AssemblyReferenceWorkspaceContinuationDemand demand,
        Func<
            InspectionWorkspace,
            AssemblyReferenceWorkspaceContinuationDemand,
            CancellationToken,
            ValueTask<AssemblyReferenceResolutionRequest>>
            constructSuccessor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(constructSuccessor);

        if (cancellationToken.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                new(null));
        }
        if (!PredecessorMatches(coordinator, predecessor, demand))
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Rejected(
                AssemblyReferenceWorkspaceContinuationRejectionReason
                    .PredecessorEvidenceMismatch,
                new(null));
        }
        if (!demand.Request.Work.TryCharge(
                AssemblyReferenceResolutionWorkKind.WorkspaceReplacement,
                amount: 1,
                out AssemblyReferenceResolutionWorkExhaustion? exhaustion))
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                exhaustion!,
                new(null));
        }

        using AssemblyReferenceResolutionDeadlineCancellation
            deadlineCancellation =
            demand.Request.Work.CreateDeadlineCancellation();
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadlineCancellation.Token);
        CancellationToken operationToken = operationCancellation.Token;

        WorkspaceRealizationCandidateStartResult start;
        try
        {
            start = await coordinator.BeginCandidateAsync(
                    predecessor.Definition.Plan,
                    operationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                new(null));
        }
        catch (OperationCanceledException)
            when (deadlineCancellation.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                demand.Request.Work.RecordDeadlineExhaustion(),
                new(null));
        }
        catch (Exception failure)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Failed(
                failure,
                new(null));
        }
        if (start
            is not WorkspaceRealizationCandidateStartResult.Prepared prepared)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Rejected(
                AssemblyReferenceWorkspaceContinuationRejectionReason
                    .CandidateUnavailable,
                new(null));
        }

        WorkspaceRealizationCandidate candidate = prepared.Candidate;
        AssemblyReferenceResolutionRequest successorRequest;
        bool successorMatches;
        try
        {
            using WorkspaceRealizationConstructionLease construction =
                candidate.EnterConstruction();
            successorRequest = await constructSuccessor(
                    construction.Workspace,
                    demand,
                    operationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "The successor constructor returned no resolution request.");
            successorMatches = await SuccessorMatchesCandidateAsync(
                    candidate,
                    predecessor,
                    demand,
                    successorRequest,
                    construction.Workspace)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: true)
                    .ConfigureAwait(false));
        }
        catch (OperationCanceledException)
            when (deadlineCancellation.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                demand.Request.Work.RecordDeadlineExhaustion(),
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: true)
                    .ConfigureAwait(false));
        }
        catch (AssemblyReferenceResolutionWorkExhaustedException exhausted)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                exhausted.Exhaustion,
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: true)
                    .ConfigureAwait(false));
        }
        catch (Exception failure)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Failed(
                failure,
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: false)
                    .ConfigureAwait(false));
        }
        if (!successorMatches)
        {
            return await RejectCandidateAsync(
                    coordinator,
                    candidate,
                    AssemblyReferenceWorkspaceContinuationRejectionReason
                        .SuccessorEvidenceMismatch)
                .ConfigureAwait(false);
        }

        WorkspaceRealizationCandidateCompletionResult completion;
        try
        {
            completion = await coordinator.CompleteCandidateAsync(
                    candidate,
                    operationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: true)
                    .ConfigureAwait(false));
        }
        catch (OperationCanceledException)
            when (deadlineCancellation.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                demand.Request.Work.RecordDeadlineExhaustion(),
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: true)
                    .ConfigureAwait(false));
        }
        catch (Exception failure)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Failed(
                failure,
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: false)
                    .ConfigureAwait(false));
        }

        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready ready)
        {
            var rejected =
                (WorkspaceRealizationCandidateCompletionResult.Rejected)
                    completion;
            return await RejectCandidateAsync(
                    coordinator,
                    candidate,
                    AssemblyReferenceWorkspaceContinuationRejectionReason
                        .CandidateCompletionRejected,
                    rejected.Reason)
                .ConfigureAwait(false);
        }
        if (!SuccessorMatchesCompletion(
                candidate,
                predecessor,
                successorRequest,
                ready.Definition))
        {
            return await RejectCandidateAsync(
                    coordinator,
                    candidate,
                    AssemblyReferenceWorkspaceContinuationRejectionReason
                        .SuccessorEvidenceMismatch)
                .ConfigureAwait(false);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: true)
                    .ConfigureAwait(false));
        }
        if (deadlineCancellation.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                demand.Request.Work.RecordDeadlineExhaustion(),
                await RetireCandidateAsync(
                        coordinator,
                        candidate,
                        cancel: true)
                    .ConfigureAwait(false));
        }

        WorkspaceRealizationCutoverResult cutover =
            coordinator.CutOver(candidate, predecessor.Definition);
        if (cutover
            is not WorkspaceRealizationCutoverResult.Activated activated)
        {
            var rejected = (WorkspaceRealizationCutoverResult.Rejected)cutover;
            return await RejectCandidateAsync(
                    coordinator,
                    candidate,
                    AssemblyReferenceWorkspaceContinuationRejectionReason
                        .CandidateCutoverRejected,
                    rejected.Reason)
                .ConfigureAwait(false);
        }
        var publication =
            new AssemblyReferenceWorkspaceContinuationPublication(
                activated.Realization,
                activated.Predecessor);

        WorkspaceRealizationOperationAdmission successorAdmission;
        try
        {
            successorAdmission = await coordinator.EnterOperationAsync(
                    operationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                new(null, publication));
        }
        catch (OperationCanceledException)
            when (deadlineCancellation.IsCancellationRequested)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                demand.Request.Work.RecordDeadlineExhaustion(),
                new(null, publication));
        }
        catch (Exception failure)
        {
            return new AssemblyReferenceWorkspaceContinuationOutcome.Failed(
                failure,
                new(null, publication));
        }
        if (successorAdmission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted
            || !ReferenceEquals(
                admitted.Lease.Realization,
                activated.Realization.Identity)
            || !SuccessorMatchesOperation(
                successorRequest,
                admitted.Lease))
        {
            if (successorAdmission
                is WorkspaceRealizationOperationAdmission.Admitted invalid)
            {
                invalid.Lease.Dispose();
            }
            return new AssemblyReferenceWorkspaceContinuationOutcome.Rejected(
                AssemblyReferenceWorkspaceContinuationRejectionReason
                    .SuccessorUnavailable,
                new(null, publication),
                successorAdmission);
        }

        WorkspaceRealizationOperationLease successorOperation =
            admitted.Lease;
        AssemblyReferenceResolutionWorkspaceReplacementReceipt replacement;
        AssemblyReferenceResolutionContinuationReceipt continuation;
        try
        {
            replacement =
                new AssemblyReferenceResolutionWorkspaceReplacementReceipt(
                    coordinator,
                    predecessor,
                    successorOperation,
                    demand.Request.Generation,
                    successorRequest.Generation,
                    demand);
            continuation = new AssemblyReferenceResolutionContinuationReceipt(
                demand.Request.BindingRequest,
                demand.Request.Generation,
                demand.Request.PolicyVersion,
                successorRequest.BindingRequest,
                successorRequest.Generation,
                successorRequest.PolicyVersion,
                replacement);
        }
        catch (Exception failure)
        {
            successorOperation.Dispose();
            return new AssemblyReferenceWorkspaceContinuationOutcome.Failed(
                failure,
                new(null, publication));
        }

        AssemblyReferenceResolutionOutcome successorResolution;
        try
        {
            successorResolution =
                await AssemblyReferenceResolutionLadder.ExecuteAsync(
                        successorRequest,
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            successorOperation.Dispose();
            return new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                new(null, publication));
        }
        catch (Exception failure)
        {
            successorOperation.Dispose();
            var failedExternal =
                new AssemblyReferenceExternalRouteOutcome.Rejected(
                    successorRequest.BindingRequest,
                    successorRequest.Generation,
                    demand.RouteSet,
                    failure,
                    continuation);
            return new AssemblyReferenceWorkspaceContinuationOutcome.Failed(
                failure,
                new(null, publication),
                failedExternal);
        }

        if (successorResolution
            is AssemblyReferenceResolutionOutcome.Incomplete incomplete)
        {
            successorOperation.Dispose();
            var incompleteExternal =
                new AssemblyReferenceExternalRouteOutcome.Incomplete(
                    successorRequest.BindingRequest,
                    successorRequest.Generation,
                    demand.RouteSet,
                    incomplete,
                    continuation);
            return new AssemblyReferenceWorkspaceContinuationOutcome.Incomplete(
                incomplete,
                new(null, publication),
                incompleteExternal);
        }
        if (successorResolution
                is not AssemblyReferenceResolutionOutcome.Resolved resolved
            || resolved.Rung
                != AssemblyReferenceResolutionRung.ReferencingContext
            || !ReferenceEquals(
                resolved.FinalRequest,
                successorRequest.BindingRequest)
            || !ReferenceEquals(
                resolved.Generation,
                successorRequest.Generation))
        {
            successorOperation.Dispose();
            var rejectedExternal =
                new AssemblyReferenceExternalRouteOutcome.Rejected(
                    successorRequest.BindingRequest,
                    successorRequest.Generation,
                    demand.RouteSet,
                    successorResolution,
                    continuation);
            return new AssemblyReferenceWorkspaceContinuationOutcome.Rejected(
                AssemblyReferenceWorkspaceContinuationRejectionReason
                    .SuccessorResolutionNotContextOwned,
                new(null, publication),
                successorResolution,
                external: rejectedExternal);
        }

        var completedExternal =
            new AssemblyReferenceExternalRouteOutcome.Completed(
            successorRequest.BindingRequest,
            successorRequest.Generation,
            demand.RouteSet,
            resolved.Selection,
            demand.SelectedRoute,
            continuation);
        return new AssemblyReferenceWorkspaceContinuationOutcome.Published(
            completedExternal,
            resolved,
            successorOperation,
            replacement,
            activated.Predecessor);
    }

    static bool PredecessorMatches(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationOperationLease predecessor,
        AssemblyReferenceWorkspaceContinuationDemand demand)
    {
        AssemblyReferenceResolutionGenerationReceipt generation =
            demand.Request.Generation;
        WorkspaceRealization? current = coordinator.Current;
        return predecessor.IsOwnedBy(coordinator)
            && ReferenceEquals(current?.Identity, predecessor.Realization)
            && ReferenceEquals(
                predecessor.Definition.Workspace,
                predecessor.Realization)
            && ReferenceEquals(
                predecessor.Scope.Revision.Workspace,
                predecessor.Realization)
            && ReferenceEquals(generation.Workspace, predecessor.Realization)
            && ReferenceEquals(
                generation.ScopeRevision,
                predecessor.Scope.Revision.Identity)
            && ReferenceEquals(
                generation.RegistrationRevision,
                predecessor.Definition.Registrations.Identity)
            && ReferenceEquals(
                generation.PhysicalComposition,
                predecessor.Scope.PhysicalComposition)
            && ReferenceEquals(
                demand.Request.FocalScope.ScopeRevision,
                predecessor.Scope.Revision.Identity)
            && ReferenceEquals(
                demand.Request.FocalScope.RegistrationRevision,
                predecessor.Definition.Registrations.Identity);
    }

    static async ValueTask<bool> SuccessorMatchesCandidateAsync(
        WorkspaceRealizationCandidate candidate,
        WorkspaceRealizationOperationLease predecessor,
        AssemblyReferenceWorkspaceContinuationDemand demand,
        AssemblyReferenceResolutionRequest successor,
        InspectionWorkspace workspace)
    {
        WorkspaceRegistrationReadResult registrationRead =
            workspace.GetRegistrationSnapshot();
        WorkspaceScopeReadResult scopeRead =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        return registrationRead
                is WorkspaceRegistrationReadResult.Available registrations
            && scopeRead is WorkspaceScopeReadResult.Available scope
            && ReferenceEquals(
                candidate.OriginPlan,
                predecessor.Definition.Plan)
            && ReferenceEquals(
                registrations.Revision.Workspace,
                candidate.Realization)
            && ReferenceEquals(
                scope.Snapshot.Revision.Workspace,
                candidate.Realization)
            && LogicalRequestMatches(demand.Request, successor)
            && ReferenceEquals(successor.Work, demand.Request.Work)
            && ReferenceEquals(
                successor.Generation.Workspace,
                candidate.Realization)
            && ReferenceEquals(
                successor.Generation.ScopeRevision,
                scope.Snapshot.Revision.Identity)
            && ReferenceEquals(
                successor.Generation.RegistrationRevision,
                registrations.Revision.Identity)
            && ReferenceEquals(
                successor.Generation.PhysicalComposition,
                scope.Snapshot.PhysicalComposition)
            && ReferenceEquals(
                successor.FocalScope.ScopeRevision,
                scope.Snapshot.Revision.Identity)
            && ReferenceEquals(
                successor.FocalScope.RegistrationRevision,
                registrations.Revision.Identity)
            && WorkspaceLogicalScopeCorrespondence.Matches(
                predecessor.Scope.Revision,
                scope.Snapshot.Revision);
    }

    static bool SuccessorMatchesCompletion(
        WorkspaceRealizationCandidate candidate,
        WorkspaceRealizationOperationLease predecessor,
        AssemblyReferenceResolutionRequest successor,
        WorkspaceDefinitionSnapshot definition) =>
        ReferenceEquals(definition.Workspace, candidate.Realization)
        && ReferenceEquals(
            successor.Generation.ScopeRevision,
            definition.Scope.Identity)
        && ReferenceEquals(
            successor.Generation.RegistrationRevision,
            definition.Registrations.Identity)
        && ReferenceEquals(definition.Plan, predecessor.Definition.Plan)
        && WorkspaceLogicalScopeCorrespondence.Matches(
            predecessor.Scope.Revision,
            definition.Scope);

    static bool SuccessorMatchesOperation(
        AssemblyReferenceResolutionRequest successor,
        WorkspaceRealizationOperationLease operation) =>
        ReferenceEquals(successor.Generation.Workspace, operation.Realization)
        && ReferenceEquals(
            successor.Generation.ScopeRevision,
            operation.Scope.Revision.Identity)
        && ReferenceEquals(
            successor.Generation.RegistrationRevision,
            operation.Definition.Registrations.Identity)
        && ReferenceEquals(
            successor.Generation.PhysicalComposition,
            operation.Scope.PhysicalComposition);

    static bool LogicalRequestMatches(
        AssemblyReferenceResolutionRequest predecessor,
        AssemblyReferenceResolutionRequest successor) =>
        Equals(
            predecessor.BindingRequest.Target,
            successor.BindingRequest.Target)
        && predecessor.BindingRequest.Scope
            == successor.BindingRequest.Scope
        && !ReferenceEquals(
            predecessor.BindingRequest,
            successor.BindingRequest)
        && !ReferenceEquals(
            predecessor.BindingRequest.Origin,
            successor.BindingRequest.Origin)
        && !ReferenceEquals(
            predecessor.PolicyVersion,
            successor.PolicyVersion);

    static async ValueTask<
        AssemblyReferenceWorkspaceContinuationOutcome.Rejected>
        RejectCandidateAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationCandidate candidate,
        AssemblyReferenceWorkspaceContinuationRejectionReason reason,
        WorkspaceRealizationCandidateRejection? candidateRejection = null) =>
        new(
            reason,
            await RetireCandidateAsync(
                    coordinator,
                    candidate,
                    cancel: false)
                .ConfigureAwait(false),
            candidateRejection: candidateRejection);

    static async ValueTask<AssemblyReferenceWorkspaceContinuationCleanup>
        RetireCandidateAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationCandidate candidate,
        bool cancel)
    {
        WorkspaceRealizationCandidateRetirementResult retirement =
            cancel
                ? coordinator.CancelCandidate(candidate)
                : coordinator.AbandonCandidate(candidate);
        WorkspaceRealizationSettlement? settlement =
            retirement
                is WorkspaceRealizationCandidateRetirementResult.Retiring
                    retiring
                ? await retiring.Retirement.Completion.ConfigureAwait(false)
                : null;
        return new(settlement);
    }
}
