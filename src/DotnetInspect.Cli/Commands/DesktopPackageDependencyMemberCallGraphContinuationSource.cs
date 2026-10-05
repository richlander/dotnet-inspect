using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Local;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Local;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.ResearchQueries;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal sealed class
    DesktopPackageDependencyMemberCallGraphContinuationSource :
        PackageDependencyMemberCallGraphExternalContinuationSource,
        IAsyncDisposable
{
    readonly PackageDependencyMemberCallGraphInspectionSource _packages;
    readonly PackageHouseOperation _packageOperation;
    readonly DesktopPlatformPackageSourceRuntime _packageRuntime;
    readonly PackagePlatformHouseAdapter _packagePlatform;
    readonly InstalledPlatformHouseAdapter? _installed;
    readonly Dictionary<PlatformFamily, PlatformFamilyTarget>
        _selectedTargets = [];

    internal DesktopPackageDependencyMemberCallGraphContinuationSource(
        PackageDependencyMemberCallGraphInspectionSource packages,
        PackageHouseOperation packageOperation,
        Func<DesktopPackageSourceComposition> createComposition,
        NuGetSourceOptions sourceOptions)
    {
        _packages = packages
            ?? throw new ArgumentNullException(nameof(packages));
        _packageOperation = packageOperation
            ?? throw new ArgumentNullException(nameof(packageOperation));
        _packageRuntime = new(
            createComposition,
            sourceOptions,
            "inspect-cli-call-graph-platform");
        _packagePlatform =
            _packageRuntime.CreateAdapter(
                "cli-call-graph-platform-package");
        string? dotnetRoot = FindActiveDotnetRoot();
        if (dotnetRoot is not null)
        {
            InstalledDotnetHiveIdentity hive =
                InstalledDotnetHiveIdentity.Create(
                    "cli-call-graph-platform");
            _installed = new(
                new InstalledReferencePackSource(hive, dotnetRoot),
                new InstalledImplementationPlatformSource(
                    hive,
                    dotnetRoot),
                "cli-call-graph-platform-installed");
        }
    }

    public override ValueTask<PlatformAssemblyReferenceExternalRoute>
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
            if (_selectedTargets.TryGetValue(
                    population.Family,
                    out PlatformFamilyTarget? target))
            {
                AddEligibility(
                    eligibilityByTarget,
                    target,
                    new PlatformAssemblyReferenceFamilyEligibility
                        .WorkspacePopulation(population));
            }
        }
        if (eligibilityByTarget.Count == 0)
        {
            return ValueTask.FromResult(
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
            PlatformSourcePlan sources = CreateSourcePlan(
                target.Family,
                includeReference: true,
                includeImplementation: false);
            var origin = new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    $"cli-call-graph-{FamilyName(target.Family)}-binding"));
            var metadata =
                new PlatformMetadataRequestEvidence<
                    AssemblyBindingRequest>(
                        platformBindingRequest,
                        $"cli-call-graph-{FamilyName(target.Family)}-request");
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
                                $"cli-call-graph-{FamilyName(target.Family)}-route"),
                            PlatformViewDemand.Reference);
            var platformRequest = new PlatformHouseRequest(
                PlatformHouseRequestIdentity.Create(
                    $"cli-call-graph-{FamilyName(target.Family)}-binding"),
                new PlatformTargetDemand.Exact(target),
                origin,
                operation,
                sources,
                BindingWorkBudget(),
                cancellationToken);
            families.Add(
                new(
                    [.. eligibility],
                    platformRequest));
        }

        return ValueTask.FromResult(
            new PlatformAssemblyReferenceExternalRoute(
                request,
                generation,
                focalScope,
                families.MoveToImmutable(),
                packageRoutes));
    }

    public override async ValueTask<
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

    public override async ValueTask<ImmutableArray<
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

    public ValueTask DisposeAsync() =>
        _packageRuntime.DisposeAsync();

    async ValueTask<ExternalAssemblyReferencePlatformResult>
        ResolveFamilyAsync(
        PlatformAssemblyReferenceFamilyRoute family,
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken)
    {
        var attempts =
            new List<PlatformAssemblyReferenceSourceAttempt>();
        int sourceOperations = 0;
        int assemblies = 0;
        long bytes = 0;
        long started = Stopwatch.GetTimestamp();
        if (_installed is not null)
        {
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.SourceOperation,
                1);
            InstalledPlatformHouseResult<InstalledReferenceRealization>
                result =
                    await _installed.RealizeReferenceAsync(
                            family.PlatformRequest)
                        .ConfigureAwait(false);
            attempts.Add(
                result switch
                {
                    InstalledPlatformHouseResult<
                        InstalledReferenceRealization>.Succeeded
                            installedSuccess =>
                            InstalledPlatformAssemblyReferenceResolver
                                .PrepareAttempt(
                                    family.PlatformRequest,
                                    installedSuccess,
                                    PlatformHouseCandidateIdentity.Create(
                                        "cli-call-graph-installed-reference")),
                    InstalledPlatformHouseResult<
                        InstalledReferenceRealization>.NotSucceeded terminal =>
                            InstalledPlatformAssemblyReferenceResolver
                                .PrepareAttempt(terminal),
                    _ => throw new InvalidOperationException(
                        "Unknown installed Platform reference result."),
                });
            sourceOperations++;
            if (result
                is InstalledPlatformHouseResult<
                    InstalledReferenceRealization>.Succeeded
                        installedRealization)
            {
                assemblies += installedRealization.Value.Libraries.Count;
                bytes += installedRealization.Value.Libraries.Sum(
                    static library => library.TotalContentLength);
                return new(
                    family.PlatformRequest,
                    await CompleteFamilyAsync(
                            family.PlatformRequest,
                            attempts,
                            sourceOperations,
                            assemblies,
                            bytes,
                            started)
                        .ConfigureAwait(false));
            }
            if (result.Contribution
                is not PlatformSourceContribution.Unavailable
                {
                    Reason: PlatformSourceUnavailabilityKind.Absent,
                })
            {
                return new(
                    family.PlatformRequest,
                    await CompleteFamilyAsync(
                            family.PlatformRequest,
                            attempts,
                            sourceOperations,
                            assemblies,
                            bytes,
                            started)
                        .ConfigureAwait(false));
            }
        }

        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.SourceOperation,
            1);
        PlatformHouseWorkBudget packageWork =
            AdmitPackageBackedWork(
                family.PlatformRequest.Work,
                work,
                retainAssemblies: false);
        PackagePlatformHouseResult<PackageReferenceRealization>
            packageResult =
                await _packagePlatform.RealizeReferenceAsync(
                        family.PlatformRequest,
                        packageWork,
                        _packageRuntime.IssueOperation(
                            family.PlatformRequest.CancellationToken))
                    .ConfigureAwait(false);
        attempts.Add(
            packageResult switch
            {
                PackagePlatformHouseResult<
                    PackageReferenceRealization>.Succeeded success =>
                        PackagePlatformAssemblyReferenceResolver
                            .PrepareAttempt(
                                family.PlatformRequest,
                                success,
                                PlatformHouseCandidateIdentity.Create(
                                    "cli-call-graph-package-reference")),
                PackagePlatformHouseResult<
                    PackageReferenceRealization>.NotSucceeded terminal =>
                        PackagePlatformAssemblyReferenceResolver
                            .PrepareAttempt(terminal),
                _ => throw new InvalidOperationException(
                    "Unknown package-backed Platform reference result."),
            });
        sourceOperations++;
        if (packageResult
            is PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded packageSuccess)
        {
            assemblies += packageSuccess.Value.Libraries.Length;
            long packageBytes = packageSuccess.Value.Libraries.Sum(
                static library => library.TotalContentLength);
            bytes += packageBytes;
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.TransferBytes,
                packageBytes);
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.RealizedAssembly,
                packageSuccess.Value.Libraries.Length);
        }

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await CompleteFamilyAsync(
                    family.PlatformRequest,
                    attempts,
                    sourceOperations,
                    assemblies,
                    bytes,
                    started)
                .ConfigureAwait(false);
        return new(
            family.PlatformRequest,
            outcome);
    }

    static ValueTask<PlatformHouseOutcome<AssemblyBindingDecision>>
        CompleteFamilyAsync(
        PlatformHouseRequest request,
        IEnumerable<PlatformAssemblyReferenceSourceAttempt> attempts,
        int sourceOperations,
        int assemblies,
        long bytes,
        long started) =>
        PlatformHouseAssemblyReferenceResolver.ResolveAsync(
            request,
            attempts,
            new(
                sourceOperations,
                targetCandidates: 0,
                assemblies,
                xmlDocuments: 0,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes,
                forwardingHops: 0,
                targetComparisons: 0,
                Stopwatch.GetElapsedTime(started)));

    async ValueTask<PlatformPopulationArtifactMaterializationOutcome>
        RealizeImplementationPopulationAsync(
        PlatformFamilyTarget target,
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken)
    {
        PlatformSourcePlan sources = CreateSourcePlan(
            target.Family,
            includeReference: false,
            includeImplementation: true);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                $"cli-call-graph-{FamilyName(target.Family)}-population"),
            new PlatformTargetDemand.Exact(target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    $"cli-call-graph-{FamilyName(target.Family)}-population")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Implementation),
            sources,
            PopulationWorkBudget(),
            cancellationToken);

        if (_installed is not null)
        {
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.SourceOperation,
                1);
            long started = Stopwatch.GetTimestamp();
            InstalledPlatformHouseResult<
                InstalledImplementationRealization> installed =
                    await _installed.RealizeImplementationAsync(request)
                        .ConfigureAwait(false);
            if (installed
                is InstalledPlatformHouseResult<
                    InstalledImplementationRealization>.Succeeded success)
            {
                ChargeImplementation(
                    work,
                    success.Value.Libraries.Count,
                    success.Value.ConsumedBytes,
                    acquired: false);
                return await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        success,
                        ImplementationWork(
                            success.Value.Frameworks.Count,
                            success.Value.Libraries.Count,
                            success.Value.ConsumedBytes,
                            Stopwatch.GetElapsedTime(started)))
                    .ConfigureAwait(false);
            }
            if (installed.Contribution
                is not PlatformSourceContribution.Unavailable
                {
                    Reason: PlatformSourceUnavailabilityKind.Absent,
                })
            {
                throw new InvalidOperationException(
                    $"Installed Platform implementation realization failed ({installed.Contribution.Kind}).");
            }
        }

        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.SourceOperation,
            1);
        PlatformHouseWorkBudget packageWork =
            AdmitPackageBackedWork(
                request.Work,
                work,
                retainAssemblies: true);
        long packageStarted = Stopwatch.GetTimestamp();
        PackagePlatformHouseResult<PackageImplementationRealization>
            package =
                await _packagePlatform.RealizeImplementationAsync(
                        request,
                        packageWork,
                        RuntimeInformation.RuntimeIdentifier,
                        _packageRuntime.IssueOperation(cancellationToken))
                    .ConfigureAwait(false);
        if (package
            is not PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded packageSuccess)
        {
            throw new InvalidOperationException(
                $"Package-backed Platform implementation realization failed ({package.Contribution.Kind}).");
        }
        ChargeImplementation(
            work,
            packageSuccess.Value.Libraries.Length,
            packageSuccess.Value.ConsumedBytes,
            acquired: false);
        return await PackagePlatformLibraryMaterializer
            .MaterializeImplementationPopulationAsync(
                request,
                packageSuccess,
                ImplementationWork(
                    packageSuccess.Value.Frameworks.Length,
                    packageSuccess.Value.Libraries.Length,
                    packageSuccess.Value.ConsumedBytes,
                    Stopwatch.GetElapsedTime(packageStarted)))
            .ConfigureAwait(false);
    }

    PlatformSourcePlan CreateSourcePlan(
        PlatformFamily family,
        bool includeReference,
        bool includeImplementation)
    {
        var selections =
            new List<PlatformSourceSelection>();
        if (includeReference)
        {
            selections.Add(
                new(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    _installed is null
                        ? [_packagePlatform.ReferenceRealization]
                        : [
                            _installed.Capabilities.ReferenceRealization,
                            _packagePlatform.ReferenceRealization,
                        ]));
        }
        if (includeImplementation)
        {
            selections.Add(
                new(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    _installed is null
                        ? [_packagePlatform.ImplementationRealization]
                        : [
                            _installed.Capabilities
                                .ImplementationRealization,
                            _packagePlatform.ImplementationRealization,
                        ]));
        }
        string name = FamilyName(family);
        return new(
            PlatformSourcePlanIdentity.Create(
                $"cli-call-graph-{name}-sources"),
            PlatformSourcePolicyGeneration.Create(
                "generation-1"),
            selections);
    }

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

    static PlatformHouseWorkBudget BindingWorkBudget() =>
        new(
            maxSourceOperations: 2,
            maxTargetCandidates: 0,
            maxAssemblies: 2,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 512L * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromMinutes(5));

    static PlatformHouseWorkBudget PopulationWorkBudget() =>
        new(
            maxSourceOperations: 1,
            maxTargetCandidates: 0,
            maxAssemblies: 4_096,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 2L * 1024 * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromMinutes(10));

    static PlatformHouseConsumedWork ImplementationWork(
        int frameworks,
        int assemblies,
        long bytes,
        TimeSpan elapsed) =>
        new(
            frameworks,
            targetCandidates: 0,
            assemblies,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed);

    static void ChargeImplementation(
        AssemblyReferenceResolutionWorkLedger work,
        int assemblies,
        long bytes,
        bool acquired)
    {
        if (acquired)
        {
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.Acquisition,
                1);
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.TransferBytes,
                bytes);
        }
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.RealizedAssembly,
            assemblies);
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.RetainedAssemblyBytes,
            bytes);
    }

    static void Charge(
        AssemblyReferenceResolutionWorkLedger work,
        AssemblyReferenceResolutionWorkKind kind,
        long amount)
    {
        if (amount != 0)
            work.Charge(kind, amount);
    }

    internal static PlatformHouseWorkBudget AdmitPackageBackedWork(
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

    static string FamilyName(PlatformFamily family) =>
        family switch
        {
            PlatformFamily.DotNetRuntime => "runtime",
            PlatformFamily.AspNetCore => "aspnetcore",
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };

    static string? FindActiveDotnetRoot()
    {
        string? configured = Environment.GetEnvironmentVariable(
            "DOTNET_ROOT");
        if (HasPacks(configured))
            return Path.GetFullPath(configured!);

        string runtimeDirectory =
            RuntimeEnvironment.GetRuntimeDirectory()
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        DirectoryInfo? version = new DirectoryInfo(runtimeDirectory);
        DirectoryInfo? framework = version.Parent;
        DirectoryInfo? shared = framework?.Parent;
        DirectoryInfo? root = shared?.Parent;
        return framework is not null
            && shared is not null
            && root is not null
            && framework.Name.Equals(
                "Microsoft.NETCore.App",
                StringComparison.OrdinalIgnoreCase)
            && shared.Name.Equals(
                "shared",
                StringComparison.OrdinalIgnoreCase)
            && HasPacks(root.FullName)
                ? root.FullName
                : null;
    }

    static bool HasPacks(string? root) =>
        !string.IsNullOrWhiteSpace(root)
        && Directory.Exists(Path.Combine(root, "packs"));
}
