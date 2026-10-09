using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Versioning;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Packages;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.ResearchQueries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal sealed class
    BrowserPackageDependencyMemberCallGraphContinuationSource :
        PackageDependencyMemberCallGraphPlatformContinuationSource
{
    readonly PackagePlatformHouseAdapter _platform;
    readonly Func<
        PlatformHouseRequest,
        PlatformHouseWorkBudget,
        PackageSourceOperationLease> _issueOperation;
    readonly TimeSpan _operationTimeout;

    internal BrowserPackageDependencyMemberCallGraphContinuationSource(
        PackageDependencyMemberCallGraphInspectionSource packages,
        PackageHouseOperation packageOperation,
        PackagePlatformHouseAdapter platform,
        Func<
            PlatformHouseRequest,
            PlatformHouseWorkBudget,
            PackageSourceOperationLease> issueOperation,
        TimeSpan operationTimeout)
        : base(
            packages,
            packageOperation,
            "browser-call-graph")
    {
        _platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        _issueOperation = issueOperation
            ?? throw new ArgumentNullException(nameof(issueOperation));
        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout));
        }
        _operationTimeout = operationTimeout;
    }

    protected override async ValueTask<PlatformTargetDiscoveryOutcome>
        DiscoverTargetAsync(
            PlatformFamily family,
            string targetFramework,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken)
    {
        if (TryCreateCurrentRuntimeTarget(
                family,
                targetFramework,
                typeof(object).Assembly
                    .GetCustomAttribute<
                        AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion,
                out PlatformFamilyTarget? currentRuntime))
        {
            return new PlatformTargetDiscoveryOutcome.Selected(
                currentRuntime);
        }

        PlatformTargetDiscoverySource package =
            PackagePlatformTargetDiscovery.CreateSource(
                _platform,
                _issueOperation);
        string familyName = FamilyName(family);
        if (!TryCreateTargetPolicy(
                "browser-call-graph",
                familyName,
                targetFramework,
                preferred: null,
                package.Capability,
                out PlatformVersionlessRuntimeTargetPolicy? targetPolicy))
        {
            return new PlatformTargetDiscoveryOutcome.Incomplete(
                new UnsupportedPlatformTargetFrameworkEvidence(
                    family,
                    targetFramework));
        }
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create(
                OperationName(family, "target-sources")),
            PlatformSourcePolicyGeneration.Create("generation-1"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.TargetDiscovery,
                    PlatformSourceSelectionMode.Fallback,
                    [package.Capability]),
            ]);
        int maxSourceOperations = (int)Math.Min(
            1,
            work.GetRemainingAllowance(
                AssemblyReferenceResolutionWorkKind.SourceOperation));
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                OperationName(family, "target")),
            new PlatformTargetDemand.FamilyDefault(
                family,
                targetPolicy,
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 128,
                    maxComparisons: 512)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    OperationName(family, "target"))),
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
                maxDuration: _operationTimeout),
            cancellationToken);
        PlatformTargetSelectionOutcome outcome =
            await PlatformHouseTargetSelector.SelectAsync(
                    request,
                    [package])
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
                new PlatformTargetDiscoveryOutcome.Selected(
                    selected.Target),
            PlatformTargetSelectionOutcome.Terminal terminal =>
                new PlatformTargetDiscoveryOutcome.Incomplete(
                    terminal),
            _ => throw new InvalidOperationException(
                "Unknown Platform target-selection outcome."),
        };
    }

    internal static bool TryCreateCurrentRuntimeTarget(
        PlatformFamily family,
        string targetFramework,
        string? informationalVersion,
        [NotNullWhen(true)] out PlatformFamilyTarget? target)
    {
        target = null;
        if (family != PlatformFamily.DotNetRuntime
            || !PlatformTargetFramework.TryParse(
                targetFramework,
                out PlatformTargetFramework? framework)
            || string.IsNullOrWhiteSpace(informationalVersion))
        {
            return false;
        }

        string versionText =
            informationalVersion.Split('+', 2)[0];
        if (!PlatformVersion.TryParse(
                versionText,
                out PlatformVersion? version)
            || version.Major != framework.Major
            || version.Minor != framework.Minor)
        {
            return false;
        }

        target = new PlatformFamilyTarget(
            family,
            framework,
            version);
        return true;
    }

    internal static PackageDependencyMemberCallGraphPlatformPruning?
        CreateCurrentRuntimePruning(
            string targetFramework,
            string targetVersion,
            IEnumerable<string> packageOverrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);
        ArgumentNullException.ThrowIfNull(packageOverrides);

        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                "Microsoft.NETCore.App",
                targetFramework,
                targetVersion,
                packageOverrides);
        if (!PackageDependencyMemberCallGraphPlatformPruning
                .TryCreateDotNetRuntime(
                    inventory,
                    out PackageDependencyMemberCallGraphPlatformPruning?
                        pruning)
            || !TryCreateCurrentRuntimeTarget(
                PlatformFamily.DotNetRuntime,
                targetFramework,
                typeof(object).Assembly
                    .GetCustomAttribute<
                        AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion,
                out PlatformFamilyTarget? currentRuntime)
            || currentRuntime.Version != pruning.Target.Version)
        {
            return null;
        }

        return pruning;
    }

    protected override PlatformSourcePlan CreateSourcePlan(
        PlatformFamily family,
        bool includeReference,
        bool includeImplementation)
    {
        var selections = new List<PlatformSourceSelection>();
        if (includeReference)
        {
            selections.Add(
                new(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [_platform.ReferenceRealization]));
        }
        if (includeImplementation)
        {
            selections.Add(
                new(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [_platform.ImplementationRealization]));
        }
        return new(
            PlatformSourcePlanIdentity.Create(
                OperationName(family, "sources")),
            PlatformSourcePolicyGeneration.Create("generation-1"),
            selections);
    }

    protected override PlatformHouseWorkBudget
        CreateBindingWorkBudget() =>
        new(
            maxSourceOperations: 1,
            maxTargetCandidates: 0,
            maxAssemblies: 2,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: BrowserInspectionScope.MaxRetainedImageBytes,
            maxForwardingHops: 0,
            maxDuration: _operationTimeout);

    protected override async ValueTask<
        ExternalAssemblyReferencePlatformResult>
        ResolveFamilyAsync(
            PlatformAssemblyReferenceFamilyRoute family,
            AssemblyReferenceResolutionWorkLedger work,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.SourceOperation,
            1);
        PlatformHouseWorkBudget packageWork =
            AdmitPackageBackedWork(
                family.PlatformRequest.Work,
                work,
                retainAssemblies: false);
        long started = Stopwatch.GetTimestamp();
        PackagePlatformHouseResult<PackageReferenceRealization> result =
            await _platform.RealizeReferenceAsync(
                    family.PlatformRequest,
                    packageWork,
                    _issueOperation(
                        family.PlatformRequest,
                        packageWork))
                .ConfigureAwait(false);
        PlatformAssemblyReferenceSourceAttempt attempt =
            result switch
            {
                PackagePlatformHouseResult<
                    PackageReferenceRealization>.Succeeded sourceSuccess =>
                        PackagePlatformAssemblyReferenceResolver
                            .PrepareAttempt(
                                family.PlatformRequest,
                                sourceSuccess,
                                PlatformHouseCandidateIdentity.Create(
                                    OperationName(
                                        family.Target.Family,
                                        "package-reference"))),
                PackagePlatformHouseResult<
                    PackageReferenceRealization>.NotSucceeded terminal =>
                        PackagePlatformAssemblyReferenceResolver
                            .PrepareAttempt(terminal),
                _ => throw new InvalidOperationException(
                    "Unknown package-backed Platform reference result."),
            };
        int assemblies = 0;
        long bytes = 0;
        if (result
            is PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded realizationSuccess)
        {
            assemblies = realizationSuccess.Value.Libraries.Length;
            bytes = realizationSuccess.Value.Libraries.Sum(
                static library => library.TotalContentLength);
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.TransferBytes,
                bytes);
            Charge(
                work,
                AssemblyReferenceResolutionWorkKind.RealizedAssembly,
                assemblies);
        }
        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    family.PlatformRequest,
                    [attempt],
                    new(
                        sourceOperations: 1,
                        targetCandidates: 0,
                        assemblies,
                        xmlDocuments: 0,
                        portablePdbs: 0,
                        sourceDocuments: 0,
                        bytes,
                        forwardingHops: 0,
                        targetComparisons: 0,
                        Stopwatch.GetElapsedTime(started)))
                .ConfigureAwait(false);
        return new(
            family.PlatformRequest,
            outcome);
    }

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
                OperationName(target.Family, "population")),
            new PlatformTargetDemand.Exact(target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    OperationName(target.Family, "population"))),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Implementation),
            sources,
            new PlatformHouseWorkBudget(
                maxSourceOperations: 1,
                maxTargetCandidates: 0,
                maxAssemblies:
                    BrowserInspectionScope.MaxAssembliesPerRole,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes:
                    BrowserInspectionScope.MaxRetainedImageBytes,
                maxForwardingHops: 0,
                maxDuration: _operationTimeout),
            cancellationToken);
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.SourceOperation,
            1);
        PlatformHouseWorkBudget packageWork =
            AdmitPackageBackedWork(
                request.Work,
                work,
                retainAssemblies: true);
        long started = Stopwatch.GetTimestamp();
        PackagePlatformHouseResult<PackageImplementationRealization>
            result =
                await _platform.RealizeImplementationAsync(
                        request,
                        packageWork,
                        WorkspaceContextLoader
                            .RepresentativeRuntimeIdentifier,
                        _issueOperation(request, packageWork))
                    .ConfigureAwait(false);
        if (result
            is not PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded success)
        {
            throw new InvalidOperationException(
                $"Package-backed Platform implementation realization failed ({result.Contribution.Kind}).");
        }
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.TransferBytes,
            success.Value.ConsumedBytes);
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.RealizedAssembly,
            success.Value.Libraries.Length);
        Charge(
            work,
            AssemblyReferenceResolutionWorkKind.RetainedAssemblyBytes,
            success.Value.ConsumedBytes);
        return await PackagePlatformLibraryMaterializer
            .MaterializeImplementationPopulationAsync(
                request,
                success,
                new(
                    success.Value.Frameworks.Length,
                    targetCandidates: 0,
                    success.Value.Libraries.Length,
                    xmlDocuments: 0,
                    portablePdbs: 0,
                    sourceDocuments: 0,
                    success.Value.ConsumedBytes,
                    forwardingHops: 0,
                    targetComparisons: 0,
                    Stopwatch.GetElapsedTime(started)))
            .ConfigureAwait(false);
    }
}
