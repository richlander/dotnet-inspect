using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Builds one API surface from an acquired assembly set.
/// </summary>
public static class AssemblySetSurfaceBuilder
{
    public static ApiSurface? Build(
        AssemblySet assemblySet,
        bool includeAll = false,
        Action<string>? log = null)
    {
        var entries = assemblySet.Assemblies;
        var packageName = entries.Count > 0
            && entries.All(static entry => entry.SourceKind == AssemblySetSourceKind.Package)
            && entries.All(entry => string.Equals(entry.Source, entries[0].Source, StringComparison.Ordinal))
                ? entries[0].Source
                : null;
        var tfms = entries
            .Select(static entry => entry.Tfm)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var tfm = tfms.Count == 1 ? tfms[0] : null;

        using var session =
            new AssemblySetResolutionSession(
                assemblySet,
                log);
        return session.BuildApiSurface(
            includeAll,
            packageName,
            tfm,
            log);
    }

    public static ApiSurface? Build(
        IReadOnlyList<string> assemblyPaths,
        bool includeAll = false,
        string? name = null,
        string? tfm = null,
        Action<string>? log = null)
    {
        using var session =
            new AssemblySetResolutionSession(
                assemblyPaths,
                log);
        return session.BuildApiSurface(
            includeAll,
            name
                ?? (assemblyPaths.Count == 1
                    ? Path.GetFileNameWithoutExtension(
                        assemblyPaths[0])
                    : null),
            tfm,
            log);
    }
}
