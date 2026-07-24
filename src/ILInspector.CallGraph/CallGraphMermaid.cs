using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ILInspector.Analysis;

namespace ILInspector.CallGraph;

/// <summary>
/// Projects the typed call-graph facts that <c>ILInspector.Analysis</c> produces
/// (<see cref="CallTreeNode"/> caller and callee roots built by
/// <c>LibraryBodyIndex.BuildCallerTree</c> / <c>BuildCallTree</c>) into a single
/// deterministic Mermaid <c>flowchart</c> centered on one selected overload:
/// <code>
/// callers -&gt; selected overload -&gt; callees
/// </code>
/// This is a host-neutral product layer that sits <em>below</em> host applications
/// so <c>dotnet-inspect</c> and the browser-Wasm prototype share one graph
/// semantics and one Mermaid document. It owns the concerns a host must not
/// re-invent: stable node identity, Mermaid escaping, duplicate/shared-node and
/// cycle collapsing, depth-limited / truncated boundary marking, external-node
/// styling, and loop-call edge annotations. It takes no dependency on Markout, the
/// CLI, or inspected-assembly loading and stays SRM-only / NativeAOT / browser-Wasm
/// friendly (see issue #3120).
/// </summary>
public static class CallGraphMermaid
{
    public sealed record Options(
        bool CompactLabels = false,
        bool RelationshipColors = false);

    /// <summary>
    /// Renders the combined caller/target/callee view. Both roots are the selected
    /// overload: <paramref name="callerRoot"/>'s children are its inbound callers
    /// (edges flow <em>into</em> the target) and <paramref name="calleeRoot"/>'s
    /// children are its outbound callees (edges flow <em>out of</em> the target).
    /// Either root may be null (e.g. the browser's first caller-only view), but not
    /// both. When both are supplied they must name the same selected member.
    /// </summary>
    public static string Render(
        CallTreeNode? callerRoot,
        CallTreeNode? calleeRoot,
        Options? options = null)
    {
        if (callerRoot is null && calleeRoot is null)
            throw new ArgumentException($"At least one of {nameof(callerRoot)} or {nameof(calleeRoot)} must be provided.");

        // Both roots are the selected overload, but the Analysis builders can resolve a
        // bodiless target (abstract / interface / extern) differently: BuildCallerTree
        // recovers the real member from an inbound call operand, while BuildCallTree has
        // no body to resolve and yields an Unsupported placeholder. Treat an Unsupported
        // placeholder as "unknown identity" so it never contradicts a resolved member, and
        // prefer the resolved member as the single centered target node.
        bool callerResolved = callerRoot is { Member.Kind: not MemberKind.Unsupported };
        bool calleeResolved = calleeRoot is { Member.Kind: not MemberKind.Unsupported };
        // Compare identities whenever both sides carry one: two resolved roots must name
        // the same member, and two Unsupported placeholders must at least name the same
        // token. Only a resolved / placeholder pair may differ — the placeholder is
        // unknown identity, not a contradiction (a bodiless target the builders resolve
        // asymmetrically).
        if (callerRoot is not null && calleeRoot is not null
            && callerResolved == calleeResolved
            && IdentityKey(callerRoot.Member) != IdentityKey(calleeRoot.Member))
            throw new ArgumentException($"{nameof(callerRoot)} and {nameof(calleeRoot)} must describe the same selected member.");

        var target = calleeResolved ? calleeRoot!.Member
            : callerResolved ? callerRoot!.Member
            : (calleeRoot ?? callerRoot)!.Member;

        var builder = new GraphBuilder(options ?? new Options(), target);
        // The selected overload is the single centered node shared by both trees; each
        // tree's root *is* that target, so map both roots to the centered id. This keeps a
        // bodiless placeholder root from becoming a second, stray "?" node.
        int targetId = builder.RegisterTarget(target);
        if (callerRoot is not null)
            builder.WalkCallers(callerRoot, targetId);
        if (calleeRoot is not null)
            builder.WalkCallees(calleeRoot, targetId);
        return builder.Render();
    }

