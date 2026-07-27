using System.Collections.Immutable;
using ILInspector.Analysis;

namespace ILInspector.CallGraph;

/// <summary>
/// How a projected node relates to the rest of the graph. Higher values win when the
/// same member is reached more than once: a member expanded somewhere
/// (<see cref="Normal"/>) is not a boundary even if depth-limited elsewhere, and the
/// selected <see cref="Focus"/> member is sticky.
/// </summary>
public enum CallGraphNodeKind
{
    /// <summary>Reached only where traversal stopped (depth-limited or truncated).</summary>
    Truncated = 0,

    /// <summary>Reached only outside the analyzed assembly set.</summary>
    External = 1,

    /// <summary>Reached as an ordinary expanded or leaf node.</summary>
    Normal = 2,

    /// <summary>The selected member the graph is centered on.</summary>
    Focus = 3,
}

/// <summary>
/// One node of a <see cref="CallGraphProjection"/>.
/// </summary>
/// <param name="Id">
/// Dense zero-based index into <see cref="CallGraphProjection.Nodes"/>. The focus node is
/// always id 0.
/// </param>
/// <param name="Member">
/// The typed member identity. This — not <paramref name="Label"/> — is the node's identity;
/// hosts must not infer identity from display text.
/// </param>
/// <param name="Label">
/// A host-neutral default spelling of <paramref name="Member"/>, offered as a convenience.
/// A host that owns its own type/member spelling should render <paramref name="Member"/>
/// itself instead.
/// </param>
/// <param name="Kind">The strongest classification observed across every occurrence.</param>
/// <param name="Perf">
/// The analysis cues (fanout, fanin, depth, loop, signals, caller scope) carried by the first
/// occurrence that had them, or null when no occurrence did. These are facts about the member, not
/// presentation: a host projects whichever it was asked for and ignores the rest.
/// </param>
public sealed record CallGraphNode(int Id, MemberRef Member, string Label, CallGraphNodeKind Kind, CallTreePerf? Perf = null);

/// <summary>
/// One directed call edge. The direction is always "caller calls callee", so an inbound
/// (reverse) tree is inverted during projection rather than left for the host to interpret.
/// </summary>
/// <param name="From">Id of the calling member.</param>
/// <param name="To">Id of the called member.</param>
/// <param name="LoopLabel">
/// Non-null when the call occurs inside a loop at any collapsed call site: either the
/// analysis loop hint or <c>"loop"</c>.
/// </param>
public readonly record struct CallGraphEdge(int From, int To, string? LoopLabel);

/// <summary>
/// A format-neutral projection of the typed call-graph facts that
/// <c>ILInspector.Analysis</c> produces (<see cref="CallTreeNode"/> caller and callee roots
/// built by <c>LibraryBodyIndex.BuildCallerTree</c> / <c>BuildCallTree</c>) into a single
/// deterministic directed graph centered on one selected overload:
/// <code>
/// callers -&gt; selected overload -&gt; callees
/// </code>
/// <para>
/// This is the host-neutral product layer that sits <em>below</em> host applications, so
/// every consumer shares one graph semantics regardless of output format. It owns the
/// concerns a host must not re-invent: stable generic-erased node identity, duplicate /
/// shared-node and cycle collapsing, inbound edge inversion, depth-limited and external
/// boundary classification, loop-call edge annotation, and deterministic node and edge
/// ordering.
/// </para>
/// <para>
/// It knows nothing about any output format. Rendering — Mermaid, a table, a tree, or
/// anything else — belongs to the host. It takes no dependency on Markout, the CLI, or
/// inspected-assembly loading and stays SRM-only / NativeAOT / browser-Wasm friendly
/// (see issue #3120).
/// </para>
/// <para>
/// Ordering is part of the contract, not an implementation detail: nodes appear focus
/// first, then caller-side discovery order, then callee-side discovery order, and edges
/// appear in first-seen order.
/// </para>
/// </summary>
public sealed class CallGraphProjection
{
    private CallGraphProjection(ImmutableArray<CallGraphNode> nodes, ImmutableArray<CallGraphEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
    }

    /// <summary>Nodes in deterministic order. The focus node is always first.</summary>
    public ImmutableArray<CallGraphNode> Nodes { get; }

