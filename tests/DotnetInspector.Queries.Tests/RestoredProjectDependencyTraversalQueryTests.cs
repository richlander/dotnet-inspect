using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using InertText;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Focused Release gates for the restored-project dependency traversal contract in
/// <c>docs/design/restored-project-dependency-traversal.md</c>.
/// </summary>
public sealed class RestoredProjectDependencyTraversalQueryTests
{
    const string Bidi = "\u202E";

    // ---- The pathological App -> ProjectB -> PackageC shape ----------------

    [Fact]
    public void Traversal_DepthOneAdmitsRootProjectRelationshipAndBoundsPackageRelationship()
    {
        RestoredProjectDependencyTraversal traversal = Traverse(
            AppProjectPackageDocument(),
            maximumDepth: 1);

        Assert.Equal(1, traversal.MaximumDepth);
        Assert.Equal(RestoredProjectTraversalCompletion.DepthBounded, traversal.Completion);
        Assert.Empty(traversal.Failures);

        RestoredProjectTraversalProjectRelationship relationship = Assert.Single(traversal.ProjectRelationships);
        Assert.IsType<RestoredProjectGraphParentIdentity.Root>(relationship.Parent);
        Assert.Equal(1, relationship.Distance);
        Assert.Empty(traversal.PackageRelationships);

        string[] expectedNodes = ["root@0", "project@1"];
        Assert.Equal(expectedNodes, traversal.Nodes.Select(Describe));

        RestoredProjectTraversalDepthBoundary boundary = Assert.Single(traversal.DepthBoundaries);
        Assert.Equal(
            relationship.Dependency,
            Assert.IsType<RestoredProjectGraphParentIdentity.Project>(boundary.Node).Identity);
        Assert.Equal(1, boundary.MaximumDepth);
    }

    [Fact]
    public void Traversal_DepthTwoAdmitsProjectToPackageRelationshipAtDistanceTwo()
    {
        RestoredProjectDependencyTraversal traversal = Traverse(
            AppProjectPackageDocument(),
            maximumDepth: 2);

        Assert.Equal(RestoredProjectTraversalCompletion.Complete, traversal.Completion);
        Assert.Empty(traversal.DepthBoundaries);
        Assert.Single(traversal.ProjectRelationships);

        RestoredProjectTraversalPackageRelationship package = Assert.Single(traversal.PackageRelationships);
        Assert.IsType<RestoredProjectGraphParentIdentity.Project>(package.Edge.Parent);
        Assert.Equal("packagec", package.Edge.Dependency.Coordinate.PackageId);
        Assert.Equal(2, package.Distance);

        string[] expectedNodes = ["root@0", "project@1", "package@2"];
        Assert.Equal(expectedNodes, traversal.Nodes.Select(Describe));
    }

    [Fact]
    public void Traversal_UnboundedTraversalCompletesAtPackageLeaf()
    {
        RestoredProjectDependencyTraversal traversal = Traverse(AppProjectPackageDocument());

        Assert.Null(traversal.MaximumDepth);
        Assert.Equal(RestoredProjectTraversalCompletion.Complete, traversal.Completion);
        Assert.True(traversal.IsComplete);
        Assert.Empty(traversal.DepthBoundaries);
        Assert.Equal(2, Assert.Single(traversal.PackageRelationships).Distance);
    }

    [Fact]
    public void Traversal_DepthZeroAdmitsOnlyTheRootAndBoundsEveryRelationship()
    {
        RestoredProjectDependencyTraversal traversal = Traverse(
            AppProjectPackageDocument(),
            maximumDepth: 0);

        Assert.Equal(RestoredProjectTraversalCompletion.DepthBounded, traversal.Completion);
        string[] expectedNodes = ["root@0"];
        Assert.Equal(expectedNodes, traversal.Nodes.Select(Describe));
        Assert.Empty(traversal.ProjectRelationships);
        Assert.Empty(traversal.PackageRelationships);
        Assert.IsType<RestoredProjectGraphParentIdentity.Root>(
            Assert.Single(traversal.DepthBoundaries).Node);
    }

