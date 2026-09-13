using System.Text.Json.Serialization;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

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
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            // Discovery mode: -D/--discover lists schema
            if (options.Discover != null)
            {
                if (options.Literal is not null)
                {
                    return DiscoverOutput.Execute(
                        options.Discover,
                        PackageAssemblyQuerySections.CreateSchema(),
                        tree: options.Tree,
                        json: options.JsonOutput,
                        tsv: options.Tsv,
                        jsonl: options.Jsonl,
                        projection: options,
                        semanticRowSelection: options.RowSelection,
                        semanticSelectionName: "Find");
                }

                if (options.IsPackageProfile)
                {
                    if (options.PackageQuery is not null)
                    {
                        return DiscoverOutput.Execute(
                            options.Discover,
                            PackageQuerySections.CreateSchema(),
                            tree: options.Tree,
                            json: options.JsonOutput,
                            tsv: options.Tsv,
                            jsonl: options.Jsonl,
                            sectionCostAnnotations: PackageQuerySections.Catalog.Pipeline.GetCostAnnotations(),
                            sectionCategories: PackageQuerySections.Catalog.SelectionCategoryMap,
                            projection: options,
                            semanticRowSelection: options.RowSelection,
                            semanticSelectionName: "Find");
                    }
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
                        projection: options,
                        semanticRowSelection: options.RowSelection,
                        semanticSelectionName: "Find");
                }

                var schema = options.Members
                    ? new DocumentSchema()
                        .Add("Members", "column", "Pattern", "Member", "Kind", "Type", "Signature", "Library", "Source")
                    : new DocumentSchema()
                        .Add("Results", "column", "Pattern", "Type", "Namespace", "Kind", "Library", "Source", "Match", "Sim");
                return DiscoverOutput.Execute(options.Discover, schema,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl,
                    projection: options,
                    semanticRowSelection: options.RowSelection,
                    semanticSelectionName: "Find");
            }

            if (options.Literal is not null)
            {
                return await ExecuteAssemblyLiteralQueryAsync(
                    options,
                    context,
                    cancellationToken);
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
                return await ExecuteMemberSearchAsync(
                    options,
                    patterns,
                    logger,
                    context.HttpClient,
                    cancellationToken);
            }

            FindSearchResult<TypeFindResult> search =
                await TypeSearchService.FindTypesAsync(
                    options,
                    patterns,
                    logger,
                    context.HttpClient,
                    cancellationToken);
            List<TypeFindResult> results = search.Rows;
            int observedRowCount = results.Count;
            if (!TrySelectRows(
                    options.RowSelection,
                    results,
                    "type",
                    out IReadOnlyList<TypeFindResult> selectedTypes))
            {
                WriteUnmatchedPatternWarning(search);
                return 1;
            }
            results = [.. selectedTypes];
            WriteUnmatchedPatternWarning(search);
            var title = patterns.Length == 1 ? $"Find: {patterns[0]}" : "Find Results";

            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                if (search.HasFailures
                    || !CliSemanticRowSelection.ProvidesExactCount(
                        options.RowSelection,
                        observedRowCount,
                        sourceComplete:
                            !search.SourceSelectionIncomplete))
                {
                    CommandError.Write(
                        "Cannot count type rows because one or more search sources were incomplete.");
                    return 1;
                }
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

    private static void WriteUnmatchedPatternWarning(
        FindSearchResult<TypeFindResult> search)
    {
        int unmatchedCount =
            search.UnmatchedPatterns?.Count ?? 0;
        if (unmatchedCount == 0 || search.Rows.Count == 0)
            return;

        CommandError.WriteWarning(
            $"{unmatchedCount} search "
            + (unmatchedCount == 1
                ? "pattern matched"
                : "patterns matched")
            + " no types.");
    }

    /// <summary>
    /// Runs the decoded-literal Package Query over an explicit, finite package
    /// selection and renders the shared evaluator's own outcomes.
    /// </summary>
    /// <remarks>
    /// The route is deliberately narrow: it acquires only the exact
    /// ID@VERSION candidates named on the command line, evaluates the
    /// selector-issued primary implementation assembly of each, and never
    /// falls back to the type-search scopes. Candidates are disposable — the
    /// shared pipeline keeps no long-lived package cache for them — so the
    /// only durable result a host may act on is the owner-issued Root
    /// reopening token each evaluated candidate carries.
    /// </remarks>
    private static async Task<int> ExecuteAssemblyLiteralQueryAsync(
        FindOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        if (options.Pattern.Length > 0
            || options.Assemblies.Length > 0
            || options.PlatformAssemblies.Length > 0
            || options.PlatformFrameworks.Length > 0
            || options.Projects.Length > 0
            || options.BinPaths.Length > 0
            || options.PackagePrefixSpecified
            || options.PackagePrefix is not null
            || options.Members
            || options.IncludeAll
            || options.TypeFilter is not null)
        {
            CommandError.Write(
                "--literal searches only explicit ID@VERSION packages; "
                + "it cannot be combined with a type pattern, API search scopes, "
                + "--package-prefix, --members, --all, or --type.");
            return 1;
        }

        if (options.SourceOptions is { } sourceOptions
            && (sourceOptions.Sources.Length > 0
                || sourceOptions.AdditionalSources.Length > 0
                || sourceOptions.ConfigFile is not null))
        {
            CommandError.Write(
                "Literal package queries currently use the NuGet Gallery source and cannot be combined with source overrides.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.Tfm))
        {
            CommandError.Write(PackageAssemblyQueryDiagnostics.MissingTargetFramework);
            return 1;
        }

        PackageAssemblyQueryPlan plan;
        try
        {
            plan = PackageAssemblyQuery.Plan(
                PackageAssemblyPatterns.StringLiteralContains,
                options.Literal!,
                options.Packages,
                options.Tfm);
        }
        catch (ArgumentException ex)
        {
            CommandError.Write(PackageAssemblyQueryDiagnostics.Describe(ex));
            return 1;
        }

        await using var payloadProvider =
            new ConfiguredPackageRootPayloadProvider(
                context.HttpClient.Timeout,
                new NuGetSourceOptions
                {
                    Sources = [PackageSource.NuGetOrg.Url],
                });

        var events = new List<PackageAssemblyQueryEvent>();
        try
        {
            await foreach (PackageAssemblyQueryEvent queryEvent
                in PackageAssemblyQuery.ExecuteAsync(
                        payloadProvider,
                        plan,
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                if (queryEvent is PackageAssemblyQueryEvent.Progress progress)
                {
                    context.Logger.Log(
                        $"Evaluated {progress.CompletedCandidates} of {progress.Limit} package candidates");
                    continue;
                }

                events.Add(queryEvent);
            }
        }
        catch (Exception ex)
            when (PackageAssemblyEvaluationExceptionEvidence.TryGetCleanup(
                ex,
                out PackageAssemblyEvaluationCleanupEvidence? cleanup))
        {
            // Cleanup evidence rides on the propagated exception, so reporting
            // only the primary message would silently drop it.
            CommandError.Write(
                ex.Message,
                [
                    "Candidate cleanup was incomplete: "
                    + PackageAssemblyQuerySections.DescribeCleanup(cleanup),
                ]);
            return 1;
        }

        PackageAssemblyQueryView view =
            PackageAssemblyQuerySections.CreateDocument(plan, events);
        PackageAssemblyLiteralUseRow[] matchRows =
            [.. view.Matches ?? []];
        if (!TrySelectRows(
                options.RowSelection,
                matchRows,
                "literal-use",
                out IReadOnlyList<PackageAssemblyLiteralUseRow>
                    selectedMatchRows))
        {
            view = PackageAssemblyQuerySections.WithSelectedMatches(
                plan,
                view,
                []);
            WriteAssemblyQueryOutput(
                view,
                options with { Count = false });
            WriteAssemblyQueryDiagnostics(events);
            return 1;
        }
        view = PackageAssemblyQuerySections.WithSelectedMatches(
            plan,
            view,
            selectedMatchRows);
        WriteAssemblyQueryOutput(view, options);

        WriteAssemblyQueryDiagnostics(events);

        return view.FailureCount == 0 ? 0 : 1;
    }

    private static void WriteAssemblyQueryDiagnostics(
        IReadOnlyList<PackageAssemblyQueryEvent> events)
    {
        foreach (PackageAssemblyQueryEvent.AcquisitionFailed failed
            in events.OfType<PackageAssemblyQueryEvent.AcquisitionFailed>())
        {
            CommandError.WriteWarning(
                $"{failed.Value.Coordinate.PackageId}@{failed.Value.Coordinate.Version}: "
                + failed.Value.Message);
        }
    }

    internal static void WriteAssemblyQueryOutput(
        PackageAssemblyQueryView view,
        FindOptions options)
    {
        if (options.Count)
        {
            if (view.FailureCount > 0)
            {
                throw new InvalidOperationException(
                    "Cannot count decoded literal uses because one or more package candidates failed; omit --count to inspect candidate outcomes.");
            }

            if (!CountOutput.TryWriteProjected(
                    view,
                    SearchViewContext.Default,
                    PackageAssemblyQuerySections.Matches,
                    options.Columns,
                    options.Fields,
                    rows: null))
            {
                throw new InvalidOperationException(
                    "The literal Package Query count projection was rejected.");
            }
        }
        else if (options.JsonOutput)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        ConfigureAssemblyQueryWriterOptions(options.Verbosity, writerOptions)),
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
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        SearchViewContext.Default,
                        ConfigureAssemblyQueryWriterOptions(options.Verbosity, writerOptions)),
                maxRows: null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(
                Console.Out,
                rows: null,
                writerOptions => MarkoutSerializer.Serialize(
                    view,
                    SearchViewContext.Default,
                    ConfigureAssemblyQueryWriterOptions(options.Verbosity, writerOptions)),
                options.Columns,
                options.Fields);
        }
    }

    static MarkoutWriterOptions ConfigureAssemblyQueryWriterOptions(
        Verbosity verbosity,
        MarkoutWriterOptions writerOptions)
    {
        writerOptions.IncludeSections = verbosity switch
        {
            Verbosity.Quiet => [],
            Verbosity.Minimal => [PackageAssemblyQuerySections.Candidates],
            _ => null,
        };
        return writerOptions;
    }

    private static async Task<int> ExecutePackageProfileAsync(
        FindOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        if (options.HasPackageProfileGroupScope
            || options.Packages.Length > 0
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
                "Package-prefix queries currently use the NuGet Gallery source and cannot be combined with source overrides.");
            return 1;
        }

        if (options.PackageQuery is not null)
            return await PackageQueryCommand.ExecuteAsync(options, context, cancellationToken);

        if (!PackageProfileQuery.IsValidPrefix(options.PackagePrefix))
        {
            CommandError.Write(
                "--package-prefix must be 1 to 100 characters without surrounding whitespace or control characters.");
            return 1;
        }

        int maximumPackages =
            options.Take ?? PackageProfileDefaultLimit;

        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(
                context.HttpClient.Timeout);
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                DotnetInspector.Networking.HttpClientFactory
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
        if (!TrySelectRowsPreservingContext(
                options.RowSelection,
                events,
                static profileEvent =>
                    profileEvent is PackageProfileEvent.Match,
                "package",
                out IReadOnlyList<PackageProfileEvent> displayEvents,
                out int packageRowCount))
        {
            WritePackageProfileDiagnostics(displayEvents, summary);
            return 1;
        }
        if (options.Count
            && (summary.Failures > 0
                || !CliSemanticRowSelection.ProvidesExactCount(
                    options.RowSelection,
                    packageRowCount,
                    sourceComplete: !summary.Truncated)))
        {
            WritePackageProfileDiagnostics(events, summary);
            CommandError.Write(
                "Cannot count package rows because package discovery is incomplete; "
                + "use -n or a closed --rows range that is satisfied by the observed rows.");
            return 1;
        }
        var view = PackageProfileSections.CreateDocument(
            request.Prefix,
            displayEvents);
        WritePackageProfileOutput(view, options);
        WritePackageProfileDiagnostics(events, summary);

        return PackageProfileExitCode(summary);
    }

    private static void WritePackageProfileDiagnostics(
        IReadOnlyList<PackageProfileEvent> events,
        PackageProfileSummary summary)
    {
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
    }

    internal static void WritePackageProfileOutput(
        PackageProfileView view,
        FindOptions options)
        => WritePackageOutput(
            view, options, PackageProfileSections.CreateCatalog().Pipeline,
            PackageProfileSections.CountRows(view));

    internal static void WritePackageOutput<T>(
        T view,
        FindOptions options,
        SectionPipeline<T> pipeline,
        int rowCount)
    {
        HashSet<string> includeSections =
            pipeline.GetCandidateSections(
                Verbosity.Normal,
                [PackageProfileSections.Packages]);

        if (options.Count)
        {
            CountOutput.WriteCount(
                rowCount);
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
        HttpClient httpClient,
        CancellationToken cancellationToken)
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

        FindSearchResult<MemberFindResult> search =
            await MemberSearchService.FindMembersAsync(
                options,
                memberPatterns,
                logger,
                httpClient,
                cancellationToken);
        List<MemberFindResult> results = search.Rows;
        int observedRowCount = results.Count;
        if (!TrySelectRows(
                options.RowSelection,
                results,
                "member",
                out IReadOnlyList<MemberFindResult> selectedMembers))
        {
            return 1;
        }
        results = [.. selectedMembers];
        var title = memberPatterns.Length == 1 ? $"Find member: {memberPatterns[0]}" : "Find Members";

        if (options.Count)
        {
            if (search.HasFailures
                || !CliSemanticRowSelection.ProvidesExactCount(
                    options.RowSelection,
                    observedRowCount,
                    sourceComplete:
                        !search.SourceSelectionIncomplete))
            {
                CommandError.Write(
                    "Cannot count member rows because one or more search sources were incomplete.");
                return 1;
            }
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

    internal static bool TrySelectRows<T>(
        RowSelectionIntent<string>? intent,
        IReadOnlyList<T> rows,
        string rowKind,
        out IReadOnlyList<T> selected)
        => CliSemanticRowSelection.TrySelect(
            intent,
            rows,
            rowKind,
            failure =>
                $"Find row selection stage "
                + $"{failure.Failure.StageNumber} requires "
                + $"{rowKind} row "
                + $"{failure.Failure.RequiredPosition}, but only "
                + $"{failure.Failure.AvailableCount} "
                + $"{rowKind} rows are available.",
            out selected);

    internal static bool TrySelectRowsPreservingContext<T>(
        RowSelectionIntent<string>? intent,
        IReadOnlyList<T> events,
        Func<T, bool> isRow,
        string rowKind,
        out IReadOnlyList<T> selectedEvents,
        out int availableRowCount)
        where T : class
        => CliSemanticRowSelection.TrySelectPreservingContext(
            intent,
            events,
            isRow,
            rowKind,
            failure =>
                $"Find row selection stage "
                + $"{failure.Failure.StageNumber} requires "
                + $"{rowKind} row "
                + $"{failure.Failure.RequiredPosition}, but only "
                + $"{failure.Failure.AvailableCount} "
                + $"{rowKind} rows are available.",
            out selectedEvents,
            out availableRowCount);

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
            maxRows: null);
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
            maxRows: null);
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
                maxRows: null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, rows: null,
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
            rows: null);
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
                maxRows: null);
        }
        else
        {
            OutputFormatter.WriteWindowedMarkdown(Console.Out, rows: null,
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
            rows: null);
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
