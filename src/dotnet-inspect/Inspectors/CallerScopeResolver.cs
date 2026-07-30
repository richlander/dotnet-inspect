using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Resolves member caller-scope flags (<c>--bin</c>/<c>--directory</c>, <c>--project</c>, and
/// <c>--caller-package</c>) into a deduplicated list of on-disk assembly paths to scan for
/// inbound callers, mirroring the scope semantics of the <c>find</c> command.
/// </summary>
public static class CallerScopeResolver
{
    /// <summary>
    /// Expands the requested directories, projects, and packages into assembly paths, excluding
    /// <paramref name="ownAssemblyPath"/> (already scanned as the member's own assembly) and
    /// de-duplicating by normalized full path.
    /// </summary>
    public static async Task<CallerScopeAssemblySet> ResolveAsync(
        IReadOnlyList<string> directories,
        IReadOnlyList<string> projects,
        IReadOnlyList<string> packages,
        string? tfm,
        string? ownAssemblyPath,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var result = new List<string>();
        // Ordinal: these are full paths, not assembly names. Case-folding them merges two files
        // that a case-sensitive volume keeps distinct, and the merge drops the second — so a real
        // caller is never scanned (#3419 review of 32951519).
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (ownAssemblyPath != null)
            seen.Add(Path.GetFullPath(ownAssemblyPath));

        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (seen.Add(full) && File.Exists(full))
                result.Add(full);
        }

        var assemblySet = await AssemblySetResolver.CollectAsync(
            httpClient,
            new AssemblySetRequest
            {
                Packages = packages,
                Projects = projects,
                Directories = directories,
                Tfm = tfm,
                TempDirPrefix = "inspect-caller",
            },
            logger.Log);

        AssemblySetDiagnosticWriter.Write(assemblySet);

        foreach (var assembly in assemblySet.Assemblies)
            Add(assembly.Path);

        return new CallerScopeAssemblySet(result, assemblySet);
    }
}

public sealed class CallerScopeAssemblySet : IDisposable
{
    private readonly AssemblySet _assemblySet;

    internal CallerScopeAssemblySet(IReadOnlyList<string> assemblies, AssemblySet assemblySet)
    {
        Assemblies = assemblies;
        _assemblySet = assemblySet;
    }

    public IReadOnlyList<string> Assemblies { get; }

    public void Dispose() => _assemblySet.Dispose();
}
