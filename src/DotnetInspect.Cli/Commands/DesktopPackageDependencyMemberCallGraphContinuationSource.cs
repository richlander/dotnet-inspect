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
        PackageDependencyMemberCallGraphPlatformContinuationSource,
        IAsyncDisposable
{
    readonly DesktopPlatformPackageSourceRuntime _packageRuntime;
    readonly PackagePlatformHouseAdapter _packagePlatform;
    readonly InstalledPlatformHouseAdapter? _installed;

    internal DesktopPackageDependencyMemberCallGraphContinuationSource(
        PackageDependencyMemberCallGraphInspectionSource packages,
        PackageHouseOperation packageOperation,
        Func<DesktopPackageSourceComposition> createComposition,
        NuGetSourceOptions sourceOptions)
        : base(
            packages,
            packageOperation,
            "cli-call-graph")
    {
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

    protected override async ValueTask<PlatformTargetDiscoveryOutcome>
        DiscoverTargetAsync(
        PlatformFamily family,
        string targetFramework,
        AssemblyReferenceResolutionWorkLedger work,
        CancellationToken cancellationToken)
    {
        PlatformTargetDiscoverySource? installed =
            _installed is null
                ? null
                : InstalledPlatformTargetDiscovery.CreateSource(_installed);
        PlatformTargetDiscoverySource package =
            PackagePlatformTargetDiscovery.CreateSource(
                _packagePlatform,
                _packageRuntime.IssueOperation);
        string familyName = FamilyName(family);
        if (!TryCreateTargetPolicy(
                "cli-call-graph",
                familyName,
                targetFramework,
                installed?.Capability,
                package.Capability,
                out PlatformVersionlessRuntimeTargetPolicy? targetPolicy))
        {
            return new PlatformTargetDiscoveryOutcome.Incomplete(
                new UnsupportedPlatformTargetFrameworkEvidence(
                    family,
                    targetFramework));
        }
        PlatformSourceCapabilityIdentity[] capabilities =
            installed is null
                ? [package.Capability]
                : [installed.Capability, package.Capability];
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create(
                $"cli-call-graph-{familyName}-target-sources"),
            PlatformSourcePolicyGeneration.Create("generation-1"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.TargetDiscovery,
                    PlatformSourceSelectionMode.Fallback,
                    capabilities),
            ]);
        int maxSourceOperations = (int)Math.Min(
            capabilities.Length,
            work.GetRemainingAllowance(
                AssemblyReferenceResolutionWorkKind.SourceOperation));
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                $"cli-call-graph-{familyName}-target"),
            new PlatformTargetDemand.FamilyDefault(
                family,
                targetPolicy,
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 128,
                    maxComparisons: 512)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    $"cli-call-graph-{familyName}-target")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            sources,
            new PlatformHouseWorkBudget(
                maxSourceOperations,
                maxTargetCandidates: 128,
                maxAssemblies: 0,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 0,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromMinutes(1)),
            cancellationToken);
        PlatformTargetSelectionOutcome outcome =
            await PlatformHouseTargetSelector.SelectAsync(
                    request,
                    installed is null
                        ? [package]
                        : [installed, package])
                .ConfigureAwait(false);
        PlatformHouseConsumedWork consumed = outcome switch
        {
            PlatformTargetSelectionOutcome.Selected selected =>
                selected.ConsumedWork,
            PlatformTargetSelectionOutcome.Terminal terminal =>
                terminal.Receipt.ConsumedWork,
            _ => throw new InvalidOperationException(
                "Unknown Platform target-selection outcome."),
        };
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.SourceOperation,
            consumed.SourceOperations);
        return outcome switch
        {
            PlatformTargetSelectionOutcome.Selected selected =>
                new PlatformTargetDiscoveryOutcome.Selected(selected.Target),
            PlatformTargetSelectionOutcome.Terminal terminal =>
                new PlatformTargetDiscoveryOutcome.Incomplete(
                    DescribeTargetSelectionFailure(terminal)),
            _ => throw new InvalidOperationException(
                "Unknown Platform target-selection outcome."),
        };
    }

    static string DescribeTargetSelectionFailure(
        PlatformTargetSelectionOutcome.Terminal terminal) =>
        $"settlement={terminal.Receipt.SettlementKind}; "
        + $"termination={terminal.Receipt.Termination?.Kind}; "
        + $"sources={string.Join(
            ", ",
            terminal.Receipt.SourceSettlements.Select(
                settlement =>
                    $"{settlement.Contribution.Capability}:"
                    + $"{settlement.Contribution.Kind}:"
                    + $"{(settlement.Contribution
                            is PlatformSourceContribution.Unavailable
                                unavailable
                        ? unavailable.Reason
                        : "-")}:"
                    + $"{settlement.Disposition}:"
                    + $"{(settlement.Contribution
                            is PlatformSourceContribution.TargetDiscovery
                                discovery
                        ? string.Join(
                            "|",
                            discovery.Candidates.Select(
                                candidate => candidate))
                        : "-")}"))}";

    public ValueTask DisposeAsync() =>
        _packageRuntime.DisposeAsync();

    protected override async ValueTask<
        ExternalAssemblyReferencePlatformResult>
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
                            family.PlatformRequest,
                            packageWork))
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

    protected override async ValueTask<
        PlatformPopulationArtifactMaterializationOutcome>
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
                        _packageRuntime.IssueOperation(
                            request,
                            packageWork))
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

    protected override PlatformSourcePlan CreateSourcePlan(
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

    protected override PlatformHouseWorkBudget
        CreateBindingWorkBudget() =>
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

    internal static bool TryCreateTargetPolicy(
        string familyName,
        string targetFramework,
        PlatformSourceCapabilityIdentity? installed,
        PlatformSourceCapabilityIdentity package,
        out PlatformVersionlessRuntimeTargetPolicy? policy) =>
        PackageDependencyMemberCallGraphPlatformContinuationSource
            .TryCreateTargetPolicy(
                "cli-call-graph",
                familyName,
                targetFramework,
                installed,
                package,
                out policy);

    internal new static PlatformHouseWorkBudget AdmitPackageBackedWork(
        PlatformHouseWorkBudget source,
        AssemblyReferenceResolutionWorkLedger work,
        bool retainAssemblies) =>
        PackageDependencyMemberCallGraphPlatformContinuationSource
            .AdmitPackageBackedWork(
                source,
                work,
                retainAssemblies);

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
