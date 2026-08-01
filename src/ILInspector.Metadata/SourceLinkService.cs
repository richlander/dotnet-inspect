using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSourceGenerationOptions(AllowDuplicateProperties = false)]
internal partial class SourceLinkJsonContext : JsonSerializerContext;

/// <summary>
/// High-level service for SourceLink queries over a .NET assembly.
/// Wraps <see cref="PdbContext"/> and provides cached type-to-source resolution.
/// </summary>
public class SourceLinkService : IDisposable
{
    /// <summary>
    /// Process-wide cache implementation for the SourceLink type→file index, wired by
    /// the tool tier at startup. This is the dependency-inversion seam that keeps the
    /// engine free of a tool-tier cache dependency: the engine defines the cache shape
    /// it needs (<see cref="ISourceLinkIndexCache"/>); the tool supplies it. Null means
    /// no persistence — the index is rebuilt from PDB data on each open.
    /// </summary>
    public static ISourceLinkIndexCache? DefaultCache { get; set; }

    private readonly PdbContext _context;
    private readonly ISourceLinkIndexCache? _cache;
    private IReadOnlyList<SourceDocument>? _trackedFiles;
    private Dictionary<string, string[]>? _typeFileIndex;

    private SourceLinkService(PdbContext context, ISourceLinkIndexCache? cache)
    {
        _context = context;
        _cache = cache;
    }

    /// <summary>
    /// Opens an assembly and probes for PDB (embedded, then standalone adjacent).
    /// After return, check <see cref="NeedsPdb"/> to see if the caller should download a PDB.
    /// </summary>
    public static SourceLinkService Open(string assemblyPath, Action<string>? log = null)
        => Open(assemblyPath, log, cache: null);

    /// <summary>
    /// Opens an assembly with an explicit index cache. Prefer passing a cache in tests to
    /// avoid the process-wide <see cref="DefaultCache"/>; when <paramref name="cache"/> is
    /// null, <see cref="DefaultCache"/> is used.
    /// </summary>
    public static SourceLinkService Open(string assemblyPath, Action<string>? log, ISourceLinkIndexCache? cache)
    {
        var context = PdbContext.Open(assemblyPath, log);
        return new SourceLinkService(context, cache ?? DefaultCache);
    }

    /// <summary>
    /// Opens an assembly with its complete PE image prefetched for shared
    /// parallel body analysis.
    /// </summary>
    public static SourceLinkService OpenPrefetched(
        string assemblyPath,
        Action<string>? log = null)
    {
        var context = PdbContext.OpenPrefetched(assemblyPath, log);
        return new SourceLinkService(context, DefaultCache);
    }

    /// <summary>
    /// The underlying PdbContext for PE/PDB plumbing not covered by this service.
    /// </summary>
    public PdbContext Context => _context;

    // --- PDB management ---

    /// <summary>Whether a PDB is loaded (embedded or external).</summary>
    public bool HasPdb => _context.HasPdb;

    /// <summary>Whether a PDB needs to be downloaded from a symbol server.</summary>
    public bool NeedsPdb => _context.NeedsPdb;

    /// <summary>
    /// Loads an external PDB file (e.g. downloaded from a symbol server).
    /// Invalidates any cached index.
    /// </summary>
    public void LoadPdb(string pdbPath, string? location = null, string? symbolServer = null)
    {
        _context.LoadPdbFromFile(pdbPath, location, symbolServer);
        _trackedFiles = null;
        _typeFileIndex = null;
    }

    // --- SourceLink queries ---

    /// <summary>Whether the PDB contains SourceLink information.</summary>
    public bool HasSourceLink => _context.HasSourceLink;

    /// <summary>The raw SourceLink JSON from the PDB.</summary>
    public string? SourceLinkJson => _context.SourceLinkJson;

    /// <summary>The repository URL extracted from SourceLink mappings.</summary>
    public string? RepositoryUrl => _context.ExtractRepositoryUrl();

