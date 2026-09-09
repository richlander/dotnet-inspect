using DotnetInspector.Models;
using DotnetInspector.Services;
using DotnetInspector.Views;
using ILInspector.CSharp;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal static class DependencyGraphProjection
{
    public static DependencyGraphDocument FromType(
        TypeDependencyResult result)
    {
        string root = result.MatchedType
            ?? throw new ArgumentException(
                "A type dependency graph requires a matched type.",
                nameof(result));
        var builder = new DependencyGraphBuilder(
            CSharpIdentifier.ContainRenderedText(root),
            DependencyGraphNodeKind.Type,
            TypeIdentity(root),
            CSharpIdentifier.ContainRenderedText(root));

        foreach (TypeDependencyRelationship relationship
            in result.Relationships)
        {
            int sourceId = builder.AddNode(
                DependencyGraphNodeKind.Type,
                TypeIdentity(relationship.SourceTypeName),
                CSharpIdentifier.ContainRenderedText(
                    relationship.SourceTypeName),
                DependencyGraphResolutionState.Resolved);
            int targetId = builder.AddNode(
                DependencyGraphNodeKind.Type,
                TypeIdentity(relationship.TargetTypeName),
                CSharpIdentifier.ContainRenderedText(
                    relationship.TargetTypeName),
                relationship.TargetResolved
                    ? DependencyGraphResolutionState.Resolved
                    : DependencyGraphResolutionState.Unresolved);
            builder.AddEdge(
                sourceId,
                targetId,
                relationship.Kind switch
                {
                    TypeDependencyRelationshipKind.BaseType =>
                        "base type",
                    _ => "interface",
                },
                relationship.Depth);
        }

        return builder.Build();
    }

    public static DependencyGraphDocument FromLibrary(
        LibraryDependencyGraphResult.Graph graph)
    {
        string rootPath = Path.GetFullPath(graph.AssemblyPath);
        var builder = new DependencyGraphBuilder(
            CSharpIdentifier.ContainRenderedText(
                graph.AssemblyName),
            DependencyGraphNodeKind.Library,
            rootPath,
            CSharpIdentifier.ContainRenderedText(graph.AssemblyName));
        var parentIdsByDepth = new Dictionary<int, int>
        {
            [-1] = builder.RootNodeId,
        };
        int unresolvedOccurrence = 0;

        foreach (AssemblyReferenceNode reference in graph.References)
        {
            if (!parentIdsByDepth.TryGetValue(
                reference.Depth - 1,
                out int parentId))
            {
                throw new InvalidOperationException(
                    "Library dependency traversal produced an invalid depth.");
            }

            string identity = reference.Path is { } path
                ? Path.GetFullPath(path)
                : $"unresolved:{parentId}:{reference.Name}:"
                    + unresolvedOccurrence++;
            int targetId = builder.AddNode(
                DependencyGraphNodeKind.Library,
                identity,
                CSharpIdentifier.ContainRenderedText(
                    LibraryInspectionView.ReferenceTreeText(reference)),
                reference.ResolutionFailure switch
                {
                    AssemblyReferenceResolutionFailure.Rejected =>
                        DependencyGraphResolutionState.Rejected,
                    AssemblyReferenceResolutionFailure.Unavailable =>
                        DependencyGraphResolutionState.Unresolved,
                    _ when reference.Path is null =>
                        DependencyGraphResolutionState.Unresolved,
                    _ => DependencyGraphResolutionState.Resolved,
                });
            builder.AddEdge(
                parentId,
                targetId,
                "assembly reference",
                reference.Depth);
            parentIdsByDepth[reference.Depth] = targetId;

            foreach (int depth in parentIdsByDepth.Keys
                .Where(depth => depth > reference.Depth)
                .ToArray())
            {
                parentIdsByDepth.Remove(depth);
            }
        }

        return builder.Build();
    }

    public static DependencyGraphDocument FromPackage(
        PackageDependencyGraphResult.Graph graph)
    {
        string rootLabel = PackageLabel(
            graph.ManifestPackageName,
            graph.ManifestVersion);
        var builder = new DependencyGraphBuilder(
            CSharpIdentifier.ContainRenderedText(graph.Title),
            DependencyGraphNodeKind.Package,
            PackageIdentity(graph.ManifestPackageName),
            rootLabel);
        AddPackageDependencies(
            builder,
            builder.RootNodeId,
            graph.Dependencies,
            depth: 0);
        return builder.Build();
    }

    private static void AddPackageDependencies(
        DependencyGraphBuilder builder,
        int parentId,
        IReadOnlyList<DependencyNode> dependencies,
        int depth)
    {
        foreach (DependencyNode dependency in dependencies)
        {
            int targetId = builder.AddNode(
                DependencyGraphNodeKind.Package,
                PackageIdentity(dependency.PackageId),
                PackageLabel(
                    dependency.PackageId,
                    dependency.Version,
                    dependency.Author),
                DependencyGraphResolutionState.Resolved);
            builder.AddEdge(
                parentId,
                targetId,
                "package dependency",
                depth);
            AddPackageDependencies(
                builder,
                targetId,
                dependency.Children,
                depth + 1);
        }
    }

    private static string TypeIdentity(string typeName) =>
        FqnParser.NormalizeTypeName(typeName);

    private static string PackageIdentity(string packageId) =>
        packageId;

    private static string PackageLabel(
        string packageName,
        string version,
        string? author = null) =>
        CSharpIdentifier.ContainRenderedText(
            string.IsNullOrWhiteSpace(version)
                ? packageName
                : string.IsNullOrWhiteSpace(author)
                    ? $"{packageName} {version}"
                    : $"{packageName} {version} [{author}]");
}
