using System.Reflection.Metadata;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SourceLinkFetch;

/// <summary>
/// Parses SourceLink JSON and maps file paths to source URLs using glob patterns.
/// </summary>
public class SourceLinkResolver
{
    // SourceLink GUID: CC110556-A091-4D38-9FEC-25AB9A351A6A
    private static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private readonly Dictionary<string, string> _documentMappings;

    internal SourceLinkResolver(Dictionary<string, string> documentMappings)
    {
        _documentMappings = documentMappings;
    }

    /// <summary>
    /// Creates a SourceLinkResolver from a PDB metadata reader.
    /// Returns null if no SourceLink information is available.
    /// </summary>
    public static SourceLinkResolver? Create(MetadataReader pdbReader)
    {
        string? sourceLinkJson = ExtractSourceLinkJson(pdbReader);
        if (sourceLinkJson == null)
            return null;

        var mappings = ParseMappings(sourceLinkJson);
        if (mappings.Count == 0)
            return null;

        return new SourceLinkResolver(mappings);
    }

    /// <summary>
    /// Applies SourceLink URL pattern to convert a file path to a source URL.
    /// </summary>
    public string? ResolveUrl(string filePath)
    {
        filePath = filePath.Replace('\\', '/');

        foreach (var (pattern, urlTemplate) in _documentMappings)
        {
            int star = pattern.IndexOf('*');
            if (star >= 0)
            {
                // SourceLink allows a key at most one '*', and requires it to be the final
                // character, so a conformant key is a prefix and the match is a prefix test.
                // Testing the first '*' for finality rejects both violations at once: a second
                // '*' leaves the first one non-final. A key we reject is one no conformant
                // producer emits and one we could not honor unambiguously anyway.
                if (star != pattern.Length - 1)
                    continue;

                string prefix = pattern[..^1];
                if (filePath.StartsWith(prefix, StringComparison.Ordinal))
                    return urlTemplate.Replace("*", filePath[prefix.Length..]);
            }
            else if (filePath == pattern)
            {
                return urlTemplate;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the repository URL from SourceLink document mappings.
    /// </summary>
    public string? ExtractRepositoryUrl()
    {
        foreach (var (_, urlTemplate) in _documentMappings)
        {
            var match = Regex.Match(urlTemplate,
                @"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/");
            if (match.Success)
                return $"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}";
        }
        return null;
    }

    /// <summary>
    /// Extracts the commit hash from SourceLink URL patterns.
    /// </summary>
    public string? ExtractCommitHash()
    {
        foreach (var (_, urlTemplate) in _documentMappings)
        {
            var match = Regex.Match(urlTemplate, @"/([0-9a-f]{40})(?:/|$)", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;
        }
        return null;
    }

    /// <summary>
    /// Converts a raw.githubusercontent.com URL to a github.com browse URL.
    /// </summary>
    public static string? ConvertToGitHubBrowseUrl(string? rawUrl)
    {
        if (rawUrl == null) return null;

        var match = Regex.Match(rawUrl,
            @"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/(.+)");
        if (match.Success)
            return $"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}/raw/{match.Groups[3].Value}/{match.Groups[4].Value}";

        return rawUrl;
    }

    /// <summary>
    /// Extracts SourceLink JSON from a PDB metadata reader.
    /// </summary>
    internal static string? ExtractSourceLinkJson(MetadataReader reader)
    {
        foreach (CustomDebugInformationHandle handle in reader.CustomDebugInformation)
        {
            CustomDebugInformation info = reader.GetCustomDebugInformation(handle);
            Guid kind = reader.GetGuid(info.Kind);

            if (kind == SourceLinkGuid)
            {
                byte[] bytes = reader.GetBlobBytes(info.Value);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseMappings(string sourceLinkJson)
    {
        Dictionary<string, string> mappings = [];

        try
        {
            using var doc = JsonDocument.Parse(sourceLinkJson);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var prop in documents.EnumerateObject())
                {
                    string? url = prop.Value.GetString();
                    if (url != null)
                        mappings[prop.Name] = url;
                }
            }
        }
        catch
        {
            // Return empty mappings on parse error
        }

        return mappings;
    }
}
