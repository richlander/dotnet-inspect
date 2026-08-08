using SLF = SourceLinkFetch;

namespace ILInspector.SourceLink;

/// <summary>
/// Canonicalizes portable-PDB document paths and resolves them to source URLs.
/// </summary>
/// <remarks>
/// The SourceLink document-map rule — key conformance, specificity ordering, case-insensitive
/// comparison, wildcard substitution and percent-encoding — has one owner,
/// <see cref="SLF.SourceLinkResolver"/>. This type adds only the path canonicalization the
/// metadata layer needs on top of that match, and must not re-derive the matching rule: a second
/// implementation of a shared rule is a defect (docs/design/inspection-layers.md, seam rule 6),
/// and the two implementations this replaced disagreed on six of nine measured cases.
/// </remarks>
internal static class SourceDocumentPath
{
    public static string Canonicalize(string filePath, string? sourceLinkJson)
        => Resolve(filePath, sourceLinkJson).CanonicalPath;

    public static SourceDocumentPathResolution Resolve(string filePath, string? sourceLinkJson)
        => CreateResolver(sourceLinkJson).Resolve(filePath);

    public static SourceDocumentPathResolver CreateResolver(string? sourceLinkJson)
        => SourceDocumentPathResolver.Create(sourceLinkJson);

    internal static string NormalizeSeparators(string path)
        => path.Replace('\\', '/');

    internal static string TrimSyntheticRoot(string path)
        => path.StartsWith("/_/", StringComparison.Ordinal) ? path[3..] : path;
}

internal sealed class SourceDocumentPathResolver
{
    public static SourceDocumentPathResolver Empty { get; } = new(SLF.SourceLinkResolver.Empty);

    private readonly SLF.SourceLinkResolver _map;

    private SourceDocumentPathResolver(SLF.SourceLinkResolver map)
    {
        _map = map;
    }

    public static SourceDocumentPathResolver Create(string? sourceLinkJson)
        => new(SLF.SourceLinkResolver.Parse(sourceLinkJson));

    internal static SourceDocumentPathResolver Create(SLF.SourceLinkResolver map)
        => new(map);

    public SourceDocumentPathResolution Resolve(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new BadImageFormatException("A portable-PDB source document has an empty path.");

        if (_map.TryResolve(filePath, out var resolution))
        {
            // The canonical path is whatever the key did not cover, which is the
            // repository-relative path for a conventional "/_/*" map; its leading separator is
            // cosmetic, so it is trimmed for display. A key can cover the whole path in two ways:
            // by carrying no wildcard, or by carrying one that the document path happens to
            // consume entirely. Both leave nothing to name the document by, so both fall back to
            // the document's own name -- the condition is "no remainder", not "not a wildcard".
            string remainder = resolution.IsPrefixMatch ? resolution.Remainder.TrimStart('/') : "";
            string canonical = remainder.Length != 0
                ? remainder
                : SourceDocumentPath.TrimSyntheticRoot(SourceDocumentPath.NormalizeSeparators(filePath));

            // The URL is built by the owner from the untrimmed remainder, so trimming for display
            // cannot alter what gets fetched.
            return new SourceDocumentPathResolution(canonical, resolution.Url, IsMapped: true);
        }

        return new SourceDocumentPathResolution(
            SourceDocumentPath.TrimSyntheticRoot(SourceDocumentPath.NormalizeSeparators(filePath)),
            ResolvedUrl: null,
            IsMapped: false);
    }
}

internal sealed record SourceDocumentPathResolution(
    string CanonicalPath,
    string? ResolvedUrl,
    bool IsMapped);
