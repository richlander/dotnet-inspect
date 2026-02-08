using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Compares API surfaces between two package or platform versions.
/// </summary>
public class DiffCommand
{
    public static async Task<int> ExecuteAsync(DiffOptions options)
    {
        var hasPlatform = !string.IsNullOrEmpty(options.PlatformVersionRange);
        var hasPackage = !string.IsNullOrEmpty(options.PackageVersionRange);

        if (!hasPlatform && !hasPackage)
        {
            Console.Error.WriteLine("Error: --package or --platform with version range required.");
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  --package System.Text.Json@9.0.0..10.0.2");
            Console.Error.WriteLine("  --platform System.Text.Json@8.0.23..10.0.2");
            return 1;
        }

        if (hasPlatform && hasPackage)
        {
            Console.Error.WriteLine("Error: Cannot specify both --package and --platform.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            ApiSurface fromSurface;
            ApiSurface toSurface;
            string fromVersion;
            string toVersion;
            string name;

            if (hasPackage)
            {
                var result = await ExecutePackageDiffAsync(options, logger, context.HttpClient);
                if (result.error != null)
                {
                    Console.Error.WriteLine(result.error);
                    return 1;
                }
                fromSurface = result.fromSurface!;
                toSurface = result.toSurface!;
                fromVersion = result.fromVersion!;
                toVersion = result.toVersion!;
                name = result.name!;
            }
            else
            {
                var result = await ExecutePlatformDiffAsync(options, logger, context.HttpClient);
                if (result.error != null)
                {
                    Console.Error.WriteLine(result.error);
                    return 1;
                }
                fromSurface = result.fromSurface!;
                toSurface = result.toSurface!;
                fromVersion = result.fromVersion!;
                toVersion = result.toVersion!;
                name = result.name!;
            }

            // Generate diff output
            var diff = GeneratePackageDiff(name, fromSurface, toSurface, fromVersion, toVersion, options);
            Console.WriteLine(diff);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<(ApiSurface? fromSurface, ApiSurface? toSurface, string? fromVersion, string? toVersion, string? name, string? error)>
        ExecutePackageDiffAsync(DiffOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var (packageName, fromVersion, toVersion) = ParseVersionRange(options.PackageVersionRange!);
        if (packageName == null || fromVersion == null || toVersion == null)
        {
            return (null, null, null, null, null, "Error: Invalid version range. Use format: Package@v1..v2");
        }

        logger.Log($"Comparing {packageName} v{fromVersion} → v{toVersion}");

        var fromOptions = new ApiOptions
        {
            PackagePath = $"{packageName}@{fromVersion}",
            Tfm = options.Tfm,
            IncludeAll = options.IncludeAll,
            Verbose = options.Verbose
        };

        var toOptions = new ApiOptions
        {
            PackagePath = $"{packageName}@{toVersion}",
            Tfm = options.Tfm,
            IncludeAll = options.IncludeAll,
            Verbose = options.Verbose
        };

        var (fromSurface, _) = await ApiServices.ExtractApiSurfaceAsync(fromOptions, logger, httpClient);
        var (toSurface, _) = await ApiServices.ExtractApiSurfaceAsync(toOptions, logger, httpClient);

        if (fromSurface == null || toSurface == null)
        {
            return (null, null, null, null, null, "Error: Failed to extract API surface from one or both versions.");
        }

        return (fromSurface, toSurface, fromVersion, toVersion, packageName, null);
    }

    private static async Task<(ApiSurface? fromSurface, ApiSurface? toSurface, string? fromVersion, string? toVersion, string? name, string? error)>
        ExecutePlatformDiffAsync(DiffOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var (assemblyName, fromVersion, toVersion) = ParseVersionRange(options.PlatformVersionRange!);
        if (assemblyName == null || fromVersion == null || toVersion == null)
        {
            return (null, null, null, null, null, "Error: Invalid version range. Use format: Assembly@v1..v2");
        }

        var framework = options.Framework ?? "runtime";
        logger.Log($"Comparing {assemblyName} in {framework} v{fromVersion} → v{toVersion}");

        // Resolve assemblies for both versions
        var (fromPath, _, _, fromError) = PlatformResolver.ResolveAssembly(
            assemblyName,
            $"{framework}@{fromVersion}",
            packsDirectory: null,
            useRuntimeAssemblies: false);

        if (fromError != null)
        {
            return (null, null, null, null, null, $"Error resolving v{fromVersion}: {fromError}");
        }

        var (toPath, _, _, toError) = PlatformResolver.ResolveAssembly(
            assemblyName,
            $"{framework}@{toVersion}",
            packsDirectory: null,
            useRuntimeAssemblies: false);

        if (toError != null)
        {
            return (null, null, null, null, null, $"Error resolving v{toVersion}: {toError}");
        }

        // Extract API surfaces from both assemblies
        var fromSurface = ExtractApiSurface(fromPath!, options.IncludeAll);
        var toSurface = ExtractApiSurface(toPath!, options.IncludeAll);

        if (fromSurface == null || toSurface == null)
        {
            return (null, null, null, null, null, "Error: Failed to extract API surface from one or both versions.");
        }

        return (fromSurface, toSurface, fromVersion, toVersion, assemblyName, null);
    }

    private static ApiSurface? ExtractApiSurface(string assemblyPath, bool includeAll)
    {
        try
        {
            using FileStream stream = File.OpenRead(assemblyPath);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
                return null;

            return ApiSurfaceExtractor.Extract(peReader, includeAll);
        }
        catch
        {
            return null;
        }
    }

    private static (string? package, string? fromVersion, string? toVersion) ParseVersionRange(string input)
    {
        // Format: Package@v1..v2
        int atIndex = input.IndexOf('@');
        if (atIndex <= 0)
            return (null, null, null);

        string packageName = input[..atIndex];
        string versionPart = input[(atIndex + 1)..];

        int dotDotIndex = versionPart.IndexOf("..", StringComparison.Ordinal);
        if (dotDotIndex <= 0)
            return (null, null, null);

        string fromVersion = versionPart[..dotDotIndex];
        string toVersion = versionPart[(dotDotIndex + 2)..];

        if (string.IsNullOrEmpty(fromVersion) || string.IsNullOrEmpty(toVersion))
            return (null, null, null);

        return (packageName, fromVersion, toVersion);
    }

    private static string GeneratePackageDiff(string name, ApiSurface fromSurface, ApiSurface toSurface, 
        string fromVersion, string toVersion, DiffOptions options)
    {
        // Build type dictionaries by full name
        var fromTypes = fromSurface.Types.ToDictionary(GetTypeFullName, t => t);
        var toTypes = toSurface.Types.ToDictionary(GetTypeFullName, t => t);

        // Determine which types to compare
        var allTypeNames = fromTypes.Keys.Union(toTypes.Keys).ToHashSet();
        
        // Apply type filter if specified
        if (options.TypeFilter?.Count > 0)
        {
            allTypeNames = allTypeNames.Where(fullName =>
            {
                var simpleName = fullName.Contains('.') ? fullName.Split('.').Last() : fullName;
                // Convert generic types for matching (e.g., Option`1 -> Option<T>)
                var convertedSimple = GenericTypeNameConverter.Convert(simpleName);
                var convertedFull = GenericTypeNameConverter.Convert(fullName);

                return options.TypeFilter.Any(f =>
                {
                    var convertedFilter = GenericTypeNameConverter.Convert(f);
                    bool isGlob = f.Contains('*') || f.Contains('?');

                    if (isGlob)
                    {
                        return FindCommand.MatchesGlobPattern(simpleName, f) ||
                               FindCommand.MatchesGlobPattern(fullName, f) ||
                               FindCommand.MatchesGlobPattern(convertedSimple, convertedFilter) ||
                               FindCommand.MatchesGlobPattern(convertedFull, convertedFilter);
                    }

                    return simpleName.Equals(f, StringComparison.OrdinalIgnoreCase) ||
                           fullName.Equals(f, StringComparison.OrdinalIgnoreCase) ||
                           convertedSimple.Equals(convertedFilter, StringComparison.OrdinalIgnoreCase) ||
                           convertedFull.Equals(convertedFilter, StringComparison.OrdinalIgnoreCase);
                });
            }).ToHashSet();
        }

        // Categorize types
        var removedTypes = allTypeNames.Where(n => fromTypes.ContainsKey(n) && !toTypes.ContainsKey(n)).OrderBy(n => n).ToList();
        var addedTypes = allTypeNames.Where(n => !fromTypes.ContainsKey(n) && toTypes.ContainsKey(n)).OrderBy(n => n).ToList();
        var commonTypes = allTypeNames.Where(n => fromTypes.ContainsKey(n) && toTypes.ContainsKey(n)).OrderBy(n => n).ToList();

        // Find changed types (types with member differences)
        var changedTypes = new List<(string name, int added, int removed, List<string> addedMembers, List<string> removedMembers)>();
        foreach (var typeName in commonTypes)
        {
            var (added, removed, addedMembers, removedMembers) = CompareTypeMembers(fromTypes[typeName], toTypes[typeName]);
            if (added > 0 || removed > 0)
            {
                changedTypes.Add((typeName, added, removed, addedMembers, removedMembers));
            }
        }

        // --name-only: just list changed type names
        if (options.NameOnly)
        {
            var allChangedNames = removedTypes
                .Concat(addedTypes)
                .Concat(changedTypes.Select(c => c.name))
                .Distinct()
                .OrderBy(n => n);
            return string.Join(Environment.NewLine, allChangedNames);
        }

        // --stat: compact statistics
        if (options.Stat)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{name} {fromVersion}..{toVersion}  +{addedTypes.Count} -{removedTypes.Count} ~{changedTypes.Count} types");
            foreach (var t in removedTypes)
            {
                sb.AppendLine($" - {GetSimpleName(t)}");
            }
            foreach (var t in addedTypes)
            {
                sb.AppendLine($" + {GetSimpleName(t)}");
            }
            foreach (var (typeName, added, removed, _, _) in changedTypes)
            {
                sb.AppendLine($" ~ {GetSimpleName(typeName),-40} +{added} -{removed}");
            }
            return sb.ToString().TrimEnd();
        }

        // Full output (default)
        var writer = new MarkoutWriter();

        // Header
        writer.WriteHeading(1, $"API Diff: {name}");
        writer.WriteParagraph($"**{fromVersion}** → **{toVersion}**");

        // Summary
        writer.WriteParagraph($"**Summary:** +{addedTypes.Count} types added, -{removedTypes.Count} types removed, {changedTypes.Count} types changed");

        if (removedTypes.Count == 0 && addedTypes.Count == 0 && changedTypes.Count == 0)
        {
            writer.WriteParagraph("*No API changes detected.*");
            return writer.ToString().TrimEnd();
        }

        // Removed Types
        if (removedTypes.Count > 0)
        {
            writer.WriteHeading(2, "Removed Types");
            foreach (var t in removedTypes)
            {
                writer.WriteListItem(t);
            }
        }

        // Added Types
        if (addedTypes.Count > 0)
        {
            writer.WriteHeading(2, "Added Types");
            foreach (var t in addedTypes)
            {
                writer.WriteListItem(t);
            }
        }

        // Changed Types
        if (changedTypes.Count > 0)
        {
            writer.WriteHeading(2, "Changed Types");
            foreach (var (typeName, added, removed, addedMembers, removedMembers) in changedTypes)
            {
                writer.WriteHeading(3, typeName);
                writer.WriteParagraph($"+{added} added, -{removed} removed");

                foreach (var sig in removedMembers)
                {
                    writer.WriteListItem($"`{sig}`");
                }

                foreach (var sig in addedMembers)
                {
                    writer.WriteListItem($"`{sig}`");
                }
            }
        }

        return writer.ToString().TrimEnd();
    }

    internal static string GetSimpleName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    internal static string GetTypeFullName(ApiType type)
    {
        return string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
    }

    internal static (int added, int removed, List<string> addedMembers, List<string> removedMembers) CompareTypeMembers(ApiType fromType, ApiType toType)
    {
        var fromMembers = fromType.Members ?? [];
        var toMembers = toType.Members ?? [];

        // Create signature-based lookup for comparison, filtering out compiler-generated members
        var fromSignatures = fromMembers
            .Where(m => !string.IsNullOrEmpty(m.Signature) && !IsCompilerGenerated(m.Name))
            .Select(m => m.Signature!)
            .ToHashSet();
        var toSignatures = toMembers
            .Where(m => !string.IsNullOrEmpty(m.Signature) && !IsCompilerGenerated(m.Name))
            .Select(m => m.Signature!)
            .ToHashSet();

        var addedMembers = toSignatures.Except(fromSignatures).OrderBy(s => s).ToList();
        var removedMembers = fromSignatures.Except(toSignatures).OrderBy(s => s).ToList();

        return (addedMembers.Count, removedMembers.Count, addedMembers, removedMembers);
    }

    private static bool IsCompilerGenerated(string name) => MemberFilters.IsCompilerGenerated(name);
}

/// <summary>
/// Options for the diff command.
/// </summary>
public record DiffOptions
{
    public string? PackageVersionRange { get; init; }
    public string? PlatformVersionRange { get; init; }
    public string? Framework { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public bool Verbose { get; init; }
    public HashSet<string>? TypeFilter { get; init; }
    public bool Stat { get; init; }
    public bool NameOnly { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
}
