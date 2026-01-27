using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Resolves types and members to their source file locations using SourceLink information from PDBs.
/// </summary>
public class SourceLinkResolver
{
    // SourceLink GUID: CC110556-A091-4D38-9FEC-25AB9A351A6A
    private static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    private readonly Dictionary<string, string> _documentMappings;

    public record TypeSourceInfo(
        string? SourceFilePath,
        string? SourceUrl,
        int? LineNumber,
        string? GitHubBrowseUrl
    );

    private SourceLinkResolver(Dictionary<string, string> documentMappings)
    {
        _documentMappings = documentMappings;
    }

    /// <summary>
    /// Creates a SourceLinkResolver from a PDB metadata reader.
    /// Returns null if no SourceLink information is available.
    /// </summary>
    public static SourceLinkResolver? Create(MetadataReader pdbReader)
    {
        string? sourceLinkJson = ExtractSourceLinkFromReader(pdbReader);
        if (sourceLinkJson == null)
            return null;

        var mappings = ParseSourceLinkMappings(sourceLinkJson);
        if (mappings.Count == 0)
            return null;

        return new SourceLinkResolver(mappings);
    }

    /// <summary>
    /// Resolves source information for a type by finding a method with debug info.
    /// </summary>
    public TypeSourceInfo? ResolveTypeSource(MetadataReader metadata, MetadataReader pdb, TypeDefinitionHandle typeHandle)
    {
        var typeDef = metadata.GetTypeDefinition(typeHandle);

        // Iterate through methods to find one with debug info
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var sourceInfo = ResolveMethodSource(pdb, methodHandle);
            if (sourceInfo != null)
                return sourceInfo;
        }

        return null;
    }

    /// <summary>
    /// Resolves source information for a specific method.
    /// </summary>
    public TypeSourceInfo? ResolveMethodSource(MetadataReader pdb, MethodDefinitionHandle methodHandle)
    {
        // Method debug information row ID matches the method row ID
        var debugInfoHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));

        try
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugInfoHandle);

            if (debugInfo.Document.IsNil)
                return null;

            var document = pdb.GetDocument(debugInfo.Document);
            string filePath = pdb.GetString(document.Name);

            // Get the first sequence point for line number
            int? lineNumber = null;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (!sp.IsHidden)
                {
                    lineNumber = sp.StartLine;
                    break;
                }
            }

            // Apply SourceLink mapping
            string? sourceUrl = ApplySourceLinkMapping(filePath);
            string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl, lineNumber);

            return new TypeSourceInfo(filePath, sourceUrl, lineNumber, browseUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies SourceLink URL pattern to convert a file path to a source URL.
    /// </summary>
    private string? ApplySourceLinkMapping(string filePath)
    {
        // Normalize path separators
        filePath = filePath.Replace('\\', '/');

        foreach (var (pattern, urlTemplate) in _documentMappings)
        {
            // SourceLink patterns use * as wildcard
            // e.g., "/_/*" -> "https://raw.githubusercontent.com/dotnet/runtime/abc123/*"
            if (pattern.Contains('*'))
            {
                // Convert pattern to regex: "/_/*" becomes "^/_/(.*)$"
                string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", "(.*)") + "$";
                var match = Regex.Match(filePath, regexPattern);

                if (match.Success && match.Groups.Count > 1)
                {
                    // Replace * in URL template with captured group
                    string captured = match.Groups[1].Value;
                    return urlTemplate.Replace("*", captured);
                }
            }
            else if (filePath == pattern)
            {
                return urlTemplate;
            }
        }

        return null;
    }

    /// <summary>
    /// Converts a raw.githubusercontent.com URL to a github.com browse URL with line number.
    /// </summary>
    private static string? ConvertToGitHubBrowseUrl(string? rawUrl, int? lineNumber)
    {
        if (rawUrl == null)
            return null;

        // Convert raw.githubusercontent.com/owner/repo/commit/path
        // to github.com/owner/repo/blob/commit/path#L123
        var match = Regex.Match(rawUrl,
            @"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/(.+)");

        if (match.Success)
        {
            string owner = match.Groups[1].Value;
            string repo = match.Groups[2].Value;
            string commit = match.Groups[3].Value;
            string path = match.Groups[4].Value;

            string browseUrl = $"https://github.com/{owner}/{repo}/blob/{commit}/{path}";
            if (lineNumber.HasValue)
            {
                browseUrl += $"#L{lineNumber}";
            }
            return browseUrl;
        }

        // For Azure DevOps or other providers, just append line number if present
        if (lineNumber.HasValue && !rawUrl.Contains('#'))
        {
            return rawUrl + $"#L{lineNumber}";
        }

        return rawUrl;
    }

    private static string? ExtractSourceLinkFromReader(MetadataReader reader)
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

    private static Dictionary<string, string> ParseSourceLinkMappings(string sourceLinkJson)
    {
        var mappings = new Dictionary<string, string>();

        try
        {
            using var doc = JsonDocument.Parse(sourceLinkJson);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var prop in documents.EnumerateObject())
                {
                    string? url = prop.Value.GetString();
                    if (url != null)
                    {
                        mappings[prop.Name] = url;
                    }
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
