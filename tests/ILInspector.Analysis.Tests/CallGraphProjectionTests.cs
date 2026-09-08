using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Covers the format-neutral call-graph projection (issue #3291) as typed data rather
/// than as rendered text: edge inversion, generic-erased node identity, duplicate and
/// cycle collapsing, node-kind precedence, loop-edge merging, and deterministic node and
/// edge ordering. The projection is the only call-graph contract this layer offers: hosts
/// render their own format from it, so graph semantics are asserted directly here rather
/// than read back out of some rendering's text.
/// Trees are constructed directly so the projection is exercised in isolation from IL
/// decoding.
/// </summary>
public class CallGraphProjectionTests
{
    static TypeRef Type(string name) => TypeRef.Definition("Sample", "Sample", name);

    static MemberRef Member(string typeName, string method, params TypeRef[] parameters)
        => new(Type(typeName), method, [.. parameters], TypeRef.CoreLib("System", "Void"), MemberKind.Method);

    static MemberRef ReferencedMember(
        AssemblyReferenceIdentity assembly,
        string typeName,
        string method)
    {
        MetadataTypeDefinitionName name =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    [typeName]))
            .Name;
        TypeRef type = TypeRef.Definition(
            assembly.Name,
            "Sample",
            typeName,
            new ResolvableTypeReference(
                new TypeReferenceOrigin.AssemblyReference(assembly),
                name));
        return new(
            type,
            method,
            [],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);
    }

    static CallTreePerf Perf(bool inLoop, string? loopHint)
        => new(0, 0, 1, inLoop, loopHint);

    static CallTreeNode Node(
        MemberRef member,
        CallTreeStatus status,
        ImmutableArray<CallTreeNode> children,
        bool inLoop = false,
        string? loopHint = null)
        => new(member, null, status, children, Perf(inLoop, loopHint));

    static CallTreeNode Leaf(
        MemberRef member,
        CallTreeStatus status = CallTreeStatus.Leaf,
        bool inLoop = false,
        string? loopHint = null)
        => Node(member, status, [], inLoop, loopHint);

    static (int From, int To, string? Loop)[] EdgeTuples(CallGraphProjection projection)
        => [.. projection.Edges.Select(e =>
            (
                e.From,
                e.To,
                e.AnyCallInLoop
                    ? e.CallSiteIds.IsEmpty
                        && !string.IsNullOrEmpty(e.LegacyLoopHint)
                            ? e.LegacyLoopHint
                            : e.Origin == CallGraphEdgeOrigin.Callers
                                ? "loop call"
                                : "loop"
                    : null))];

    static GraphNodeEvidence Evidence(int token)
    {
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(CallGraphProjectionTests).Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "call-graph projection test"));
        GraphNodeStorageKey storage =
            GraphNodeStorageKey.Definition(
                source,
                new Guid("11111111-1111-1111-1111-111111111111"),
                token);
        return new GraphNodeEvidence(
            storage,
            GraphNodeIdentity.FromStorage(storage),
            correspondence: null);
    }

    static GraphNodeEvidence CallSiteEvidence(DirectCall call)
    {
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(CallGraphProjectionTests).Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "call-graph projection test"));
        GraphNodeStorageKey storage = GraphNodeStorageKey.CallSite(
            source,
            call.Caller.ModuleVersionId,
            call);
        return new GraphNodeEvidence(
            storage,
            GraphNodeIdentity.FromStorage(storage),
            correspondence: null);
    }

    static GraphNodeEvidence DefinitionEvidence(DirectCall call)
    {
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(CallGraphProjectionTests).Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "call-graph projection test"));
        GraphNodeStorageKey storage =
            GraphNodeStorageKey.Definition(
                source,
                call.Caller.ModuleVersionId,
                call.Caller.MetadataToken);
        return new GraphNodeEvidence(
            storage,
            GraphNodeIdentity.FromStorage(storage),
            correspondence: null);
    }

    static GraphNodeEvidence Reidentified(
        GraphNodeEvidence evidence) =>
        new(
            evidence.Storage,
            GraphNodeIdentity.CreateDocumentLocal(),
            correspondence: null);

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

    [Fact]
    public void FocusIsAlwaysNodeZero()
    {
        var projection = CallGraphProjection.FromCallees(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded, [Leaf(Member("Svc", "Do"))]));

        Assert.Equal(0, projection.Focus.Id);
        Assert.Equal(CallGraphNodeKind.Focus, projection.Focus.Kind);
        Assert.Same(projection.Nodes[0], projection.Focus);
    }

    [Fact]
    public void CompleteGraphEvidenceIsTheIdentityDomain()
    {
        MemberRef repeated = Member("Svc", "Do");
        CallTreeNode root = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [
                Leaf(repeated) with { GraphEvidence = Evidence(2) },
                Leaf(repeated) with { GraphEvidence = Evidence(3) },
            ]) with
        {
            GraphEvidence = Evidence(1),
        };

        CallGraphProjection projection =
            CallGraphProjection.FromCallees(root);

        Assert.Equal(3, projection.Nodes.Length);
        Assert.Equal(2, projection.Nodes.Count(node =>
            node.Member.Name == "Do"));
    }

    [Fact]
    public void ConflictingDefinitionAndResolutionAssembliesAreWithheld()
    {
        MemberRef repeated = Member("Svc", "Do");
        GraphNodeEvidence repeatedEvidence = Evidence(2);
        var first = new AssemblyReferenceIdentity(
            "First",
            new Version(1, 0),
            null,
            null);
        var second = new AssemblyReferenceIdentity(
            "Second",
            new Version(1, 0),
            null,
            null);
        CallTreeNode root = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [
                Leaf(repeated) with
                {
                    GraphEvidence = repeatedEvidence,
                    DefinitionAssemblyIdentity = first,
                    ResolutionAssemblyIdentity = first,
                },
                Leaf(repeated) with
                {
                    GraphEvidence = repeatedEvidence,
                    DefinitionAssemblyIdentity = second,
                    ResolutionAssemblyIdentity = second,
                },
            ]) with
        {
            GraphEvidence = Evidence(1),
        };

        CallGraphNode projected = Assert.Single(
            CallGraphProjection.FromCallees(root).Nodes,
            node => node.Member.Name == repeated.Name);

        Assert.Null(projected.DefinitionAssemblyIdentity);
        Assert.Null(projected.ResolutionAssemblyIdentity);
    }

    [Fact]
    public void FindNodePrefersExactDefinitionEvidence()
    {
        MemberRef repeated = Member("Svc", "Do");
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    Member("Target", "Run"),
                    CallTreeStatus.Expanded,
                    [
                        Leaf(repeated) with
                        {
                            GraphEvidence = Evidence(2),
                        },
                        Leaf(repeated) with
                        {
                            GraphEvidence = Evidence(3),
                        },
                    ]) with
                {
                    GraphEvidence = Evidence(1),
                });
        var method = new MethodIdentity(
            repeated.DeclaringType.Assembly,
            new Guid("11111111-1111-1111-1111-111111111111"),
            repeated.DeclaringType,
            repeated.Name,
            repeated.ParameterTypes,
            repeated.ReturnType,
            3,
            IsStatic: true);

        Assert.Equal(
            CallGraphNodeMatch.Found,
            projection.FindNode(method, out CallGraphNode node));
        Assert.Contains(
            node.GraphEvidence,
            evidence => evidence.Storage.MethodToken == 3);
    }

    [Fact]
    public void FindNodeUsesTypedStructuralFallback()
    {
        MemberRef focus = Member("Target", "Run");
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    focus,
                    CallTreeStatus.Expanded,
                    [Leaf(Member("Svc", "Do"))]));
        var method = new MethodIdentity(
            focus.DeclaringType.Assembly,
            Guid.NewGuid(),
            focus.DeclaringType,
            focus.Name,
            focus.ParameterTypes,
            focus.ReturnType,
            0x06001234,
            IsStatic: true);

        Assert.Equal(
            CallGraphNodeMatch.Found,
            projection.FindNode(method, out CallGraphNode node));
        Assert.Same(projection.Focus, node);
    }

    [Fact]
    public void FindNodeUsesRetainedCallSiteDefinitionEvidence()
    {
        MemberRef focus = Member("Target", "Run");
        MemberRef callee = Member("Svc", "Do");
        DirectCall call = Call(focus, callee, 4);
        GraphNodeEvidence callSite = CallSiteEvidence(call);
        GraphNodeEvidence definition = Evidence(3);
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    focus,
                    CallTreeStatus.Expanded,
                    [
                        Leaf(callee) with
                        {
                            GraphEvidence = new GraphNodeEvidence(
                                callSite.Storage,
                                GraphNodeIdentity.CreateDocumentLocal(),
                                correspondence: null,
                                definitionStorage:
                                    definition.Storage),
                        },
                    ]));
        var method = new MethodIdentity(
            callee.DeclaringType.Assembly,
            definition.Storage.ModuleVersionId,
            callee.DeclaringType,
            callee.Name,
            callee.ParameterTypes,
            callee.ReturnType,
            definition.Storage.MethodToken,
            IsStatic: true);

        Assert.Equal(
            CallGraphNodeMatch.Found,
            projection.FindNode(method, out CallGraphNode node));
        Assert.Equal("Do", node.Member.Name);
    }

    [Fact]
    public void FindNodeDoesNotCrossVersionedEvidence()
    {
        MemberRef repeated = Member("Svc", "Do");
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    Member("Target", "Run"),
                    CallTreeStatus.Expanded,
                    [
                        Leaf(repeated) with
                        {
                            GraphEvidence = Evidence(2),
                        },
                        Leaf(repeated) with
                        {
                            GraphEvidence = Evidence(3),
                        },
                    ]) with
                {
                    GraphEvidence = Evidence(1),
                });
        var method = new MethodIdentity(
            repeated.DeclaringType.Assembly,
            Guid.NewGuid(),
            repeated.DeclaringType,
            repeated.Name,
            repeated.ParameterTypes,
            repeated.ReturnType,
            0x06001234,
            IsStatic: true);

        Assert.Equal(
            CallGraphNodeMatch.NotProjected,
            projection.FindNode(method, out _));
    }

    [Fact]
    public void FindCalleeRowUsesRetainedNonRepresentativeCallSite()
    {
        MemberRef focus = Member("Target", "Run");
        MemberRef callee = Member("Svc", "Do");
        DirectCall first = Call(focus, callee, 4);
        DirectCall second = Call(focus, callee, 8);
        DirectCall versionSkewed = Call(focus, callee, 12);
        CallTreeNode root = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(callee) with
                {
                    GraphEvidence = CallSiteEvidence(first),
                    ParentEdgeCallSites = [first, second],
                },
                Leaf(callee) with
                {
                    GraphEvidence =
                        CallSiteEvidence(versionSkewed),
                    ParentEdgeCallSites = [versionSkewed],
                },
            ]) with
        {
            GraphEvidence = Evidence(1),
        };

        CallGraphProjection projection =
            CallGraphProjection.FromCallees(root);

        Assert.Equal(
            CallGraphRowMatch.Found,
            projection.FindFocusCalleeRow(
                second,
                out CallGraphRow row));
        Assert.Contains(
            projection.CallSites,
            site =>
                site.EdgeId == row.Number - 1
                && site.Call == second);
    }

    [Fact]
    public void FindCalleeTargetRestoresVersionDistinctOccurrenceIdentity()
    {
        var v1 = new AssemblyReferenceIdentity(
            "Versioned.Target",
            new Version(1, 0, 0, 0),
            null,
            null);
        var v2 = v1 with { Version = new Version(2, 0, 0, 0) };
        MemberRef focus = Member("Target", "Run");
        MemberRef calleeV1 = ReferencedMember(v1, "Svc", "Do");
        MemberRef calleeV2 = ReferencedMember(v2, "Svc", "Do");
        DirectCall first = Call(focus, calleeV1, 4);
        DirectCall second = Call(focus, calleeV2, 8);
        CallTreeNode root = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(calleeV1) with
                {
                    DefinitionAssemblyIdentity = v1,
                    ResolutionAssemblyIdentity = v1,
                    ParentEdgeCallSites = [first],
                },
                Leaf(calleeV2) with
                {
                    ResolutionAssemblyIdentity = v2,
                    ParentEdgeCallSites = [second],
                },
            ]);
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(root);

        Assert.Equal(
            CallGraphRowMatch.Found,
            projection.FindFocusCalleeTarget(
                first,
                out CallGraphNode firstTarget));
        Assert.Equal(v1, firstTarget.DefinitionAssemblyIdentity);
        Assert.Equal(v1, firstTarget.OccurrenceAssemblyIdentity);

        Assert.Equal(
            CallGraphRowMatch.Found,
            projection.FindFocusCalleeTarget(
                second,
                out CallGraphNode secondTarget));
        Assert.Null(secondTarget.DefinitionAssemblyIdentity);
        Assert.Equal(v2, secondTarget.OccurrenceAssemblyIdentity);
        Assert.Same(calleeV2, secondTarget.Member);
    }

    [Fact]
    public void MissingEvidenceSelectsStructuralIdentityForTheWholeProjection()
    {
        MemberRef repeated = Member("Svc", "Do");
        CallTreeNode root = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [
                Leaf(repeated) with { GraphEvidence = Evidence(2) },
                Leaf(repeated),
            ]) with
        {
            GraphEvidence = Evidence(1),
        };

        CallGraphProjection projection =
            CallGraphProjection.FromCallees(root);

        Assert.Equal(2, projection.Nodes.Length);
        CallGraphNode callee = Assert.Single(
            projection.Nodes,
            node => node.Member.Name == "Do");
        Assert.Single(callee.GraphEvidence);
    }

    [Fact]
    public void InstanceSelfRecursionFromBodyIndexCollapsesOntoFocus()
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        MethodIdentity method = index.DeclaredMethods.Single(candidate =>
            candidate.DeclaringType.Name
                == "InstanceRecursionApi"
            && candidate.Name
                == "Recurse");

        CallGraphProjection projection = CallGraphProjection.Create(
            index.BuildCallerTree(method.MetadataToken),
            index.BuildCallTree(method.MetadataToken));

        Assert.Single(
            projection.Nodes,
            node => node.Member.DeclaringType.Name
                    == "InstanceRecursionApi"
                && node.Member.Name
                    == "Recurse");
        Assert.Contains(
            projection.Edges,
            edge => edge.From == projection.Focus.Id
                && edge.To == projection.Focus.Id);
    }

    [Fact]
    public void CalleeEdgesPointFromFocusToCallee()
    {
        var projection = CallGraphProjection.FromCallees(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded, [Leaf(Member("Svc", "Do"))]));

        // Outbound: the selected overload calls its callee.
        Assert.Equal([(0, 1, (string?)null)], EdgeTuples(projection));
    }

    [Fact]
    public void CallerEdgesAreInvertedToPointIntoFocus()
    {
        var projection = CallGraphProjection.FromCallers(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded, [Leaf(Member("Client", "Invoke"))]));

        // Inbound: a reverse tree records the caller as a child, but the projected edge
        // must be oriented caller -> callee so a host never has to invert it.
        Assert.Equal([(1, 0, (string?)null)], EdgeTuples(projection));
    }

    [Fact]
    public void RowsAreNumberedEdgesInDeterministicOrder()
    {
        var target = Member("Widget", "Build");
        var callers = Node(target, CallTreeStatus.Expanded, [Leaf(Member("Program", "Main"))]);
        var callees = Node(target, CallTreeStatus.Expanded, [Leaf(Member("Store", "Save"))]);

        var projection = CallGraphProjection.Create(callers, callees);

        Assert.Equal(2, projection.RowCount);
        Assert.Equal([1, 2], projection.Rows.Select(row => row.Number));
        Assert.Equal(projection.Edges, projection.Rows.Select(row => row.Edge));
    }

    [Fact]
    public void CallerAndCalleeWalksDeduplicateTheSamePhysicalCallSite()
    {
        MemberRef focus = Member("Focus", "Run");
        MemberRef peer = Member("Peer", "Invoke");
        DirectCall focusCallsPeer = Call(focus, peer, 4);
        DirectCall peerCallsFocus = Call(peer, focus, 8);
        CallTreeNode callerPeer =
            Leaf(peer) with
            {
                ParentEdgeCallSites = [peerCallsFocus],
            };
        CallTreeNode calleeFocus =
            Leaf(
                focus,
                CallTreeStatus.AlreadyShown) with
            {
                ParentEdgeCallSites = [peerCallsFocus],
            };
        CallTreeNode calleePeer =
            Node(
                peer,
                CallTreeStatus.Expanded,
                [calleeFocus]) with
            {
                ParentEdgeCallSites = [focusCallsPeer],
            };

        CallGraphProjection projection =
            CallGraphProjection.Create(
                Node(
                    focus,
                    CallTreeStatus.Expanded,
                    [callerPeer]),
                Node(
                    focus,
                    CallTreeStatus.Expanded,
                    [calleePeer]));

        Assert.Equal(2, projection.CallSites.Length);
        CallGraphEdge peerToFocus = Assert.Single(
            projection.Edges,
            edge => projection.Nodes[edge.From].Member == peer
                && projection.Nodes[edge.To].Member == focus);
        int callSiteId = Assert.Single(
            peerToFocus.CallSiteIds);
        Assert.Same(
            peerCallsFocus,
            projection.CallSites[callSiteId].Call);
    }

    [Fact]
    public void ConflictingDetachedTargetsKeepOnePhysicalReceipt()
    {
        MemberRef focus = Member("Focus", "Run");
        MemberRef peer = Member("Peer", "Invoke");
        DirectCall focusCallsPeer = Call(focus, peer, 4);
        DirectCall peerCallsFocus = Call(peer, focus, 8);
        GraphNodeEvidence focusEvidence =
            DefinitionEvidence(focusCallsPeer);
        GraphNodeEvidence peerEvidence = Evidence(2);
        CallTreeNode callerRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Node(
                    peer,
                    CallTreeStatus.Expanded,
                    [
                        Leaf(
                            focus,
                            CallTreeStatus.AlreadyShown) with
                        {
                            GraphEvidence = focusEvidence,
                            ParentEdgeCallSites =
                                [focusCallsPeer],
                        },
                    ]) with
                {
                    GraphEvidence = peerEvidence,
                    ParentEdgeCallSites = [peerCallsFocus],
                },
            ]) with
        {
            GraphEvidence = focusEvidence,
        };
        CallTreeNode calleeRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(peer) with
                {
                    GraphEvidence = Reidentified(peerEvidence),
                    ParentEdgeCallSites = [focusCallsPeer],
                },
            ]) with
        {
            GraphEvidence = focusEvidence,
        };

        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);

        CallGraphEdge[] outbound =
        [
            .. projection.Edges.Where(
                edge => edge.From == projection.Focus.Id),
        ];
        Assert.Equal(2, outbound.Length);
        Assert.Single(
            outbound,
            edge => edge.CallSiteIds.Length == 1);
        Assert.Single(
            outbound,
            edge => edge.CallSiteIds.IsEmpty);
        Assert.Single(
            outbound,
            edge =>
                edge.HasUnavailablePhysicalOccurrences);
        Assert.Equal(2, projection.CallSites.Length);
        Assert.Equal(
            CallGraphRowMatch.Found,
            projection.FindFocusCalleeRow(
                focusCallsPeer,
                out _));
    }

    [Fact]
    public void PartiallyConflictingEdgeDisclosesMissingLoopedReceipt()
    {
        MemberRef focus = Member("Focus", "Run");
        MemberRef peer = Member("Peer", "Invoke");
        DirectCall looped =
            Call(focus, peer, 4) with { InLoop = true };
        DirectCall plain = Call(focus, peer, 12);
        DirectCall peerCallsFocus = Call(peer, focus, 20);
        GraphNodeEvidence focusEvidence =
            DefinitionEvidence(looped);
        GraphNodeEvidence peerEvidence = Evidence(2);
        GraphNodeEvidence calleeEvidence =
            Reidentified(peerEvidence);
        CallTreeNode callerRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Node(
                    peer,
                    CallTreeStatus.Expanded,
                    [
                        Leaf(
                            focus,
                            CallTreeStatus.AlreadyShown) with
                        {
                            GraphEvidence = focusEvidence,
                            ParentEdgeCallSites = [looped],
                        },
                    ]) with
                {
                    GraphEvidence = peerEvidence,
                    ParentEdgeCallSites = [peerCallsFocus],
                },
            ]) with
        {
            GraphEvidence = focusEvidence,
        };
        CallTreeNode calleeRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(
                    peer,
                    inLoop: true,
                    loopHint: "loop") with
                {
                    GraphEvidence = calleeEvidence,
                    ParentEdgeCallSites = [looped, plain],
                },
            ]) with
        {
            GraphEvidence = focusEvidence,
        };

        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);
        int calleeId = Assert.Single(
            projection.Nodes,
            node => node.Identity == calleeEvidence.Identity).Id;
        CallGraphEdge partial = Assert.Single(
            projection.Edges,
            edge => edge.From == projection.Focus.Id
                && edge.To == calleeId);

        int callSiteId = Assert.Single(partial.CallSiteIds);
        Assert.Same(
            plain,
            projection.CallSites[callSiteId].Call);
        Assert.True(partial.HasUnavailablePhysicalOccurrences);
        Assert.True(partial.AnyCallInLoop);
        Assert.Null(partial.LegacyLoopHint);
    }

    [Fact]
    public void ConflictingDetachedCallersKeepOnePhysicalReceipt()
    {
        MemberRef focus = Member("Focus", "Run");
        MemberRef peer = Member("Peer", "Invoke");
        DirectCall focusCallsPeer = Call(focus, peer, 4);
        DirectCall peerCallsFocus = Call(peer, focus, 8);
        GraphNodeEvidence focusEvidence = Evidence(1);
        GraphNodeEvidence peerEvidence =
            DefinitionEvidence(peerCallsFocus);
        CallTreeNode callerRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(peer) with
                {
                    GraphEvidence = peerEvidence,
                    ParentEdgeCallSites = [peerCallsFocus],
                },
            ]) with
        {
            GraphEvidence = focusEvidence,
        };
        CallTreeNode calleeRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Node(
                    peer,
                    CallTreeStatus.Expanded,
                    [
                        Leaf(
                            focus,
                            CallTreeStatus.AlreadyShown) with
                        {
                            GraphEvidence = focusEvidence,
                            ParentEdgeCallSites =
                                [peerCallsFocus],
                        },
                    ]) with
                {
                    GraphEvidence = Reidentified(peerEvidence),
                    ParentEdgeCallSites = [focusCallsPeer],
                },
            ]) with
        {
            GraphEvidence = focusEvidence,
        };

        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);

        CallGraphEdge[] inbound =
        [
            .. projection.Edges.Where(
                edge => edge.To == projection.Focus.Id),
        ];
        Assert.Equal(2, inbound.Length);
        Assert.Single(
            inbound,
            edge => edge.CallSiteIds.Length == 1);
        Assert.Single(
            inbound,
            edge =>
                edge.HasUnavailablePhysicalOccurrences);
        Assert.Equal(2, projection.CallSites.Length);
    }

    [Fact]
    public void SameMvidSitesFromDistinctArtifactsRemainDistinct()
    {
        MemberRef focus = Member("Focus", "Run");
        MemberRef peer = Member("Peer", "Invoke");
        DirectCall peerCallsFocus = Call(peer, focus, 8);
        GraphNodeEvidence firstCallerEvidence =
            DefinitionEvidence(peerCallsFocus);
        GraphNodeEvidence secondCallerEvidence =
            DefinitionEvidence(peerCallsFocus);
        CallTreeNode root = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(peer) with
                {
                    GraphEvidence = firstCallerEvidence,
                    ParentEdgeCallSites = [peerCallsFocus],
                    ParentEdgeCallerDefinition =
                        firstCallerEvidence.Storage,
                },
                Leaf(peer) with
                {
                    GraphEvidence = secondCallerEvidence,
                    ParentEdgeCallSites = [peerCallsFocus],
                    ParentEdgeCallerDefinition =
                        secondCallerEvidence.Storage,
                },
            ]) with
        {
            GraphEvidence = Evidence(1),
        };

        CallGraphProjection projection =
            CallGraphProjection.FromCallers(root);

        Assert.Equal(2, projection.Edges.Length);
        Assert.Equal(2, projection.CallSites.Length);
        Assert.All(
            projection.Edges,
            edge => Assert.Single(edge.CallSiteIds));
    }

    [Fact]
    public void DetachedCatalogDirectionsDeduplicatePhysicalReceipts()
    {
        string path =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        LibraryBodyIndex index = LibraryBodyIndex.Open(path);
        MethodIdentity method = index.DeclaredMethods.Single(
            candidate =>
                candidate.DeclaringType.Name
                    == "InstanceRecursionApi"
                && candidate.Name == "IsEven");
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "call-graph projection test"));

        CallTreeNode callers;
        using (var scope = new CatalogCallGraphScope(
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(path)),
            [new CatalogCallGraphParticipant(index, assembly)]))
        {
            callers = scope.Detach(
                scope.BuildCallerTree(
                    index,
                    method.MetadataToken));
        }

        CallTreeNode callees;
        using (var scope = new CatalogCallGraphScope(
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(path)),
            [new CatalogCallGraphParticipant(index, assembly)]))
        {
            callees = scope.Detach(
                scope.BuildCallTree(
                    index,
                    method.MetadataToken));
        }

        CallGraphProjection projection =
            CallGraphProjection.Create(callers, callees);

        Assert.Equal(2, projection.CallSites.Length);
        Assert.All(
            projection.CallSites.GroupBy(site =>
                (
                    site.Call.EvidenceMethod.ModuleVersionId,
                    site.Call.EvidenceMethod.MetadataToken,
                    site.Call.ILOffset,
                    site.Call.OperandToken)),
            group => Assert.Single(group));
        Assert.All(
            projection.CallSites,
            site => Assert.False(site.Identity.IsPortable));
    }

    [Fact]
    public void MixedEvidenceProjectionUsesOneReceiptIdentityDomain()
    {
        MemberRef focus = Member("Focus", "Run");
        DirectCall recursive = Call(focus, focus, 4);
        GraphNodeEvidence evidence =
            DefinitionEvidence(recursive);
        CallTreeNode callerRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(
                    focus,
                    CallTreeStatus.AlreadyShown) with
                {
                    GraphEvidence = evidence,
                    ParentEdgeCallSites = [recursive],
                    ParentEdgeCallerDefinition =
                        evidence.Storage,
                },
            ]) with
        {
            GraphEvidence = evidence,
        };
        CallTreeNode calleeRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(
                    focus,
                    CallTreeStatus.AlreadyShown) with
                {
                    ParentEdgeCallSites = [recursive],
                },
            ]);

        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);

        Assert.Single(projection.Edges);
        Assert.Single(projection.CallSites);
        Assert.True(projection.CallSites[0].Identity.IsPortable);
    }

    [Fact]
    public void ContradictoryPhysicalReceiptEvidenceIsRejected()
    {
        MemberRef focus = Member("Focus", "Run");
        MemberRef peer = Member("Peer", "Invoke");
        DirectCall plain = Call(focus, peer, 4);
        DirectCall looped = plain with { InLoop = true };
        CallTreeNode root = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(peer) with
                {
                    ParentEdgeCallSites = [plain, looped],
                },
            ]);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                CallGraphProjection.FromCallees(root));

        Assert.Contains(
            "contradictory evidence",
            exception.Message);
    }

    [Fact]
    public void NodesAreOrderedFocusThenCallersThenCallees()
    {
        var target = Member("Widget", "Build");
        var callers = Node(target, CallTreeStatus.Expanded, [Leaf(Member("Program", "Main"))]);
        var callees = Node(target, CallTreeStatus.Expanded, [Leaf(Member("Store", "Save"))]);

        var projection = CallGraphProjection.Create(callers, callees);

        Assert.Equal(
            ["Widget.Build()", "Program.Main()", "Store.Save()"],
            projection.Nodes.Select(n => n.Label));
        // Ids are dense and match position: hosts index into Nodes by edge endpoint.
        Assert.Equal([0, 1, 2], projection.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void ProjectionIsDeterministicAcrossRuns()
    {
        var target = Member("Widget", "Build");
        var callers = Node(target, CallTreeStatus.Expanded,
        [
            Node(Member("Api", "Handle"), CallTreeStatus.Expanded, [Leaf(Member("Program", "Main"))]),
            Leaf(Member("Loop", "Tick"), inLoop: true, loopHint: "loop call"),
        ]);
        var callees = Node(target, CallTreeStatus.Expanded,
        [
            Leaf(Member("Store", "Save")),
            Leaf(Member("Log", "Write"), CallTreeStatus.External),
        ]);

        var first = CallGraphProjection.Create(callers, callees);
        var second = CallGraphProjection.Create(callers, callees);

        Assert.Equal(first.Nodes.Select(n => (n.Id, n.Label, n.Kind)), second.Nodes.Select(n => (n.Id, n.Label, n.Kind)));
        Assert.Equal(EdgeTuples(first), EdgeTuples(second));

        // Ordering is contract, so pin it exactly: focus, caller DFS, callee DFS.
        Assert.Equal(
            ["Widget.Build()", "Api.Handle()", "Program.Main()", "Loop.Tick()", "Store.Save()", "Log.Write()"],
            first.Nodes.Select(n => n.Label));
        Assert.Equal(
            [(1, 0, null), (2, 1, null), (3, 0, "loop call"), (0, 4, null), (0, 5, null)],
            EdgeTuples(first));
    }

    [Fact]
    public void SharedCalleeCollapsesToOneNodeWithTwoIncomingEdges()
    {
        var shared = Member("Shared", "S");
        var projection = CallGraphProjection.FromCallees(
            Node(Member("Root", "M"), CallTreeStatus.Expanded,
            [
                Node(Member("A", "A"), CallTreeStatus.Expanded, [Leaf(shared)]),
                Node(Member("B", "B"), CallTreeStatus.Expanded, [Leaf(shared, CallTreeStatus.AlreadyShown)]),
            ]));

        Assert.Single(projection.Nodes, n => n.Label == "Shared.S()");
        Assert.Contains((1, 2, (string?)null), EdgeTuples(projection));
        Assert.Contains((3, 2, (string?)null), EdgeTuples(projection));
    }

    [Fact]
    public void CycleCollapsesBackToTheSameNode()
    {
        // A -> B -> A (the second A is recorded AlreadyShown by the tree builder).
        var projection = CallGraphProjection.FromCallees(
            Node(Member("A", "A"), CallTreeStatus.Expanded,
            [
                Node(Member("B", "B"), CallTreeStatus.Expanded,
                    [Leaf(Member("A", "A"), CallTreeStatus.AlreadyShown)]),
            ]));

        Assert.Equal(2, projection.Nodes.Length);
        Assert.Equal([(0, 1, (string?)null), (1, 0, (string?)null)], EdgeTuples(projection));
    }

    [Fact]
    public void FocusCyclesAreShortestThenStableEdgeRowOrder()
    {
        MemberRef focus = Member("A", "A");
        var projection = CallGraphProjection.FromCallees(
            Node(
                focus,
                CallTreeStatus.Expanded,
                [
                    Node(
                        Member("B", "B"),
                        CallTreeStatus.Expanded,
                        [Leaf(focus, CallTreeStatus.AlreadyShown)]),
                    Leaf(focus, CallTreeStatus.AlreadyShown),
                    Node(
                        Member("E", "E"),
                        CallTreeStatus.Expanded,
                        [Leaf(focus, CallTreeStatus.AlreadyShown)]),
                    Node(
                        Member("C", "C"),
                        CallTreeStatus.Expanded,
                        [
                            Node(
                                Member("D", "D"),
                                CallTreeStatus.Expanded,
                                [Leaf(focus, CallTreeStatus.AlreadyShown)]),
                        ]),
                ]));

        CallGraphCycleSearchResult result =
            projection.FindFocusCycles();

        Assert.True(result.IsComplete);
        Assert.Equal(
            [[3], [1, 2], [4, 5], [6, 7, 8]],
            result.Witnesses.Select(witness =>
                witness.EdgeRows.ToArray()));
        Assert.True(result.Witnesses[0].IsDirect);
        Assert.False(result.Witnesses[1].IsDirect);
    }

    [Fact]
    public void FocusCycleSearchReportsIndependentCostLimits()
    {
        MemberRef focus = Member("A", "A");
        var projection = CallGraphProjection.FromCallees(
            Node(
                focus,
                CallTreeStatus.Expanded,
                [
                    Node(
                        Member("B", "B"),
                        CallTreeStatus.Expanded,
                        [Leaf(focus, CallTreeStatus.AlreadyShown)]),
                    Leaf(focus, CallTreeStatus.AlreadyShown),
                ]));

        CallGraphCycleSearchResult witnessLimited =
            projection.FindFocusCycles(
                new CallGraphCycleSearchOptions
                {
                    MaxWitnesses = 1,
                });
        CallGraphCycleSearchResult pathLimited =
            projection.FindFocusCycles(
                new CallGraphCycleSearchOptions
                {
                    MaxPaths = 1,
                });

        Assert.Single(witnessLimited.Witnesses);
        Assert.Equal(
            CallGraphCycleSearchLimit.WitnessBudget,
            witnessLimited.Limits);
        Assert.Single(pathLimited.Witnesses);
        Assert.Equal([3], pathLimited.Witnesses[0].EdgeRows);
        Assert.Equal(
            CallGraphCycleSearchLimit.PathBudget,
            pathLimited.Limits);
    }

    [Fact]
    public void ExhaustedTraversalProducesACompleteEmptyCycleCensus()
    {
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Leaf(Member("A", "A")));

        CallGraphCycleSearchResult result =
            projection.FindFocusCycles();

        Assert.True(result.IsComplete);
        Assert.Empty(result.Witnesses);
        Assert.False(
            projection.HasUnexploredTraversalBoundary);
    }

    [Fact]
    public void BodilessCalleeKeepsAnEmptyCycleCensusIncomplete()
    {
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Leaf(
                    Member("IService", "Run"),
                    CallTreeStatus.Bodiless));

        Assert.True(
            projection.HasUnexploredTraversalBoundary);
        Assert.False(
            projection.HasAnalysisFailureBoundary);
        Assert.Empty(
            projection.FindFocusCycles().Witnesses);
    }

    [Fact]
    public void BodyAnalysisFailureRemainsAnExplicitTraversalBoundary()
    {
        CallTreeNode failed =
            Leaf(
                Member("Service", "Run"),
                CallTreeStatus.AnalysisIncomplete) with
            {
                Diagnostic = new AnalysisDiagnostic(
                    0x06000001,
                    "Service.Run",
                    "BadImageFormatException: invalid body"),
            };

        CallGraphProjection projection =
            CallGraphProjection.FromCallees(failed);

        Assert.True(
            projection.HasUnexploredTraversalBoundary);
        Assert.True(
            projection.HasAnalysisFailureBoundary);
    }

    [Fact]
    public void UnresolvedVirtualDispatchKeepsAnEmptyCycleCensusIncomplete()
    {
        CallTreeNode virtualTarget =
            Leaf(
                Member("Service", "Run")) with
            {
                HasUnresolvedDispatch = true,
            };

        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    Member("Caller", "Invoke"),
                    CallTreeStatus.Expanded,
                    [virtualTarget]));

        Assert.True(
            projection.HasUnexploredTraversalBoundary);
        Assert.False(
            projection.HasAnalysisFailureBoundary);
        Assert.Empty(
            projection.FindFocusCycles().Witnesses);
    }

    [Fact]
    public void CycleWitnessSurvivesUnresolvedVirtualDispatch()
    {
        MemberRef focus = Member("Caller", "Invoke");
        CallTreeNode returnToFocus =
            Leaf(
                focus,
                CallTreeStatus.AlreadyShown) with
            {
                HasUnresolvedDispatch = true,
            };
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    focus,
                    CallTreeStatus.Expanded,
                    [
                        Node(
                            Member("Service", "Run"),
                            CallTreeStatus.Expanded,
                            [returnToFocus]),
                    ]));

        CallGraphCycleSearchResult result =
            projection.FindFocusCycles();

        Assert.Single(result.Witnesses);
        Assert.True(
            projection.HasUnexploredTraversalBoundary);
    }

    [Fact]
    public void FocusCycleSearchDoesNotRepeatNodesWithinAWitness()
    {
        MemberRef focus = Member("A", "A");
        MemberRef b = Member("B", "B");
        MemberRef c = Member("C", "C");
        var projection = CallGraphProjection.FromCallees(
            Node(
                focus,
                CallTreeStatus.Expanded,
                [
                    Node(
                        b,
                        CallTreeStatus.Expanded,
                        [
                            Node(
                                c,
                                CallTreeStatus.Expanded,
                                [
                                    Leaf(
                                        b,
                                        CallTreeStatus.AlreadyShown),
                                    Leaf(
                                        focus,
                                        CallTreeStatus.AlreadyShown),
                                ]),
                        ]),
                ]));

        CallGraphCycleWitness witness =
            Assert.Single(
                projection.FindFocusCycles().Witnesses);

        Assert.Equal([1, 2, 4], witness.EdgeRows);
    }

    [Fact]
    public void CycleCompletenessCollapsesBoundariesWithinOneDirection()
    {
        MemberRef shared = Member("Shared", "Work");
        CallGraphProjection complete =
            CallGraphProjection.FromCallees(
                Node(
                    Member("A", "A"),
                    CallTreeStatus.Expanded,
                    [
                        Node(
                            Member("B", "B"),
                            CallTreeStatus.Expanded,
                            [
                                Leaf(
                                    shared,
                                    CallTreeStatus.DepthLimited),
                            ]),
                        Leaf(
                            shared,
                            CallTreeStatus.Expanded),
                    ]));
        CallGraphProjection incomplete =
            CallGraphProjection.FromCallees(
                Node(
                    Member("A", "A"),
                    CallTreeStatus.Expanded,
                    [
                        Leaf(
                            shared,
                            CallTreeStatus.DepthLimited),
                    ]));

        Assert.False(
            complete.HasUnexploredTraversalBoundary);
        Assert.True(
            incomplete.HasUnexploredTraversalBoundary);
    }

    [Fact]
    public void CallerLeafDoesNotHideAnOutboundTraversalBoundary()
    {
        MemberRef focus = Member("A", "A");
        CallTreeNode scopeLocalCallerLeaf =
            Leaf(focus);
        CallTreeNode boundedCallee =
            Node(
                focus,
                CallTreeStatus.Expanded,
                [
                    Leaf(
                        Member("B", "B"),
                        CallTreeStatus.DepthLimited),
                ]);

        CallGraphProjection projection =
            CallGraphProjection.Create(
                scopeLocalCallerLeaf,
                boundedCallee);

        Assert.True(
            projection.HasUnexploredTraversalBoundary);
    }

    [Fact]
    public void CompleteCalleeTraversalProvesFocusCycleCompleteness()
    {
        MemberRef focus = Member("A", "A");
        CallGraphProjection projection =
            CallGraphProjection.Create(
                Leaf(
                    focus,
                    CallTreeStatus.DepthLimited),
                Leaf(focus));

        Assert.False(
            projection.HasUnexploredTraversalBoundary);
    }

    [Fact]
    public void AlreadyShownDoesNotHideATruncatedPrimaryOccurrence()
    {
        MemberRef focus = Member("A", "A");
        CallGraphProjection projection =
            CallGraphProjection.FromCallees(
                Node(
                    focus,
                    CallTreeStatus.Truncated,
                    [
                        Leaf(
                            focus,
                            CallTreeStatus.AlreadyShown),
                    ]));

        Assert.True(
            projection.HasUnexploredTraversalBoundary);
    }

    [Fact]
    public void GenericSelfRecursionCollapsesOntoTheFocusNode()
    {
        // The root is built as an open definition while the recursive callee edge is a
        // constructed MethodSpec. Both must erase to one identity, so recursion is a
        // self-loop rather than two same-named nodes.
        var openReturn = TypeRef.MethodGenericParameter(0, "T");
        var rootMember = new MemberRef(Type("Calc"), "Recurse", [], openReturn, MemberKind.Method) { GenericArity = 1 };
        var recursiveCall = new MemberRef(Type("Calc"), "Recurse", [], TypeRef.CoreLib("System", "Int32"), MemberKind.Method)
        {
            GenericArity = 1,
            TypeArguments = [TypeRef.CoreLib("System", "Int32")],
            OpenReturnType = openReturn,
        };

        var projection = CallGraphProjection.FromCallees(
            Node(rootMember, CallTreeStatus.Expanded, [Leaf(recursiveCall, CallTreeStatus.AlreadyShown)]));

        Assert.Single(projection.Nodes);
        Assert.Equal([(0, 0, (string?)null)], EdgeTuples(projection));
    }

    [Fact]
    public void IdentityComesFromMemberNotLabel()
    {
        // Two members whose declaring type shares namespace + name but differs by assembly
        // produce the SAME label yet must stay distinct nodes. This is the guard against a
        // host (or a future refactor) keying nodes on display text.
        var fromA = new MemberRef(TypeRef.Definition("AsmA", "Shared", "Widget"), "Work", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method);
        var fromB = new MemberRef(TypeRef.Definition("AsmB", "Shared", "Widget"), "Work", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method);

        var projection = CallGraphProjection.FromCallees(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded, [Leaf(fromA), Leaf(fromB)]));

        Assert.Equal(3, projection.Nodes.Length);
        Assert.Equal(projection.Nodes[1].Label, projection.Nodes[2].Label);
        Assert.NotEqual(projection.Nodes[1].Member.DeclaringType.Assembly, projection.Nodes[2].Member.DeclaringType.Assembly);
    }

    [Fact]
    public void ReturnTypeOnlyOverloadsStayDistinct()
    {
        var toInt = new MemberRef(Type("Conv"), "op_Implicit", [Type("Src")], TypeRef.CoreLib("System", "Int32"), MemberKind.Method);
        var toString = new MemberRef(Type("Conv"), "op_Implicit", [Type("Src")], TypeRef.CoreLib("System", "String"), MemberKind.Method);

        var projection = CallGraphProjection.FromCallees(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded, [Leaf(toInt), Leaf(toString)]));

        Assert.Equal(3, projection.Nodes.Length);
    }

    [Theory]
    [InlineData(CallTreeStatus.External, CallGraphNodeKind.External)]
    [InlineData(CallTreeStatus.DepthLimited, CallGraphNodeKind.Truncated)]
    [InlineData(CallTreeStatus.Truncated, CallGraphNodeKind.Truncated)]
    [InlineData(CallTreeStatus.Leaf, CallGraphNodeKind.Normal)]
    [InlineData(CallTreeStatus.Expanded, CallGraphNodeKind.Normal)]
    [InlineData(CallTreeStatus.AlreadyShown, CallGraphNodeKind.Normal)]
    public void StatusMapsToNodeKind(CallTreeStatus status, CallGraphNodeKind expected)
    {
        var projection = CallGraphProjection.FromCallees(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded, [Leaf(Member("Other", "Work"), status)]));

        Assert.Equal(expected, projection.Nodes[1].Kind);
    }

    [Fact]
    public void StrongestKindWinsWhenAMemberIsReachedTwice()
    {
        var repeated = Member("Deep", "Work");
        var projection = CallGraphProjection.FromCallees(
            Node(Member("Root", "M"), CallTreeStatus.Expanded,
            [
                // Expanded in one place ...
                Node(repeated, CallTreeStatus.Expanded, [Leaf(Member("Leaf", "L"))]),
                // ... depth-limited in another. Expanded outranks the boundary.
                Leaf(repeated, CallTreeStatus.DepthLimited),
            ]));

        Assert.Equal(CallGraphNodeKind.Normal, projection.Nodes[1].Kind);
    }

    [Fact]
    public void FocusKindIsStickyWhenTheFocusIsAlsoReachedAsABoundary()
    {
        var target = Member("A", "A");
        var projection = CallGraphProjection.FromCallees(
            Node(target, CallTreeStatus.Expanded,
            [
                Node(Member("B", "B"), CallTreeStatus.Expanded,
                    [Leaf(target, CallTreeStatus.DepthLimited)]),
            ]));

        // The focus must not be demoted to a dead end by a depth-limited back edge.
        Assert.Equal(CallGraphNodeKind.Focus, projection.Nodes[0].Kind);
    }

    [Fact]
    public void LoopAnnotationSurvivesEdgeCollapse()
    {
        // The same caller->callee edge seen twice, looped at only one call site, keeps the
        // loop annotation rather than losing it to whichever site was visited last.
        var shared = Member("Cache", "Get");
        var projection = CallGraphProjection.FromCallees(
            Node(Member("Root", "M"), CallTreeStatus.Expanded,
            [
                Leaf(shared),
                Leaf(shared, inLoop: true, loopHint: "hot loop"),
            ]));

        Assert.Equal([(0, 1, "hot loop")], EdgeTuples(projection));
    }

    [Fact]
    public void LoopWithoutHintFallsBackToGenericLabel()
    {
        var projection = CallGraphProjection.FromCallees(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded,
            [Leaf(Member("Svc", "Do"), inLoop: true, loopHint: null)]));

        Assert.Equal([(0, 1, "loop")], EdgeTuples(projection));
    }

    [Fact]
    public void PhysicalCallSitesOverrideLegacyLoopEvidenceOnCollapsedEdge()
    {
        MemberRef focus = Member("Target", "Run");
        MemberRef peer = Member("Peer", "Tick");
        DirectCall physical = Call(focus, peer, 4);
        CallTreeNode callerRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Node(
                    peer,
                    CallTreeStatus.Expanded,
                    [Leaf(focus, inLoop: true, loopHint: "loop call")]),
            ]);
        CallTreeNode calleeRoot = Node(
            focus,
            CallTreeStatus.Expanded,
            [
                Leaf(peer) with
                {
                    ParentEdgeCallSites = [physical],
                },
            ]);

        CallGraphProjection projection =
            CallGraphProjection.Create(callerRoot, calleeRoot);
        CallGraphEdge edge = Assert.Single(
            projection.Edges.Where(
                candidate =>
                    candidate.From == 0
                    && candidate.To == 1));

        Assert.Single(edge.CallSiteIds);
        Assert.False(edge.AnyCallInLoop);
        Assert.Null(edge.LegacyLoopHint);
        Assert.False(
            projection.CallSites[edge.CallSiteIds[0]].Call.InLoop);
    }

    [Fact]
    public void UnsupportedCalleeRootCollapsesOntoResolvedFocus()
    {
        var resolved = Member("Widget", "Build");
        var callers = Node(resolved, CallTreeStatus.Expanded, [Leaf(Member("Program", "Main"))]);
        var calleeRoot = Leaf(MemberRef.Unsupported("method token 0x06000001"));

        var projection = CallGraphProjection.Create(callers, calleeRoot);

        Assert.Equal(2, projection.Nodes.Length);
        Assert.Equal("Widget.Build()", projection.Focus.Label);
        Assert.Equal([(1, 0, (string?)null)], EdgeTuples(projection));
    }

    [Fact]
    public void RejectsDifferentSelectedMembers()
    {
        var callers = Leaf(Member("Target", "Run"), CallTreeStatus.Expanded);
        var callees = Leaf(Member("Other", "Run"), CallTreeStatus.Expanded);

        Assert.Throws<ArgumentException>(() => CallGraphProjection.Create(callers, callees));
    }

    [Fact]
    public void RejectsDifferentUnsupportedRoots()
    {
        var callers = Leaf(MemberRef.Unsupported("method token 0x06000001"));
        var callees = Leaf(MemberRef.Unsupported("method token 0x06000002"));

        Assert.Throws<ArgumentException>(() => CallGraphProjection.Create(callers, callees));
    }

    [Fact]
    public void RejectsEmptyInput()
        => Assert.Throws<ArgumentException>(() => CallGraphProjection.Create(null, null));

    [Fact]
    public void RejectsNullSingleSidedRoots()
    {
        Assert.Throws<ArgumentNullException>(() => CallGraphProjection.FromCallers(null!));
        Assert.Throws<ArgumentNullException>(() => CallGraphProjection.FromCallees(null!));
    }

    [Fact]
    public void LoopAnnotationSurvivesEdgeInversionOnTheCallerSide()
    {
        // A caller that invokes the focus from inside a loop keeps its annotation when the
        // edge is inverted to point into the focus — the label belongs to the edge, not to
        // the direction it was discovered in.
        var projection = CallGraphProjection.FromCallers(
            Node(Member("Target", "Run"), CallTreeStatus.Expanded,
            [Leaf(Member("Pump", "Tick"), inLoop: true, loopHint: "loop call")]));

        Assert.Equal([(1, 0, "loop call")], EdgeTuples(projection));
    }

    [Fact]
    public void SameNameMembersFromDifferentAssembliesStayDistinct()
    {
        // Two callees whose declaring type has the same namespace + name but a different
        // assembly must not collapse: the display spelling drops the assembly, but they are
        // genuinely different members (#1741-class hazard). Identity is structural, so the
        // projection must keep them apart even though both would render the same label.
        var fromA = new MemberRef(
            TypeRef.Definition("AsmA", "Shared", "Widget"), "Work", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method);
        var fromB = new MemberRef(
            TypeRef.Definition("AsmB", "Shared", "Widget"), "Work", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method);

        var callees = Node(Member("Target", "Run"), CallTreeStatus.Expanded, [Leaf(fromA), Leaf(fromB)]);

        var projection = CallGraphProjection.FromCallees(callees);

        Assert.Equal(3, projection.Nodes.Length);
        Assert.Equal(2, projection.Edges.Length);
        Assert.All(projection.Edges, e => Assert.Equal(0, e.From));
        Assert.Equal(2, projection.Edges.Select(e => e.To).Distinct().Count());
    }

    [Fact]
    public void FocusKeepsFanOutFromTheCalleeWalkAndRootKindFromTheCallerWalk()
    {
        var focus = Member("Ns.Target", "Run");
        var callerRoot = new CallTreeNode(
            focus, null, CallTreeStatus.Expanded, [Leaf(Member("Ns.Up", "CallsIn"))],
            new CallTreePerf(0, 7, 2, false, null, "target"));
        var calleeRoot = new CallTreeNode(
            focus, null, CallTreeStatus.Expanded, [Leaf(Member("Ns.Down", "CalledBy"))],
            new CallTreePerf(9, 0, 3, false));

        var projection = CallGraphProjection.Create(callerRoot, calleeRoot);
        var perf = projection.Nodes[0].Perf;

        Assert.NotNull(perf);
        // Each direction measures one degree and hard-codes the other to zero, so the focus
        // must publish the caller walk's fan-in and the callee walk's fan-out, not one record.
        Assert.Equal(9, perf.Fanout);
        Assert.Equal(7, perf.Fanin);
        Assert.Equal(3, perf.MaxDepth);
        Assert.Equal("target", perf.RootKind);
    }

    [Fact]
    public void MemberSeenByBothWalksKeepsTheDegreeEachWalkMeasured()
    {
        var focus = Member("Ns.Target", "Run");
        var shared = Member("Ns.Both", "Cycles");
        // The caller walk runs first and reports fan-out 0 for every node it sees; without a
        // merge its zero would pin the shared node and erase the callee walk's real fan-out.
        var callerRoot = new CallTreeNode(
            focus, null, CallTreeStatus.Expanded,
            [new CallTreeNode(shared, null, CallTreeStatus.Leaf, [], new CallTreePerf(0, 4, 1, false))],
            new CallTreePerf(0, 4, 2, false, null, "target"));
        var calleeRoot = new CallTreeNode(
            focus, null, CallTreeStatus.Expanded,
            [new CallTreeNode(shared, null, CallTreeStatus.Leaf, [], new CallTreePerf(5, 0, 1, false))],
            new CallTreePerf(2, 0, 2, false));

        var projection = CallGraphProjection.Create(callerRoot, calleeRoot);
        var sharedNode = Assert.Single(projection.Nodes, n => n.Member.Name == "Cycles");

        Assert.NotNull(sharedNode.Perf);
        Assert.Equal(5, sharedNode.Perf.Fanout);
        Assert.Equal(4, sharedNode.Perf.Fanin);
    }

    [Fact]
    public void MergingCuesNeverErasesAnObservationWithABareBoundaryOccurrence()
    {
        var focus = Member("Ns.Target", "Run");
        var external = Member("Ns.Far", "Boundary");
        // The bare occurrence arrives FIRST, on the caller walk, so a first-non-null-wins
        // rule pins the empty record and the callee walk's real cues are lost.
        var callerRoot = new CallTreeNode(
            focus, null, CallTreeStatus.Expanded,
            [new CallTreeNode(external, null, CallTreeStatus.External, [], new CallTreePerf(0, 0, 1, false))],
            new CallTreePerf(0, 0, 1, false));
        var calleeRoot = new CallTreeNode(
            focus, null, CallTreeStatus.Expanded,
            [new CallTreeNode(external, null, CallTreeStatus.External, [], new CallTreePerf(2, 3, 1, true, "loop", null, null, "Other.dll"))],
            new CallTreePerf(0, 0, 1, false));

        var projection = CallGraphProjection.Create(callerRoot, calleeRoot);
        var node = Assert.Single(projection.Nodes, n => n.Member.Name == "Boundary");

        Assert.NotNull(node.Perf);
        Assert.Equal(2, node.Perf.Fanout);
        Assert.Equal(3, node.Perf.Fanin);
        Assert.Equal("Other.dll", node.Perf.Source);
        Assert.True(node.Perf.InLoop);
        Assert.Equal("loop", node.Perf.LoopHint);
    }
}