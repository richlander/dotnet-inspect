using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace ILInspector.Analysis.Tests;

public sealed class ExternalFocusedCallGraphProjectionTests
{
    static TypeRef Type(string name) =>
        TypeRef.Definition("Sample", "Sample", name);

    static MemberRef Member(string name) =>
        new(
            Type(name),
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

    static CallTreeNode Node(
        string name,
        params CallTreeNode[] children) =>
        new(
            Member(name),
            null,
            CallTreeStatus.Expanded,
            [.. children],
            null);

    static CallTreeNode Leaf(
        string name,
        CallTreeStatus status = CallTreeStatus.Leaf) =>
        new(Member(name), null, status, [], null);

    static int Id(CallGraphProjection graph, string name) =>
        Assert.Single(
            graph.Nodes,
            node => node.Member.Name == name).Id;

    static string[] Rows(
        ExternalFocusedCallGraphProjection projection) =>
        [
            .. projection.Rows.Select(row =>
                $"{projection.Source.Nodes[row.Edge.From].Member.Name}"
                + "->"
                + $"{projection.Source.Nodes[row.Edge.To].Member.Name}"),
        ];

    [Fact]
    public void BoundaryOnly_RetainsIncomingAndOutgoingCrossings()
    {
        CallTreeNode focus = Node(
            "Focus",
            Leaf("Local"),
            Leaf("Outgoing"));
        CallTreeNode callers = Node(
            "Focus",
            Leaf("Incoming"));
        CallGraphProjection graph =
            CallGraphProjection.Create(callers, focus);

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [Id(graph, "Focus"), Id(graph, "Local")],
                    [Id(graph, "Incoming"), Id(graph, "Outgoing")]));

