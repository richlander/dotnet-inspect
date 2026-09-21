using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyTraversalQueryTests
{
    [Fact]
    public async Task
        EdgeRealization_UsesTraversalTargetWithoutReselectingPollyRoot()
    {
        byte[] nupkg = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "RealizedPackageDependencyContext",
                "polly.core.8.8.0.nupkg"));
        RealizedPackageDependencyContext context =
            await RealizedContextAsync(
                nupkg,
                "polly.core",
                "8.8.0",
                "netstandard2.0");
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        var resolver = new StubCandidateResolver();
        PackageAcquisitionCandidate candidate = Pinned(
            issuer,
            authorization,
            "system.componentmodel.annotations",
            "4.5.0");
        resolver.SetResponse(
            "polly.core",
            "system.componentmodel.annotations",
            () => Resolved(candidate));
        foreach ((string dependency, string version) in new[]
                 {
                     ("microsoft.bcl.asyncinterfaces", "6.0.0"),
                     ("microsoft.bcl.timeprovider", "8.0.0"),
                     ("system.threading.tasks.extensions", "4.5.4"),
                 })
        {
            resolver.SetResponse(
                "polly.core",
                dependency,
                () => Resolved(
                    Pinned(
                        issuer,
                        authorization,
                        dependency,
                        version)));
        }

        PackageDependencyTraversalOutcome traversal = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    context,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources),
            ],
            resolver,
            new StubManifestAcquirer(),
            maxDepth: 1,
            traversalTargetPolicy:
                TraversalTargetFrameworkPolicy.ProductDefault);
        int edgeIndex = traversal.Edges.ToList().FindIndex(edge =>
            edge.Declaration.CanonicalPackageId
                == "system.componentmodel.annotations");
        var request = new PackageDependencyEdgeRealizationRequest(
            traversal,
            rootOccurrenceIndex: 0,
            edgeIndex,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net12.0"));

        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(request);

        Assert.Equal(1, execution.Subject.Distance);
        Assert.Same(context.Evidence, execution.Input.Root);
        Assert.Same(candidate, execution.Subject.Candidate);
        Assert.Same(
            candidate,
            Assert.IsType<PackageHouseDemand.Candidate>(
                execution.Request.Demand).Value);
        Assert.Equal(
            "net12.0",
            execution.Request.TargetContext!.RequestedFramework);
        Assert.Equal(
            ".NETStandard2.0",
            context.Evidence.Selection.SelectedFramework?.ToString());
        Assert.Equal(
            PackageHouseAssetSelectionKind.Compile,
            execution.Request.AssetSelection);
        Assert.Equal(
            PackageHouseLibraryHandoffMode.PackageOnly,
            execution.Request.LibraryHandoff);
        Assert.Null(execution.Pruning);
    }

    [Fact]
    public async Task
        EdgeRealization_ComposesPlatformPruningAgainstTraversalTarget()
    {
        RealizedPackageDependencyContext context =
            await RealizedContextAsync(
                "source",
                "1.0.0",
                Dependency(
                    "system.componentmodel.annotations",
                    "[1.0.0]"));
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate candidate = Pinned(
            issuer,
            authorization,
            "system.componentmodel.annotations",
            "1.0.0");
        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "source",
            "system.componentmodel.annotations",
            () => Resolved(candidate));
        PackageDependencyTraversalOutcome traversal = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    context,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources),
            ],
            resolver,
            new StubManifestAcquirer(),
            maxDepth: 1,
            traversalTargetPolicy:
                TraversalTargetFrameworkPolicy.ProductDefault);
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
                ["system.componentmodel.annotations|4.5.0"]);
        var request = new PackageDependencyEdgeRealizationRequest(
            traversal,
            rootOccurrenceIndex: 0,
            edgeIndex: 0,
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(
                "net12.0",
                platformTarget: platformTarget),
            inventory);

        PackageDependencyEdgeRealizationExecution execution =
            PackageDependencyEdgeRealizationQuery.Execute(request);

        Assert.NotNull(execution.Pruning);
        Assert.True(execution.DelegatesToPlatform);
        Assert.Same(execution.Request, execution.Pruning.Request);
        Assert.Same(platformTarget, execution.Pruning.Target);
        Assert.Equal(
            PlatformSubsumption.Subsumed,
            execution.Pruning.Supply.Subsumption);
    }

    [Fact]
    public async Task
        EdgeRealization_PreservesSameCoordinateCandidateCorrespondence()
    {
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate first = Pinned(
            new PackageAcquisitionCandidateIssuer(),
            authorization,
            "shared",
            "1.0.0");
        PackageAcquisitionCandidate second = Pinned(
            new PackageAcquisitionCandidateIssuer(),
            authorization,
            "shared",
            "1.0.0");
        PackageDependencyTraversalRootOccurrence rootA = Root(
            "roota",
            "1.0.0",
            Dependency("shared", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalRootOccurrence rootB = Root(
            "rootb",
            "1.0.0",
            Dependency("shared", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "shared", () => Resolved(first));
        resolver.SetResponse("rootb", "shared", () => Resolved(second));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(
            first,
            ManifestBytes("shared", "1.0.0", ""));
        acquirer.SetManifest(
            second,
            ManifestBytes("shared", "1.0.0", ""));
        PackageDependencyTraversalOutcome traversal = await ExecuteAsync(
            [rootA, rootB],
            resolver,
            acquirer);
        int firstEdgeIndex = traversal.Edges.ToList().FindIndex(edge =>
            edge.SourceCoordinate.PackageId == "roota");
        int secondEdgeIndex = traversal.Edges.ToList().FindIndex(edge =>
            edge.SourceCoordinate.PackageId == "rootb");
        PackageHouseOperation operation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize);

        PackageDependencyEdgeRealizationExecution firstExecution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    firstEdgeIndex,
                    operation,
                    PackageHouseTargetContext.Exact("net12.0")));
        PackageDependencyEdgeRealizationExecution secondExecution =
            PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 1,
                    secondEdgeIndex,
                    operation,
                    PackageHouseTargetContext.Exact("net12.0")));

        Assert.Same(first, firstExecution.Subject.Candidate);
        Assert.Same(
            first,
            Assert.IsType<PackageHouseDemand.Candidate>(
                firstExecution.Request.Demand).Value);
        Assert.Same(second, secondExecution.Subject.Candidate);
        Assert.Same(
            second,
            Assert.IsType<PackageHouseDemand.Candidate>(
                secondExecution.Request.Demand).Value);
        Assert.NotSame(
            firstExecution.Subject.Candidate,
            secondExecution.Subject.Candidate);
    }

    [Fact]
    public async Task
        EdgeRealization_RejectsNonCandidateAndMismatchedTargetRequests()
    {
        RealizedPackageDependencyContext context =
            await RealizedContextAsync(
                "source",
                "1.0.0",
                Dependency("target", "[1.0.0]"));
        PackageDependencyTraversalOutcome traversal = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    context,
                    PackageDependencyTraversalExpansionAuthority
                        .DirectDeclarationsOnly),
            ],
            new StubCandidateResolver(),
            new StubManifestAcquirer());
        PackageHouseOperation operation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize);

        Assert.Throws<ArgumentException>(
            () => PackageDependencyEdgeRealizationQuery.Execute(
                new PackageDependencyEdgeRealizationRequest(
                    traversal,
                    rootOccurrenceIndex: 0,
                    edgeIndex: 0,
                    operation,
                    PackageHouseTargetContext.Exact("net12.0"))));
        Assert.Throws<ArgumentException>(
            () => new PackageDependencyEdgeRealizationRequest(
                traversal,
                rootOccurrenceIndex: 0,
                edgeIndex: 0,
                operation,
                PackageHouseTargetContext.Exact("net11.0")));
    }
}
