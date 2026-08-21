using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>How a node in a bounded call tree relates to the rest of the graph.</summary>
public enum CallTreeStatus
{
    /// <summary>An in-assembly method whose outbound calls were expanded as children.</summary>
    Expanded,

    /// <summary>An in-assembly method with no outbound calls (a true leaf).</summary>
    Leaf,

    /// <summary>A callee that resolves outside the current assembly, so it is not expanded.</summary>
    External,

    /// <summary>A method already expanded elsewhere in the tree (shared callee or cycle); not re-expanded.</summary>
    AlreadyShown,

    /// <summary>An in-assembly method with outbound calls left unexpanded because the depth limit was reached.</summary>
    DepthLimited,

    /// <summary>An in-assembly method whose children were partially expanded before the node budget ran out.</summary>
    Truncated,

    /// <summary>
    /// A resolved method with no IL body, so static operand traversal cannot prove
    /// that runtime dispatch or an external implementation cannot re-enter the graph.
    /// </summary>
    Bodiless,

    /// <summary>
    /// A method whose IL body analysis failed, so its recorded calls may be incomplete.
    /// </summary>
    AnalysisIncomplete,
}

/// <summary>
/// One node in a bounded outbound (callee) call tree rooted at a selected method.
/// Presentation-free: callers format <see cref="Member"/> and <see cref="Kind"/> for display.
/// </summary>
public sealed record CallTreeNode(
    MemberRef Member,
    CallKind? Kind,
    CallTreeStatus Status,
    ImmutableArray<CallTreeNode> Children,
    CallTreePerf? Perf = null)
{
    /// <summary>
    /// Physical and correspondence evidence for this occurrence when the tree
    /// was built from a catalog call-graph scope.
    /// </summary>
    public GraphNodeEvidence? GraphEvidence { get; init; }

    /// <summary>
    /// Physical calls supporting the edge between this node and its parent.
    /// Each call retains its semantic caller-to-callee direction regardless of
    /// whether this node belongs to a caller or callee tree. The root has no
    /// parent-edge call sites.
    /// </summary>
    public ImmutableArray<DirectCall> ParentEdgeCallSites { get; init; } = [];

    /// <summary>
    /// Acquisition-aware definition storage for the caller that owns
    /// <see cref="ParentEdgeCallSites"/>, when a catalog scope supplied the
    /// edge.
    /// </summary>
    public GraphNodeStorageKey? ParentEdgeCallerDefinition { get; init; }

    /// <summary>
    /// The recoverable body-analysis failure that made this node incomplete, if any.
    /// </summary>
    public AnalysisDiagnostic? Diagnostic { get; init; }

    /// <summary>
    /// Whether this occurrence can dispatch to an override that the static operand
    /// traversal does not represent.
    /// </summary>
    public bool HasUnresolvedDispatch { get; init; }
}

/// <summary>Perf-triage cues surfaced for a call-graph node.</summary>
/// <remarks>
/// <see cref="Fanout"/>/<see cref="Fanin"/>/<see cref="MaxDepth"/>/<see cref="InLoop"/>
/// are scale/leverage cues; <see cref="Signals"/> carries the <em>kind-of-work</em>
/// signals (allocations, copies, unsafe, reflection, throw/catch/finally) projected
/// on request via <c>--fields</c>.
/// </remarks>
public sealed record CallTreePerf(
    int Fanout,
    int Fanin,
    int MaxDepth,
    bool InLoop,
    string? LoopHint = null,
    string? RootKind = null,
    MethodSignals? Signals = null,
    string? Source = null)
{
    /// <summary>The node's signals, never null (falls back to <see cref="MethodSignals.None"/>).</summary>
    public MethodSignals SignalsOrNone => Signals ?? MethodSignals.None;
}

static class CallTreeMember
{
    internal static string ToQualifiedDisplayString(
        MethodIdentity method) =>
        $"{method.DeclaringType.ToQualifiedDisplayString()}::{method.Name}";

    internal static MemberRef FromDefinition(MethodIdentity method) =>
        new(
            method.DeclaringType,
            method.Name,
            method.ParameterTypes,
            method.ReturnType,
            method.Name is ".ctor" or ".cctor"
                ? MemberKind.Constructor
                : MemberKind.Method)
        {
            GenericArity = method.GenericArity,
            HasThis = !method.IsStatic,
            SignatureHeader = method.SignatureHeader,
            RequiredParameterCount = method.RequiredParameterCount,
            IsOperator = method.IsOperator,
            OpenParameterTypes = method.ParameterTypes,
            OpenReturnType = method.ReturnType,
        };
}

static class CallTreeOrdering
{
    internal static IOrderedEnumerable<T> OrderCallers<T>(
        IEnumerable<T> edges,
        Func<T, string> assemblyName,
        Func<T, string> qualifiedDisplayName,
        Func<T, int> parameterCount,
        Func<T, Guid> moduleVersionId,
        Func<T, int> methodToken,
        Func<T, int> ilOffset) =>
        edges
            .OrderBy(assemblyName, StringComparer.Ordinal)
            .ThenBy(qualifiedDisplayName, StringComparer.Ordinal)
            .ThenBy(parameterCount)
            .ThenBy(moduleVersionId)
            .ThenBy(methodToken)
            .ThenBy(ilOffset);
}
