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

    public enum SourceResolutionMethod
    {
        /// <summary>Source resolved from method debug info (sequence points).</summary>
        SourceLink,
        /// <summary>Source inferred from PDB document name matching type name.</summary>
        Inferred
    }

    public record TypeSourceInfo(
        string? SourceFilePath,
        string? SourceUrl,
        int? LineNumber,
        string? GitHubBrowseUrl,
        SourceResolutionMethod ResolutionMethod = SourceResolutionMethod.SourceLink
    )
    {
        /// <summary>
        /// Additional source files for partial types (e.g., JObject.Async.cs alongside JObject.cs).
        /// Only populated when type has multiple source files.
        /// </summary>
        public List<PartialSourceFile>? AdditionalSourceFiles { get; init; }
        
        /// <summary>
        /// Indicates whether this type is defined across multiple partial files.
        /// </summary>
        public bool IsPartialType => AdditionalSourceFiles?.Count > 0;
    }
    
    /// <summary>
    /// Represents a source file that is part of a partial type definition.
    /// </summary>
    public record PartialSourceFile(
        string FilePath,
        string? SourceUrl,
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
    /// Falls back to document name matching for interfaces/abstract types without implementations.
    /// Also collects all source files for partial types.
    /// </summary>
    public TypeSourceInfo? ResolveTypeSource(MetadataReader metadata, MetadataReader pdb, TypeDefinitionHandle typeHandle)
    {
        var typeDef = metadata.GetTypeDefinition(typeHandle);
        var typeName = metadata.GetString(typeDef.Name);

        // Collect ALL unique source files from all methods of this type
        var allSourceFiles = CollectAllSourceFiles(metadata, pdb, typeHandle);
        
        // Also check PDB documents for files matching the type name pattern
        // This catches files that may not have any methods (e.g., partial with only fields)
        var documentFiles = FindDocumentsMatchingTypeName(pdb, typeName);
        foreach (var docFile in documentFiles)
        {
            if (!allSourceFiles.ContainsKey(docFile.FilePath))
            {
                allSourceFiles[docFile.FilePath] = docFile;
            }
        }

        if (allSourceFiles.Count == 0)
        {
            // Fallback: search all documents for a file that matches the type name
            // This works for interfaces and abstract types that have no method implementations
            return ResolveTypeSourceByDocumentName(pdb, typeName);
        }

        // Determine the primary file (prefer {TypeName}.cs over {TypeName}.*.cs)
        var primaryFile = SelectPrimarySourceFile(allSourceFiles.Values.ToList(), typeName);
        
        // Build additional source files list (excluding primary)
        List<PartialSourceFile>? additionalFiles = null;
        if (allSourceFiles.Count > 1)
        {
            additionalFiles = allSourceFiles.Values
                .Where(f => f.FilePath != primaryFile.FilePath)
                .Select(f => new PartialSourceFile(f.FilePath, f.SourceUrl, f.GitHubBrowseUrl))
                .ToList();
        }

        return new TypeSourceInfo(
            primaryFile.FilePath,
            primaryFile.SourceUrl,
            null, // Line number not meaningful for type-level
            primaryFile.GitHubBrowseUrl,
            SourceResolutionMethod.SourceLink
        )
        {
            AdditionalSourceFiles = additionalFiles
        };
    }
    
    /// <summary>
    /// Collects all unique source files from all methods of a type.
    /// </summary>
    private Dictionary<string, PartialSourceFile> CollectAllSourceFiles(
        MetadataReader metadata, MetadataReader pdb, TypeDefinitionHandle typeHandle)
    {
        var sourceFiles = new Dictionary<string, PartialSourceFile>(StringComparer.OrdinalIgnoreCase);
        var typeDef = metadata.GetTypeDefinition(typeHandle);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var sourceInfo = ResolveMethodSource(pdb, methodHandle);
            if (sourceInfo?.SourceFilePath != null && !sourceFiles.ContainsKey(sourceInfo.SourceFilePath))
            {
                sourceFiles[sourceInfo.SourceFilePath] = new PartialSourceFile(
                    sourceInfo.SourceFilePath,
                    sourceInfo.SourceUrl,
                    sourceInfo.GitHubBrowseUrl
                );
            }
        }

        return sourceFiles;
    }
    
    /// <summary>
    /// Finds PDB documents matching the type name pattern (e.g., JObject.cs, JObject.Async.cs).
    /// </summary>
    private List<PartialSourceFile> FindDocumentsMatchingTypeName(MetadataReader pdb, string typeName)
    {
        var matches = new List<PartialSourceFile>();
        
        foreach (var docHandle in pdb.Documents)
        {
            var document = pdb.GetDocument(docHandle);
            string filePath = pdb.GetString(document.Name);
            string fileName = Path.GetFileName(filePath);
            
            // Match {TypeName}.cs or {TypeName}.*.cs patterns
            if (fileName.Equals($"{typeName}.cs", StringComparison.OrdinalIgnoreCase) ||
                (fileName.StartsWith($"{typeName}.", StringComparison.OrdinalIgnoreCase) && 
                 fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                string? sourceUrl = ApplySourceLinkMapping(filePath);
                string? browseUrl = ConvertToGitHubRawUrl(sourceUrl);
                matches.Add(new PartialSourceFile(filePath, sourceUrl, browseUrl));
            }
        }

        return matches;
    }
    
    /// <summary>
    /// Selects the primary source file from a list of candidates.
    /// Prefers {TypeName}.cs over {TypeName}.*.cs patterns.
    /// </summary>
    private static PartialSourceFile SelectPrimarySourceFile(List<PartialSourceFile> files, string typeName)
    {
        // Prefer exact match: {TypeName}.cs
        var primaryPattern = $"{typeName}.cs";
        var primary = files.FirstOrDefault(f => 
            Path.GetFileName(f.FilePath).Equals(primaryPattern, StringComparison.OrdinalIgnoreCase));
        
        if (primary != null)
            return primary;

        // Otherwise, return the first one (or the one with shortest name)
        return files.OrderBy(f => Path.GetFileName(f.FilePath).Length).First();
    }

    /// <summary>
    /// Attempts to resolve source info by searching PDB documents for a matching file name.
    /// Used for interfaces and types without method implementations.
    /// </summary>
    private TypeSourceInfo? ResolveTypeSourceByDocumentName(MetadataReader pdb, string typeName)
    {
        // Look for documents that match the type name pattern
        // e.g., for "IContractResolver", look for "*IContractResolver.cs" or similar
        foreach (var docHandle in pdb.Documents)
        {
            var document = pdb.GetDocument(docHandle);
            string filePath = pdb.GetString(document.Name);
            
            // Check if the file name matches the type name
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                string? sourceUrl = ApplySourceLinkMapping(filePath);
                string? browseUrl = ConvertToGitHubRawUrl(sourceUrl);
                return new TypeSourceInfo(filePath, sourceUrl, null, browseUrl, SourceResolutionMethod.Inferred);
            }
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

            // Apply SourceLink mapping (no line number for type-level URLs - they're not useful)
            string? sourceUrl = ApplySourceLinkMapping(filePath);
            string? browseUrl = ConvertToGitHubRawUrl(sourceUrl);

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

    private static string? ConvertToGitHubRawUrl(string? rawUrl) => GitHubUrlResolver.ConvertToGitHubRawUrl(rawUrl);

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
