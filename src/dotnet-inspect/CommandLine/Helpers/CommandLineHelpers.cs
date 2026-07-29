using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using DotnetInspector.Packages;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Shared helper methods for command-line argument processing.
/// Provides file path classification and version number detection.
/// </summary>
public static class CommandLineHelpers
{
    public const string PlatformLibraryOptionName = "--platform-library";

    public static Option<bool> CreatePlatformSearchOption()
        => new("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard), or one platform library when followed by a value" };

    public static Option<string[]> CreatePlatformLibrarySearchOption()
        => new(PlatformLibraryOptionName)
        {
            Description = "Search one or more platform libraries",
            Hidden = true
        };

    public static (bool AllPlatformFrameworks, string[] PlatformAssemblies) ParsePlatformSearchOption(
        ParseResult parseResult,
        Option<bool> platformOption,
        Option<string[]> platformLibraryOption)
    {
        var values = parseResult.GetValue(platformLibraryOption) ?? [];
        return (parseResult.GetValue(platformOption), values);
    }

    /// <summary>
    /// Parses a -t value as either a numeric limit or null (glob patterns are handled separately).
    /// </summary>
    public static int? ParseTypeLimit(string? value)
        => value != null && int.TryParse(value, out var n) ? n : null;

    /// <summary>
    /// Creates a progress logger that writes to stderr when <paramref name="verbose"/>
    /// is set, or null otherwise. Centralizes the convention that verbose diagnostic
    /// output goes to stderr, keeping stdout reserved for machine-readable results.
    /// </summary>
    public static Action<string>? CreateVerboseLogger(bool verbose)
        => verbose ? msg => Console.Error.WriteLine(msg) : null;

    /// <summary>
    /// Resolves a package ID prefix and merges with existing packages.
    /// </summary>
    public static async Task<string[]> MergeWithPrefixPackagesAsync(string[] packages, string? prefix, bool verbose)
    {
        if (prefix == null)
            return packages;

        var prefixPackages = await ResolvePrefixPackagesAsync(prefix, verbose);
        return [.. packages, .. prefixPackages];
    }

    /// <summary>
    /// Resolves a package ID prefix to a list of matching package names via NuGet search.
    /// </summary>
    private static async Task<string[]> ResolvePrefixPackagesAsync(string prefix, bool verbose)
    {
        Action<string>? log = CreateVerboseLogger(verbose);
        var client = HttpClientFactory.Shared;

        log?.Invoke($"Resolving packages with prefix: {prefix}");

        List<NuGetSearchResult> results;
        try
        {
            results = await NuGetSearchService.SearchByPrefixAsync(client, prefix, log: log);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            or InvalidOperationException or TaskCanceledException)
        {
            // Prefix expansion is a convenience over arguments the user could have typed out.
            // A feed that cannot be reached or parsed degrades to "no matches" with a warning
            // rather than aborting the command the prefix was only decorating.
            Console.Error.WriteLine($"Warning: could not resolve prefix \"{prefix}\": {ex.Message}");
            return [];
        }

        if (results.Count == 0)
        {
            Console.Error.WriteLine($"Warning: No packages found matching prefix \"{prefix}\"");
            return [];
        }

        log?.Invoke($"Found {results.Count} package(s) matching prefix \"{prefix}\"");
        var packageNames = results.Select(r => r.PackageId).ToArray();

        foreach (var pkg in packageNames)
            log?.Invoke($"  {pkg}");

        return packageNames;
    }

    /// <summary>
    /// Classifies a positional argument by file extension.
    /// Returns true if the positional was a file path (.dll or .nupkg) and sets the appropriate out parameter.
    /// </summary>
    /// <param name="positional">The positional argument to classify.</param>
    /// <param name="libraryPath">Set to the path if it ends with .dll.</param>
    /// <param name="packagePath">Set to the path if it ends with .nupkg.</param>
    /// <returns>True if the argument was classified as a file path.</returns>
    public static bool TryClassifyAsFilePath(string? positional, out string? libraryPath, out string? packagePath)
    {
        libraryPath = null;
        packagePath = null;

        if (string.IsNullOrEmpty(positional))
            return false;

        if (positional.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            libraryPath = positional;
            return true;
        }

        if (positional.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            packagePath = positional;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the value looks like a version number (e.g. "2.0.0", "8.0.0-preview.1").
    /// Used to detect when a user passes a version as a positional argument instead of using the @ syntax.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value appears to be a version number.</returns>
    public static bool LooksLikeVersionNumber(string? value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsAsciiDigit(value[0]))
            return false;

        // Must contain at least one dot followed by a digit (e.g. "2.0")
        for (int i = 1; i < value.Length - 1; i++)
        {
            if (value[i] == '.' && char.IsAsciiDigit(value[i + 1]))
                return true;
        }

        return false;
    }
}