    /// <summary>
    /// The revision source is served at. Returns null if no SourceLink data, or if no single
    /// origin describes every document the assembly resolves.
    /// </summary>
    public string? CommitHash => ExtractCommitHash();

    // --- File queries ---

    /// <summary>
    /// Gets all source documents tracked in the PDB with SourceLink resolution.
    /// Results are cached for the lifetime of this service.
    /// </summary>
    public IReadOnlyList<SourceDocument> GetTrackedFiles()
    {
        _trackedFiles ??= _context.EnumerateSourceDocuments().ToList();
        return _trackedFiles;
    }

    /// <summary>
    /// Gets only embedded source documents.
    /// </summary>
    public IReadOnlyList<SourceDocument> GetEmbeddedFiles()
    {
        return GetTrackedFiles().Where(d => d.IsEmbedded).ToList();
    }

    // --- Type resolution ---

    /// <summary>
    /// Resolves the assembly path that actually implements a type, following type forwarders.
    /// Returns null if the type is defined in this assembly (not forwarded).
    /// </summary>
    public string? ResolveImplementationAssemblyPath(string typeName)
        => _context.ResolveImplementationAssemblyPath(typeName);

    /// <summary>
    /// Opens a new SourceLinkService for the assembly that implements the given type,
    /// following type forwarders. Returns null if the type is not forwarded.
    /// The caller is responsible for disposing the returned service and acquiring its PDB.
    /// </summary>
    public SourceLinkService? OpenImplementation(string typeName)
    {
        var implPath = _context.ResolveImplementationAssemblyPath(typeName);
        if (implPath == null)
            return null;

        return Open(implPath, _context.Log);
    }

    /// <summary>
    /// Resolves detailed source information for a type (line number, GitHub URL, partial files).
    /// </summary>
    public SourceLinkResolver.TypeSourceInfo? ResolveTypeSource(string typeName)
    {
        return _context.ResolveTypeSource(typeName);
    }

    /// <summary>
    /// Resolves source file and line range for a specific method overload.
    /// </summary>
    public SourceLinkResolver.MethodSourceInfo? ResolveMethodSource(string typeName, string methodName, int overloadIndex, bool publicOnly = false, int metadataToken = 0)
    {
        return _context.ResolveMethodSource(typeName, methodName, overloadIndex, publicOnly, metadataToken);
    }

    /// <summary>
    /// Resolves source file and line number from a method token and IL offset.
    /// Works even without SourceLink (returns file path + line, no URL).
    /// </summary>
    public SourceLinkResolver.ILOffsetSourceInfo? ResolveByILOffset(int methodToken, int ilOffset)
    {
        return _context.ResolveByILOffset(methodToken, ilOffset);
    }

    /// <summary>
    /// Gets the source file paths for a type, including all partial class files.
    /// Uses a cached index built from PDB sequence point data.
    /// The index is persisted to disk keyed by commit hash for cross-invocation reuse.
    /// </summary>
    public string[] GetTrackedFilesForType(string typeName)
    {
        var index = GetOrBuildTypeFileIndex();
        return index.TryGetValue(typeName, out var files) ? files : [];
    }

    // --- Index building ---

    private Dictionary<string, string[]> GetOrBuildTypeFileIndex()
    {
        if (_typeFileIndex != null)
            return _typeFileIndex;

        // Try loading from disk cache. The key names both the origin and this assembly's symbols.
        // The origin is not enough on its own in either direction: a bare revision is shared by
        // every fork containing that commit, and a full origin is shared by every assembly built
        // from that repository at that revision -- and the index being cached is built from one
        // assembly's PDB, so origin alone serves one assembly's source files for another's types.
        var cacheKey = BuildIndexCacheKey();
        if (cacheKey != null)
        {
            var cached = _cache?.TryGet(cacheKey);
            if (cached != null)
            {
                try
                {
                    _typeFileIndex = JsonSerializer.Deserialize(cached, SourceLinkJsonContext.Default.DictionaryStringStringArray) ?? [];
                    return _typeFileIndex;
                }
                catch
                {
                    // Corrupted cache, rebuild
                }
            }
        }

        // Build from PDB data
        _typeFileIndex = BuildTypeFileIndex();

        // Persist to disk
        if (cacheKey != null && _typeFileIndex.Count > 0)
        {
            try
            {
                var json = JsonSerializer.Serialize(_typeFileIndex, SourceLinkJsonContext.Default.DictionaryStringStringArray);
                _cache?.Set(cacheKey, json);
            }
            catch
            {
                // Best-effort caching
            }
        }

        return _typeFileIndex;
    }

