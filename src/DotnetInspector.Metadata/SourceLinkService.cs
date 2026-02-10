using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotnetInspector.Core;

namespace DotnetInspector.Metadata;

[JsonSerializable(typeof(Dictionary<string, string[]>))]
internal partial class SourceLinkJsonContext : JsonSerializerContext;

/// <summary>
/// High-level service for SourceLink queries over a .NET assembly.
/// Wraps <see cref="PdbContext"/> and provides cached type-to-source resolution.
/// </summary>
public class SourceLinkService : IDisposable
{
    private readonly PdbContext _context;
    private IReadOnlyList<SourceDocument>? _trackedFiles;
    private Dictionary<string, string[]>? _typeFileIndex;

    private SourceLinkService(PdbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Opens an assembly and probes for PDB (embedded, then standalone adjacent).
    /// After return, check <see cref="NeedsPdb"/> to see if the caller should download a PDB.
    /// </summary>
    public static SourceLinkService Open(string assemblyPath, Action<string>? log = null)
    {
        var context = PdbContext.Open(assemblyPath, log);
        return new SourceLinkService(context);
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
    /// The commit hash extracted from SourceLink URL patterns.
    /// Returns null if no SourceLink data or the commit hash cannot be determined.
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
    /// Resolves detailed source information for a type (line number, GitHub URL, partial files).
    /// </summary>
    public SourceLinkResolver.TypeSourceInfo? ResolveTypeSource(string typeName)
    {
        return _context.ResolveTypeSource(typeName);
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

        // Try loading from disk cache
        var commitHash = CommitHash;
        if (commitHash != null)
        {
            var cached = CoreCache.TryGet("sourcelink-index", commitHash);
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
        if (commitHash != null && _typeFileIndex.Count > 0)
        {
            try
            {
                var json = JsonSerializer.Serialize(_typeFileIndex, SourceLinkJsonContext.Default.DictionaryStringStringArray);
                CoreCache.Set("sourcelink-index", commitHash, json);
            }
            catch
            {
                // Best-effort caching
            }
        }

        return _typeFileIndex;
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
                var shortName = typeName.Contains('.') ? typeName[(typeName.LastIndexOf('.') + 1)..] : typeName;
                
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

    private string? ExtractCommitHash()
    {
        var repoUrl = RepositoryUrl;
        if (repoUrl == null)
            return null;

        // SourceLink URLs typically contain a commit hash in the path
        // e.g., https://raw.githubusercontent.com/owner/repo/COMMITHASH/*
        var match = Regex.Match(repoUrl, @"/([0-9a-f]{40})(?:/|$)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;

        // Try SourceLink JSON directly for the commit hash pattern
        if (SourceLinkJson != null)
        {
            match = Regex.Match(SourceLinkJson, @"/([0-9a-f]{40})(?:/|$)", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
