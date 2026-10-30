using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// How one member relates to the rest of an assembly-context call graph.
/// </summary>
public enum AssemblyContextMemberCallGraphNodeKind
{
    Truncated,
    External,
    Normal,
    Focus,
}

/// <summary>
/// One projected member in an assembly-context call graph.
/// </summary>
public sealed record AssemblyContextMemberCallGraphNode(
    int Id,
    CallGraphMemberNavigationDocument Member,
    string DefaultLabel,
    AssemblyContextMemberCallGraphNodeKind Kind,
    AssemblyReferenceIdentity? DefinitionAssemblyIdentity = null,
    AssemblyReferenceIdentity? ResolutionAssemblyIdentity = null,
    AssemblyReferenceIdentity? OccurrenceAssemblyIdentity = null);

public enum AssemblyContextMemberCallGraphTreeStatus
{
    Expanded,
    Leaf,
    External,
    AlreadyShown,
    DepthLimited,
    Truncated,
    Bodiless,
    AnalysisIncomplete,
}

public enum AssemblyContextMemberCallGraphCallKind
{
    Call,
    CallVirtual,
    NewObject,
    LoadFunction,
    LoadVirtualFunction,
    CallIndirect,
}

public sealed record AssemblyContextMemberCallGraphTreeNode(
    CallGraphMemberNavigationDocument Member,
    AssemblyContextMemberCallGraphCallKind? Kind,
    AssemblyContextMemberCallGraphTreeStatus Status,
    ImmutableArray<AssemblyContextMemberCallGraphTreeNode> Children,
    bool InLoop);

public sealed record AssemblyContextMemberCallGraphDiagnostics(
    int IncompleteNodeCount,
    int IncompleteEdgeCount,
    int BindingIdentityConflictCount);

/// <summary>
/// One projected caller-to-callee relationship.
/// </summary>
public readonly record struct AssemblyContextMemberCallGraphEdge(
    int From,
    int To,
    bool AnyCallInLoop);

/// <summary>
/// Owner-issued call-tree and deterministic graph evidence for one selected member.
/// </summary>
public sealed record AssemblyContextMemberCallGraphDocument(
    CallGraphTier Tier,
    AssemblyContextMemberCallGraphTreeNode? CalleeRoot,
    AssemblyContextMemberCallGraphTreeNode? CallerRoot,
    ImmutableArray<AssemblyContextMemberCallGraphNode> Nodes,
    ImmutableArray<AssemblyContextMemberCallGraphEdge> Edges,
    AssemblyContextMemberCallGraphDiagnostics Diagnostics,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary)
{
    public bool NoBody => CalleeRoot is null && CallerRoot is null;
}

/// <summary>
/// Builds one member call graph over a binding-consistent assembly context.
/// </summary>
/// <remarks>
/// The query owns the progressive Analysis session and the deterministic
/// CallGraph projection. Consumers receive the selected roots plus the
/// projection facts needed for host-owned rendering; they do not construct
/// either producer.
/// </remarks>
public static class AssemblyContextMemberCallGraphQuery
{
    public static InspectionQuery<AssemblyContextMemberCallGraphDocument>
        Definition { get; } =
        new("Assembly context member call graph", InspectionCost.Unbounded);

    public static AssemblyContextMemberCallGraphDocument Execute(
        AssemblyContextGroup group,
        ResolvedAssemblyReference rootAssembly,
        int memberToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(rootAssembly);

        using var session = new MemberCallGraphSession(
            group,
            rootAssembly,
            memberToken);
        MemberCallGraphView view = session.HasCrossLibraryScope
            ? session.CrossLibrary()
            : session.Callers();
        return Project(view);
    }

    internal static AssemblyContextMemberCallGraphDocument Project(
        MemberCallGraphView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        CallGraphProjection projection = CallGraphProjection.Create(
            view.CallerRoot,
            view.CalleeRoot);
        return new AssemblyContextMemberCallGraphDocument(
            view.Tier,
            ProjectTree(view.CalleeRoot),
            ProjectTree(view.CallerRoot),
            [
                .. projection.Nodes.Select(node =>
                    ProjectNode(node)),
            ],
            [
                .. projection.Edges.Select(edge =>
                    new AssemblyContextMemberCallGraphEdge(
                        edge.From,
                        edge.To,
                        edge.AnyCallInLoop)),
            ],
            new(
                view.Diagnostics.IncompleteNodeCount,
                view.Diagnostics.IncompleteEdgeCount,
                view.Diagnostics.BindingIdentityConflictCount),
            projection.HasUnexploredTraversalBoundary,
            projection.HasAnalysisFailureBoundary);
    }