    /// <summary>Renders the inbound (caller) half only, centered on the selected overload.</summary>
    public static string RenderCallers(CallTreeNode callerRoot)
    {
        ArgumentNullException.ThrowIfNull(callerRoot);
        return Render(callerRoot, null);
    }

    /// <summary>Renders the outbound (callee) half only, centered on the selected overload.</summary>
    public static string RenderCallees(CallTreeNode calleeRoot)
    {
        ArgumentNullException.ThrowIfNull(calleeRoot);
        return Render(null, calleeRoot);
    }

    /// <summary>
    /// How a graph node is styled. Higher values win when a member is seen more than
    /// once: a member expanded somewhere (<see cref="Normal"/>) is not a boundary even
    /// if depth-limited elsewhere, and the selected <see cref="Target"/> is sticky.
    /// </summary>
    enum NodeClass
    {
        Truncated = 0,
        External = 1,
        Normal = 2,
        Target = 3,
    }

    sealed class NodeInfo(int id, string label, NodeClass nodeClass, string relationshipClass)
    {
        public int Id { get; } = id;
        public string Label { get; } = label;
        public NodeClass Class { get; set; } = nodeClass;
        public string RelationshipClass { get; } = relationshipClass;
    }

    readonly record struct Edge(int From, int To, string? LoopLabel);

    sealed class GraphBuilder
    {
        readonly Options _options;
        readonly MemberRef _target;
        readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        readonly List<NodeInfo> _nodes = [];
        readonly Dictionary<(int From, int To), int> _edgeIndex = [];
        readonly List<Edge> _edges = [];

        public GraphBuilder(Options options, MemberRef target)
        {
            _options = options;
            _target = target;
        }

        public int RegisterTarget(MemberRef member) => GetOrAdd(member, NodeClass.Target);

        /// <summary>Walk a reverse (caller) tree: each child calls its parent, so edges point child → parent.</summary>
        public void WalkCallers(CallTreeNode node, int nodeId)
        {
            foreach (var child in node.Children)
            {
                int childId = GetOrAdd(child.Member, ClassFor(child.Status));
                AddEdge(childId, nodeId, LoopLabel(child.Perf));
                WalkCallers(child, childId);
            }
        }

        /// <summary>Walk an outbound (callee) tree: each parent calls its children, so edges point parent → child.</summary>
        public void WalkCallees(CallTreeNode node, int nodeId)
        {
            foreach (var child in node.Children)
            {
                int childId = GetOrAdd(child.Member, ClassFor(child.Status));
                AddEdge(nodeId, childId, LoopLabel(child.Perf));
                WalkCallees(child, childId);
            }
        }

        int GetOrAdd(MemberRef member, NodeClass candidate)
        {
            var key = IdentityKey(member);
            if (!_ids.TryGetValue(key, out var id))
            {
                id = _nodes.Count;
                _ids[key] = id;
                _nodes.Add(new NodeInfo(id, Label(member), candidate, RelationshipClass(member)));
                return id;
            }

            // A member seen more than once keeps its strongest classification: the
            // selected target is sticky, an expanded/leaf occurrence outranks a boundary,
            // so a shared node is not mislabelled a dead end.
            var info = _nodes[id];
            if (candidate > info.Class)
                info.Class = candidate;
            return id;
        }

        void AddEdge(int from, int to, string? loopLabel)
        {
            if (_edgeIndex.TryGetValue((from, to), out var index))
            {
                // A shared edge that is a loop call from any site keeps its loop annotation.
                if (loopLabel is not null && _edges[index].LoopLabel is null)
                    _edges[index] = _edges[index] with { LoopLabel = loopLabel };
                return;
            }

            _edgeIndex[(from, to)] = _edges.Count;
            _edges.Add(new Edge(from, to, loopLabel));
        }

