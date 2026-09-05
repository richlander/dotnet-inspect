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
    string? CanonicalPath = null)
{
    public SourceDocumentResolutionStatus ResolutionStatus { get; init; }
}

/// <summary>Pre-allocation limits for reading an embedded PDB and its SourceLink map.</summary>
public sealed class SourceLinkReadLimits
{
    public static SourceLinkReadLimits Unlimited { get; } =
        new(int.MaxValue, int.MaxValue, int.MaxValue);

    public SourceLinkReadLimits(
        int maxEmbeddedPdbBytes,
        int maxMapBytes,
        int maxMappings,
        PdbExpansionBudget? embeddedPdbBudget = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEmbeddedPdbBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMapBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMappings);
        MaxEmbeddedPdbBytes = maxEmbeddedPdbBytes;
        MaxMapBytes = maxMapBytes;
        MaxMappings = maxMappings;
        EmbeddedPdbBudget = embeddedPdbBudget;
    }

    public int MaxEmbeddedPdbBytes { get; }
    public int MaxMapBytes { get; }
    public int MaxMappings { get; }
    public PdbExpansionBudget? EmbeddedPdbBudget { get; }
}

/// <summary>
/// High-level SourceLink service over Metadata's PE/PDB extraction APIs.
/// </summary>
public sealed class SourceLinkService : IDisposable
{
    static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    static readonly Guid EmbeddedSourceKind =
        new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    public static ISourceLinkIndexCache? DefaultCache { get; set; }

