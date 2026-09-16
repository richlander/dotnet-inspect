using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Metadata;

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
/// The typed member payload. Hosts use <paramref name="Identity"/> to join the
/// node and must not infer identity from this value or from
/// <paramref name="Label"/>.
/// </param>
/// <param name="Identity">
/// The Analysis-owned identity that the projection used to collapse physical
/// occurrences onto this logical node.
/// </param>
/// <param name="Label">
/// A host-neutral default spelling of <paramref name="Member"/>, offered as a convenience.
/// A host that owns its own type/member spelling should render <paramref name="Member"/>
/// itself instead.
/// </param>
/// <param name="Kind">The strongest classification observed across every occurrence.</param>
/// <param name="Perf">
/// The analysis cues (fanout, fanin, depth, loop, signals, caller scope) observed for this
/// member, merged across both walk directions, or null when neither observed any. These are
/// facts about the member, not presentation: a host projects whichever it was asked for and
/// ignores the rest.
/// </param>
/// <param name="GraphEvidence">
/// Distinct physical evidence carried by the projected tree occurrences that
/// collapsed onto this logical node.
/// </param>
/// <param name="DefinitionAssemblyIdentity">
/// Exact assembly identity of the unambiguous resolved definition site, when catalog evidence
/// supplied one. <c>CalleeTreeCarriesResolvedDefinitionAssemblyIdentity</c> and
/// <c>ConflictingDefinitionAndResolutionAssembliesAreWithheld</c> gate preservation and
/// ambiguity.
/// </param>
/// <param name="ResolutionAssemblyIdentity">
/// Exact terminal assembly identity observed while resolving the declaring
/// type. An unresolved value is an acquisition hint, not a definition claim.
/// <c>ConflictingDefinitionAndResolutionAssembliesAreWithheld</c> gates
/// conflict withholding.
/// </param>
/// <param name="OccurrenceAssemblyIdentity">
/// Exact assembly scope encoded by one physical call occurrence. Logical
/// projection nodes leave this null; an occurrence target returned by
/// <see cref="CallGraphProjection.FindFocusCalleeTarget"/> retains it without
/// changing terminal-resolution semantics.
/// </param>
public sealed record CallGraphNode(
    int Id,
    GraphNodeIdentity Identity,
    MemberRef Member,
    string Label,
    CallGraphNodeKind Kind,
    CallTreePerf? Perf = null,
    ImmutableArray<GraphNodeEvidence> GraphEvidence = default,
    AssemblyReferenceIdentity? DefinitionAssemblyIdentity = null,
    AssemblyReferenceIdentity? ResolutionAssemblyIdentity = null,
    AssemblyReferenceIdentity? OccurrenceAssemblyIdentity = null);

/// <summary>The traversal half that first contributed one logical edge.</summary>
public enum CallGraphEdgeOrigin
{
    Callers,
    Callees,
}

/// <summary>Descriptive dispatch modality derived from one physical call.</summary>
public enum CallGraphDispatchKind
{
    Direct,
    Virtual,
    FunctionPointer,
    VirtualFunctionPointer,
    Indirect,
}

/// <summary>
/// One directed call edge. The direction is always "caller calls callee", so an inbound
/// (reverse) tree is inverted during projection rather than left for the host to interpret.
/// </summary>
/// <param name="From">Id of the calling member.</param>
/// <param name="To">Id of the called member.</param>
/// <param name="AnyCallInLoop">
/// Whether any retained physical call site supporting this edge occurs in a
/// loop. Evidence-free trees fall back to their typed loop flag.
/// </param>
/// <param name="Origin">
/// The traversal half that first contributed the edge. Hosts may use this to
/// preserve caller-side versus callee-side presentation without storing label
/// text in the projection.
/// </param>
/// <param name="CallSiteIds">
/// Dense ids into <see cref="CallGraphProjection.CallSites"/> for every retained
/// physical call supporting this edge.
/// </param>
/// <param name="HasUnavailablePhysicalOccurrences">
/// Whether one or more physical occurrences supporting this edge could not be
/// retained because independently detached scopes disagreed on their logical
/// endpoint identity.
/// </param>
/// <param name="LegacyLoopHint">
/// The analysis hint retained only for an evidence-free tree edge. Physical
/// call edges derive loop presentation from typed occurrence evidence.
/// </param>
public readonly record struct CallGraphEdge(
    int From,
    int To,
    bool AnyCallInLoop,
    CallGraphEdgeOrigin Origin,
    ImmutableArray<int> CallSiteIds,
    bool HasUnavailablePhysicalOccurrences,
    string? LegacyLoopHint);

