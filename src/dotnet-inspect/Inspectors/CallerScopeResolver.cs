using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Resolves member caller-scope flags (<c>--bin</c>/<c>--directory</c>, <c>--project</c>, and
/// <c>--caller-package</c>) into a deduplicated list of on-disk assembly paths to scan for
/// cross-assembly callers and Call Graph traversal, mirroring the scope semantics of the
/// <c>find</c> command.
/// </summary>
public static class CallerScopeResolver
{
    /// <summary>
    /// Expands the requested directories, projects, and packages into assembly paths, excluding
    /// <paramref name="ownAssemblyPath"/> (already scanned as the member's own assembly) and
    /// de-duplicating by physical file identity when the host exposes it, with ordinal normalized
    /// paths as the conservative fallback. Gated by
    /// <c>ResolveAsync_HardLinkedAssembliesAreScannedOnce</c> and
    /// <c>ResolveAsync_CaseDistinctWindowsAssembliesRemainDistinct</c>.
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
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var seenFiles = new HashSet<PhysicalFileIdentity>();

        if (ownAssemblyPath != null)
            Remember(Path.GetFullPath(ownAssemblyPath));

        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full) || !Remember(full))
                return;

            result.Add(full);
        }

        bool Remember(string full)
        {
            if (!seenPaths.Add(full))
                return false;

            if (!PhysicalFileIdentityProvider.TryGet(
                full,
                out PhysicalFileIdentity identity,
                out string? failure))
            {
                logger.Log(
                    $"Physical file identity unavailable for '{full}': {failure}. " +
                    "Using ordinal full-path identity.");
                return true;
            }

            return seenFiles.Add(identity);
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
