using System.IO.Compression;
using DotnetInspector.Packages;
using DotnetInspector.QueriesConsumer;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageDependencyTraversalQueryTests
{
    [Fact]
    public async Task
        Traversal_RealizedPollyContextsPreservePackageSelectionUnderDefaultTarget()
    {
        byte[] nupkg = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "RealizedPackageDependencyContext",
                "polly.core.8.8.0.nupkg"));
        RealizedPackageDependencyContext exactContext =
            await RealizedContextAsync(
                nupkg,
                "polly.core",
                "8.8.0",
                "netstandard2.0");
        RealizedPackageDependencyContext compatibleContext =
            await RealizedContextAsync(
                nupkg,
                "polly.core",
                "8.8.0",
                "net12.0",
                compatible: true);

        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        var resolver = new StubCandidateResolver();
        foreach (string dependency in new[]
                 {
                     "microsoft.bcl.asyncinterfaces",
                     "microsoft.bcl.timeprovider",
                     "system.componentmodel.annotations",
                     "system.threading.tasks.extensions",
                 })
        {
            PackageAcquisitionCandidate candidate = Pinned(
                issuer,
                authorization,
                dependency,
                "1.0.0");
            resolver.SetResponse(
                "polly.core",
                dependency,
                () => Resolved(candidate));
        }

        TraversalTargetFrameworkPolicy traversalTargetPolicy =
            TraversalTargetFrameworkPolicy.ProductDefault;
        var acquirer = new StubManifestAcquirer();
        PackageDependencyTraversalOutcome exactOutcome = await ExecuteAsync(
            [
                RealizedPackageDependencyContextConsumer.CreateTraversalRoot(
                    exactContext),
            ],
            resolver,
            acquirer,
            maxDepth: 1,
            traversalTargetPolicy: traversalTargetPolicy);
        PackageDependencyTraversalOutcome compatibleOutcome = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    compatibleContext,
                    PackageDependencyTraversalExpansionAuthority.RecursiveSources),
            ],
            new StubCandidateResolver(),
            new StubManifestAcquirer(),
            traversalTargetPolicy: traversalTargetPolicy);

        Assert.Equal(
            ".NETStandard2.0",
            exactContext.Evidence.Selection.SelectedFramework?.ToString());
        Assert.Equal(
            "net8.0",
            compatibleContext.Evidence.Selection.SelectedFramework?.ToString());
        Assert.Equal(
            [
                "microsoft.bcl.asyncinterfaces",
                "microsoft.bcl.timeprovider",
                "system.componentmodel.annotations",
                "system.threading.tasks.extensions",
            ],
            exactOutcome.Edges
                .Select(edge => edge.Declaration.CanonicalPackageId)
                .Order(StringComparer.Ordinal));
        Assert.Empty(compatibleOutcome.Edges);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.DepthBounded,
            exactOutcome.Roots[0].Completion);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Complete,
            compatibleOutcome.Roots[0].Completion);
        Assert.Same(
            exactContext,
            Assert.IsType<PackageDependencyTraversalRootSource.RealizedPackage>(
                exactOutcome.Roots[0].Occurrence.Source).Context);
        Assert.Same(
            traversalTargetPolicy,
            exactOutcome.TraversalTargetPolicy);
        Assert.Same(
            compatibleContext,
            Assert.IsType<PackageDependencyTraversalRootSource.RealizedPackage>(
                compatibleOutcome.Roots[0].Occurrence.Source).Context);
        Assert.Same(
            traversalTargetPolicy,
            compatibleOutcome.TraversalTargetPolicy);
        Assert.Equal(0, acquirer.CallCount);
    }

    [Fact]
    public async Task
        Traversal_EqualCoordinateRealizedContextsRemainDistinct()
    {
        RealizedPackageDependencyContext firstContext =
            await RealizedContextAsync(
                "shared",
                "1.0.0",
                Dependency("left", "[1.0.0]"));
        RealizedPackageDependencyContext secondContext =
            await RealizedContextAsync(
                "shared",
                "1.0.0",
                Dependency("right", "[2.0.0]"));
        var first = new PackageDependencyTraversalRootOccurrence(
            firstContext,
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);
        var second = new PackageDependencyTraversalRootOccurrence(
            secondContext,
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [first, second],
            new StubCandidateResolver(),
            new StubManifestAcquirer());

        PackageDependencyTraversalNode node = Assert.Single(outcome.Nodes);
        Assert.Equal(2, node.ProjectionIndexes.Length);
        Assert.Equal(2, outcome.Edges.Length);
        Assert.NotSame(
            firstContext.Subject.ContentGeneration,
            secondContext.Subject.ContentGeneration);
        Assert.NotSame(
            firstContext.Subject.Selection,
            secondContext.Subject.Selection);
        Assert.Same(
            firstContext,
            Assert.IsType<PackageDependencyTraversalRootSource.RealizedPackage>(
                outcome.Roots[0].Occurrence.Source).Context);
        Assert.Same(
            secondContext,
            Assert.IsType<PackageDependencyTraversalRootSource.RealizedPackage>(
                outcome.Roots[1].Occurrence.Source).Context);

        int firstEdgeIndex = outcome.Edges.ToList().FindIndex(
            edge => edge.SourceProjectionIndex
                == outcome.Roots[0].ProjectionIndex);
        int secondEdgeIndex = outcome.Edges.ToList().FindIndex(
            edge => edge.SourceProjectionIndex
                == outcome.Roots[1].ProjectionIndex);
        Assert.True(
            outcome.RootReachability[0].IsEdgeAdmitted(firstEdgeIndex, out _));
        Assert.False(
            outcome.RootReachability[0].IsEdgeAdmitted(secondEdgeIndex, out _));
        Assert.False(
            outcome.RootReachability[1].IsEdgeAdmitted(firstEdgeIndex, out _));
        Assert.True(
            outcome.RootReachability[1].IsEdgeAdmitted(secondEdgeIndex, out _));
    }

    [Fact]
    public async Task
        Traversal_RealizedIncompleteContextRetainsSurvivingEdgeAndFailure()
    {
        RealizedPackageDependencyContext context =
            await RealizedContextAsync(
                "incomplete",
                "1.0.0",
                """
                <dependency id="usable" version="[1.0.0]" />
                <dependency id="conflict" version="[1.0.0]" />
                <dependency id="Conflict" version="[2.0.0]" />
                """);
        var root = new PackageDependencyTraversalRootOccurrence(
            context,
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            new StubCandidateResolver(),
            new StubManifestAcquirer());

        PackageDependencyTraversalEdge edge = Assert.Single(outcome.Edges);
        Assert.Equal("usable", edge.Declaration.CanonicalPackageId);
        Assert.IsType<
            PackageDependencyTraversalManifestFailureDetail.Declaration>(
                Assert.Single(outcome.Failures).Detail);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
        Assert.Same(
            context,
            Assert.IsType<PackageDependencyTraversalRootSource.RealizedPackage>(
                outcome.Roots[0].Occurrence.Source).Context);
    }

    private static async ValueTask<RealizedPackageDependencyContext>
        RealizedContextAsync(
            string packageId,
            string version,
            string dependenciesXml) =>
        await RealizedContextAsync(
            PackageBytes(packageId, version, dependenciesXml),
            packageId,
            version,
            target: null);

    private static async ValueTask<RealizedPackageDependencyContext>
        RealizedContextAsync(
            byte[] nupkg,
            string packageId,
            string version,
            string? target,
            bool compatible = false)
    {
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, version),
            new InMemoryPackageContent(
                nupkg,
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        PackageRootBinding binding = compatible
            ? PackageRootBinding.CreateFromSourceWithCompatibleSelection(
                payload,
                target ?? throw new ArgumentNullException(nameof(target)))
            : PackageRootBinding.CreateFromSource(payload, target);
        RealizedPackageDependencyContextResult.Available available =
            Assert.IsType<RealizedPackageDependencyContextResult.Available>(
                await RealizedPackageDependencyContextQuery.ExecuteAsync(
                    binding,
                    TestContext.Current.CancellationToken));
        return available.Context;
    }

    private static byte[] PackageBytes(
        string packageId,
        string version,
        string dependenciesXml)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(
                $"{packageId}.nuspec").Open();
            entry.Write(ManifestBytes(packageId, version, dependenciesXml));
        }

        return stream.ToArray();
    }
}
