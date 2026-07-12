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

        return Build(
            entries.Select(static entry => entry.Path).ToList(),
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
        if (assemblyPaths.Count == 0)
            return null;

        if (assemblyPaths.Count == 1)
        {
            var single = AssemblyReader.ExtractApiSurface(assemblyPaths[0], includeAll);
            if (single is not null)
            {
                single.Name = name ?? Path.GetFileNameWithoutExtension(assemblyPaths[0]);
                single.Tfm = tfm;
            }
            return single;
        }

        var merged = new ApiSurface { Name = name, Tfm = tfm };
        foreach (var path in assemblyPaths)
        {
            var surface = AssemblyReader.ExtractApiSurface(path, includeAll);
            if (surface is null)
            {
                log?.Invoke($"  ! {Path.GetFileName(path)}: API surface unavailable");
                continue;
            }

            log?.Invoke($"  + {Path.GetFileNameWithoutExtension(path)}: {surface.PublicTypeCount} types");
            merged.Types.AddRange(surface.Types);
            merged.PublicTypeCount += surface.PublicTypeCount;
            merged.PublicMethodCount += surface.PublicMethodCount;
            merged.PublicPropertyCount += surface.PublicPropertyCount;
            merged.PublicEventCount += surface.PublicEventCount;
            merged.PublicFieldCount += surface.PublicFieldCount;
        }

        merged.Types = merged.Types.OrderBy(static type => type.FullName).ToList();
        return merged.Types.Count == 0 ? null : merged;
    }
}