    /// <summary>
    /// The cache key for this assembly's type-to-file index, or null when the assembly cannot be
    /// identified precisely enough to share one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The debug directory's CodeView identity — the PDB GUID and age — names the exact symbols
    /// this index was built from, which is what the index actually depends on. Parts are joined
    /// with the same length-prefixed encoding <see cref="SLF.SourceLinkOrigin.Identity"/> uses, so
    /// no combination of values can spell another combination's key.
    /// </para>
    /// <para>
    /// Returning null when either part is missing declines the cache rather than falling back to
    /// a weaker key. A cache miss costs one index rebuild; a key that does not name the assembly
    /// hands back another assembly's source files, which is wrong output rather than slow output.
    /// </para>
    /// </remarks>
    private string? BuildIndexCacheKey()
        => BuildIndexCacheKey(_context.Provenance().Origin?.Identity, _context.PdbId);

    /// <summary>
    /// Composes the key from the two identities, so the composition can be gated without a build
    /// that carries SourceLink data. Internal for that reason only.
    /// </summary>
    internal static string? BuildIndexCacheKey(string? originIdentity, CodeViewInfo? pdbId)
    {
        if (originIdentity is null || pdbId is null)
        {
            return null;
        }

        string symbols = $"{pdbId.Guid:N}-{pdbId.Age}";
        return $"{originIdentity}{symbols.Length}:{symbols}|";
    }

    private Dictionary<string, string[]> BuildTypeFileIndex()
    {
        Dictionary<string, List<string>> index = [];

        var metadataReader = _context.GetMetadataReader();
        var pdbReader = _context.GetPdbReader();
        if (metadataReader == null || pdbReader == null)
            return [];

        // Walk all type definitions and find their source files via method sequence points
        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeHandle);
            var typeName = metadataReader.GetFullTypeName(typeDef);

            if (string.IsNullOrEmpty(typeName) || typeName == "<Module>")
                continue;

            HashSet<string> filePathsForType = [];
            foreach (var methodHandle in typeDef.GetMethods())
            {
                try
                {
                    var debugInfo = pdbReader.GetMethodDebugInformation(methodHandle);
                    if (debugInfo.Document.IsNil)
                        continue;

                    var doc = pdbReader.GetDocument(debugInfo.Document);
                    var filePath = pdbReader.GetString(doc.Name);
                    if (!string.IsNullOrEmpty(filePath))
                        filePathsForType.Add(filePath);
                }
                catch
                {
                    // Skip methods without debug info
                }
            }

            if (filePathsForType.Count > 0)
            {
                // Use short name (without namespace) for lookup convenience
                var shortName = TypeMatcher.GetSimpleName(typeName);
                
                // Store under both full and short name
                var paths = filePathsForType.Order().ToArray();
                index.TryAdd(typeName, []);
                index[typeName] = MergePaths(index[typeName], paths);

                if (shortName != typeName)
                {
                    index.TryAdd(shortName, []);
                    index[shortName] = MergePaths(index[shortName], paths);
                }
            }
        }

        return index.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static List<string> MergePaths(List<string> existing, string[] newPaths)
    {
        foreach (var p in newPaths)
        {
            if (!existing.Contains(p))
                existing.Add(p);
        }
        return existing;
    }

    private string? ExtractCommitHash() => _context.Provenance().Origin?.Revision;

    public void Dispose()
    {
        _context.Dispose();
    }
}
