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
        ImmutableHashSet<string> repositoryAssemblies = projectMap.Values
            .Where(project => project.AssemblyName is not null)
            .Select(project => project.AssemblyName!)
            .ToImmutableHashSet(StringComparer.Ordinal);
        ImmutableHashSet<string> platformAssemblies =
            (platformAssemblyNames ?? [])
                .ToImmutableHashSet(StringComparer.Ordinal);
        string[] collisions = repositoryAssemblies
            .Intersect(platformAssemblies, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (collisions.Length != 0)
        {
            throw new DependencyPolicyException(
                "Governed repository assembly names collide with platform "
                + $"assemblies: [{string.Join(", ", collisions)}].");
        }

        return new(
            projectMap,
            repositoryAssemblies,
            platformAssemblies);
    }
}
