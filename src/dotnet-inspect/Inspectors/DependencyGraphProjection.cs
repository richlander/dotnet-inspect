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
            result.MatchedTypeIdentity ?? root,
            CSharpIdentifier.ContainRenderedText(root));

        foreach (TypeDependencyRelationship relationship
            in result.Relationships)
        {
            int sourceId = builder.AddNode(
                DependencyGraphNodeKind.Type,
                relationship.SourceTypeIdentity,
                CSharpIdentifier.ContainRenderedText(
                    relationship.SourceTypeName),
                DependencyGraphResolutionState.Resolved);
            int targetId = builder.AddNode(
                DependencyGraphNodeKind.Type,
                relationship.TargetTypeIdentity,
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
        var unresolvedIdentities =
            new Dictionary<
                UnresolvedLibraryReferenceKey,
                string>(
                UnresolvedLibraryReferenceKeyComparer.Instance);
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
                : UnresolvedLibraryIdentity(
                    parentId,
                    reference,
                    unresolvedIdentities);
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

    public static DependencyGraphDocument FromLibrary(
        LibraryDependencyGraphResult.Empty empty) =>
        new DependencyGraphBuilder(
            CSharpIdentifier.ContainRenderedText(
                empty.AssemblyName),
            DependencyGraphNodeKind.Library,
            Path.GetFullPath(empty.AssemblyPath),
            CSharpIdentifier.ContainRenderedText(
                empty.AssemblyName))
        .Build();

    public static DependencyGraphDocument FromPackage(
        PackageDependencyGraphResult.Graph graph)
    {
        string rootLabel = PackageLabel(
            graph.ManifestPackageName,
            graph.ManifestVersion);
        var builder = new DependencyGraphBuilder(
            CSharpIdentifier.ContainRenderedText(graph.Title),
            DependencyGraphNodeKind.Package,
            PackageIdentity(
                graph.ManifestPackageName,
                graph.ManifestVersion),
            rootLabel);
        AddPackageDependencies(
            builder,
            builder.RootNodeId,
            graph.Dependencies,
            depth: 0);
        return builder.Build();
    }

    public static DependencyGraphDocument FromPackage(
        PackageDependencyGraphResult.Empty empty) =>
        new DependencyGraphBuilder(
            CSharpIdentifier.ContainRenderedText(
                PackageTitle(
                    empty.PackageName,
                    empty.Version)),
            DependencyGraphNodeKind.Package,
            PackageIdentity(
                empty.ManifestPackageName,
                empty.ManifestVersion),
            PackageLabel(
                empty.ManifestPackageName,
                empty.ManifestVersion))
        .Build();

    private static void AddPackageDependencies(
        DependencyGraphBuilder builder,
        int parentId,
        IReadOnlyList<DependencyNode> dependencies,
        int depth)
    {
        foreach (DependencyNode dependency in dependencies)
        {
            string version =
                dependency.ResolvedVersion
                ?? dependency.Version;
            int targetId = builder.AddNode(
                DependencyGraphNodeKind.Package,
                PackageIdentity(dependency.PackageId, version),
                PackageLabel(
                    dependency.PackageId,
                    version,
                    dependency.Author),
                dependency.Resolution switch
                {
                    DependencyNodeResolutionState.Unresolved =>
                        DependencyGraphResolutionState.Unresolved,
                    _ => DependencyGraphResolutionState.Resolved,
                });
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

    private static string PackageIdentity(
        string packageId,
        string version) =>
        DependencyResolutionService.PackageTraversalIdentity(
            packageId,
            version);

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

    private static string PackageTitle(
        string packageName,
        string version) =>
        string.IsNullOrWhiteSpace(version)
            ? packageName
            : $"{packageName} ({version})";

    private static string UnresolvedLibraryIdentity(
        int parentId,
        AssemblyReferenceNode reference,
        Dictionary<UnresolvedLibraryReferenceKey, string>
            identities)
    {
        var key = new UnresolvedLibraryReferenceKey(
            parentId,
            new AssemblyReferenceIdentity(
                reference.Name,
                Version.TryParse(
                    reference.Version,
                    out Version? version)
                    ? version
                    : null,
                reference.Culture,
                reference.PublicKeyToken));
        if (identities.TryGetValue(key, out string? identity))
            return identity;

        identity = $"unresolved:{identities.Count}";
        identities.Add(key, identity);
        return identity;
    }

    private readonly record struct UnresolvedLibraryReferenceKey(
        int ParentId,
        AssemblyReferenceIdentity Reference);

    private sealed class UnresolvedLibraryReferenceKeyComparer :
        IEqualityComparer<UnresolvedLibraryReferenceKey>
    {
        public static UnresolvedLibraryReferenceKeyComparer Instance
            { get; } = new();

        public bool Equals(
            UnresolvedLibraryReferenceKey x,
            UnresolvedLibraryReferenceKey y) =>
            x.ParentId == y.ParentId
            && AssemblyReferenceIdentity.EquivalentComparer.Equals(
                x.Reference,
                y.Reference);

        public int GetHashCode(
            UnresolvedLibraryReferenceKey obj) =>
            HashCode.Combine(
                obj.ParentId,
                AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
                    obj.Reference));
    }
}
