using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
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

        // Discovery mode: -D/--discover lists schema
        if (options.Discover != null)
        {
            var schemaMap = new DocumentSchema()
                .Add("Changes", "column", "Change", "Type", "Detail");
            return DiscoverOutput.Execute(options.Discover, schemaMap,
                tree: options.Tree, json: false, markdown: !options.OneLine);
        }

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

            if (options.OneLine)
            {
                var typeDiffs = ApplyFilters(diff, options);
                var view = DiffOutputFormatter.BuildOneLineView(name, typeDiffs, fromVersion, toVersion);
                var writerOpts = new MarkoutWriterOptions
                {
                    Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
                };
                OutputFormatter.ConfigureTableWriterOptions(writerOpts, options.Tsv);
                OutputFormatter.WriteTable(options.Tsv, Console.Out, !options.NoHeader,
                    (writer, formatter) => MarkoutSerializer.Serialize(view, writer, formatter, DiffViewContext.Default, writerOpts));
            }
            else
            {
                var output = RenderDiff(name, diff, fromVersion, toVersion, options);
                Console.WriteLine(output);
            }

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

        logger.Log($"Comparing {packageName} v{fromVersion} -> v{toVersion}");

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

        var (fromSurface, _) = await ApiServices.ExtractMergedApiSurfaceAsync(fromOptions, logger, httpClient);
        var (toSurface, _) = await ApiServices.ExtractMergedApiSurfaceAsync(toOptions, logger, httpClient);

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
            return (null, null, null, null, null, "Error: Invalid version range. Use format: Library@v1..v2");
        }

        var framework = options.Framework ?? "runtime";
        logger.Log($"Comparing {assemblyName} in {framework} v{fromVersion} -> v{toVersion}");

        // Resolve assemblies for both versions (downloads ref packs as needed)
        var (fromPath, _, _, fromError) = await PlatformResolver.ResolveAssemblyAsync(
            assemblyName,
            httpClient,
            logger.Log,
            $"{framework}@{fromVersion}");

        if (fromError != null)
        {
            return (null, null, null, null, null, $"Error resolving v{fromVersion}: {fromError}");
        }

        var (toPath, _, _, toError) = await PlatformResolver.ResolveAssemblyAsync(
            assemblyName,
            httpClient,
            logger.Log,
            $"{framework}@{toVersion}");

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

    internal static IReadOnlyList<TypeDiff> ApplyFilters(ApiDiff diff, DiffOptions options)
    {
        var typeDiffs = diff.TypeDiffs;

        // Apply type filter post-Compare
        if (options.TypeFilter.Count > 0)
        {
            typeDiffs = typeDiffs
                .Where(td => TypeMatcher.MatchesAnyTypeFilter(td.TypeFullName, options.TypeFilter))
                .ToList();
        }

        // Apply classification filter
        return FilterByClassification(typeDiffs, options);
    }

    internal static string RenderDiff(string name, ApiDiff diff, string fromVersion, string toVersion, DiffOptions options)
    {
        var typeDiffs = ApplyFilters(diff, options);

        if (options.NameOnly)
        {
            return OutputFormatter.RenderTable(options.Tsv, showHeader: false, (writer, formatter) =>
            {
                var nameWriter = new Markout.MarkoutWriter(writer, formatter, OutputFormatter.CreateTableWriterOptions(options.Tsv));
                DiffOutputFormatter.RenderNameOnly(nameWriter, typeDiffs);
                nameWriter.Flush();
            });
        }

        var markdown = DiffOutputFormatter.RenderFullMarkdown(name, typeDiffs, fromVersion, toVersion);
        return MarkdownTableRowLimiter.Apply(markdown, options.Rows);
    }

    private static IReadOnlyList<TypeDiff> FilterByClassification(IReadOnlyList<TypeDiff> typeDiffs, DiffOptions options)
    {
        if (!options.Breaking && !options.Additive)
            return typeDiffs;

        HashSet<ChangeClassification> allowed = [];
        if (options.Breaking) allowed.Add(ChangeClassification.Breaking);
        if (options.Additive) allowed.Add(ChangeClassification.Additive);

        List<TypeDiff> filtered = [];
        foreach (var td in typeDiffs)
        {
            var changes = td.Changes.Where(c => allowed.Contains(c.Classification)).ToList();
            if (changes.Count > 0)
                filtered.Add(new TypeDiff(td.TypeFullName, changes));
        }
        return filtered;
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
    public HashSet<string> TypeFilter { get; init; } = [];
    public bool OneLine { get; init; }
    public bool Tsv { get; init; }
    public bool NoHeader { get; init; }
    public bool NameOnly { get; init; }
    public bool Breaking { get; init; }
    public bool Additive { get; init; }
    public bool Legend { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }
    public string[]? Select { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public int? Rows { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public bool IsRawOutput => OneLine || NoHeader || NameOnly;
}