        public string Render()
        {
            var sb = new StringBuilder();
            sb.Append("flowchart LR\n");

            // Nodes in stable id order (target first, then caller DFS, then callee DFS).
            foreach (var node in _nodes)
            {
                sb.Append("    ").Append(NodeId(node.Id)).Append("[\"").Append(Escape(node.Label)).Append("\"]");
                if (!_options.RelationshipColors && ClassName(node.Class) is { } className)
                    sb.Append(":::").Append(className);
                sb.Append('\n');
            }

            // Edges in stable first-seen order.
            foreach (var edge in _edges)
            {
                sb.Append("    ").Append(NodeId(edge.From));
                if (edge.LoopLabel is { } loop)
                    sb.Append(" -->|").Append(Escape(loop, edgeLabel: true)).Append("| ");
                else
                    sb.Append(" --> ");
                sb.Append(NodeId(edge.To)).Append('\n');
            }

            if (_options.RelationshipColors)
            {
                foreach (var node in _nodes)
                    sb.Append("    class ").Append(NodeId(node.Id)).Append(' ').Append(node.RelationshipClass).Append(";\n");
                // Keep the semantic palette in CSS variables so the host can switch
                // between light and dark presentation without regenerating the graph.
                sb.Append("    classDef target fill:var(--graph-target-fill),stroke:var(--graph-target-stroke),stroke-width:3px,color:var(--graph-target-text);\n");
                sb.Append("    classDef sameType fill:var(--graph-same-type-fill),stroke:var(--graph-same-type-stroke),stroke-width:2px,color:var(--graph-same-type-text);\n");
                sb.Append("    classDef differentType fill:var(--graph-different-type-fill),stroke:var(--graph-different-type-stroke),stroke-width:2px,color:var(--graph-different-type-text);\n");
                sb.Append("    classDef differentAssembly fill:var(--graph-different-assembly-fill),stroke:var(--graph-different-assembly-stroke),stroke-width:2px,color:var(--graph-different-assembly-text);\n");
                return sb.ToString();
            }

            // Emit a classDef only when the class is used, in a fixed order.
            if (_nodes.Exists(n => n.Class == NodeClass.Target))
                sb.Append("    classDef target fill:#dae8fc,stroke:#6c8ebf,stroke-width:2px;\n");
            if (_nodes.Exists(n => n.Class == NodeClass.External))
                sb.Append("    classDef external fill:#f5f5f5,stroke:#999999,stroke-dasharray:4 3,color:#666666;\n");
            if (_nodes.Exists(n => n.Class == NodeClass.Truncated))
                sb.Append("    classDef truncated fill:#fff2cc,stroke:#d6b656,stroke-dasharray:2 2;\n");

            return sb.ToString();
        }

        /// <summary>Compact, host-neutral member spelling used as the Mermaid node label.</summary>
        string Label(MemberRef member)
        {
            if (member.Kind == MemberKind.Unsupported)
                return member.DeclaringType.ToDisplayString();

            if (_options.CompactLabels)
                return $"{member.DeclaringType.Name}.{member.Name}";

            var name = member.Name;
            if (!member.TypeArguments.IsDefaultOrEmpty)
                name += "<" + string.Join(", ", member.TypeArguments.Select(t => t.ToDisplayString())) + ">";
            var parameters = string.Join(", ", member.ParameterTypes.Select(p => p.ToDisplayString()));
            return $"{member.DeclaringType.ToDisplayString()}.{name}({parameters})";
        }

        string RelationshipClass(MemberRef member)
        {
            if (!_options.RelationshipColors)
                return "normal";
            if (IdentityKey(member) == IdentityKey(_target))
                return "target";

            var targetType = GenericMemberIdentity.KeyFragment(
                GenericMemberIdentity.OpenDeclaringType(_target.DeclaringType));
            var memberType = GenericMemberIdentity.KeyFragment(
                GenericMemberIdentity.OpenDeclaringType(member.DeclaringType));
            if (string.Equals(targetType, memberType, StringComparison.Ordinal))
                return "sameType";
            if (string.Equals(member.DeclaringType.Assembly, _target.DeclaringType.Assembly, StringComparison.Ordinal))
                return "differentType";
            return "differentAssembly";
        }
    }