/// <summary>
/// Opaque identity for one physical call receipt. Catalog identities retain
/// acquisition registration; synthetic identities retain structural caller
/// identity. Both retain the evidence method's MVID and metadata token because
/// multiple synthesized bodies can share one declared caller and IL offset.
/// </summary>
public sealed class CallGraphCallSiteIdentity
    : IEquatable<CallGraphCallSiteIdentity>
{
    readonly GraphNodeStorageKey? _callerStorage;
    readonly GraphNodeIdentity? _structuralCaller;
    readonly Guid _evidenceModuleVersionId;
    readonly int _evidenceMethodToken;

    internal CallGraphCallSiteIdentity(
        GraphNodeStorageKey? callerStorage,
        GraphNodeIdentity? structuralCaller,
        Guid evidenceModuleVersionId,
        int evidenceMethodToken,
        int ilOffset,
        int operandToken)
    {
        if ((callerStorage is null) == (structuralCaller is null))
        {
            throw new ArgumentException(
                "Exactly one physical caller identity is required.");
        }

        _callerStorage = callerStorage;
        _structuralCaller = structuralCaller;
        _evidenceModuleVersionId = evidenceModuleVersionId;
        _evidenceMethodToken = evidenceMethodToken;
        ILOffset = ilOffset;
        OperandToken = operandToken;
    }

    public int ILOffset { get; }
    public int OperandToken { get; }
    public bool IsPortable =>
        _callerStorage is null
        && _structuralCaller!.IsPortable;

    public bool Equals(CallGraphCallSiteIdentity? other) =>
        other is not null
        && Equals(_callerStorage, other._callerStorage)
        && Equals(_structuralCaller, other._structuralCaller)
        && _evidenceModuleVersionId
            == other._evidenceModuleVersionId
        && _evidenceMethodToken
            == other._evidenceMethodToken
        && ILOffset == other.ILOffset
        && OperandToken == other.OperandToken;

    public override bool Equals(object? obj) =>
        obj is CallGraphCallSiteIdentity other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            _callerStorage,
            _structuralCaller,
            _evidenceModuleVersionId,
            _evidenceMethodToken,
            ILOffset,
            OperandToken);
}

/// <summary>One physical call site retained behind a logical edge.</summary>
public sealed record CallGraphCallSite(
    int Id,
    int EdgeId,
    CallGraphCallSiteIdentity Identity,
    DirectCall Call,
    CallGraphDispatchKind DispatchKind);

/// <summary>
/// One stable row of a <see cref="CallGraphProjection"/>.
/// </summary>
/// <param name="Number">
/// The one-based row number in the projection's deterministic edge order. The number is
/// retained when rows are filtered, so a window never renumbers the surviving rows.
/// </param>
/// <param name="Edge">
/// The call edge denoted by this row. Call graphs answer "what calls what", so edges are
/// their row unit.
/// </param>
public readonly record struct CallGraphRow(int Number, CallGraphEdge Edge);

/// <summary>The outcome of locating one physical focus call in a projection.</summary>
public enum CallGraphRowMatch
{
    /// <summary>The call maps to exactly one projected logical edge.</summary>
    Found,

    /// <summary>The bounded projection contains no edge for the call.</summary>
    NotProjected,

    /// <summary>More than one projected edge claims the call.</summary>
    Ambiguous,
}

/// <summary>The outcome of locating a method definition in a projection.</summary>
public enum CallGraphNodeMatch
{
    /// <summary>The method maps to exactly one projected logical node.</summary>
    Found,

    /// <summary>The bounded projection contains no node for the method.</summary>
    NotProjected,

    /// <summary>More than one projected node can represent the method.</summary>
    Ambiguous,
}

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
/// concerns a host must not re-invent: Analysis-owned node identity, duplicate /
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
public sealed partial class CallGraphProjection
{
    private CallGraphProjection(
        ImmutableArray<CallGraphNode> nodes,
        ImmutableArray<CallGraphEdge> edges,
        ImmutableArray<CallGraphCallSite> callSites,
        bool hasUnexploredTraversalBoundary,
        bool hasAnalysisFailureBoundary)
    {
        Nodes = nodes;
        Edges = edges;
        CallSites = callSites;
        HasUnexploredTraversalBoundary =
            hasUnexploredTraversalBoundary;
        HasAnalysisFailureBoundary =
            hasAnalysisFailureBoundary;

        var rows = ImmutableArray.CreateBuilder<CallGraphRow>(edges.Length);
        for (var i = 0; i < edges.Length; i++)
            rows.Add(new CallGraphRow(i + 1, edges[i]));
        Rows = rows.MoveToImmutable();
    }

    /// <summary>Nodes in deterministic order. The focus node is always first.</summary>
    public ImmutableArray<CallGraphNode> Nodes { get; }

