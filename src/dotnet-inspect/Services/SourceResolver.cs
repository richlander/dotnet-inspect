using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.CommandLine;
using DotnetInspector.Metadata;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Services;

/// <summary>
/// Resolves source specifications (package, assembly, platform) from command arguments.
/// Handles file path classification, platform resolution, and qualified type name parsing.
/// </summary>
public static class SourceResolver
{
    private const string CoreLibAssemblyName = "System.Private.CoreLib";

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
    /// Resolves a dotted source-qualified type name using the shared precedence:
    /// exact local type match first, then a typo-friendly platform prefix match.
    /// </summary>
    internal static LocalProbeResult? TryResolveQualifiedTypeName(string name, bool allowPlatformPrefixFallback)
    {
        var probe = TryProbeLocalQualifiedName(name);
        if (probe != null || !allowPlatformPrefixFallback)
            return probe;

        return TryProbePlatformQualifiedPrefix(name);
    }

    /// <summary>
    /// Peels a dotted name and returns the longest platform assembly prefix even when the
    /// remaining type name is misspelled. This is intentionally platform-only to avoid
    /// misclassifying one-argument package names as package/type pairs.
    /// </summary>
    internal static LocalProbeResult? TryProbePlatformQualifiedPrefix(string name)
    {
        var bestDot = -1;
        for (int i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] != '.')
                continue;

            var candidate = name[..i];
            var remainder = name[(i + 1)..];
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(remainder))
                continue;
            if (!candidate.Contains('.'))
                continue;

            if (!PlatformResolver.IsPlatformCandidate(candidate))
                continue;

            var (path, _, _, _) = PlatformResolver.ResolveAssembly(candidate);
            if (path != null)
            {
                bestDot = i;
                break;
            }
        }

        return bestDot > 0
            ? new LocalProbeResult(name[..bestDot], name[(bestDot + 1)..], LocalSourceKind.Platform)
            : null;
    }

    /// <summary>
    /// Lightweight type existence check using raw metadata.
    /// </summary>
    internal static bool AssemblyHasType(string assemblyPath, string typeName)
        => PlatformResolver.HasType(assemblyPath, typeName);

    /// <summary>
    /// Resolves a one-token alias or simple type name from System.Private.CoreLib.
    /// This intentionally probes one local runtime assembly only; misses keep the
    /// existing package/source fallback behavior.
    /// </summary>
    internal static LocalProbeResult? TryResolveBareCoreLibTypeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var typeName = NormalizeCoreLibTypeQuery(name);
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly(CoreLibAssemblyName);
        if (assemblyPath == null || error != null)
            return null;

        var match = FindUniquePublicType(assemblyPath, typeName);
        return match != null
            ? new LocalProbeResult(CoreLibAssemblyName, match, LocalSourceKind.Platform)
            : null;
    }

    private static string NormalizeCoreLibTypeQuery(string name)
    {
        var trimmed = name.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "char" => "System.Char",
            "decimal" => "System.Decimal",
            "double" => "System.Double",
            "float" => "System.Single",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "nint" => "System.IntPtr",
            "nuint" => "System.UIntPtr",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "object" => "System.Object",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "string" => "System.String",
            "void" => "System.Void",
            _ => trimmed
        };
    }

    private static string? FindUniquePublicType(string assemblyPath, string typeName)
    {
        var normalized = DotnetInspector.Metadata.TypeMatcher.Normalize(typeName);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            return null;

        var reader = peReader.GetMetadataReader();
        List<string> publicTypes = [];
        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (!typeDef.IsPublic)
                continue;

            var name = reader.GetString(typeDef.Name);
            var ns = reader.GetString(typeDef.Namespace);
            var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            publicTypes.Add(fullName);
        }

        var exactMatches = publicTypes.Where(fullName =>
            fullName.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || DotnetInspector.Metadata.TypeMatcher.GetSimpleName(fullName).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exactMatches.Count == 1)
            return exactMatches[0];
        if (exactMatches.Count > 1)
            return null;

        string? match = null;
        foreach (var fullName in publicTypes)
        {
            if (!DotnetInspector.Metadata.TypeMatcher.Matches(fullName, normalized))
                continue;

            if (match != null)
                return null;

            match = fullName;
        }

        return match;
    }

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
            if (packagePath != null) packagePath = DotnetInspector.Metadata.TypeMatcher.Normalize(packagePath);
            if (typeName != null) typeName = DotnetInspector.Metadata.TypeMatcher.Normalize(typeName);

            // Check for version number passed as separate argument
            if (CommandLineHelpers.LooksLikeVersionNumber(typeName))
            {
                return new ResolvedSource(
                    packagePath, assemblyPath, platformAssembly, null, null,
                    VersionError: true,
                    VersionErrorMessage: $"Error: '{typeName}' looks like a version number. Use '{packagePath}@{typeName}' to specify a version.");
            }

            if (args.Length == 1 && packagePath != null && typeName == null)
            {
                var coreLibType = TryResolveBareCoreLibTypeName(packagePath);
                if (coreLibType != null)
                {
                    typeName = coreLibType.Remainder;
                    platformAssembly = coreLibType.SourceName;
                    packagePath = null;
                }
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
                    // Convert backtick notation back to C#-style for display
                    var displayName = DotnetInspector.Output.ApiOutputFormatter.FormatGenericTypeName(packagePath, null);
                    return new ResolvedSource(
                        packagePath, assemblyPath, platformAssembly, null, null,
                        VersionError: true,
                        VersionErrorMessage: $"Error: '{displayName}' looks like a type name but was not found in platform libraries. Specify the source: 'source <package> \"{displayName}\"'.");
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
                    var probe = TryResolveQualifiedTypeName(bareName, allowPlatformPrefixFallback: true);
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
