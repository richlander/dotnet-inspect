using System.Collections.ObjectModel;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// A live <see cref="ILInspector.Metadata.Corpus"/> together with the temporary extraction directories
/// backing it and the serializable <see cref="CorpusManifest"/> that describes it. Disposing releases
/// every owned extraction directory, so callers should keep it alive for as long as they operate within
/// the corpus and dispose it exactly once when done.
/// </summary>
public sealed class PopulatedCorpus : IDisposable
{
    private readonly IReadOnlyList<AssemblySet> _sets;
    private bool _disposed;

    internal PopulatedCorpus(
        IReadOnlyList<AssemblySet> sets,
        Corpus corpus,
        CorpusManifest manifest,
        IReadOnlyList<AssemblySetDiagnostic> diagnostics)
    {
        _sets = sets;
        Corpus = corpus;
        Manifest = manifest;
        Diagnostics = new ReadOnlyCollection<AssemblySetDiagnostic>([.. diagnostics]);
    }

    /// <summary>The resolved, closed set of assemblies to operate within.</summary>
    public Corpus Corpus { get; }

    /// <summary>The serializable recipe that describes <see cref="Corpus"/> and can repopulate an equivalent set.</summary>
    public CorpusManifest Manifest { get; }

    /// <summary>Diagnostics gathered while populating (missing directories, unusable packages, …).</summary>
    public IReadOnlyList<AssemblySetDiagnostic> Diagnostics { get; }

    /// <summary>Removes every temporary extraction directory owned by this corpus.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Best-effort per set: one failing Dispose must not strand the others' extraction directories.
        foreach (var set in _sets)
        {
            try { set.Dispose(); } catch { }
        }

        _disposed = true;
    }
}

/// <summary>
/// Turns the acquisition layer's <see cref="AssemblySet"/> into the metadata layer's closed-set
/// <see cref="Corpus"/> and its serializable <see cref="CorpusManifest"/>. This is the seam that makes
/// <see cref="AssemblySetResolver"/> a corpus <em>producer</em>: acquisition (packages, platforms,
/// projects, downloads) stays here, while the resulting corpus carries no feed reference and operates
/// entirely offline.
/// </summary>
public static class CorpusProducer
{
    /// <summary>Maps a resolved <see cref="AssemblySet"/> to a closed-set <see cref="Corpus"/>.</summary>
    public static Corpus ToCorpus(AssemblySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return ToCorpus(set.Assemblies);
    }

    /// <summary>Maps resolved assembly entries to a closed-set <see cref="Corpus"/>, one member per entry.</summary>
    public static Corpus ToCorpus(IEnumerable<AssemblySetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new Corpus(entries.Select(static entry => new CorpusMember
        {
            AssemblyPath = entry.Path,
            Source = entry.Source,
            Version = entry.Version,
            Tfm = entry.Tfm,
        }));
    }

    /// <summary>Builds the serializable manifest describing a resolved <see cref="AssemblySet"/>.</summary>
    public static CorpusManifest ToManifest(AssemblySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return ToManifest(set.Assemblies);
    }

    /// <summary>
    /// Builds the serializable manifest describing resolved assembly entries. A package or whole platform
    /// framework collapses to one logical entry (its many assemblies share one package/framework
    /// name+version+TFM). Every other source — loose assembly, project, directory, and an individually
    /// resolved platform assembly — is normalized to a reload-by-<em>path</em>
    /// <see cref="AssemblySetSourceKind.Assembly"/> entry (Id = the assembly's full path) so the manifest
    /// is always repopulatable and working-directory independent. Entry order follows first appearance;
    /// duplicates are removed.
    /// </summary>
    public static CorpusManifest ToManifest(IEnumerable<AssemblySetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var manifestEntries = new List<CorpusManifestEntry>();
        var seen = new HashSet<(AssemblySetSourceKind Kind, string Id, string? Version, string? Tfm)>();

        foreach (var entry in entries)
        {
            var manifestEntry = IsLogicalOrigin(entry.SourceKind)
                ? new CorpusManifestEntry(entry.SourceKind, entry.Source, entry.Version, entry.Tfm)
                : new CorpusManifestEntry(AssemblySetSourceKind.Assembly, Path.GetFullPath(entry.Path), entry.Version, entry.Tfm);

            if (seen.Add((manifestEntry.Kind, manifestEntry.Id, manifestEntry.Version, manifestEntry.Tfm)))
                manifestEntries.Add(manifestEntry);
        }

        return new CorpusManifest { Entries = manifestEntries };
    }

