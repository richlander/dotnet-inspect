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
    IReadOnlyList<PdbDocumentInfo>? _documents;
    Dictionary<string, List<string>>? _docsByFirstSegment;
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
        SourceResolutionMethod ResolutionMethod = SourceResolutionMethod.SourceLink)
    {
        public List<PartialSourceFile> AdditionalSourceFiles { get; init; } = [];
        public bool IsPartialType => AdditionalSourceFiles.Count > 0;
    }

    public record PartialSourceFile(
        string FilePath,
        string? SourceUrl,
        string? GitHubBrowseUrl);

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
        string? GitHubBrowseUrl);

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

        foreach (string path in type.FilePaths)
            files.TryAdd(path, Decorate(path));

        foreach (string path in FindDocumentsMatchingTypeName(simpleName))
            files.TryAdd(path, Decorate(path));

        if (files.Count == 0)
        {
            string? inferred = Documents
                .Select(static document => document.FilePath)
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
                SourceResolutionMethod.Inferred);
        }

        var primary = SelectPrimarySourceFile(files.Values, simpleName);
        return new TypeSourceInfo(
            primary.FilePath,
            primary.SourceUrl,
            LineNumber: null,
            primary.GitHubBrowseUrl)
        {
            AdditionalSourceFiles =
            [
                .. files.Values
                    .Where(file => file.FilePath != primary.FilePath)
                    .OrderBy(static file => file.FilePath, StringComparer.Ordinal),
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

    IReadOnlyList<PdbDocumentInfo> Documents
        => _documents ??= [.. _context.EnumeratePdbDocuments()];

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
            foreach (var document in Documents)
            {
                string fileName = Path.GetFileName(document.FilePath);
                int firstDot = fileName.IndexOf('.');
                string segment = firstDot >= 0 ? fileName[..firstDot] : fileName;
                if (!index.TryGetValue(segment, out var paths))
                    index[segment] = paths = [];
                paths.Add(document.FilePath);
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
        return new PartialSourceFile(
            filePath,
            url,
            SLF.SourceLinkProvenance.BrowseUrl(url));
    }

    static PartialSourceFile SelectPrimarySourceFile(
        IEnumerable<PartialSourceFile> files,
        string typeName)
    {
        string primaryName = $"{typeName}.cs";
        return files.FirstOrDefault(file => Path.GetFileName(file.FilePath)
                .Equals(primaryName, StringComparison.OrdinalIgnoreCase))
            ?? files.OrderBy(file => Path.GetFileName(file.FilePath).Length)
                .ThenBy(static file => file.FilePath, StringComparer.Ordinal)
                .First();
    }

}