    [Fact]
    public void Traversal_RootRelativeDepthIsUnknowableFromFactsAlone()
    {
        byte[] bytes = AppProjectPackageDocument();

        // The facts owner reports only the package-resolving edge, whose parent is ProjectB. This
        // is the exact gap this owner closes: nothing in the facts graph connects the root to it.
        RestoredProjectGraphResult.Available graph = Assert.IsType<RestoredProjectGraphResult.Available>(
            Available(RestoredProjectDependencyFactsQuery.Execute(bytes)).Graph);
        RestoredProjectGraphEdge edge = Assert.Single(graph.Edges);
        Assert.IsType<RestoredProjectGraphParentIdentity.Project>(edge.Parent);
        Assert.DoesNotContain(
            graph.Edges,
            candidate => candidate.Parent is RestoredProjectGraphParentIdentity.Root);

        RestoredProjectDependencyTraversal traversal = Traverse(bytes);
        Assert.Single(traversal.ProjectRelationships);
        Assert.Equal(1, traversal.ProjectRelationships[0].Distance);
    }

    // ---- Cycles, revisits, and diamonds ------------------------------------

    [Fact]
    public void Traversal_ProjectCycleTerminatesAndRetainsClosingRelationship()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["ProjectB/1.0.0"] = ProjectNode(("ProjectC", "1.0.0")),
                    ["ProjectC/1.0.0"] = ProjectNode(("ProjectB", "1.0.0")),
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("ProjectB >= 1.0.0") },
            frameworks: EmptyFrameworks);

        RestoredProjectDependencyTraversal traversal = Traverse(bytes);

        Assert.Equal(RestoredProjectTraversalCompletion.Complete, traversal.Completion);
        string[] expectedNodes = ["project@1", "project@2", "root@0"];
        Assert.Equal(expectedNodes, traversal.Nodes.Select(Describe).Order());

        // Three distinct relationships survive, including the one closing the cycle back onto an
        // already-visited project, and the walk terminates.
        Assert.Equal(3, traversal.ProjectRelationships.Length);
        int[] expectedDistances = [1, 2, 3];
        Assert.Equal(expectedDistances, traversal.ProjectRelationships.Select(r => r.Distance).Order());
        Assert.Equal(
            1,
            traversal.ProjectRelationships.Count(r => r.Parent is RestoredProjectGraphParentIdentity.Root));
        Assert.Equal(
            2,
            traversal.ProjectRelationships.Count(r => r.Parent is RestoredProjectGraphParentIdentity.Project));
        Assert.Equal(
            3,
            traversal.ProjectRelationships.Select(r => r.Identity).Distinct().Count());
    }

    [Fact]
    public void Traversal_ProjectDiamondRetainsBothRelationshipsAtMinimumDistance()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["ProjectB/1.0.0"] = ProjectNode(("ProjectD", "1.0.0")),
                    ["ProjectC/1.0.0"] = ProjectNode(("ProjectD", "1.0.0")),
                    ["ProjectD/1.0.0"] = ProjectNode(("PackageE", "1.0.0")),
                    ["PackageE/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject
            {
                ["net11.0"] = new JsonArray("ProjectB >= 1.0.0", "ProjectC >= 1.0.0"),
            },
            frameworks: EmptyFrameworks);

        RestoredProjectDependencyTraversal traversal = Traverse(bytes);

        Assert.Equal(RestoredProjectTraversalCompletion.Complete, traversal.Completion);
        Assert.Equal(4, traversal.ProjectRelationships.Length);

        RestoredProjectProjectNodeIdentity shared = traversal.ProjectRelationships
            .GroupBy(relationship => relationship.Dependency)
            .Single(group => group.Count() == 2)
            .Key;

        // Both parents keep their own relationship, and the shared node keeps one minimum distance.
        Assert.All(
            traversal.ProjectRelationships.Where(relationship => relationship.Dependency == shared),
            relationship => Assert.Equal(2, relationship.Distance));
        Assert.Equal(
            2,
            traversal.Nodes
                .Single(node => node.Identity is RestoredProjectGraphParentIdentity.Project project
                    && project.Identity == shared)
                .MinimumDistance);
        Assert.Equal(3, Assert.Single(traversal.PackageRelationships).Distance);
    }

    // ---- Target selection is shared, never reimplemented --------------------

    [Theory]
    [InlineData(null, null)]
    [InlineData("net10.0", null)]
    [InlineData("net11.0", null)]
    [InlineData("net11.0", "linux-x64")]
    public void Traversal_TargetSelectionIsSharedWithTheFactsOwner(string? framework, string? runtimeIdentifier)
    {
        byte[] bytes = ReadCopiedAssetsBytes();
        RestoredProjectTargetRequest? target = framework is null
            ? null
            : new RestoredProjectTargetRequest(framework, runtimeIdentifier);

        RestoredProjectDependencyFacts expected =
            Available(RestoredProjectDependencyFactsQuery.Execute(bytes, target));
        RestoredProjectDependencyTraversal traversal = Traverse(bytes, target);

        Assert.Equal(expected.SelectionIdentity, traversal.Facts.SelectionIdentity);
        Assert.Equal(expected.ContentProvenance, traversal.Facts.ContentProvenance);
        Assert.Equal(
            expected.SelectedTarget?.FrameworkIdentity,
            traversal.Facts.SelectedTarget?.FrameworkIdentity);
        Assert.Equal(
            expected.SelectedTarget?.RuntimeIdentifierIdentity,
            traversal.Facts.SelectedTarget?.RuntimeIdentifierIdentity);
        Assert.Equal(
            target is null
                ? RestoredProjectTargetSelectionProvenance.Default
                : RestoredProjectTargetSelectionProvenance.Requested,
            traversal.Facts.SelectedTarget!.Provenance);
    }

    // ---- Locator equivalence apart from provenance --------------------------

    [Fact]
    public void Traversal_CsprojLocatorAndDirectAssetsBytesProduceOneTraversalIdentity()
    {
        string projectDirectory = FixtureCatalog.RestoredProjectDependencyFacts.ProjectDirectory();
        string csproj = Path.Combine(projectDirectory, "DotnetInspector.RestoredProjectFixtures.csproj");

        Assert.True(
            ProjectAssetsParser.TryFindAssets(csproj, out string? fromCsproj, out ProjectAssetsStatus csprojStatus));
        Assert.Equal(ProjectAssetsStatus.Found, csprojStatus);
        Assert.True(
            ProjectAssetsParser.TryFindAssets(
                projectDirectory,
                out string? fromDirectory,
                out ProjectAssetsStatus directoryStatus));
        Assert.Equal(ProjectAssetsStatus.Found, directoryStatus);
        Assert.Equal(fromCsproj, fromDirectory);

        // Locator provenance stays with the host: the query is reached with bytes only.
        RestoredProjectDependencyTraversal fromLocator = Traverse(File.ReadAllBytes(fromCsproj!));
        RestoredProjectDependencyTraversal fromDirect = Traverse(ReadCopiedAssetsBytes());

        Assert.Equal(fromLocator.Identity.Selection, fromDirect.Identity.Selection);
        Assert.Equal(fromLocator.Identity.TopologyDigest, fromDirect.Identity.TopologyDigest);
        Assert.Equal(Describe(fromLocator), Describe(fromDirect));
    }

    [Fact]
    public void Traversal_RestoredFixtureProjectReferenceIsRootRelative()
    {
        RestoredProjectDependencyTraversal traversal = Traverse(
            ReadCopiedAssetsBytes(),
            new RestoredProjectTargetRequest("net11.0"));

        RestoredProjectTraversalProjectRelationship rootReference = Assert.Single(
            traversal.ProjectRelationships,
            relationship => relationship.Parent is RestoredProjectGraphParentIdentity.Root);
        Assert.Equal(1, rootReference.Distance);
        Assert.StartsWith(
            "sha256:",
            rootReference.Dependency.SourceIdentity,
            StringComparison.Ordinal);
        Assert.Contains(traversal.PackageRelationships, relationship => relationship.Distance >= 1);
    }

    // ---- Package relationships retain the facts owner's exact evidence ------

    [Fact]
    public void Traversal_PackageRelationshipRetainsTheExactFactsEdgeEvidence()
    {
        byte[] bytes = AppProjectPackageDocument();
        RestoredProjectDependencyTraversal traversal = Traverse(bytes);

        RestoredProjectGraphResult.Available published =
            Assert.IsType<RestoredProjectGraphResult.Available>(traversal.Facts.Graph);
        RestoredProjectGraphEdge admitted = Assert.Single(traversal.PackageRelationships).Edge;

        // Not an equal copy: the traversal publishes the facts owner's own edge instance.
        Assert.Same(Assert.Single(published.Edges), admitted);

        RestoredProjectGraphEdge independent = Assert.Single(
            Assert.IsType<RestoredProjectGraphResult.Available>(
                Available(RestoredProjectDependencyFactsQuery.Execute(bytes)).Graph).Edges);
        Assert.Equal(independent.Identity, admitted.Identity);
        Assert.Equal(independent.Dependency, admitted.Dependency);
        Assert.Equal(independent.CanonicalVersionConstraint, admitted.CanonicalVersionConstraint);
        Assert.Equal(
            independent.SourceVersionConstraintSpelling.ToString(),
            admitted.SourceVersionConstraintSpelling.ToString());
        Assert.Equal(independent.Role, admitted.Role);
        Assert.Equal(independent.DeclarationAssociation, admitted.DeclarationAssociation);
    }

    // ---- Identity ----------------------------------------------------------

    [Fact]
    public void Traversal_ProjectOnlyTopologyChangeMovesTraversalIdentityNotSelectionIdentity()
    {
        byte[] baseline = AppProjectPackageDocument();
        byte[] extended = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["ProjectB/1.0.0"] = ProjectNode(("PackageC", "1.0.0"), ("ProjectE", "1.0.0")),
                    ["ProjectE/1.0.0"] = new JsonObject { ["type"] = "project" },
                    ["PackageC/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("ProjectB >= 1.0.0") },
            frameworks: EmptyFrameworks);

        RestoredProjectDependencyTraversal baselineTraversal = Traverse(baseline);
        RestoredProjectDependencyTraversal extendedTraversal = Traverse(extended);

        // The added project branch resolves no package, so the facts owner's evidence is identical.
        Assert.Equal(
            baselineTraversal.Facts.SelectionIdentity,
            extendedTraversal.Facts.SelectionIdentity);
        Assert.NotEqual(
            baselineTraversal.Identity.TopologyDigest,
            extendedTraversal.Identity.TopologyDigest);
        Assert.Single(baselineTraversal.ProjectRelationships);
        Assert.Equal(2, extendedTraversal.ProjectRelationships.Length);
    }

    [Fact]
    public void Traversal_JsonPropertyOrderChangesNeitherTraversalIdentityNorOrdering()
    {
        byte[] bytes = AppProjectPackageDocument();
        RestoredProjectDependencyTraversal traversal = Traverse(bytes);
        RestoredProjectDependencyTraversal reordered = Traverse(WithReversedPropertyOrder(bytes));

        Assert.Equal(traversal.Identity.Selection, reordered.Identity.Selection);
        Assert.Equal(traversal.Identity.TopologyDigest, reordered.Identity.TopologyDigest);
        Assert.Equal(Describe(traversal), Describe(reordered));
    }

    [Fact]
    public void Traversal_DepthRequestChangesTraversalIdentity()
    {
        byte[] bytes = AppProjectPackageDocument();

        Assert.NotEqual(
            Traverse(bytes, maximumDepth: 1).Identity.TopologyDigest,
            Traverse(bytes, maximumDepth: 2).Identity.TopologyDigest);
    }

    [Fact]
    public void Traversal_IdentityIsCultureInvariant()
    {
        byte[] bytes = AppProjectPackageDocument();
        CultureInfo originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string expected = Traverse(bytes).Identity.TopologyDigest;

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            Assert.Equal(expected, Traverse(bytes).Identity.TopologyDigest);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ---- Containment --------------------------------------------------------

    [Fact]
    public void Traversal_HostileProjectSpellingRemainsInertBesideAnOpaqueIdentity()
    {
        string hostileProject = $"Evil{Bidi}Proj";
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    [$"{hostileProject}/1.0.0"] = new JsonObject { ["type"] = "project" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray($"{hostileProject} >= 1.0.0") },
            frameworks: EmptyFrameworks);

        RestoredProjectDependencyTraversal traversal = Traverse(bytes);

        RestoredProjectTraversalProjectRelationship relationship = Assert.Single(traversal.ProjectRelationships);
        Assert.StartsWith("sha256:", relationship.Dependency.SourceIdentity, StringComparison.Ordinal);
        Assert.DoesNotContain(Bidi, relationship.Dependency.SourceIdentity, StringComparison.Ordinal);

        InertString spelling = relationship.SourceDependencySpelling;
        Assert.True(spelling.RequiredContainment);
        Assert.DoesNotContain(Bidi, spelling.ToString(), StringComparison.Ordinal);

        RestoredProjectTraversalNode node = Assert.Single(
            traversal.Nodes,
            candidate => candidate.Identity is RestoredProjectGraphParentIdentity.Project);
        Assert.Equal(spelling.ToString(), node.SourceProjectSpelling!.Value.ToString());
        Assert.Null(
            traversal.Nodes
                .Single(candidate => candidate.Identity is RestoredProjectGraphParentIdentity.Root)
                .SourceProjectSpelling);
    }

    // ---- Failures, completion, and depth interaction -------------------------

    [Fact]
    public void Traversal_MalformedDocumentPreservesTheTypedDocumentFailure()
    {
        RestoredProjectDependencyTraversalResult result =
            RestoredProjectDependencyTraversalQuery.Execute(Encoding.UTF8.GetBytes("{"));

        RestoredProjectDependencyTraversalFailure.Document failure =
            Assert.IsType<RestoredProjectDependencyTraversalFailure.Document>(
                Assert.IsType<RestoredProjectDependencyTraversalResult.Failed>(result).Failure);
        Assert.Equal(
            RestoredProjectDependencyFailureReason.MalformedOrDuplicateBearingJson,
            failure.Failure.Reason);
    }

    [Fact]
    public void Traversal_UnsatisfiedTargetRequestIsUnavailableAndKeepsItsFacts()
    {
        RestoredProjectDependencyTraversalResult result = RestoredProjectDependencyTraversalQuery.Execute(
            AppProjectPackageDocument(),
            new RestoredProjectDependencyTraversalRequest(new RestoredProjectTargetRequest("net1.0")));

        RestoredProjectDependencyTraversalResult.Unavailable unavailable =
            Assert.IsType<RestoredProjectDependencyTraversalResult.Unavailable>(result);
        Assert.Null(unavailable.Facts.SelectedTarget);
        Assert.IsType<RestoredProjectGraphResult.Unavailable>(unavailable.Facts.Graph);
    }

    [Fact]
    public void Traversal_AmbiguousTargetIdentityPreservesTheTypedGraphFailure()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject(),
                ["NET11.0"] = new JsonObject(),
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray() },
            frameworks: EmptyFrameworks);

        RestoredProjectDependencyTraversalFailure.Graph failure =
            Assert.IsType<RestoredProjectDependencyTraversalFailure.Graph>(
                Assert.IsType<RestoredProjectDependencyTraversalResult.Failed>(
                    RestoredProjectDependencyTraversalQuery.Execute(bytes)).Failure);
        Assert.Equal(RestoredProjectGraphFailureReason.AmbiguousTargetIdentity, failure.Failure.Reason);
        Assert.NotNull(failure.Facts.ContentProvenance);
    }

    [Fact]
    public void Traversal_FailureBeyondTheDepthBoundaryDoesNotPoisonTheBoundedAnswer()
    {
        byte[] bytes = MalformedGrandchildDocument();

        RestoredProjectDependencyTraversal bounded = Traverse(bytes, maximumDepth: 2);
        Assert.Empty(bounded.Failures);
        Assert.Equal(RestoredProjectTraversalCompletion.DepthBounded, bounded.Completion);

        // The failing node is still reported as a boundary rather than an inspected leaf.
        RestoredProjectTraversalDepthBoundary boundary = Assert.Single(bounded.DepthBoundaries);
        Assert.Equal(2, boundary.MaximumDepth);

        RestoredProjectDependencyTraversal unbounded = Traverse(bytes);
        Assert.Equal(RestoredProjectTraversalCompletion.Partial, unbounded.Completion);
        Assert.Equal(
            RestoredProjectGraphFailureReason.UnresolvedDependency,
            Assert.Single(unbounded.Failures).Reason);
    }

    [Fact]
    public void Traversal_UnboundedFailuresMatchThePublishedFactsGraphFailures()
    {
        RestoredProjectDependencyTraversal traversal = Traverse(MalformedGrandchildDocument());

        RestoredProjectGraphResult.Available graph =
            Assert.IsType<RestoredProjectGraphResult.Available>(traversal.Facts.Graph);
        Assert.Equal(
            graph.Failures.Select(failure => (failure.Reason, failure.Count)),
            traversal.Failures.Select(failure => (failure.Reason, failure.Count)));
    }

    [Fact]
    public void Traversal_PartialTakesPrecedenceOverTheDepthBoundary()
    {
        byte[] bytes = SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["ProjectB/1.0.0"] = ProjectNode(("PackageC", "1.0.0")),
                    ["PackageC/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject
            {
                ["net11.0"] = new JsonArray("ProjectB >= 1.0.0", "Absent.Root.Entry >= 1.0.0"),
            },
            frameworks: EmptyFrameworks);

        RestoredProjectDependencyTraversal traversal = Traverse(bytes, maximumDepth: 1);

        // A root-owned failure lies inside depth 1, so partial wins over the ProjectB boundary.
        Assert.Equal(
            RestoredProjectGraphFailureReason.UnresolvedRootEntry,
            Assert.Single(traversal.Failures).Reason);
        Assert.NotEmpty(traversal.DepthBoundaries);
        Assert.Equal(RestoredProjectTraversalCompletion.Partial, traversal.Completion);
    }

    [Fact]
    public void Traversal_ProjectRelationshipBoundIsPartialWithoutChangingFactsCompletion()
    {
        byte[] bytes = DenseProjectMeshDocument(projects: 130);

        RestoredProjectDependencyTraversal traversal = Traverse(bytes);

        RestoredProjectGraphResult.Available graph =
            Assert.IsType<RestoredProjectGraphResult.Available>(traversal.Facts.Graph);
        Assert.True(graph.IsComplete);
        Assert.Equal(
            RestoredProjectGraphFailureReason.ConfiguredLimitExceeded,
            Assert.Single(traversal.Failures).Reason);
        Assert.Equal(RestoredProjectTraversalCompletion.Partial, traversal.Completion);
        Assert.True(
            traversal.ProjectRelationships.Length
                <= RestoredProjectDependencyTraversalQuery.MaxProjectRelationships);
    }

    [Fact]
    public void Traversal_NegativeMaximumDepthIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RestoredProjectDependencyTraversalRequest(maximumDepth: -1));
    }

    // ---- Helpers ------------------------------------------------------------

    static RestoredProjectDependencyTraversal Traverse(
        byte[] assetsBytes,
        RestoredProjectTargetRequest? target = null,
        int? maximumDepth = null) =>
        Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
            RestoredProjectDependencyTraversalQuery.Execute(
                assetsBytes,
                new RestoredProjectDependencyTraversalRequest(target, maximumDepth))).Value;

    static RestoredProjectDependencyFacts Available(RestoredProjectDependencyFactsResult result) =>
        Assert.IsType<RestoredProjectDependencyFactsResult.Available>(result).Value;

    static string Describe(RestoredProjectTraversalNode node) =>
        node.Identity switch
        {
            RestoredProjectGraphParentIdentity.Root => $"root@{node.MinimumDistance}",
            RestoredProjectGraphParentIdentity.Project => $"project@{node.MinimumDistance}",
            RestoredProjectGraphParentIdentity.Package => $"package@{node.MinimumDistance}",
            _ => "?",
        };

    /// <summary>A canonical, order-free description of every admitted traversal fact.</summary>
    static ImmutableArray<string> Describe(RestoredProjectDependencyTraversal traversal)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add($"completion={traversal.Completion}");
        lines.Add($"depth={traversal.MaximumDepth?.ToString() ?? "unbounded"}");
        foreach (RestoredProjectTraversalNode node in traversal.Nodes)
            lines.Add($"node {Describe(node)} {DescribeIdentity(node.Identity)}");
        foreach (RestoredProjectTraversalProjectRelationship relationship in traversal.ProjectRelationships)
        {
            lines.Add(
                $"project {DescribeIdentity(relationship.Parent)} -> "
                + $"{relationship.Dependency.SourceIdentity} @{relationship.Distance}");
        }

        foreach (RestoredProjectTraversalPackageRelationship relationship in traversal.PackageRelationships)
        {
            lines.Add(
                $"package {DescribeIdentity(relationship.Edge.Parent)} -> "
                + $"{relationship.Edge.Dependency.Coordinate.PackageId}/"
                + $"{relationship.Edge.Dependency.Coordinate.Version} @{relationship.Distance}");
        }

        foreach (RestoredProjectTraversalDepthBoundary boundary in traversal.DepthBoundaries)
            lines.Add($"boundary {DescribeIdentity(boundary.Node)} @{boundary.MaximumDepth}");
        foreach (RestoredProjectGraphFailure failure in traversal.Failures)
            lines.Add($"failure {failure.Reason} x{failure.Count}");
        return lines.ToImmutable();
    }

    static string DescribeIdentity(RestoredProjectGraphParentIdentity identity) => identity switch
    {
        RestoredProjectGraphParentIdentity.Root => "root",
        RestoredProjectGraphParentIdentity.Project project => project.Identity.SourceIdentity,
        RestoredProjectGraphParentIdentity.Package package =>
            $"{package.Identity.Coordinate.PackageId}/{package.Identity.Coordinate.Version}",
        _ => "?",
    };

    static JsonObject EmptyFrameworks =>
        new() { ["net11.0"] = new JsonObject { ["dependencies"] = new JsonObject() } };

    static JsonObject ProjectNode(params (string Name, string Constraint)[] dependencies)
    {
        var declared = new JsonObject();
        foreach ((string name, string constraint) in dependencies)
            declared.Add(name, constraint);
        return new JsonObject { ["type"] = "project", ["dependencies"] = declared };
    }

    /// <summary>The pathological <c>App -&gt; ProjectB -&gt; PackageC</c> shape.</summary>
    static byte[] AppProjectPackageDocument() =>
        SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["ProjectB/1.0.0"] = ProjectNode(("PackageC", "1.0.0")),
                    ["PackageC/1.0.0"] = new JsonObject { ["type"] = "package" },
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("ProjectB >= 1.0.0") },
            frameworks: EmptyFrameworks);

    /// <summary>
    /// <c>App -&gt; ProjectB -&gt; ProjectC</c>, where <c>ProjectC</c> declares a dependency that
    /// resolves to no selected-target node. Its failure belongs to distance 2.
    /// </summary>
    static byte[] MalformedGrandchildDocument() =>
        SyntheticDocument(
            targets: new JsonObject
            {
                ["net11.0"] = new JsonObject
                {
                    ["ProjectB/1.0.0"] = ProjectNode(("ProjectC", "1.0.0")),
                    ["ProjectC/1.0.0"] = ProjectNode(("Absent.Package", "1.0.0")),
                },
            },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("ProjectB >= 1.0.0") },
            frameworks: EmptyFrameworks);

    /// <summary>
    /// A project mesh whose relationship occurrences exceed
    /// <see cref="RestoredProjectDependencyTraversalQuery.MaxProjectRelationships"/> while its node
    /// and edge counts stay inside every facts-owner bound.
    /// </summary>
    static byte[] DenseProjectMeshDocument(int projects)
    {
        var targets = new JsonObject();
        for (int index = 0; index < projects; index++)
        {
            var dependencies = new JsonObject();
            for (int other = 0; other < projects; other++)
            {
                if (other != index)
                    dependencies.Add($"Mesh.Project{other}", "1.0.0");
            }

            targets.Add(
                $"Mesh.Project{index}/1.0.0",
                new JsonObject { ["type"] = "project", ["dependencies"] = dependencies });
        }

        return SyntheticDocument(
            targets: new JsonObject { ["net11.0"] = targets },
            rootGroups: new JsonObject { ["net11.0"] = new JsonArray("Mesh.Project0 >= 1.0.0") },
            frameworks: EmptyFrameworks);
    }

    static byte[] SyntheticDocument(JsonObject targets, JsonObject rootGroups, JsonObject frameworks)
    {
        var document = new JsonObject
        {
            ["version"] = 4,
            ["targets"] = targets,
            ["projectFileDependencyGroups"] = rootGroups,
            ["project"] = new JsonObject { ["frameworks"] = frameworks },
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    static byte[] WithReversedPropertyOrder(byte[] assetsBytes)
    {
        JsonNode root = JsonNode.Parse(assetsBytes)!;
        return Encoding.UTF8.GetBytes(Reverse(root).ToJsonString());
    }

    static JsonNode Reverse(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                var reversedObject = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> property in obj.Reverse())
                    reversedObject.Add(property.Key, property.Value is null ? null : Reverse(property.Value));
                return reversedObject;
            case JsonArray array:
                var reversedArray = new JsonArray();
                foreach (JsonNode? element in array)
                    reversedArray.Add(element is null ? null : Reverse(element));
                return reversedArray;
            default:
                return node.DeepClone();
        }
    }

    static byte[] ReadCopiedAssetsBytes() =>
        File.ReadAllBytes(FixtureCatalog.RestoredProjectDependencyFacts.AssetPath("project.assets.json"));
}
