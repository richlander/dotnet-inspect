using DotnetInspector.Commands;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Result of a type lookup operation.
/// Contains all information needed to inspect the type.
/// </summary>
public record TypeLookupResult(
    string FullTypeName,
    string AssemblyPath,
    string AssemblyName,
    string? Framework,
    string? Version);

/// <summary>
/// Unified service for locating types across packages, assemblies, and platform frameworks.
/// Used by find, member, and type commands to resolve type names to assembly paths.
/// </summary>
public static class TypeLookupService
{
    /// <summary>
    /// Framework name mappings (full names to short names).
    /// </summary>
    private static readonly Dictionary<string, string> FrameworkNameMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.NETCore.App"] = "runtime",
        ["Microsoft.NETCore.App.Ref"] = "runtime",
        ["Microsoft.AspNetCore.App"] = "aspnetcore",
        ["Microsoft.AspNetCore.App.Ref"] = "aspnetcore",
        ["NETStandard.Library"] = "netstandard",
        ["NETStandard.Library.Ref"] = "netstandard"
    };

    /// <summary>
    /// Attempts to recognize a framework name and return the short form.
    /// </summary>
    public static string? TryMapFrameworkName(string name)
    {
        if (FrameworkNameMappings.TryGetValue(name, out var shortName))
            return shortName;

        // Already a short name?
        if (PlatformResolver.FrameworkMappings.ContainsKey(name))
            return name;

        return null;
    }

    /// <summary>
    /// Searches for a type by name across the specified scope.
    /// Returns the type's location (assembly path, full name, framework, etc.).
    /// </summary>
    public static async Task<TypeLookupResult?> FindTypeAsync(
        string typeName,
        string[] platformFrameworks,
        HttpClient httpClient,
        VerboseLogger logger,
        List<string> tempDirs)
    {
        // Ensure frameworks are available locally
        var requests = PlatformPackService.GetMissingPackRequests(platformFrameworks);
        if (requests.Count > 0)
        {
            await foreach (var _ in PlatformPackService.EnsurePacksAsync(requests, httpClient, logger.Log))
            {
            }
        }

        // Search for the type across all framework assemblies
        var options = new FindOptions
        {
            Pattern = typeName,
            PlatformFrameworks = platformFrameworks,
            Limit = 1 // We only need the first match
        };

        var results = await TypeSearchService.CollectTypesAsync(options, typeName, logger, tempDirs, httpClient);

        if (results.Count == 0)
            return null;

        var match = results[0];

        // Resolve the assembly path
        var assemblyPath = await ResolveAssemblyPathAsync(match, httpClient, logger);
        if (assemblyPath == null)
            return null;

        return new TypeLookupResult(
            match.FullName,
            assemblyPath,
            match.Assembly ?? "",
            match.Source,
            match.SourceVersion);
    }

    /// <summary>
    /// Searches for a type with fallback to partial matching.
    /// Returns multiple candidates if exact match not found.
    /// </summary>
    public static async Task<(TypeLookupResult? Match, List<TypeSearchResult> Suggestions)> FindTypeWithSuggestionsAsync(
        string typeName,
        string[] platformFrameworks,
        HttpClient httpClient,
        VerboseLogger logger,
        List<string> tempDirs)
    {
        // First try exact match
        var match = await FindTypeAsync(typeName, platformFrameworks, httpClient, logger, tempDirs);
        if (match != null)
            return (match, []);

        // No exact match - collect all types and find suggestions
        var options = new FindOptions
        {
            Pattern = "*", // Get all types
            PlatformFrameworks = platformFrameworks
        };

        var allTypes = await TypeSearchService.CollectTypesAsync(options, null, logger, tempDirs, httpClient);
        var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();

        // Find similar types
        var suggestions = TypeMatcher.FindClosest(typeNames, typeName, minSimilarity: 0.5, maxResults: 5).ToList();
        if (suggestions.Count == 0)
            return (null, []);

        // Map suggestions back to TypeSearchResult
        var suggestionSet = suggestions.Select(s => s.Name).ToHashSet();
        var suggestionResults = allTypes
            .Where(t => suggestionSet.Contains(t.FullName))
            .DistinctBy(t => t.FullName)
            .ToList();

        return (null, suggestionResults);
    }

    /// <summary>
    /// Resolves a TypeSearchResult to an actual assembly path.
    /// </summary>
    private static async Task<string?> ResolveAssemblyPathAsync(
        TypeSearchResult result,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        if (result.Assembly == null || result.Source == null)
            return null;

        // Try to map Source to a framework short name
        var framework = TryMapFrameworkName(result.Source) ?? result.Source;

        // Use PlatformResolver to get the actual path
        var (assemblyPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
            result.Assembly,
            httpClient,
            logger.Log,
            framework);

        if (error != null)
        {
            logger.Log($"Warning: Could not resolve assembly path: {error}");
            return null;
        }

        return assemblyPath;
    }
}
