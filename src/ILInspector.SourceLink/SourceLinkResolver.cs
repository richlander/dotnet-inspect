using ILInspector.Metadata;
using SLF = SourceLinkFetch;

namespace ILInspector.SourceLink;

/// <summary>
/// Decorates Metadata's raw PDB correlations with SourceLink paths, URLs, and provenance.
/// </summary>
public sealed class SourceLinkResolver
{
    readonly PdbContext _context;
    readonly SLF.SourceLinkResolver _map;
    IReadOnlyList<string>? _documentPaths;
    Dictionary<string, List<string>>? _docsByFirstSegment;
    Dictionary<int, PdbDocumentInfo>? _documentsByRowId;
    Dictionary<string, PdbDocumentInfo>? _uniqueDocumentsByPath;
    Dictionary<string, PdbTypeDocumentInfo>? _typesByFullName;
    Dictionary<string, PdbTypeDocumentInfo>? _typesBySimpleName;

    internal SourceLinkResolver(
        PdbContext context,
        SLF.SourceLinkResolver map)
    {
        _context = context;
        _map = map;
    }

    public enum SourceResolutionMethod
    {
        SourceLink,
        Inferred,
    }

    public record TypeSourceInfo(
        string? SourceFilePath,
        string? SourceUrl,
        int? LineNumber,
        string? GitHubBrowseUrl,
        SourceResolutionMethod ResolutionMethod = SourceResolutionMethod.SourceLink,
        byte[]? Checksum = null,
        string? ChecksumAlgorithm = null)
    {
        public List<PartialSourceFile> AdditionalSourceFiles { get; init; } = [];
        public bool IsPartialType => AdditionalSourceFiles.Count > 0;
    }

    public record PartialSourceFile(
        string FilePath,
        string? SourceUrl,
        string? GitHubBrowseUrl,
        byte[]? Checksum = null,
        string? ChecksumAlgorithm = null);

    public record MethodSourceInfo(
        string FilePath,
        string? SourceUrl,
        int StartLine,
        int EndLine,
        byte[]? Checksum = null,
        string? ChecksumAlgorithm = null);

    public record ILOffsetSourceInfo(
        string? MethodName,
        string FilePath,
        string? SourceUrl,
        int Line,
        int MatchedOffset,
        string? GitHubBrowseUrl,
        byte[]? Checksum = null,
        string? ChecksumAlgorithm = null);

    public TypeSourceInfo? ResolveTypeSource(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        EnsureTypeIndexes();
        if (!_typesByFullName!.TryGetValue(typeName, out var type)
            && !_typesBySimpleName!.TryGetValue(typeName, out type))
        {
            return null;
        }

        string simpleName = type.TypeSimpleName;
        Dictionary<string, PartialSourceFile> files =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (var documents in type.Documents.GroupBy(
            static document => document.FilePath,
            StringComparer.Ordinal))
        {
            var candidates = documents.Take(2).ToArray();
            files.TryAdd(
                documents.Key,
                candidates.Length == 1
                    ? Decorate(candidates[0])
                    : Decorate(documents.Key));
        }

        foreach (string path in FindDocumentsMatchingTypeName(simpleName))
            files.TryAdd(path, Decorate(path));

        if (files.Count == 0)
        {
            string? inferred = DocumentPaths
                .FirstOrDefault(path => Path.GetFileNameWithoutExtension(path)
                    .Equals(simpleName, StringComparison.OrdinalIgnoreCase));
            if (inferred is null)
                return null;

            var file = Decorate(inferred);
            return new TypeSourceInfo(
                file.FilePath,
                file.SourceUrl,
                LineNumber: null,
                file.GitHubBrowseUrl,
                SourceResolutionMethod.Inferred,
                file.Checksum,
                file.ChecksumAlgorithm);
        }

        var primary = SelectPrimarySourceFile(files.Values, simpleName);
        return new TypeSourceInfo(
            primary.FilePath,
            primary.SourceUrl,
            LineNumber: null,
            primary.GitHubBrowseUrl,
            Checksum: primary.Checksum,
            ChecksumAlgorithm: primary.ChecksumAlgorithm)
        {
            AdditionalSourceFiles =
            [
                .. files.Values
                    .Where(file => file.FilePath != primary.FilePath),
            ],
        };
    }

    public MethodSourceInfo? ResolveMethodSource(
        string typeName,
        string methodName,
        int overloadIndex,
        bool publicOnly = false,
        int metadataToken = 0)
    {
        var raw = _context.ResolveMethodDocument(
            typeName,
            methodName,
            overloadIndex,
            publicOnly,
            metadataToken);
        return raw is null
            ? null
            : new MethodSourceInfo(
                raw.FilePath,
                _map.ResolveUrl(raw.FilePath),
                raw.StartLine,
                raw.EndLine,
                raw.Checksum,
                raw.ChecksumAlgorithm);
    }

