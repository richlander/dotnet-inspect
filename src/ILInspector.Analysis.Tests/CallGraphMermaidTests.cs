using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace ILInspector.Analysis.Tests;

/// <summary>
/// Covers the host-neutral Mermaid call-graph projection (issue #3120): edge
/// direction, escaping, duplicate/cycle collapsing, boundary/external statuses,
/// deterministic ids/ordering, loop-call annotations, and the exact combined
/// caller/target/callee document. Trees are constructed directly so the projection
/// is exercised in isolation from IL decoding.
/// </summary>
public class CallGraphMermaidTests
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

    static string[] Lines(string mermaid)
        => mermaid.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

    [Fact]
    public void RendersOutboundEdgeFromTargetToCallee()
    {
        var callees = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Service", "Do"))]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        Assert.Equal("flowchart LR", lines[0]);
        Assert.Contains("n0[\"Target.Run()\"]:::target", lines);
        Assert.Contains("n1[\"Service.Do()\"]", lines);
        // Outbound: the selected overload flows to its callee.
        Assert.Contains("n0 --> n1", lines);
        Assert.DoesNotContain("n1 --> n0", lines);
    }

    [Fact]
    public void RendersInboundEdgeFromCallerIntoTarget()
    {
        var callers = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Client", "Invoke"))]);

        var lines = Lines(CallGraphMermaid.RenderCallers(callers));

        Assert.Contains("n0[\"Target.Run()\"]:::target", lines);
        Assert.Contains("n1[\"Client.Invoke()\"]", lines);
        // Inbound: the caller flows into the selected overload.
        Assert.Contains("n1 --> n0", lines);
        Assert.DoesNotContain("n0 --> n1", lines);
    }

    [Fact]
    public void EscapesHostileMemberNames()
    {
        var hostile = Member("Weird", "a\"b<c>d#e&f|g");
        var mermaid = CallGraphMermaid.RenderCallees(Leaf(hostile, CallTreeStatus.Expanded));
        var lines = Lines(mermaid);

        Assert.Contains("n0[\"Weird.a#quot;b#60;c#62;d#35;e#38;f#124;g()\"]:::target", lines);
        // The raw hostile characters never leak into the flowchart body.
        Assert.DoesNotContain('<', mermaid);
        Assert.DoesNotContain('>', mermaid);
        Assert.DoesNotContain('|', mermaid);
        // The only double quotes are the label delimiters themselves.
        Assert.Equal(2, mermaid.Count(ch => ch == '"'));
    }

    [Fact]
    public void CollapsesSharedCalleeIntoOneNodeWithTwoIncomingEdges()
    {
        var shared = Member("Shared", "S");
        var callees = Node(
            Member("Root", "M"),
            CallTreeStatus.Expanded,
            [
                Node(Member("A", "A"), CallTreeStatus.Expanded, [Leaf(shared)]),
                Node(Member("B", "B"), CallTreeStatus.Expanded, [Leaf(shared, CallTreeStatus.AlreadyShown)]),
            ]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        // Shared.S is a single node (n2), reached from both A (n1) and B (n3).
        Assert.Single(lines, line => line.Contains("\"Shared.S()\""));
        Assert.Contains("n2[\"Shared.S()\"]", lines);
        Assert.Contains("n1 --> n2", lines);
        Assert.Contains("n3 --> n2", lines);
        // The already-shown reference is not styled as a boundary.
        Assert.DoesNotContain("n2[\"Shared.S()\"]:::truncated", lines);
    }

    [Fact]
    public void CollapsesCycleBackToTheSameNode()
    {
        // A -> B -> A (the second A is recorded AlreadyShown by the tree builder).
        var callees = Node(
            Member("A", "A"),
            CallTreeStatus.Expanded,
            [
                Node(Member("B", "B"), CallTreeStatus.Expanded,
                    [Leaf(Member("A", "A"), CallTreeStatus.AlreadyShown)]),
            ]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        Assert.Single(lines, line => line.Contains("\"A.A()\""));
        Assert.Contains("n0 --> n1", lines);
        Assert.Contains("n1 --> n0", lines);
    }

    [Fact]
    public void StylesExternalCalleeAndEmitsExternalClassDef()
    {
        var callees = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(Member("System.Console", "WriteLine"), CallTreeStatus.External)]);

        var mermaid = CallGraphMermaid.RenderCallees(callees);
        var lines = Lines(mermaid);

        Assert.Contains("n1[\"System.Console.WriteLine()\"]:::external", lines);
        Assert.Contains(lines, line => line.StartsWith("classDef external "));
    }

    [Theory]
    [InlineData(CallTreeStatus.DepthLimited)]
    [InlineData(CallTreeStatus.Truncated)]
    public void StylesBoundaryNodesAsTruncated(CallTreeStatus status)
    {
        var callees = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Deep", "Work"), status)]);

        var mermaid = CallGraphMermaid.RenderCallees(callees);
        var lines = Lines(mermaid);

        Assert.Contains("n1[\"Deep.Work()\"]:::truncated", lines);
        Assert.Contains(lines, line => line.StartsWith("classDef truncated "));
    }

    [Fact]
    public void ExpandedOccurrenceWinsOverBoundaryForSameMember()
    {
        var target = Member("Deep", "Work");
        var callees = Node(
            Member("Root", "M"),
            CallTreeStatus.Expanded,
            [
                // Expanded in one place ...
                Node(target, CallTreeStatus.Expanded, [Leaf(Member("Leaf", "L"))]),
                // ... depth-limited in another. The expanded view should win (no boundary class).
                Leaf(target, CallTreeStatus.DepthLimited),
            ]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        Assert.Contains("n1[\"Deep.Work()\"]", lines);
        Assert.DoesNotContain("n1[\"Deep.Work()\"]:::truncated", lines);
    }

    [Fact]
    public void AnnotatesOutboundLoopCallEdge()
    {
        var callees = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Cache", "Get"), inLoop: true, loopHint: "loop")]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        Assert.Contains("n0 -->|loop| n1", lines);
    }

    [Fact]
    public void AnnotatesInboundLoopCallEdge()
    {
        var callers = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Pump", "Tick"), inLoop: true, loopHint: "loop call")]);

        var lines = Lines(CallGraphMermaid.RenderCallers(callers));

        Assert.Contains("n1 -->|loop call| n0", lines);
    }

    [Fact]
    public void AssignsDeterministicIdsTargetFirstThenCallersThenCallees()
    {
        var callers = Node(
            Member("Widget", "Build"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Program", "Main"))]);
        var callees = Node(
            Member("Widget", "Build"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Store", "Save"))]);

        var first = CallGraphMermaid.Render(callers, callees);
        var second = CallGraphMermaid.Render(callers, callees);

        // Stable across runs.
        Assert.Equal(first, second);

        var lines = Lines(first);
        Assert.Contains("n0[\"Widget.Build()\"]:::target", lines);
        Assert.Contains("n1[\"Program.Main()\"]", lines);
        Assert.Contains("n2[\"Store.Save()\"]", lines);
    }

    [Fact]
    public void RejectsDifferentSelectedMembers()
    {
        var callers = Leaf(Member("Target", "Run"), CallTreeStatus.Expanded);
        var callees = Leaf(Member("Other", "Run"), CallTreeStatus.Expanded);

        Assert.Throws<ArgumentException>(() => CallGraphMermaid.Render(callers, callees));
    }

    [Fact]
    public void RejectsEmptyInput()
        => Assert.Throws<ArgumentException>(() => CallGraphMermaid.Render(null, null));

    [Fact]
    public void RendersExactCombinedCallerTargetCalleeDocument()
    {
        var target = Member("Widget", "Build");

        // callers: Program.Main -> Api.Handle -> Widget.Build, plus a looped caller.
        var callers = Node(
            target,
            CallTreeStatus.Expanded,
            [
                Node(Member("Api", "Handle"), CallTreeStatus.Expanded, [Leaf(Member("Program", "Main"))]),
                Leaf(Member("Loop", "Tick"), inLoop: true, loopHint: "loop call"),
            ]);

        // callees: Widget.Build -> Store.Save, and an external Log.Write.
        var callees = Node(
            target,
            CallTreeStatus.Expanded,
            [
                Leaf(Member("Store", "Save")),
                Leaf(Member("Log", "Write"), CallTreeStatus.External),
            ]);

        var expected = string.Join('\n',
        [
            "flowchart LR",
            "    n0[\"Widget.Build()\"]:::target",
            "    n1[\"Api.Handle()\"]",
            "    n2[\"Program.Main()\"]",
            "    n3[\"Loop.Tick()\"]",
            "    n4[\"Store.Save()\"]",
            "    n5[\"Log.Write()\"]:::external",
            "    n1 --> n0",
            "    n2 --> n1",
            "    n3 -->|loop call| n0",
            "    n0 --> n4",
            "    n0 --> n5",
            "    classDef target fill:#dae8fc,stroke:#6c8ebf,stroke-width:2px;",
            "    classDef external fill:#f5f5f5,stroke:#999999,stroke-dasharray:4 3,color:#666666;",
            "",
        ]);

        var actual = CallGraphMermaid.Render(callers, callees).Replace("\r\n", "\n");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KeepsSameNameMembersFromDifferentAssembliesDistinct()
    {
        // Two callees whose declaring type has the same namespace + name but a different
        // assembly must not collapse: the display spelling drops the assembly, but they are
        // genuinely different members (#1741-class hazard).
        var fromA = new MemberRef(TypeRef.Definition("AsmA", "Shared", "Widget"), "Work", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method);
        var fromB = new MemberRef(TypeRef.Definition("AsmB", "Shared", "Widget"), "Work", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method);

        var callees = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(fromA), Leaf(fromB)]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        Assert.Contains("n1[\"Widget.Work()\"]", lines);
        Assert.Contains("n2[\"Widget.Work()\"]", lines);
        Assert.Contains("n0 --> n1", lines);
        Assert.Contains("n0 --> n2", lines);
    }

    [Fact]
    public void CollapsesGenericTargetSelfRecursionOntoOneNode()
    {
        // A generic target that calls itself: the Analysis builders build the root as an
        // open definition (its return type is the open T) while the recursive callee edge
        // is a constructed MethodSpec that still carries the open signature (OpenReturnType)
        // for cross-assembly keying. Both must erase to one identity so recursion renders as
        // a self-loop rather than splitting into two same-named nodes.
        var openReturn = TypeRef.MethodGenericParameter(0, "T");
        var rootMember = new MemberRef(Type("Calc"), "Recurse", [], openReturn, MemberKind.Method) { GenericArity = 1 };
        var recursiveCall = new MemberRef(Type("Calc"), "Recurse", [], TypeRef.CoreLib("System", "Int32"), MemberKind.Method)
        {
            GenericArity = 1,
            TypeArguments = [TypeRef.CoreLib("System", "Int32")],
            OpenReturnType = openReturn,
        };

        var callees = Node(
            rootMember,
            CallTreeStatus.Expanded,
            [Leaf(recursiveCall, CallTreeStatus.AlreadyShown)]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        Assert.Single(lines, line => line.Contains("\"Calc.Recurse"));
        Assert.Contains("n0 --> n0", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("n1["));
    }

    [Fact]
    public void KeepsReturnTypeOnlyOverloadsDistinct()
    {
        // Conversion operators can share declaring type, name, and parameters yet differ
        // only by return type; they must stay distinct nodes.
        var toInt = new MemberRef(Type("Conv"), "op_Implicit", [Type("Src")], TypeRef.CoreLib("System", "Int32"), MemberKind.Method);
        var toString = new MemberRef(Type("Conv"), "op_Implicit", [Type("Src")], TypeRef.CoreLib("System", "String"), MemberKind.Method);

        var callees = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(toInt), Leaf(toString)]);

        var lines = Lines(CallGraphMermaid.RenderCallees(callees));

        Assert.Contains("n1[\"Conv.op_Implicit(Src)\"]", lines);
        Assert.Contains("n2[\"Conv.op_Implicit(Src)\"]", lines);
        Assert.Contains("n0 --> n1", lines);
        Assert.Contains("n0 --> n2", lines);
    }

    [Fact]
    public void CombinesResolvedCallersWithUnsupportedCalleeRoot()
    {
        // Bodiless target: BuildCallerTree recovers the real member, BuildCallTree yields an
        // Unsupported placeholder root. The combined view must render, center on the resolved
        // member, and not sprout a stray placeholder node.
        var resolved = Member("Widget", "Build");
        var callers = Node(resolved, CallTreeStatus.Expanded, [Leaf(Member("Program", "Main"))]);
        var calleeRoot = Leaf(MemberRef.Unsupported("method token 0x06000001"));

        var lines = Lines(CallGraphMermaid.Render(callers, calleeRoot));

        Assert.Contains("n0[\"Widget.Build()\"]:::target", lines);
        Assert.Contains("n1[\"Program.Main()\"]", lines);
        Assert.Contains("n1 --> n0", lines);
        // No third node: the Unsupported placeholder collapses onto the resolved target.
        Assert.DoesNotContain(lines, line => line.StartsWith("n2["));
    }

    [Fact]
    public void RejectsDifferentUnsupportedRoots()
    {
        // Two placeholder roots naming different tokens are genuinely different members and
        // must still be rejected: the wildcard applies only to a placeholder paired with a
        // resolved member.
        var callers = Leaf(MemberRef.Unsupported("method token 0x06000001"));
        var callees = Leaf(MemberRef.Unsupported("method token 0x06000002"));

        Assert.Throws<ArgumentException>(() => CallGraphMermaid.Render(callers, callees));
    }

    [Fact]
    public void EncodesStructuralCharactersInEdgeLabels()
    {
        // A host-supplied loop hint carrying an unbalanced ')' would break an unquoted
        // Mermaid edge label; it must be entity-encoded so the flowchart grammar stays valid.
        var callees = Node(
            Member("Target", "Run"),
            CallTreeStatus.Expanded,
            [Leaf(Member("Svc", "Do"), inLoop: true, loopHint: "loop) x")]);

        var mermaid = CallGraphMermaid.RenderCallees(callees);
        var lines = Lines(mermaid);

        Assert.Contains("n0 -->|loop#41; x| n1", lines);
        // The raw structural character never reaches the edge label.
        Assert.DoesNotContain("loop) x", mermaid);
    }
}
