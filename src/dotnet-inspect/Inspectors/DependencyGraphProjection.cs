using System.Collections.Immutable;
using System.Globalization;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Inspectors;

internal static class DependencyGraphProjection
{
    internal static DependencyGraphDocument Type(
        TypeDependencyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.MatchedType is null)
            throw new ArgumentException(
                "A type dependency graph requires a matched root.",
                nameof(result));

        var builder = new Builder(
            new DependencyGraphNodeIdentity.Type(result.MatchedType),
            Field(result.MatchedType));
        foreach (TypeDependencyRelationship relationship in
                 result.Relationships.OrderBy(
                     static relationship => relationship.Ordinal))
        {
            builder.AddEdge(
                new DependencyGraphNodeIdentity.Type(
                    relationship.SourceTypeName),
                Field(relationship.SourceTypeName),
                new DependencyGraphNodeIdentity.Type(
                    relationship.TargetTypeName),
                Field(relationship.TargetTypeName),
                relationship.Kind switch
                {
                    TypeDependencyRelationshipKind.BaseType => "base-type",
                    _ => "interface",
                },
                DependencyGraphResolutionState.Declared,
                evidenceIdentity: null);
        }

        return builder.Build();
    }

    internal static DependencyGraphDocument Library(
        LibraryDependencyGraphResult.Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        LibraryMetadataService.AssemblyReferenceGraph referenceGraph =
            graph.ReferenceGraph;
        var builder = new Builder(
            new DependencyGraphNodeIdentity.Library(referenceGraph.Root),
            Field(graph.AssemblyName));

        foreach (LibraryMetadataService.AssemblyReferenceRelationship
                 relationship in referenceGraph.Relationships.OrderBy(
                     static relationship => relationship.Ordinal))
        {
            builder.AddEdge(
                new DependencyGraphNodeIdentity.Library(
                    relationship.Source),
                LibraryLabel(
                    relationship.Source,
                    FindNode(referenceGraph.Nodes, relationship.Source)),
                new DependencyGraphNodeIdentity.Library(
                    relationship.Target),
                LibraryLabel(
                    relationship.Target,
                    FindNode(referenceGraph.Nodes, relationship.Target)),
                "assembly-reference",
                relationship.ResolutionFailure switch
                {
                    AssemblyReferenceResolutionFailure.Unavailable =>
                        DependencyGraphResolutionState.Unavailable,
                    AssemblyReferenceResolutionFailure.Rejected =>
                        DependencyGraphResolutionState.Rejected,
                    null when relationship.IsResolved =>
                        DependencyGraphResolutionState.Resolved,
                    null => DependencyGraphResolutionState.Declared,
                    _ => throw new InvalidOperationException(
                        "Unknown assembly reference resolution failure."),
                },
                new DependencyGraphEvidenceIdentity.AssemblyReference(
                    relationship.RequestedTarget));
        }

        return builder.Build();
    }

    internal static DependencyGraphDocument Library(
        LibraryDependencyGraphResult.Empty empty)
    {
        ArgumentNullException.ThrowIfNull(empty);
        return new Builder(
            new DependencyGraphNodeIdentity.Library(empty.Identity),
            Field(empty.AssemblyName)).Build();
    }

    internal static DependencyGraphDocument Package(
        PackageDependencyGraphResult.Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        PackageDependencyGraph dependencyGraph = graph.DependencyGraph;
        var rootIdentity = new PackageDependencyIdentity(
            graph.ManifestPackageName,
            graph.ManifestVersion);
        var builder = new Builder(
            PackageIdentity(rootIdentity),
            Field(graph.Title));

        foreach (PackageDependencyRelationship relationship in
                 dependencyGraph.Relationships.OrderBy(
                     static relationship => relationship.Ordinal))
        {
            builder.AddEdge(
                PackageIdentity(relationship.Source),
                PackageLabel(
                    relationship.Source,
                    FindNode(
                        dependencyGraph.Nodes,
                        relationship.Source)?.Author),
                PackageIdentity(relationship.Target),
                PackageLabel(
                    relationship.Target,
                    FindNode(
                        dependencyGraph.Nodes,
                        relationship.Target)?.Author),
                "package-dependency",
                relationship.Resolution switch
                {
                    PackageDependencyResolutionState.Resolved =>
                        DependencyGraphResolutionState.Resolved,
                    PackageDependencyResolutionState.Unavailable =>
                        DependencyGraphResolutionState.Unavailable,
                    _ => DependencyGraphResolutionState.Declared,
                },
                new DependencyGraphEvidenceIdentity
                    .PackageVersionConstraint(
                        Field(relationship.VersionConstraint)));
        }

        return builder.Build();
    }

    internal static DependencyGraphDocument Package(
        PackageDependencyGraphResult.Empty empty)
    {
        ArgumentNullException.ThrowIfNull(empty);
        var rootIdentity = new PackageDependencyIdentity(
            empty.ManifestPackageName,
            empty.ManifestVersion);
        return new Builder(
            PackageIdentity(rootIdentity),
            Field($"{empty.PackageName} ({empty.Version})")).Build();
    }

    private static DependencyGraphNodeIdentity.Package PackageIdentity(
        PackageDependencyIdentity identity) =>
        new(identity.PackageId, identity.Version);

    private static PackageDependencyGraphNode? FindNode(
        IReadOnlyList<PackageDependencyGraphNode> nodes,
        PackageDependencyIdentity identity) =>
        nodes.FirstOrDefault(node =>
            StringComparer.OrdinalIgnoreCase.Equals(
                node.Identity.PackageId,
                identity.PackageId)
            && StringComparer.OrdinalIgnoreCase.Equals(
                node.Identity.Version,
                identity.Version));

    private static AssemblyReferenceNode? FindNode(
        IReadOnlyList<AssemblyReferenceNode> nodes,
        ManagedMetadataIdentity identity) =>
        identity is ManagedMetadataIdentity.Assembly assembly
            ? nodes.FirstOrDefault(node =>
                (node.ResolvedIdentity
                    ?? new AssemblyReferenceIdentity(
                        node.Name,
                        Version.TryParse(
                            node.Version,
                            out Version? version)
                            ? version
                            : null,
                        Culture: null,
                        node.PublicKeyToken))
                .IsEquivalentTo(assembly.Identity))
            : null;

    private static InertString LibraryLabel(
        ManagedMetadataIdentity identity,
        AssemblyReferenceNode? node)
    {
        if (identity is ManagedMetadataIdentity.Module module)
            return Field(module.Name);

        var assembly = identity as ManagedMetadataIdentity.Assembly
            ?? throw new InvalidOperationException(
                "Unknown managed metadata identity.");
        if (node is null)
            return Field(AssemblyIdentityFormatter.Format(assembly.Identity));

        string version = assembly.Identity.Version?.ToString() ?? "";
        string label = !string.IsNullOrEmpty(node.Company)
            ? $"{assembly.Identity.Name} {version} [{node.Company}]"
            : $"{assembly.Identity.Name} {version}";
        if (node.ResolutionFailure is { } failure)
        {
            label +=
                $" ({failure.ToString().ToLowerInvariant()})";
        }
        return Field(label);
    }

    private static InertString PackageLabel(
        PackageDependencyIdentity identity,
        string? author)
    {
        InertString id = Field(identity.PackageId);
        InertString version = Field(identity.Version);
        return string.IsNullOrEmpty(author)
            ? InertString.Format(
                TextPolicy.Field,
                $"{id} {version}")
            : InertString.Format(
                TextPolicy.Field,
                $"{id} {version} [{Field(author)}]");
    }

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);

    private sealed class Builder(
        DependencyGraphNodeIdentity rootIdentity,
        InertString rootLabel)
    {
        private readonly List<DependencyGraphNode> _nodes =
        [
            new(0, rootIdentity, rootLabel),
        ];
        private readonly Dictionary<string, int> _nodeIds =
            new(StringComparer.Ordinal)
            {
                [Key(rootIdentity)] = 0,
            };
        private readonly List<PendingEdge> _edges = [];

        internal void AddEdge(
            DependencyGraphNodeIdentity source,
            InertString sourceLabel,
            DependencyGraphNodeIdentity target,
            InertString targetLabel,
            string relationship,
            DependencyGraphResolutionState resolution,
            DependencyGraphEvidenceIdentity? evidenceIdentity)
        {
            int sourceId = AddNode(source, sourceLabel);
            int targetId = AddNode(target, targetLabel);
            _edges.Add(
                new PendingEdge(
                    sourceId,
                    targetId,
                    relationship,
                    resolution,
                    evidenceIdentity));
        }

        internal DependencyGraphDocument Build()
        {
            Dictionary<int, int> distances = MinimumDistances();
            return new DependencyGraphDocument(
                [new DependencyGraphRootOccurrence(1, 0)],
                [.. _nodes],
                [
                    .. _edges.Select((edge, index) =>
                        new DependencyGraphEdge(
                            index,
                            edge.SourceNodeId,
                            edge.TargetNodeId,
                            edge.Relationship,
                            [1],
                            distances.TryGetValue(
                                edge.SourceNodeId,
                                out int sourceDepth)
                                ? sourceDepth + 1
                                : 0,
                            edge.Resolution,
                            edge.EvidenceIdentity)),
                ]);
        }

        private int AddNode(
            DependencyGraphNodeIdentity identity,
            InertString label)
        {
            string key = Key(identity);
            if (_nodeIds.TryGetValue(key, out int id))
                return id;

            id = _nodes.Count;
            _nodeIds.Add(key, id);
            _nodes.Add(new DependencyGraphNode(id, identity, label));
            return id;
        }

        private Dictionary<int, int> MinimumDistances()
        {
            var distances = new Dictionary<int, int> { [0] = 0 };
            var pending = new Queue<int>();
            pending.Enqueue(0);
            while (pending.Count > 0)
            {
                int source = pending.Dequeue();
                int distance = distances[source] + 1;
                foreach (PendingEdge edge in _edges.Where(
                             edge => edge.SourceNodeId == source))
                {
                    if (distances.TryGetValue(
                            edge.TargetNodeId,
                            out int previous)
                        && previous <= distance)
                    {
                        continue;
                    }

                    distances[edge.TargetNodeId] = distance;
                    pending.Enqueue(edge.TargetNodeId);
                }
            }

            return distances;
        }

        private static string Key(DependencyGraphNodeIdentity identity) =>
            identity switch
            {
                DependencyGraphNodeIdentity.Type type =>
                    "type\0" + type.Name,
                DependencyGraphNodeIdentity.Library library =>
                    LibraryKey(library.Identity),
                DependencyGraphNodeIdentity.Package package =>
                    "package\0"
                    + package.Id.ToUpperInvariant()
                    + "\0"
                    + package.Version.ToUpperInvariant(),
                _ => throw new InvalidOperationException(
                    "Unknown dependency graph node identity."),
            };

        private static string LibraryKey(
            ManagedMetadataIdentity identity) =>
            identity switch
            {
                ManagedMetadataIdentity.Assembly assembly =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"library\0assembly\0"
                        + $"{assembly.Identity.Name.ToUpperInvariant()}\0"
                        + $"{assembly.Identity.Version}\0"
                        + $"{(assembly.Identity.Culture ?? "").ToUpperInvariant()}\0"
                        + $"{(assembly.Identity.PublicKeyToken ?? "").ToUpperInvariant()}"),
                ManagedMetadataIdentity.Module module =>
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"library\0module\0"
                        + $"{module.Name.ToUpperInvariant()}\0"
                        + $"{module.ModuleVersionId:D}"),
                _ => throw new InvalidOperationException(
                    "Unknown managed metadata identity."),
            };

        private sealed record PendingEdge(
            int SourceNodeId,
            int TargetNodeId,
            string Relationship,
            DependencyGraphResolutionState Resolution,
            DependencyGraphEvidenceIdentity? EvidenceIdentity);
    }
}
