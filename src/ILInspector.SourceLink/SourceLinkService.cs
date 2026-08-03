using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Metadata;
using SLF = SourceLinkFetch;

namespace ILInspector.SourceLink;

[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSourceGenerationOptions(AllowDuplicateProperties = false)]
internal partial class SourceLinkJsonContext : JsonSerializerContext;

public record SourceDocument(
    string FilePath,
    bool IsEmbedded,
    string? ResolvedUrl,
    byte[]? Checksum = null,
    string? ChecksumAlgorithm = null,
    int DocumentRowId = 0,
    string? CanonicalPath = null);

/// <summary>
/// High-level SourceLink service over Metadata's PE/PDB extraction APIs.
/// </summary>
public sealed class SourceLinkService : IDisposable
{
    static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    static readonly Guid EmbeddedSourceKind =
        new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    public static ISourceLinkIndexCache? DefaultCache { get; set; }

    readonly PdbContext _context;
    readonly ISourceLinkIndexCache? _cache;
    string? _sourceLinkJson;
    SLF.SourceLinkResolver? _map;
    SourceDocumentPathResolver _pathResolver = SourceDocumentPathResolver.Empty;
    SourceLinkResolver? _resolver;
    SourceLinkFetch.SourceLinkProvenanceResult? _provenance;
    IReadOnlyList<SourceDocument>? _trackedFiles;
    Dictionary<string, string[]>? _typeFileIndex;
    int _observedPdbVersion = -1;

    SourceLinkService(PdbContext context, ISourceLinkIndexCache? cache)
    {
        _context = context;
        _cache = cache;
        RefreshPdbState();
    }

    public static SourceLinkService Open(string assemblyPath, Action<string>? log = null)
        => Open(assemblyPath, log, cache: null);

    public static SourceLinkService Open(
        string assemblyPath,
        Action<string>? log,
        ISourceLinkIndexCache? cache)
        => new(PdbContext.Open(assemblyPath, log), cache ?? DefaultCache);

    public static SourceLinkService Open(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null,
        ISourceLinkIndexCache? cache = null)
        => new(PdbContext.Open(assembly, log), cache ?? DefaultCache);

    public static SourceLinkService OpenPrefetched(
        string assemblyPath,
        Action<string>? log = null)
        => new(PdbContext.OpenPrefetched(assemblyPath, log), DefaultCache);

    public PdbContext Context => _context;
    public bool HasPdb => _context.HasPdb;
    public bool NeedsPdb => _context.NeedsPdb;
    public bool HasSourceLink
    {
        get
        {
            EnsureCurrentPdbState();
            return _sourceLinkJson is not null;
        }
    }
    public string? SourceLinkJson
    {
        get
        {
            EnsureCurrentPdbState();
            return _sourceLinkJson;
        }
    }
    public string? RepositoryUrl => Provenance().Origin?.RepositoryUrl;
    public string? CommitHash => Provenance().Origin?.Revision;

    public void LoadPdb(
        string pdbPath,
        string? location = null,
        string? symbolServer = null)
    {
        _context.LoadPdbFromFile(pdbPath, location, symbolServer);
        RefreshPdbState();
    }

    public IReadOnlyList<SourceDocument> GetTrackedFiles()
    {
        EnsureCurrentPdbState();
        if (_trackedFiles is not null)
            return _trackedFiles;

        _trackedFiles =
        [
            .. _context.EnumeratePdbDocuments().Select(document =>
            {
                var resolution = _pathResolver.Resolve(document.FilePath);
                return new SourceDocument(
                    document.FilePath,
                    _context.HasDocumentCustomDebugInformation(
                        document.DocumentRowId,
                        EmbeddedSourceKind),
                    resolution.ResolvedUrl,
                    document.Checksum,
                    document.ChecksumAlgorithm,
                    document.DocumentRowId,
                    resolution.CanonicalPath);
            }),
        ];
        return _trackedFiles;
    }

    public IReadOnlyList<SourceDocument> GetEmbeddedFiles()
        => [.. GetTrackedFiles().Where(static document => document.IsEmbedded)];

    public SourceLinkResolver.TypeSourceInfo? ResolveTypeSource(string typeName)
    {
        EnsureCurrentPdbState();
        return _resolver?.ResolveTypeSource(typeName);
    }

    public SourceLinkResolver.MethodSourceInfo? ResolveMethodSource(
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly = false,
        int metadataToken = 0)
    {
        EnsureCurrentPdbState();
        return _resolver?.ResolveMethodSource(
            typeName,
            methodName,
            overloadIndex,
            publicOnly,
            metadataToken);
    }