    public string? ApplySourceLinkMapping(string filePath)
        => _map.ResolveUrl(filePath);

    IReadOnlyList<string> DocumentPaths
        => _documentPaths ??= [.. _context.EnumeratePdbDocumentPaths()];

    void EnsureTypeIndexes()
    {
        if (_typesByFullName is not null)
            return;

        var fullNames =
            new Dictionary<string, PdbTypeDocumentInfo>(StringComparer.OrdinalIgnoreCase);
        var simpleNames =
            new Dictionary<string, PdbTypeDocumentInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in _context.EnumerateTypeDocuments())
        {
            fullNames.TryAdd(type.TypeFullName, type);
            simpleNames.TryAdd(type.TypeSimpleName, type);
        }

        _typesByFullName = fullNames;
        _typesBySimpleName = simpleNames;
    }

    IEnumerable<string> FindDocumentsMatchingTypeName(string typeName)
    {
        if (_docsByFirstSegment is null)
        {
            var index =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in DocumentPaths)
            {
                string fileName = Path.GetFileName(path);
                int firstDot = fileName.IndexOf('.');
                string segment = firstDot >= 0 ? fileName[..firstDot] : fileName;
                if (!index.TryGetValue(segment, out var paths))
                    index[segment] = paths = [];
                paths.Add(path);
            }
            _docsByFirstSegment = index;
        }

        return _docsByFirstSegment.TryGetValue(typeName, out var candidates)
            ? candidates.Where(path => Path.GetFileName(path)
                .EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            : [];
    }

    PartialSourceFile Decorate(string filePath)
    {
        string? url = _map.ResolveUrl(filePath);
        EnsureDocumentIndexes();
        _uniqueDocumentsByPath!.TryGetValue(filePath, out PdbDocumentInfo? document);
        return new PartialSourceFile(
            filePath,
            url,
            SLF.SourceLinkProvenance.BrowseUrl(url),
            document?.Checksum,
            document?.ChecksumAlgorithm);
    }

    PartialSourceFile Decorate(PdbDocumentReference reference)
    {
        string? url = _map.ResolveUrl(reference.FilePath);
        EnsureDocumentIndexes();
        _documentsByRowId!.TryGetValue(reference.DocumentRowId, out PdbDocumentInfo? document);
        if (document is not null
            && !string.Equals(document.FilePath, reference.FilePath, StringComparison.Ordinal))
        {
            document = null;
        }

        return new PartialSourceFile(
            reference.FilePath,
            url,
            SLF.SourceLinkProvenance.BrowseUrl(url),
            document?.Checksum,
            document?.ChecksumAlgorithm);
    }

    void EnsureDocumentIndexes()
    {
        if (_documentsByRowId is not null)
            return;

        var (byRowId, uniqueByPath) = BuildDocumentIndexes(
            _context.EnumeratePdbDocuments());
        _documentsByRowId = byRowId;
        _uniqueDocumentsByPath = uniqueByPath;
    }

    internal static (
        Dictionary<int, PdbDocumentInfo> ByRowId,
        Dictionary<string, PdbDocumentInfo> UniqueByPath)
        BuildDocumentIndexes(IEnumerable<PdbDocumentInfo> documents)
    {
        Dictionary<int, PdbDocumentInfo> byRowId = [];
        Dictionary<string, PdbDocumentInfo> uniqueByPath =
            new(StringComparer.Ordinal);
        HashSet<string> ambiguousPaths = new(StringComparer.Ordinal);

        foreach (PdbDocumentInfo document in documents)
        {
            byRowId.TryAdd(document.DocumentRowId, document);
            if (ambiguousPaths.Contains(document.FilePath))
                continue;

            if (!uniqueByPath.TryAdd(document.FilePath, document))
            {
                uniqueByPath.Remove(document.FilePath);
                ambiguousPaths.Add(document.FilePath);
            }
        }

        return (byRowId, uniqueByPath);
    }

    static PartialSourceFile SelectPrimarySourceFile(
        IEnumerable<PartialSourceFile> files,
        string typeName)
    {
        string primaryName = $"{typeName}.cs";
        return files.FirstOrDefault(file => Path.GetFileName(file.FilePath)
                .Equals(primaryName, StringComparison.OrdinalIgnoreCase))
            ?? files.OrderBy(file => Path.GetFileName(file.FilePath).Length)
                .First();
    }

}
