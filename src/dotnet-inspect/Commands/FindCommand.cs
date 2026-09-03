using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public class FindCommand
{
    public const string Name = "find";
    internal const int PackageProfileDefaultLimit = 500;
    internal const int PackageProfileMaximumLimit = 1_000;

    public static async Task<int> ExecuteAsync(
        FindOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.IsPackageProfile
            && options.Count
            && options.Limit is not null)
        {
            CommandError.Write(
                "--count cannot be combined with -t for a package-prefix search.");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            // Discovery mode: -D/--discover lists schema
            if (options.Discover != null)
            {
                if (options.IsPackageProfile)
                {
                    PackageProfileSectionCatalog catalog =
                        PackageProfileSections.CreateCatalog();
                    SectionPipeline<PackageProfileView> pipeline =
                        catalog.Pipeline;
                    return DiscoverOutput.Execute(
                        options.Discover,
                        PackageProfileSections.CreateSchema(),
                        tree: options.Tree,
                        json: options.JsonOutput,
                        tsv: options.Tsv,
                        jsonl: options.Jsonl,
                        sectionCostAnnotations:
                            pipeline.GetCostAnnotations(),
                        sectionCategories:
                            catalog.Sections.SelectionCategoryMap,
                        projection: options);
                }

                var schema = options.Members
                    ? new DocumentSchema()
                        .Add("Members", "column", "Pattern", "Member", "Kind", "Type", "Signature", "Library", "Source")
                    : new DocumentSchema()
                        .Add("Results", "column", "Pattern", "Type", "Namespace", "Kind", "Library", "Source", "Match", "Sim");
                return DiscoverOutput.Execute(options.Discover, schema,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl,
                    projection: options);
            }

            if (options.IsPackageProfile)
            {
                return await ExecutePackageProfileAsync(
                    options,
                    context,
                    cancellationToken);
            }

            var patterns = options.Pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                CommandError.Write("No pattern specified.");
                return 1;
            }

            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            if (options.Members)
            {
                return await ExecuteMemberSearchAsync(options, patterns, logger, context.HttpClient);
            }

            var results = await TypeSearchService.FindTypesAsync(options, patterns, logger, context.HttpClient);
            var title = patterns.Length == 1 ? $"Find: {patterns[0]}" : "Find Results";

            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                if (!WriteCount(results, title, options))
                    return 1;
            }
            else if (options.JsonOutput)
            {
                // --fields/--columns name post-lowering vocabulary (computed table columns), so
                // naming one opts into the lowered display view; plain --json keeps the typed
                // result document (#3494). This combination used to fail closed (#3386) only
                // because the lowered JSON view did not exist yet.
                if (IsColumnProjectionRequested(options))
                {
                    WriteProjectedJson(results, title, options);
                }
                else
                {
                    JsonOutputHelper.Write(
                        results,
                        TypeFindResultJsonContext.Default.ListTypeFindResult,
                        TypeFindResultCompactJsonContext.Default.ListTypeFindResult,
                        options.CompactJson);
                }
            }
            else
            {
                WriteOutput(results, title, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    private static async Task<int> ExecutePackageProfileAsync(
        FindOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        if (options.Packages.Length > 0
            || options.Assemblies.Length > 0
            || options.PlatformAssemblies.Length > 0
            || options.PlatformFrameworks.Length > 0
            || options.Projects.Length > 0
            || options.BinPaths.Length > 0
            || options.Members
            || options.IncludeAll
            || options.Tfm is not null)
        {
            CommandError.Write(
                "Patternless --package-prefix cannot be combined with API search scopes, --all, or --tfm.");
            return 1;
        }

        if (options.SourceOptions is { } sourceOptions
            && (sourceOptions.Sources.Length > 0
                || sourceOptions.AdditionalSources.Length > 0
                || sourceOptions.ConfigFile is not null))
        {
            CommandError.Write(
                "Package-prefix manifest profiles currently use the NuGet Gallery source and cannot be combined with source overrides.");
            return 1;
        }

        if (!PackageProfileQuery.IsValidPrefix(options.PackagePrefix))
        {
            CommandError.Write(
                "--package-prefix must be 1 to 100 characters without surrounding whitespace or control characters.");
            return 1;
        }

        if (options.TypeFilter is not null
            && !int.TryParse(options.TypeFilter, out _))
        {
            CommandError.Write(
                $"-t must be an integer between 1 and {PackageProfileMaximumLimit} for a package-prefix profile.");
            return 1;
        }

        int maximumPackages =
            options.Limit ?? PackageProfileDefaultLimit;
        if (maximumPackages is <= 0
            or > PackageProfileMaximumLimit)
        {
            CommandError.Write(
                $"-t must be between 1 and {PackageProfileMaximumLimit} for a package-prefix profile (got {maximumPackages}).");
            return 1;
        }

        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(
                context.HttpClient.Timeout);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                DotnetInspector.Core.HttpClientFactory
                    .CreateCredentialFreeHandler(),
                fetchOptions);
        using var operationContext = new NuGetOperationContext(
            fetchOptions.RequestTimeout,
            fetchOptions.OperationTimeout,
            cancellationToken);
        var request = new PackagePrefixProfileRequest(
            options.PackagePrefix!,
            maximumPackages);
        PackageProfileSectionCatalog catalog =
            PackageProfileSections.CreateCatalog();
        HashSet<string> includeSections =
            [PackageProfileSections.Packages];
        CompiledInspectionPlan<PackageProfileQueryContext> queryPlan =
            catalog.Lens.Plan(Verbosity.Normal, includeSections);
        InspectionQueryResults queryResults =
            await queryPlan.RunAsync(
                new PackageProfileQueryContext(
                    source,
                    request,
                    operationContext),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        var events = queryResults.Get(PackageProfileQuery.Definition);

        PackageProfileSummary summary = events
            .OfType<PackageProfileEvent.Completed>()
            .Single()
            .Value;
        var view = PackageProfileSections.CreateDocument(
            request.Prefix,
            events,
            options.Rows);
        WritePackageProfileOutput(view, options);

        foreach (PackageProfileEvent.Failure failure
            in events.OfType<PackageProfileEvent.Failure>())
        {
            string subject = failure.Value.PackageId is { Length: > 0 } id
                ? $"{id}: "
                : "";
            CommandError.WriteWarning(
                $"{subject}{failure.Value.Message}");
        }

        if (summary.Truncated)
        {
            CommandError.WriteWarning(
                summary.TruncationReason
                    == PackageSearchTruncationReason.RequestedLimit
                        ? "Package discovery reached the requested package limit."
                        : "Package discovery was truncated by a pagination limit; narrow the prefix.");
        }

        return PackageProfileExitCode(summary);
    }

    internal static void WritePackageProfileOutput(
        PackageProfileView view,
        FindOptions options)
    {
        SectionPipeline<PackageProfileView> pipeline =
            PackageProfileSections.CreateCatalog().Pipeline;
        HashSet<string> includeSections =
            pipeline.GetCandidateSections(
                Verbosity.Normal,
                [PackageProfileSections.Packages]);

        if (options.Count)
        {
            CountOutput.WriteCount(
                PackageProfileSections.CountRows(view));
        }
        else if (options.JsonOutput)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        writerOptions);
                },
                !options.CompactJson,
                maxRows: null);
        }
        else if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                !options.NoHeader,
                options.Tsv,
                options.Jsonl,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        writerOptions);
                },
                maxRows: null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(
                Console.Out,
                rows: null,
                writerOptions =>
                {
                    writerOptions.IncludeSections = includeSections;
                    return MarkoutSerializer.Serialize(
                        view,
                        SearchViewContext.Default,
                        writerOptions);
                },
                options.Columns,
                options.Fields);
        }
    }

    internal static int PackageProfileExitCode(
        PackageProfileSummary summary) =>
        summary.Failures == 0
        && summary.TruncationReason
            is PackageSearchTruncationReason.None
                or PackageSearchTruncationReason.RequestedLimit
            ? 0
            : 1;

    private static async Task<int> ExecuteMemberSearchAsync(
        FindOptions options,
        string[] patterns,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        // Strip the leading '.' sentinel from each segment so ".Serialize" and "Serialize" both search
        // the member named "Serialize". ".ctor"/".cctor" are preserved (they are real member names).
        var memberPatterns = patterns
            .Select(MemberPatternSentinel.Strip)
            .Where(p => p.Length > 0)
            .ToArray();

        if (memberPatterns.Length == 0)
        {
            CommandError.Write("No member pattern specified.");
            return 1;
        }

        var results = await MemberSearchService.FindMembersAsync(options, memberPatterns, logger, httpClient);
        var title = memberPatterns.Length == 1 ? $"Find member: {memberPatterns[0]}" : "Find Members";

        if (options.Count)
        {
            if (!WriteMemberCount(results, title, options))
                return 1;
        }
        else if (options.JsonOutput)
        {
            // See the type-search branch: a projection request lowers --json to the display view.
            if (IsColumnProjectionRequested(options))
            {
                WriteMemberProjectedJson(results, title, options);
            }
            else
            {
                JsonOutputHelper.Write(
                    results,
                    MemberFindResultJsonContext.Default.ListMemberFindResult,
                    MemberFindResultCompactJsonContext.Default.ListMemberFindResult,
                    options.CompactJson);
            }
        }
        else
        {
            WriteMemberOutput(results, title, options);
        }

        return 0;
    }

    private static bool IsColumnProjectionRequested(FindOptions options)
        => options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };

    /// <summary>
    /// Writes the lowered JSON view of a type search: the same section and column projection the
    /// table formats apply, emitted as JSON (#3494).
    /// </summary>
    private static void WriteProjectedJson(List<TypeFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        OutputFormatter.WriteProjectedJson(Console.Out, options.Columns, options.Fields,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
            !options.CompactJson,
            options.Rows);
    }

    /// <summary>
    /// Writes the lowered JSON view of a member search. See <see cref="WriteProjectedJson"/>.
    /// </summary>
    private static void WriteMemberProjectedJson(List<MemberFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        OutputFormatter.WriteProjectedJson(Console.Out, options.Columns, options.Fields,
            (writer, formatter, writerOptions) =>
                MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
            !options.CompactJson,
            options.Rows);
    }

    private static void WriteOutput(List<TypeFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                options.Rows);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, options.Rows,
                opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
        }
    }

    private static bool WriteCount(List<TypeFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);
        return CountOutput.TryWriteProjected(
            view,
            SearchViewContext.Default,
            "Results",
            options.Columns,
            options.Fields,
            options.Rows);
    }

    private static void WriteMemberOutput(List<MemberFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                options.Rows);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, options.Rows,
                opts => MarkoutSerializer.Serialize(view, SearchViewContext.Default, opts));
        }
    }

    private static bool WriteMemberCount(List<MemberFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);
        return CountOutput.TryWriteProjected(
            view,
            SearchViewContext.Default,
            "Members",
            options.Columns,
            options.Fields,
            options.Rows);
    }
}

/// <summary>
/// Represents a type found during search.
/// </summary>
public record class TypeSearchResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("library")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }
}
