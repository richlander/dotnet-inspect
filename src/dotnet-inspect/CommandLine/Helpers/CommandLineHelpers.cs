using DotnetInspector.Output;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using NuGetFetch;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Thrown when <c>--package-prefix</c> could not be expanded because the feed could not be
/// reached or understood.
/// </summary>
/// <remarks>
/// Distinct from a prefix that expands to nothing. An empty expansion is an answer: the feed was
/// searched and matched no package, so the command proceeds and exits 0. A failed expansion is
/// not an answer — the set of packages the user asked about is unknown, and continuing with an
/// empty set reports "nothing found" for packages that were never looked at. It is carried as an
/// exception because expansion happens while binding options, before a command has any result to
/// attach a failure to; Program.cs renders it as the standard <c>Error:</c> line and exit 1.
/// </remarks>
public sealed class PrefixResolutionException(string message) : Exception(message);

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

    /// <summary>
    /// Parses a -t value as either a numeric limit or null (glob patterns are handled separately).
    /// </summary>
    public static int? ParseTypeLimit(string? value)
        => value != null && int.TryParse(value, out var n) ? n : null;

    public static bool IsBooleanOptionEnabled(
        IReadOnlyList<string> tokens,
        string option)
    {
        bool enabled = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (token.Equals(option, StringComparison.Ordinal))
            {
                enabled =
                    i + 1 >= tokens.Count
                    || !bool.TryParse(
                        tokens[i + 1],
                        out bool value)
                    || value;
                continue;
            }

            if (token.Length > option.Length
                && token.StartsWith(option, StringComparison.Ordinal)
                && token[option.Length] is '=' or ':')
            {
                enabled =
                    !bool.TryParse(
                        token[(option.Length + 1)..],
                        out bool attachedValue)
                    || attachedValue;
            }
        }

        return enabled;
    }

    /// <summary>
    /// Creates a progress logger that writes to stderr when <paramref name="verbose"/>
    /// is set, or null otherwise. Centralizes the convention that verbose diagnostic
    /// output goes to stderr, keeping stdout reserved for machine-readable results.
    /// </summary>
    /// <remarks>
    /// This is the same channel as <see cref="DotnetInspector.Output.VerboseLogger"/>
    /// in delegate form, so it gets the same owner: progress text quotes paths
    /// and exception messages from untrusted input (issue #3319).
    /// </remarks>
    public static Action<string>? CreateVerboseLogger(bool verbose)
    {
        var logger = new VerboseLogger(verbose);
        return verbose ? logger.Log : null;
    }

    /// <summary>
    /// Resolves a package ID prefix to a list of matching package names via NuGet search.
    /// </summary>
    internal static async Task<SourceSelector.PackageReference[]> ResolvePrefixPackagesAsync(
        PackagePrefixRequest request,
        HttpClient client,
        bool verbose,
        NuGetSourceOptions? sourceOptions)
    {
        string prefix = request.Prefix;
        Action<string>? log = CreateVerboseLogger(verbose);

        log?.Invoke($"Resolving packages with prefix: {prefix}");

        List<NuGetSearchResult> results;
        SourceSelector.PackageReference[] packages;
        try
        {
            results = await NuGetSearchService.SearchByPrefixAsync(
                client,
                prefix,
                take: request.MaxPackages,
                prerelease: request.IncludePrerelease,
                log: log,
                sourceOptions: sourceOptions,
                fetchOptions: NuGetFetchOptions.FromRequestTimeout(
                    client.Timeout));
            packages = results.Select(result => new SourceSelector.PackageReference(result.PackageId)).ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException || IsPrefixResolutionFailure(ex))
        {
            // The command cannot proceed honestly: it does not know which packages the prefix
            // named. Reported as a clean CLI error rather than an escaping stack trace, and never
            // as an empty expansion, which would exit 0 having inspected nothing.
            throw new PrefixResolutionException(
                $"Could not resolve packages for prefix \"{prefix}\": {ex.Message}");
        }

        if (results.Count == 0)
        {
            CommandError.WriteWarning($"No packages found matching prefix \"{prefix}\"");
            return [];
        }

        WarnIfPackagePrefixLimitReached(results.Count, request);
        log?.Invoke($"Found {results.Count} package(s) matching prefix \"{prefix}\"");
        foreach (var package in packages)
            log?.Invoke($"  {package.PackageId}");

        return packages;
    }

    internal static void WarnIfPackagePrefixLimitReached(int resultCount, PackagePrefixRequest request)
    {
        if (resultCount < request.MaxPackages)
            return;

        CommandError.WriteWarning(
            $"Package prefix \"{request.Prefix}\" reached the {request.MaxPackages}-package search limit; additional matches may be omitted.");
    }

    internal static bool IsPrefixResolutionFailure(Exception error) =>
        error is HttpRequestException or JsonException or InvalidOperationException
            or TaskCanceledException or IOException or TimeoutException;

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

    public static bool IsExplicitLibraryPath(string value) =>
        Path.IsPathRooted(value)
        || (value.Length > 0 && value[0] is '/' or '\\')
        || value.StartsWith("./", StringComparison.Ordinal)
        || value.StartsWith(@".\", StringComparison.Ordinal)
        || value.StartsWith("../", StringComparison.Ordinal)
        || value.StartsWith(@"..\", StringComparison.Ordinal)
        || (value.Length >= 2
            && char.IsAsciiLetter(value[0])
            && value[1] == ':');

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
