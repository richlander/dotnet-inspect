using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.ResearchQueries;

public abstract class
    PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
{
    private protected
        PackageDependencyMemberCallGraphPlatformRouteFormationOutcome()
    {
    }

    public sealed class Completed :
        PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
    {
        public Completed(PlatformAssemblyReferenceExternalRoute route)
        {
            ArgumentNullException.ThrowIfNull(route);
            Route = route;
        }

        public PlatformAssemblyReferenceExternalRoute Route { get; }
    }

    public sealed class Incomplete :
        PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
    {
        public Incomplete(object evidence)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public object Evidence { get; }
    }
}

public abstract class
    PackageDependencyMemberCallGraphExternalContinuationSource
{
    public abstract ValueTask<
        PackageDependencyMemberCallGraphPlatformRouteFormationOutcome>
        FormPlatformRouteAsync(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            MemberCallGraphFocalScopeReceipt focalScope,
            PackageAssemblyReferenceRouteEligibilityReceipt packageRoutes,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    public abstract ValueTask<ExternalAssemblyReferenceSupplierOutcome>
        ResolveAsync(
            PackageAssemblyReferenceExternalRoute packageRoute,
            PlatformAssemblyReferenceExternalRoute platformRoute,
            AssemblyBindingSelection referencingContextSelection,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    public abstract ValueTask<ImmutableArray<
        PackageAssemblyContextPlatformLibrary>>
        AdmitPlatformPopulationAsync(
            InspectionWorkspace workspace,
            WorkspaceRegistrationRevision registrations,
            PlatformFamilyTarget target,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    public abstract ValueTask<PlatformTargetDiscoveryOutcome>
        DiscoverIntrinsicCoreLibraryTargetAsync(
            PlatformFamily family,
            string targetFramework,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    public abstract ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        RealizeIntrinsicCoreLibraryPopulationAsync(
            PlatformFamilyTarget target,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    public abstract PlatformTypeCatalogDerivationBounds
        IntrinsicCoreLibraryCatalogBounds
    { get; }
}

public sealed class PackageDependencyMemberCallGraphContinuation :
    PackageDependencyMemberCallGraphInspectionContinuation
{
    readonly PackageDependencyMemberCallGraphExternalContinuationSource
        _source;
    readonly AssemblyReferenceResolutionWorkBudget _budget;

    public PackageDependencyMemberCallGraphContinuation(
        PackageDependencyMemberCallGraphExternalContinuationSource source,
        AssemblyReferenceResolutionWorkBudget budget)
    {
        _source = source
            ?? throw new ArgumentNullException(nameof(source));
        _budget = budget
            ?? throw new ArgumentNullException(nameof(budget));
    }

    public override async ValueTask<
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome>>
        ExecuteAsync(
            PackageDependencyMemberCallGraphInspectionPreparation
                preparation,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        cancellationToken.ThrowIfCancellationRequested();

        PackageDependencyMemberCallGraphInspectionRequest request =
            preparation.Request;
        await using var coordinator =
            new WorkspaceReplacementCoordinator();
        InitialRealizationOutcome initial =
            await CreateInitialRealizationAsync(
                    coordinator,
                    preparation,
                    cancellationToken)
                .ConfigureAwait(false);
        if (initial is InitialRealizationOutcome.Unavailable unavailable)
        {
            return PackageDependencyMemberCallGraphInspection
                .ProjectUnavailable(
                    unavailable.Reason,
                    unavailable.Detail);
        }
        var ready = (InitialRealizationOutcome.Ready)initial;
        PackageDependencyMemberCallGraphRequest lowerRequest =
            ready.Request;
        PackageDependencyMemberCallGraphPreparation graphPreparation =
            ready.Preparation;
        WorkspaceRealizationOperationLease operation =
            ready.Operation;

        var work = new AssemblyReferenceResolutionWorkLedger(_budget);
        var additionalAssets =
            new List<PackageAssemblyContextAdditionalPackageAsset>();
        var platformTargets =
            new List<PlatformFamilyTarget>();
        var successorBindings =
            graphPreparation.GraphBindings.ToList();
        var attempted =
            new HashSet<AssemblyReferenceOccurrenceKey>();
        bool intrinsicCoreLibraryAttempted = false;
        PackageDependencyIntrinsicCoreLibraryContinuationEvidence?
            intrinsicCoreLibraryContinuation = null;
        PackageDependencyMemberCallGraphGeneration? generation = null;
        var associations =
            new Dictionary<
                int,
                PackageAssemblyReferenceSupplierAssociation>();
        try
        {
            generation = await CreateGenerationAsync(
                    operation,
                    lowerRequest,
                    graphPreparation,
                    additionalAssets,
                    platformLibraries: [],
                    cancellationToken)
                .ConfigureAwait(false);
            while (generation.Outcome
                is PackageRoleMemberCallGraphOutcome.Available available)
            {
                if (!intrinsicCoreLibraryAttempted
                    && !available.IntrinsicCoreLibraryOccurrences.IsEmpty)
                {
                    intrinsicCoreLibraryAttempted = true;
                    IntrinsicCoreLibraryContinuationResult intrinsic =
                        await ContinueIntrinsicCoreLibraryAsync(
                                coordinator,
                                operation,
                                generation,
                                available.IntrinsicCoreLibraryOccurrences[0],
                                request,
                                lowerRequest,
                                graphPreparation,
                                additionalAssets,
                                platformTargets,
                                successorBindings,
                                work,
                                cancellationToken)
                            .ConfigureAwait(false);
                    intrinsicCoreLibraryContinuation = intrinsic.Evidence;
                    if (intrinsic
                        is IntrinsicCoreLibraryContinuationResult.Published
                            intrinsicPublished)
                    {
                        PackageDependencyMemberCallGraphGeneration
                            intrinsicPredecessor = generation;
                        generation = intrinsicPublished.Generation;
                        operation.Dispose();
                        operation = intrinsicPublished.Operation;
                        await intrinsicPredecessor.DisposeAsync()
                            .ConfigureAwait(false);
                        associations.Clear();
                        continue;
                    }
                    break;
                }

                PackageAssemblyReferenceCallOccurrenceEvidence? occurrence =
                    available.AssemblyReferenceOccurrences.FirstOrDefault(
                        candidate => !attempted.Contains(Key(candidate)));
                if (occurrence is null)
                    break;

                attempted.Add(Key(occurrence));
                PackageAssemblyReferenceSupplierAssociation association =
                    SupplierAssociation(
                        lowerRequest,
                        graphPreparation,
                        generation,
                        occurrence,
                        associations);
                AssemblyReferenceResolutionRequest resolution =
                    CreateResolutionRequest(
                        occurrence.Request,
                        occurrence.Context,
                        association,
                        work,
                        _source);
                AssemblyReferenceResolutionOutcome outcome =
                    await AssemblyReferenceResolutionLadder.ExecuteAsync(
                            resolution,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (outcome
                    is not AssemblyReferenceResolutionOutcome
                        .AcquisitionRequired acquisition)
                {
                    if (ProjectTerminal(outcome) is { } terminal)
                    {
                        return terminal;
                    }
                    continue;
                }

                AssemblyReferenceWorkspaceContinuationDemand demand =
                    ExternalAssemblyReferenceWorkspaceContinuationDemandAdapter
                        .Create(resolution, acquisition);
                ExternalAssemblyReferenceSupplierOutcome supplier =
                    (ExternalAssemblyReferenceSupplierOutcome)
                        acquisition.OwnerEvidence;
                PackageDependencyMemberCallGraphGeneration?
                    successorGeneration = null;
                var successorAssociations =
                    new Dictionary<
                        int,
                        PackageAssemblyReferenceSupplierAssociation>();
                ImmutableArray<
                    PackageAssemblyContextPlatformLibrary>
                    successorPlatformLibraries = [];
                async ValueTask ReleaseConstructedSuccessorAsync()
                {
                    PackageDependencyMemberCallGraphGeneration? constructed =
                        successorGeneration;
                    successorGeneration = null;
                    if (constructed is not null)
                    {
                        await constructed.DisposeAsync()
                            .ConfigureAwait(false);
                    }
                }
                AssemblyReferenceWorkspaceContinuationOutcome continuation =
                    await AssemblyReferenceWorkspaceContinuationOperation
                        .ExecuteAsync(
                            coordinator,
                            operation,
                            demand,
                            async (
                                workspace,
                                _,
                                token) =>
                            {
                                WorkspaceRegistrationRevision registrations =
                                    Registration(workspace);
                                if (ApplyPackageSelection(
                                        supplier,
                                        graphPreparation,
                                        additionalAssets)
                                    is { } selectedBinding
                                    && !successorBindings.Contains(
                                        selectedBinding))
                                {
                                    int reducedIndex =
                                        successorBindings.FindIndex(
                                            binding =>
                                                binding.Coordinate.PackageId
                                                    .Equals(
                                                        selectedBinding
                                                            .Coordinate
                                                            .PackageId,
                                                        StringComparison
                                                            .OrdinalIgnoreCase));
                                    if (reducedIndex >= 0)
                                    {
                                        successorBindings[reducedIndex] =
                                            selectedBinding;
                                    }
                                    else
                                    {
                                        successorBindings.Add(
                                            selectedBinding);
                                    }
                                }
                                ImmutableArray<PackageRootBinding>
                                    successorBindingSnapshot =
                                        [.. successorBindings];
                                WorkspaceScopeSnapshot scope =
                                    await AdmitPackagesAsync(
                                            workspace,
                                            registrations,
                                            successorBindingSnapshot,
                                            request.WorkspaceDeadline,
                                            token)
                                        .ConfigureAwait(false);
                                AddPlatformTarget(
                                    supplier,
                                    platformTargets);
                                var platformLibraries =
                                    ImmutableArray.CreateBuilder<
                                        PackageAssemblyContextPlatformLibrary>();
                                foreach (PlatformFamilyTarget target
                                    in platformTargets)
                                {
                                    platformLibraries.AddRange(
                                        await _source
                                            .AdmitPlatformPopulationAsync(
                                                workspace,
                                                registrations,
                                                target,
                                                work,
                                                token)
                                            .ConfigureAwait(false));
                                }
                                successorPlatformLibraries =
                                    platformLibraries.ToImmutable();
                                successorGeneration =
                                    await PackageDependencyMemberCallGraphOperation
                                        .ExecuteGenerationAsync(
                                            workspace,
                                            scope,
                                            registrations,
                                            successorBindingSnapshot,
                                            graphPreparation.Root,
                                            lowerRequest.Focus,
                                            lowerRequest.Graph,
                                            lowerRequest.SupplyChainBaseline,
                                            lowerRequest.RealizationOptions,
                                            additionalAssets,
                                            successorPlatformLibraries,
                                            token)
                                        .ConfigureAwait(false);
                                PackageAssemblyReferenceBindingEvidence
                                    successorEvidence =
                                        successorGeneration
                                            .CreateAssemblyReferenceContinuationEvidence(
                                                occurrence);
                                PackageAssemblyReferenceSupplierAssociation
                                    successorAssociation =
                                        SupplierAssociation(
                                            lowerRequest,
                                            graphPreparation,
                                            successorGeneration,
                                            successorEvidence,
                                            successorAssociations);
                                return CreateResolutionRequest(
                                    successorEvidence.Request,
                                    successorEvidence.Context,
                                    successorAssociation,
                                    work,
                                    _source);
                            },
                            ReleaseConstructedSuccessorAsync,
                            cancellationToken)
                        .ConfigureAwait(false);

                WorkspaceRealizationOperationLease? successorOperation;
                if (continuation
                    is AssemblyReferenceWorkspaceContinuationOutcome
                        .Published published)
                {
                    successorOperation = published.SuccessorOperation;
                }
                else
                {
                    Func<ValueTask>? disposeUnadoptedSuccessor =
                        successorGeneration is null
                            ? null
                            : successorGeneration.DisposeAsync;
                    successorOperation =
                        await AdoptPublishedSuccessorOperationAsync(
                                coordinator,
                                continuation,
                                disposeUnadoptedSuccessor)
                            .ConfigureAwait(false);
                }
                if (successorOperation is null)
                {
                    if (successorGeneration is not null)
                    {
                        await successorGeneration.DisposeAsync()
                            .ConfigureAwait(false);
                        successorGeneration = null;
                    }
                    InspectionEnvelope<
                        PackageDependencyMemberCallGraphInspectionOutcome>?
                        terminalWithoutPublication =
                            ProjectTerminal(
                                continuation,
                                cancellationToken);
                    return terminalWithoutPublication
                        ?? throw new InvalidOperationException(
                            "A non-published continuation did not retain a terminal outcome.");
                }

                PackageDependencyMemberCallGraphGeneration predecessor =
                    generation;
                generation = successorGeneration
                    ?? throw new InvalidOperationException(
                        "A published continuation did not retain its successor graph generation.");
                operation.Dispose();
                operation = successorOperation;
                await predecessor.DisposeAsync();
                associations = successorAssociations;
                InspectionEnvelope<
                    PackageDependencyMemberCallGraphInspectionOutcome>?
                    continuationTerminal =
                        ProjectTerminal(
                            continuation,
                            cancellationToken);
                if (continuationTerminal is not null)
                    return continuationTerminal;
            }

            PackageRoleCleanupReport cleanup =
                await generation.CloseAsync().ConfigureAwait(false);
            PackageDependencyMemberCallGraphOutcome lowerOutcome =
                PackageDependencyMemberCallGraphOperation
                    .CompletePreparedGeneration(
                        lowerRequest,
                        graphPreparation,
                        generation,
                        cleanup,
                        intrinsicCoreLibraryContinuation);
            generation = null;
            return PackageDependencyMemberCallGraphInspection
                .ProjectEnvelope(lowerOutcome);
        }
        finally
        {
            if (generation is not null)
                await generation.DisposeAsync();
            operation.Dispose();
        }
    }

    async ValueTask<IntrinsicCoreLibraryContinuationResult>
        ContinueIntrinsicCoreLibraryAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationOperationLease predecessorOperation,
        PackageDependencyMemberCallGraphGeneration predecessorGeneration,
        PackageIntrinsicCoreLibraryCallOccurrenceEvidence occurrence,
        PackageDependencyMemberCallGraphInspectionRequest request,
        PackageDependencyMemberCallGraphRequest lowerRequest,
        PackageDependencyMemberCallGraphPreparation graphPreparation,
        List<PackageAssemblyContextAdditionalPackageAsset> additionalAssets,
        List<PlatformFamilyTarget> platformTargets,
        List<PackageRootBinding> successorBindings,
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken)
    {
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            context = predecessorGeneration
                .CreateIntrinsicCoreLibraryContinuationEvidence(
                    occurrence);
        IntrinsicCoreLibraryPlatformApplicabilityPlanResult plan =
            IntrinsicCoreLibraryPlatformApplicabilityQuery.Plan(
                context,
                predecessorGeneration.FocalScope,
                PlatformFamily.DotNetRuntime);
        if (plan
            is IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                .OutsideOperationScope)
        {
            return new IntrinsicCoreLibraryContinuationResult.Terminal(
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .OutsideOperationScope,
                    null,
                    "The call-graph focal scope does not admit the Runtime Platform population."));
        }
        if (plan
            is IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                .Rejected planRejected)
        {
            return new IntrinsicCoreLibraryContinuationResult.Terminal(
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .ApplicabilityRejected,
                    null,
                    planRejected.Reason.ToString()));
        }

        PlatformTargetDiscoveryOutcome discovery =
            await _source.DiscoverIntrinsicCoreLibraryTargetAsync(
                    PlatformFamily.DotNetRuntime,
                    lowerRequest.Traversal.TraversalTargetPolicy
                        .TargetFramework,
                    work,
                    cancellationToken)
                .ConfigureAwait(false);
        if (discovery
            is not PlatformTargetDiscoveryOutcome.Selected selected)
        {
            object evidence =
                ((PlatformTargetDiscoveryOutcome.Incomplete)discovery)
                    .Evidence;
            return new IntrinsicCoreLibraryContinuationResult.Terminal(
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .TargetUnavailable,
                    null,
                    evidence.ToString()
                        ?? evidence.GetType().Name));
        }

        PlatformPopulationArtifactMaterializationOutcome platform =
            await _source.RealizeIntrinsicCoreLibraryPopulationAsync(
                    selected.Target,
                    work,
                    cancellationToken)
                .ConfigureAwait(false);
        IntrinsicCoreLibraryRouteDecision decision =
            IntrinsicCoreLibraryPlatformApplicabilityQuery.Execute(
                ((IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                    .Eligible)plan).Plan,
                platform,
                _source.IntrinsicCoreLibraryCatalogBounds,
                cancellationToken);
        if (decision
                is not IntrinsicCoreLibraryRouteDecision.Applicable
                    applicable
            || platform
                is not PlatformPopulationArtifactMaterializationOutcome
                    .Completed completed)
        {
            if (platform
                is PlatformPopulationArtifactMaterializationOutcome
                    .Completed retained)
            {
                _ = await PlatformPopulationAuthorityRetirement
                    .RetireAsync(retained)
                    .ConfigureAwait(false);
            }
            return new IntrinsicCoreLibraryContinuationResult.Terminal(
                IntrinsicContinuationEvidence(
                    decision,
                    selected.Target));
        }
        PackageDependencyMemberCallGraphGeneration? provisional = null;
        WorkspaceScopeSnapshot? successorScope = null;
        WorkspaceRegistrationRevision? successorRegistrations = null;
        ImmutableArray<PackageAssemblyContextPlatformLibrary>
            retainedPlatformLibraries = [];
        IntrinsicCoreLibraryWorkspaceContinuationOutcome continuation =
            await IntrinsicCoreLibraryWorkspaceContinuationOperation
                .ExecuteAsync(
                    coordinator,
                    predecessorOperation,
                    applicable,
                    completed,
                    async (workspace, token) =>
                    {
                        WorkspaceRegistrationRevision registrations =
                            Registration(workspace);
                        ImmutableArray<PackageRootBinding> bindings =
                            [.. successorBindings];
                        WorkspaceScopeSnapshot scope =
                            await AdmitPackagesAsync(
                                    workspace,
                                    registrations,
                                    bindings,
                                    request.WorkspaceDeadline,
                                    token)
                                .ConfigureAwait(false);
                        var existingPlatforms =
                            ImmutableArray.CreateBuilder<
                                PackageAssemblyContextPlatformLibrary>();
                        foreach (PlatformFamilyTarget target
                            in platformTargets.Where(
                                target => target != selected.Target))
                        {
                            existingPlatforms.AddRange(
                                await _source
                                    .AdmitPlatformPopulationAsync(
                                        workspace,
                                        registrations,
                                        target,
                                        work,
                                        token)
                                    .ConfigureAwait(false));
                        }
                        retainedPlatformLibraries =
                            existingPlatforms.ToImmutable();
                        successorScope = scope;
                        successorRegistrations = registrations;
                        provisional =
                            await PackageDependencyMemberCallGraphOperation
                                .ExecuteGenerationAsync(
                                    workspace,
                                    scope,
                                    registrations,
                                    bindings,
                                    graphPreparation.Root,
                                    lowerRequest.Focus,
                                    lowerRequest.Graph,
                                    lowerRequest.SupplyChainBaseline,
                                    lowerRequest.RealizationOptions,
                                    additionalAssets,
                                    retainedPlatformLibraries,
                                    token)
                                .ConfigureAwait(false);
                        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
                            successor = provisional
                                .CreateIntrinsicCoreLibraryContinuationEvidence(
                                    context);
                        return new(
                            successor,
                            provisional.FocalScope);
                    },
                    _source.IntrinsicCoreLibraryCatalogBounds,
                    cancellationToken)
                .ConfigureAwait(false);
        if (continuation
            is not IntrinsicCoreLibraryWorkspaceContinuationOutcome
                .Published published)
        {
            if (provisional is not null)
                await provisional.DisposeAsync().ConfigureAwait(false);
            if (continuation
                is IntrinsicCoreLibraryWorkspaceContinuationOutcome.Cancelled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new OperationCanceledException(cancellationToken);
            }
            return new IntrinsicCoreLibraryContinuationResult.Terminal(
                WorkspaceContinuationEvidence(
                    continuation,
                    selected.Target));
        }

        WorkspaceRealizationOperationAdmission admission =
            await coordinator.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        if (admission
                is not WorkspaceRealizationOperationAdmission.Admitted
                    admitted
            || !ReferenceEquals(
                admitted.Lease.Realization,
                published.Receipt.Successor.Identity))
        {
            if (admission
                is WorkspaceRealizationOperationAdmission.Admitted invalid)
            {
                invalid.Lease.Dispose();
            }
            if (provisional is not null)
                await provisional.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                "The published intrinsic CoreLib successor could not issue its retained graph operation.");
        }

        WorkspaceRealizationOperationLease successorOperation =
            admitted.Lease;
        try
        {
            if (provisional is not null)
            {
                await provisional.DisposeAsync().ConfigureAwait(false);
                provisional = null;
            }
            var platformLibraries =
                retainedPlatformLibraries.ToBuilder();
            platformLibraries.AddRange(
                published.Receipt.PlatformAdmission.Occurrences.Select(
                    library =>
                        new PackageAssemblyContextPlatformLibrary(
                            selected.Target,
                            library,
                            AssemblyReferenceIdentity.EquivalentComparer
                                .Equals(
                                    library.Library.ApiAssembly
                                        .AssemblyIdentity!.Identity,
                                    applicable.Receipt.Member.PlatformLibrary
                                        .Library.ApiAssembly.AssemblyIdentity!
                                        .Identity))));
            PlatformFamilyTarget? previous = platformTargets.SingleOrDefault(
                target => target.Family == selected.Target.Family);
            if (previous is not null)
                platformTargets.Remove(previous);
            platformTargets.Add(selected.Target);

            PackageDependencyMemberCallGraphGeneration generation =
                await PackageDependencyMemberCallGraphOperation
                    .ExecuteGenerationAsync(
                        successorOperation.Workspace,
                        successorScope
                            ?? throw new InvalidOperationException(
                                "The intrinsic CoreLib successor did not retain its Workspace Scope."),
                        successorRegistrations
                            ?? throw new InvalidOperationException(
                                "The intrinsic CoreLib successor did not retain its registrations."),
                        [.. successorBindings],
                        graphPreparation.Root,
                        lowerRequest.Focus,
                        lowerRequest.Graph,
                        lowerRequest.SupplyChainBaseline,
                        lowerRequest.RealizationOptions,
                        additionalAssets,
                        platformLibraries.ToImmutable(),
                        cancellationToken)
                    .ConfigureAwait(false);
            return new IntrinsicCoreLibraryContinuationResult.Published(
                generation,
                successorOperation,
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .Published,
                    selected.Target,
                    null));
        }
        catch
        {
            successorOperation.Dispose();
            throw;
        }
    }

    static PackageDependencyIntrinsicCoreLibraryContinuationEvidence
        IntrinsicContinuationEvidence(
        IntrinsicCoreLibraryRouteDecision decision,
        PlatformFamilyTarget target) =>
        decision switch
        {
            IntrinsicCoreLibraryRouteDecision.OutsideOperationScope =>
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .OutsideOperationScope,
                    target,
                    null),
            IntrinsicCoreLibraryRouteDecision.Unavailable unavailable =>
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .ApplicabilityUnavailable,
                    target,
                    unavailable.Reason.ToString()),
            IntrinsicCoreLibraryRouteDecision.Rejected rejected =>
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .ApplicabilityRejected,
                    target,
                    $"{rejected.Reason}; "
                    + $"populationTarget={rejected.PopulationReceipt?.HouseReceipt.TargetSettlement.SettledTarget}; "
                    + $"catalogTarget={rejected.Catalog?.Target}; "
                    + $"catalogDerivation={rejected.CatalogDerivation?.Kind}"),
            IntrinsicCoreLibraryRouteDecision.Incomplete incomplete =>
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .ApplicabilityIncomplete,
                    target,
                    incomplete.Evidence.GetType().Name),
            _ => throw new InvalidOperationException(
                "Unknown terminal intrinsic CoreLib applicability decision."),
        };

    static PackageDependencyIntrinsicCoreLibraryContinuationEvidence
        WorkspaceContinuationEvidence(
        IntrinsicCoreLibraryWorkspaceContinuationOutcome continuation,
        PlatformFamilyTarget target) =>
        continuation switch
        {
            IntrinsicCoreLibraryWorkspaceContinuationOutcome.Rejected
                rejected =>
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .WorkspaceRejected,
                    target,
                    rejected.Reason.ToString()),
            IntrinsicCoreLibraryWorkspaceContinuationOutcome.Failed failed =>
                new(
                    PackageDependencyIntrinsicCoreLibraryContinuationKind
                        .WorkspaceFailed,
                    target,
                    failed.Failure.Message),
            _ => throw new InvalidOperationException(
                "Unknown terminal intrinsic CoreLib Workspace continuation outcome."),
        };

    static AssemblyReferenceResolutionRequest
        CreateResolutionRequest(
        AssemblyBindingRequest request,
        AssemblyBindingSelectionSnapshot context,
        PackageAssemblyReferenceSupplierAssociation association,
        AssemblyReferenceResolutionWorkLedger work,
        PackageDependencyMemberCallGraphExternalContinuationSource source)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(source);
        PackageAssemblyReferenceRouteProjectionOutcome projection =
            association.RouteProjection;
        if (projection
            is PackageAssemblyReferenceRouteProjectionOutcome
                .Incomplete incomplete)
        {
            return CreateIncompleteResolutionRequest(
                request,
                context,
                projection.Generation,
                projection.FocalScope,
                incomplete,
                work,
                Math.Max(1, incomplete.Routes.Length));
        }
        var completed =
            (PackageAssemblyReferenceRouteProjectionOutcome.Completed)
                projection;

        var packageRoute = new PackageAssemblyReferenceExternalRoute(
            request,
            association,
            completed.Receipt);
        var plan = new AssemblyReferenceResolutionRoutePlan(
            request,
            projection.Generation,
            projection.FocalScope,
            context.Version,
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<
                    AssemblyReferenceResolutionContextOutcome>(
                    new AssemblyReferenceResolutionContextOutcome.Selected(
                        request,
                        projection.Generation,
                        context));
            },
            async (advancement, ledger, token) =>
            {
                token.ThrowIfCancellationRequested();
                PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
                    platformFormation =
                    await source.FormPlatformRouteAsync(
                            request,
                            projection.Generation,
                            projection.FocalScope,
                            completed.Receipt,
                            ledger,
                            token)
                        .ConfigureAwait(false);
                if (platformFormation
                    is PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
                        .Incomplete platformIncomplete)
                {
                    return new AssemblyReferenceExternalRouteSetFormationOutcome
                        .Incomplete(platformIncomplete.Evidence);
                }
                PlatformAssemblyReferenceExternalRoute platformRoute =
                    ((PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
                        .Completed)platformFormation).Route;
                AssemblyReferenceExternalRouteSet? routeSet = null;
                routeSet = new AssemblyReferenceExternalRouteSet(
                    request,
                    projection.Generation,
                    advancement,
                    [packageRoute, platformRoute],
                    async (ledger, executeToken) =>
                    {
                        ExternalAssemblyReferenceSupplierOutcome supplier =
                            await source.ResolveAsync(
                                    packageRoute,
                                    platformRoute,
                                    context.Selection,
                                    ledger,
                                    executeToken)
                                .ConfigureAwait(false);
                        return SupplierOutcome(
                            request,
                            context.Version,
                            routeSet!,
                            supplier);
                    });
                return new AssemblyReferenceExternalRouteSetFormationOutcome
                    .Completed(routeSet);
            });
        return new(plan, work);
    }

    static AssemblyReferenceResolutionRequest
        CreateIncompleteResolutionRequest(
        AssemblyBindingRequest request,
        AssemblyBindingSelectionSnapshot context,
        AssemblyReferenceResolutionGenerationReceipt generation,
        MemberCallGraphFocalScopeReceipt focalScope,
        object incomplete,
        AssemblyReferenceResolutionWorkLedger work,
        int packageRouteOccurrences)
    {
        var plan = new AssemblyReferenceResolutionRoutePlan(
            request,
            generation,
            focalScope,
            context.Version,
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<
                    AssemblyReferenceResolutionContextOutcome>(
                    new AssemblyReferenceResolutionContextOutcome.Selected(
                        request,
                        generation,
                        context));
            },
            (_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (packageRouteOccurrences > 0)
                {
                    work.Charge(
                        AssemblyReferenceResolutionWorkKind
                            .PackageRouteOccurrence,
                        packageRouteOccurrences);
                }
                return ValueTask.FromResult<
                    AssemblyReferenceExternalRouteSetFormationOutcome>(
                    new AssemblyReferenceExternalRouteSetFormationOutcome
                        .Incomplete(incomplete));
            });
        return new(plan, work);
    }

    static PackageAssemblyReferenceSupplierAssociation
        SupplierAssociation(
        PackageDependencyMemberCallGraphRequest lowerRequest,
        PackageDependencyMemberCallGraphPreparation preparation,
        PackageDependencyMemberCallGraphGeneration generation,
        PackageAssemblyReferenceBindingEvidence occurrence,
        IDictionary<int, PackageAssemblyReferenceSupplierAssociation>
            associations)
    {
        int projectionIndex =
            preparation.ResolveReferencingProjection(
                lowerRequest,
                occurrence.Origin.Package);
        if (associations.TryGetValue(
                projectionIndex,
                out PackageAssemblyReferenceSupplierAssociation? cached))
        {
            return cached;
        }

        PackageDependencyTraversalOutcome traversal =
            lowerRequest.Traversal;
        int rootIndex = lowerRequest.Focus.RootOccurrenceIndex;
        ImmutableDictionary<int, int> edges =
            traversal.ReachableEdgesFromProjection(
                rootIndex,
                projectionIndex);
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            executions =
            [
                .. lowerRequest.EdgeExecutions.Where(execution =>
                    execution.Subject.RootOccurrenceIndex == rootIndex
                    && edges.ContainsKey(execution.Subject.EdgeIndex)),
            ];
        PackageAssemblyReferenceSupplierAssociation created =
            PackageAssemblyReferenceSupplierAssociation.Create(
                new(
                    generation.Generation,
                    generation.FocalScope,
                    traversal,
                    rootIndex,
                    executions,
                    originProjectionIndex: projectionIndex));
        associations.Add(projectionIndex, created);
        return created;
    }

    static AssemblyReferenceExternalRouteOutcome SupplierOutcome(
        AssemblyBindingRequest request,
        AssemblyBindingPolicyVersion version,
        AssemblyReferenceExternalRouteSet routes,
        ExternalAssemblyReferenceSupplierOutcome supplier) =>
        supplier switch
        {
            ExternalAssemblyReferenceSupplierOutcome.PackageOwned package =>
                new AssemblyReferenceExternalRouteOutcome
                    .AcquisitionRequired(
                        request,
                        routes.Generation,
                        routes,
                        package.Route,
                        supplier),
            ExternalAssemblyReferenceSupplierOutcome.PlatformOwned platform =>
                new AssemblyReferenceExternalRouteOutcome
                    .AcquisitionRequired(
                        request,
                        routes.Generation,
                        routes,
                        platform.Platform.Route,
                        supplier),
            ExternalAssemblyReferenceSupplierOutcome.NameOwnedNoMatch =>
                CompletedMissing(
                    request,
                    version,
                    routes,
                    AssemblyBindingSelection.NameOwnedButNoMatch()),
            ExternalAssemblyReferenceSupplierOutcome.NoSupplier =>
                CompletedMissing(
                    request,
                    version,
                    routes,
                    AssemblyBindingSelection.NameNotOwned()),
            ExternalAssemblyReferenceSupplierOutcome.Unavailable =>
                new AssemblyReferenceExternalRouteOutcome.Unavailable(
                    request,
                    routes.Generation,
                    routes,
                    supplier),
            ExternalAssemblyReferenceSupplierOutcome.Incomplete =>
                new AssemblyReferenceExternalRouteOutcome.Incomplete(
                    request,
                    routes.Generation,
                    routes,
                    supplier),
            _ => new AssemblyReferenceExternalRouteOutcome.Rejected(
                request,
                routes.Generation,
                routes,
                supplier),
        };

    static AssemblyReferenceExternalRouteOutcome.Completed CompletedMissing(
        AssemblyBindingRequest request,
        AssemblyBindingPolicyVersion version,
        AssemblyReferenceExternalRouteSet routes,
        AssemblyBindingSelection selection) =>
        new(
            request,
            routes.Generation,
            routes,
            new(
                version,
                selection));

    static InspectionEnvelope<
        PackageDependencyMemberCallGraphInspectionOutcome>?
        ProjectTerminal(
        AssemblyReferenceResolutionOutcome outcome) =>
        outcome switch
        {
            AssemblyReferenceResolutionOutcome.Unavailable unavailable =>
                PackageDependencyMemberCallGraphInspection
                    .ProjectUnavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .AssemblyReferenceResolutionUnavailable,
                        DescribeEvidence(
                            "Assembly-reference resolution was unavailable",
                            unavailable.Evidence)),
            AssemblyReferenceResolutionOutcome.Rejected rejected =>
                PackageDependencyMemberCallGraphInspection
                    .ProjectUnavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .AssemblyReferenceResolutionRejected,
                        DescribeEvidence(
                            "Assembly-reference resolution was rejected",
                            rejected.Evidence)),
            AssemblyReferenceResolutionOutcome.Incomplete incomplete =>
                PackageDependencyMemberCallGraphInspection
                    .ProjectUnavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .AssemblyReferenceResolutionIncomplete,
                        DescribeEvidence(
                            "Assembly-reference resolution was incomplete",
                            incomplete.Evidence)),
            _ => null,
        };

    static InspectionEnvelope<
        PackageDependencyMemberCallGraphInspectionOutcome>?
        ProjectTerminal(
        AssemblyReferenceWorkspaceContinuationOutcome outcome,
        CancellationToken cancellationToken) =>
        outcome switch
        {
            AssemblyReferenceWorkspaceContinuationOutcome.Published =>
                null,
            AssemblyReferenceWorkspaceContinuationOutcome.Rejected
                rejected =>
                PackageDependencyMemberCallGraphInspection
                    .ProjectUnavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .AssemblyReferenceContinuationRejected,
                        $"Assembly-reference Workspace continuation was rejected ({rejected.Reason})."),
            AssemblyReferenceWorkspaceContinuationOutcome.Incomplete
                incomplete =>
                PackageDependencyMemberCallGraphInspection
                    .ProjectUnavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .AssemblyReferenceContinuationIncomplete,
                        DescribeEvidence(
                            "Assembly-reference Workspace continuation was incomplete",
                            incomplete.Evidence)),
            AssemblyReferenceWorkspaceContinuationOutcome.Failed failed =>
                PackageDependencyMemberCallGraphInspection
                    .ProjectUnavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .AssemblyReferenceContinuationFailed,
                        $"Assembly-reference Workspace continuation failed ({failed.Failure.Message})."),
            AssemblyReferenceWorkspaceContinuationOutcome.Cancelled =>
                ThrowCancellation(cancellationToken),
            _ => throw new InvalidOperationException(
                "Unknown AssemblyRef Workspace continuation outcome."),
        };

    static InspectionEnvelope<
        PackageDependencyMemberCallGraphInspectionOutcome>?
        ThrowCancellation(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(
            "Assembly-reference Workspace continuation was cancelled.",
            cancellationToken);
    }

    static string DescribeEvidence(
        string prefix,
        object evidence) =>
        evidence switch
        {
            AssemblyReferenceResolutionWorkExhaustion exhaustion =>
                exhaustion.Kind
                    == AssemblyReferenceResolutionWorkKind.Deadline
                    ? $"{prefix}: the shared deadline {exhaustion.Deadline:O} was reached."
                    : $"{prefix}: {exhaustion.Kind} requested {exhaustion.Requested} after consuming {exhaustion.Consumed} of {exhaustion.ConfiguredMaximum}.",
            PackageAssemblyReferenceRouteProjectionOutcome.Incomplete
                incomplete =>
                $"{prefix}: {incomplete.Description}",
            _ => $"{prefix} ({evidence.GetType().Name}).",
        };

    static async ValueTask<InitialRealizationOutcome>
        CreateInitialRealizationAsync(
        WorkspaceReplacementCoordinator coordinator,
        PackageDependencyMemberCallGraphInspectionPreparation preparation,
        CancellationToken cancellationToken)
    {
        WorkspaceRealizationCandidateStartResult start =
            await coordinator.BeginCandidateAsync(
                    preparation.Request.WorkspacePlan,
                    cancellationToken)
                .ConfigureAwait(false);
        if (start
            is not WorkspaceRealizationCandidateStartResult.Prepared
                preparedCandidate)
        {
            return new InitialRealizationOutcome.Unavailable(
                PackageDependencyMemberCallGraphInspectionUnavailableReason
                    .RootWorkspaceNotCommitted,
                $"The initial Workspace candidate was unavailable ({start.GetType().Name}).");
        }
        WorkspaceRealizationCandidate candidate =
            preparedCandidate.Candidate;
        PackageDependencyMemberCallGraphRequest? lowerRequest = null;
        PackageDependencyMemberCallGraphPreparation? graphPreparation =
            null;
        InitialRealizationOutcome.Unavailable? constructionFailure = null;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            InspectionWorkspace workspace = construction.Workspace;
            WorkspaceRegistrationReadResult registrationRead =
                workspace.GetRegistrationSnapshot();
            if (registrationRead
                is not WorkspaceRegistrationReadResult.Available
                    registration)
            {
                var registrationUnavailable =
                    (WorkspaceRegistrationReadResult.Unavailable)
                        registrationRead;
                constructionFailure =
                    new InitialRealizationOutcome.Unavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .RootWorkspaceNotCommitted,
                        $"The operation Workspace registrations were unavailable ({registrationUnavailable.RuntimeFailure}).");
                goto ConstructionComplete;
            }
            WorkspaceRegistrationRevision registrations =
                registration.Revision;
            WorkspaceScopeReadResult initialRead =
                await workspace.GetScopeSnapshotAsync()
                    .ConfigureAwait(false);
            if (initialRead
                is not WorkspaceScopeReadResult.Available initial)
            {
                var scopeUnavailable =
                    (WorkspaceScopeReadResult.Unavailable)initialRead;
                constructionFailure =
                    new InitialRealizationOutcome.Unavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .RootWorkspaceNotCommitted,
                        $"The operation Workspace was unavailable ({scopeUnavailable.RuntimeFailure}).");
                goto ConstructionComplete;
            }
            WorkspaceScopeOperationResult rootAdmission =
                await workspace.AddPackagesAsync(
                        initial.Snapshot.Revision,
                        initial.Snapshot.PublicationBase,
                        [preparation.Request.Root],
                        preparation.Request.WorkspaceDeadline,
                        cancellationToken)
                    .ConfigureAwait(false);
            WorkspaceScopeSnapshot? rootedScope = rootAdmission switch
            {
                WorkspaceScopeOperationResult.Committed committed =>
                    committed.Snapshot,
                WorkspaceScopeOperationResult.NoEffect noEffect =>
                    noEffect.Snapshot,
                _ => null,
            };
            if (rootedScope is null)
            {
                constructionFailure =
                    new InitialRealizationOutcome.Unavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .RootWorkspaceNotCommitted,
                        PackageDependencyMemberCallGraphInspection
                            .DescribeScopeOperation(rootAdmission));
                goto ConstructionComplete;
            }
            lowerRequest = new(
                workspace,
                rootedScope,
                registrations,
                preparation.Traversal,
                [preparation.Request.Root],
                preparation.EdgeExecutions,
                new(
                    rootOccurrenceIndex: 0,
                    preparation.Request.Focus.ModuleVersionId,
                    preparation.Request.Focus.MethodToken),
                preparation.Request.Graph,
                preparation.Request.WorkspaceDeadline,
                preparation.Request.RealizationOptions,
                preparation.Request.SupplyChainBaseline);
            PackageDependencyMemberCallGraphPreparationOutcome prepared =
                await PackageDependencyMemberCallGraphOperation.PrepareAsync(
                        lowerRequest,
                        preparation.Source.House,
                        preparation.Source.IssueOperation(
                            preparation.Request.RealizationOperation,
                            cancellationToken))
                    .ConfigureAwait(false);
            if (prepared
                is PackageDependencyMemberCallGraphPreparationOutcome
                    .WorkspaceNotCommitted notCommitted)
            {
                constructionFailure =
                    new InitialRealizationOutcome.Unavailable(
                        PackageDependencyMemberCallGraphInspectionUnavailableReason
                            .DependencyWorkspaceNotCommitted,
                        PackageDependencyMemberCallGraphInspection
                            .DescribeScopeOperation(
                                notCommitted.ScopeOperation));
                goto ConstructionComplete;
            }
            graphPreparation =
                ((PackageDependencyMemberCallGraphPreparationOutcome
                    .Prepared)prepared).Value;

        ConstructionComplete:
            ;
        }
        if (constructionFailure is not null)
        {
            _ = coordinator.CancelCandidate(candidate);
            return constructionFailure;
        }

        WorkspaceRealizationCandidateCompletionResult completion =
            await coordinator.CompleteCandidateAsync(
                candidate,
                cancellationToken)
            .ConfigureAwait(false);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            _ = coordinator.CancelCandidate(candidate);
            return new InitialRealizationOutcome.Unavailable(
                PackageDependencyMemberCallGraphInspectionUnavailableReason
                    .RootWorkspaceNotCommitted,
                $"The initial Workspace candidate could not complete ({completion}).");
        }
        WorkspaceRealizationCutoverResult cutover =
            coordinator.CutOver(candidate);
        if (cutover is not WorkspaceRealizationCutoverResult.Activated)
        {
            _ = coordinator.CancelCandidate(candidate);
            return new InitialRealizationOutcome.Unavailable(
                PackageDependencyMemberCallGraphInspectionUnavailableReason
                    .RootWorkspaceNotCommitted,
                $"The initial Workspace candidate could not be activated ({cutover}).");
        }
        WorkspaceRealizationOperationAdmission admission =
            await coordinator.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return new InitialRealizationOutcome.Unavailable(
                PackageDependencyMemberCallGraphInspectionUnavailableReason
                    .RootWorkspaceNotCommitted,
                $"The initial Workspace operation was unavailable ({admission}).");
        }
        return new InitialRealizationOutcome.Ready(
            lowerRequest!,
            graphPreparation!,
            admitted.Lease);
    }

    static ValueTask<PackageDependencyMemberCallGraphGeneration>
        CreateGenerationAsync(
        WorkspaceRealizationOperationLease operation,
        PackageDependencyMemberCallGraphRequest lowerRequest,
        PackageDependencyMemberCallGraphPreparation preparation,
        IEnumerable<PackageAssemblyContextAdditionalPackageAsset>
            additionalAssets,
        IEnumerable<PackageAssemblyContextPlatformLibrary>
            platformLibraries,
        CancellationToken cancellationToken) =>
        PackageDependencyMemberCallGraphOperation.ExecuteGenerationAsync(
            operation.Workspace,
            operation.Scope,
            operation.Definition.Registrations,
            preparation.GraphBindings,
            preparation.Root,
            lowerRequest.Focus,
            lowerRequest.Graph,
            lowerRequest.SupplyChainBaseline,
            lowerRequest.RealizationOptions,
            additionalAssets,
            platformLibraries,
            cancellationToken);

    static WorkspaceRegistrationRevision Registration(
        InspectionWorkspace workspace) =>
        workspace.GetRegistrationSnapshot()
            is WorkspaceRegistrationReadResult.Available available
                ? available.Revision
                : throw new InvalidOperationException(
                    "The continuation Workspace registrations were unavailable.");

    static async ValueTask<WorkspaceScopeSnapshot> AdmitPackagesAsync(
        InspectionWorkspace workspace,
        WorkspaceRegistrationRevision registrations,
        IEnumerable<PackageRootBinding> packages,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        WorkspaceScopeReadResult read =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        WorkspaceScopeSnapshot initial =
            ((WorkspaceScopeReadResult.Available)read).Snapshot;
        WorkspaceScopeOperationResult admission =
            await workspace.AddPackagesAsync(
                    initial.Revision,
                    initial.PublicationBase,
                    [.. packages],
                    deadline,
                    cancellationToken)
                .ConfigureAwait(false);
        return admission switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            _ => throw new InvalidOperationException(
                "The continuation Workspace did not admit its package closure."),
        };
    }

    static PackageRootBinding? ApplyPackageSelection(
        ExternalAssemblyReferenceSupplierOutcome supplier,
        PackageDependencyMemberCallGraphPreparation graphPreparation,
        ICollection<PackageAssemblyContextAdditionalPackageAsset>
            additionalAssets)
    {
        if (supplier
            is not ExternalAssemblyReferenceSupplierOutcome.PackageOwned
                package)
        {
            return null;
        }

        PackageRootBinding binding =
            graphPreparation.ResolvePackageBinding(
                package.Package.Selection.Evidence.Route);
        PackageCompileAsset asset =
            package.Package.Selection.Evidence.PayloadAsset;
        foreach (PackageAssemblyContextAdditionalPackageAsset previous
            in additionalAssets.Where(
                    item =>
                        !ReferenceEquals(item.Package, binding)
                        && item.Package.Coordinate.PackageId.Equals(
                            binding.Coordinate.PackageId,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray())
        {
            additionalAssets.Remove(previous);
        }
        if (!additionalAssets.Any(item =>
                ReferenceEquals(item.Package, binding)
                && Equals(item.Asset, asset)))
        {
            additionalAssets.Add(new(binding, asset));
        }
        return binding;
    }

    static void AddPlatformTarget(
        ExternalAssemblyReferenceSupplierOutcome supplier,
        ICollection<PlatformFamilyTarget> targets)
    {
        if (supplier
            is not ExternalAssemblyReferenceSupplierOutcome.PlatformOwned
                platform)
        {
            return;
        }
        PlatformFamilyTarget selected = platform.Platform.Family.Target;
        PlatformFamilyTarget? previous = targets.SingleOrDefault(
            target => target.Family == selected.Family);
        if (previous is not null && previous != selected)
            targets.Remove(previous);
        if (!targets.Contains(selected))
            targets.Add(selected);
    }

    internal static async ValueTask<WorkspaceRealizationOperationLease?>
        AdoptPublishedSuccessorOperationAsync(
        WorkspaceReplacementCoordinator coordinator,
        AssemblyReferenceWorkspaceContinuationOutcome continuation,
        Func<ValueTask>? disposeUnadoptedSuccessor)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(continuation);
        AssemblyReferenceWorkspaceContinuationPublication? publication =
            continuation switch
            {
                AssemblyReferenceWorkspaceContinuationOutcome.Rejected
                    rejected => rejected.Cleanup.Publication,
                AssemblyReferenceWorkspaceContinuationOutcome.Cancelled
                    cancelled => cancelled.Cleanup.Publication,
                AssemblyReferenceWorkspaceContinuationOutcome.Incomplete
                    incomplete => incomplete.Cleanup.Publication,
                AssemblyReferenceWorkspaceContinuationOutcome.Failed
                    failed => failed.Cleanup.Publication,
                _ => null,
            };
        if (publication is null)
            return null;
        if (disposeUnadoptedSuccessor is null)
        {
            throw new InvalidOperationException(
                "A published continuation did not retain its successor graph generation.");
        }

        try
        {
            WorkspaceRealizationOperationAdmission admission =
                await coordinator.EnterOperationAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            if (admission
                is not WorkspaceRealizationOperationAdmission.Admitted admitted
                || !ReferenceEquals(
                    admitted.Lease.Realization,
                    publication.Successor.Identity))
            {
                if (admission
                    is WorkspaceRealizationOperationAdmission.Admitted invalid)
                {
                    invalid.Lease.Dispose();
                }
                throw new InvalidOperationException(
                    "A published successor Workspace could not issue its retained graph operation.");
            }
            return admitted.Lease;
        }
        catch
        {
            await disposeUnadoptedSuccessor().ConfigureAwait(false);
            throw;
        }
    }

    static AssemblyReferenceOccurrenceKey Key(
        PackageAssemblyReferenceCallOccurrenceEvidence occurrence) =>
        new(
            occurrence.CallSite.CallerModuleVersionId,
            occurrence.CallSite.CallerMethodToken,
            occurrence.CallSite.ILOffset,
            occurrence.CallSite.OperandToken,
            occurrence.Request.Target,
            occurrence.Request.Scope);

    abstract record IntrinsicCoreLibraryContinuationResult(
        PackageDependencyIntrinsicCoreLibraryContinuationEvidence Evidence)
    {
        internal sealed record Terminal(
            PackageDependencyIntrinsicCoreLibraryContinuationEvidence Evidence)
            : IntrinsicCoreLibraryContinuationResult(Evidence);

        internal sealed record Published(
            PackageDependencyMemberCallGraphGeneration Generation,
            WorkspaceRealizationOperationLease Operation,
            PackageDependencyIntrinsicCoreLibraryContinuationEvidence Evidence)
            : IntrinsicCoreLibraryContinuationResult(Evidence);
    }

    abstract record InitialRealizationOutcome
    {
        private InitialRealizationOutcome()
        {
        }

        internal sealed record Ready(
            PackageDependencyMemberCallGraphRequest Request,
            PackageDependencyMemberCallGraphPreparation Preparation,
            WorkspaceRealizationOperationLease Operation)
            : InitialRealizationOutcome;

        internal sealed record Unavailable(
            PackageDependencyMemberCallGraphInspectionUnavailableReason
                Reason,
            string Detail)
            : InitialRealizationOutcome;
    }

    readonly record struct AssemblyReferenceOccurrenceKey(
        Guid CallerModuleVersionId,
        int CallerMethodToken,
        int ILOffset,
        int OperandToken,
        AssemblyBindingTarget Target,
        AssemblyResolutionScope Scope);
}
