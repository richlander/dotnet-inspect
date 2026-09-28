using DotnetInspector.PackageQueries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;

namespace DotnetInspector.ResearchQueries;

public enum IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
{
    PredecessorUnavailable,
    PredecessorEvidenceMismatch,
    PlatformEvidenceMismatch,
    CandidateUnavailable,
    SuccessorWorkspaceMismatch,
    SuccessorDefinitionMismatch,
    SuccessorOccurrenceMismatch,
    SuccessorApplicabilityNotApplicable,
    LibraryAdmissionRejected,
    DeclarationAdmissionRejected,
    CandidateCompletionRejected,
    CandidateCutoverRejected,
}

/// <summary>
/// Fresh intrinsic CoreLib evidence produced while constructing a replacement
/// Workspace.
/// </summary>
public sealed class IntrinsicCoreLibraryWorkspaceSuccessorEvidence
{
    public IntrinsicCoreLibraryWorkspaceSuccessorEvidence(
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            context,
        MemberCallGraphFocalScopeReceipt focalScope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(focalScope);
        Context = context;
        FocalScope = focalScope;
    }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        Context
    { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }
}

/// <summary>
/// Resource-free correspondence between predecessor and successor occurrences
/// of one logical intrinsic CoreLib request.
/// </summary>
public sealed class IntrinsicCoreLibraryWorkspaceOccurrenceCorrespondence
{
    internal IntrinsicCoreLibraryWorkspaceOccurrenceCorrespondence(
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            predecessor,
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            successor)
    {
        Predecessor = predecessor;
        Successor = successor;
    }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        Predecessor
    { get; }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        Successor
    { get; }
}

/// <summary>
/// Resource-free proof that one complete Platform population was published in
/// a fresh Workspace generation for one continued intrinsic CoreLib request.
/// </summary>
public sealed class IntrinsicCoreLibraryWorkspaceContinuationReceipt
{
    internal IntrinsicCoreLibraryWorkspaceContinuationReceipt(
        IntrinsicCoreLibraryRouteApplicabilityReceipt predecessorApplicability,
        IntrinsicCoreLibraryRouteApplicabilityReceipt successorApplicability,
        IntrinsicCoreLibraryWorkspaceOccurrenceCorrespondence correspondence,
        WorkspaceDefinitionSnapshot predecessorDefinition,
        WorkspaceRealization successor,
        WorkspaceLibraryAdmissionReceipt platformAdmission,
        WorkspaceDeclarationContextReceipt platformDeclarations)
    {
        PredecessorApplicability = predecessorApplicability;
        SuccessorApplicability = successorApplicability;
        Correspondence = correspondence;
        PredecessorDefinition = predecessorDefinition;
        Successor = successor;
        PlatformAdmission = platformAdmission;
        PlatformDeclarations = platformDeclarations;
    }

    public IntrinsicCoreLibraryRouteApplicabilityReceipt
        PredecessorApplicability
    { get; }

    public IntrinsicCoreLibraryRouteApplicabilityReceipt
        SuccessorApplicability
    { get; }

    public IntrinsicCoreLibraryWorkspaceOccurrenceCorrespondence
        Correspondence
    { get; }

    public WorkspaceDefinitionSnapshot PredecessorDefinition { get; }

    public WorkspaceRealization Successor { get; }

    public WorkspaceLibraryAdmissionReceipt PlatformAdmission { get; }

    public WorkspaceDeclarationContextReceipt PlatformDeclarations { get; }
}

/// <summary>
/// Terminal cleanup evidence when continuation does not publish.
/// </summary>
public sealed class IntrinsicCoreLibraryWorkspaceContinuationCleanup
{
    internal IntrinsicCoreLibraryWorkspaceContinuationCleanup(
        PlatformPopulationAuthorityRetirementResult? platform,
        WorkspaceRealizationSettlement? candidate)
    {
        Platform = platform;
        Candidate = candidate;
    }

    public PlatformPopulationAuthorityRetirementResult? Platform { get; }

    public WorkspaceRealizationSettlement? Candidate { get; }

    public bool Succeeded =>
        Platform is null or { Succeeded: true }
        && Candidate is null or { Succeeded: true };
}

public abstract class IntrinsicCoreLibraryWorkspaceContinuationOutcome
{
    private IntrinsicCoreLibraryWorkspaceContinuationOutcome()
    {
    }

