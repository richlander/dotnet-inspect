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
    CallTreePerf? Perf = null);

/// <summary>Perf-triage cues surfaced for a call-graph node.</summary>
public sealed record CallTreePerf(int Fanout, int Fanin, int MaxDepth, bool InLoop, string? LoopHint = null, string? RootKind = null);