    public SourceLinkResolver.ILOffsetSourceInfo? ResolveByILOffset(
        int methodToken,
        int ilOffset)
    {
        EnsureCurrentPdbState();
        var location = _context.ResolvePdbLocation(methodToken, ilOffset);
        if (location is null)
            return null;

        string? sourceUrl = _map?.ResolveUrl(location.FilePath);
        return new SourceLinkResolver.ILOffsetSourceInfo(
            location.MethodName,
            location.FilePath,
            sourceUrl,
            location.Line,
            location.MatchedOffset,
            SLF.SourceLinkProvenance.BrowseUrl(sourceUrl));
    }

    public string[] GetTrackedFilesForType(string typeName)
    {
        EnsureCurrentPdbState();
        var index = GetOrBuildTypeFileIndex();
        return index.TryGetValue(typeName, out var files) ? files : [];
    }

    public SourceLinkFetch.SourceLinkProvenanceResult Provenance()
    {
        EnsureCurrentPdbState();
        return _provenance ??= _map is null
            ? new SourceLinkFetch.SourceLinkProvenanceResult(
                null,
                "the PDB carries no SourceLink map")
            : SLF.SourceLinkProvenance.Determine(
                _map,
                _context.EnumeratePdbDocuments().Select(static document => document.FilePath));
    }

    internal SourceDocumentPathResolver PathResolver
    {
        get
        {
            EnsureCurrentPdbState();
            return _pathResolver;
        }
    }

    void RefreshPdbState()
    {
        _sourceLinkJson = _context
            .GetModuleCustomDebugInformation(SourceLinkKind)
            .Select(static bytes => Encoding.UTF8.GetString(bytes))
            .FirstOrDefault();
        _map = _sourceLinkJson is null
            ? null
            : SLF.SourceLinkResolver.Parse(_sourceLinkJson);
        _pathResolver = _map is null
            ? SourceDocumentPathResolver.Empty
            : SourceDocumentPathResolver.Create(_map);
        _resolver = _map is null ? null : new SourceLinkResolver(_context, _map);
        _provenance = null;
        _trackedFiles = null;
        _typeFileIndex = null;
        _observedPdbVersion = _context.PdbVersion;
    }

    void EnsureCurrentPdbState()
    {
        if (_observedPdbVersion != _context.PdbVersion)
            RefreshPdbState();
    }

    Dictionary<string, string[]> GetOrBuildTypeFileIndex()
    {
        if (_typeFileIndex is not null)
            return _typeFileIndex;

        string? cacheKey = BuildIndexCacheKey();
        if (cacheKey is not null && _cache?.TryGet(cacheKey) is { } cached)
        {
            try
            {
                _typeFileIndex = JsonSerializer.Deserialize(
                    cached,
                    SourceLinkJsonContext.Default.DictionaryStringStringArray) ?? [];
                return _typeFileIndex;
            }
            catch (JsonException)
            {
            }
        }

        _typeFileIndex = BuildTypeFileIndex();
        if (cacheKey is not null && _typeFileIndex.Count > 0)
        {
            try
            {
                _cache?.Set(
                    cacheKey,
                    JsonSerializer.Serialize(
                        _typeFileIndex,
                        SourceLinkJsonContext.Default.DictionaryStringStringArray));
            }
            catch
            {
            }
        }
        return _typeFileIndex;
    }

    string? BuildIndexCacheKey()
        => BuildIndexCacheKey(Provenance().Origin?.Identity, _context.PdbId);

    internal static string? BuildIndexCacheKey(
        string? originIdentity,
        CodeViewInfo? pdbId)
    {
        if (originIdentity is null || pdbId is null)
            return null;

        string symbols = $"{pdbId.Guid:N}-{pdbId.Age}";
        return $"{originIdentity}{symbols.Length}:{symbols}|";
    }

    Dictionary<string, string[]> BuildTypeFileIndex()
    {
        Dictionary<string, List<string>> index = [];
        foreach (var type in _context.EnumerateTypeDocuments())
        {
            if (type.FilePaths.Count == 0)
                continue;
            Add(type.TypeFullName, type.FilePaths);
            string indexName = TypeFileIndexName(type.TypeFullName);
            if (indexName != type.TypeFullName)
                Add(indexName, type.FilePaths);
        }
        return index.ToDictionary(
            static item => item.Key,
            static item => item.Value.ToArray());

        void Add(string key, IReadOnlyList<string> paths)
        {
            if (!index.TryGetValue(key, out var existing))
                index[key] = existing = [];
            foreach (string path in paths)
            {
                if (!existing.Contains(path, StringComparer.Ordinal))
                    existing.Add(path);
            }
        }
    }

    static string TypeFileIndexName(string fullName)
    {
        int separator = fullName.LastIndexOf('.');
        return separator >= 0 ? fullName[(separator + 1)..] : fullName;
    }

    public void Dispose() => _context.Dispose();
}
