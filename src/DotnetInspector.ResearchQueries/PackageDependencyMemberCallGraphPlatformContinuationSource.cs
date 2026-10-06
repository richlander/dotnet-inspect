using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.ResearchQueries;

/// <summary>
/// Owns the host-neutral Platform continuation lifecycle while hosts supply
/// concrete target discovery and artifact realization capabilities.
/// </summary>
public abstract class PackageDependencyMemberCallGraphPlatformContinuationSource :
    PackageDependencyMemberCallGraphExternalContinuationSource
{
    readonly PackageDependencyMemberCallGraphInspectionSource _packages;
    readonly PackageHouseOperation _packageOperation;
    readonly string _operationPrefix;
    readonly Dictionary<PlatformFamily, PlatformFamilyTarget>
        _selectedTargets = [];

    protected PackageDependencyMemberCallGraphPlatformContinuationSource(
        PackageDependencyMemberCallGraphInspectionSource packages,
        PackageHouseOperation packageOperation,
        string operationPrefix)
    {
        _packages = packages
            ?? throw new ArgumentNullException(nameof(packages));
        _packageOperation = packageOperation
            ?? throw new ArgumentNullException(nameof(packageOperation));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationPrefix);
        _operationPrefix = operationPrefix;
    }

    public sealed override async ValueTask<
        PackageDependencyMemberCallGraphPlatformRouteFormationOutcome>
        FormPlatformRouteAsync(
            AssemblyBindingRequest request,
            AssemblyReferenceResolutionGenerationReceipt generation,
            MemberCallGraphFocalScopeReceipt focalScope,
            PackageAssemblyReferenceRouteEligibilityReceipt packageRoutes,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(work);

        var eligibilityByTarget =
            new Dictionary<
                PlatformFamilyTarget,
                List<PlatformAssemblyReferenceFamilyEligibility>>();
        foreach (PackageAssemblyReferenceRouteOccurrence route
            in packageRoutes.Routes)
        {
            if (route.Disposition
                    != PackageAssemblyReferenceRouteDisposition
                        .PlatformDelegated
                || route.Execution.Pruning is not { } pruning)
            {
                continue;
            }

            AddEligibility(
                eligibilityByTarget,
                pruning.Target,
                new PlatformAssemblyReferenceFamilyEligibility
                    .DelegatedPackageRoute(route));
            _selectedTargets[pruning.Target.Family] = pruning.Target;
        }
        foreach (MemberCallGraphPlatformPopulationScope population
            in focalScope.PlatformPopulations)
        {
            if (!_selectedTargets.TryGetValue(
                    population.Family,
                    out PlatformFamilyTarget? target))
            {
                PlatformTargetDiscoveryOutcome selection =
                    await DiscoverTargetAsync(
                            population.Family,
                            packageRoutes.Traversal.TraversalTargetPolicy
                                .TargetFramework,
                            work,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (selection
                    is PlatformTargetDiscoveryOutcome.Incomplete incomplete)
                {
                    return new
                        PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
                            .Incomplete(incomplete.Evidence);
                }
                target =
                    ((PlatformTargetDiscoveryOutcome.Selected)selection).Target;
                _selectedTargets.Add(population.Family, target);
            }
            AddEligibility(
                eligibilityByTarget,
                target,
                new PlatformAssemblyReferenceFamilyEligibility
                    .WorkspacePopulation(population));
        }
        if (eligibilityByTarget.Count == 0)
        {
            return new
                PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
                    .Completed(
                        new PlatformAssemblyReferenceExternalRoute(
                            request,
                            generation,
                            focalScope,
                            [],
                            packageRoutes,
                            new(
                                generation,
                                focalScope,
                                packageRoutes,
                                "No exact Platform family target is eligible for this AssemblyRef route.")));
        }

        var platformBindingRequest = new AssemblyBindingRequest(
            request.Target,
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);
        var families =
            ImmutableArray.CreateBuilder<
                PlatformAssemblyReferenceFamilyRoute>(
                    eligibilityByTarget.Count);
        foreach ((
            PlatformFamilyTarget target,
            List<PlatformAssemblyReferenceFamilyEligibility> eligibility)
            in eligibilityByTarget.OrderBy(
                static pair => pair.Key.Family))
        {
            string familyName = FamilyName(target.Family);
            PlatformSourcePlan sources = CreateSourcePlan(
                target.Family,
                includeReference: true,
                includeImplementation: false);
            var origin = new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    $"{_operationPrefix}-{familyName}-binding"));
            var metadata =
                new PlatformMetadataRequestEvidence<
                    AssemblyBindingRequest>(
                        platformBindingRequest,
                        $"{_operationPrefix}-{familyName}-request");
            var prerequisites = new PlatformAssemblyReferenceRoute(
                metadata.Identity,
                target,
                origin,
                sources.Identity,
                sources.Generation);
            var operation =
                new PlatformHouseOperation.ResolveAssemblyReference
                    .WithPrerequisites<
                        PlatformAssemblyReferenceRoute>(
                            metadata,
                            new(
                                prerequisites,
                                $"{_operationPrefix}-{familyName}-route"),
                            PlatformViewDemand.Reference);
            var platformRequest = new PlatformHouseRequest(
                PlatformHouseRequestIdentity.Create(
                    $"{_operationPrefix}-{familyName}-binding"),
                new PlatformTargetDemand.Exact(target),
                origin,
                operation,
                sources,
                CreateBindingWorkBudget(),
                cancellationToken);
            families.Add(
                new(
                    [.. eligibility],
                    platformRequest));
        }

        return new
            PackageDependencyMemberCallGraphPlatformRouteFormationOutcome
                .Completed(
                    new PlatformAssemblyReferenceExternalRoute(
                        request,
                        generation,
                        focalScope,
                        families.MoveToImmutable(),
                        packageRoutes));
    }

    public sealed override async ValueTask<
        ExternalAssemblyReferenceSupplierOutcome> ResolveAsync(
            PackageAssemblyReferenceExternalRoute packageRoute,
            PlatformAssemblyReferenceExternalRoute platformRoute,
            AssemblyBindingSelection referencingContextSelection,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken)
    {
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.PackageRouteOccurrence,
            Math.Max(
                1,
                packageRoute.PackageRoutes.Routes.Length));
        using PackageSourceOperationLease packageOperation =
            _packages.IssueOperation(
                _packageOperation,
                cancellationToken);
        return await ExternalAssemblyReferenceSupplierAssociation
            .ExecuteAsync(
                packageRoute,
                referencingContextSelection,
                _packages.House,
                packageOperation,
                async (_, token) =>
                    await PlatformAssemblyReferenceRouteAdapter
                        .ExecuteAsync(
                            platformRoute,
                            (family, familyToken) =>
                                ResolveFamilyAsync(
                                    family,
                                    work,
                                    familyToken),
                            token)
                        .ConfigureAwait(false),
                cancellationToken,
                work.Charge)
            .ConfigureAwait(false);
    }

    public sealed override async ValueTask<ImmutableArray<
        PackageAssemblyContextPlatformLibrary>>
        AdmitPlatformPopulationAsync(
            InspectionWorkspace workspace,
            WorkspaceRegistrationRevision registrations,
            PlatformFamilyTarget target,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken)
    {
        _selectedTargets[target.Family] = target;
        PlatformPopulationArtifactMaterializationOutcome materialized =
            await RealizeImplementationPopulationAsync(
                    target,
                    work,
                    cancellationToken)
                .ConfigureAwait(false);
        if (materialized
            is not PlatformPopulationArtifactMaterializationOutcome
                .Completed completed)
        {
            throw new InvalidOperationException(
                $"The complete Platform implementation population was not realized ({materialized.Realization.Receipt.HouseReceipt.SettlementKind}).");
        }

        bool admitted = false;
        try
        {
            WorkspaceLibraryAdmissionOutcome outcome =
                await workspace.AdmitLibraryBatchAsync(
                        registrations,
                        completed.Artifacts,
                        completed.Population.Owners)
                    .ConfigureAwait(false);
            if (outcome
                is not WorkspaceLibraryAdmissionOutcome.Accepted accepted)
            {
                throw new InvalidOperationException(
                    $"The successor Workspace rejected the Platform implementation population ({outcome}).");
            }
            admitted = true;
            if (accepted.Receipt.Occurrences.Length
                != completed.Population.Value.Members.Count)
            {
                throw new InvalidOperationException(
                    "The admitted Platform Library occurrences do not align with the realized population.");
            }
            return
            [
                .. accepted.Receipt.Occurrences.Select(
                    occurrence =>
                        new PackageAssemblyContextPlatformLibrary(
                            target,
                            occurrence)),
            ];
        }
        finally
        {
            if (!admitted)
            {
                _ = await PlatformPopulationAuthorityRetirement
                    .RetireAsync(completed)
                    .ConfigureAwait(false);
            }
        }
    }

    protected string OperationName(
        PlatformFamily family,
        string operation) =>
        $"{_operationPrefix}-{FamilyName(family)}-{operation}";

    protected abstract ValueTask<PlatformTargetDiscoveryOutcome>
        DiscoverTargetAsync(
            PlatformFamily family,
            string targetFramework,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    protected abstract PlatformSourcePlan CreateSourcePlan(
        PlatformFamily family,
        bool includeReference,
        bool includeImplementation);

    protected abstract PlatformHouseWorkBudget CreateBindingWorkBudget();

    protected abstract ValueTask<ExternalAssemblyReferencePlatformResult>
        ResolveFamilyAsync(
            PlatformAssemblyReferenceFamilyRoute family,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    protected abstract ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
        RealizeImplementationPopulationAsync(
            PlatformFamilyTarget target,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken);

    protected static bool TryCreateTargetPolicy(
        string operationPrefix,
        string familyName,
        string targetFramework,
        PlatformSourceCapabilityIdentity? preferred,
        PlatformSourceCapabilityIdentity fallback,
        [NotNullWhen(true)]
        out PlatformVersionlessRuntimeTargetPolicy? policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        ArgumentNullException.ThrowIfNull(targetFramework);
        ArgumentNullException.ThrowIfNull(fallback);
        if (!PlatformTargetFramework.TryParse(
                targetFramework,
                out PlatformTargetFramework? fallbackFramework))
        {
            policy = null;
            return false;
        }
        PlatformTargetDiscoveryStage? preferredStage =
            preferred is null
                ? null
                : new(
                    new PlatformTargetDiscoveryScope.AllFrameworks(),
                    [preferred]);
        var fallbackStage = new PlatformTargetDiscoveryStage(
            new PlatformTargetDiscoveryScope.ExactFramework(
                fallbackFramework),
            [fallback]);
        policy = new PlatformVersionlessRuntimeTargetPolicy(
            PlatformTargetSelectionPolicyIdentity.Create(
                $"{operationPrefix}-{familyName}-default"),
            PlatformTargetSelectionPolicyGeneration.Create(
                "generation-1"),
            PlatformVersion.Parse(
                $"{fallbackFramework.Major}.{fallbackFramework.Minor}.0"),
            preferredStage,
            fallbackStage);
        return true;
    }

    protected static void Charge(
        AssemblyReferenceResolutionWorkLedger work,
        AssemblyReferenceResolutionWorkKind kind,
        long amount)
    {
        if (amount != 0)
            work.Charge(kind, amount);
    }

    protected static PlatformHouseWorkBudget AdmitPackageBackedWork(
        PlatformHouseWorkBudget source,
        AssemblyReferenceResolutionWorkLedger work,
        bool retainAssemblies)
    {
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.Acquisition,
            1);
        long transferBytes =
            work.GetRemainingAllowance(
                AssemblyReferenceResolutionWorkKind.TransferBytes);
        if (source.MaxBytes != 0 && transferBytes == 0)
        {
            work.Charge(
                AssemblyReferenceResolutionWorkKind.TransferBytes,
                1);
        }
        long bytes = Math.Min(
            source.MaxBytes,
            transferBytes);
        if (retainAssemblies)
        {
            long retainedBytes =
                work.GetRemainingAllowance(
                    AssemblyReferenceResolutionWorkKind
                        .RetainedAssemblyBytes);
            if (source.MaxBytes != 0 && retainedBytes == 0)
            {
                work.Charge(
                    AssemblyReferenceResolutionWorkKind
                        .RetainedAssemblyBytes,
                    1);
            }
            bytes = Math.Min(
                bytes,
                retainedBytes);
        }
        long realizedAssemblies =
            work.GetRemainingAllowance(
                AssemblyReferenceResolutionWorkKind.RealizedAssembly);
        if (source.MaxAssemblies != 0 && realizedAssemblies == 0)
        {
            work.Charge(
                AssemblyReferenceResolutionWorkKind.RealizedAssembly,
                1);
        }
        long assemblies = Math.Min(
            source.MaxAssemblies,
            realizedAssemblies);
        return new(
            source.MaxSourceOperations,
            source.MaxTargetCandidates,
            checked((int)assemblies),
            source.MaxXmlDocuments,
            source.MaxPortablePdbs,
            source.MaxSourceDocuments,
            bytes,
            source.MaxForwardingHops,
            source.MaxDuration);
    }

    protected static string FamilyName(PlatformFamily family) =>
        family switch
        {
            PlatformFamily.DotNetRuntime => "runtime",
            PlatformFamily.AspNetCore => "aspnetcore",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    protected abstract class PlatformTargetDiscoveryOutcome
    {
        private protected PlatformTargetDiscoveryOutcome()
        {
        }

        public sealed class Selected(
            PlatformFamilyTarget target) :
            PlatformTargetDiscoveryOutcome
        {
            public PlatformFamilyTarget Target { get; } = target;
        }

        public sealed class Incomplete(object evidence) :
            PlatformTargetDiscoveryOutcome
        {
            public object Evidence { get; } = evidence
                ?? throw new ArgumentNullException(nameof(evidence));
        }
    }

    protected sealed record UnsupportedPlatformTargetFrameworkEvidence(
        PlatformFamily Family,
        string TargetFramework);

    static void AddEligibility(
        IDictionary<
            PlatformFamilyTarget,
            List<PlatformAssemblyReferenceFamilyEligibility>> routes,
        PlatformFamilyTarget target,
        PlatformAssemblyReferenceFamilyEligibility eligibility)
    {
        if (!routes.TryGetValue(target, out var values))
        {
            values = [];
            routes.Add(target, values);
        }
        values.Add(eligibility);
    }
}
