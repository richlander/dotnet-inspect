using System.Text.Json;

namespace ILInspector.Metadata;

internal static class SourceDocumentPath
{
    public static string Canonicalize(string filePath, string? sourceLinkJson)
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
                                return normalizedPath[prefix.Length..].TrimStart('/');
                        }
                        else if (string.Equals(normalizedPath, pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            return normalizedPath.StartsWith("/_/", StringComparison.Ordinal)
                                ? normalizedPath[3..]
                                : normalizedPath;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // The SourceLink resolver owns malformed-map reporting. Keep the PDB path usable.
            }
        }

        return normalizedPath.StartsWith("/_/", StringComparison.Ordinal)
            ? normalizedPath[3..]
            : normalizedPath;
    }

    private static string NormalizeSeparators(string path)
        => path.Replace('\\', '/');
}
