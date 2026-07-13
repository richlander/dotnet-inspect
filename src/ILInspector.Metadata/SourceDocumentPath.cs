using System.Text.Json;

namespace ILInspector.Metadata;

internal static class SourceDocumentPath
{
    public static string Canonicalize(string filePath, string? sourceLinkJson)
        => Resolve(filePath, sourceLinkJson).CanonicalPath;

    public static SourceDocumentPathResolution Resolve(string filePath, string? sourceLinkJson)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new BadImageFormatException("A portable-PDB source document has an empty path.");

        string normalizedPath = NormalizeSeparators(filePath);
        if (!string.IsNullOrWhiteSpace(sourceLinkJson))
        {
            try
            {
                using var document = JsonDocument.Parse(sourceLinkJson);
                if (document.RootElement.TryGetProperty("documents", out var mappings))
                {
                    foreach (var mapping in mappings
                        .EnumerateObject()
                        .OrderByDescending(
                            static mapping => mapping.Name.Length))
                    {
                        string pattern = NormalizeSeparators(mapping.Name);
                        if (pattern.EndsWith('*'))
                        {
                            string prefix = pattern[..^1];
                            if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                string suffix = normalizedPath[prefix.Length..].TrimStart('/');
                                return new SourceDocumentPathResolution(
                                    suffix,
                                    ResolveUrl(mapping.Value.GetString(), suffix),
                                    IsMapped: true);
                            }
                        }
                        else if (string.Equals(normalizedPath, pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            return new SourceDocumentPathResolution(
                                TrimSyntheticRoot(normalizedPath),
                                mapping.Value.GetString(),
                                IsMapped: true);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // The SourceLink resolver owns malformed-map reporting. Keep the PDB path usable.
            }
        }

        return new SourceDocumentPathResolution(
            TrimSyntheticRoot(normalizedPath),
            ResolvedUrl: null,
            IsMapped: false);
    }

    private static string NormalizeSeparators(string path)
        => path.Replace('\\', '/');

    private static string TrimSyntheticRoot(string path)
        => path.StartsWith("/_/", StringComparison.Ordinal) ? path[3..] : path;

    private static string? ResolveUrl(string? urlPattern, string suffix)
    {
        if (string.IsNullOrWhiteSpace(urlPattern))
            return null;

        return urlPattern.EndsWith('*')
            ? urlPattern[..^1] + EscapeSourceLinkPath(suffix)
            : urlPattern;
    }

    private static string EscapeSourceLinkPath(string path)
        => string.Join(
            '/',
            path.Split('/').Select(static segment => Uri.EscapeDataString(segment)));
}

internal sealed record SourceDocumentPathResolution(
    string CanonicalPath,
    string? ResolvedUrl,
    bool IsMapped);