    /// <summary>
    /// A stable structural identity for a member so shared callees, cycles, the
    /// target-as-caller-and-callee, and self-recursion all collapse to one node. This
    /// mirrors the Analysis layer's cross-assembly caller-graph identity
    /// (<see cref="GenericMemberIdentity"/>): the open-definition side (the target root
    /// and caller nodes, which <c>BuildCallerTree</c> builds without method type
    /// arguments) and the constructed-call-site side (callee edges decoded from IL) must
    /// erase to the <em>same</em> key, so a generic target that calls itself collapses
    /// onto <c>n0</c> instead of splitting. Non-generic members keep their exact
    /// instantiated signature — including the return type, which alone separates C#
    /// conversion operators — while same-name / same-arity generic overloads coarsen, the
    /// accepted trade the rest of the product already makes. The declaring type is
    /// assembly-qualified, so same-namespace / same-name types from different assemblies
    /// stay distinct (#1741).
    /// </summary>
    static string IdentityKey(MemberRef member)
    {
        // Mirror LibraryBodyIndex.CallerGraphKey exactly so the projection and the builder
        // compute byte-identical keys for the same MemberRef.
        var eraseGenericSignature = GenericMemberIdentity.ShouldErase(member.DeclaringType, member.ParameterTypes, member.ReturnType, member.TypeArguments);
        var openDeclaring = GenericMemberIdentity.OpenDeclaringType(member.DeclaringType);
        var shape = eraseGenericSignature
            ? GenericMemberIdentity.ErasedParameterShape(member.OpenSignatureParameters)
            : string.Join(",", member.ParameterTypes.Select(GenericMemberIdentity.KeyFragment));
        return $"{GenericMemberIdentity.KeyFragment(openDeclaring)}|{member.Name}|{member.ParameterTypes.Length}|{shape}|{GenericMemberIdentity.KeyFragment(member.OpenSignatureReturn)}";
    }

    static NodeClass ClassFor(CallTreeStatus status) => status switch
    {
        CallTreeStatus.External => NodeClass.External,
        CallTreeStatus.DepthLimited or CallTreeStatus.Truncated => NodeClass.Truncated,
        _ => NodeClass.Normal,
    };

    static string? ClassName(NodeClass nodeClass) => nodeClass switch
    {
        NodeClass.Target => "target",
        NodeClass.External => "external",
        NodeClass.Truncated => "truncated",
        _ => null,
    };

    // The loop flag lives on the deeper (child) node and describes the parent↔child
    // call edge: for a callee tree the parent calls the child in a loop; for a caller
    // tree the child (caller) calls the parent in a loop.
    static string? LoopLabel(CallTreePerf? perf)
        => perf is { InLoop: true } p
            ? string.IsNullOrEmpty(p.LoopHint) ? "loop" : p.LoopHint
            : null;

    static string NodeId(int id) => "n" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes text for a Mermaid quoted label / edge label using Mermaid entity codes,
    /// so hostile or unusual member names (quotes, angle brackets, pipes, <c>#</c>)
    /// cannot break out of the label or the flowchart grammar. Node labels are wrapped in
    /// <c>["..."]</c> so brackets/parentheses are already safe there; edge labels
    /// (<c>|...|</c>) are unquoted, so <paramref name="edgeLabel"/> additionally entity-
    /// encodes the structural delimiters that would otherwise corrupt an edge label.
    /// </summary>
    static string Escape(string text, bool edgeLabel = false)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '#': sb.Append("#35;"); break;
                case '"': sb.Append("#quot;"); break;
                case '<': sb.Append("#60;"); break;
                case '>': sb.Append("#62;"); break;
                case '&': sb.Append("#38;"); break;
                case '|': sb.Append("#124;"); break;
                case '\r': sb.Append("#13;"); break;
                case '\n': sb.Append("#10;"); break;
                case '(' when edgeLabel: sb.Append("#40;"); break;
                case ')' when edgeLabel: sb.Append("#41;"); break;
                case '[' when edgeLabel: sb.Append("#91;"); break;
                case ']' when edgeLabel: sb.Append("#93;"); break;
                case '{' when edgeLabel: sb.Append("#123;"); break;
                case '}' when edgeLabel: sb.Append("#125;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
