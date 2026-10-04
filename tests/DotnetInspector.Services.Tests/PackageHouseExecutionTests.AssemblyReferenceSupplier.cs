using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task
        AssemblyReferenceRouteProjectionBindsScopeAndOrdersTraversalRoutes()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    IncludeManifest: true));
        PackageRootBinding rootBinding =
            CallGraphRootBinding(
                ("contoso.first", RouteVersion),
                ("contoso.second", RouteVersion));
        PackageDependencyTraversalOutcome traversal =
            await CompleteSupplierTraversalAsync(
                environment,
                rootBinding);
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            executions = PreparedRouteExecutions(traversal);
        ResolutionEnvironment resolution =
            await ResolutionEnvironment.CreateAsync();

        var completed = Assert.IsType<
            PackageAssemblyReferenceRouteProjectionOutcome.Completed>(
                PackageAssemblyReferenceRouteProjection.Project(
                    new(
                        resolution.Generation,
                        resolution.FocalScope,
                        traversal,
                        rootOccurrenceIndex: 0,
                        [executions[1], executions[0]])));

        Assert.Same(resolution.Generation, completed.Receipt.Generation);
        Assert.Same(resolution.FocalScope, completed.Receipt.FocalScope);
        Assert.Same(traversal, completed.Receipt.Traversal);
        Assert.Equal(
            [0, 1],
            completed.Receipt.Routes.Select(
                static route => route.Subject.EdgeIndex));
        Assert.All(
            completed.Receipt.Routes,
            static route => Assert.Equal(
                PackageAssemblyReferenceRouteDisposition.PackageCandidate,
                route.Disposition));
    }

    [Fact]
    public async Task
        AssemblyReferenceRouteProjectionRejectsMissingOrDuplicateExecutions()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    IncludeManifest: true));
        PackageDependencyTraversalOutcome traversal =
            await CompleteSupplierTraversalAsync(
                environment,
                CallGraphRootBinding(
                    ("contoso.first", RouteVersion),
                    ("contoso.second", RouteVersion)));
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            executions = PreparedRouteExecutions(traversal);
        PackageDependencyTraversalOutcome foreignTraversal =
            await CompleteSupplierTraversalAsync(
                environment,
                CallGraphRootBinding(("contoso.foreign", RouteVersion)));
        PackageDependencyEdgeRealizationExecution foreignExecution =
            Assert.Single(PreparedRouteExecutions(foreignTraversal));
        ResolutionEnvironment resolution =
            await ResolutionEnvironment.CreateAsync();

        Assert.Throws<ArgumentException>(
            () => PackageAssemblyReferenceRouteProjection.Project(
                new(
                    resolution.Generation,
                    resolution.FocalScope,
                    traversal,
                    rootOccurrenceIndex: 0,
                    [executions[0]])));
        Assert.Throws<ArgumentException>(
            () => PackageAssemblyReferenceRouteProjection.Project(
                new(
                    resolution.Generation,
                    resolution.FocalScope,
                    traversal,
                    rootOccurrenceIndex: 0,
                    [executions[0], executions[0], executions[1]])));
        Assert.Throws<ArgumentException>(
            () => PackageAssemblyReferenceRouteProjection.Project(
                new(
                    resolution.Generation,
                    resolution.FocalScope,
                    traversal,
                    rootOccurrenceIndex: 0,
                    [executions[0], executions[1], foreignExecution])));
    }

    [Fact]
    public async Task
        AssemblyReferenceRouteProjectionRejectsForeignFocalScope()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    IncludeManifest: true));
        PackageDependencyTraversalOutcome traversal =
            await CompleteSupplierTraversalAsync(
                environment,
                CallGraphRootBinding(("contoso.route", RouteVersion)));
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            executions = PreparedRouteExecutions(traversal);
        ResolutionEnvironment generation =
            await ResolutionEnvironment.CreateAsync();
        ResolutionEnvironment foreignScope =
            await ResolutionEnvironment.CreateAsync();

        Assert.Throws<ArgumentException>(
            () => new PackageAssemblyReferenceRouteProjectionRequest(
                generation.Generation,
                foreignScope.FocalScope,
                traversal,
                rootOccurrenceIndex: 0,
                executions));
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierSelectsExactPackageBeforeLowerTiers()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        string exactPackage = identity.Name;
        const string LowerPackage = "unrelated.supplier";
        byte[] image = File.ReadAllBytes(CallGraphTargetPath);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [exactPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                            [LowerPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (exactPackage, RouteVersion),
                (LowerPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.ExactPackageId,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            exactPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        Assert.Equal(identity, selected.Selection.Assembly.Identity);
        Assert.Equal(
            [exactPackage.ToLowerInvariant()],
            environment.Clients[0].PayloadPackageIds
                .Select(static package => package.ToLowerInvariant())
                .Distinct());
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierUsesCompleteInventoryForNoAffinityPackage()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string EmptyPackage = "unrelated.empty";
        const string SupplierPackage = "xunit.extensibility.core";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [EmptyPackage] =
                            [
                                (
                                    "lib/net11.0/Unrelated.Empty.dll",
                                    File.ReadAllBytes(
                                        CallGraphCallerPath)),
                            ],
                            [SupplierPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    File.ReadAllBytes(
                                        CallGraphTargetPath)),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (EmptyPackage, RouteVersion),
                (SupplierPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.SelectedFileName,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            SupplierPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        Assert.Equal(
            [EmptyPackage, SupplierPackage],
            environment.Clients[0].PayloadPackageIds.Select(
                static package => package.ToLowerInvariant()));
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierSelectsPackageFamilyPrefixBeforeNoAffinity()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string PrefixPackage = "ILInspector.Analysis";
        const string NoAffinityPackage = "unrelated.supplier";
        byte[] image = File.ReadAllBytes(CallGraphTargetPath);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [PrefixPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                            [NoAffinityPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (PrefixPackage, RouteVersion),
                (NoAffinityPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.PackageFamilyPrefix,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            PrefixPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierContinuesAfterExactIdentityMiss()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string LowerPackage = "unrelated.supplier";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [identity.Name] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    File.ReadAllBytes(
                                        CallGraphTargetV2Path)),
                            ],
                            [LowerPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    File.ReadAllBytes(
                                        CallGraphTargetPath)),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (identity.Name, RouteVersion),
                (LowerPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var selected = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.True(selected.ObservedNameOwnedMiss);
        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.SelectedFileName,
            selected.Selection.Evidence.Tier);
        Assert.Equal(
            LowerPackage,
            selected.Selection.Evidence.Route.Candidate.Coordinate
                .PackageId,
            ignoreCase: true);
        Assert.Contains(
            selected.EvaluatedCandidates,
            evidence =>
                evidence.Tier
                    == PackageAssemblyReferenceSupplierTier.ExactPackageId
                && evidence.OwnsSimpleName
                && !evidence.MatchesRequest);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierReportsSameTierAmbiguity()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        const string FirstPackage = "first.supplier";
        const string SecondPackage = "second.supplier";
        byte[] image = File.ReadAllBytes(CallGraphTargetPath);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntriesByPackageId:
                        new Dictionary<
                            string,
                            IReadOnlyList<(
                                string EntryPath,
                                byte[] Content)>>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            [FirstPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                            [SecondPackage] =
                            [
                                (
                                    $"lib/net11.0/{identity.Name}.dll",
                                    image),
                            ],
                        },
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (FirstPackage, RouteVersion),
                (SecondPackage, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var ambiguous = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Ambiguous>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.Equal(
            PackageAssemblyReferenceSupplierTier.SelectedFileName,
            ambiguous.Tier);
        Assert.Equal(2, ambiguous.Candidates.Length);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierReportsAssemblyByteLimit()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            $"lib/net11.0/{identity.Name}.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ],
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                new PackageAssemblyReferenceSupplierLimits
                {
                    MaxAssemblyBytes = 1,
                },
                (identity.Name, RouteVersion));
        var store = new InMemoryPackageStore();
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Incomplete>(
                await association.ResolveAsync(
                    BindingRequest(identity),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse((_, _) => store),
                    operation));

        Assert.Contains(
            "assembly-byte limit",
            incomplete.Reason,
            StringComparison.Ordinal);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierReusesFileListAndDecodedMember()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        string packageId = identity.Name;
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            $"lib/net11.0/{identity.Name}.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ],
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (packageId, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);
        AssemblyBindingRequest request = BindingRequest(identity);

        var first = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    request,
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));
        var second = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Selected>(
                await association.ResolveAsync(
                    request,
                    AssemblyBindingSelection.NameNotOwned(),
                    house,
                    operation));

        Assert.Same(
            first.Selection.Assembly,
            second.Selection.Assembly);
        Assert.Equal(1, environment.Clients[0].PayloadRequests);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        AssemblyReferenceSupplierExcludesPrunedPackageWithoutAcquisition()
    {
        const string PackageId = "system.text.json";
        const string PackageVersion = "10.0.0";
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [PackageVersion],
                    IncludeManifest: true));
        PackageRootBinding rootBinding =
            CallGraphRootBinding((PackageId, PackageVersion));
        PackageDependencyTraversalOutcome traversal =
            await CompleteSupplierTraversalAsync(
                environment,
                rootBinding);
        var platformTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net11.0",
                    NuGetVersion.Parse("11.0.0")),
                [$"{PackageId}|11.0.0"]);
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact(
                        "net11.0",
                        platformTarget: platformTarget),
                    inventory));
        ResolutionEnvironment resolution =
            await ResolutionEnvironment.CreateAsync(
                PlatformFamily.DotNetRuntime);
        PackageAssemblyReferenceSupplierAssociation association =
            PackageAssemblyReferenceSupplierAssociation.Create(
                new PackageAssemblyReferenceSupplierAssociationRequest(
                    resolution.Generation,
                    resolution.FocalScope,
                    traversal,
                    rootOccurrenceIndex: 0,
                    [execution]));
        var projected = Assert.IsType<
            PackageAssemblyReferenceRouteProjectionOutcome.Completed>(
                association.RouteProjection);
        PackageAssemblyReferenceRouteOccurrence route =
            Assert.Single(projected.Receipt.Routes);
        Assert.Equal(
            PackageAssemblyReferenceRouteDisposition.PlatformDelegated,
            route.Disposition);
        Assert.Same(execution, route.Execution);
        var bindingRequest = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(CallGraphTargetIdentity()),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);
        var requestOrigin = new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create(
                "delegated-package-route"));
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create(
                "delegated-package-route-sources"),
            PlatformSourcePolicyGeneration.Create(
                "delegated-package-route-generation"),
            [
                new(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [
                        PlatformSourceCapabilityIdentity.Create(
                            "delegated-package-route-source"),
                    ]),
            ]);
        var metadata =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                bindingRequest,
                "delegated-package-binding");
        var prerequisites = new PlatformAssemblyReferenceRoute(
            metadata.Identity,
            platformTarget,
            requestOrigin,
            sources.Identity,
            sources.Generation);
        var platformRequest = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "delegated-package-binding-request"),
            new PlatformTargetDemand.Exact(platformTarget),
            requestOrigin,
            new PlatformHouseOperation.ResolveAssemblyReference
                .WithPrerequisites<PlatformAssemblyReferenceRoute>(
                    metadata,
                    new(
                        prerequisites,
                        "delegated-package-binding-route"),
                    PlatformViewDemand.Reference),
            sources,
            new(
                maxSourceOperations: 1,
                maxTargetCandidates: 1,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 1,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)));
        MemberCallGraphPlatformPopulationScope population =
            Assert.Single(
                resolution.FocalScope.PlatformPopulations);
        PackageHouseSettlement.Acquired foreignAcquired =
            await ExecuteFrameworkReferencesAsync(
                PackageHouseTargetContext.Exact("net8.0"),
                FrameworkArchive(
                    """
                    <group targetFramework="net8.0">
                      <frameworkReference name="Microsoft.NETCore.App" />
                    </group>
                    """,
                    ($"lib/net8.0/{MaterializedPackageId}.dll", [])));
        var foreignFramework = Assert.IsType<
            PackageHouseFrameworkReferenceOutcome.Selected>(
                PackageHouseFrameworkReferenceProjection.Project(
                    foreignAcquired));
        RealizedPackageDependencyContext foreignRoot =
            await RouteRootContextAsync(
                Assert.IsType<
                    PackageHouseRootContributionOutcome.Contributed>(
                        PackageHouseRootContributionAdapter.Create(
                            foreignAcquired))
                    .Contribution.Binding);
        var foreignEligibility =
            new PlatformAssemblyReferenceFamilyEligibility
                .PackageFrameworkReference(
                    foreignFramework.Evidence,
                    Assert.Single(
                        foreignFramework.Evidence.Occurrences),
                    foreignRoot);
        Assert.Throws<ArgumentException>(
            "families",
            () => new PlatformAssemblyReferenceExternalRoute(
                BindingRequest(CallGraphTargetIdentity()),
                resolution.Generation,
                resolution.FocalScope,
                [
                    new(
                        [
                            new PlatformAssemblyReferenceFamilyEligibility
                                .WorkspacePopulation(population),
                            foreignEligibility,
                        ],
                        platformRequest),
                ],
                projected.Receipt));
        var platformRoute =
            new PlatformAssemblyReferenceExternalRoute(
                BindingRequest(CallGraphTargetIdentity()),
                resolution.Generation,
                resolution.FocalScope,
                [
                    new(
                        [
                            new PlatformAssemblyReferenceFamilyEligibility
                                .WorkspacePopulation(population),
                        ],
                        platformRequest),
                ],
                projected.Receipt);
        Assert.Same(
            route,
            Assert.Single(platformRoute.DelegatedPackageRoutes));
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                execution.Request,
                TestContext.Current.CancellationToken);

        var missing = Assert.IsType<
            PackageAssemblyReferenceSupplierOutcome.Missing>(
                await association.ResolveAsync(
                    BindingRequest(CallGraphTargetIdentity()),
                    AssemblyBindingSelection.NameNotOwned(),
                    environment.CreateContentHouse(
                        (_, _) => new InMemoryPackageStore()),
                    operation));

        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
        Assert.Empty(environment.Clients[0].PayloadPackageIds);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        ExternalAssemblyReferenceSupplierDoesNotInvokePlatformAfterPackageSelection()
    {
        AssemblyReferenceIdentity identity = CallGraphTargetIdentity();
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            $"lib/net11.0/{identity.Name}.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ],
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association,
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions) =
            await SupplierAssociationAsync(
                environment,
                (identity.Name, RouteVersion));
        var store = new InMemoryPackageStore();
        PackageHouse house = environment.CreateContentHouse(
            (_, _) => store);
        using PackageSourceOperationLease operation =
            environment.IssueOperation(
                executions[0].Request,
                TestContext.Current.CancellationToken);
        int platformCalls = 0;
        AssemblyBindingRequest request = BindingRequest(identity);
        var packageRoutes = Assert.IsType<
            PackageAssemblyReferenceRouteProjectionOutcome.Completed>(
                association.RouteProjection);
        var packageRoute =
            new PackageAssemblyReferenceExternalRoute(
                request,
                association,
                packageRoutes.Receipt);
        var charged =
            new Dictionary<AssemblyReferenceResolutionWorkKind, long>();

        var packageOwned = Assert.IsType<
            ExternalAssemblyReferenceSupplierOutcome.PackageOwned>(
                await ExternalAssemblyReferenceSupplierAssociation
                    .ExecuteAsync(
                        packageRoute,
                        AssemblyBindingSelection.NameNotOwned(),
                        house,
                        operation,
                        (_, _) =>
                        {
                            platformCalls++;
                            throw new InvalidOperationException(
                                "Platform must not run after Package selection.");
                        },
                        TestContext.Current.CancellationToken,
                        (kind, amount) =>
                            charged[kind] =
                                charged.GetValueOrDefault(kind) + amount));

        Assert.Equal(identity, packageOwned.Package.Selection.Assembly.Identity);
        Assert.Same(request, packageOwned.Request);
        Assert.Same(packageRoute, packageOwned.Route);
        Assert.Same(
            packageRoutes.Receipt,
            packageRoute.PackageRoutes);
        Assert.Equal(0, platformCalls);
        Assert.True(
            charged[
                AssemblyReferenceResolutionWorkKind
                    .PackageCandidateOperation] > 0);
        Assert.True(
            charged[
                AssemblyReferenceResolutionWorkKind.SourceOperation] > 0);
        Assert.Equal(
            1,
            charged[AssemblyReferenceResolutionWorkKind.Acquisition]);
        Assert.Equal(
            1,
            charged[
                AssemblyReferenceResolutionWorkKind.RealizedAssembly]);
        operation.Dispose();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        EmptyPlatformCompositionRetainsTypedNonParticipation()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        (
                            $"lib/net11.0/{MaterializedPackageId}.dll",
                            File.ReadAllBytes(CallGraphTargetPath)),
                    ],
                    IncludeManifest: true));
        (PackageAssemblyReferenceSupplierAssociation association, _) =
            await SupplierAssociationAsync(
                environment,
                (MaterializedPackageId, RouteVersion));
        var projected = Assert.IsType<
            PackageAssemblyReferenceRouteProjectionOutcome.Completed>(
                association.RouteProjection);
        AssemblyBindingRequest request =
            BindingRequest(CallGraphTargetIdentity());
        var nonParticipation =
            new PlatformAssemblyReferenceNonParticipationEvidence(
                projected.Receipt.Generation,
                projected.Receipt.FocalScope,
                projected.Receipt,
                "No exact Platform target is eligible.");
        var route = new PlatformAssemblyReferenceExternalRoute(
            request,
            projected.Receipt.Generation,
            projected.Receipt.FocalScope,
            [],
            projected.Receipt,
            nonParticipation);
        var familyCalls = 0;

        var missing = Assert.IsType<
            PlatformAssemblyReferenceBindingOutcome.Missing>(
                await PlatformAssemblyReferenceRouteAdapter.ExecuteAsync(
                    route,
                    (_, _) =>
                    {
                        familyCalls++;
                        throw new InvalidOperationException(
                            "An empty Platform composition has no family work.");
                    },
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, familyCalls);
        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
        Assert.Empty(missing.FamilyResults);
        Assert.Same(nonParticipation, route.NonParticipation);
        await environment.AssertRootSettledAsync();
    }

    private static async Task<(
        PackageAssemblyReferenceSupplierAssociation Association,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            Executions)> SupplierAssociationAsync(
        HouseEnvironment environment,
        params (string PackageId, string Version)[] dependencies)
        => await SupplierAssociationAsync(
            environment,
            limits: null,
            dependencies);

    private static async Task<(
        PackageAssemblyReferenceSupplierAssociation Association,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            Executions)> SupplierAssociationAsync(
        HouseEnvironment environment,
        PackageAssemblyReferenceSupplierLimits? limits,
        params (string PackageId, string Version)[] dependencies)
    {
        PackageRootBinding rootBinding =
            CallGraphRootBinding(dependencies);
        PackageDependencyTraversalOutcome traversal =
            await CompleteSupplierTraversalAsync(
                environment,
                rootBinding);
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            snapshot = PreparedRouteExecutions(traversal);
        ResolutionEnvironment resolution =
            await ResolutionEnvironment.CreateAsync();
        return (
            PackageAssemblyReferenceSupplierAssociation.Create(
                new PackageAssemblyReferenceSupplierAssociationRequest(
                    resolution.Generation,
                    resolution.FocalScope,
                    traversal,
                    rootOccurrenceIndex: 0,
                    snapshot,
                    limits)),
            snapshot);
    }

    private static ImmutableArray<
        PackageDependencyEdgeRealizationExecution>
        PreparedRouteExecutions(
            PackageDependencyTraversalOutcome traversal)
    {
        var executions =
            ImmutableArray.CreateBuilder<
                PackageDependencyEdgeRealizationExecution>();
        for (int edgeIndex = 0;
            edgeIndex < traversal.Edges.Length;
            edgeIndex++)
        {
            if (traversal.RootReachability[0].IsEdgeAdmitted(
                    edgeIndex,
                    out _)
                && traversal.Edges[edgeIndex].Authority
                    == PackageDependencyTraversalEdgeEmissionAuthority
                        .ResolvedCandidate)
            {
                executions.Add(
                    PackageDependencyEdgeRealizationQuery.Execute(
                        new PackageDependencyEdgeRealizationRequest(
                            traversal,
                            rootOccurrenceIndex: 0,
                            edgeIndex,
                            PackageHouseOperation.Create(
                                PackageHouseOperationProfile.Realize),
                            PackageHouseTargetContext.Exact(
                                traversal.TraversalTargetPolicy
                                    .TargetFramework))));
            }
        }

        return executions.ToImmutable();
    }

    private static async Task<PackageDependencyTraversalOutcome>
        CompleteSupplierTraversalAsync(
            HouseEnvironment environment,
            PackageRootBinding rootBinding)
    {
        var candidateSource =
            new AuthorizedPackageDependencyCandidateSource(
                environment.Authorization,
                environment.Root);
        PackageDependencyTraversalOutcome traversal =
            await PackageDependencyTraversalQuery.ExecuteAsync(
                new PackageDependencyTraversalRequest(
                    [
                        new PackageDependencyTraversalRootOccurrence(
                            await RouteRootContextAsync(rootBinding),
                            PackageDependencyTraversalExpansionAuthority
                                .RecursiveSources),
                    ],
                    TraversalTargetFrameworkPolicy.ProductDefault,
                    new PackageDependencyTraversalCandidateAdapter(
                        candidateSource),
                    new AuthorizedPackageDependencyManifestSource(
                        candidateSource),
                    new PackageDependencyTraversalWorkBudget(
                        maxManifestProjections: 8,
                        maxDeclarationResolutions: 8),
                    maxDepth: 2),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Complete,
            traversal.Roots[0].Completion);
        foreach (HouseSourceClient client in environment.Clients)
            client.ResetPayloadTracking();
        return traversal;
    }

    private static AssemblyReferenceIdentity CallGraphTargetIdentity()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(CallGraphTargetPath);
        return session.AssemblyIdentity();
    }

    private static string CallGraphTargetV2Path =>
        FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath();

    private static AssemblyBindingRequest BindingRequest(
        AssemblyReferenceIdentity identity) =>
        new(
            AssemblyBindingTarget.Reference(identity),
            AssemblyBindingOrigin.FromAssembly(
                ResolvedAssemblyReference.CreateFromPath(
                    CallGraphCallerPath,
                    AssemblyResolutionProvenance.Local(
                        "Package supplier association test"))),
            AssemblyResolutionScope.Any);

    private sealed record ResolutionEnvironment(
        AssemblyReferenceResolutionGenerationReceipt Generation,
        MemberCallGraphFocalScopeReceipt FocalScope)
    {
        internal static async Task<ResolutionEnvironment> CreateAsync(
            PlatformFamily? platformFamily = null)
        {
            ImmutableArray<WorkspaceRegistration> initialRegistrations =
                platformFamily is { } family
                    ?
                    [
                        new WorkspaceRegistration.Ecosystem(
                            new WorkspaceEcosystemRegistrationDeclaration(
                                WorkspaceEcosystemRegistrationId.Create(
                                    "ecosystem.runtime"),
                                namespaceRoots: [],
                                corePackages: [],
                                populations:
                                [
                                    new
                                        WorkspaceEcosystemPopulationDeclaration
                                            .Platform(
                                                new(family)),
                                ])),
                    ]
                    : [];
            await using var workspace = new InspectionWorkspace(
                initialRegistrations);
            var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync());
            var registrations =
                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot());
            MemberCallGraphFocalScopeReceipt focalScope =
                MemberCallGraphFocalScopeReceipt.CaptureEverything(
                    scope.Snapshot,
                    registrations.Revision);
            return new(
                AssemblyReferenceResolutionGenerationReceipt.Capture(
                    scope.Snapshot,
                    focalScope),
                focalScope);
        }
    }
}