    public static AssemblyContextMemberCallGraphNode ProjectNode(
        CallGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new(
            node.Id,
            CallGraphMemberNavigationProjection.ProjectOpenSignature(
                node.Member),
            node.Label,
            NodeKind(node.Kind),
            node.DefinitionAssemblyIdentity,
            node.ResolutionAssemblyIdentity,
            node.OccurrenceAssemblyIdentity);
    }

    static AssemblyContextMemberCallGraphTreeNode? ProjectTree(
        Analysis.CallTreeNode? node) =>
        node is null
            ? null
            : ProjectTreeNode(node);

    static AssemblyContextMemberCallGraphTreeNode ProjectTreeNode(
        Analysis.CallTreeNode node) =>
        new(
            CallGraphMemberNavigationProjection.ProjectClosedSignature(
                node.Member),
            node.Kind is null
                ? null
                : CallKind(node.Kind.Value),
            TreeStatus(node.Status),
            [.. node.Children.Select(ProjectTreeNode)],
            node.Perf?.InLoop ?? false);

    static AssemblyContextMemberCallGraphCallKind CallKind(
        Analysis.CallKind kind) =>
        kind switch
        {
            Analysis.CallKind.Call =>
                AssemblyContextMemberCallGraphCallKind.Call,
            Analysis.CallKind.CallVirtual =>
                AssemblyContextMemberCallGraphCallKind.CallVirtual,
            Analysis.CallKind.NewObject =>
                AssemblyContextMemberCallGraphCallKind.NewObject,
            Analysis.CallKind.LoadFunction =>
                AssemblyContextMemberCallGraphCallKind.LoadFunction,
            Analysis.CallKind.LoadVirtualFunction =>
                AssemblyContextMemberCallGraphCallKind.LoadVirtualFunction,
            Analysis.CallKind.CallIndirect =>
                AssemblyContextMemberCallGraphCallKind.CallIndirect,
            _ => throw new InvalidOperationException(
                "Unknown call kind."),
        };

    static AssemblyContextMemberCallGraphTreeStatus TreeStatus(
        Analysis.CallTreeStatus status) =>
        status switch
        {
            Analysis.CallTreeStatus.Expanded =>
                AssemblyContextMemberCallGraphTreeStatus.Expanded,
            Analysis.CallTreeStatus.Leaf =>
                AssemblyContextMemberCallGraphTreeStatus.Leaf,
            Analysis.CallTreeStatus.External =>
                AssemblyContextMemberCallGraphTreeStatus.External,
            Analysis.CallTreeStatus.AlreadyShown =>
                AssemblyContextMemberCallGraphTreeStatus.AlreadyShown,
            Analysis.CallTreeStatus.DepthLimited =>
                AssemblyContextMemberCallGraphTreeStatus.DepthLimited,
            Analysis.CallTreeStatus.Truncated =>
                AssemblyContextMemberCallGraphTreeStatus.Truncated,
            Analysis.CallTreeStatus.Bodiless =>
                AssemblyContextMemberCallGraphTreeStatus.Bodiless,
            Analysis.CallTreeStatus.AnalysisIncomplete =>
                AssemblyContextMemberCallGraphTreeStatus.AnalysisIncomplete,
            _ => throw new InvalidOperationException(
                "Unknown call-tree status."),
        };

    static AssemblyContextMemberCallGraphNodeKind NodeKind(
        CallGraphNodeKind kind) =>
        kind switch
        {
            CallGraphNodeKind.Truncated =>
                AssemblyContextMemberCallGraphNodeKind.Truncated,
            CallGraphNodeKind.External =>
                AssemblyContextMemberCallGraphNodeKind.External,
            CallGraphNodeKind.Normal =>
                AssemblyContextMemberCallGraphNodeKind.Normal,
            CallGraphNodeKind.Focus =>
                AssemblyContextMemberCallGraphNodeKind.Focus,
            _ => throw new InvalidOperationException(
                "Unknown call-graph node kind."),
        };
}
