using System.Collections.Immutable;
using InertText;

namespace DotnetInspector.Sections;

public sealed record DependencyHierarchyNode(
    DependencyHierarchyOccurrenceIdentity Identity,
    InertString Label,
    InertString? Relationship,
    DependencyHierarchyOccurrenceDisposition? Disposition,
    ImmutableArray<int> BoundaryDepths,
    bool IsFragment)
{
    public ImmutableArray<int> BoundaryDepths { get; init; } =
        BoundaryDepths.IsDefault ? [] : BoundaryDepths;
}

public static class DependencyHierarchyProjection
{
    public static void Write(
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceIdentity>
            selectedOccurrences,
        IInspectionHierarchySink<DependencyHierarchyNode> sink)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedOccurrences);
        ArgumentNullException.ThrowIfNull(sink);

        DependencyGraphDocument graph = document.BackingGraph;
        Dictionary<DependencyHierarchyOccurrenceIdentity,
            DependencyHierarchyOccurrence> occurrencesByIdentity =
            document.Occurrences.ToDictionary(
                static occurrence => occurrence.Identity);
        Dictionary<int, DependencyGraphEdge> edgesById =
            graph.Edges.ToDictionary(static edge => edge.Id);
        Dictionary<DependencyHierarchyOccurrenceIdentity,
            HierarchyOccurrenceContext> contextsByOccurrence =
            ContextsByOccurrence(document);
        var selected = new HashSet<DependencyHierarchyOccurrenceIdentity>();
        var occurrences =
            new List<DependencyHierarchyOccurrence>(
                selectedOccurrences.Count);
        foreach (DependencyHierarchyOccurrenceIdentity identity in
                 selectedOccurrences)
        {
            if (!occurrencesByIdentity.TryGetValue(
                    identity,
                    out DependencyHierarchyOccurrence? occurrence))
            {
                throw new ArgumentException(
                    $"Unknown dependency hierarchy occurrence '{identity.RootOccurrence.Value}:{identity.Value}'.",
                    nameof(selectedOccurrences));
            }
            if (!selected.Add(identity))
            {
                throw new ArgumentException(
                    $"Duplicate dependency hierarchy occurrence '{identity.RootOccurrence.Value}:{identity.Value}'.",
                    nameof(selectedOccurrences));
            }
            occurrences.Add(occurrence);
        }

        var childrenByParent =
            new Dictionary<
                DependencyHierarchyOccurrenceIdentity,
                List<DependencyHierarchyOccurrence>>();
        var fragmentParents =
            new List<DependencyHierarchyOccurrenceIdentity>();
        var fragmentParentSet =
            new HashSet<DependencyHierarchyOccurrenceIdentity>();
        foreach (DependencyHierarchyOccurrence occurrence in occurrences)
        {
            if (!childrenByParent.TryGetValue(
                    occurrence.ParentIdentity,
                    out List<DependencyHierarchyOccurrence>? children))
            {
                children = [];
                childrenByParent.Add(
                    occurrence.ParentIdentity,
                    children);
            }
            children.Add(occurrence);

            DependencyHierarchyOccurrenceIdentity parent =
                occurrence.ParentIdentity;
            if (parent.Value != 0
                && !selected.Contains(parent)
                && fragmentParentSet.Add(parent))
            {
                fragmentParents.Add(parent);
            }
        }

        int topLevelCount =
            document.Roots.Length + fragmentParents.Count;
        int topLevelIndex = 0;
        foreach (DependencyHierarchyRootOccurrence root in document.Roots)
        {
            WriteNode(
                root.Identity,
                disposition: null,
                isFragment: false,
                includeBoundary: true,
                isLastSibling: ++topLevelIndex == topLevelCount,
                sink);
        }
        foreach (DependencyHierarchyOccurrenceIdentity fragmentParent in
                 fragmentParents)
        {
            WriteNode(
                fragmentParent,
                occurrencesByIdentity[fragmentParent].Disposition,
                isFragment: true,
                includeBoundary: true,
                isLastSibling: ++topLevelIndex == topLevelCount,
                sink);
        }

        void WriteNode(
            DependencyHierarchyOccurrenceIdentity identity,
            DependencyHierarchyOccurrenceDisposition? disposition,
            bool isFragment,
            bool includeBoundary,
            bool isLastSibling,
            IInspectionHierarchySink<DependencyHierarchyNode> destination)
        {
            HierarchyOccurrenceContext context =
                contextsByOccurrence[identity];
            var node = new DependencyHierarchyNode(
                identity,
                graph.Nodes[context.NodeId].Label,
                identity.Value == 0 || isFragment
                    ? null
                    : new InertString(
                        TextPolicy.Field,
                        edgesById[
                            occurrencesByIdentity[identity].IncomingEdgeId]
                            .Relationship),
                disposition,
                includeBoundary
                    ? BoundaryDepths(
                        graph,
                        context,
                        identity.RootOccurrence.Value)
                    : [],
                isFragment);
            if (!childrenByParent.TryGetValue(
                    identity,
                    out List<DependencyHierarchyOccurrence>? children))
            {
                destination.WriteNode(node, isLastSibling);
                return;
            }

            destination.WriteNode(
                node,
                isLastSibling,
                childSink =>
                {
                    for (int index = 0; index < children.Count; index++)
                    {
                        DependencyHierarchyOccurrence child =
                            children[index];
                        WriteNode(
                            child.Identity,
                            child.Disposition,
                            isFragment: false,
                            includeBoundary:
                                child.Disposition
                                    == DependencyHierarchyOccurrenceDisposition
                                        .Expanded,
                            isLastSibling: index == children.Count - 1,
                            childSink);
                    }
                });
        }
    }

    private static Dictionary<DependencyHierarchyOccurrenceIdentity,
        HierarchyOccurrenceContext> ContextsByOccurrence(
        DependencyHierarchyDocument document)
    {
        DependencyGraphDocument graph = document.BackingGraph;
        Dictionary<int, DependencyGraphEdge> edgesById =
            graph.Edges.ToDictionary(static edge => edge.Id);
        Dictionary<int, int> rootPackageProjections =
            graph.PackageProjections
                .Where(static projection =>
                    projection.RootOccurrence is not null)
                .ToDictionary(
                    static projection => projection.RootOccurrence!.Value,
                    static projection => projection.Id);
        var result =
            new Dictionary<
                DependencyHierarchyOccurrenceIdentity,
                HierarchyOccurrenceContext>();
        foreach (DependencyHierarchyRootOccurrence root in document.Roots)
        {
            result.Add(
                root.Identity,
                new HierarchyOccurrenceContext(
                    root.NodeId,
                    rootPackageProjections.TryGetValue(
                        root.RootOccurrence.Value,
                        out int projectionId)
                            ? projectionId
                            : null));
        }
        foreach (DependencyHierarchyOccurrence occurrence in
                 document.Occurrences)
        {
            result.Add(
                occurrence.Identity,
                new HierarchyOccurrenceContext(
                    occurrence.TargetNodeId,
                    edgesById[occurrence.IncomingEdgeId]
                        .TargetPackageProjectionId));
        }
        return result;
    }

    private static ImmutableArray<int> BoundaryDepths(
        DependencyGraphDocument graph,
        HierarchyOccurrenceContext context,
        int rootOccurrence) =>
    [
        .. graph.DepthBoundaries
            .Where(boundary =>
                boundary.NodeId == context.NodeId
                && boundary.PackageProjectionId
                    == context.PackageProjectionId
                && boundary.RootOccurrences.Contains(rootOccurrence))
            .Select(static boundary => boundary.MaximumDepth)
            .Distinct()
            .Order(),
    ];

    private readonly record struct HierarchyOccurrenceContext(
        int NodeId,
        int? PackageProjectionId);
}
