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
    Analysis.MemberRef Member,
    string DefaultLabel,
    AssemblyContextMemberCallGraphNodeKind Kind,
    AssemblyReferenceIdentity? DefinitionAssemblyIdentity = null,
    AssemblyReferenceIdentity? ResolutionAssemblyIdentity = null,
    AssemblyReferenceIdentity? OccurrenceAssemblyIdentity = null);

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
    Analysis.CallTreeNode? CalleeRoot,
    Analysis.CallTreeNode? CallerRoot,
    ImmutableArray<AssemblyContextMemberCallGraphNode> Nodes,
    ImmutableArray<AssemblyContextMemberCallGraphEdge> Edges,
    Analysis.CatalogCallGraphDiagnostics Diagnostics,
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
            view.CalleeRoot,
            view.CallerRoot,
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
            view.Diagnostics,
            projection.HasUnexploredTraversalBoundary,
            projection.HasAnalysisFailureBoundary);
    }

    public static AssemblyContextMemberCallGraphNode ProjectNode(
        CallGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new(
            node.Id,
            node.Member,
            node.Label,
            NodeKind(node.Kind),
            node.DefinitionAssemblyIdentity,
            node.ResolutionAssemblyIdentity,
            node.OccurrenceAssemblyIdentity);
    }

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