    /// <summary>Edges in deterministic first-seen order, always oriented caller → callee.</summary>
    public ImmutableArray<CallGraphEdge> Edges { get; }

    /// <summary>
    /// Physical call sites in deterministic first-seen order. Repeated
    /// observations from the caller and callee walks appear once.
    /// </summary>
    public ImmutableArray<CallGraphCallSite> CallSites { get; }

    /// <summary>
    /// Rows in deterministic order, one per <see cref="Edges"/> entry. Row numbers are
    /// one-based and stable across filtering.
    /// </summary>
    public ImmutableArray<CallGraphRow> Rows { get; }

    /// <summary>The number of edge rows in this projection.</summary>
    public int RowCount => Rows.Length;

    /// <summary>
    /// Whether the outbound traversal stopped at an unresolved external,
    /// depth, or node boundary.
    /// </summary>
    public bool HasUnexploredTraversalBoundary { get; }

    /// <summary>
    /// Whether the outbound traversal contains a recoverable body-analysis failure.
    /// Positive graph evidence remains valid, but absence is not exhaustive.
    /// </summary>
    public bool HasAnalysisFailureBoundary { get; }

    /// <summary>The selected overload the graph is centered on.</summary>
    public CallGraphNode Focus => Nodes[0];

    /// <summary>
    /// Resolves one physical call site in the selected member to its stable
    /// logical edge row. Exact catalog storage evidence wins; a unique typed
    /// structural match handles assembly-local projections.
    /// </summary>
    public CallGraphRowMatch FindFocusCalleeRow(
        DirectCall call,
        out CallGraphRow row) =>
        FindCalleeRow(Focus.Id, call, out row);

    /// <summary>
    /// Resolves one physical call site in the selected member to its exact typed target.
    /// The projected row supplies logical graph ownership, while the physical occurrence
    /// restores decoder-retained assembly-reference identity that structural node grouping
    /// intentionally omits.
    /// </summary>
    /// <remarks>
    /// The returned occurrence view keeps the projected node's logical
    /// <see cref="CallGraphNode.Id"/> and <see cref="CallGraphNode.Identity"/>;
    /// it is not a replacement entry in <see cref="Nodes"/>.
    /// </remarks>
    public CallGraphRowMatch FindFocusCalleeTarget(
        DirectCall call,
        out CallGraphNode target)
    {
        CallGraphRowMatch match = FindFocusCalleeRow(call, out CallGraphRow row);
        if (match != CallGraphRowMatch.Found)
        {
            target = null!;
            return match;
        }

        CallGraphNode projected =
            Nodes.Single(node => node.Id == row.Edge.To);
        AssemblyReferenceIdentity? occurrenceAssembly =
            DeclaringAssemblyIdentity(call.Callee.DeclaringType);
        AssemblyReferenceIdentity? definitionAssembly =
            projected.DefinitionAssemblyIdentity;
        if (definitionAssembly is not null
            && projected.ResolutionAssemblyIdentity is null
            && occurrenceAssembly is not null
            && !definitionAssembly.IsEquivalentTo(occurrenceAssembly))
        {
            definitionAssembly = null;
        }
        target = projected with
        {
            Member = call.Callee,
            DefinitionAssemblyIdentity = definitionAssembly,
            OccurrenceAssemblyIdentity = occurrenceAssembly,
        };
        return CallGraphRowMatch.Found;
    }

    /// <summary>
    /// Resolves one method definition to its projected logical node. Exact
    /// physical definition evidence wins; structural identity handles
    /// evidence-free projections.
    /// </summary>
    public CallGraphNodeMatch FindNode(
        MethodIdentity method,
        out CallGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(method);

        CallGraphNode[] exact =
        [
            .. Nodes.Where(candidate =>
                candidate.GraphEvidence.Any(evidence =>
                    MatchesDefinition(evidence.Storage, method)
                    || evidence.DefinitionStorage is { } definition
                        && MatchesDefinition(definition, method))),
        ];
        if (exact.Length == 1)
        {
            node = exact[0];
            return CallGraphNodeMatch.Found;
        }
        if (exact.Length > 1)
        {
            node = null!;
            return CallGraphNodeMatch.Ambiguous;
        }

        GraphNodeIdentity identity =
            GraphNodeIdentity.FromMethod(method);
        CallGraphNode[] structural =
        [
            .. Nodes.Where(candidate =>
                candidate.Identity == identity),
        ];
        if (structural.Length == 1)
        {
            node = structural[0];
            return CallGraphNodeMatch.Found;
        }

        node = null!;
        return structural.Length > 1
            ? CallGraphNodeMatch.Ambiguous
            : CallGraphNodeMatch.NotProjected;

        static bool MatchesDefinition(
            GraphNodeStorageKey storage,
            MethodIdentity candidate) =>
            storage.Kind == GraphNodeStorageKind.Definition
            && storage.ModuleVersionId == candidate.ModuleVersionId
            && storage.MethodToken == candidate.MetadataToken;
    }

