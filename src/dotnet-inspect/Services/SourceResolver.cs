using System.Reflection.Metadata;
using DotnetInspector.CommandLine;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Services;

/// <summary>
/// Resolves source specifications (package, assembly, platform) from command arguments.
/// Handles file path classification, platform resolution, and qualified type name parsing.
/// </summary>
public static class SourceResolver
{
    /// <summary>
    /// Result of source resolution containing resolved paths and any extracted type information.
    /// </summary>
    public record ResolvedSource(
        string? PackagePath,
        string? AssemblyPath,
        string? PlatformAssembly,
        string? FrameworkOverride,
        string? TypeName,
        bool VersionError = false,
        string? VersionErrorMessage = null);

    /// <summary>
    /// Kind of local source found by peel-and-probe.
    /// </summary>
    internal enum LocalSourceKind { Platform, CachedPackage }

    /// <summary>
    /// Result of a local peel-and-probe: the resolved source name, the remaining suffix, and what kind of source matched.
    /// </summary>
    internal record LocalProbeResult(string SourceName, string Remainder, LocalSourceKind Kind);

    /// <summary>
    /// Determines if a library option is a selector (bare .dll name) vs a full path.
    /// </summary>
    public static bool IsLibrarySelector(string? assembly, string? package)
        => assembly != null && package == null
            && !assembly.Contains('/') && !assembly.Contains('\\')
            && assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines if explicit source options were provided.
    /// </summary>
    public static bool HasExplicitSource(string? package, string? assembly, string? platform, bool isLibrarySelector)
        => package != null || (assembly != null && !isLibrarySelector) || platform != null;

