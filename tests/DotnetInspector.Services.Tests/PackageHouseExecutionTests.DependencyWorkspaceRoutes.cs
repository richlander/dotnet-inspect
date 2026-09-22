using System.Collections.Immutable;
using System.Text;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    private const string RouteRootPackageId = "route.root";
    private const string RoutePackageOne = "route.package.one";
    private const string RoutePlatformPackage = "route.platform.package";
    private const string RoutePackageTwo = "route.package.two";
    private const string RouteSuppliedPackage = "route.supplied";
    private const string RouteVersion = "1.0.0";

    [Fact]
    public async Task
        DependencyWorkspaceRoutesBatchPackagesAndRetainPlatformDelegation()
    {
        byte[] assembly = File.ReadAllBytes(
            typeof(PackageHouseExecutionTests).Assembly.Location);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        ("lib/net11.0/Route.Dependency.dll", assembly),
                    ]));
        PackageRootBinding rootBinding = RouteRootBinding(
            RouteRootPackageId,
            (RoutePackageOne, RouteVersion),
            (RoutePlatformPackage, RouteVersion),
            (RoutePackageTwo, RouteVersion),
            (RouteSuppliedPackage, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageRootBinding suppliedBinding =
            RouteRootBinding(RouteSuppliedPackage);
        RealizedPackageDependencyContext suppliedContext =
            await RouteRootContextAsync(suppliedBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources),
                new PackageDependencyTraversalRootOccurrence(
                    suppliedContext,
                    PackageDependencyTraversalExpansionAuthority
                        .DirectDeclarationsOnly,
                    PackageDependencyTraversalRootRecurrenceAuthority
                        .ExactCoordinate));
        Assert.Equal(
            "net12.0",
            traversal.TraversalTargetPolicy.TargetFramework);
        var platformTarget = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net12.0"),
            PlatformVersion.Parse("12.0.0"));
        PlatformPruneInventory inventory =
            PlatformPruneInventory.FromExactFamily(
                new PlatformPruneTarget(
                    "Microsoft.NETCore.App",
                    "net12.0",
                    NuGetVersion.Parse("12.0.0")),
                [$"{RoutePlatformPackage}|{RouteVersion}"]);
        PackageHouse house = environment.CreateHouse(
            (_, _) => new InMemoryPackageStore());
        var realizations =
            ImmutableArray.CreateBuilder<
                PackageDependencyEdgeRealizationEvidence>();
        for (int edgeIndex = 0;
            edgeIndex < traversal.Edges.Length;
            edgeIndex++)
        {
            if (traversal.Edges[edgeIndex].Authority
                != PackageDependencyTraversalEdgeEmissionAuthority
                    .ResolvedCandidate)
            {
                continue;
            }

            PackageDependencyEdgeRealizationExecution execution =
                PackageDependencyEdgeRealizationQuery.Execute(
                    new PackageDependencyEdgeRealizationRequest(
                        traversal,
                        rootOccurrenceIndex: 0,
                        edgeIndex,
                        PackageHouseOperation.Create(
                            PackageHouseOperationProfile.Realize),
                        PackageHouseTargetContext.Exact(
                            "net12.0",
                            platformTarget: platformTarget),
                        inventory));
            realizations.Add(
                await execution.ExecuteAsync(
                    house,
                    environment.IssueOperation(
                        execution.Request,
                        TestContext.Current.CancellationToken)));
        }

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted = Assert.IsType<
            WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding, suppliedBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        WorkspacePackageOccurrenceDescriptor suppliedBefore =
            Assert.IsType<WorkspacePackageOccurrenceDescriptor>(
                rooted.FindPackageOccurrence(suppliedBinding));

        PackageDependencyWorkspaceRouteOutcome.Completed completed =
            Assert.IsType<PackageDependencyWorkspaceRouteOutcome.Completed>(
                await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        workspace,
                        rooted,
                        traversal,
                        [rootBinding, suppliedBinding],
                        realizations.ToImmutable(),
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));

        Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            completed.ScopeOperation);
        Assert.Equal(4, completed.Scope.Packages.Length);
        Assert.Equal(
            "netstandard2.0",
            completed.Scope.Packages[0].Occurrence.Package
                .RequestedTargetFramework);
        PackageDependencyWorkspaceDestination.Package[] realizedPackages =
        [
            .. completed.Destinations
                .OfType<PackageDependencyWorkspaceDestination.Package>()
                .Where(route => route.Source
                    == PackageDependencyWorkspacePackageRouteSource
                        .ResolvedCandidate),
        ];
        Assert.Equal(2, realizedPackages.Length);
        Assert.All(
            realizedPackages,
            route =>
            {
                Assert.Equal(
                    PackageDependencyWorkspacePackageRouteSource
                        .ResolvedCandidate,
                    route.Source);
                Assert.NotNull(route.Realization);
                Assert.Equal(
                    "net12.0",
                    route.Occurrence.Occurrence.Package
                        .RequestedTargetFramework);
                Assert.Equal(
                    "net11.0",
                    route.Occurrence.Occurrence.Package
                        .SelectedTargetFramework);
            });
        PackageDependencyWorkspaceDestination.Package supplied =
            Assert.Single(
                completed.Destinations
                    .OfType<PackageDependencyWorkspaceDestination.Package>(),
                route => route.Source
                    == PackageDependencyWorkspacePackageRouteSource
                        .SuppliedRoot);
        WorkspacePackageOccurrenceDescriptor suppliedAfter =
            Assert.IsType<WorkspacePackageOccurrenceDescriptor>(
                completed.Scope.FindPackageOccurrence(suppliedBinding));
        Assert.NotSame(suppliedBefore, suppliedAfter);
        Assert.Same(suppliedAfter, supplied.Occurrence);
        Assert.Null(supplied.Realization);
        PackageDependencyWorkspaceDestination.Platform platform =
            Assert.Single(
                completed.Destinations
                    .OfType<
                        PackageDependencyWorkspaceDestination.Platform>());
        Assert.Equal(
            RoutePlatformPackage,
            platform.Subject.Edge.Declaration.CanonicalPackageId);
        Assert.Same(
            platform.Realization.PlatformDelegation,
            platform.Delegation);
        Assert.Equal(2, environment.Clients[0].PayloadRequests);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyWorkspaceRoutesCanonicalizeRepeatedExactCandidate()
    {
        byte[] assembly = File.ReadAllBytes(
            typeof(PackageHouseExecutionTests).Assembly.Location);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadContentEntries:
                    [
                        ("lib/net11.0/Route.Dependency.dll", assembly),
                    ]));
        PackageRootBinding firstRoot =
            RouteRootBinding(
                "route.root.first",
                (RoutePackageOne, RouteVersion));
        PackageRootBinding secondRoot =
            RouteRootBinding(
                "route.root.second",
                (RoutePackageOne, RouteVersion));
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    await RouteRootContextAsync(firstRoot),
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources),
                new PackageDependencyTraversalRootOccurrence(
                    await RouteRootContextAsync(secondRoot),
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageHouse house = environment.CreateHouse(
            (_, _) => new InMemoryPackageStore());
        var realizations =
            ImmutableArray.CreateBuilder<
                PackageDependencyEdgeRealizationEvidence>();
        for (var rootIndex = 0;
            rootIndex < traversal.Roots.Length;
            rootIndex++)
        {
            for (var edgeIndex = 0;
                edgeIndex < traversal.Edges.Length;
                edgeIndex++)
            {
                if (!traversal.RootReachability[rootIndex]
                        .IsEdgeAdmitted(edgeIndex, out _)
                    || traversal.Edges[edgeIndex].Authority
                        != PackageDependencyTraversalEdgeEmissionAuthority
                            .ResolvedCandidate)
                {
                    continue;
                }

                PackageDependencyEdgeRealizationExecution execution =
                    PackageDependencyEdgeRealizationQuery.Execute(
                        new PackageDependencyEdgeRealizationRequest(
                            traversal,
                            rootIndex,
                            edgeIndex,
                            PackageHouseOperation.Create(
                                PackageHouseOperationProfile.Realize),
                            PackageHouseTargetContext.Exact(
                                "net12.0")));
                realizations.Add(
                    await execution.ExecuteAsync(
                        house,
                        environment.IssueOperation(
                            execution.Request,
                            TestContext.Current.CancellationToken)));
            }
        }

        Assert.Equal(2, realizations.Count);
        Assert.Equal(
            realizations[0].Subject.Candidate.Correspondence,
            realizations[1].Subject.Candidate.Correspondence);
        Assert.NotSame(
            Assert.IsType<
                    PackageHouseRootContributionOutcome.Contributed>(
                    realizations[0].RootContribution)
                .Contribution.Binding,
            Assert.IsType<
                    PackageHouseRootContributionOutcome.Contributed>(
                    realizations[1].RootContribution)
                .Contribution.Binding);

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [firstRoot, secondRoot],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        PackageDependencyWorkspaceRouteOutcome.Completed completed =
            Assert.IsType<PackageDependencyWorkspaceRouteOutcome.Completed>(
                await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        workspace,
                        rooted,
                        traversal,
                        [firstRoot, secondRoot],
                        realizations.ToImmutable(),
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));

        Assert.Equal(3, completed.Scope.Packages.Length);
        PackageDependencyWorkspaceDestination.Package[] destinations =
        [
            .. completed.Destinations.OfType<
                PackageDependencyWorkspaceDestination.Package>(),
        ];
        Assert.Equal(2, destinations.Length);
        Assert.Same(
            destinations[0].Occurrence,
            destinations[1].Occurrence);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task
        DependencyWorkspaceRoutesDoNotPublishPartialBatchWhenPreparationFails()
    {
        await using HouseEnvironment environment =
            HouseEnvironment.CreateForAnyPackage(
                new SourceBehavior(
                    [RouteVersion],
                    PayloadEntries:
                    [
                        "lib/net11.0/Broken.Dependency.dll",
                    ]));
        PackageRootBinding rootBinding =
            RouteRootBinding(
                RouteRootPackageId,
                (RoutePackageOne, RouteVersion));
        RealizedPackageDependencyContext rootContext =
            await RouteRootContextAsync(rootBinding);
        PackageDependencyTraversalOutcome traversal =
            await RouteTraversalAsync(
                environment,
                new PackageDependencyTraversalRootOccurrence(
                    rootContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources));
        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Realize),
                    PackageHouseTargetContext.Exact("net12.0")));
        PackageDependencyEdgeRealizationEvidence realization =
            await execution.ExecuteAsync(
                environment.CreateHouse(
                    (_, _) => new InMemoryPackageStore()),
                environment.IssueOperation(
                    execution.Request,
                    TestContext.Current.CancellationToken));
        Assert.IsType<PackageHouseRootContributionOutcome.Contributed>(
            realization.RootContribution);

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty = await CurrentScopeAsync(workspace);
        WorkspaceScopeSnapshot rooted = Assert.IsType<
            WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [rootBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;

        PackageDependencyWorkspaceRouteOutcome.NotCommitted notCommitted =
            Assert.IsType<
                PackageDependencyWorkspaceRouteOutcome.NotCommitted>(
                await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        workspace,
                        rooted,
                        traversal,
                        [rootBinding],
                        [realization],
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));

        WorkspaceScopeOperationResult.Failed failed =
            Assert.IsType<WorkspaceScopeOperationResult.Failed>(
                notCommitted.ScopeOperation);
        Assert.Equal(
            ArtifactRootFailure.PreparationFailed,
            failed.Failure);
        Assert.Same(rooted.Revision, failed.Snapshot.Revision);
        Assert.Single(failed.Snapshot.Packages);
        Assert.Same(
            failed.Snapshot,
            await CurrentScopeAsync(workspace));
        await environment.AssertRootSettledAsync();
    }

    private static async Task<PackageDependencyTraversalOutcome>
        RouteTraversalAsync(
            HouseEnvironment environment,
            params PackageDependencyTraversalRootOccurrence[] roots)
    {
        var candidateSource =
            new AuthorizedPackageDependencyCandidateSource(
                environment.Authorization,
                environment.Root);
        return await PackageDependencyTraversalQuery.ExecuteAsync(
            new PackageDependencyTraversalRequest(
                [.. roots],
                TraversalTargetFrameworkPolicy.ProductDefault,
                new PackageDependencyTraversalCandidateAdapter(
                    candidateSource),
                new UnexpectedManifestAcquirer(),
                new PackageDependencyTraversalWorkBudget(
                    maxManifestProjections: 3,
                    maxDeclarationResolutions: 3),
                maxDepth: 1),
            TestContext.Current.CancellationToken);
    }

    private static async ValueTask<RealizedPackageDependencyContext>
        RouteRootContextAsync(PackageRootBinding binding) =>
        Assert.IsType<RealizedPackageDependencyContextResult.Available>(
            await RealizedPackageDependencyContextQuery.ExecuteAsync(
                binding,
                TestContext.Current.CancellationToken)).Context;

    private static async ValueTask<WorkspaceScopeSnapshot> CurrentScopeAsync(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;

    private static PackageRootBinding RouteRootBinding(
        string rootPackageId,
        params (string PackageId, string Version)[] dependencies)
    {
        string dependencyXml = string.Join(
            Environment.NewLine,
            dependencies.Select(
                dependency =>
                    $"""<dependency id="{dependency.PackageId}" version="[{dependency.Version}]" />"""));
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{rootPackageId}}</id>
                <version>{{RouteVersion}}</version>
                <authors>dotnet-inspect</authors>
                <description>Workspace route composition fixture.</description>
                <dependencies>
                  <group targetFramework="netstandard2.0">
                    {{dependencyXml}}
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        byte[] archive = TestPackageArchive.Create(
            (
                $"{rootPackageId}.nuspec",
                manifest),
            (
                "lib/netstandard2.0/Route.Root.dll",
                File.ReadAllBytes(
                    typeof(PackageHouseExecutionTests).Assembly.Location)));
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(
                rootPackageId,
                RouteVersion),
            new InMemoryPackageContent(
                archive,
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(
            payload,
            "netstandard2.0");
    }
}
