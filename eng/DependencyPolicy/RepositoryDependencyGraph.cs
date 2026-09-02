using System.Collections.Immutable;

namespace DependencyPolicy;

internal sealed record ProjectDependencyNode(
    string ProjectName,
    string ProjectPath,
    ImmutableArray<string> ProjectReferences,
    string? AssemblyName,
    ImmutableArray<string> AssemblyReferences);

internal sealed record RepositoryDependencyGraph(
    ImmutableDictionary<string, ProjectDependencyNode> Projects,
    ImmutableHashSet<string> RepositoryAssemblyNames,
    ImmutableHashSet<string> PlatformAssemblyNames)
{
    internal static RepositoryDependencyGraph Create(
        IEnumerable<ProjectDependencyNode> projects,
        IEnumerable<string>? platformAssemblyNames = null)
    {
        ImmutableDictionary<string, ProjectDependencyNode> projectMap =
            projects.ToImmutableDictionary(
                project => project.ProjectName,
                StringComparer.Ordinal);
        return new(
            projectMap,
            projectMap.Values
                .Where(project => project.AssemblyName is not null)
                .Select(project => project.AssemblyName!)
                .ToImmutableHashSet(StringComparer.Ordinal),
            (platformAssemblyNames ?? [])
                .ToImmutableHashSet(StringComparer.Ordinal));
    }
}
