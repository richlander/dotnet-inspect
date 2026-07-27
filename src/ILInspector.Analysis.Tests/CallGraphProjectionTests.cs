using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;

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
        => [.. projection.Edges.Select(e => (e.From, e.To, e.LoopLabel))];

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
}
