using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.RegularExpressions;
using SLF = SourceLinkFetch;

namespace DotnetInspector.Metadata;

/// <summary>
/// Resolves types and members to their source file locations using SourceLink information from PDBs.
/// Delegates URL mapping to the SourceLinkFetch library.
/// </summary>
public class SourceLinkResolver
{
    private readonly SLF.SourceLinkResolver _slfResolver;

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
        public List<PartialSourceFile> AdditionalSourceFiles { get; init; } = [];

        /// <summary>
        /// Indicates whether this type is defined across multiple partial files.
        /// </summary>
        public bool IsPartialType => AdditionalSourceFiles.Count > 0;
    }

    /// <summary>
    /// Represents a source file that is part of a partial type definition.
    /// </summary>
    public record PartialSourceFile(
        string FilePath,
        string? SourceUrl,
        string? GitHubBrowseUrl
    );

    /// <summary>
    /// Source location for a method, including the full line range from sequence points.
    /// </summary>
    public record MethodSourceInfo(
        string FilePath,
        string? SourceUrl,
        int StartLine,
        int EndLine
    );

    private SourceLinkResolver(SLF.SourceLinkResolver slfResolver)
    {
        _slfResolver = slfResolver;
    }

    /// <summary>
    /// Creates a SourceLinkResolver from a PDB metadata reader.
    /// Returns null if no SourceLink information is available.
    /// </summary>
    public static SourceLinkResolver? Create(MetadataReader pdbReader)
    {
        var slfResolver = SLF.SourceLinkResolver.Create(pdbReader);
        if (slfResolver is null)
            return null;

        return new SourceLinkResolver(slfResolver);
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
        List<PartialSourceFile> additionalFiles = [];
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
    /// Resolves source information for a type by name, without requiring a TypeDefinitionHandle.
    /// Finds the type definition handle internally.
    /// </summary>
    public TypeSourceInfo? ResolveTypeSource(MetadataReader metadata, MetadataReader pdb, string typeName)
    {
        var typeHandle = FindTypeDefinitionHandle(metadata, typeName);
        if (typeHandle == null)
            return null;

        return ResolveTypeSource(metadata, pdb, typeHandle.Value);
    }

    /// <summary>
    /// Extracts the repository URL from SourceLink document mappings.
    /// </summary>
    public string? ExtractRepositoryUrl()
        => _slfResolver.ExtractRepositoryUrl();

    /// <summary>
    /// Extracts the repository URL from a PDB reader's SourceLink information.
    /// </summary>
    public static string? ExtractRepositoryUrl(MetadataReader pdbReader)
    {
        var resolver = Create(pdbReader);
        return resolver?.ExtractRepositoryUrl();
    }

    /// <summary>
    /// Extracts the commit hash from SourceLink URL patterns.
    /// </summary>
    public string? ExtractCommitHash()
        => _slfResolver.ExtractCommitHash();

    /// <summary>
    /// Finds a TypeDefinitionHandle by type name in the metadata reader.
    /// </summary>
    private static TypeDefinitionHandle? FindTypeDefinitionHandle(MetadataReader reader, string typeName)
    {
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            string name = reader.GetString(typeDef.Name);
            string fullName = reader.GetFullTypeName(typeDef);

            if (fullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                return typeDefHandle;
            }
        }
        return null;
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
        List<PartialSourceFile> matches = [];

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
                string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);
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
        foreach (var docHandle in pdb.Documents)
        {
            var document = pdb.GetDocument(docHandle);
            string filePath = pdb.GetString(document.Name);

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                string? sourceUrl = ApplySourceLinkMapping(filePath);
                string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);
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
        var debugInfoHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));

        try
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugInfoHandle);

            if (debugInfo.Document.IsNil)
                return null;

            var document = pdb.GetDocument(debugInfo.Document);
            string filePath = pdb.GetString(document.Name);

            int? lineNumber = null;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (!sp.IsHidden)
                {
                    lineNumber = sp.StartLine;
                    break;
                }
            }

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            string? browseUrl = ConvertToGitHubBrowseUrl(sourceUrl);

            return new TypeSourceInfo(filePath, sourceUrl, lineNumber, browseUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the full source line range for a method using all sequence points.
    /// Returns null if the method has no debug info.
    /// </summary>
    public MethodSourceInfo? ResolveMethodSourceRange(MetadataReader pdb, MethodDefinitionHandle methodHandle)
    {
        var debugInfoHandle = MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle));

        try
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugInfoHandle);

            if (debugInfo.Document.IsNil)
                return null;

            var document = pdb.GetDocument(debugInfo.Document);
            string filePath = pdb.GetString(document.Name);

            int minLine = int.MaxValue, maxLine = 0;
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.IsHidden) continue;
                if (sp.StartLine < minLine) minLine = sp.StartLine;
                if (sp.EndLine > maxLine) maxLine = sp.EndLine;
            }

            if (minLine == int.MaxValue)
                return null;

            string? sourceUrl = ApplySourceLinkMapping(filePath);
            return new MethodSourceInfo(filePath, sourceUrl, minLine, maxLine);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies SourceLink URL pattern to convert a file path to a source URL.
    /// </summary>
    public string? ApplySourceLinkMapping(string filePath)
        => _slfResolver.ResolveUrl(filePath);

    /// <summary>
    /// Converts a raw.githubusercontent.com URL to a github.com browse URL.
    /// </summary>
    private static string? ConvertToGitHubBrowseUrl(string? rawUrl)
        => SLF.SourceLinkResolver.ConvertToGitHubBrowseUrl(rawUrl);

}
