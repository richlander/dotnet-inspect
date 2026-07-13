using System.Text.Json;

namespace ILInspector.Metadata;

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
    public static SourceDocumentPathResolver Empty { get; } = new([]);

    private readonly SourceDocumentPathMapping[] _mappings;

    private SourceDocumentPathResolver(SourceDocumentPathMapping[] mappings)
    {
        _mappings = mappings;
    }

    public static SourceDocumentPathResolver Create(string? sourceLinkJson)
    {
        if (string.IsNullOrWhiteSpace(sourceLinkJson))
            return Empty;

        try
        {
            using var document = JsonDocument.Parse(sourceLinkJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("documents", out var mappings)
                || mappings.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            return new SourceDocumentPathResolver(mappings
                .EnumerateObject()
                .Select(static mapping => new SourceDocumentPathMapping(
                    SourceDocumentPath.NormalizeSeparators(mapping.Name),
                    SourceLinkUrlPattern(mapping.Value)))
                .OrderByDescending(static mapping => mapping.Pattern.Length)
                .ToArray());
        }
        catch (JsonException)
        {
            // The SourceLink resolver owns malformed-map reporting. Keep the PDB path usable.
            return Empty;
        }
    }

    public SourceDocumentPathResolution Resolve(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new BadImageFormatException("A portable-PDB source document has an empty path.");

        string normalizedPath = SourceDocumentPath.NormalizeSeparators(filePath);
        foreach (var mapping in _mappings)
        {
            string pattern = mapping.Pattern;
            if (pattern.EndsWith('*'))
            {
                string prefix = pattern[..^1];
                if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string suffix = normalizedPath[prefix.Length..].TrimStart('/');
                    return new SourceDocumentPathResolution(
                        suffix,
                        ResolveUrl(mapping.UrlPattern, suffix),
                        IsMapped: true);
                }
            }
            else if (string.Equals(normalizedPath, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new SourceDocumentPathResolution(
                    SourceDocumentPath.TrimSyntheticRoot(normalizedPath),
                    mapping.UrlPattern,
                    IsMapped: true);
            }
        }

        return new SourceDocumentPathResolution(
            SourceDocumentPath.TrimSyntheticRoot(normalizedPath),
            ResolvedUrl: null,
            IsMapped: false);
    }

    private static string? ResolveUrl(string? urlPattern, string suffix)
    {
        if (string.IsNullOrWhiteSpace(urlPattern))
            return null;

        return urlPattern.EndsWith('*')
            ? urlPattern[..^1] + EscapeSourceLinkPath(suffix)
            : urlPattern;
    }

    private static string? SourceLinkUrlPattern(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string EscapeSourceLinkPath(string path)
        => string.Join(
            '/',
            path.Split('/').Select(static segment => Uri.EscapeDataString(segment)));
}

internal sealed record SourceDocumentPathMapping(
    string Pattern,
    string? UrlPattern);

internal sealed record SourceDocumentPathResolution(
    string CanonicalPath,
    string? ResolvedUrl,
    bool IsMapped);
