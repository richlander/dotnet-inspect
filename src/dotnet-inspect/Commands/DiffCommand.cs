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
    public const string Name = "diff";
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

            var diff = ApiDiffAnalyzer.Compare(fromSurface, toSurface);
            var output = RenderDiff(name, diff, fromVersion, toVersion, options);
            Console.WriteLine(output);

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
        return AssemblyReader.ExtractApiSurface(assemblyPath, includeAll);
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

    internal static string RenderDiff(string name, ApiDiff diff, string fromVersion, string toVersion, DiffOptions options)
    {
        var typeDiffs = diff.TypeDiffs;

        // Apply type filter post-Compare
        if (options.TypeFilter?.Count > 0)
        {
            typeDiffs = typeDiffs
                .Where(td => TypeMatcher.MatchesAnyTypeFilter(td.TypeFullName, options.TypeFilter))
                .ToList();
        }

        // Apply classification filter
        typeDiffs = FilterByClassification(typeDiffs, options);

        if (options.NameOnly)
            return RenderNameOnly(typeDiffs);

        if (options.Stat)
            return RenderStat(name, typeDiffs, fromVersion, toVersion);

        return RenderFullMarkdown(name, typeDiffs, fromVersion, toVersion);
    }

    private static IReadOnlyList<TypeDiff> FilterByClassification(IReadOnlyList<TypeDiff> typeDiffs, DiffOptions options)
    {
        if (!options.Breaking && !options.Additive)
            return typeDiffs;

        var allowed = new HashSet<ChangeClassification>();
        if (options.Breaking) allowed.Add(ChangeClassification.Breaking);
        if (options.Additive) allowed.Add(ChangeClassification.Additive);

        var filtered = new List<TypeDiff>();
        foreach (var td in typeDiffs)
        {
            var changes = td.Changes.Where(c => allowed.Contains(c.Classification)).ToList();
            if (changes.Count > 0)
                filtered.Add(new TypeDiff(td.TypeFullName, changes));
        }
        return filtered;
    }

    private static string RenderNameOnly(IReadOnlyList<TypeDiff> typeDiffs)
    {
        var names = typeDiffs.Select(td => td.TypeFullName).OrderBy(n => n);
        return string.Join(Environment.NewLine, names);
    }

    private static string RenderStat(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(name);
        sb.Append(' ');
        sb.Append(fromVersion);
        sb.Append("..");
        sb.Append(toVersion);
        sb.Append("  ");
        sb.AppendLine(FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking));

        foreach (var td in typeDiffs.OrderBy(td => td.TypeFullName))
        {
            char symbol;
            string detail;

            if (td.IsAdded)
            {
                symbol = '+';
                detail = "(added)";
            }
            else if (td.IsRemoved)
            {
                symbol = '-';
                detail = "(removed)";
            }
            else if (td.BreakingCount > 0)
            {
                symbol = '\u2717'; // ✗
                detail = FormatSummaryCounts(td.BreakingCount, td.AdditiveCount, td.PotentiallyBreakingCount);
            }
            else
            {
                symbol = '~';
                detail = FormatSummaryCounts(td.BreakingCount, td.AdditiveCount, td.PotentiallyBreakingCount);
            }

            sb.AppendLine($" {symbol} {TypeMatcher.GetSimpleName(td.TypeFullName),-40} {detail}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatSummaryCounts(int breaking, int additive, int potentiallyBreaking)
    {
        var parts = new List<string>(3);
        if (breaking > 0) parts.Add($"{breaking} breaking");
        if (additive > 0) parts.Add($"{additive} additive");
        if (potentiallyBreaking > 0) parts.Add($"{potentiallyBreaking} potentially breaking");
        return parts.Count > 0 ? string.Join(", ", parts) : "no changes";
    }

    private static string RenderFullMarkdown(string name, IReadOnlyList<TypeDiff> typeDiffs, string fromVersion, string toVersion)
    {
        var writer = new MarkoutWriter();

        writer.WriteHeading(1, $"API Diff: {name}");
        writer.WriteParagraph($"**{fromVersion}** → **{toVersion}**");

        if (typeDiffs.Count == 0)
        {
            writer.WriteParagraph("*No API changes detected.*");
            return writer.ToString().TrimEnd();
        }

        int totalBreaking = 0, totalAdditive = 0, totalPotentiallyBreaking = 0;
        foreach (var td in typeDiffs)
        {
            totalBreaking += td.BreakingCount;
            totalAdditive += td.AdditiveCount;
            totalPotentiallyBreaking += td.PotentiallyBreakingCount;
        }

        var summary = FormatSummaryCounts(totalBreaking, totalAdditive, totalPotentiallyBreaking);
        writer.WriteParagraph($"**Summary:** {summary} across {typeDiffs.Count} types");

        // Group by classification: breaking first, then potentially breaking, then additive
        WriteSection(writer, "Breaking Changes", ChangeClassification.Breaking, typeDiffs);
        WriteSection(writer, "Potentially Breaking Changes", ChangeClassification.PotentiallyBreaking, typeDiffs);
        WriteSection(writer, "Additive Changes", ChangeClassification.Additive, typeDiffs);

        return writer.ToString().TrimEnd();
    }

    private static void WriteSection(MarkoutWriter writer, string heading, ChangeClassification classification, IReadOnlyList<TypeDiff> typeDiffs)
    {
        // Collect types that have changes of this classification
        var relevantTypes = typeDiffs
            .Where(td => td.Changes.Any(c => c.Classification == classification))
            .OrderBy(td => td.TypeFullName)
            .ToList();

        if (relevantTypes.Count == 0)
            return;

        writer.WriteHeading(2, heading);

        foreach (var td in relevantTypes)
        {
            writer.WriteHeading(3, TypeMatcher.GetSimpleName(td.TypeFullName));

            var changes = td.Changes
                .Where(c => c.Classification == classification)
                .ToList();

            foreach (var change in changes)
            {
                var message = change.Message;

                // For signature changes, append old → new values
                if (change.Kind == ChangeKind.MemberSignatureChanged &&
                    change.OldValue != null && change.NewValue != null)
                {
                    message += $": `{change.OldValue}` → `{change.NewValue}`";
                }

                writer.WriteListItem(message);
            }
        }
    }
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
    public bool Breaking { get; init; }
    public bool Additive { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
}
