using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Release gates for the host-neutral Package Dependency Traversal Query
/// (<c>#5996</c>), compiled by the Queries test executable. Two harnesses are
/// used deliberately:
/// </summary>
/// <remarks>
/// <para>
/// Most gates use <see cref="StubCandidateResolver"/> and
/// <see cref="StubManifestAcquirer"/>: lightweight direct implementations of the
/// traversal-owned capability interfaces that let a pathological graph shape (cycles,
/// shared nodes, budgets, cancellation, ordering) be constructed precisely without
/// standing up source authorization. Every manifest they hand back is still real
/// nuspec content run through the product's own
/// <see cref="PackageManifestFactsQuery"/>/<see cref="PackageDependencyGroupsQuery"/>/
/// <see cref="PackageDependencyEvidenceQuery"/> pipeline inside the traversal query
/// itself, so this never fabricates evidence the query would otherwise compute.
/// </para>
/// <para>
/// The framework-classification, budget-adjacent-failure, and host-equivalence gates
/// instead exercise the real <c>DotnetInspector.PackageQueries</c> adapters
/// (<see cref="PackageDependencyTraversalCandidateAdapter"/>,
/// <see cref="AuthorizedPackageDependencyManifestSource"/>,
/// <see cref="DesktopPackageDependencyTraversalManifestSource"/>) over a fake or real
/// local-folder package source, so they prove the product's own candidate-resolution
/// and manifest-acquisition classification rather than a re-implementation of it.
/// </para>
/// </remarks>
public sealed class PackageDependencyTraversalQueryTests
{
    private const int DefaultManifestBudget = 100;
    private const int DefaultDeclarationBudget = 100;

    [Fact]
    public async Task Traversal_SamePackageIdDifferentVersionsRemainDistinct()
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate utility15 = Pinned(
            issuer,
            authorization,
            "utility",
            "1.5.0");
        PackageAcquisitionCandidate utility24 = Pinned(
            issuer,
            authorization,
            "utility",
            "2.4.0");

