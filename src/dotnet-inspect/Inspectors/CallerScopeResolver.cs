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
        // Keyed on the spelling the entry actually has on disk, compared exactly. Neither plain
        // comparer is correct on its own: folding case merges two distinct files on a
        // case-sensitive volume and silently drops the second, while comparing raw strings exactly
        // splits one file on a case-insensitive volume and scans it twice, reporting every call
        // site in it twice. Both were shipped in turn during review of #3419. Asking the
        // filesystem which spelling it holds settles it without assuming a platform rule.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Canonicalizing a directory costs one filesystem lookup per segment, and a scope is
        // overwhelmingly many files under a few directories, so the answers are memoized.
        var directorySpellings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (ownAssemblyPath != null)
            seen.Add(OnDiskSpelling(Path.GetFullPath(ownAssemblyPath), directorySpellings));

        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full))
                return;

            if (!seen.Add(OnDiskSpelling(full, directorySpellings)))
                return;

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

    /// <summary>
    /// The spelling <paramref name="fullPath"/> actually has on disk, so that two ways of naming
    /// one file compare equal while two genuinely distinct files do not.
    ///
    /// <para>Case sensitivity is a property of the volume and, on Windows, of the individual
    /// directory — not of the operating system. Rather than guess, this asks the filesystem to
    /// match each segment and takes the directory entry it yields: where the volume resolved a
    /// differently cased spelling onto an existing entry, that entry's own spelling is the
    /// canonical one; where the exact name exists, the segment is already canonical.</para>
    ///
    /// <para>A segment this cannot resolve is left as written. Failing to canonicalize can only
    /// cost a deduplication — one file scanned twice — while merging two distinct files drops a
    /// caller outright, so the failure direction is the survivable one.</para>
    /// </summary>
    static string OnDiskSpelling(string fullPath, Dictionary<string, string> directorySpellings)
    {
        var directory = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name))
            return fullPath;

        var canonicalDirectory = CanonicalDirectory(directory, directorySpellings);
        return Path.Combine(canonicalDirectory, MatchedEntry(canonicalDirectory, name, files: true));
    }

    /// <summary>
    /// <paramref name="directory"/> with every segment given the spelling it holds on disk,
    /// memoized in <paramref name="memo"/> because a scope's files share few directories.
    /// </summary>
    static string CanonicalDirectory(string directory, Dictionary<string, string> memo)
    {
        if (memo.TryGetValue(directory, out var cached))
            return cached;

        var parent = Path.GetDirectoryName(directory);
        var leaf = Path.GetFileName(directory);

        string canonical;
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
        {
            // A root, or a segment with no name of its own, has no parent to enumerate it from.
            canonical = directory;
        }
        else
        {
            var canonicalParent = CanonicalDirectory(parent, memo);
            canonical = Path.Combine(canonicalParent, MatchedEntry(canonicalParent, leaf, files: false));
        }

        memo[directory] = canonical;
        return canonical;
    }

    /// <summary>
    /// The name of the single entry of <paramref name="parent"/> that the filesystem matches
    /// against <paramref name="name"/>, or <paramref name="name"/> unchanged when there is none.
    /// </summary>
    static string MatchedEntry(string parent, string name, bool files)
    {
        // The name is a literal, but it is being handed to a pattern-matching API. Windows forbids
        // both wildcard characters in a file name; other platforms do not, and a name carrying one
        // would match entries it does not denote.
        if (name.AsSpan().ContainsAny('*', '?'))
            return name;

        try
        {
            foreach (var entry in files
                ? Directory.EnumerateFiles(parent, name)
                : Directory.EnumerateDirectories(parent, name))
            {
                return Path.GetFileName(entry);
            }
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or System.Security.SecurityException)
        {
            return name;
        }

        return name;
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