    /// <summary>
    /// Resolves one physical call site from a projected caller node to its
    /// stable logical edge row.
    /// </summary>
    public CallGraphRowMatch FindCalleeRow(
        int callerNodeId,
        DirectCall call,
        out CallGraphRow row)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentOutOfRangeException.ThrowIfNegative(callerNodeId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            callerNodeId,
            Nodes.Length);

        CallGraphRow[] exact =
        [
            .. Rows.Where(candidate =>
                candidate.Edge.From == callerNodeId
                && candidate.Edge.CallSiteIds.Any(callSiteId =>
                    SamePhysicalCallSite(
                        CallSites[callSiteId].Call,
                        call))),
        ];
        if (exact.Length == 1)
        {
            row = exact[0];
            return CallGraphRowMatch.Found;
        }
        if (exact.Length > 1)
        {
            row = default;
            return CallGraphRowMatch.Ambiguous;
        }

        GraphNodeIdentity callee =
            GraphNodeIdentity.FromMember(call.Callee);
        CallGraphRow[] structural =
        [
            .. Rows.Where(candidate =>
                candidate.Edge.From == callerNodeId
                && GraphNodeIdentity.FromMember(
                    Nodes[candidate.Edge.To].Member) == callee),
        ];
        if (structural.Length == 1)
        {
            row = structural[0];
            return CallGraphRowMatch.Found;
        }