        PackageDependencyTraversalRootOccurrence rootA = Root(
            "roota",
            "1.0.0",
            Dependency("utility", "[1.0.0, 2.0.0)"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalRootOccurrence rootB = Root(
            "rootb",
            "1.0.0",
            Dependency("utility", "[2.0.0, 3.0.0)"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "roota",
            "utility",
            () => Resolved(utility15));
        resolver.SetResponse(
            "rootb",
            "utility",
            () => Resolved(utility24));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(utility15, ManifestBytes("utility", "1.5.0", ""));
        acquirer.SetManifest(utility24, ManifestBytes("utility", "2.4.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [rootA, rootB],
            resolver,
            acquirer);

        Assert.Equal(2, outcome.Roots.Length);
        Assert.Equal(4, outcome.Nodes.Length);
        PackageDependencyTraversalNode utility15Node = Assert.Single(
            outcome.Nodes.Where(node => node.Coordinate == utility15.Coordinate));
        PackageDependencyTraversalNode utility24Node = Assert.Single(
            outcome.Nodes.Where(node => node.Coordinate == utility24.Coordinate));
        Assert.NotEqual(utility15Node.Coordinate, utility24Node.Coordinate);
        Assert.Equal(2, outcome.Edges.Count(
            edge => edge.Declaration.CanonicalPackageId == "utility"));
        Assert.True(outcome.IsComplete);
    }

    [Fact]
    public async Task Traversal_SharedNodeRetainsAllParentEdges()
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate shared = Pinned(
            issuer,
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
        resolver.SetResponse("roota", "shared", () => Resolved(shared));
        resolver.SetResponse("rootb", "shared", () => Resolved(shared));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(shared, ManifestBytes("shared", "1.0.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [rootA, rootB],
            resolver,
            acquirer);

        Assert.Equal(3, outcome.Nodes.Length);
        Assert.Equal(2, outcome.Edges.Count(
            edge => edge.Declaration.CanonicalPackageId == "shared"));
        Assert.Equal(1, acquirer.CallCount);
        Assert.True(outcome.IsComplete);
    }

    [Fact]
    public async Task Traversal_RootRelativeDepthDoesNotUseGlobalVisitedSet()
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate shared = Pinned(
            issuer,
            authorization,
            "shared",
            "1.0.0");
        PackageAcquisitionCandidate bridge = Pinned(
            issuer,
            authorization,
            "bridge",
            "1.0.0");

        PackageDependencyTraversalRootOccurrence rootA = Root(
            "roota",
            "1.0.0",
            Dependency("shared", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalRootOccurrence rootB = Root(
            "rootb",
            "1.0.0",
            Dependency("bridge", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "shared", () => Resolved(shared));
        resolver.SetResponse("rootb", "bridge", () => Resolved(bridge));
        resolver.SetResponse("bridge", "shared", () => Resolved(shared));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(shared, ManifestBytes("shared", "1.0.0", ""));
        acquirer.SetManifest(bridge,
            ManifestBytes(
                "bridge",
                "1.0.0",
                Dependency("shared", "[1.0.0]")));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [rootA, rootB],
            resolver,
            acquirer);

        int sharedNodeIndex = outcome.Nodes.ToList().FindIndex(
            node => node.Coordinate == shared.Coordinate);
        Assert.Equal(1, outcome.RootReachability[0].NodeDistances[sharedNodeIndex]);
        Assert.Equal(2, outcome.RootReachability[1].NodeDistances[sharedNodeIndex]);
        Assert.Equal(1, acquirer.CallsFor(shared.Coordinate));
        Assert.Equal(2, acquirer.CallCount);
    }

    [Fact]
    public async Task Traversal_CycleRetainsClosingEdgeAndTerminates()
    {
        // RootA -> Shared; RootB -> Bridge -> Shared -> RootB (cycle-closing edge).
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate shared = Pinned(
            issuer,
            authorization,
            "shared",
            "1.0.0");
        PackageAcquisitionCandidate bridge = Pinned(
            issuer,
            authorization,
            "bridge",
            "1.0.0");
        PackageAcquisitionCandidate rootBAsCandidate = Pinned(
            issuer,
            authorization,
            "rootb",
            "1.0.0");

        PackageDependencyTraversalRootOccurrence rootA = Root(
            "roota",
            "1.0.0",
            Dependency("shared", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalRootOccurrence rootB = Root(
            "rootb",
            "1.0.0",
            Dependency("bridge", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "shared", () => Resolved(shared));
        resolver.SetResponse("rootb", "bridge", () => Resolved(bridge));
        resolver.SetResponse("bridge", "shared", () => Resolved(shared));
        resolver.SetResponse("shared", "rootb", () => Resolved(rootBAsCandidate));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(bridge,
            ManifestBytes("bridge", "1.0.0", Dependency("shared", "[1.0.0]")));
        acquirer.SetManifest(shared,
            ManifestBytes("shared", "1.0.0", Dependency("rootb", "[1.0.0]")));
        acquirer.SetManifest(rootBAsCandidate, ManifestBytes("rootb", "1.0.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [rootA, rootB],
            resolver,
            acquirer,
            maxDepth: 2);

        int sharedNodeIndex = outcome.Nodes.ToList().FindIndex(
            node => node.Coordinate == shared.Coordinate);
        Assert.Equal(1, outcome.RootReachability[0].NodeDistances[sharedNodeIndex]);
        Assert.Equal(2, outcome.RootReachability[1].NodeDistances[sharedNodeIndex]);

        PackageDependencyTraversalEdge closingEdge = Assert.Single(
            outcome.Edges.Where(
                edge => edge.Declaration.CanonicalPackageId == "rootb"
                    && edge.SourceCoordinate == shared.Coordinate));
        int closingEdgeIndex = outcome.Edges.ToList().IndexOf(closingEdge);
        Assert.True(
            outcome.RootReachability[0].IsEdgeAdmitted(closingEdgeIndex, out int aDistance));
        Assert.Equal(2, aDistance);
        Assert.False(
            outcome.RootReachability[1].IsEdgeAdmitted(closingEdgeIndex, out _));

        PackageDependencyTraversalEdge bridgeToSharedEdge = Assert.Single(
            outcome.Edges.Where(
                edge => edge.Declaration.CanonicalPackageId == "shared"
                    && edge.SourceCoordinate == bridge.Coordinate));
        Assert.True(
            outcome.RootReachability[1].IsEdgeAdmitted(
                outcome.Edges.ToList().IndexOf(bridgeToSharedEdge),
                out int bridgeDistance));
        Assert.Equal(2, bridgeDistance);
        Assert.Equal(1, acquirer.CallsFor(shared.Coordinate));
    }

    [Fact]
    public async Task Traversal_DepthBoundSkipsEndpointManifestAcquisition()
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate direct = Pinned(
            issuer,
            authorization,
            "direct",
            "1.0.0");

        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("direct", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "direct", () => Resolved(direct));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(direct, ManifestBytes("direct", "1.0.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer,
            maxDepth: 1);

        Assert.Equal(0, acquirer.CallCount);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.DepthBounded,
            outcome.Roots[0].Completion);
        PackageDependencyTraversalDepthBoundary boundary =
            Assert.Single(outcome.DepthBoundaries);
        Assert.Equal(1, boundary.MaximumDepth);
        Assert.Equal([0], boundary.AffectedRootOccurrences);
        Assert.Equal(direct.Coordinate, outcome.Nodes[boundary.NodeIndex].Coordinate);
        Assert.Equal(
            boundary.NodeIndex,
            outcome.Projections[boundary.ProjectionIndex].NodeIndex);
    }

    [Fact]
    public async Task Traversal_EquivalentCandidateAcquisitionIsSingleFlight()
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate shared = Pinned(
            issuer,
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
        resolver.SetResponse("roota", "shared", () => Resolved(shared));
        resolver.SetResponse("rootb", "shared", () => Resolved(shared));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(shared, ManifestBytes("shared", "1.0.0", ""));

        await ExecuteAsync([rootA, rootB], resolver, acquirer);

        Assert.Equal(1, acquirer.CallCount);
    }

    [Fact]
    public async Task Traversal_RootEvidenceRequiresCandidateCorrespondenceForReuse()
    {
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        // RootB's own coordinate is also reached transitively through RootA, but no
        // owner-issued correspondence exists for root-supplied evidence, so the
        // candidate-acquired projection must still be acquired independently.
        PackageAcquisitionCandidate rootBAsCandidate = Pinned(
            issuer,
            authorization,
            "rootb",
            "1.0.0");

        PackageDependencyTraversalRootOccurrence rootA = Root(
            "roota",
            "1.0.0",
            Dependency("rootb", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalRootOccurrence rootB = Root(
            "rootb",
            "1.0.0",
            "",
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "rootb", () => Resolved(rootBAsCandidate));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(rootBAsCandidate, ManifestBytes("rootb", "1.0.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [rootA, rootB],
            resolver,
            acquirer);

        PackageDependencyTraversalNode rootBNode = Assert.Single(
            outcome.Nodes.Where(node => node.Coordinate == rootBAsCandidate.Coordinate));
        Assert.Equal(2, rootBNode.ProjectionIndexes.Length);
        Assert.Equal(1, acquirer.CallCount);
    }

    [Fact]
    public async Task Traversal_SourceRelativeProjectionPreservesDistinctContent()
    {
        PackageSourceAuthorization authorization = Authorize("authority-a");
        // Two independent candidate resolutions for the same coordinate, issued by
        // different (non-corresponding) issuers, must not be treated as one
        // single-flighted projection.
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
        acquirer.SetManifest(first,
            ManifestBytes("shared", "1.0.0", Dependency("left", "[1.0.0]")));
        acquirer.SetManifest(second,
            ManifestBytes("shared", "1.0.0", Dependency("right", "[1.0.0]")));
        resolver.SetResponse(
            "shared",
            "left",
            () => new PackageDependencyTraversalCandidateResult.Failed(
                new PackageDependencyTraversalCandidateFailure.NoMatchingVersion()));
        resolver.SetResponse(
            "shared",
            "right",
            () => new PackageDependencyTraversalCandidateResult.Failed(
                new PackageDependencyTraversalCandidateFailure.NoMatchingVersion()));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [rootA, rootB],
            resolver,
            acquirer);

        PackageDependencyTraversalNode sharedNode = Assert.Single(
            outcome.Nodes.Where(node => node.Coordinate == first.Coordinate));
        Assert.Equal(2, sharedNode.ProjectionIndexes.Length);
        Assert.Equal(2, acquirer.CallCount);
        Assert.Contains(
            outcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "left");
        Assert.Contains(
            outcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "right");
    }

    [Fact]
    public async Task Traversal_DirectOnlyRootsAreSourceBounded()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("direct", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);

        var resolver = new StubCandidateResolver();
        var acquirer = new StubManifestAcquirer();

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer);

        Assert.Equal(0, resolver.CallCount);
        Assert.Equal(0, acquirer.CallCount);
        PackageDependencyTraversalEdge edge = Assert.Single(outcome.Edges);
        Assert.Equal(
            PackageDependencyTraversalEdgeEmissionAuthority.DirectBoundary,
            edge.Authority);
        Assert.IsType<PackageDependencyTraversalEdgeTarget.DeclarationBoundary>(
            edge.Target);
        Assert.Single(outcome.DeclarationBoundaries);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.SourceBounded,
            outcome.Roots[0].Completion);
    }

    [Fact]
    public async Task Traversal_FailedResolutionRetainsDeclarationEdge()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("missing", "[9.9.9]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "roota",
            "missing",
            () => new PackageDependencyTraversalCandidateResult.Failed(
                new PackageDependencyTraversalCandidateFailure.NoMatchingVersion()));
        var acquirer = new StubManifestAcquirer();

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer);

        PackageDependencyTraversalEdge edge = Assert.Single(outcome.Edges);
        Assert.Equal(
            PackageDependencyTraversalEdgeEmissionAuthority.FailedResolution,
            edge.Authority);
        PackageDependencyTraversalFailedResolutionNode node =
            Assert.Single(outcome.FailedResolutions);
        Assert.IsType<PackageDependencyTraversalCandidateResult.Failed>(node.Outcome);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
        Assert.False(outcome.IsSuccessful);
    }

    [Fact]
    public async Task Traversal_EdgeAdmissionRespectsRootAuthority()
    {
        // A direct-only root and a recursively authorized root name the same exact
        // coordinate; each admits only its own emission-authority edges.
        PackageDependencyTraversalRootOccurrence directRoot = Root(
            "shared",
            "1.0.0",
            Dependency("child", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);
        PackageDependencyTraversalRootOccurrence recursiveRoot = Root(
            "shared",
            "1.0.0",
            Dependency("child", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate child = Pinned(issuer, authorization, "child", "1.0.0");
        var resolver = new StubCandidateResolver();
        resolver.SetResponse("shared", "child", () => Resolved(child));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(child, ManifestBytes("child", "1.0.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [directRoot, recursiveRoot],
            resolver,
            acquirer);

        Assert.Equal(
            PackageDependencyTraversalRootCompletion.SourceBounded,
            outcome.Roots[0].Completion);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Complete,
            outcome.Roots[1].Completion);
        Assert.Single(outcome.DeclarationBoundaries);
        PackageDependencyTraversalEdge directEdge = Assert.Single(
            outcome.Edges.Where(
                edge => edge.Authority
                    == PackageDependencyTraversalEdgeEmissionAuthority.DirectBoundary));
        PackageDependencyTraversalEdge resolvedEdge = Assert.Single(
            outcome.Edges.Where(
            edge => edge.Authority
                == PackageDependencyTraversalEdgeEmissionAuthority.ResolvedCandidate));
        int directEdgeIndex = outcome.Edges.IndexOf(directEdge);
        int resolvedEdgeIndex = outcome.Edges.IndexOf(resolvedEdge);
        Assert.True(
            outcome.RootReachability[0].IsEdgeAdmitted(directEdgeIndex, out _));
        Assert.False(
            outcome.RootReachability[0].IsEdgeAdmitted(resolvedEdgeIndex, out _));
        Assert.False(
            outcome.RootReachability[1].IsEdgeAdmitted(directEdgeIndex, out _));
        Assert.True(
            outcome.RootReachability[1].IsEdgeAdmitted(resolvedEdgeIndex, out _));
    }

    [Fact]
    public async Task Traversal_RepeatedDirectRootRequiresCorrespondenceToCoalesce()
    {
        PackageDependencyTraversalRootOccurrence first = Root(
            "shared",
            "1.0.0",
            Dependency("child", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);
        PackageDependencyTraversalRootOccurrence second = Root(
            "shared",
            "1.0.0",
            Dependency("child", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);

        var resolver = new StubCandidateResolver();
        var acquirer = new StubManifestAcquirer();

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [first, second],
            resolver,
            acquirer);

        PackageDependencyTraversalNode sharedNode = Assert.Single(outcome.Nodes);
        Assert.Equal(2, sharedNode.ProjectionIndexes.Length);
        Assert.Equal(2, outcome.DeclarationBoundaries.Length);
        Assert.Equal(2, outcome.Edges.Length);
    }

    [Fact]
    public async Task Traversal_FrameworkModeIsStructuralCurrency()
    {
        // The root's own selection is fixed at construction time (a mismatched
        // "net472" request), but the traversal's typed framework mode -- never that
        // retained inert request text -- drives every transitive manifest
        // projection's group selection.
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("child", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources,
            requestedFramework: "net472");
        string childDependencies =
            Dependency("net6-dep", "[1.0.0]", "net6.0")
            + Dependency("net8-dep", "[1.0.0]", "net8.0");

        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate child = Pinned(issuer, authorization, "child", "1.0.0");

        PackageDependencyTraversalFrameworkMode.TryCreateExact(
            "net6.0",
            out PackageDependencyTraversalFrameworkMode.Exact exact);
        Assert.Empty(
            typeof(PackageDependencyTraversalFrameworkMode.Exact)
                .GetConstructors());
        var exactResolver = new StubCandidateResolver();
        exactResolver.SetResponse("roota", "child", () => Resolved(child));
        exactResolver.SetResponse(
            "child",
            "net6-dep",
            () => new PackageDependencyTraversalCandidateResult.Failed(
                new PackageDependencyTraversalCandidateFailure.NoMatchingVersion()));
        var exactAcquirer = new StubManifestAcquirer();
        exactAcquirer.SetManifest(child,
            ManifestBytes("child", "1.0.0", childDependencies));
        PackageDependencyTraversalOutcome exactOutcome = await ExecuteAsync(
            [root],
            exactResolver,
            exactAcquirer,
            frameworkMode: exact);
        PackageDependencyEvidenceRoot childEvidenceExact = FindProjectionEvidence(
            exactOutcome,
            child.Coordinate);
        Assert.Equal(
            "net6.0",
            childEvidenceExact.Selection.SelectedFramework?.ToString());
        Assert.Contains(
            exactOutcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "net6-dep");
        Assert.DoesNotContain(
            exactOutcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "net8-dep");

        var defaultResolver = new StubCandidateResolver();
        defaultResolver.SetResponse("roota", "child", () => Resolved(child));
        defaultResolver.SetResponse(
            "child",
            "net8-dep",
            () => new PackageDependencyTraversalCandidateResult.Failed(
                new PackageDependencyTraversalCandidateFailure.NoMatchingVersion()));
        var defaultAcquirer = new StubManifestAcquirer();
        defaultAcquirer.SetManifest(child,
            ManifestBytes("child", "1.0.0", childDependencies));
        PackageDependencyTraversalOutcome defaultOutcome = await ExecuteAsync(
            [root],
            defaultResolver,
            defaultAcquirer,
            frameworkMode: new PackageDependencyTraversalFrameworkMode.ManifestDefault());
        PackageDependencyEvidenceRoot childEvidenceDefault = FindProjectionEvidence(
            defaultOutcome,
            child.Coordinate);
        Assert.Equal(
            "net8.0",
            childEvidenceDefault.Selection.SelectedFramework?.ToString());
        Assert.Contains(
            defaultOutcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "net8-dep");
        Assert.DoesNotContain(
            defaultOutcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "net6-dep");
    }

    [Fact]
    public async Task Traversal_ManifestDefaultUsesOwnerNoRequestSelection()
    {
        string dependencies =
            Dependency("low", "[1.0.0]", "net6.0")
            + Dependency("high", "[1.0.0]", "net8.0");
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            dependencies,
            requestedFramework: null);
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var resolver = new StubCandidateResolver();
        resolver.SetResponse(
            "roota",
            "high",
            () => new PackageDependencyTraversalCandidateResult.Failed(
                new PackageDependencyTraversalCandidateFailure.NoMatchingVersion()));
        var acquirer = new StubManifestAcquirer();
        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer,
            frameworkMode: new PackageDependencyTraversalFrameworkMode.ManifestDefault());

        Assert.Equal("net8.0", rootEvidence.Selection.SelectedFramework?.ToString());
        Assert.Contains(
            outcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "high");
        Assert.DoesNotContain(
            outcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "low");
    }

    [Fact]
    public async Task Traversal_ExactFrameworkNoMatchRemainsVisible()
    {
        string dependencies = Dependency("dep", "[1.0.0]", "net8.0");
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            dependencies,
            requestedFramework: "net472");
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalFrameworkMode.TryCreateExact(
            "net472",
            out PackageDependencyTraversalFrameworkMode.Exact exact);

        var resolver = new StubCandidateResolver();
        var acquirer = new StubManifestAcquirer();
        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer,
            frameworkMode: exact);

        Assert.Empty(outcome.Edges);
        Assert.Equal(
            PackageDependencyEvidenceSelectionStatus.NoMatchingTargetFramework,
            rootEvidence.Selection.Status);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Complete,
            outcome.Roots[0].Completion);
        Assert.True(outcome.IsComplete);
    }

    [Fact]
    public async Task Traversal_WorkBudgetRetainsUnprocessedFrontier()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("first", "[1.0.0]") + Dependency("second", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate first = Pinned(issuer, authorization, "first", "1.0.0");
        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "first", () => Resolved(first));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(first, ManifestBytes("first", "1.0.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer,
            maxDeclarationResolutions: 1);

        Assert.Equal(1, resolver.CallCount);
        PackageDependencyTraversalWorkBudgetNode budgetNode =
            Assert.Single(outcome.WorkBudgetDeclarations);
        Assert.Equal("second", budgetNode.CanonicalPackageId);
        Assert.Equal(1, budgetNode.Limit);
        Assert.Contains(
            outcome.Edges,
            edge => edge.Authority
                == PackageDependencyTraversalEdgeEmissionAuthority.WorkBudgetBoundary);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);

        // The manifest-projection budget exhaustion path: the candidate resolves but
        // its manifest is never acquired, leaving an unexpanded frontier.
        var resolver2 = new StubCandidateResolver();
        resolver2.SetResponse("roota", "first", () => Resolved(first));
        resolver2.SetResponse(
            "roota",
            "second",
            () => new PackageDependencyTraversalCandidateResult.Failed(
                new PackageDependencyTraversalCandidateFailure.NoMatchingVersion()));
        var acquirer2 = new StubManifestAcquirer();
        acquirer2.SetManifest(first, ManifestBytes("first", "1.0.0", ""));
        PackageDependencyTraversalOutcome outcome2 = await ExecuteAsync(
            [root],
            resolver2,
            acquirer2,
            maxManifestProjections: 0);

        Assert.Equal(0, acquirer2.CallCount);
        PackageDependencyTraversalFailure frontierFailure = Assert.Single(
            outcome2.Failures);
        Assert.IsType<
            PackageDependencyTraversalManifestFailureDetail
                .ManifestProjectionBudgetExhausted>(frontierFailure.Detail);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome2.Roots[0].Completion);

        PackageDependencyTraversalRootOccurrence sharedRootA = Root(
            "roota",
            "1.0.0",
            Dependency("first", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageDependencyTraversalRootOccurrence sharedRootB = Root(
            "rootb",
            "1.0.0",
            Dependency("first", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var sharedResolver = new StubCandidateResolver();
        sharedResolver.SetResponse("roota", "first", () => Resolved(first));
        sharedResolver.SetResponse("rootb", "first", () => Resolved(first));
        PackageDependencyTraversalOutcome sharedOutcome = await ExecuteAsync(
            [sharedRootA, sharedRootB],
            sharedResolver,
            new StubManifestAcquirer(),
            maxManifestProjections: 0);
        PackageDependencyTraversalFailure sharedFailure =
            Assert.Single(sharedOutcome.Failures);
        Assert.Equal([0, 1], sharedFailure.AffectedRootOccurrences);
    }

    [Fact]
    public async Task Traversal_CancellationDoesNotPublishOutcome()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("child", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var cancellation = new CancellationTokenSource();
        var resolver = new CancelingCandidateResolver(cancellation);
        var acquirer = new StubManifestAcquirer();

        PackageDependencyTraversalRequest request = BuildRequest(
            [root],
            resolver,
            acquirer);

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackageDependencyTraversalQuery.ExecuteAsync(
                request,
                cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Traversal_CancellationAfterFinalAcquisitionDoesNotPublishOutcome()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("child", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageAcquisitionCandidate candidate = Pinned(
            issuer,
            Authorize("authority-a"),
            "child",
            "1.0.0");
        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "child", () => Resolved(candidate));
        var cancellation = new CancellationTokenSource();
        PackageSourceResultFactory factory = CreateResultFactoryFor(
            candidate.Authorities[0].Authority);
        var acquirer = new CancelingManifestAcquirer(
            cancellation,
            factory.Manifest(
                candidate.Coordinate,
                ManifestBytes("child", "1.0.0", "")));

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PackageDependencyTraversalQuery.ExecuteAsync(
                    BuildRequest([root], resolver, acquirer),
                    cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Traversal_SourceCompletionOrderDoesNotAffectResult()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("first", "[1.0.0]") + Dependency("second", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate first = Pinned(issuer, authorization, "first", "1.0.0");
        PackageAcquisitionCandidate second = Pinned(issuer, authorization, "second", "1.0.0");

        async Task<PackageDependencyTraversalOutcome> RunAsync(
            TimeSpan firstDelay,
            TimeSpan secondDelay)
        {
            var resolver = new DelayingCandidateResolver();
            resolver.SetResponse("first", firstDelay, () => Resolved(first));
            resolver.SetResponse("second", secondDelay, () => Resolved(second));
            var acquirer = new StubManifestAcquirer();
            acquirer.SetManifest(first, ManifestBytes("first", "1.0.0", ""));
            acquirer.SetManifest(second, ManifestBytes("second", "1.0.0", ""));
            return await ExecuteAsync([root], resolver, acquirer);
        }

        PackageDependencyTraversalOutcome firstSlow = await RunAsync(
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(1));
        PackageDependencyTraversalOutcome secondSlow = await RunAsync(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(40));

        Assert.Equal(
            firstSlow.Nodes.Select(node => node.Coordinate),
            secondSlow.Nodes.Select(node => node.Coordinate));
        Assert.Equal(
            firstSlow.Edges.Select(edge =>
                (
                    edge.SourceCoordinate,
                    edge.Declaration.CanonicalPackageId,
                    edge.Declaration.CanonicalVersionConstraint,
                    edge.Authority)),
            secondSlow.Edges.Select(edge =>
                (
                    edge.SourceCoordinate,
                    edge.Declaration.CanonicalPackageId,
                    edge.Declaration.CanonicalVersionConstraint,
                    edge.Authority)));
        Assert.Equal(
            ["first", "second"],
            firstSlow.Edges.Select(
                edge => edge.Declaration.CanonicalPackageId));
    }

    [Fact]
    public async Task Traversal_InertTextRemainsInertThroughGraphResult()
    {
        await using RegistryFixture fixture = new();
        fixture.Registry.Add(
            "dependency",
            "1.2.3",
            Dependency("ChIlD", "[2.0.0]"));
        fixture.Registry.Add("child", "2.0.0", "");
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        PackageDependencyTraversalEdge acquiredEdge = Assert.Single(
            outcome.Edges.Where(
                edge => edge.SourceCoordinate.PackageId == "dependency"));
        Assert.IsType<InertString>(
            acquiredEdge.Declaration.SourcePackageIdSpelling);
        Assert.IsType<InertString>(
            acquiredEdge.Declaration.SourceVersionConstraintSpelling);
        Assert.Equal(
            "ChIlD",
            acquiredEdge.Declaration.SourcePackageIdSpelling.ToString());
        Assert.Equal(
            "[2.0.0]",
            acquiredEdge.Declaration.SourceVersionConstraintSpelling.ToString());
    }

    // ---- Product-adapter gates (real #5765 candidate resolution and real exact
    // manifest acquisition over a fake source client). ----

    [Fact]
    public async Task Traversal_ExactDeclarationUsesPinnedAcquisition()
    {
        await using RegistryFixture fixture = new();
        fixture.Registry.Add("dependency", "1.2.3", "");
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            requestedFramework: null);
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        Assert.Equal(0, fixture.Client.VersionCalls);
        Assert.True(outcome.IsComplete);
        Assert.Contains(
            outcome.Edges,
            edge => edge.Authority
                    == PackageDependencyTraversalEdgeEmissionAuthority
                        .ResolvedCandidate
                && edge.Declaration.CanonicalPackageId == "dependency");
    }

    [Fact]
    public async Task Traversal_BareVersionRequiresCandidateResolution()
    {
        await using RegistryFixture fixture = new();
        fixture.Registry.Add("dependency", "1.0.0", "");
        fixture.Registry.Add("dependency", "1.5.0", "");
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            // A bare version is a minimum-inclusive range, not an exact pin, so it
            // must go through complete version-range discovery rather than pinned
            // acquisition.
            Dependency("dependency", "1.0.0"),
            requestedFramework: null);
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        Assert.True(fixture.Client.VersionCalls > 0);
        PackageDependencyTraversalEdge edge = Assert.Single(outcome.Edges);
        Assert.Equal(
            PackageDependencyTraversalEdgeEmissionAuthority.ResolvedCandidate,
            edge.Authority);
        var target = Assert.IsType<PackageDependencyTraversalEdgeTarget.Node>(
            edge.Target);
        // NuGet range resolution selects the lowest applicable version, not
        // "latest": both 1.0.0 and 1.5.0 satisfy ">= 1.0.0", so 1.0.0 wins.
        Assert.Equal("1.0.0", outcome.Nodes[target.NodeIndex].Coordinate.Version);
    }

    [Fact]
    public async Task Traversal_CandidateResolverIncompleteOutcomeRemainsVisible()
    {
        await using RegistryFixture fixture = new();
        fixture.Client.FailVersionDiscovery = true;
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("dependency", "1.0.0"),
            requestedFramework: null);
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        PackageDependencyTraversalFailedResolutionNode node =
            Assert.Single(outcome.FailedResolutions);
        var incomplete = Assert.IsType<
            PackageDependencyTraversalCandidateResult.Incomplete>(node.Outcome);
        Assert.IsType<
            PackageDependencyTraversalCandidateIncomplete.VersionDiscovery>(
                incomplete.Evidence);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
    }

    [Fact]
    public async Task Traversal_ResolutionFailureIsNotDependencyFreeLeaf()
    {
        await using RegistryFixture fixture = new();
        // "dependency" is never added, so version discovery is authoritatively empty.
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("dependency", "1.0.0"),
            requestedFramework: null);
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        PackageDependencyTraversalFailedResolutionNode node =
            Assert.Single(outcome.FailedResolutions);
        var failed = Assert.IsType<PackageDependencyTraversalCandidateResult.Failed>(
            node.Outcome);
        Assert.IsType<PackageDependencyTraversalCandidateFailure.NoMatchingVersion>(
            failed.Failure);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
        Assert.False(outcome.IsComplete);
        Assert.False(outcome.IsSuccessful);
    }

    [Fact]
    public async Task Traversal_ManifestFailureIsNotDependencyFreeLeaf()
    {
        await using RegistryFixture fixture = new();
        // The version is known (so candidate resolution succeeds) but its manifest
        // bytes were never published, so exact acquisition fails.
        fixture.Registry.AddVersionOnly("dependency", "1.2.3");
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            requestedFramework: null);
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        PackageDependencyTraversalFailure failure = Assert.Single(outcome.Failures);
        var acquisition = Assert.IsType<
            PackageDependencyTraversalManifestFailureDetail.Acquisition>(
                failure.Detail);
        PackageAuthorityFailure authorityFailure =
            Assert.Single(acquisition.Failures);
        Assert.IsType<InertString>(authorityFailure.Authority);
        Assert.NotEqual("", authorityFailure.Authority.ToString());
        Assert.NotNull(authorityFailure.SourceFailure);
        Assert.Same(fixture.Client.Source, authorityFailure.ResultSource);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
        Assert.Empty(
            outcome.Projections[failure.ProjectionIndex].OutgoingEdgeIndexes);
    }

    [Fact]
    public async Task Traversal_ManifestCandidateMismatchIsIdentityFailure()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate candidate = Pinned(
            issuer,
            authorization,
            "dependency",
            "1.2.3");
        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "dependency", () => Resolved(candidate));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(
            candidate,
            PackageSourceCoordinate.Create("other", "9.9.9"),
            ManifestBytes("other", "9.9.9", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer);

        PackageDependencyTraversalFailure failure =
            Assert.Single(outcome.Failures);
        var identity = Assert.IsType<
            PackageDependencyTraversalManifestFailureDetail.Identity>(
                failure.Detail);
        Assert.Equal(
            PackageManifestFailureReason.IdentityMismatch,
            identity.Failure.Reason);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
    }

    [Fact]
    public async Task Traversal_InvalidDeclarationDoesNotSuppressValidSibling()
    {
        PackageDependencyEvidenceRoot validRoot = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("valid", "[1.0.0]"),
            requestedFramework: null);
        var available =
            (PackageDependencyEvidenceDeclarationResult.Available)
                validRoot.Declaration;
        PackageDependencyEvidenceGroup group = Assert.Single(available.Groups);
        PackageDependencyEvidenceRoot incompleteRoot = validRoot with
        {
            Declaration = new PackageDependencyEvidenceDeclarationResult.Available(
                available.Groups,
                [
                    new PackageDependencyEvidenceDeclarationFailure
                        .InvalidPackageDeclaration(
                            group.Identity,
                            SourceOccurrenceCount: 1),
                ],
                PackageDependencyEvidencePhaseCompletion.Incomplete),
        };
        var root = new PackageDependencyTraversalRootOccurrence(
            incompleteRoot,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        var issuer = new PackageAcquisitionCandidateIssuer();
        PackageSourceAuthorization authorization = Authorize("authority-a");
        PackageAcquisitionCandidate valid = Pinned(
            issuer,
            authorization,
            "valid",
            "1.0.0");
        var resolver = new StubCandidateResolver();
        resolver.SetResponse("roota", "valid", () => Resolved(valid));
        var acquirer = new StubManifestAcquirer();
        acquirer.SetManifest(valid, ManifestBytes("valid", "1.0.0", ""));

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            resolver,
            acquirer);

        Assert.Contains(
            outcome.Edges,
            edge => edge.Declaration.CanonicalPackageId == "valid");
        Assert.Contains(
            outcome.Failures,
            failure => failure.Detail
                is PackageDependencyTraversalManifestFailureDetail.Declaration);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
    }

    [Fact]
    public async Task Traversal_ManifestExpansionUsesManifestBytesOnly()
    {
        await using RegistryFixture fixture = new();
        fixture.Registry.Add("dependency", "1.2.3", "");
        fixture.Client.ForbidPayloadAndSymbols = true;
        PackageDependencyEvidenceRoot rootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            requestedFramework: null);
        PackageDependencyTraversalRootOccurrence root = new(
            rootEvidence,
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [root],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        Assert.True(outcome.IsComplete);
        Assert.Equal(0, fixture.Client.PackageOrSymbolCalls);
    }

    [Fact]
    public async Task Traversal_AuthorizedManifestSourceRejectsForeignCandidate()
    {
        await using RegistryFixture fixture = new();
        var foreignIssuer = new PackageAcquisitionCandidateIssuer();
        PackageAcquisitionCandidate foreignCandidate = Pinned(
            foreignIssuer,
            Authorize("foreign-authority"),
            "dependency",
            "1.2.3");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.ManifestAcquirer.AcquireAsync(
                foreignCandidate,
                TestContext.Current.CancellationToken));
        Assert.False(fixture.Client.IsDisposed);
    }

    [Fact]
    public async Task Traversal_OperationDeadlineAffectsOnlyUnfinishedRoots()
    {
        await using RegistryFixture fixture = new();
        fixture.Registry.Add("dependency", "1.2.3", "");
        fixture.Client.DelayManifestBeyondOperationDeadline = true;
        PackageDependencyTraversalRootOccurrence directRoot = Root(
            "direct",
            "1.0.0",
            Dependency("boundary", "[1.0.0]"),
            PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly);
        PackageDependencyTraversalRootOccurrence recursiveRoot = Root(
            "recursive",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(250),
            TestContext.Current.CancellationToken);

        PackageDependencyTraversalOutcome outcome =
            await PackageDependencyTraversalQuery.ExecuteAsync(
                BuildRequest(
                    [directRoot, recursiveRoot],
                    fixture.CandidateResolver,
                    fixture.ManifestAcquirer),
                TestContext.Current.CancellationToken,
                operationContext: operation);

        Assert.Equal(
            PackageDependencyTraversalRootCompletion.SourceBounded,
            outcome.Roots[0].Completion);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[1].Completion);
        var incomplete = Assert.IsType<
            PackageDependencyTraversalManifestFailureDetail
                .IncompleteAcquisition>(
                    Assert.Single(outcome.Failures).Detail);
        Assert.Contains(
            incomplete.Failures,
            failure => failure.Timeout?.Kind
                == PackageSourceTimeoutKind.Operation);
    }

