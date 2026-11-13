using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspector.Ecosystems;

public static partial class EcosystemPackCatalog
{
    public static InspectionEnvelope<DependencyEcosystemCompositionDocument>
        InspectDependencyComposition(
            DependencyGraphDocument graph,
            IEnumerable<string> targets,
            bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(targets);

        var coordinates = new HashSet<PackageSourceCoordinate>();
        var counts = new Dictionary<EcosystemPackId, int>();
        int unclassified = 0;
        foreach (DependencyGraphNode node in graph.Nodes)
        {
            PackageSourceCoordinate? coordinate = node.Identity switch
            {
                DependencyGraphNodeIdentity.Package package =>
                    PackageSourceCoordinate.Create(package.Id, package.Version),
                DependencyGraphNodeIdentity.RestoredPackage package =>
                    package.Identity.Coordinate,
                _ => null,
            };
            if (coordinate is null || !coordinates.Add(coordinate))
                continue;

            if (PackageOwner(coordinate.PackageId) is { } ecosystem)
                counts[ecosystem.Id] = counts.GetValueOrDefault(ecosystem.Id) + 1;
            else
                unclassified++;
        }

        return new(
            new(
                coordinates.Count,
                unclassified,
                [.. Discover()
                    .Where(pack => counts.ContainsKey(pack.Id))
                    .OrderBy(pack => pack.Order)
                    .Select(pack => new DependencyEcosystemCompositionRow(
                        pack.Id.Value, pack.Title, counts[pack.Id]))],
                [.. targets.Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)],
                isComplete && graph.DepthBoundaries.IsEmpty),
            new InspectionShare.NonProjectable(
                "dependencies/composition",
                "Dependency composition requires its owning graph inspection."));
    }

    public static InspectionEnvelope<DependencyInspectionContent>
        WithDependencyComposition(
            InspectionEnvelope<DependencyInspectionContent> inspection,
            TraversalTargetFrameworkPolicy packageTraversalTarget)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        DependencyInspectionContent content = inspection.Content;
        DependencyGraphDocument graph = content.Hierarchy.BackingGraph;
        bool packages = graph.Nodes.Any(node =>
            node.Identity is DependencyGraphNodeIdentity.Package);
        string[] restoredTargets =
        [
            .. graph.Nodes
                .Select(node => node.Identity)
                .OfType<DependencyGraphNodeIdentity.RestoredRoot>()
                .Select(root => root.Identity.Selection.TargetIdentity),
        ];
        if (!packages && restoredTargets.Length == 0)
            return inspection;

        DependencyEcosystemCompositionDocument composition =
            InspectDependencyComposition(
                graph,
                packages
                    ? restoredTargets.Append(packageTraversalTarget.TargetFramework)
                    : restoredTargets,
                content.Summary.RootSetCompletion
                    == DependencyInspectionRootSetCompletion.Complete
                    && content.Summary.TraversalCompletion
                        == DependencyInspectionTraversalCompletion.Complete)
                .Content;
        return new(
            content with { EcosystemComposition = composition },
            inspection.Share,
            inspection.Diagnostics);
    }

    public static InspectionEnvelope<ProjectInputDocument>
        WithDependencyComposition(
            InspectionEnvelope<ProjectInputDocument> inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ProjectInputDocument content = inspection.Content;
        if (content.RestoredTraversal
            is not RestoredProjectDependencyTraversalResult.Available available)
        {
            return inspection;
        }

        RestoredProjectDependencyTraversal traversal = available.Value;
        DependencyGraphDocument graph = RestoredTraversalGraphProjection.Project(
            traversal, 1, content.DeclaredName);
        DependencyEcosystemCompositionDocument composition =
            InspectDependencyComposition(
                graph,
                [traversal.Root.Selection.TargetIdentity],
                traversal.IsComplete)
                .Content;
        return new(
            content with { EcosystemComposition = composition },
            inspection.Share,
            inspection.Diagnostics);
    }
}