        Assert.Equal(
            ["Incoming->Focus", "Focus->Outgoing"],
            Rows(projection));
        Assert.Equal(projection.Rows, projection.BoundaryRows);
        Assert.Empty(projection.ConnectorRows);
        Assert.True(projection.HasCompleteBoundaryClassification);
    }

    [Fact]
    public void SeededConnectors_RetainShortestOutgoingPath()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node(
                    "Focus",
                    Node(
                        "First",
                        Node(
                            "BoundarySource",
                            Leaf("External"))),
                    Node(
                        "Noise",
                        Leaf("NoiseLeaf"))));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [
                        Id(graph, "Focus"),
                        Id(graph, "First"),
                        Id(graph, "BoundarySource"),
                        Id(graph, "Noise"),
                        Id(graph, "NoiseLeaf"),
                    ],
                    [Id(graph, "External")],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Focus")]));

        Assert.Equal(
            [
                "Focus->First",
                "First->BoundarySource",
                "BoundarySource->External",
            ],
            Rows(projection));
        Assert.Equal(
            ["BoundarySource->External"],
            projection.BoundaryRows.Select(row =>
                $"{graph.Nodes[row.Edge.From].Member.Name}"
                + "->"
                + $"{graph.Nodes[row.Edge.To].Member.Name}"));
        Assert.Equal(4, projection.Nodes.Length);
    }

    [Fact]
    public void SeededConnectors_RetainShortestIncomingPath()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallers(
                Node(
                    "Focus",
                    Node(
                        "First",
                        Node(
                            "BoundaryTarget",
                            Leaf("External")))));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [
                        Id(graph, "Focus"),
                        Id(graph, "First"),
                        Id(graph, "BoundaryTarget"),
                    ],
                    [Id(graph, "External")],
                    ExternalFocusedCallGraphDirection.Incoming,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Focus")]));

        Assert.Equal(
            [
                "First->Focus",
                "BoundaryTarget->First",
                "External->BoundaryTarget",
            ],
            Rows(projection));
    }

    [Fact]
    public void SeededConnectors_BoundaryAtSeedNeedsNoLocalEdge()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node("Focus", Leaf("External")));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [Id(graph, "Focus")],
                    [Id(graph, "External")],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Focus")]));

        Assert.Equal(["Focus->External"], Rows(projection));
        Assert.Empty(projection.ConnectorRows);
    }

    [Fact]
    public void SeededConnectors_ChooseStableShortestRowSequence()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node(
                    "Focus",
                    Node(
                        "First",
                        Node(
                            "BoundarySource",
                            Leaf("External"))),
                    Node(
                        "Second",
                        Leaf(
                            "BoundarySource",
                            CallTreeStatus.AlreadyShown))));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [
                        Id(graph, "Focus"),
                        Id(graph, "First"),
                        Id(graph, "Second"),
                        Id(graph, "BoundarySource"),
                    ],
                    [Id(graph, "External")],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Focus")]));

        Assert.Contains("Focus->First", Rows(projection));
        Assert.DoesNotContain("Focus->Second", Rows(projection));
    }

    [Fact]
    public void SeededConnectors_IgnoreCycleEdge()
    {
        CallTreeNode focus = Node(
            "Focus",
            Node(
                "Connected",
                Leaf("Focus", CallTreeStatus.AlreadyShown),
                Leaf("External")));
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(focus);

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [
                        Id(graph, "Focus"),
                        Id(graph, "Connected"),
                    ],
                    [Id(graph, "External")],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Focus")]));

        Assert.Contains("Connected->External", Rows(projection));
        Assert.DoesNotContain("Connected->Focus", Rows(projection));
    }

    [Fact]
    public void SeededConnectors_OmitDisconnectedBoundary()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node(
                    "Focus",
                    Node("Connected", Leaf("External")),
                    Node("Disconnected", Leaf("OtherExternal"))));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [
                        Id(graph, "Focus"),
                        Id(graph, "Connected"),
                        Id(graph, "Disconnected"),
                    ],
                    [
                        Id(graph, "External"),
                        Id(graph, "OtherExternal"),
                    ],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Connected")]));

        Assert.Contains("Connected->External", Rows(projection));
        Assert.DoesNotContain(
            "Disconnected->OtherExternal",
            Rows(projection));
    }

    [Fact]
    public void UnknownBoundary_RemainsVisibleAndIncomplete()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node("Focus", Leaf("Unknown")));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [Id(graph, "Focus")],
                    []));

        Assert.Empty(projection.Rows);
        Assert.Equal(
            "Unknown",
            graph.Nodes[
                Assert.Single(
                    projection.UnclassifiedBoundaryRows).Edge.To]
                .Member.Name);
        Assert.False(projection.HasCompleteBoundaryClassification);
    }

    [Fact]
    public void SeededUnknownBoundary_RetainsItsConnectorSeparately()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node(
                    "Focus",
                    Node("Local", Leaf("Unknown"))));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [Id(graph, "Local"), Id(graph, "Focus")],
                    [],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Focus")]));

        Assert.Empty(projection.Rows);
        Assert.Equal(
            ["Local->Unknown"],
            projection.UnclassifiedBoundaryRows.Select(row =>
                $"{graph.Nodes[row.Edge.From].Member.Name}"
                + "->"
                + $"{graph.Nodes[row.Edge.To].Member.Name}"));
        Assert.Equal(
            ["Focus->Local"],
            projection.UnclassifiedConnectorRows.Select(row =>
                $"{graph.Nodes[row.Edge.From].Member.Name}"
                + "->"
                + $"{graph.Nodes[row.Edge.To].Member.Name}"));
        Assert.Equal(2, projection.EvidenceRows.Length);
        Assert.Equal(3, projection.Nodes.Length);
        Assert.False(projection.HasCompleteBoundaryClassification);
    }

    [Fact]
    public void SeededUnknownBoundary_RemainsVisibleWhenDisconnected()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node(
                    "Focus",
                    Node("Connected", Leaf("External")),
                    Node("Disconnected", Leaf("Unknown"))));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [
                        Id(graph, "Focus"),
                        Id(graph, "Connected"),
                        Id(graph, "Disconnected"),
                    ],
                    [Id(graph, "External")],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Connected")]));

        Assert.Equal(
            ["Connected->External"],
            Rows(projection));
        CallGraphRow unknown =
            Assert.Single(projection.UnclassifiedBoundaryRows);
        Assert.Equal(
            "Disconnected->Unknown",
            $"{graph.Nodes[unknown.Edge.From].Member.Name}"
            + "->"
            + $"{graph.Nodes[unknown.Edge.To].Member.Name}");
        Assert.Empty(projection.UnclassifiedConnectorRows);
        Assert.False(projection.HasCompleteBoundaryClassification);
    }

    [Fact]
    public void SeededEmptyResult_RetainsSeed()
    {
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                Node("Focus", Leaf("Local")));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [Id(graph, "Focus"), Id(graph, "Local")],
                    [],
                    ExternalFocusedCallGraphDirection.Outgoing,
                    ExternalFocusedCallGraphMode.SeededConnectors,
                    [Id(graph, "Focus")]));

        Assert.Empty(projection.Rows);
        Assert.Equal(
            "Focus",
            Assert.Single(projection.Nodes).Member.Name);
        Assert.True(projection.HasCompleteBoundaryClassification);
    }

    [Fact]
    public void RetainedBoundaryPreservesPhysicalCallSites()
    {
        MemberRef focus = Member("Focus");
        MemberRef external = Member("External");
        DirectCall first = Call(focus, external, 4);
        DirectCall second = Call(focus, external, 8);
        CallGraphProjection graph =
            CallGraphProjection.FromCallees(
                new(
                    focus,
                    null,
                    CallTreeStatus.Expanded,
                    [
                        new(
                            external,
                            null,
                            CallTreeStatus.External,
                            [],
                            null)
                        {
                            ParentEdgeCallSites = [first, second],
                        },
                    ],
                    null));

        ExternalFocusedCallGraphProjection projection =
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new(
                    [Id(graph, "Focus")],
                    [Id(graph, "External")]));

        Assert.Equal(2, projection.CallSites.Length);
        Assert.Equal(
            graph.CallSites.Select(static site => site.Id),
            projection.CallSites.Select(static site => site.Id));
    }

    [Fact]
    public void SourceBoundariesRemainVisibleBesidePositiveRows()
    {
        CallGraphProjection traversalLimited =
            CallGraphProjection.FromCallees(
                Node(
                    "Focus",
                    Leaf(
                        "External",
                        CallTreeStatus.External)));
        ExternalFocusedCallGraphProjection traversalProjection =
            ExternalFocusedCallGraphProjection.Create(
                traversalLimited,
                new(
                    [Id(traversalLimited, "Focus")],
                    [Id(traversalLimited, "External")]));

        Assert.Single(traversalProjection.Rows);
        Assert.True(traversalProjection.Source
            .HasUnexploredTraversalBoundary);
        Assert.True(
            traversalProjection.HasCompleteBoundaryClassification);

        CallTreeNode failed = new(
            Member("External"),
            null,
            CallTreeStatus.AnalysisIncomplete,
            [],
            null)
        {
            Diagnostic = new AnalysisDiagnostic(
                0x06000001,
                "External",
                "BadImageFormatException: invalid body"),
        };
        CallGraphProjection analysisLimited =
            CallGraphProjection.FromCallees(
                Node("Focus", failed));
        ExternalFocusedCallGraphProjection analysisProjection =
            ExternalFocusedCallGraphProjection.Create(
                analysisLimited,
                new(
                    [Id(analysisLimited, "Focus")],
                    [Id(analysisLimited, "External")]));

        Assert.Single(analysisProjection.Rows);
        Assert.True(analysisLimited.HasAnalysisFailureBoundary);
        Assert.True(
            analysisProjection.HasCompleteBoundaryClassification);
    }

    [Fact]
    public void RequestRejectsInvalidMembership()
    {
        Assert.Throws<ArgumentException>(() =>
            new ExternalFocusedCallGraphRequest([], []));
        Assert.Throws<ArgumentException>(() =>
            new ExternalFocusedCallGraphRequest([0], [0]));
        Assert.Throws<ArgumentException>(() =>
            new ExternalFocusedCallGraphRequest(
                [0],
                [],
                mode: ExternalFocusedCallGraphMode.SeededConnectors));
        Assert.Throws<ArgumentException>(() =>
            new ExternalFocusedCallGraphRequest(
                [0],
                [],
                seedNodeIds: [0]));

        CallGraphProjection graph =
            CallGraphProjection.FromCallees(Node("Focus"));
        Assert.Throws<ArgumentException>(() =>
            ExternalFocusedCallGraphProjection.Create(
                graph,
                new([1], [])));
    }

    static DirectCall Call(
        MemberRef caller,
        MemberRef callee,
        int offset) =>
        new(
            new MethodIdentity(
                caller.DeclaringType.Assembly,
                new Guid(
                    "22222222-2222-2222-2222-222222222222"),
                caller.DeclaringType,
                caller.Name,
                caller.ParameterTypes,
                caller.ReturnType,
                0x06000001,
                IsStatic: true),
            callee,
            offset,
            0x06000002,
            0x06000002,
            CallKind.Call)
        {
            ExactTarget = true,
        };
}
