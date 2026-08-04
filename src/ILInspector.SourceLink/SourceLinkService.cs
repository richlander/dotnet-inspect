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
    readonly Action<string>? _log;
    bool _sourceLinkPresent;
    string? _sourceLinkJson;
    string? _sourceLinkError;
    SLF.SourceLinkResolver? _map;
    SourceDocumentPathResolver _pathResolver = SourceDocumentPathResolver.Empty;
    SourceLinkResolver? _resolver;
    SourceLinkFetch.SourceLinkProvenanceResult? _provenance;
    IReadOnlyList<SourceDocument>? _trackedFiles;
    Dictionary<string, string[]>? _typeFileIndex;
    int _observedPdbVersion = -1;

    SourceLinkService(
        PdbContext context,
        ISourceLinkIndexCache? cache,
        Action<string>? log)
    {
        _context = context;
        _cache = cache;
        _log = log;
        RefreshPdbState();
    }

    public static SourceLinkService Open(string assemblyPath, Action<string>? log = null)
        => Open(assemblyPath, log, cache: null);

    public static SourceLinkService Open(
        string assemblyPath,
        Action<string>? log,
        ISourceLinkIndexCache? cache)
        => new(PdbContext.Open(assemblyPath, log), cache ?? DefaultCache, log);

    public static SourceLinkService Open(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null,
        ISourceLinkIndexCache? cache = null)
        => new(PdbContext.Open(assembly, log), cache ?? DefaultCache, log);

    public static SourceLinkService OpenPrefetched(
        string assemblyPath,
        Action<string>? log = null)
        => new(PdbContext.OpenPrefetched(assemblyPath, log), DefaultCache, log);

    public PdbContext Context => _context;
    public bool HasPdb => _context.HasPdb;
    public bool NeedsPdb => _context.NeedsPdb;
    public bool HasSourceLink
    {
        get
        {
            EnsureCurrentPdbState();
            return _sourceLinkPresent;
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
                _sourceLinkError ?? "the PDB carries no SourceLink map")
            : SLF.SourceLinkProvenance.Determine(
                _map,
                _context.EnumeratePdbDocumentPaths());
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
        _sourceLinkPresent = false;
        _sourceLinkJson = null;
        _sourceLinkError = null;
        _map = null;
        _pathResolver = SourceDocumentPathResolver.Empty;
        _resolver = null;
        _provenance = null;
        _trackedFiles = null;
        _typeFileIndex = null;

        try
        {
            var sourceLink =
                _context.ReadModuleCustomDebugInformation(SourceLinkKind);
            _sourceLinkPresent =
                sourceLink.Status != PdbCustomDebugInformationStatus.Absent;
            if (sourceLink.Status == PdbCustomDebugInformationStatus.Duplicate)
            {
                _sourceLinkError =
                    "the PDB carries multiple SourceLink custom debug information records";
                _log?.Invoke($"SourceLink unavailable: {_sourceLinkError}");
                return;
            }

            if (sourceLink.Value is null)
                return;

            _sourceLinkJson = Encoding.UTF8.GetString(sourceLink.Value);
            _map = SLF.SourceLinkResolver.Parse(_sourceLinkJson);
            _pathResolver = SourceDocumentPathResolver.Create(_map);
            _resolver = new SourceLinkResolver(_context, _map);
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            _sourceLinkError =
                $"the SourceLink custom debug information could not be read: {ex.Message}";
            _log?.Invoke($"SourceLink unavailable: {_sourceLinkError}");
        }
        finally
        {
            _observedPdbVersion = _context.PdbVersion;
        }
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
            string[] orderedPaths = [.. type.FilePaths.Order()];
            Add(type.TypeFullName, orderedPaths);
            string indexName = TypeFileIndexName(type.TypeFullName);
            if (indexName != type.TypeFullName)
                Add(indexName, orderedPaths);
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

    static bool IsPdbInspectionFailure(Exception exception)
        => exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException;

    public void Dispose() => _context.Dispose();
}