    public sealed class Published :
        IntrinsicCoreLibraryWorkspaceContinuationOutcome
    {
        internal Published(
            IntrinsicCoreLibraryWorkspaceContinuationReceipt receipt,
            WorkspaceRealizationRetirement? predecessorRetirement)
        {
            Receipt = receipt;
            PredecessorRetirement = predecessorRetirement;
        }

        public IntrinsicCoreLibraryWorkspaceContinuationReceipt Receipt
        { get; }

        public WorkspaceRealizationRetirement? PredecessorRetirement
        { get; }
    }

    public sealed class Rejected :
        IntrinsicCoreLibraryWorkspaceContinuationOutcome
    {
        internal Rejected(
            IntrinsicCoreLibraryWorkspaceContinuationRejectionReason reason,
            IntrinsicCoreLibraryWorkspaceContinuationCleanup cleanup,
            IntrinsicCoreLibraryRouteDecision? successorApplicability = null,
            WorkspaceLibraryAdmissionOutcome? libraryAdmission = null,
            WorkspacePlatformPopulationDeclarationAdmissionOutcome?
                declarationAdmission = null,
            WorkspaceRealizationCandidateRejection? candidateRejection = null)
        {
            Reason = reason;
            Cleanup = cleanup;
            SuccessorApplicability = successorApplicability;
            LibraryAdmission = libraryAdmission;
            DeclarationAdmission = declarationAdmission;
            CandidateRejection = candidateRejection;
        }

        public IntrinsicCoreLibraryWorkspaceContinuationRejectionReason Reason
        { get; }

        public IntrinsicCoreLibraryWorkspaceContinuationCleanup Cleanup
        { get; }

        public IntrinsicCoreLibraryRouteDecision? SuccessorApplicability
        { get; }

        public WorkspaceLibraryAdmissionOutcome? LibraryAdmission { get; }

        public WorkspacePlatformPopulationDeclarationAdmissionOutcome?
            DeclarationAdmission
        { get; }

        public WorkspaceRealizationCandidateRejection? CandidateRejection
        { get; }
    }

    public sealed class Cancelled :
        IntrinsicCoreLibraryWorkspaceContinuationOutcome
    {
        internal Cancelled(
            IntrinsicCoreLibraryWorkspaceContinuationCleanup cleanup) =>
            Cleanup = cleanup;

        public IntrinsicCoreLibraryWorkspaceContinuationCleanup Cleanup
        { get; }
    }

    public sealed class Failed :
        IntrinsicCoreLibraryWorkspaceContinuationOutcome
    {
        internal Failed(
            Exception failure,
            IntrinsicCoreLibraryWorkspaceContinuationCleanup cleanup)
        {
            Failure = failure;
            Cleanup = cleanup;
        }

        public Exception Failure { get; }

        public IntrinsicCoreLibraryWorkspaceContinuationCleanup Cleanup
        { get; }
    }
}

/// <summary>
/// Publishes an applicable intrinsic CoreLib Platform population through one
/// immutable Workspace replacement generation.
/// </summary>
public static class IntrinsicCoreLibraryWorkspaceContinuationOperation
{
    abstract class CandidatePreparation
    {
        private CandidatePreparation()
        {
        }

        internal sealed class Prepared : CandidatePreparation
        {
            internal Prepared(
                IntrinsicCoreLibraryWorkspaceSuccessorEvidence successor,
                IntrinsicCoreLibraryRouteDecision.Applicable
                    successorApplicability,
                WorkspaceLibraryAdmissionOutcome libraryAdmission,
                WorkspacePlatformPopulationDeclarationAdmissionOutcome
                    declarationAdmission)
            {
                Successor = successor;
                SuccessorApplicability = successorApplicability;
                LibraryAdmission = libraryAdmission;
                DeclarationAdmission = declarationAdmission;
            }

            internal IntrinsicCoreLibraryWorkspaceSuccessorEvidence Successor
            { get; }

            internal IntrinsicCoreLibraryRouteDecision.Applicable
                SuccessorApplicability
            { get; }

            internal WorkspaceLibraryAdmissionOutcome LibraryAdmission
            { get; }