    /// <summary>
    /// Resolves a request into a live corpus. The single explicit network/extraction step for a corpus:
    /// the returned <see cref="PopulatedCorpus"/> owns the extraction directories and, once returned,
    /// searches over it are offline. Throws <see cref="InvalidOperationException"/> (surfacing any
    /// diagnostics) when the request resolves no assemblies, so an all-failed acquisition cannot return a
    /// success-shaped empty corpus.
    /// </summary>
    public static async Task<PopulatedCorpus> PopulateAsync(
        HttpClient httpClient,
        AssemblySetRequest request,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);

        var set = await AssemblySetResolver.CollectAsync(httpClient, request, log).ConfigureAwait(false);
        try
        {
            if (set.Assemblies.Count == 0)
                throw new InvalidOperationException(DescribeEmptyResolution(set.Diagnostics));

            return new PopulatedCorpus([set], ToCorpus(set), ToManifest(set), set.Diagnostics);
        }
        catch
        {
            set.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Repopulates a live corpus from a previously serialized manifest, resolving each entry with its own
    /// pinned TFM. The returned <see cref="PopulatedCorpus.Manifest"/> is rebuilt from what actually
    /// resolved, so it always describes the returned corpus. Aggregated diagnostics surface any entry
    /// that could not be resolved rather than silently shrinking the set; if nothing resolves at all the
    /// method throws <see cref="InvalidOperationException"/> rather than returning an empty corpus.
    /// </summary>
    public static async Task<PopulatedCorpus> PopulateFromManifestAsync(
        HttpClient httpClient,
        CorpusManifest manifest,
        NuGetSourceOptions? sourceOptions = null,
        string tempDirPrefix = "inspect-corpus",
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Entries.Count == 0)
        {
            throw new ArgumentException(
                "Corpus manifest has no entries; cannot populate a corpus from an empty manifest.",
                nameof(manifest));
        }

        var sets = new List<AssemblySet>();
        var entries = new List<AssemblySetEntry>();
        var diagnostics = new List<AssemblySetDiagnostic>();

        try
        {
            foreach (var entry in manifest.Entries)
            {
                var request = BuildEntryRequest(entry, sourceOptions, tempDirPrefix);
                var set = await AssemblySetResolver.CollectAsync(httpClient, request, log).ConfigureAwait(false);
                sets.Add(set);
                entries.AddRange(set.Assemblies);
                diagnostics.AddRange(set.Diagnostics);
            }

            if (entries.Count == 0)
                throw new InvalidOperationException(DescribeEmptyResolution(diagnostics));

            return new PopulatedCorpus(sets, ToCorpus(entries), ToManifest(entries), diagnostics);
        }
        catch
        {
            foreach (var set in sets)
            {
                try { set.Dispose(); } catch { }
            }
            throw;
        }
    }

    private static string DescribeEmptyResolution(IReadOnlyList<AssemblySetDiagnostic> diagnostics)
    {
        var message = "Populating the corpus resolved no assemblies.";
        if (diagnostics.Count == 0)
            return message;

        return message + " " + string.Join("; ", diagnostics.Select(static d => $"{d.Severity}: {d.Message}"));
    }

    private static bool IsLogicalOrigin(AssemblySetSourceKind kind) => kind switch
    {
        // A package and a whole platform framework each fan out to many assemblies that share one
        // logical name+version+TFM, so they collapse to a single re-populatable entry. Every other kind
        // (including a single platform assembly, whose Source records its framework rather than the
        // requested assembly) is captured individually by path.
        AssemblySetSourceKind.Package => true,
        AssemblySetSourceKind.PlatformFramework => true,
        _ => false,
    };

    private static AssemblySetRequest BuildEntryRequest(
        CorpusManifestEntry entry,
        NuGetSourceOptions? sourceOptions,
        string tempDirPrefix)
    {
        var request = new AssemblySetRequest
        {
            Tfm = entry.Tfm,
            SourceOptions = sourceOptions,
            TempDirPrefix = tempDirPrefix,
        };

        return entry.Kind switch
        {
            AssemblySetSourceKind.Package => request with { Packages = [PackageSpec(entry)] },
            AssemblySetSourceKind.PlatformFramework => request with { PlatformFrameworks = [entry.Id] },
            AssemblySetSourceKind.PlatformAssembly => request with { PlatformAssemblies = [entry.Id] },
            AssemblySetSourceKind.Assembly => request with { Assemblies = [entry.Id] },
            AssemblySetSourceKind.Project => request with { Projects = [entry.Id] },
            AssemblySetSourceKind.Directory => request with { Directories = [entry.Id] },
            _ => throw new NotSupportedException($"Unsupported corpus manifest entry kind: {entry.Kind}."),
        };
    }

    private static string PackageSpec(CorpusManifestEntry entry) =>
        string.IsNullOrEmpty(entry.Version) ? entry.Id : $"{entry.Id}@{entry.Version}";
}