        row = default;
        return structural.Length > 1
            ? CallGraphRowMatch.Ambiguous
            : CallGraphRowMatch.NotProjected;
    }

    static bool SamePhysicalCallSite(
        DirectCall first,
        DirectCall second) =>
        first.EvidenceMethod.AssemblyName
            == second.EvidenceMethod.AssemblyName
        && first.EvidenceMethod.ModuleVersionId
            == second.EvidenceMethod.ModuleVersionId
        && first.EvidenceMethod.MetadataToken
            == second.EvidenceMethod.MetadataToken
        && first.ILOffset == second.ILOffset
        && first.OperandToken == second.OperandToken;

    static AssemblyReferenceIdentity? DeclaringAssemblyIdentity(
        TypeRef type)
    {
        while (type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }
        if (type.Kind != TypeRefKind.Definition)
            return null;

        return type.Resolution?.Origin switch
        {
            TypeReferenceOrigin.AssemblyReference reference =>
                reference.Assembly,
            TypeReferenceOrigin.CurrentAssembly current =>
                current.Assembly,
            _ => null,
        };
    }

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
        bool useGraphEvidence =
            HasCompleteGraphEvidence(callerRoot)
            && HasCompleteGraphEvidence(calleeRoot);
        bool useAcquisitionReceiptIdentity =
            useGraphEvidence
            && HasCompleteCallerDefinitions(callerRoot)
            && HasCompleteCallerDefinitions(calleeRoot);
        if (callerRoot is not null && calleeRoot is not null
            && callerResolved == calleeResolved
            && Identity(callerRoot, useGraphEvidence)
                != Identity(calleeRoot, useGraphEvidence))
            throw new ArgumentException($"{nameof(callerRoot)} and {nameof(calleeRoot)} must describe the same selected member.");

        var focus = calleeResolved ? calleeRoot!.Member
            : callerResolved ? callerRoot!.Member
            : (calleeRoot ?? callerRoot)!.Member;

        var builder = new Builder(
            useGraphEvidence,
            useAcquisitionReceiptIdentity);
        // The selected overload is the single centered node shared by both trees; each
        // tree's root *is* that focus, so map both roots to the same id. This keeps a
        // bodiless placeholder root from becoming a second, stray "?" node.
        // Each tree measured half of the focus: the callee root owns fan-out, the caller
        // root owns fan-in and the root classification. Merge them rather than picking one,
        // or the focus reports a direction it never measured.
        int focusId = builder.RegisterFocus(
            focus,
            MergePerf(calleeRoot?.Perf, callerRoot?.Perf),
            calleeRoot?.GraphEvidence,
            callerRoot?.GraphEvidence,
            calleeRoot?.DefinitionAssemblyIdentity,
            callerRoot?.DefinitionAssemblyIdentity,
            calleeRoot?.ResolutionAssemblyIdentity,
            callerRoot?.ResolutionAssemblyIdentity);
        if (callerRoot is not null)
            builder.WalkCallers(callerRoot, focusId);
        if (calleeRoot is not null)
            builder.WalkCallees(calleeRoot, focusId);
        // A reverse tree is closed only over its indexed caller scope: a Leaf
        // means "no callers here", not "no callers anywhere". Only the body-owned
        // outbound traversal can prove that no focus cycle exists.
        bool hasUnexploredTraversalBoundary =
            !IsTraversalComplete(
                calleeRoot,
                useGraphEvidence)
            || HasUnresolvedDispatch(calleeRoot);
        bool hasAnalysisFailureBoundary =
            HasAnalysisFailure(calleeRoot);
        return builder.Build(
            hasUnexploredTraversalBoundary,
            hasAnalysisFailureBoundary);
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

    private sealed class MutableNode(
        int id,
        GraphNodeIdentity identity,
        MemberRef member,
        string label,
        CallGraphNodeKind kind,
        CallTreePerf? perf)
    {
        public int Id { get; } = id;
        public GraphNodeIdentity Identity { get; } = identity;
        public MemberRef Member { get; } = member;
        public string Label { get; } = label;
        public CallGraphNodeKind Kind { get; set; } = kind;
        public CallTreePerf? Perf { get; set; } = perf;
        public List<GraphNodeEvidence> GraphEvidence { get; } = [];
        public AssemblyReferenceIdentity? DefinitionAssemblyIdentity
            { get; set; }
        public bool HasDefinitionAssemblyConflict { get; set; }
        public AssemblyReferenceIdentity? ResolutionAssemblyIdentity
            { get; set; }
        public bool HasResolutionAssemblyConflict { get; set; }
    }

    private sealed class Builder(
        bool useGraphEvidence,
        bool useAcquisitionReceiptIdentity)
    {
        private sealed class MutableEdge(
            int from,
            int to,
            CallGraphEdgeOrigin origin)
        {
            public int From { get; } = from;
            public int To { get; } = to;
            public CallGraphEdgeOrigin Origin { get; } = origin;
            public bool PhysicalAnyCallInLoop { get; set; }
            public bool FallbackAnyCallInLoop { get; set; }
            public bool HasUnavailablePhysicalOccurrences { get; set; }
            public bool AnyCallInLoop =>
                CallSiteIds.Count > 0
                    ? PhysicalAnyCallInLoop
                        || (HasUnavailablePhysicalOccurrences
                            && FallbackAnyCallInLoop)
                    : FallbackAnyCallInLoop;
            public string? LegacyLoopHint { get; set; }
            public List<int> CallSiteIds { get; } = [];
        }

        private readonly Dictionary<GraphNodeIdentity, int> _ids = [];
        private readonly List<MutableNode> _nodes = [];
        private readonly Dictionary<(int From, int To), int> _edgeIndex = [];
        private readonly List<MutableEdge> _edges = [];
        private readonly Dictionary<
            CallGraphCallSiteIdentity,
            int> _callSiteIds = [];
        private readonly List<CallGraphCallSite> _callSites = [];

        public int RegisterFocus(
            MemberRef member,
            CallTreePerf? perf,
            GraphNodeEvidence? firstEvidence,
            GraphNodeEvidence? secondEvidence,
            AssemblyReferenceIdentity? firstDefinitionAssembly,
            AssemblyReferenceIdentity? secondDefinitionAssembly,
            AssemblyReferenceIdentity? firstResolutionAssembly,
            AssemblyReferenceIdentity? secondResolutionAssembly)
        {
            GraphNodeIdentity identity = useGraphEvidence
                ? (firstEvidence ?? secondEvidence)!.Identity
                : GraphNodeIdentity.FromMember(member);
            int id = GetOrAdd(
                identity,
                member,
                CallGraphNodeKind.Focus,
                perf,
                firstEvidence,
                firstDefinitionAssembly,
                firstResolutionAssembly);
            AddEvidence(_nodes[id], secondEvidence);
            AddDefinitionAssembly(
                _nodes[id],
                secondDefinitionAssembly);
            AddResolutionAssembly(
                _nodes[id],
                secondResolutionAssembly);
            return id;
        }

        /// <summary>Walk a reverse (caller) tree: each child calls its parent, so edges point child → parent.</summary>
        public void WalkCallers(CallTreeNode node, int nodeId)
        {
            foreach (var child in node.Children)
            {
                int childId = GetOrAdd(
                    Identity(child, useGraphEvidence),
                    child.Member,
                    KindFor(child.Status),
                    child.Perf,
                    child.GraphEvidence,
                    child.DefinitionAssemblyIdentity,
                    child.ResolutionAssemblyIdentity);
                AddEdge(
                    childId,
                    nodeId,
                    child.ParentEdgeCallSites,
                    child.ParentEdgeCallerDefinition,
                    child.Perf is { InLoop: true },
                    child.Perf?.LoopHint,
                    CallGraphEdgeOrigin.Callers);
                WalkCallers(child, childId);
            }
        }

        /// <summary>Walk an outbound (callee) tree: each parent calls its children, so edges point parent → child.</summary>
        public void WalkCallees(CallTreeNode node, int nodeId)
        {
            foreach (var child in node.Children)
            {
                int childId = GetOrAdd(
                    Identity(child, useGraphEvidence),
                    child.Member,
                    KindFor(child.Status),
                    child.Perf,
                    child.GraphEvidence,
                    child.DefinitionAssemblyIdentity,
                    child.ResolutionAssemblyIdentity);
                AddEdge(
                    nodeId,
                    childId,
                    child.ParentEdgeCallSites,
                    child.ParentEdgeCallerDefinition,
                    child.Perf is { InLoop: true },
                    child.Perf?.LoopHint,
                    CallGraphEdgeOrigin.Callees);
                WalkCallees(child, childId);
            }
        }

        public CallGraphProjection Build(
            bool hasUnexploredTraversalBoundary,
            bool hasAnalysisFailureBoundary)
        {
            var nodes = ImmutableArray.CreateBuilder<CallGraphNode>(_nodes.Count);
            foreach (var node in _nodes)
            {
                nodes.Add(
                    new CallGraphNode(
                        node.Id,
                        node.Identity,
                        node.Member,
                        node.Label,
                        node.Kind,
                        node.Perf,
                        [.. node.GraphEvidence],
                        node.DefinitionAssemblyIdentity,
                        node.ResolutionAssemblyIdentity));
            }
            var edges = ImmutableArray.CreateBuilder<CallGraphEdge>(
                _edges.Count);
            foreach (MutableEdge edge in _edges)
            {
                edges.Add(
                    new CallGraphEdge(
                        edge.From,
                        edge.To,
                        edge.AnyCallInLoop,
                        edge.Origin,
                        [.. edge.CallSiteIds],
                        edge.HasUnavailablePhysicalOccurrences,
                        edge.LegacyLoopHint));
            }
            return new CallGraphProjection(
                nodes.MoveToImmutable(),
                edges.MoveToImmutable(),
                [.. _callSites],
                hasUnexploredTraversalBoundary,
                hasAnalysisFailureBoundary);
        }

        private int GetOrAdd(
            GraphNodeIdentity identity,
            MemberRef member,
            CallGraphNodeKind candidate,
            CallTreePerf? perf,
            GraphNodeEvidence? evidence,
            AssemblyReferenceIdentity? definitionAssemblyIdentity,
            AssemblyReferenceIdentity? resolutionAssemblyIdentity = null)
        {
            if (!_ids.TryGetValue(identity, out var id))
            {
                id = _nodes.Count;
                _ids[identity] = id;
                var node = new MutableNode(
                    id,
                    identity,
                    member,
                    Label(member),
                    candidate,
                    perf);
                AddEvidence(node, evidence);
                AddDefinitionAssembly(
                    node,
                    definitionAssemblyIdentity);
                AddResolutionAssembly(
                    node,
                    resolutionAssemblyIdentity);
                _nodes.Add(node);
                return id;
            }

            // A member seen more than once keeps its strongest classification: the
            // selected focus is sticky, an expanded/leaf occurrence outranks a boundary,
            // so a shared node is not mislabelled a dead end.
            var info = _nodes[id];
            if (candidate > info.Kind)
                info.Kind = candidate;
            // A member reached by both walks was measured twice, each time by a walk that
            // only indexes one direction, so merge the observations field by field.
            info.Perf = MergePerf(info.Perf, perf);
            AddEvidence(info, evidence);
            AddDefinitionAssembly(
                info,
                definitionAssemblyIdentity);
            AddResolutionAssembly(
                info,
                resolutionAssemblyIdentity);
            return id;
        }

        static void AddEvidence(
            MutableNode node,
            GraphNodeEvidence? evidence)
        {
            if (evidence is null
                || node.GraphEvidence.Any(
                    existing => existing.Storage.Equals(evidence.Storage)))
            {
                return;
            }

            node.GraphEvidence.Add(evidence);
        }

        static void AddDefinitionAssembly(
            MutableNode node,
            AssemblyReferenceIdentity? identity)
        {
            if (identity is null
                || node.HasDefinitionAssemblyConflict)
            {
                return;
            }
            if (node.DefinitionAssemblyIdentity is null)
            {
                node.DefinitionAssemblyIdentity = identity;
                return;
            }
            if (node.DefinitionAssemblyIdentity.IsEquivalentTo(identity))
                return;

            node.DefinitionAssemblyIdentity = null;
            node.HasDefinitionAssemblyConflict = true;
        }

        static void AddResolutionAssembly(
            MutableNode node,
            AssemblyReferenceIdentity? identity)
        {
            if (identity is null
                || node.HasResolutionAssemblyConflict)
            {
                return;
            }
            if (node.ResolutionAssemblyIdentity is null)
            {
                node.ResolutionAssemblyIdentity = identity;
                return;
            }
            if (node.ResolutionAssemblyIdentity
                .IsEquivalentTo(identity))
            {
                return;
            }

            node.ResolutionAssemblyIdentity = null;
            node.HasResolutionAssemblyConflict = true;
        }

        private void AddEdge(
            int from,
            int to,
            ImmutableArray<DirectCall> callSites,
            GraphNodeStorageKey? callerDefinition,
            bool fallbackInLoop,
            string? fallbackLoopHint,
            CallGraphEdgeOrigin origin)
        {
            if (!_edgeIndex.TryGetValue((from, to), out int index))
            {
                index = _edges.Count;
                _edgeIndex.Add((from, to), index);
                _edges.Add(new MutableEdge(from, to, origin));
            }

            MutableEdge edge = _edges[index];
            if (callSites.IsDefaultOrEmpty)
            {
                AddLegacyFallbackEvidence(
                    edge,
                    fallbackInLoop,
                    fallbackLoopHint);
                return;
            }

            foreach (DirectCall call in callSites)
            {
                ArgumentNullException.ThrowIfNull(call);
                var identity = new CallGraphCallSiteIdentity(
                    useAcquisitionReceiptIdentity
                        ? callerDefinition
                        : null,
                    useAcquisitionReceiptIdentity
                        ? null
                        : GraphNodeIdentity.FromMember(
                            _nodes[from].Member),
                    call.EvidenceMethod.ModuleVersionId,
                    call.EvidenceMethod.MetadataToken,
                    call.ILOffset,
                    call.OperandToken);
                if (_callSiteIds.TryGetValue(
                        identity,
                        out int existingId))
                {
                    CallGraphCallSite existing =
                        _callSites[existingId];
                    if (!SameCallEvidence(
                            existing.Call,
                            call))
                    {
                        throw new InvalidOperationException(
                            "One physical call-site identity cannot carry contradictory evidence.");
                    }
                    if (existing.EdgeId != index)
                    {
                        // Independently detached direction scopes can disagree
                        // on the target identity for one physical receipt.
                        AddUnavailablePhysicalEvidence(
                            edge,
                            fallbackInLoop,
                            fallbackLoopHint);
                        continue;
                    }
                    edge.LegacyLoopHint = null;
                    edge.PhysicalAnyCallInLoop |= call.InLoop;
                    continue;
                }

                edge.LegacyLoopHint = null;
                edge.PhysicalAnyCallInLoop |= call.InLoop;
                int id = _callSites.Count;
                _callSiteIds.Add(identity, id);
                _callSites.Add(
                    new CallGraphCallSite(
                        id,
                        index,
                        identity,
                        call,
                        DispatchKind(call)));
                edge.CallSiteIds.Add(id);
            }
        }

        static bool SameCallEvidence(
            DirectCall first,
            DirectCall second) =>
            first == second
            || first with
            {
                Caller = second.Caller,
            } == second;

        static void AddLegacyFallbackEvidence(
            MutableEdge edge,
            bool inLoop,
            string? loopHint)
        {
            edge.FallbackAnyCallInLoop |= inLoop;
            if (edge.CallSiteIds.Count == 0
                && inLoop
                && edge.LegacyLoopHint is null)
            {
                edge.LegacyLoopHint = loopHint;
            }
        }

        static void AddUnavailablePhysicalEvidence(
            MutableEdge edge,
            bool inLoop,
            string? loopHint)
        {
            edge.HasUnavailablePhysicalOccurrences = true;
            AddLegacyFallbackEvidence(
                edge,
                inLoop,
                loopHint);
        }
    }

    static GraphNodeIdentity Identity(
        CallTreeNode node,
        bool useGraphEvidence) =>
        useGraphEvidence
            ? node.GraphEvidence!.Identity
            : GraphNodeIdentity.FromMember(node.Member);

    static bool HasCompleteGraphEvidence(CallTreeNode? root)
    {
        if (root is null)
            return true;
        if (root.GraphEvidence is null)
            return false;
        foreach (CallTreeNode child in root.Children)
        {
            if (!HasCompleteGraphEvidence(child))
                return false;
        }

        return true;
    }

    static bool HasCompleteCallerDefinitions(CallTreeNode? root)
    {
        if (root is null)
            return true;

        foreach (CallTreeNode child in root.Children)
        {
            if (!child.ParentEdgeCallSites.IsDefaultOrEmpty)
            {
                GraphNodeStorageKey? definition =
                    child.ParentEdgeCallerDefinition;
                if (definition is not
                        {
                            Kind: GraphNodeStorageKind.Definition,
                        }
                    || child.ParentEdgeCallSites.Any(call =>
                        definition.ModuleVersionId
                            != call.Caller.ModuleVersionId
                        || definition.MethodToken
                            != call.Caller.MetadataToken))
                {
                    return false;
                }
            }

            if (!HasCompleteCallerDefinitions(child))
                return false;
        }

        return true;
    }

    static bool IsTraversalComplete(
        CallTreeNode? root,
        bool useGraphEvidence)
    {
        if (root is null)
            return false;

        var completeByIdentity =
            new Dictionary<GraphNodeIdentity, bool>();
        Add(root);
        return completeByIdentity.Values.All(
            static complete => complete);

        void Add(CallTreeNode node)
        {
            GraphNodeIdentity identity =
                Identity(node, useGraphEvidence);
            bool complete = node.Status
                is CallTreeStatus.Expanded
                or CallTreeStatus.Leaf;
            completeByIdentity.TryGetValue(
                identity,
                out bool alreadyComplete);
            completeByIdentity[identity] =
                alreadyComplete || complete;
            foreach (CallTreeNode child in node.Children)
                Add(child);
        }
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
        CallTreeStatus.DepthLimited
            or CallTreeStatus.Truncated
            or CallTreeStatus.Bodiless
            or CallTreeStatus.AnalysisIncomplete
                => CallGraphNodeKind.Truncated,
        _ => CallGraphNodeKind.Normal,
    };

    private static bool HasAnalysisFailure(CallTreeNode? node)
        => node is not null
            && (node.Status == CallTreeStatus.AnalysisIncomplete
                || node.Diagnostic is not null
                || node.Children.Any(HasAnalysisFailure));

    private static bool HasUnresolvedDispatch(CallTreeNode? node)
        => node is not null
            && (node.HasUnresolvedDispatch
                || node.Children.Any(HasUnresolvedDispatch));

    private static CallGraphDispatchKind DispatchKind(
        DirectCall call) =>
        call.Kind switch
        {
            CallKind.Call or CallKind.NewObject =>
                CallGraphDispatchKind.Direct,
            CallKind.CallVirtual when call.ExactTarget =>
                CallGraphDispatchKind.Direct,
            CallKind.CallVirtual =>
                CallGraphDispatchKind.Virtual,
            CallKind.LoadFunction =>
                CallGraphDispatchKind.FunctionPointer,
            CallKind.LoadVirtualFunction =>
                CallGraphDispatchKind.VirtualFunctionPointer,
            CallKind.CallIndirect =>
                CallGraphDispatchKind.Indirect,
            _ => throw new ArgumentOutOfRangeException(
                nameof(call)),
        };

    /// <summary>
    /// Combines two observations of the same member, one per walk direction.
    /// </summary>
    /// <remarks>
    /// Neither walk sees the whole member. A caller tree indexes the caller scope and reports
    /// fan-in, the root classification, and cross-assembly source, but hard-codes fan-out to 0.
    /// A callee tree indexes the callee scope and reports fan-out, but never classifies a root.
    /// Picking one record therefore publishes a direction that was never measured, so each field
    /// is merged by the side that actually measured it. Degrees and depth are lower bounds over
    /// whichever scope set that walk indexed, so the larger observation is the better-informed
    /// one and a direction that does not measure a degree reports 0 and can never win.
    /// </remarks>
    private static CallTreePerf? MergePerf(CallTreePerf? first, CallTreePerf? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        return first with
        {
            Fanout = Math.Max(first.Fanout, second.Fanout),
            Fanin = Math.Max(first.Fanin, second.Fanin),
            MaxDepth = Math.Max(first.MaxDepth, second.MaxDepth),
            InLoop = first.InLoop || second.InLoop,
            LoopHint = first.InLoop ? first.LoopHint : second.LoopHint,
            RootKind = first.RootKind ?? second.RootKind,
            Signals = PreferMeasured(first.Signals, second.Signals),
            Source = first.Source ?? second.Source,
        };

        // Both walks default a member they could not resolve to the None singleton, so a
        // by-reference test distinguishes "no signals were measured" from "none were found".
        static MethodSignals? PreferMeasured(MethodSignals? first, MethodSignals? second)
            => first is not null && !ReferenceEquals(first, MethodSignals.None) ? first : second ?? first;
    }
}