    /// <summary>Edges in deterministic first-seen order, always oriented caller → callee.</summary>
    public ImmutableArray<CallGraphEdge> Edges { get; }

    /// <summary>The selected overload the graph is centered on.</summary>
    public CallGraphNode Focus => Nodes[0];

    /// <summary>
    /// Projects the combined caller/target/callee view. Both roots are the selected
    /// overload: <paramref name="callerRoot"/>'s children are its inbound callers and
    /// <paramref name="calleeRoot"/>'s children are its outbound callees. Either root may
    /// be null (e.g. a caller-only view), but not both. When both are supplied they must
    /// name the same selected member.
    /// </summary>
    public static CallGraphProjection Create(CallTreeNode? callerRoot, CallTreeNode? calleeRoot)
    {
        if (callerRoot is null && calleeRoot is null)
            throw new ArgumentException($"At least one of {nameof(callerRoot)} or {nameof(calleeRoot)} must be provided.");

        // Both roots are the selected overload, but the Analysis builders can resolve a
        // bodiless target (abstract / interface / extern) differently: BuildCallerTree
        // recovers the real member from an inbound call operand, while BuildCallTree has
        // no body to resolve and yields an Unsupported placeholder. Treat an Unsupported
        // placeholder as "unknown identity" so it never contradicts a resolved member, and
        // prefer the resolved member as the single centered focus node.
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

        var focus = calleeResolved ? calleeRoot!.Member
            : callerResolved ? callerRoot!.Member
            : (calleeRoot ?? callerRoot)!.Member;

        var builder = new Builder();
        // The selected overload is the single centered node shared by both trees; each
        // tree's root *is* that focus, so map both roots to the same id. This keeps a
        // bodiless placeholder root from becoming a second, stray "?" node.
        // The callee root carries the selected member's own cues when it has a body; the
        // caller root is the fallback for a bodiless target.
        int focusId = builder.RegisterFocus(focus, calleeRoot?.Perf ?? callerRoot?.Perf);
        if (callerRoot is not null)
            builder.WalkCallers(callerRoot, focusId);
        if (calleeRoot is not null)
            builder.WalkCallees(calleeRoot, focusId);
        return builder.Build();
    }

    /// <summary>Projects the inbound (caller) half only, centered on the selected overload.</summary>
    public static CallGraphProjection FromCallers(CallTreeNode callerRoot)
    {
        ArgumentNullException.ThrowIfNull(callerRoot);
        return Create(callerRoot, null);
    }

    /// <summary>Projects the outbound (callee) half only, centered on the selected overload.</summary>
    public static CallGraphProjection FromCallees(CallTreeNode calleeRoot)
    {
        ArgumentNullException.ThrowIfNull(calleeRoot);
        return Create(null, calleeRoot);
    }

    private sealed class MutableNode(int id, MemberRef member, string label, CallGraphNodeKind kind, CallTreePerf? perf)
    {
        public int Id { get; } = id;
        public MemberRef Member { get; } = member;
        public string Label { get; } = label;
        public CallGraphNodeKind Kind { get; set; } = kind;
        public CallTreePerf? Perf { get; set; } = perf;
    }

    private sealed class Builder
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        private readonly List<MutableNode> _nodes = [];
        private readonly Dictionary<(int From, int To), int> _edgeIndex = [];
        private readonly List<CallGraphEdge> _edges = [];

        public int RegisterFocus(MemberRef member, CallTreePerf? perf) => GetOrAdd(member, CallGraphNodeKind.Focus, perf);

        /// <summary>Walk a reverse (caller) tree: each child calls its parent, so edges point child → parent.</summary>
        public void WalkCallers(CallTreeNode node, int nodeId)
        {
            foreach (var child in node.Children)
            {
                int childId = GetOrAdd(child.Member, KindFor(child.Status), child.Perf);
                AddEdge(childId, nodeId, LoopLabel(child.Perf));
                WalkCallers(child, childId);
            }
        }

        /// <summary>Walk an outbound (callee) tree: each parent calls its children, so edges point parent → child.</summary>
        public void WalkCallees(CallTreeNode node, int nodeId)
        {
            foreach (var child in node.Children)
            {
                int childId = GetOrAdd(child.Member, KindFor(child.Status), child.Perf);
                AddEdge(nodeId, childId, LoopLabel(child.Perf));
                WalkCallees(child, childId);
            }
        }

