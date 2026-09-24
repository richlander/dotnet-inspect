using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspector.Queries.Tests;

public sealed class InspectionGraphFocusProjectionTests
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

    static InspectionGraphSubject Subject(string name)
    {
        MemberRef member = Member(name);
        return InspectionGraphSubject.ForMember(
            GraphNodeIdentity.FromMember(member),
            member);
    }

    static InspectionGraphDocument Source(
        string[] names,
        (string From, string To)[] relationships,
        string? seed = null,
        IEnumerable<InspectionGraphLimit>? limits = null,
        IEnumerable<InspectionGraphFailure>? failures = null)
    {
        var ids = names.Select((name, id) => (name, id))
            .ToDictionary(static item => item.name, static item => item.id);
        InspectionGraphNode[] nodes =
        [
            .. names.Select((name, id) =>
                new InspectionGraphNode(
                    id,
                    Subject(name),
                    InspectionGraphNodeRole.Unclassified,
                    [])),
        ];
        InspectionGraphOccurrence[] occurrences =
        [
            .. relationships.Select((relationship, id) =>
                new InspectionGraphOccurrence(
                    id,
                    CallGraphInspectionGraphCatalog.Call,
                    nodes[ids[relationship.From]].Subject,
                    nodes[ids[relationship.To]].Subject,
                    new CallGraphLogicalEdgeEvidence(id),
                    [])),
        ];
        InspectionGraphEdge[] edges =
        [
            .. relationships.Select((relationship, id) =>
                new InspectionGraphEdge(
                    id,
                    ids[relationship.From],
                    ids[relationship.To],
                    CallGraphInspectionGraphCatalog.Call,
                    [id])),
        ];
        InspectionGraphModeRequest mode = seed is null
            ? InspectionGraphModeRequest.InducedSet(
                InspectionGraphInducedSetRule.DocumentSubjects)
            : InspectionGraphModeRequest.SingleSeed(
                nodes[ids[seed]].Subject);
        InspectionGraphSeed[] seeds = seed is null
            ? []
            :
            [
                new(
                    nodes[ids[seed]].Subject,
                    InspectionGraphTarget.Node(ids[seed]),
                    InspectionGraphSeedRole.Primary),
            ];
        return new InspectionGraphDocument(
            InspectionGraphDocumentScope.Portable,
            mode,
            nodes,
            [],
            edges,
            occurrences,
            [],
            seeds,
            limits ?? [],
            failures ?? []);
    }

    static InspectionGraphDocument Project(
        InspectionGraphDocument source,
        InspectionGraphTraversalDirection direction,
        string[] inside,
        string[] outside)
    {
        InspectionGraphScopeDecision[] decisions =
        [
            .. inside.Select(name =>
                new InspectionGraphScopeDecision(
                    Node(source, name).Subject,
                    InspectionGraphScopeMembership.Inside)),
            .. outside.Select(name =>
                new InspectionGraphScopeDecision(
                    Node(source, name).Subject,
                    InspectionGraphScopeMembership.Outside)),
        ];
        InspectionGraphFocusRequest request =
            InspectionGraphFocusRequest.ExitFrontier(
                source.ModeRequest,
                [CallGraphInspectionGraphCatalog.Call],
                direction,
                decisions);
        return InspectionGraphFocusProjection.Project(source, request);
    }

    static InspectionGraphNode Node(
        InspectionGraphDocument document,
        string name) =>
        Assert.Single(
            document.Nodes,
            node => Name(node) == name);

    static string Name(InspectionGraphNode node) =>
        Assert.IsType<InspectionGraphMemberIdentity.CallGraph>(
            Assert.IsType<InspectionGraphSubject.MemberSubject>(
                node.Subject)
                .Identity)
            .Member.Name;

    static string[] Edges(InspectionGraphDocument document) =>
        [
            .. document.Edges.Select(edge =>
                $"{Name(document.Nodes[edge.FromNodeId])}"
                + "->"
                + Name(document.Nodes[edge.ToNodeId])),
        ];

    static string Role(
        InspectionGraphDocument document,
        InspectionGraphTarget target) =>
        Assert.Single(
            Assert.IsType<InspectionGraphValue.TokenSet>(
                Assert.Single(
                    document.Characteristics,
                    characteristic =>
                        ReferenceEquals(
                            characteristic.Descriptor,
                            InspectionGraphFocusCatalog.Role)
                        && characteristic.Target == target)
                    .Value)
                .Values);

    [Fact]
    public void InducedFrontier_RetainsIncomingAndOutgoingExits()
    {
        InspectionGraphDocument source = Source(
            ["Focus", "Local", "Incoming", "Outgoing"],
            [
                ("Incoming", "Focus"),
                ("Focus", "Local"),
                ("Focus", "Outgoing"),
            ]);

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Both,
            ["Focus", "Local"],
            ["Incoming", "Outgoing"]);

        Assert.Equal(
            ["Incoming->Focus", "Focus->Outgoing"],
            Edges(result));
        Assert.All(
            result.Edges,
            edge => Assert.Equal(
                InspectionGraphFocusCatalog.ExitRole,
                Role(
                    result,
                    InspectionGraphTarget.Edge(edge.Id))));
    }

    [Fact]
    public void SeededFrontier_RetainsShortestOutgoingConnector()
    {
        InspectionGraphDocument source = Source(
            [
                "Focus",
                "First",
                "BoundarySource",
                "External",
                "Noise",
                "NoiseLeaf",
            ],
            [
                ("Focus", "First"),
                ("First", "BoundarySource"),
                ("BoundarySource", "External"),
                ("Focus", "Noise"),
                ("Noise", "NoiseLeaf"),
            ],
            "Focus");

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Outgoing,
            ["Focus", "First", "BoundarySource", "Noise", "NoiseLeaf"],
            ["External"]);

        Assert.Equal(
            [
                "Focus->First",
                "First->BoundarySource",
                "BoundarySource->External",
            ],
            Edges(result));
        Assert.Equal(
            [
                InspectionGraphFocusCatalog.ConnectorRole,
                InspectionGraphFocusCatalog.ConnectorRole,
                InspectionGraphFocusCatalog.ExitRole,
            ],
            result.Edges.Select(edge =>
                Role(
                    result,
                    InspectionGraphTarget.Edge(edge.Id))));
        Assert.Equal(
            InspectionGraphFocusCatalog.FocusRole,
            Role(result, InspectionGraphTarget.Node(0)));
        Assert.NotNull(result.FocusRequest);
        Assert.Same(source.ModeRequest, result.ModeRequest);
    }

    [Fact]
    public void SeededFrontier_RetainsShortestIncomingConnector()
    {
        InspectionGraphDocument source = Source(
            ["External", "BoundaryTarget", "First", "Focus"],
            [
                ("External", "BoundaryTarget"),
                ("BoundaryTarget", "First"),
                ("First", "Focus"),
            ],
            "Focus");

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Incoming,
            ["BoundaryTarget", "First", "Focus"],
            ["External"]);

        Assert.Equal(
            [
                "External->BoundaryTarget",
                "BoundaryTarget->First",
                "First->Focus",
            ],
            Edges(result));
        Assert.Equal(
            InspectionGraphFocusCatalog.ExitRole,
            Role(result, InspectionGraphTarget.Edge(0)));
    }

    [Fact]
    public void SeededFrontier_ChoosesLexicographicallyFirstShortestPath()
    {
        InspectionGraphDocument source = Source(
            [
                "Focus",
                "First",
                "Second",
                "BoundarySource",
                "External",
            ],
            [
                ("Focus", "First"),
                ("First", "BoundarySource"),
                ("Focus", "Second"),
                ("Second", "BoundarySource"),
                ("BoundarySource", "External"),
            ],
            "Focus");

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Outgoing,
            ["Focus", "First", "Second", "BoundarySource"],
            ["External"]);

        Assert.Contains("Focus->First", Edges(result));
        Assert.DoesNotContain("Focus->Second", Edges(result));
    }

    [Fact]
    public void SeededFrontier_TerminatesCyclesWithoutRetainingCycleNoise()
    {
        InspectionGraphDocument source = Source(
            ["Focus", "Connected", "External"],
            [
                ("Focus", "Connected"),
                ("Connected", "Focus"),
                ("Connected", "External"),
            ],
            "Focus");

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Outgoing,
            ["Focus", "Connected"],
            ["External"]);

        Assert.Equal(
            ["Focus->Connected", "Connected->External"],
            Edges(result));
    }

    [Fact]
    public void SeededFrontier_OmitsDisconnectedExitButRetainsUnknownBoundary()
    {
        InspectionGraphDocument source = Source(
            [
                "Focus",
                "Connected",
                "External",
                "Disconnected",
                "OtherExternal",
                "Unknown",
            ],
            [
                ("Focus", "Connected"),
                ("Connected", "External"),
                ("Disconnected", "OtherExternal"),
                ("Disconnected", "Unknown"),
            ],
            "Focus");

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Outgoing,
            ["Focus", "Connected", "Disconnected"],
            ["External", "OtherExternal"]);

        Assert.Equal(
            ["Focus->Connected", "Connected->External", "Disconnected->Unknown"],
            Edges(result));
        InspectionGraphEdge unknown = Assert.Single(
            result.Edges,
            edge => Name(result.Nodes[edge.ToNodeId]) == "Unknown");
        Assert.Equal(
            InspectionGraphFocusCatalog.UnclassifiedBoundaryRole,
            Role(
                result,
                InspectionGraphTarget.Edge(unknown.Id)));
        Assert.Contains(
            result.Limits,
            limit => ReferenceEquals(
                    limit.Descriptor,
                    InspectionGraphFocusCatalog
                        .ScopeClassificationIncomplete)
                && limit.Target
                    == InspectionGraphTarget.Edge(unknown.Id));
    }

    [Fact]
    public void UnknownBoundary_RetainsReachableConnectorSeparately()
    {
        InspectionGraphDocument source = Source(
            ["Focus", "Local", "Unknown"],
            [
                ("Focus", "Local"),
                ("Local", "Unknown"),
            ],
            "Focus");

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Outgoing,
            ["Focus", "Local"],
            []);

        Assert.Equal(
            ["Focus->Local", "Local->Unknown"],
            Edges(result));
        Assert.Equal(
            [
                InspectionGraphFocusCatalog.ConnectorRole,
                InspectionGraphFocusCatalog.UnclassifiedBoundaryRole,
            ],
            result.Edges.Select(edge =>
                Role(
                    result,
                    InspectionGraphTarget.Edge(edge.Id))));
    }

    [Fact]
    public void DirectExit_PreservesEveryPhysicalOccurrence()
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
        InspectionGraphDocument source =
            CallGraphInspectionGraphAdapter.Create(graph);

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Outgoing,
            ["Focus"],
            ["External"]);

        Assert.Equal(2, result.Occurrences.Length);
        Assert.All(
            result.Occurrences,
            occurrence => Assert.IsType<
                CallGraphCallSiteEvidence>(
                    occurrence.Evidence));
    }

    [Fact]
    public void Projection_PreservesGlobalAndRetainedTargetDiagnostics()
    {
        InspectionGraphDocument source = Source(
            ["NoiseSource", "NoiseTarget", "Focus", "External"],
            [
                ("NoiseSource", "NoiseTarget"),
                ("Focus", "External"),
            ],
            limits:
            [
                new(
                    CallGraphInspectionGraphCatalog.TraversalIncomplete),
                new(
                    CallGraphInspectionGraphCatalog
                        .PhysicalOccurrencesUnavailable,
                    InspectionGraphTarget.Edge(1)),
            ],
            failures:
            [
                new(
                    CallGraphInspectionGraphCatalog.AnalysisIncomplete),
                new(
                    CallGraphInspectionGraphCatalog.AnalysisIncomplete,
                    InspectionGraphTarget.Edge(1)),
            ]);

        InspectionGraphDocument result = Project(
            source,
            InspectionGraphTraversalDirection.Outgoing,
            ["Focus"],
            ["NoiseSource", "NoiseTarget", "External"]);

        InspectionGraphEdge edge = Assert.Single(result.Edges);
        Assert.Equal("Focus->External", Assert.Single(Edges(result)));
        Assert.Contains(
            result.Limits,
            limit => ReferenceEquals(
                    limit.Descriptor,
                    CallGraphInspectionGraphCatalog.TraversalIncomplete)
                && limit.Target is null);
        Assert.Contains(
            result.Limits,
            limit => ReferenceEquals(
                    limit.Descriptor,
                    CallGraphInspectionGraphCatalog
                        .PhysicalOccurrencesUnavailable)
                && limit.Target
                    == InspectionGraphTarget.Edge(edge.Id));
        Assert.Contains(
            result.Failures,
            failure => ReferenceEquals(
                    failure.Descriptor,
                    CallGraphInspectionGraphCatalog.AnalysisIncomplete)
                && failure.Target is null);
        Assert.Contains(
            result.Failures,
            failure => ReferenceEquals(
                    failure.Descriptor,
                    CallGraphInspectionGraphCatalog.AnalysisIncomplete)
                && failure.Target
                    == InspectionGraphTarget.Edge(edge.Id));
    }

    [Fact]
    public void RequestRejectsConflictingForeignOrUnknownOrigins()
    {
        InspectionGraphDocument source = Source(
            ["Focus", "External"],
            [("Focus", "External")],
            "Focus");
        InspectionGraphSubject focus = Node(source, "Focus").Subject;

        Assert.Throws<ArgumentException>(() =>
            InspectionGraphFocusRequest.ExitFrontier(
                source.ModeRequest,
                [CallGraphInspectionGraphCatalog.Call],
                InspectionGraphTraversalDirection.Outgoing,
                [
                    new(
                        focus,
                        InspectionGraphScopeMembership.Inside),
                    new(
                        focus,
                        InspectionGraphScopeMembership.Outside),
                ]));
        Assert.Throws<ArgumentException>(() =>
            InspectionGraphFocusRequest.ExitFrontier(
                source.ModeRequest,
                [CallGraphInspectionGraphCatalog.Call],
                InspectionGraphTraversalDirection.Outgoing,
                [
                    new(
                        focus,
                        InspectionGraphScopeMembership.Unknown),
                ]));

        InspectionGraphFocusRequest foreign =
            InspectionGraphFocusRequest.ExitFrontier(
                source.ModeRequest,
                [CallGraphInspectionGraphCatalog.Call],
                InspectionGraphTraversalDirection.Outgoing,
                [
                    new(
                        focus,
                        InspectionGraphScopeMembership.Inside),
                    new(
                        Subject("Foreign"),
                        InspectionGraphScopeMembership.Outside),
                ]);
        Assert.Throws<ArgumentException>(() =>
            InspectionGraphFocusProjection.Project(source, foreign));
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
