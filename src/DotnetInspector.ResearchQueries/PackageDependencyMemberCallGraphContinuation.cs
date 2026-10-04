using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.ResearchQueries;

public abstract class
    PackageDependencyMemberCallGraphExternalContinuationSource
{
    public abstract ValueTask<PlatformAssemblyReferenceExternalRoute>
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
        (
            PackageDependencyMemberCallGraphRequest lowerRequest,
            PackageDependencyMemberCallGraphPreparation graphPreparation,
            WorkspaceRealizationOperationLease operation) =
                await CreateInitialRealizationAsync(
                        coordinator,
                        preparation,
                        cancellationToken)
                    .ConfigureAwait(false);

        var work = new AssemblyReferenceResolutionWorkLedger(_budget);
        var additionalAssets =
            new List<PackageAssemblyContextAdditionalPackageAsset>();
        var platformTargets =
            new List<PlatformFamilyTarget>();
        var attempted =
            new HashSet<AssemblyReferenceOccurrenceKey>();
        PackageDependencyMemberCallGraphGeneration? generation = null;
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
                PackageAssemblyReferenceCallOccurrenceEvidence? occurrence =
                    available.AssemblyReferenceOccurrences.FirstOrDefault(
                        candidate => !attempted.Contains(Key(candidate)));
                if (occurrence is null)
                    break;

                attempted.Add(Key(occurrence));
                AssemblyReferenceResolutionRequest resolution =
                    await CreateResolutionRequestAsync(
                            lowerRequest,
                            generation,
                            occurrence,
                            work,
                            cancellationToken)
                        .ConfigureAwait(false);
                AssemblyReferenceResolutionOutcome outcome =
                    await AssemblyReferenceResolutionLadder.ExecuteAsync(
                            resolution,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (outcome
                    is not AssemblyReferenceResolutionOutcome
                        .AcquisitionRequired acquisition)
                {
                    if (outcome
                        is AssemblyReferenceResolutionOutcome.Incomplete)
                    {
                        break;
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
                ImmutableArray<
                    PackageAssemblyContextPlatformLibrary>
                    successorPlatformLibraries = [];
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
                                WorkspaceScopeSnapshot scope =
                                    await AdmitPackagesAsync(
                                            workspace,
                                            registrations,
                                            graphPreparation.GraphBindings,
                                            request.WorkspaceDeadline,
                                            token)
                                        .ConfigureAwait(false);
                                ApplyPackageSelection(
                                    supplier,
                                    graphPreparation.GraphBindings,
                                    additionalAssets);
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
                                            graphPreparation.GraphBindings,
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
                                return await CreateResolutionRequestAsync(
                                        lowerRequest,
                                        successorGeneration,
                                        successorEvidence,
                                        work,
                                        token)
                                    .ConfigureAwait(false);
                            },
                            cancellationToken)
                        .ConfigureAwait(false);

                WorkspaceRealizationOperationLease? successorOperation =
                    continuation
                        is AssemblyReferenceWorkspaceContinuationOutcome
                            .Published published
                        ? published.SuccessorOperation
                        : await EnterPublishedSuccessorAsync(
                                coordinator,
                                continuation,
                                cancellationToken)
                            .ConfigureAwait(false);
                if (successorOperation is null)
                {
                    if (successorGeneration is not null)
                        await successorGeneration.DisposeAsync();
                    break;
                }

                PackageDependencyMemberCallGraphGeneration predecessor =
                    generation;
                generation = successorGeneration
                    ?? throw new InvalidOperationException(
                        "A published continuation did not retain its successor graph generation.");
                operation.Dispose();
                operation = successorOperation;
                await predecessor.DisposeAsync();
            }

            PackageRoleCleanupReport cleanup =
                await generation.CloseAsync().ConfigureAwait(false);
            PackageDependencyMemberCallGraphOutcome lowerOutcome =
                PackageDependencyMemberCallGraphOperation
                    .CompletePreparedGeneration(
                        lowerRequest,
                        graphPreparation,
                        generation,
                        cleanup);
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

    async ValueTask<AssemblyReferenceResolutionRequest>
        CreateResolutionRequestAsync(
        PackageDependencyMemberCallGraphRequest lowerRequest,
        PackageDependencyMemberCallGraphGeneration generation,
        PackageAssemblyReferenceBindingEvidence occurrence,
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken)
    {
        var association =
            PackageAssemblyReferenceSupplierAssociation.Create(
                new(
                    generation.Generation,
                    generation.FocalScope,
                    lowerRequest.Traversal,
                    lowerRequest.Focus.RootOccurrenceIndex,
                    lowerRequest.EdgeExecutions));
        if (association.RouteProjection
            is not PackageAssemblyReferenceRouteProjectionOutcome
                .Completed completed)
        {
            throw new InvalidOperationException(
                "The package AssemblyRef route projection was incomplete.");
        }

        var packageRoute = new PackageAssemblyReferenceExternalRoute(
            occurrence.Request,
            association,
            completed.Receipt);
        PlatformAssemblyReferenceExternalRoute platformRoute =
            await _source.FormPlatformRouteAsync(
                    occurrence.Request,
                    generation.Generation,
                    generation.FocalScope,
                    completed.Receipt,
                    work,
                    cancellationToken)
                .ConfigureAwait(false);
        AssemblyReferenceExternalRouteSet? routeSet = null;
        var plan = new AssemblyReferenceResolutionRoutePlan(
            occurrence.Request,
            generation.Generation,
            generation.FocalScope,
            occurrence.BindingPolicyVersion,
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult<
                    AssemblyReferenceResolutionContextOutcome>(
                    new AssemblyReferenceResolutionContextOutcome.Selected(
                        occurrence.Request,
                        generation.Generation,
                        occurrence.Context));
            },
            (advancement, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                routeSet = new AssemblyReferenceExternalRouteSet(
                    occurrence.Request,
                    generation.Generation,
                    advancement,
                    [packageRoute, platformRoute],
                    async (ledger, executeToken) =>
                    {
                        ExternalAssemblyReferenceSupplierOutcome supplier =
                            await _source.ResolveAsync(
                                    packageRoute,
                                    platformRoute,
                                    occurrence.Context.Selection,
                                    ledger,
                                    executeToken)
                                .ConfigureAwait(false);
                        return SupplierOutcome(
                            occurrence,
                            routeSet!,
                            supplier);
                    });
                return ValueTask.FromResult<
                    AssemblyReferenceExternalRouteSetFormationOutcome>(
                    new AssemblyReferenceExternalRouteSetFormationOutcome
                        .Completed(routeSet));
            });
        return new(plan, work);
    }

    static AssemblyReferenceExternalRouteOutcome SupplierOutcome(
        PackageAssemblyReferenceBindingEvidence occurrence,
        AssemblyReferenceExternalRouteSet routes,
        ExternalAssemblyReferenceSupplierOutcome supplier) =>
        supplier switch
        {
            ExternalAssemblyReferenceSupplierOutcome.PackageOwned package =>
                new AssemblyReferenceExternalRouteOutcome
                    .AcquisitionRequired(
                        occurrence.Request,
                        routes.Generation,
                        routes,
                        package.Route,
                        supplier),
            ExternalAssemblyReferenceSupplierOutcome.PlatformOwned platform =>
                new AssemblyReferenceExternalRouteOutcome
                    .AcquisitionRequired(
                        occurrence.Request,
                        routes.Generation,
                        routes,
                        platform.Platform.Route,
                        supplier),
            ExternalAssemblyReferenceSupplierOutcome.NameOwnedNoMatch =>
                CompletedMissing(
                    occurrence,
                    routes,
                    AssemblyBindingSelection.NameOwnedButNoMatch()),
            ExternalAssemblyReferenceSupplierOutcome.NoSupplier =>
                CompletedMissing(
                    occurrence,
                    routes,
                    AssemblyBindingSelection.NameNotOwned()),
            ExternalAssemblyReferenceSupplierOutcome.Unavailable =>
                new AssemblyReferenceExternalRouteOutcome.Unavailable(
                    occurrence.Request,
                    routes.Generation,
                    routes,
                    supplier),
            ExternalAssemblyReferenceSupplierOutcome.Incomplete =>
                new AssemblyReferenceExternalRouteOutcome.Incomplete(
                    occurrence.Request,
                    routes.Generation,
                    routes,
                    supplier),
            _ => new AssemblyReferenceExternalRouteOutcome.Rejected(
                occurrence.Request,
                routes.Generation,
                routes,
                supplier),
        };

    static AssemblyReferenceExternalRouteOutcome.Completed CompletedMissing(
        PackageAssemblyReferenceBindingEvidence occurrence,
        AssemblyReferenceExternalRouteSet routes,
        AssemblyBindingSelection selection) =>
        new(
            occurrence.Request,
            routes.Generation,
            routes,
            new(
                occurrence.BindingPolicyVersion,
                selection));

    static async ValueTask<(
        PackageDependencyMemberCallGraphRequest Request,
        PackageDependencyMemberCallGraphPreparation Preparation,
        WorkspaceRealizationOperationLease Operation)>
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
            throw new InvalidOperationException(
                $"The initial Workspace candidate was unavailable ({start.GetType().Name}).");
        }
        WorkspaceRealizationCandidate candidate =
            preparedCandidate.Candidate;
        PackageDependencyMemberCallGraphRequest lowerRequest;
        PackageDependencyMemberCallGraphPreparation graphPreparation;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            InspectionWorkspace workspace = construction.Workspace;
            WorkspaceRegistrationRevision registrations =
                Registration(workspace);
            WorkspaceScopeSnapshot rootedScope =
                await AdmitPackagesAsync(
                        workspace,
                        registrations,
                        [preparation.Request.Root],
                        preparation.Request.WorkspaceDeadline,
                        cancellationToken)
                    .ConfigureAwait(false);
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
                is not PackageDependencyMemberCallGraphPreparationOutcome
                    .Prepared completedPreparation)
            {
                throw new InvalidOperationException(
                    "The initial dependency Workspace was not committed.");
            }
            graphPreparation = completedPreparation.Value;
        }

        WorkspaceRealizationCandidateCompletionResult completion =
            await coordinator.CompleteCandidateAsync(
                candidate,
                cancellationToken)
            .ConfigureAwait(false);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            throw new InvalidOperationException(
                $"The initial Workspace candidate could not complete ({completion}).");
        }
        WorkspaceRealizationCutoverResult cutover =
            coordinator.CutOver(candidate);
        if (cutover is not WorkspaceRealizationCutoverResult.Activated)
        {
            throw new InvalidOperationException(
                $"The initial Workspace candidate could not be activated ({cutover}).");
        }
        WorkspaceRealizationOperationAdmission admission =
            await coordinator.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            throw new InvalidOperationException(
                $"The initial Workspace operation was unavailable ({admission}).");
        }
        return (
            lowerRequest,
            graphPreparation,
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

    static void ApplyPackageSelection(
        ExternalAssemblyReferenceSupplierOutcome supplier,
        ImmutableArray<PackageRootBinding> graphBindings,
        ICollection<PackageAssemblyContextAdditionalPackageAsset>
            additionalAssets)
    {
        if (supplier
            is not ExternalAssemblyReferenceSupplierOutcome.PackageOwned
                package)
        {
            return;
        }

        string packageId =
            package.Package.Selection.Evidence.Route.Candidate
                .Coordinate.PackageId;
        string packageVersion =
            package.Package.Selection.Evidence.Route.Candidate
                .Coordinate.Version;
        PackageRootBinding binding = graphBindings.Single(
            candidate =>
                candidate.Coordinate.PackageId.Equals(
                    packageId,
                    StringComparison.OrdinalIgnoreCase)
                && candidate.Coordinate.Version.Equals(
                    packageVersion,
                    StringComparison.OrdinalIgnoreCase));
        PackageCompileAsset asset =
            package.Package.Selection.Evidence.PayloadAsset;
        if (!additionalAssets.Any(item =>
                ReferenceEquals(item.Package, binding)
                && Equals(item.Asset, asset)))
        {
            additionalAssets.Add(new(binding, asset));
        }
    }

    static void AddPlatformTarget(
        ExternalAssemblyReferenceSupplierOutcome supplier,
        ICollection<PlatformFamilyTarget> targets)
    {
        if (supplier
                is ExternalAssemblyReferenceSupplierOutcome.PlatformOwned
                    platform
            && !targets.Contains(platform.Platform.Family.Target))
        {
            targets.Add(platform.Platform.Family.Target);
        }
    }

    static async ValueTask<WorkspaceRealizationOperationLease?>
        EnterPublishedSuccessorAsync(
        WorkspaceReplacementCoordinator coordinator,
        AssemblyReferenceWorkspaceContinuationOutcome continuation,
        CancellationToken cancellationToken)
    {
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

        WorkspaceRealizationOperationAdmission admission =
            await coordinator.EnterOperationAsync(cancellationToken)
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

    static AssemblyReferenceOccurrenceKey Key(
        PackageAssemblyReferenceCallOccurrenceEvidence occurrence) =>
        new(
            occurrence.CallSite.CallerModuleVersionId,
            occurrence.CallSite.CallerMethodToken,
            occurrence.CallSite.ILOffset,
            occurrence.CallSite.OperandToken,
            occurrence.Request.Target,
            occurrence.Request.Scope);

    readonly record struct AssemblyReferenceOccurrenceKey(
        Guid CallerModuleVersionId,
        int CallerMethodToken,
        int ILOffset,
        int OperandToken,
        AssemblyBindingTarget Target,
        AssemblyResolutionScope Scope);
}