    [Fact]
    public async Task Traversal_HostAdaptersPreserveEquivalentGraph()
    {
        string folder = CreateTemporaryDirectory();
        WriteLocalSourcePackage(folder, "roota", "1.0.0", Dependency("dependency", "[1.2.3]"));
        WriteLocalSourcePackage(
            folder,
            "dependency",
            "1.2.3",
            Dependency("leaf", "[2.0.0]"));
        WriteLocalSourcePackage(folder, "leaf", "2.0.0", "");

        // Browser/Wasm-style: explicit caller-owned source clients over a fake
        // in-memory registry.
        await using RegistryFixture fixture = new();
        fixture.Registry.Add(
            "dependency",
            "1.2.3",
            Dependency("leaf", "[2.0.0]"));
        fixture.Registry.Add("leaf", "2.0.0", "");
        PackageDependencyEvidenceRoot browserRootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            requestedFramework: null);
        PackageDependencyTraversalOutcome browserOutcome = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    browserRootEvidence,
                    PackageDependencyTraversalExpansionAuthority.RecursiveSources),
            ],
            fixture.CandidateResolver,
            fixture.ManifestAcquirer);

        // CLI desktop-style: DesktopPackageSourceComposition over a real local-folder
        // feed, restricted to that folder so no ambient nuget.config is consulted.
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(30));
        var sourceOptions = new NuGetSourceOptions { Sources = [folder] };
        IPackageDependencyTraversalCandidateResolver desktopResolver =
            new PackageDependencyTraversalCandidateAdapter(
                new DesktopPackageDependencyCandidateSource(
                    composition,
                    sourceOptions));
        IPackageDependencyTraversalManifestAcquirer desktopAcquirer =
            new DesktopPackageDependencyTraversalManifestSource(composition);

        PackageDependencyEvidenceRoot desktopRootEvidence = BuildRoot(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            requestedFramework: null);
        PackageDependencyTraversalOutcome desktopOutcome = await ExecuteAsync(
            [
                new PackageDependencyTraversalRootOccurrence(
                    desktopRootEvidence,
                    PackageDependencyTraversalExpansionAuthority.RecursiveSources),
            ],
            desktopResolver,
            desktopAcquirer);

        Assert.Equal(
            browserOutcome.Nodes.Select(node => node.Coordinate).OrderBy(c => c.PackageId),
            desktopOutcome.Nodes.Select(node => node.Coordinate).OrderBy(c => c.PackageId));
        Assert.Equal(
            SemanticEdges(browserOutcome),
            SemanticEdges(desktopOutcome));
        Assert.True(browserOutcome.IsComplete);
        Assert.True(desktopOutcome.IsComplete);
        Assert.False(fixture.Client.IsDisposed);

        static string[] SemanticEdges(
            PackageDependencyTraversalOutcome outcome) =>
        [
            .. outcome.Edges.Select(edge =>
            {
                string target = edge.Target switch
                {
                    PackageDependencyTraversalEdgeTarget.Node node =>
                        outcome.Nodes[node.NodeIndex].Coordinate.ToString(),
                    PackageDependencyTraversalEdgeTarget.DeclarationBoundary =>
                        "declaration-boundary",
                    PackageDependencyTraversalEdgeTarget.FailedResolution =>
                        "failed-resolution",
                    PackageDependencyTraversalEdgeTarget.WorkBudget =>
                        "work-budget",
                    _ => throw new InvalidOperationException(),
                };
                return $"{edge.SourceCoordinate}|"
                    + $"{edge.Declaration.CanonicalPackageId}|"
                    + $"{edge.Declaration.CanonicalVersionConstraint}|"
                    + $"{edge.Authority}|{target}";
            }),
        ];
    }

    [Fact]
    public async Task Traversal_HostManifestFallbackPreservesSourceDiagnostics()
    {
        PackageDependencyTraversalRootOccurrence root = Root(
            "roota",
            "1.0.0",
            Dependency("dependency", "[1.2.3]"),
            PackageDependencyTraversalExpansionAuthority.RecursiveSources);
        PackageSource missingSource = new(
            "missing",
            "https://missing.example/v3/index.json");
        PackageSource successfulSource = new(
            "successful",
            "https://successful.example/v3/index.json");
        var authorization = new UniformPackageSourceAuthorization(
            [missingSource, successfulSource]);
        PackageSourceAuthorization dependencyAuthorization =
            authorization.AuthorizeSourcesFor("dependency");
        var missingRegistry = new PackageRegistry();
        var successfulRegistry = new PackageRegistry();
        successfulRegistry.Add("dependency", "1.2.3", "");
        using var missingClient = new RegistryPackageSourceClient(
            CreateResultFactoryFor(dependencyAuthorization.Authorities[0]),
            missingRegistry);
        using var successfulClient = new RegistryPackageSourceClient(
            CreateResultFactoryFor(dependencyAuthorization.Authorities[1]),
            successfulRegistry);
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                authority => ReferenceEquals(
                    authority.Association,
                    dependencyAuthorization.Authorities[0].Association)
                        ? missingClient
                        : successfulClient);
        var candidateSource = new AuthorizedPackageDependencyCandidateSource(
            authorization,
            lease.CreateAuthorization());
        PackageDependencyTraversalOutcome explicitOutcome = await ExecuteAsync(
            [root],
            new PackageDependencyTraversalCandidateAdapter(candidateSource),
            new AuthorizedPackageDependencyManifestSource(candidateSource));

        PackageDependencyTraversalProjection explicitProjection = FindProjection(
            explicitOutcome,
            PackageSourceCoordinate.Create("dependency", "1.2.3"));
        AssertManifestFallbackDiagnostics(explicitOutcome, explicitProjection);

        string desktopSources = CreateTemporaryDirectory();
        string missingFolder = Directory.CreateDirectory(
            Path.Combine(desktopSources, "a-missing")).FullName;
        string successfulFolder = Directory.CreateDirectory(
            Path.Combine(desktopSources, "b-successful")).FullName;
        WriteLocalSourcePackage(successfulFolder, "dependency", "1.2.3", "");
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(30));
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [missingFolder, successfulFolder],
        };
        PackageDependencyTraversalOutcome desktopOutcome = await ExecuteAsync(
            [root],
            new PackageDependencyTraversalCandidateAdapter(
                new DesktopPackageDependencyCandidateSource(
                    composition,
                    sourceOptions)),
            new DesktopPackageDependencyTraversalManifestSource(composition));

        PackageDependencyTraversalProjection desktopProjection = FindProjection(
            desktopOutcome,
            PackageSourceCoordinate.Create("dependency", "1.2.3"));
        AssertManifestFallbackDiagnostics(desktopOutcome, desktopProjection);

        static void AssertManifestFallbackDiagnostics(
            PackageDependencyTraversalOutcome outcome,
            PackageDependencyTraversalProjection projection)
        {
            PackageAuthorityFailure diagnostic = Assert.Single(
                projection.Diagnostics);
            Assert.IsType<InertString>(diagnostic.Authority);
            Assert.NotNull(diagnostic.SourceFailure);
            Assert.Empty(outcome.Failures);
            Assert.True(outcome.IsComplete);
        }
    }

    [Fact]
    public async Task Traversal_FailedManifestProjectionRetainsFallbackSourceDiagnostics()
    {
        string desktopSources = CreateTemporaryDirectory();
        string missingFolder = Directory.CreateDirectory(
            Path.Combine(desktopSources, "a-missing")).FullName;
        string invalidFolder = Directory.CreateDirectory(
            Path.Combine(desktopSources, "b-invalid")).FullName;
        WriteLocalSourcePackage(
            invalidFolder,
            "dependency",
            "1.2.3",
            Dependency("child", "not-a-version"));
        await using var composition = new DesktopPackageSourceComposition(
            TimeSpan.FromSeconds(30));
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [missingFolder, invalidFolder],
        };

        PackageDependencyTraversalOutcome outcome = await ExecuteAsync(
            [
                Root(
                    "roota",
                    "1.0.0",
                    Dependency("dependency", "[1.2.3]"),
                    PackageDependencyTraversalExpansionAuthority.RecursiveSources),
            ],
            new PackageDependencyTraversalCandidateAdapter(
                new DesktopPackageDependencyCandidateSource(
                    composition,
                    sourceOptions)),
            new DesktopPackageDependencyTraversalManifestSource(composition));

        PackageDependencyTraversalProjection projection = FindProjection(
            outcome,
            PackageSourceCoordinate.Create("dependency", "1.2.3"));
        PackageAuthorityFailure diagnostic = Assert.Single(
            projection.Diagnostics);
        Assert.IsType<InertString>(diagnostic.Authority);
        Assert.NotNull(diagnostic.SourceFailure);
        var identity = Assert.IsType<
            PackageDependencyTraversalManifestFailureDetail.Identity>(
                Assert.Single(outcome.Failures).Detail);
        Assert.Equal(
            PackageManifestFailureReason.InvalidDependencyContract,
            identity.Failure.Reason);
        Assert.Equal(
            PackageDependencyTraversalRootCompletion.Partial,
            outcome.Roots[0].Completion);
    }

    // ---- Shared fixtures and helpers ----

    private static PackageDependencyTraversalRequest BuildRequest(
        ImmutableArray<PackageDependencyTraversalRootOccurrence> roots,
        IPackageDependencyTraversalCandidateResolver resolver,
        IPackageDependencyTraversalManifestAcquirer acquirer,
        int? maxDepth = null,
        int maxManifestProjections = DefaultManifestBudget,
        int maxDeclarationResolutions = DefaultDeclarationBudget,
        PackageDependencyTraversalFrameworkMode? frameworkMode = null) =>
        new(
            roots,
            frameworkMode ?? new PackageDependencyTraversalFrameworkMode.ManifestDefault(),
            resolver,
            acquirer,
            new PackageDependencyTraversalWorkBudget(
                maxManifestProjections,
                maxDeclarationResolutions),
            maxDepth);

    private static Task<PackageDependencyTraversalOutcome> ExecuteAsync(
        ImmutableArray<PackageDependencyTraversalRootOccurrence> roots,
        IPackageDependencyTraversalCandidateResolver resolver,
        IPackageDependencyTraversalManifestAcquirer acquirer,
        int? maxDepth = null,
        int maxManifestProjections = DefaultManifestBudget,
        int maxDeclarationResolutions = DefaultDeclarationBudget,
        PackageDependencyTraversalFrameworkMode? frameworkMode = null) =>
        PackageDependencyTraversalQuery.ExecuteAsync(
            BuildRequest(
                roots,
                resolver,
                acquirer,
                maxDepth,
                maxManifestProjections,
                maxDeclarationResolutions,
                frameworkMode),
            TestContext.Current.CancellationToken);

    private static PackageDependencyTraversalCandidateResult.Resolved Resolved(
        PackageAcquisitionCandidate candidate) =>
        new(candidate, []);

    private static PackageAcquisitionCandidate Pinned(
        PackageAcquisitionCandidateIssuer issuer,
        PackageSourceAuthorization authorization,
        string packageId,
        string version)
    {
        PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(
            packageId,
            version);
        PackageAcquisitionCandidateResult result = issuer.ResolvePinnedCandidate(
            authorization,
            coordinate);
        return Assert.IsType<PackageAcquisitionCandidateResult>(result).Candidate!;
    }

    private static PackageSourceAuthorization Authorize(string sourceName) =>
        PackageSourceAuthorization.Authorize(
            [new PackageSource(sourceName, $"https://{sourceName}.example/v3/index.json")]);

    private static string Dependency(
        string id,
        string constraint,
        string targetFramework = "") =>
        string.IsNullOrEmpty(targetFramework)
            ? $"""<dependency id="{id}" version="{constraint}" />"""
            : $"""
              <group targetFramework="{targetFramework}">
                <dependency id="{id}" version="{constraint}" />
              </group>
              """;

    private static byte[] ManifestBytes(
        string packageId,
        string version,
        string dependenciesXml) =>
        Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
                <authors>Traversal Test</authors>
                <description>Traversal test package.</description>
                <dependencies>
                  {{dependenciesXml}}
                </dependencies>
              </metadata>
            </package>
            """);

    private static PackageDependencyEvidenceRoot BuildRoot(
        string packageId,
        string version,
        string dependenciesXml,
        string? requestedFramework) =>
        Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            Assert.IsType<PackageManifestFactsResult.Available>(
                                PackageManifestFactsQuery.ExecuteSelfAttested(
                                    ManifestBytes(
                                        packageId,
                                        version,
                                        dependenciesXml))).Value,
                            PackageDependencyEvidenceAcquisitionForm.PackageArchive,
                            requestedFramework),
                    ])).Roots);

    private static PackageDependencyEvidenceRoot FindProjectionEvidence(
        PackageDependencyTraversalOutcome outcome,
        PackageSourceCoordinate coordinate)
    {
        return FindProjection(outcome, coordinate).Evidence!;
    }

    private static PackageDependencyTraversalProjection FindProjection(
        PackageDependencyTraversalOutcome outcome,
        PackageSourceCoordinate coordinate)
    {
        int nodeIndex = outcome.Nodes.ToList().FindIndex(
            node => node.Coordinate == coordinate);
        return outcome.Projections.Single(
            projection => projection.NodeIndex == nodeIndex
                && projection.Kind
                    == PackageDependencyTraversalProjectionKind.CandidateAcquired);
    }

    private static PackageDependencyTraversalRootOccurrence Root(
        string packageId,
        string version,
        string dependenciesXml,
        PackageDependencyTraversalExpansionAuthority authority,
        string? requestedFramework = null) =>
        new(
            BuildRoot(packageId, version, dependenciesXml, requestedFramework),
            authority);

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "traversal-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteLocalSourcePackage(
        string folder,
        string packageId,
        string version,
        string dependenciesXml)
    {
        string path = Path.Combine(folder, $"{packageId}.{version}.nupkg");
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry($"{packageId}.nuspec");
        using Stream entryStream = entry.Open();
        entryStream.Write(ManifestBytes(packageId, version, dependenciesXml));
    }

    private sealed class StubCandidateResolver : IPackageDependencyTraversalCandidateResolver
    {
        private readonly Dictionary<
            (string SourcePackageId, string DependencyId),
            Func<PackageDependencyTraversalCandidateResult>> _byDeclaration = [];

        public int CallCount { get; private set; }

        public void SetResponse(
            string sourcePackageId,
            string dependencyId,
            Func<PackageDependencyTraversalCandidateResult> response) =>
            _byDeclaration[
                (sourcePackageId.ToLowerInvariant(), dependencyId.ToLowerInvariant())] =
                response;

        public ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
            PackageDependencyEvidenceDeclaration declaration,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            string sourcePackageId = SourcePackageIdOf(declaration);
            var key = (sourcePackageId, declaration.CanonicalPackageId);
            if (!_byDeclaration.TryGetValue(
                    key,
                    out Func<PackageDependencyTraversalCandidateResult>? response))
            {
                throw new InvalidOperationException(
                    $"No stub response was configured for '{sourcePackageId}' -> '{declaration.CanonicalPackageId}'.");
            }

            return ValueTask.FromResult(response());
        }

        internal static string SourcePackageIdOf(
            PackageDependencyEvidenceDeclaration declaration) =>
            declaration.Identity.Group
                is PackageDependencyEvidenceGroupIdentity.Package package
                ? package.Root.Coordinate.PackageId
                : throw new InvalidOperationException(
                    "The traversal test stub only supports package-owned declarations.");
    }

    private sealed class DelayingCandidateResolver :
        IPackageDependencyTraversalCandidateResolver
    {
        private readonly Dictionary<
            string,
            (TimeSpan Delay, Func<PackageDependencyTraversalCandidateResult> Response)>
            _byDependencyId = new(StringComparer.OrdinalIgnoreCase);

        public void SetResponse(
            string dependencyId,
            TimeSpan delay,
            Func<PackageDependencyTraversalCandidateResult> response) =>
            _byDependencyId[dependencyId] = (delay, response);

        public async ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
            PackageDependencyEvidenceDeclaration declaration,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            (TimeSpan delay, Func<PackageDependencyTraversalCandidateResult> response) =
                _byDependencyId[declaration.CanonicalPackageId];
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return response();
        }
    }

    private sealed class CancelingCandidateResolver(
        CancellationTokenSource cancellation) :
        IPackageDependencyTraversalCandidateResolver
    {
        public async ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
            PackageDependencyEvidenceDeclaration declaration,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellation.Cancel();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                "Cancellation did not stop candidate resolution.");
        }
    }

    private sealed class CancelingManifestAcquirer(
        CancellationTokenSource cancellation,
        PackageSourceManifest manifest) :
        IPackageDependencyTraversalManifestAcquirer
    {
        public Task<PackageDependencyTraversalManifestResult> AcquireAsync(
            PackageAcquisitionCandidate candidate,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            cancellation.Cancel();
            return Task.FromResult<PackageDependencyTraversalManifestResult>(
                new PackageDependencyTraversalManifestResult.Acquired(
                    manifest,
                    []));
        }
    }

    private sealed class StubManifestAcquirer : IPackageDependencyTraversalManifestAcquirer
    {
        private readonly Dictionary<
            PackageAcquisitionCandidateCorrespondence,
            Func<PackageDependencyTraversalManifestResult>> _byCorrespondence = [];
        private readonly Dictionary<PackageSourceCoordinate, int> _callsByCoordinate = [];

        public int CallCount { get; private set; }

        public int CallsFor(PackageSourceCoordinate coordinate) =>
            _callsByCoordinate.TryGetValue(coordinate, out int count) ? count : 0;

        public void SetManifest(PackageAcquisitionCandidate candidate, byte[] bytes)
        {
            SetManifest(candidate, candidate.Coordinate, bytes);
        }

        public void SetManifest(
            PackageAcquisitionCandidate candidate,
            PackageSourceCoordinate manifestCoordinate,
            byte[] bytes)
        {
            PackageSourceResultFactory factory = CreateResultFactoryFor(
                candidate.Authorities[0].Authority);
            _byCorrespondence[candidate.Correspondence] = () =>
                new PackageDependencyTraversalManifestResult.Acquired(
                    factory.Manifest(manifestCoordinate, bytes),
                    []);
        }

        public Task<PackageDependencyTraversalManifestResult> AcquireAsync(
            PackageAcquisitionCandidate candidate,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            CallCount++;
            _callsByCoordinate[candidate.Coordinate] =
                CallsFor(candidate.Coordinate) + 1;
            cancellationToken.ThrowIfCancellationRequested();
            if (!_byCorrespondence.TryGetValue(
                    candidate.Correspondence,
                    out Func<PackageDependencyTraversalManifestResult>? response))
            {
                throw new InvalidOperationException(
                    $"No stub manifest was configured for '{candidate.Coordinate.PackageId}'.");
            }

            return Task.FromResult(response());
        }
    }

    private static PackageSourceResultFactory CreateResultFactoryFor(
        ConfiguredPackageAuthority authority)
    {
        PackageSourceResultFactory? captured = null;
        using IPackageSourceClient client = PackageSourceClientFactory.CreateCustom(
            PackageSourceDescriptor.NuGetGallery,
            authority.Association,
            factory =>
            {
                captured = factory;
                return new UnusedPackageSourceClient(factory.Source);
            });
        return captured!;
    }

    private sealed class UnusedPackageSourceClient(PackageSourceResultIdentity source) :
        IPackageSourceClient
    {
        public PackageSourceResultIdentity Source { get; } = source;
        public PackageSourceCapabilities Capabilities => PackageSourceCapabilities.None;

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>>
            SearchByPrefixAsync(
                string prefix,
                int take = 100,
                bool prerelease = false,
                CancellationToken cancellationToken = default,
                NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    /// <summary>An in-memory package feed used by the product adapters directly.</summary>
    private sealed class PackageRegistry
    {
        private readonly Dictionary<string, List<string>> _versions =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, byte[]> _bytesByCoordinate =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(string packageId, string version, string dependenciesXml)
        {
            AddVersionOnly(packageId, version);
            _bytesByCoordinate[Key(packageId, version)] =
                ManifestBytes(packageId, version, dependenciesXml);
        }

        public void AddVersionOnly(string packageId, string version)
        {
            if (!_versions.TryGetValue(packageId, out List<string>? list))
            {
                list = [];
                _versions[packageId] = list;
            }

            list.Add(version);
        }

        public IReadOnlyList<string> VersionsOf(string packageId) =>
            _versions.TryGetValue(packageId, out List<string>? list)
                ? list
                : [];

        public byte[]? BytesOf(string packageId, string version) =>
            _bytesByCoordinate.TryGetValue(
                Key(packageId, version),
                out byte[]? bytes)
                ? bytes
                : null;

        private static string Key(string packageId, string version) =>
            $"{packageId}@{version}";
    }

    private sealed class RegistryPackageSourceClient(
        PackageSourceResultFactory factory,
        PackageRegistry registry) : IPackageSourceClient
    {
        private bool _disposed;

        public bool FailVersionDiscovery { get; set; }

        public bool ForbidPayloadAndSymbols { get; set; }

        public bool DelayManifestBeyondOperationDeadline { get; set; }

        public int VersionCalls { get; private set; }

        public int PackageOrSymbolCalls { get; private set; }

        public bool IsDisposed => _disposed;

        public PackageSourceResultIdentity Source => factory.Source;

        public PackageSourceCapabilities Capabilities =>
            PackageSourceCapabilities.VersionEnumeration
            | PackageSourceCapabilities.Manifest;

        public Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VersionCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (FailVersionDiscovery)
                return Task.FromResult(factory.FailedVersions(PackageSourceFailureKind.Transport));

            PackageCandidateObservation[] candidates =
            [
                .. registry.VersionsOf(packageId).Select(
                    version => factory.Candidate(
                        PackageSourceCoordinate.Create(packageId, version),
                        PackageDiscoveryContract.CompleteVersionEnumeration,
                        PackageListingState.Listed)),
            ];
            return Task.FromResult(
                factory.SucceededVersions(
                    factory.Versions(candidates, hasAuthoritativeListingState: true)));
        }

        public async Task<PackageSourceOperationResult<PackageSourceManifest>>
            GetManifestAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (DelayManifestBeyondOperationDeadline)
            {
                await Task.Delay(
                    500,
                    TestContext.Current.CancellationToken).ConfigureAwait(false);
            }

            PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(
                packageId,
                version);
            byte[]? bytes = registry.BytesOf(packageId, version);
            return bytes is null
                ? factory.FailedManifest(
                    coordinate,
                    PackageSourceFailureKind.NotFound)
                : factory.SucceededManifest(
                    coordinate,
                    factory.Manifest(coordinate, bytes));
        }

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
            string query,
            int take = 20,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
            string prefix,
            int take = 100,
            bool prerelease = false,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            throw new NotSupportedException();

        public Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            PackageOrSymbolCalls++;
            if (ForbidPayloadAndSymbols)
            {
                throw new InvalidOperationException(
                    "Manifest-only traversal must never acquire a package archive.");
            }

            throw new NotSupportedException();
        }

        public Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            PackageOrSymbolCalls++;
            if (ForbidPayloadAndSymbols)
            {
                throw new InvalidOperationException(
                    "Manifest-only traversal must never acquire symbol content.");
            }

            throw new NotSupportedException();
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// Wires the real #5765 <see cref="PackageDependencyCandidateQuery"/> and the
    /// real candidate-authorized exact manifest capability over one fake, in-memory
    /// registry-backed source client.
    /// </summary>
    private sealed class RegistryFixture : IAsyncDisposable
    {
        public PackageRegistry Registry { get; } = new();

        public RegistryPackageSourceClient Client { get; }

        public PackageSourceSettlementLease Lease { get; }

        public IPackageDependencyTraversalCandidateResolver CandidateResolver { get; }

        public IPackageDependencyTraversalManifestAcquirer ManifestAcquirer { get; }

        public RegistryFixture()
        {
            var authorization = new UniformPackageSourceAuthorization(
                [new PackageSource("registry", "https://registry.example/v3/index.json")]);
            ConfiguredPackageAuthority authority = authorization
                .AuthorizeSourcesFor("probe")
                .Authorities[0];
            PackageSourceResultFactory factory = CreateResultFactoryFor(authority);
            Client = new RegistryPackageSourceClient(factory, Registry);
            Lease = PackageSourceSettlementService.IssueLease(
                _ => Client);
            var candidateSource =
                new AuthorizedPackageDependencyCandidateSource(
                    authorization,
                    Lease.CreateAuthorization());
            CandidateResolver = new PackageDependencyTraversalCandidateAdapter(
                candidateSource);
            ManifestAcquirer = new AuthorizedPackageDependencyManifestSource(
                candidateSource);
        }

        public async ValueTask DisposeAsync()
        {
            await Lease.DisposeAsync();
            Client.Dispose();
        }
    }
}
