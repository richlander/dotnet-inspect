using System.IO.Compression;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyTraversalQueryTests
{
    [Fact]
    public async Task
        WorkspaceRoutesReuseExactSuppliedRootOccurrence()
    {
        (PackageRootBinding sourceBinding,
            RealizedPackageDependencyContext sourceContext) =
            await WorkspaceRouteRootAsync(
                "route.source",
                "1.0.0",
                Dependency("route.supplied", "[1.0.0]"));
        (PackageRootBinding suppliedBinding,
            RealizedPackageDependencyContext suppliedContext) =
            await WorkspaceRouteRootAsync(
                "route.supplied",
                "1.0.0",
                dependenciesXml: "");
        PackageDependencyTraversalOutcome traversal = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    sourceContext,
                    PackageDependencyTraversalExpansionAuthority
                        .RecursiveSources),
                new PackageDependencyTraversalRootOccurrence(
                    suppliedContext,
                    PackageDependencyTraversalExpansionAuthority
                        .DirectDeclarationsOnly,
                    PackageDependencyTraversalRootRecurrenceAuthority
                        .ExactCoordinate),
            ],
            new StubCandidateResolver(),
            new StubManifestAcquirer(),
            maxDepth: 1);
        PackageDependencyTraversalEdge suppliedEdge =
            Assert.Single(traversal.Edges);
        Assert.Equal(
            PackageDependencyTraversalEdgeEmissionAuthority.SuppliedRoot,
            suppliedEdge.Authority);

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [sourceBinding, suppliedBinding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        WorkspacePackageOccurrenceDescriptor expected =
            Assert.IsType<WorkspacePackageOccurrenceDescriptor>(
                rooted.FindPackageOccurrence(suppliedBinding));

        PackageDependencyWorkspaceRouteOutcome.Completed completed =
            Assert.IsType<PackageDependencyWorkspaceRouteOutcome.Completed>(
                await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        workspace,
                        rooted,
                        traversal,
                        [sourceBinding, suppliedBinding],
                        [],
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));

        Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
            completed.ScopeOperation);
        PackageDependencyWorkspaceDestination.Package destination =
            Assert.IsType<PackageDependencyWorkspaceDestination.Package>(
                Assert.Single(completed.Destinations));
        Assert.Same(expected, destination.Occurrence);
        Assert.Equal(
            PackageDependencyWorkspacePackageRouteSource.SuppliedRoot,
            destination.Source);
        Assert.Null(destination.Realization);
    }

    [Fact]
    public async Task
        WorkspaceRoutesRequireEveryResolvedEdgeRealizationBeforeMutation()
    {
        (PackageRootBinding binding,
            RealizedPackageDependencyContext context) =
            await WorkspaceRouteRootAsync(
                "route.source",
                "1.0.0",
                Dependency("route.target", "[1.0.0]"));
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate candidate = Pinned(
            new PackageAcquisitionCandidateIssuer(),
            authorization,
            "route.target",
            "1.0.0");
        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "route.source",
            "route.target",
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
            maxDepth: 1);
        Assert.Equal(
            PackageDependencyTraversalEdgeEmissionAuthority
                .ResolvedCandidate,
            Assert.Single(traversal.Edges).Authority);

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [binding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;

        await Assert.ThrowsAsync<ArgumentException>(
            async () =>
                await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        workspace,
                        rooted,
                        traversal,
                        [binding],
                        [],
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));
        WorkspaceScopeSnapshot current =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        Assert.Same(rooted, current);
    }

    [Fact]
    public async Task
        WorkspaceRoutesReturnCancellationWithoutBoundaryDestinations()
    {
        (PackageRootBinding binding,
            RealizedPackageDependencyContext context) =
            await WorkspaceRouteRootAsync(
                "route.source",
                "1.0.0",
                Dependency("route.boundary", "[1.0.0]"));
        PackageDependencyTraversalOutcome traversal = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    context,
                    PackageDependencyTraversalExpansionAuthority
                        .DirectDeclarationsOnly),
            ],
            new StubCandidateResolver(),
            new StubManifestAcquirer());
        Assert.Equal(
            PackageDependencyTraversalEdgeEmissionAuthority.DirectBoundary,
            Assert.Single(traversal.Edges).Authority);

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [binding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        PackageDependencyWorkspaceRouteOutcome.NotCommitted notCommitted =
            Assert.IsType<
                PackageDependencyWorkspaceRouteOutcome.NotCommitted>(
                await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        workspace,
                        rooted,
                        traversal,
                        [binding],
                        [],
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    cancellation.Token));

        Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            notCommitted.ScopeOperation);
    }

    [Fact]
    public async Task WorkspaceRoutesRetainTraversalBoundary()
    {
        (PackageRootBinding binding,
            RealizedPackageDependencyContext context) =
            await WorkspaceRouteRootAsync(
                "route.source",
                "1.0.0",
                Dependency("route.boundary", "[1.0.0]"));
        PackageDependencyTraversalOutcome traversal = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    context,
                    PackageDependencyTraversalExpansionAuthority
                        .DirectDeclarationsOnly),
            ],
            new StubCandidateResolver(),
            new StubManifestAcquirer());

        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot empty =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot rooted =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    empty.Revision,
                    empty.PublicationBase,
                    [binding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    TestContext.Current.CancellationToken)).Snapshot;

        PackageDependencyWorkspaceRouteOutcome.Completed completed =
            Assert.IsType<PackageDependencyWorkspaceRouteOutcome.Completed>(
                await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        workspace,
                        rooted,
                        traversal,
                        [binding],
                        [],
                        DateTimeOffset.UtcNow.AddMinutes(1)),
                    TestContext.Current.CancellationToken));

        PackageDependencyWorkspaceDestination.Unavailable unavailable =
            Assert.IsType<
                PackageDependencyWorkspaceDestination.Unavailable>(
                Assert.Single(completed.Destinations));
        Assert.Equal(
            PackageDependencyWorkspaceUnavailableReason.TraversalBoundary,
            unavailable.Reason);
        Assert.Null(unavailable.Realization);
        Assert.Null(unavailable.NoContributionReason);
        Assert.Equal(
            PackageDependencyTraversalEdgeEmissionAuthority.DirectBoundary,
            unavailable.Subject.Edge.Authority);
    }

    private static async ValueTask<(
        PackageRootBinding Binding,
        RealizedPackageDependencyContext Context)> WorkspaceRouteRootAsync(
            string packageId,
            string version,
            string dependenciesXml)
    {
        byte[] manifest = ManifestBytes(
            packageId,
            version,
            dependenciesXml);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream nuspec = archive.CreateEntry(
                $"{packageId}.nuspec").Open())
            {
                nuspec.Write(manifest);
            }

            using Stream assembly = archive.CreateEntry(
                $"lib/net11.0/{packageId}.dll").Open();
            assembly.Write(
                File.ReadAllBytes(
                    typeof(PackageDependencyTraversalQueryTests)
                        .Assembly.Location));
        }

        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, version),
            new InMemoryPackageContent(
                stream.ToArray(),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(payload, "net11.0");
        RealizedPackageDependencyContext context =
            Assert.IsType<RealizedPackageDependencyContextResult.Available>(
                await RealizedPackageDependencyContextQuery.ExecuteAsync(
                    binding,
                    TestContext.Current.CancellationToken)).Context;
        return (binding, context);
    }
}