    /// <summary>
    /// Peels segments from the right of a dotted name, probing each candidate
    /// against local sources: dotnet hive, dotnet-inspect cache, NuGet global cache.
    /// Returns the first hit with its remainder, or null if nothing matches locally.
    /// </summary>
    internal static LocalProbeResult? TryProbeLocalQualifiedName(string name)
    {
        // Require at least 2 dots (e.g., System.Text.Json.JsonSerializer).
        // Single-dot names like "System.CommandLine" are ambiguous: could be
        // a package name or assembly + type. Treat as a package to avoid
        // false positives from greedy matches like System + CommandLine.
        int dotCount = 0;
        for (int j = 0; j < name.Length; j++)
            if (name[j] == '.') dotCount++;
        if (dotCount < 2) return null;

        string? platformCandidate = null;

        for (int i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] != '.') continue;
            var candidate = name[..i];
            var remainder = name[(i + 1)..];

            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(remainder))
                continue;

            // Space 1: dotnet hive (ref packs + shared runtime)
            var (path, _, _, _) = PlatformResolver.ResolveAssembly(candidate);
            if (path != null)
            {
                if (AssemblyHasType(path, remainder))
                    return new LocalProbeResult(candidate, remainder, LocalSourceKind.Platform);
                // Library exists but doesn't contain the type — remember the remainder
                // and keep probing shorter prefixes
                platformCandidate ??= remainder;
                continue;
            }

            // Space 2 & 3: dotnet-inspect cache + NuGet global cache
            if (NuGetCache.TryGetLatestCachedVersion(candidate) != null)
                return new LocalProbeResult(candidate, remainder, LocalSourceKind.CachedPackage);
        }

        // Fallback: a platform library matched but the type wasn't in it.
        // Scan all runtime ref assemblies for the type (e.g., INumber<T> is in
        // System.Runtime, not System.Numerics).
        if (platformCandidate != null)
        {
            var runtimeLib = PlatformResolver.FindLibraryContainingType(platformCandidate);
            if (runtimeLib != null)
                return new LocalProbeResult(runtimeLib, platformCandidate, LocalSourceKind.Platform);
        }

        return null;
    }

    /// <summary>
    /// Lightweight type existence check using raw metadata.
    /// </summary>
    internal static bool AssemblyHasType(string assemblyPath, string typeName)
        => PlatformResolver.HasType(assemblyPath, typeName);

    /// <summary>
    /// Resolves source from positional arguments and explicit options.
    /// Handles file classification, platform resolution, and version detection.
    /// </summary>
    /// <param name="args">Positional arguments from command line.</param>
    /// <param name="explicitPackage">Explicit --package value.</param>
    /// <param name="explicitAssembly">Explicit --library value.</param>
    /// <param name="explicitPlatform">Explicit --platform value.</param>
    /// <param name="verbose">Whether to log verbose messages.</param>
    /// <param name="tryQualifiedTypeName">Whether to attempt parsing qualified type names (Type command only).</param>
    /// <returns>Resolved source information.</returns>
    public static async Task<ResolvedSource> ResolveAsync(
        string[] args,
        string? explicitPackage,
        string? explicitAssembly,
        string? explicitPlatform,
        bool verbose,
        bool tryQualifiedTypeName = false)
    {
        bool isLibrarySelector = IsLibrarySelector(explicitAssembly, explicitPackage);
        bool hasExplicitSource = HasExplicitSource(explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

        string? packagePath = explicitPackage;
        string? assemblyPath = explicitAssembly;
        string? platformAssembly = explicitPlatform;
        string? typeName = null;
        string? frameworkOverride = null;

        if (hasExplicitSource)
        {
            // With explicit source, first arg is type name
            if (args.Length >= 1) typeName = args[0];
        }
        else
        {
            // Without explicit source, first arg is package, second is type
            if (args.Length >= 1) packagePath = args[0];
            if (args.Length >= 2) typeName = args[1];

            // Normalize C#-style generic notation to CLR backtick notation.
            // e.g., "Dictionary<TKey,TValue>" → "Dictionary`2", "List<T>" → "List`1"
            if (packagePath != null) packagePath = GenericTypeNameConverter.Convert(packagePath);
            if (typeName != null) typeName = GenericTypeNameConverter.Convert(typeName);

            // Check for version number passed as separate argument
            if (CommandLineHelpers.LooksLikeVersionNumber(typeName))
            {
                return new ResolvedSource(
                    packagePath, assemblyPath, platformAssembly, null, null,
                    VersionError: true,
                    VersionErrorMessage: $"Error: '{typeName}' looks like a version number. Use '{packagePath}@{typeName}' to specify a version.");
            }

            // Detect bare type names (e.g., "Dictionary`2", "List`1") passed without a source.
            // Backticks never appear in package names — they're .NET generic arity notation.
            // Try to auto-resolve the containing library from platform ref assemblies.
            if (packagePath != null && typeName == null && packagePath.Contains('`'))
            {
                var library = PlatformResolver.FindLibraryContainingType(packagePath);
                if (library != null)
                {
                    typeName = packagePath;
                    packagePath = null;
                    platformAssembly = library;
                }
                else
                {
                    return new ResolvedSource(
                        packagePath, assemblyPath, platformAssembly, null, null,
                        VersionError: true,
                        VersionErrorMessage: $"Error: '{packagePath}' looks like a type name but was not found in platform libraries. Specify the source: 'source <package> {packagePath}'.");
                }
            }

            // Classify file paths
            if (CommandLineHelpers.TryClassifyAsFilePath(packagePath, out var dllPath, out var nupkgPath))
            {
                if (dllPath != null)
                {
                    assemblyPath = dllPath;
                    packagePath = null;
                }
                else if (nupkgPath != null)
                {
                    packagePath = nupkgPath;
                }
            }
            else if (packagePath != null)
            {
                var bareName = packagePath.Contains('@') ? packagePath[..packagePath.IndexOf('@')] : packagePath;
                var explicitVersion = packagePath.Contains('@') ? packagePath[(packagePath.IndexOf('@') + 1)..] : null;

                // Try platform resolution for whole name (can go remote for platform candidates)
                if (PlatformResolver.IsPlatformCandidate(bareName))
                {
                    var client = HttpClientFactory.Shared;
                    Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;

                    // Build framework spec if explicit version given
                    string? frameworkSpec = null;
                    if (explicitVersion != null)
                    {
                        var (_, discoveredFramework, _, _) = PlatformResolver.ResolveAssembly(bareName);
                        if (discoveredFramework != null)
                            frameworkSpec = $"{discoveredFramework}@{explicitVersion}";
                    }

                    // Resolve assembly (local-first, then network if needed)
                    var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
                        bareName, client, log, frameworkSpec);

                    if (resolvedPath != null && resolvedError == null)
                    {
                        platformAssembly = bareName;
                        packagePath = null;
                        frameworkOverride = frameworkSpec;
                    }
                }

                // If still unresolved, try local peel-and-probe across all spaces
                if (tryQualifiedTypeName && typeName == null
                    && packagePath != null && platformAssembly == null && assemblyPath == null)
                {
                    var probe = TryProbeLocalQualifiedName(bareName);
                    if (probe != null)
                    {
                        typeName = probe.Remainder;
                        if (probe.Kind == LocalSourceKind.Platform)
                        {
                            platformAssembly = probe.SourceName;
                            packagePath = null;
                        }
                        else
                        {
                            packagePath = probe.SourceName;
                        }
                    }
                }
            }
        }

        return new ResolvedSource(packagePath, assemblyPath, platformAssembly, frameworkOverride, typeName);
    }
}
