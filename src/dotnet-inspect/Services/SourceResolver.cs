using DotnetInspector.CommandLine;
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

            // Check for version number passed as separate argument
            if (CommandLineHelpers.LooksLikeVersionNumber(typeName))
            {
                return new ResolvedSource(
                    packagePath, assemblyPath, platformAssembly, null, null,
                    VersionError: true,
                    VersionErrorMessage: $"Error: '{typeName}' looks like a version number. Use '{packagePath}@{typeName}' to specify a version.");
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
            // Try platform resolution
            else if (packagePath != null && PlatformResolver.IsPlatformCandidate(
                packagePath.Contains('@') ? packagePath[..packagePath.IndexOf('@')] : packagePath))
            {
                var bareName = packagePath.Contains('@') ? packagePath[..packagePath.IndexOf('@')] : packagePath;
                var explicitVersion = packagePath.Contains('@') ? packagePath[(packagePath.IndexOf('@') + 1)..] : null;

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
                // Assembly not found — try qualified type name (e.g., System.Text.Json.JsonSerializer)
                else if (tryQualifiedTypeName && typeName == null &&
                    PlatformResolver.TryParseQualifiedTypeName(bareName, out var qtAsm, out var qtTyp))
                {
                    platformAssembly = qtAsm;
                    typeName = qtTyp;
                    packagePath = null;
                }
            }
        }

        return new ResolvedSource(packagePath, assemblyPath, platformAssembly, frameworkOverride, typeName);
    }
}