    readonly PdbContext _context;
    readonly ISourceLinkIndexCache? _cache;
    readonly Action<string>? _log;
    readonly SourceLinkReadLimits _readLimits;
    bool _sourceLinkPresent;
    string? _sourceLinkJson;
    string? _sourceLinkError;
    int _sourceLinkEncodedBytes;
    SourceLinkMapLimitKind _sourceLinkLimitKind;
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
        Action<string>? log,
        SourceLinkReadLimits? readLimits = null)
    {
        _context = context;
        _cache = cache;
        _log = log;
        _readLimits = readLimits ?? SourceLinkReadLimits.Unlimited;
        RefreshPdbState();
    }

    public static SourceLinkService Open(string assemblyPath, Action<string>? log = null)
        => Open(assemblyPath, log, cache: null);

    public static SourceLinkService Open(
        string assemblyPath,
        Action<string>? log,
        ISourceLinkIndexCache? cache)
        => new(PdbContext.Open(assemblyPath, log), cache ?? DefaultCache, log);

    /// <summary>
    /// Inspects SourceLink text carried by a standalone portable PDB without associating the PDB
    /// with an assembly. Package-content audit uses this path to cover every package-local PDB;
    /// method/document mapping still requires the identity-checked assembly path.
    /// </summary>
    public static SourceLinkMapAudit InspectPortablePdb(
        string pdbPath,
        int maxEncodedBytes = int.MaxValue,
        int maxMappings = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEncodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMappings);
        PdbCustomDebugInformationResult sourceLink =
            PdbContext.ReadPortablePdbModuleCustomDebugInformation(
                pdbPath,
                SourceLinkKind,
                maxEncodedBytes);
        if (sourceLink.Status == PdbCustomDebugInformationStatus.Absent)
            return new SourceLinkMapAudit(SourceLinkMapInspection.Absent, [], 0);

        if (sourceLink.Status == PdbCustomDebugInformationStatus.Duplicate)
        {
            return new SourceLinkMapAudit(
                new SourceLinkMapInspection(
                    SourceLinkMapStatus.Unusable,
                    "the PDB carries multiple SourceLink custom debug information records",
                    [],
                    []),
                [],
                0);
        }

        if (sourceLink.LimitExceeded)
        {
            return new SourceLinkMapAudit(
                new SourceLinkMapInspection(
                    SourceLinkMapStatus.Unusable,
                    "the SourceLink map exceeded the caller's encoded-byte limit",
                    [],
                    []),
                [],
                sourceLink.ValueLength,
                SourceLinkMapLimitKind.EncodedBytes);
        }

        if (sourceLink.Value is null)
        {
            return new SourceLinkMapAudit(
                new SourceLinkMapInspection(
                    SourceLinkMapStatus.Unusable,
                    sourceLink.Error is null
                        ? "the SourceLink custom debug information could not be read"
                        : $"the SourceLink custom debug information could not be read: {sourceLink.Error}",
                    [],
                    []),
                [],
                0);
        }

        try
        {
            string json = StrictUtf8.GetString(sourceLink.Value);
            SLF.SourceLinkResolver map =
                SLF.SourceLinkResolver.Parse(json, maxMappings);
            SourceLinkMapInspection inspection = CreateMapInspection(map);
            if (map.MappingLimitExceeded)
            {
                return new SourceLinkMapAudit(
                    inspection,
                    [],
                    sourceLink.ValueLength,
                    SourceLinkMapLimitKind.Mappings);
            }

            SourceLinkMapEntry[] entries =
            [
                .. map.DocumentMappings.Select(static mapping =>
                    new SourceLinkMapEntry(mapping.Document, mapping.Url)),
            ];
            return new SourceLinkMapAudit(
                inspection,
                entries,
                sourceLink.ValueLength);
        }
        catch (DecoderFallbackException ex)
        {
            return new SourceLinkMapAudit(
                new SourceLinkMapInspection(
                    SourceLinkMapStatus.Unusable,
                    $"the SourceLink custom debug information could not be read: {ex.Message}",
                    [],
                    []),
                [],
                sourceLink.ValueLength);
        }
    }

    /// <summary>
    /// Opens only the PE metadata and debug directory. Embedded and adjacent PDBs are not loaded.
    /// </summary>
    public static SourceLinkService OpenMetadataOnly(
        string assemblyPath,
        Action<string>? log = null)
        => new(PdbContext.OpenMetadataOnly(assemblyPath, log), DefaultCache, log);

    /// <summary>
    /// Opens PE metadata and an embedded portable PDB without probing an adjacent PDB.
    /// </summary>
    /// <remarks>
    /// <c>PdbContextDescriptorTests.MetadataOnlyAndEmbeddedOnly_KeepTheirPdbAcquisitionBoundaries</c>
    /// gates the underlying acquisition boundary.
    /// </remarks>
    public static SourceLinkService OpenEmbeddedPdbOnly(
        string assemblyPath,
        Action<string>? log = null)
        => new(PdbContext.OpenEmbeddedPdbOnly(assemblyPath, log), DefaultCache, log);

    public static SourceLinkService OpenEmbeddedPdbOnly(
        string assemblyPath,
        SourceLinkReadLimits limits,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new(
            PdbContext.OpenEmbeddedPdbOnly(
                assemblyPath,
                limits.MaxEmbeddedPdbBytes,
                log,
                limits.EmbeddedPdbBudget),
            DefaultCache,
            log,
            limits);
    }

    public static SourceLinkService OpenEmbeddedPdbOnlyPrefetched(
        string assemblyPath,
        SourceLinkReadLimits limits,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new(
            PdbContext.OpenEmbeddedPdbOnlyPrefetched(
                assemblyPath,
                limits.MaxEmbeddedPdbBytes,
                log,
                limits.EmbeddedPdbBudget),
            DefaultCache,
            log,
            limits);
    }

    public static SourceLinkService OpenEmbeddedPdbOnly(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null)
        => new(
            PdbContext.OpenEmbeddedPdbOnly(assembly, log),
            DefaultCache,
            log);

    public static SourceLinkService OpenEmbeddedPdbOnly(
        ResolvedAssemblyReference assembly,
        Action<string>? log,
        ISourceLinkIndexCache? cache)
        => new(
            PdbContext.OpenEmbeddedPdbOnly(assembly, log),
            cache,
            log);

    public static SourceLinkService OpenEmbeddedPdbOnly(
        ResolvedAssemblyReference assembly,
        SourceLinkReadLimits limits,
        Action<string>? log = null,
        ISourceLinkIndexCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new(
            PdbContext.OpenEmbeddedPdbOnly(
                assembly,
                limits.MaxEmbeddedPdbBytes,
                log,
                limits.EmbeddedPdbBudget),
            cache,
            log,
            limits);
    }

    public static SourceLinkService OpenEmbeddedPdbOnlyPrefetched(
        ResolvedAssemblyReference assembly,
        SourceLinkReadLimits limits,
        Action<string>? log = null,
        ISourceLinkIndexCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new(
            PdbContext.OpenEmbeddedPdbOnlyPrefetched(
                assembly,
                limits.MaxEmbeddedPdbBytes,
                log,
                limits.EmbeddedPdbBudget),
            cache,
            log,
            limits);
    }

    public static SourceLinkService OpenMetadataOnly(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null)
        => new(PdbContext.OpenMetadataOnly(assembly, log), DefaultCache, log);

    public static SourceLinkService OpenMetadataOnly(
        ResolvedAssemblyReference assembly,
        Action<string>? log,
        ISourceLinkIndexCache? cache)
        => new(
            PdbContext.OpenMetadataOnly(assembly, log),
            cache,
            log);

    public static SourceLinkService Open(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null,
        ISourceLinkIndexCache? cache = null)
        => new(PdbContext.Open(assembly, log), cache ?? DefaultCache, log);

    public static SourceLinkService OpenPrefetched(
        string assemblyPath,
        Action<string>? log = null)
        => new(PdbContext.OpenPrefetched(assemblyPath, log), DefaultCache, log);

    public static SourceLinkService OpenPrefetched(
        ResolvedAssemblyReference assembly,
        Action<string>? log = null,
        ISourceLinkIndexCache? cache = null)
        => new(
            PdbContext.OpenPrefetched(assembly, log),
            cache ?? DefaultCache,
            log);

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
    /// <summary>
    /// SourceLink document mappings exactly as decoded from the map, including entries the
    /// SourceLink grammar rejected. Consumers can audit authored text without reparsing it.
    /// </summary>
    public IReadOnlyList<SourceLinkMapEntry> SourceLinkMapEntries
    {
        get
        {
            EnsureCurrentPdbState();
            return _map?.DocumentMappings
                .Select(static mapping => new SourceLinkMapEntry(
                    mapping.Document,
                    mapping.Url))
                .ToArray()
                ?? [];
        }
    }
    public SourceLinkMapInspection SourceLinkMap
    {
        get
        {
            EnsureCurrentPdbState();

            if (!_sourceLinkPresent)
                return SourceLinkMapInspection.Absent;

            if (_map is null)
            {
                return new SourceLinkMapInspection(
                    SourceLinkMapStatus.Unusable,
                    _sourceLinkError ?? "the SourceLink map could not be read",
                    [],
                    []);
            }

            return CreateMapInspection(_map);
        }
    }

    /// <summary>Returns the bounded SourceLink map audit for the current PDB state.</summary>
    public SourceLinkMapAudit InspectSourceLinkMap()
    {
        EnsureCurrentPdbState();
        return new SourceLinkMapAudit(
            SourceLinkMap,
            SourceLinkMapEntries,
            _sourceLinkEncodedBytes,
            _sourceLinkLimitKind);
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
                    resolution.CanonicalPath)
                {
                    ResolutionStatus = resolution.Status,
                };
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

    public SourceLinkResolver.TypeSourceInfo? ResolveTypeSource(
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(type);
        EnsureCurrentPdbState();
        return _resolver?.ResolveTypeSource(type);
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
        SourceDocument? document = GetTrackedFiles()
            .FirstOrDefault(candidate =>
                candidate.DocumentRowId == location.DocumentRowId
                && string.Equals(
                    candidate.FilePath,
                    location.FilePath,
                    StringComparison.Ordinal));
        return new SourceLinkResolver.ILOffsetSourceInfo(
            location.MethodName,
            location.FilePath,
            sourceUrl,
            location.Line,
            location.MatchedOffset,
            SLF.SourceLinkProvenance.BrowseUrl(sourceUrl),
            document?.Checksum,
            document?.ChecksumAlgorithm);
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
        _sourceLinkEncodedBytes = 0;
        _sourceLinkLimitKind = SourceLinkMapLimitKind.None;
        _map = null;
        _pathResolver = SourceDocumentPathResolver.Empty;
        _resolver = null;
        _provenance = null;
        _trackedFiles = null;
        _typeFileIndex = null;

        try
        {
            var sourceLink =
                _context.ReadModuleCustomDebugInformation(
                    SourceLinkKind,
                    _readLimits.MaxMapBytes);
            _sourceLinkPresent =
                sourceLink.Status != PdbCustomDebugInformationStatus.Absent;
            if (sourceLink.Status == PdbCustomDebugInformationStatus.Duplicate)
            {
                _sourceLinkError =
                    "the PDB carries multiple SourceLink custom debug information records";
                _log?.Invoke($"SourceLink unavailable: {_sourceLinkError}");
                return;
            }

            _sourceLinkEncodedBytes = sourceLink.ValueLength;
            if (sourceLink.LimitExceeded)
            {
                _sourceLinkLimitKind = SourceLinkMapLimitKind.EncodedBytes;
                _sourceLinkError =
                    "the SourceLink map exceeded the caller's encoded-byte limit";
                _log?.Invoke($"SourceLink unavailable: {_sourceLinkError}");
                return;
            }

            if (sourceLink.Value is null)
            {
                if (sourceLink.Error is not null)
                {
                    _sourceLinkError =
                        $"the SourceLink custom debug information could not be read: {sourceLink.Error}";
                    _log?.Invoke($"SourceLink unavailable: {_sourceLinkError}");
                }
                return;
            }

            _sourceLinkJson = StrictUtf8.GetString(sourceLink.Value);
            _map = SLF.SourceLinkResolver.Parse(
                _sourceLinkJson,
                _readLimits.MaxMappings);
            if (_map.MappingLimitExceeded)
            {
                _sourceLinkLimitKind = SourceLinkMapLimitKind.Mappings;
                _sourceLinkError = _map.ParseError;
                _log?.Invoke($"SourceLink unavailable: {_sourceLinkError}");
                return;
            }

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
            _resolver = new SourceLinkResolver(_context, _map);
            _observedPdbVersion = _context.PdbVersion;
        }
    }

    void EnsureCurrentPdbState()
    {
        if (_observedPdbVersion != _context.PdbVersion)
            RefreshPdbState();
    }

    static SourceLinkMapInspection CreateMapInspection(SLF.SourceLinkResolver map)
    {
        if (map.ParseError is not null)
        {
            return new SourceLinkMapInspection(
                SourceLinkMapStatus.Unusable,
                map.ParseError,
                map.DocumentKeys,
                map.RejectedKeys);
        }

        if (map.IsEmpty)
        {
            return new SourceLinkMapInspection(
                SourceLinkMapStatus.Unusable,
                "the SourceLink map contains no usable document mappings",
                map.DocumentKeys,
                map.RejectedKeys);
        }

        return new SourceLinkMapInspection(
            map.RejectedKeys.Count > 0
                ? SourceLinkMapStatus.PartiallyUsable
                : SourceLinkMapStatus.Usable,
            null,
            map.DocumentKeys,
            map.RejectedKeys);
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

        string symbols = pdbId.IsPortable
            ? $"{pdbId.Guid:N}-{pdbId.Stamp:x8}"
            : $"{pdbId.Guid:N}-{pdbId.Age}";
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
            or ArgumentOutOfRangeException
            or DecoderFallbackException;

    public void Dispose() => _context.Dispose();

    /// <summary>
    /// Disposes the service and returns the first owned-resource disposal
    /// failure after all cleanup has been attempted.
    /// </summary>
    public Exception? DisposeWithFailure() =>
        _context.DisposeWithFailure();
}