        public CallGraphProjection Build()
        {
            var nodes = ImmutableArray.CreateBuilder<CallGraphNode>(_nodes.Count);
            foreach (var node in _nodes)
                nodes.Add(new CallGraphNode(node.Id, node.Member, node.Label, node.Kind, node.Perf));
            return new CallGraphProjection(nodes.MoveToImmutable(), [.. _edges]);
        }

        private int GetOrAdd(MemberRef member, CallGraphNodeKind candidate, CallTreePerf? perf)
        {
            var key = IdentityKey(member);
            if (!_ids.TryGetValue(key, out var id))
            {
                id = _nodes.Count;
                _ids[key] = id;
                _nodes.Add(new MutableNode(id, member, Label(member), candidate, perf));
                return id;
            }

            // A member seen more than once keeps its strongest classification: the
            // selected focus is sticky, an expanded/leaf occurrence outranks a boundary,
            // so a shared node is not mislabelled a dead end.
            var info = _nodes[id];
            if (candidate > info.Kind)
                info.Kind = candidate;
            // Boundary occurrences carry no cues, so the first occurrence that has them wins
            // rather than a later bare one erasing them.
            info.Perf ??= perf;
            return id;
        }

        private void AddEdge(int from, int to, string? loopLabel)
        {
            if (_edgeIndex.TryGetValue((from, to), out var index))
            {
                // A shared edge that is a loop call from any site keeps its loop annotation.
                if (loopLabel is not null && _edges[index].LoopLabel is null)
                    _edges[index] = _edges[index] with { LoopLabel = loopLabel };
                return;
            }

            _edgeIndex[(from, to)] = _edges.Count;
            _edges.Add(new CallGraphEdge(from, to, loopLabel));
        }
    }

    /// <summary>
    /// A stable structural identity for a member so shared callees, cycles, the
    /// focus-as-caller-and-callee, and self-recursion all collapse to one node. This
    /// mirrors the Analysis layer's cross-assembly caller-graph identity
    /// (<see cref="GenericMemberIdentity"/>): the open-definition side (the focus root and
    /// caller nodes, which <c>BuildCallerTree</c> builds without method type arguments)
    /// and the constructed-call-site side (callee edges decoded from IL) must erase to the
    /// <em>same</em> key, so a generic member that calls itself collapses onto one node
    /// instead of splitting. Non-generic members keep their exact instantiated signature —
    /// including the return type, which alone separates C# conversion operators — while
    /// same-name / same-arity generic overloads coarsen, the accepted trade the rest of
    /// the product already makes. The declaring type is assembly-qualified, so
    /// same-namespace / same-name types from different assemblies stay distinct (#1741).
    /// </summary>
    internal static string IdentityKey(MemberRef member)
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

    /// <summary>Compact, host-neutral member spelling offered as a default node label.</summary>
    internal static string Label(MemberRef member)
    {
        if (member.Kind == MemberKind.Unsupported)
            return member.DeclaringType.ToDisplayString();

        var name = member.Name;
        if (!member.TypeArguments.IsDefaultOrEmpty)
            name += "<" + string.Join(", ", member.TypeArguments.Select(t => t.ToDisplayString())) + ">";
        var parameters = string.Join(", ", member.ParameterTypes.Select(p => p.ToDisplayString()));
        return $"{member.DeclaringType.ToDisplayString()}.{name}({parameters})";
    }

    private static CallGraphNodeKind KindFor(CallTreeStatus status) => status switch
    {
        CallTreeStatus.External => CallGraphNodeKind.External,
        CallTreeStatus.DepthLimited or CallTreeStatus.Truncated => CallGraphNodeKind.Truncated,
        _ => CallGraphNodeKind.Normal,
    };

    // The loop flag lives on the deeper (child) node and describes the parent↔child
    // call edge: for a callee tree the parent calls the child in a loop; for a caller
    // tree the child (caller) calls the parent in a loop.
    private static string? LoopLabel(CallTreePerf? perf)
        => perf is { InLoop: true } p
            ? string.IsNullOrEmpty(p.LoopHint) ? "loop" : p.LoopHint
            : null;
}
