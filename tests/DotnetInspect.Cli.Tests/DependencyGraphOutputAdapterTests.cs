using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using InertText;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class DependencyGraphOutputAdapterTests
{
    [Fact]
    public async Task Json_PackageProjectionRetainsIssuedDiagnostics()
    {
        var document = new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "contoso.package",
                        "1.0.0"),
                    new InertString(
                        TextPolicy.Field,
                        "Contoso.Package 1.0.0")),
            ],
            [],
            [
                new DependencyGraphPackageProjection(
                    0,
                    0,
                    PackageDependencyTraversalProjectionKind
                        .CandidateAcquired,
                    PackageDependencyTraversalProjectionExpansion.Expanded,
                    Evidence: null,
                    Candidate: null,
                    RootOccurrence: null,
                    [
                        new PackageAuthorityFailure(
                            new InertString(
                                TextPolicy.Field,
                                "local authority"),
                            PackageAuthorityFailureKind.Transport,
                            "The source could not provide the manifest."),
                    ]),
            ],
            []);

        string json = await RenderAsync(
            document,
            [],
            OutputFormat.Json);

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement diagnostic = parsed.RootElement
            .GetProperty("package_projections")[0]
            .GetProperty("diagnostics")[0];
        Assert.Equal("Transport", diagnostic.GetProperty("kind").GetString());
        Assert.Equal(
            "local authority",
            diagnostic.GetProperty("authority").GetString());
    }

    [Fact]
    public async Task SharedDag_PreservesEdgesAndRootAcrossGraphSinks()
    {
        DependencyGraphDocument document = SharedDag();
        List<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document);

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string mermaid = await RenderAsync(
            document,
            rows,
            OutputFormat.Mermaid);
        string table = await RenderAsync(
            document,
            rows,
            OutputFormat.Table);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);
        string jsonLines = await RenderAsync(
            document,
            rows,
            OutputFormat.Jsonl);
        string compactJson = await RenderAsync(
            document,
            rows,
            OutputFormat.Json,
            compactJson: true);

        Assert.Equal(5, rows.Count);
        Assert.Contains("Root", tree, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(tree, "Shared"));
        Assert.Contains("(revisit) Shared", tree, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(tree, "Leaf"));

        Assert.StartsWith("graph TD", mermaid, StringComparison.Ordinal);
        Assert.Contains("Root", mermaid, StringComparison.Ordinal);
        Assert.True(
            MermaidEdgeCount(mermaid) == 5,
            mermaid);

        Assert.Equal(5, DataLineCount(table));
        Assert.Equal(
            5,
            jsonLines.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries).Length);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            5,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
        Assert.True(NonEmptyLineCount(json) > 1);
        Assert.Equal(1, NonEmptyLineCount(compactJson));
        Assert.Contains(
            parsed.RootElement.GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("root_occurrences")
                .GetArrayLength() == 1);
    }

    [Fact]
    public async Task RowWindow_SelectsTheSameLogicalEdgesInEverySink()
    {
        DependencyGraphDocument document = SharedDag();
        IReadOnlyList<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document)
                .Skip(2)
                .Take(2)
                .ToArray();

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string mermaid = await RenderAsync(
            document,
            rows,
            OutputFormat.Mermaid);
        string table = await RenderAsync(
            document,
            rows,
            OutputFormat.Table);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);
        string jsonLines = await RenderAsync(
            document,
            rows,
            OutputFormat.Jsonl);

        Assert.Contains("Root", tree, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(tree, "Shared"));
        Assert.Contains("(fragment) A", tree, StringComparison.Ordinal);
        Assert.Contains("(fragment) B", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaf", tree, StringComparison.Ordinal);
        Assert.True(
            MermaidEdgeCount(mermaid) == 2,
            mermaid);
        Assert.Contains("Root", mermaid, StringComparison.Ordinal);
        Assert.Equal(2, DataLineCount(table));
        Assert.Equal(
            2,
            jsonLines.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries).Length);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            2,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public async Task DepthBoundary_IsTypedPresentationContextWithinTheRowWindow()
    {
        DependencyGraphDocument original = SharedDag();
        DependencyGraphDocument document = original with
        {
            DepthBoundaries =
            [
                new DependencyGraphDepthBoundary(
                    NodeId: 3,
                    PackageProjectionId: null,
                    MaximumDepth: 2,
                    [1],
                    DependencyGraphDepthBoundaryProducerKind.Restored),
            ],
        };
        List<DependencyGraphEdgeRow> allRows =
            DependencyGraphOutputAdapter.EdgeRows(document);
        DependencyGraphEdgeRow selected = allRows[2];

        string tree = await RenderAsync(
            document,
            [selected],
            OutputFormat.PlainText);
        string table = await RenderAsync(
            document,
            [selected],
            OutputFormat.Table);
        string json = await RenderAsync(
            document,
            [selected],
            OutputFormat.Json);
        string excludedJson = await RenderAsync(
            document,
            [allRows[0]],
            OutputFormat.Json);

        Assert.Equal(original.Edges.Length, allRows.Count);
        Assert.Contains(
            "(bounded at depth 2) Shared",
            tree,
            StringComparison.Ordinal);
        Assert.Equal(1, DataLineCount(table));
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            1,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
        JsonElement boundary = Assert.Single(
            parsed.RootElement.GetProperty("depth_boundaries")
                .EnumerateArray());
        Assert.Equal(3, boundary.GetProperty("node_id").GetInt32());
        Assert.Equal(
            "Restored",
            boundary.GetProperty("producer").GetString());
        Assert.Equal(
            [1],
            boundary.GetProperty("root_occurrences")
                .EnumerateArray()
                .Select(static occurrence => occurrence.GetInt32()));

        using JsonDocument excluded = JsonDocument.Parse(excludedJson);
        Assert.Empty(
            excluded.RootElement.GetProperty("depth_boundaries")
                .EnumerateArray());
    }

    [Fact]
    public async Task RowWindow_MarksDetachedCycleAsFragment()
    {
        DependencyGraphDocument document = DetachedCycle();
        IReadOnlyList<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document)
                .Skip(1)
                .ToArray();

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);

        Assert.Contains("(fragment) A", tree, StringComparison.Ordinal);
        Assert.Contains("(revisit)", tree, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RowWindow_MarksDirectedBranchThatJoinsReachableTarget()
    {
        DependencyGraphDocument document = JoinedFragment();
        IReadOnlyList<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document)
                .Skip(1)
                .ToArray();

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);

        Assert.Contains("(fragment) Branch", tree, StringComparison.Ordinal);
        Assert.Contains("(revisit) Shared", tree, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceAuthoredLabelsRemainInertAcrossSinks()
    {
        const string Hazard = "Shared\u202Ename";
        DependencyGraphDocument document = SharedDag(Hazard);
        List<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document);

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string table = await RenderAsync(
            document,
            rows,
            OutputFormat.Table);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);

        Assert.DoesNotContain(Hazard, tree, StringComparison.Ordinal);
        Assert.DoesNotContain(Hazard, table, StringComparison.Ordinal);
        Assert.DoesNotContain(Hazard, json, StringComparison.Ordinal);
        Assert.Contains("\\u202E", tree, StringComparison.Ordinal);
        Assert.Contains("\\u202E", table, StringComparison.Ordinal);
        Assert.Contains("\\u202E", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootOnlyGraph_RemainsVisibleWithoutInventingAnEdge()
    {
        DependencyGraphDocument document = new(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Type(
                        "System.IDisposable"),
                    new InertString(
                        TextPolicy.Field,
                        "System.IDisposable")),
            ],
            [],
            [],
            []);
        List<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document);

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string mermaid = await RenderAsync(
            document,
            rows,
            OutputFormat.Mermaid);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);

        Assert.Empty(rows);
        Assert.Contains("System.IDisposable", tree, StringComparison.Ordinal);
        Assert.Contains("System.IDisposable", mermaid, StringComparison.Ordinal);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            1,
            parsed.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(
            0,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public void Combine_RepeatedRootCoalescesLogicalEdgesAndUnionsReachability()
    {
        DependencyGraphDocument first =
            DependencyGraphProjection.WithRootOccurrence(SharedDag(), 1);
        DependencyGraphDocument second =
            DependencyGraphProjection.WithRootOccurrence(SharedDag(), 2);

        DependencyGraphDocument combined =
            DependencyGraphProjection.Combine([first, second]);

        Assert.Equal(2, combined.Roots.Length);
        Assert.Equal(5, combined.Edges.Length);
        Assert.All(
            combined.Edges,
            edge => Assert.Equal([1, 2], edge.RootOccurrences));
    }

    [Fact]
    public void Combine_UsesMetadataAssemblyEquivalenceButExactModuleIdentity()
    {
        var upperAssembly = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "Contoso.Shared",
                new Version(1, 2, 3, 4),
                Culture: null,
                PublicKeyToken: "0011223344556677"));
        var lowerAssembly = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "contoso.shared",
                new Version(1, 2, 3, 4),
                Culture: "neutral",
                PublicKeyToken: "0011223344556677"));
        DependencyGraphDocument combinedAssemblies =
            DependencyGraphProjection.Combine(
            [
                LibraryRoot(upperAssembly, "Contoso.Shared", 1),
                LibraryRoot(lowerAssembly, "contoso.shared", 2),
            ]);

        DependencyGraphNode assemblyNode =
            Assert.Single(combinedAssemblies.Nodes);
        Assert.Equal("Contoso.Shared", assemblyNode.Label.ToString());
        Assert.Equal(
            [1, 2],
            combinedAssemblies.Roots.Select(
                static root => root.OccurrenceIndex));
        Assert.All(
            combinedAssemblies.Roots,
            root => Assert.Equal(assemblyNode.Id, root.NodeId));

        Guid moduleVersionId = Guid.NewGuid();
        DependencyGraphDocument combinedModules =
            DependencyGraphProjection.Combine(
            [
                LibraryRoot(
                    new ManagedMetadataIdentity.Module(
                        "Contoso.Shared.netmodule",
                        moduleVersionId),
                    "Contoso.Shared.netmodule",
                    1),
                LibraryRoot(
                    new ManagedMetadataIdentity.Module(
                        "contoso.shared.netmodule",
                        moduleVersionId),
                    "contoso.shared.netmodule",
                    2),
            ]);

        Assert.Equal(2, combinedModules.Nodes.Length);
        Assert.NotEqual(
            combinedModules.Roots[0].NodeId,
            combinedModules.Roots[1].NodeId);
    }

    [Fact]
    public async Task MultiRootTree_UsesEachRootsAdmittedEdges()
    {
        DependencyGraphDocument document = new(
            [
                new DependencyGraphRootOccurrence(1, 0),
                new DependencyGraphRootOccurrence(2, 1),
            ],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Package("first", "1.0.0"),
                    new InertString(TextPolicy.Field, "First")),
                new DependencyGraphNode(
                    1,
                    new DependencyGraphNodeIdentity.Package("second", "1.0.0"),
                    new InertString(TextPolicy.Field, "Second")),
                new DependencyGraphNode(
                    2,
                    new DependencyGraphNodeIdentity.Package("leaf", "1.0.0"),
                    new InertString(TextPolicy.Field, "Leaf")),
            ],
            [
                new DependencyGraphEdge(
                    0,
                    0,
                    1,
                    "package-dependency",
                    [1],
                    1,
                    DependencyGraphResolutionState.Resolved,
                    null),
                new DependencyGraphEdge(
                    1,
                    1,
                    2,
                    "package-dependency",
                    [2],
                    1,
                    DependencyGraphResolutionState.Resolved,
                    null),
            ],
            [],
            []);

        (_, string tree, string error) =
            await ConsoleCapture.RunAsync(() =>
            {
                DependencyGraphOutputAdapter.Write(
                    document,
                    DependencyGraphOutputAdapter.EdgeRows(document),
                    OutputFormat.PlainText,
                    tree: true,
                    embeddedMermaid: false,
                    noHeader: false,
                    compactJson: false);
                return Task.FromResult(0);
            });

        Assert.Empty(error);
        Assert.DoesNotContain(
            "First\n   └─ Second\n      └─ Leaf",
            tree,
            StringComparison.Ordinal);
        Assert.Contains(
            "└─ (revisit) Second\n   └─ Leaf",
            tree,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiRootTree_RevisitsSharedConnectingEdges()
    {
        DependencyGraphDocument document = SharedConnectorRoots();
        List<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document);

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string table = await RenderAsync(
            document,
            rows,
            OutputFormat.Table);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);

        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain("(fragment)", tree, StringComparison.Ordinal);
        Assert.Equal(3, Occurrences(tree, "package-dependency"));
        int laterRoot = tree.LastIndexOf(
            "(revisit) Shared",
            StringComparison.Ordinal);
        Assert.True(laterRoot >= 0, tree);
        int connector = tree.IndexOf(
            "(revisit) Bridge",
            laterRoot,
            StringComparison.Ordinal);
        Assert.True(connector > laterRoot, tree);
        int leaf = tree.IndexOf(
            "Leaf",
            connector,
            StringComparison.Ordinal);
        Assert.True(leaf > connector, tree);
        Assert.Equal(3, DataLineCount(table));
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            rows.Count,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public void EmptyPackageRoot_UsesCanonicalSemanticVersion()
    {
        DependencyGraphDocument document =
            DependencyGraphProjection.Package(
                new PackageDependencyGraphResult.Empty(
                    "Example",
                    "1.0",
                    "Example",
                    "1.0",
                    "No additional dependencies.",
                    PackageDependencyGraphResult.EmptyKind.SelectedGroup));

        DependencyGraphNode root = Assert.Single(document.Nodes);
        var identity = Assert.IsType<
            DependencyGraphNodeIdentity.Package>(
                root.Identity);
        Assert.Equal("1.0.0", identity.Version);
    }

    [Fact]
    public void TypeProjection_PreservesCaseDistinctSemanticNodes()
    {
        var result = new TypeDependencyResult(
            "IdentityFixture.CaseRoot",
            [])
        {
            Relationships =
            [
                new(
                    "IdentityFixture.CaseRoot",
                    "IdentityFixture.IFoo",
                    TypeDependencyRelationshipKind.Interface,
                    0),
                new(
                    "IdentityFixture.CaseRoot",
                    "IdentityFixture.Ifoo",
                    TypeDependencyRelationshipKind.Interface,
                    1),
            ],
        };

        DependencyGraphDocument document =
            DependencyGraphProjection.Type(result);

        Assert.Equal(3, document.Nodes.Length);
        Assert.Equal(2, document.Edges.Length);
        Assert.NotEqual(
            document.Edges[0].TargetNodeId,
            document.Edges[1].TargetNodeId);
        Assert.Contains(
            document.Nodes,
            static node =>
                node.Identity is DependencyGraphNodeIdentity.Type
                {
                    Name: "IdentityFixture.IFoo",
                });
        Assert.Contains(
            document.Nodes,
            static node =>
                node.Identity is DependencyGraphNodeIdentity.Type
                {
                    Name: "IdentityFixture.Ifoo",
                });
    }

    [Fact]
    public async Task MermaidRelationshipLabelsDoNotDependOnTheRowWindow()
    {
        DependencyGraphDocument original = SharedDag();
        DependencyGraphDocument document = original with
        {
            Edges =
            [
                original.Edges[0] with { Relationship = "base-type" },
                .. original.Edges.Skip(1).Select(edge =>
                    edge with { Relationship = "interface" }),
            ],
        };
        DependencyGraphEdgeRow selected =
            DependencyGraphOutputAdapter.EdgeRows(document)[1];

        string mermaid = await RenderAsync(
            document,
            [selected],
            OutputFormat.Mermaid);

        Assert.Contains("interface", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageProjection_PreservesUnavailableResolution()
    {
        var root =
            new PackageDependencyIdentity("Root.Package", "1.0.0");
        var target =
            new PackageDependencyIdentity("Missing.Package", "2.0.0");
        var graph = new PackageDependencyGraphResult.Graph(
            root.PackageId,
            root.Version,
            root.PackageId,
            root.Version,
            new PackageDependencyGraph(
                [
                    new PackageDependencyGraphNode(root, Author: null),
                    new PackageDependencyGraphNode(target, Author: null),
                ],
                [
                    new PackageDependencyRelationship(
                        root,
                        target,
                        "[2.0.0, )",
                        PackageDependencyResolutionState.Unavailable,
                        Ordinal: 0),
                ],
                []));

        DependencyGraphEdgeRow row = Assert.Single(
            DependencyGraphOutputAdapter.EdgeRows(
                DependencyGraphProjection.Package(graph)));

        Assert.Equal("unavailable", row.Resolution);
    }

    private static async Task<string> RenderAsync(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        OutputFormat format,
        bool compactJson = false)
    {
        (string output, string error) =
            await ConsoleCapture.RunAsync(() =>
                DependencyGraphOutputAdapter.Write(
                    document,
                    rows,
                    format,
                    tree: false,
                    embeddedMermaid: false,
                    noHeader: false,
                    compactJson));
        Assert.Empty(error);
        return output;
    }

    private static DependencyGraphDocument SharedDag(
        string sharedLabel = "Shared")
    {
        static InertString Label(string value) =>
            new(TextPolicy.Field, value);

        ImmutableArray<DependencyGraphNode> nodes =
        [
            new(
                0,
                new DependencyGraphNodeIdentity.Package("root", "1.0.0"),
                Label("Root")),
            new(
                1,
                new DependencyGraphNodeIdentity.Package("a", "1.0.0"),
                Label("A")),
            new(
                2,
                new DependencyGraphNodeIdentity.Package("b", "1.0.0"),
                Label("B")),
            new(
                3,
                new DependencyGraphNodeIdentity.Package(
                    "shared",
                    "1.0.0"),
                Label(sharedLabel)),
            new(
                4,
                new DependencyGraphNodeIdentity.Package("leaf", "1.0.0"),
                Label("Leaf")),
        ];
        ImmutableArray<DependencyGraphEdge> edges =
        [
            Edge(0, 0, 1, 1),
            Edge(1, 0, 2, 1),
            Edge(2, 1, 3, 2),
            Edge(3, 2, 3, 2),
            Edge(4, 3, 4, 3),
        ];
        return new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            nodes,
            edges,
            [],
            []);
    }

    private static DependencyGraphDocument DetachedCycle()
    {
        static InertString Label(string value) =>
            new(TextPolicy.Field, value);

        return new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "owner",
                        "1.0.0"),
                    Label("Owner")),
                new(
                    1,
                    new DependencyGraphNodeIdentity.Package("a", "1.0.0"),
                    Label("A")),
                new(
                    2,
                    new DependencyGraphNodeIdentity.Package("b", "1.0.0"),
                    Label("B")),
            ],
            [
                Edge(0, 0, 1, 1),
                Edge(1, 1, 2, 2),
                Edge(2, 2, 1, 3),
            ],
            [],
            []);
    }

    private static DependencyGraphDocument JoinedFragment()
    {
        static InertString Label(string value) =>
            new(TextPolicy.Field, value);

        return new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "owner",
                        "1.0.0"),
                    Label("Owner")),
                new(
                    1,
                    new DependencyGraphNodeIdentity.Package(
                        "branch",
                        "1.0.0"),
                    Label("Branch")),
                new(
                    2,
                    new DependencyGraphNodeIdentity.Package(
                        "shared",
                        "1.0.0"),
                    Label("Shared")),
            ],
            [
                Edge(0, 0, 1, 1),
                Edge(1, 0, 2, 1),
                Edge(2, 1, 2, 2),
            ],
            [],
            []);
    }

    private static DependencyGraphDocument SharedConnectorRoots()
    {
        static InertString Label(string value) =>
            new(TextPolicy.Field, value);

        return new DependencyGraphDocument(
            [
                new DependencyGraphRootOccurrence(1, 0),
                new DependencyGraphRootOccurrence(2, 1),
            ],
            [
                new(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "root",
                        "1.0.0"),
                    Label("Root")),
                new(
                    1,
                    new DependencyGraphNodeIdentity.Package(
                        "shared",
                        "1.0.0"),
                    Label("Shared")),
                new(
                    2,
                    new DependencyGraphNodeIdentity.Package(
                        "bridge",
                        "1.0.0"),
                    Label("Bridge")),
                new(
                    3,
                    new DependencyGraphNodeIdentity.Package(
                        "leaf",
                        "1.0.0"),
                    Label("Leaf")),
            ],
            [
                new DependencyGraphEdge(
                    0,
                    0,
                    1,
                    "package-dependency",
                    [1],
                    1,
                    DependencyGraphResolutionState.Resolved,
                    null),
                new DependencyGraphEdge(
                    1,
                    1,
                    2,
                    "package-dependency",
                    [1, 2],
                    2,
                    DependencyGraphResolutionState.Resolved,
                    null),
                new DependencyGraphEdge(
                    2,
                    2,
                    3,
                    "package-dependency",
                    [2],
                    2,
                    DependencyGraphResolutionState.Resolved,
                    null),
            ],
            [],
            []);
    }

    private static DependencyGraphEdge Edge(
        int id,
        int source,
        int target,
        int depth) =>
        new(
            id,
            source,
            target,
            "package-dependency",
            [1],
            depth,
            DependencyGraphResolutionState.Resolved,
            EvidenceIdentity: null);

    private static DependencyGraphDocument LibraryRoot(
        ManagedMetadataIdentity identity,
        string label,
        int occurrence) =>
        new(
            [new DependencyGraphRootOccurrence(occurrence, 0)],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Library(identity),
                    new InertString(TextPolicy.Field, label)),
            ],
            [],
            [],
            []);

    private static int Occurrences(string value, string expected) =>
        value.Split(expected, StringSplitOptions.None).Length - 1;

    private static int DataLineCount(string table) =>
        table.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Count();

    private static int NonEmptyLineCount(string value) =>
        value.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries).Length;

    private static int MermaidEdgeCount(string mermaid) =>
        mermaid.Split('\n').Count(static line =>
            line.Contains("-->", StringComparison.Ordinal)
            || line.Contains("-.->", StringComparison.Ordinal));
}