            internal WorkspacePlatformPopulationDeclarationAdmissionOutcome
                DeclarationAdmission
            { get; }
        }

        internal sealed class Rejected : CandidatePreparation
        {
            internal Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason reason,
                bool platformSettled,
                IntrinsicCoreLibraryRouteDecision? successorApplicability =
                    null,
                WorkspaceLibraryAdmissionOutcome? libraryAdmission = null,
                WorkspacePlatformPopulationDeclarationAdmissionOutcome?
                    declarationAdmission = null)
            {
                Reason = reason;
                PlatformSettled = platformSettled;
                SuccessorApplicability = successorApplicability;
                LibraryAdmission = libraryAdmission;
                DeclarationAdmission = declarationAdmission;
            }

            internal
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                Reason
            { get; }

            internal bool PlatformSettled { get; }

            internal IntrinsicCoreLibraryRouteDecision?
                SuccessorApplicability
            { get; }

            internal WorkspaceLibraryAdmissionOutcome? LibraryAdmission
            { get; }

            internal
                WorkspacePlatformPopulationDeclarationAdmissionOutcome?
                DeclarationAdmission
            { get; }
        }
    }

    sealed class CandidatePreparationState
    {
        internal bool PlatformSettled { get; set; }
    }

    public static async ValueTask<
        IntrinsicCoreLibraryWorkspaceContinuationOutcome> ExecuteAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationOperationLease predecessor,
        IntrinsicCoreLibraryRouteDecision.Applicable applicability,
        PlatformPopulationArtifactMaterializationOutcome.Completed platform,
        Func<
            InspectionWorkspace,
            CancellationToken,
            ValueTask<IntrinsicCoreLibraryWorkspaceSuccessorEvidence>>
            constructSuccessor,
        PlatformTypeCatalogDerivationBounds catalogBounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(predecessor);
        ArgumentNullException.ThrowIfNull(applicability);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(constructSuccessor);
        ArgumentNullException.ThrowIfNull(catalogBounds);

        if (cancellationToken.IsCancellationRequested)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome
                .Cancelled(
                    await CleanupAsync(platform, null, cancel: true)
                        .ConfigureAwait(false));
        }

        if (await PredecessorRejectionAsync(
                coordinator,
                predecessor,
                applicability.Receipt)
                .ConfigureAwait(false)
            is { } predecessorRejection)
        {
            return await RejectBeforeCandidateAsync(
                predecessorRejection,
                platform).ConfigureAwait(false);
        }
        if (!PlatformMatches(applicability.Receipt, platform))
        {
            return await RejectBeforeCandidateAsync(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .PlatformEvidenceMismatch,
                platform).ConfigureAwait(false);
        }

        WorkspaceRealizationCandidateStartResult start;
        try
        {
            start = await coordinator.BeginCandidateAsync(
                    predecessor.Definition.Plan,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome
                .Cancelled(
                    await CleanupAsync(platform, null, cancel: true)
                        .ConfigureAwait(false));
        }
        catch (Exception failure)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome.Failed(
                failure,
                await CleanupAsync(platform, null, cancel: false)
                    .ConfigureAwait(false));
        }

        if (start
            is not WorkspaceRealizationCandidateStartResult.Prepared prepared)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome
                .Rejected(
                    IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                        .CandidateUnavailable,
                    await CleanupAsync(platform, null, cancel: false)
                        .ConfigureAwait(false));
        }

        WorkspaceRealizationCandidate candidate = prepared.Candidate;
        var preparationState = new CandidatePreparationState();
        CandidatePreparation preparation;
        try
        {
            preparation = await PrepareCandidateAsync(
                    candidate,
                    predecessor,
                    applicability.Receipt,
                    platform,
                    constructSuccessor,
                    catalogBounds,
                    preparationState,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome
                .Cancelled(
                    await CleanupAsync(
                        platform,
                        candidate,
                        cancel: true,
                        preparationState.PlatformSettled,
                        coordinator: coordinator)
                        .ConfigureAwait(false));
        }
        catch (Exception failure)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome.Failed(
                failure,
                await CleanupAsync(
                    platform,
                    candidate,
                    cancel: false,
                    preparationState.PlatformSettled,
                    coordinator: coordinator)
                    .ConfigureAwait(false));
        }

        if (preparation is CandidatePreparation.Rejected preparationRejected)
        {
            return await RejectAsync(
                coordinator,
                candidate,
                platform,
                preparationRejected.PlatformSettled,
                preparationRejected.Reason,
                preparationRejected.SuccessorApplicability,
                preparationRejected.LibraryAdmission,
                preparationRejected.DeclarationAdmission)
                .ConfigureAwait(false);
        }

        var preparedCandidate =
            (CandidatePreparation.Prepared)preparation;
        WorkspaceRealizationCandidateCompletionResult completion;
        try
        {
            completion = await coordinator.CompleteCandidateAsync(
                    candidate,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome
                .Cancelled(
                    await CleanupAsync(
                        platform,
                        candidate,
                        cancel: true,
                        platformSettled: true,
                        coordinator: coordinator)
                        .ConfigureAwait(false));
        }
        catch (Exception failure)
        {
            return new IntrinsicCoreLibraryWorkspaceContinuationOutcome.Failed(
                failure,
                await CleanupAsync(
                    platform,
                    candidate,
                    cancel: false,
                    platformSettled: true,
                    coordinator: coordinator)
                    .ConfigureAwait(false));
        }

        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            var rejected =
                (WorkspaceRealizationCandidateCompletionResult.Rejected)
                        completion;
            return await RejectAsync(
                coordinator,
                candidate,
                platform,
                platformSettled: true,
                reason:
                        IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                            .CandidateCompletionRejected,
                candidateRejection: rejected.Reason)
                .ConfigureAwait(false);
        }

        WorkspaceRealizationCutoverResult cutover =
            coordinator.CutOver(
                candidate,
                predecessor.Definition);
        if (cutover
            is not WorkspaceRealizationCutoverResult.Activated activated)
        {
            var rejected =
                (WorkspaceRealizationCutoverResult.Rejected)cutover;
            return await RejectAsync(
                coordinator,
                candidate,
                platform,
                platformSettled: true,
                reason:
                        IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                            .CandidateCutoverRejected,
                candidateRejection: rejected.Reason)
                .ConfigureAwait(false);
        }

        var successorApplicable =
            (IntrinsicCoreLibraryRouteDecision.Applicable)
                preparedCandidate.SuccessorApplicability;
        var accepted =
            (WorkspaceLibraryAdmissionOutcome.Accepted)
                preparedCandidate.LibraryAdmission;
        var admitted =
            (WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Admitted)preparedCandidate.DeclarationAdmission;
        var receipt = new IntrinsicCoreLibraryWorkspaceContinuationReceipt(
            applicability.Receipt,
            successorApplicable.Receipt,
            new(
                applicability.Receipt.Context,
                preparedCandidate.Successor.Context),
            predecessor.Definition,
            activated.Realization,
            accepted.Receipt,
            admitted.Context.Receipt);
        return new IntrinsicCoreLibraryWorkspaceContinuationOutcome
            .Published(receipt, activated.Predecessor);
    }

    static async ValueTask<CandidatePreparation> PrepareCandidateAsync(
        WorkspaceRealizationCandidate candidate,
        WorkspaceRealizationOperationLease predecessor,
        IntrinsicCoreLibraryRouteApplicabilityReceipt applicability,
        PlatformPopulationArtifactMaterializationOutcome.Completed platform,
        Func<
            InspectionWorkspace,
            CancellationToken,
            ValueTask<IntrinsicCoreLibraryWorkspaceSuccessorEvidence>>
            constructSuccessor,
        PlatformTypeCatalogDerivationBounds catalogBounds,
        CandidatePreparationState preparationState,
        CancellationToken cancellationToken)
    {
        using WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction();
        InspectionWorkspace workspace = construction.Workspace;
        IntrinsicCoreLibraryWorkspaceSuccessorEvidence successor =
            await constructSuccessor(workspace, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The successor constructor returned no evidence.");

        WorkspaceRegistrationReadResult registrationRead =
            workspace.GetRegistrationSnapshot();
        WorkspaceScopeReadResult scopeRead =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        if (registrationRead
                is not WorkspaceRegistrationReadResult.Available registrations
            || scopeRead
                is not WorkspaceScopeReadResult.Available scope
            || !SuccessorMatchesWorkspace(
                candidate,
                successor,
                registrations.Revision,
                scope.Snapshot))
        {
            return new CandidatePreparation.Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .SuccessorWorkspaceMismatch,
                platformSettled: false);
        }

        if (!ReferenceEquals(
                candidate.OriginPlan,
                predecessor.Definition.Plan)
            || !SameLogicalScope(
                predecessor.Scope,
                scope.Snapshot)
            || ReferenceEquals(
                successor.Context.ScopeRevision,
                applicability.Context.ScopeRevision)
            || ReferenceEquals(
                successor.FocalScope.RegistrationRevision,
                applicability.FocalScope.RegistrationRevision))
        {
            return new CandidatePreparation.Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .SuccessorDefinitionMismatch,
                platformSettled: false);
        }

        if (!SameLogicalRequest(applicability.Context, successor.Context))
        {
            return new CandidatePreparation.Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .SuccessorOccurrenceMismatch,
                platformSettled: false);
        }

        IntrinsicCoreLibraryPlatformApplicabilityPlanResult successorPlan =
            IntrinsicCoreLibraryPlatformApplicabilityQuery.Plan(
                successor.Context,
                successor.FocalScope,
                applicability.Target.Family);
        if (successorPlan
            is not IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                .Eligible eligible)
        {
            return new CandidatePreparation.Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .SuccessorApplicabilityNotApplicable,
                platformSettled: false);
        }

        IntrinsicCoreLibraryRouteDecision successorDecision =
            IntrinsicCoreLibraryPlatformApplicabilityQuery.Execute(
                eligible.Plan,
                platform,
                catalogBounds,
                cancellationToken);
        if (successorDecision
                is not IntrinsicCoreLibraryRouteDecision.Applicable
                    successorApplicable
            || successorApplicable.Receipt.Target != applicability.Target
            || !ReferenceEquals(
                successorApplicable.Receipt.Member,
                applicability.Member))
        {
            return new CandidatePreparation.Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .SuccessorApplicabilityNotApplicable,
                platformSettled: false,
                successorDecision);
        }

        WorkspaceLibraryAdmissionOutcome libraryAdmission =
            await workspace.AdmitLibraryBatchAsync(
                    registrations.Revision,
                    platform.Artifacts,
                    platform.Population.Owners)
                .ConfigureAwait(false);
        preparationState.PlatformSettled = true;
        if (libraryAdmission
            is not WorkspaceLibraryAdmissionOutcome.Accepted accepted)
        {
            return new CandidatePreparation.Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .LibraryAdmissionRejected,
                preparationState.PlatformSettled,
                successorDecision,
                libraryAdmission);
        }

        WorkspacePlatformPopulationDeclarationAdmissionOutcome
            declarationAdmission =
                WorkspacePlatformPopulationDeclarationAdmission.Admit(
                    workspace,
                    accepted.Receipt,
                    platform.Population.Value,
                    platform.Population.Receipt,
                    catalogBounds.MemberInspection);
        if (declarationAdmission
            is not WorkspacePlatformPopulationDeclarationAdmissionOutcome
                .Admitted)
        {
            return new CandidatePreparation.Rejected(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .DeclarationAdmissionRejected,
                preparationState.PlatformSettled,
                successorDecision,
                libraryAdmission,
                declarationAdmission);
        }

        return new CandidatePreparation.Prepared(
            successor,
            successorApplicable,
            libraryAdmission,
            declarationAdmission);
    }

    static async ValueTask<
        IntrinsicCoreLibraryWorkspaceContinuationRejectionReason?>
        PredecessorRejectionAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationOperationLease predecessor,
        IntrinsicCoreLibraryRouteApplicabilityReceipt applicability)
    {
        InspectionWorkspace workspace;
        try
        {
            workspace = predecessor.Workspace;
        }
        catch (ObjectDisposedException)
        {
            return IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                .PredecessorUnavailable;
        }

        WorkspaceRegistrationReadResult registrationRead =
            workspace.GetRegistrationSnapshot();
        WorkspaceScopeReadResult scopeRead =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        if (registrationRead
                is not WorkspaceRegistrationReadResult.Available registrations
            || scopeRead
                is not WorkspaceScopeReadResult.Available scope)
        {
            return IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                .PredecessorUnavailable;
        }

        WorkspaceRealization? current = coordinator.Current;
        return ReferenceEquals(
                current?.Identity,
                predecessor.Realization)
            && ReferenceEquals(
                workspace.Identity,
                predecessor.Realization)
            && ReferenceEquals(
                predecessor.Definition.Workspace,
                predecessor.Realization)
            && ReferenceEquals(
                predecessor.Scope.Revision.Workspace,
                predecessor.Realization)
            && ReferenceEquals(
                predecessor.Definition.Scope.Identity,
                predecessor.Scope.Revision.Identity)
            && ReferenceEquals(
                registrations.Revision.Identity,
                predecessor.Definition.Registrations.Identity)
            && ReferenceEquals(
                scope.Snapshot.Revision.Identity,
                predecessor.Definition.Scope.Identity)
            && ReferenceEquals(
                applicability.Context.ScopeRevision,
                predecessor.Scope.Revision.Identity)
            && ReferenceEquals(
                applicability.FocalScope.ScopeRevision,
                predecessor.Scope.Revision.Identity)
            && ReferenceEquals(
                applicability.FocalScope.RegistrationRevision,
                predecessor.Definition.Registrations.Identity)
                ? null
                : IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .PredecessorEvidenceMismatch;
    }

    static bool PlatformMatches(
        IntrinsicCoreLibraryRouteApplicabilityReceipt applicability,
        PlatformPopulationArtifactMaterializationOutcome.Completed platform) =>
        ReferenceEquals(
            applicability.PopulationReceipt,
            platform.Population.Receipt)
        && platform.Population.Value.Members.Any(
            member => ReferenceEquals(member, applicability.Member));

    static bool SuccessorMatchesWorkspace(
        WorkspaceRealizationCandidate candidate,
        IntrinsicCoreLibraryWorkspaceSuccessorEvidence successor,
        WorkspaceRegistrationRevision registrations,
        WorkspaceScopeSnapshot scope) =>
        ReferenceEquals(registrations.Workspace, candidate.Realization)
        && ReferenceEquals(scope.Revision.Workspace, candidate.Realization)
        && ReferenceEquals(
            successor.Context.ScopeRevision,
            scope.Revision.Identity)
        && ReferenceEquals(
            successor.FocalScope.ScopeRevision,
            scope.Revision.Identity)
        && ReferenceEquals(
            successor.FocalScope.RegistrationRevision,
            registrations.Identity);

    static bool SameLogicalRequest(
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            predecessor,
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            successor)
    {
        PackageIntrinsicCoreLibraryCallOccurrenceEvidence first =
            predecessor.Occurrence;
        PackageIntrinsicCoreLibraryCallOccurrenceEvidence second =
            successor.Occurrence;
        CallGraphCallSiteEvidence firstCall = first.CallSite;
        CallGraphCallSiteEvidence secondCall = second.CallSite;

        return firstCall.CallerModuleVersionId
                == secondCall.CallerModuleVersionId
            && firstCall.CallerMethodToken == secondCall.CallerMethodToken
            && firstCall.ILOffset == secondCall.ILOffset
            && firstCall.OperandToken == secondCall.OperandToken
            && firstCall.CallKind == secondCall.CallKind
            && firstCall.DispatchKind == secondCall.DispatchKind
            && firstCall.InLoop == secondCall.InLoop
            && firstCall.Target == secondCall.Target
            && first.Correspondence.Type == second.Correspondence.Type
            && first.Origin.Asset == second.Origin.Asset
            && StringComparer.OrdinalIgnoreCase.Equals(
                first.Origin.Package.PackageId,
                second.Origin.Package.PackageId)
            && StringComparer.OrdinalIgnoreCase.Equals(
                first.Origin.Package.PackageVersion,
                second.Origin.Package.PackageVersion)
            && string.Equals(
                first.Origin.Package.RequestedTargetFramework,
                second.Origin.Package.RequestedTargetFramework,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                first.Origin.Package.RequestedRuntimeIdentifier,
                second.Origin.Package.RequestedRuntimeIdentifier,
                StringComparison.OrdinalIgnoreCase)
            && !ReferenceEquals(first, second)
            && !ReferenceEquals(firstCall.Identity, secondCall.Identity)
            && !ReferenceEquals(first.Origin.Package, second.Origin.Package)
            && !ReferenceEquals(
                first.Origin.Registration,
                second.Origin.Registration)
            && !ReferenceEquals(first.Context.Group, second.Context.Group)
            && !ReferenceEquals(
                first.Context.BindingPolicyVersion,
                second.Context.BindingPolicyVersion);
    }

    static bool SameLogicalScope(
        WorkspaceScopeSnapshot predecessor,
        WorkspaceScopeSnapshot successor)
    {
        if (predecessor.Packages.Length != successor.Packages.Length)
            return false;

        for (int index = 0; index < predecessor.Packages.Length; index++)
        {
            WorkspacePackageDescriptor first =
                predecessor.Packages[index].Occurrence.Package;
            WorkspacePackageDescriptor second =
                successor.Packages[index].Occurrence.Package;
            if (first.Coordinate != second.Coordinate
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    first.PackageId,
                    second.PackageId)
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    first.PackageVersion,
                    second.PackageVersion)
                || !string.Equals(
                    first.TargetFramework,
                    second.TargetFramework,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    first.RequestedTargetFramework,
                    second.RequestedTargetFramework,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    first.SelectedTargetFramework,
                    second.SelectedTargetFramework,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    first.RuntimeIdentifier,
                    second.RuntimeIdentifier,
                    StringComparison.OrdinalIgnoreCase)
                || first.SelectionStatus != second.SelectionStatus
                || ReferenceEquals(
                    predecessor.Packages[index].Occurrence.Identity,
                    successor.Packages[index].Occurrence.Identity))
            {
                return false;
            }
        }

        return true;
    }

    static async ValueTask<
        IntrinsicCoreLibraryWorkspaceContinuationOutcome.Rejected>
        RejectBeforeCandidateAsync(
            IntrinsicCoreLibraryWorkspaceContinuationRejectionReason reason,
            PlatformPopulationArtifactMaterializationOutcome.Completed
                platform) =>
        new(
            reason,
            await CleanupAsync(platform, null, cancel: false)
                .ConfigureAwait(false));

    static async ValueTask<
        IntrinsicCoreLibraryWorkspaceContinuationOutcome.Rejected> RejectAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationCandidate candidate,
        PlatformPopulationArtifactMaterializationOutcome.Completed platform,
        bool platformSettled,
        IntrinsicCoreLibraryWorkspaceContinuationRejectionReason reason,
        IntrinsicCoreLibraryRouteDecision? successorApplicability = null,
        WorkspaceLibraryAdmissionOutcome? libraryAdmission = null,
        WorkspacePlatformPopulationDeclarationAdmissionOutcome?
            declarationAdmission = null,
        WorkspaceRealizationCandidateRejection? candidateRejection = null) =>
        new(
            reason,
            await CleanupAsync(
                    platform,
                    candidate,
                    cancel: false,
                    platformSettled,
                    coordinator)
                .ConfigureAwait(false),
            successorApplicability,
            libraryAdmission,
            declarationAdmission,
            candidateRejection);

    static async ValueTask<
        IntrinsicCoreLibraryWorkspaceContinuationCleanup> CleanupAsync(
        PlatformPopulationArtifactMaterializationOutcome.Completed platform,
        WorkspaceRealizationCandidate? candidate,
        bool cancel,
        bool platformSettled = false,
        WorkspaceReplacementCoordinator? coordinator = null)
    {
        PlatformPopulationAuthorityRetirementResult? platformRetirement = null;
        if (!platformSettled)
        {
            platformRetirement =
                await PlatformPopulationAuthorityRetirement
                    .RetireAsync(platform)
                    .ConfigureAwait(false);
        }

        WorkspaceRealizationSettlement? candidateSettlement = null;
        if (candidate is not null && coordinator is not null)
        {
            WorkspaceRealizationCandidateRetirementResult retirement =
                cancel
                    ? coordinator.CancelCandidate(candidate)
                    : coordinator.AbandonCandidate(candidate);
            if (retirement
                is WorkspaceRealizationCandidateRetirementResult.Retiring
                    retiring)
            {
                candidateSettlement =
                    await retiring.Retirement.Completion.ConfigureAwait(false);
            }
            else
            {
                candidateSettlement =
                    await candidate.Settlement.ConfigureAwait(false);
            }
        }

        return new(platformRetirement, candidateSettlement);
    }
}
