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
using DotnetInspector.SourceSelection;
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

    public static async Task<int> ExecuteAsync(
        FindOptions options,
        CancellationToken cancellationToken = default)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            if (options.Literal is null
                && options.CandidateTake is not null)
            {
                CommandError.Write(
                    "--take is available only with find --literal --package-prefix.");
                return 1;
            }

            // Discovery mode: -D/--discover lists schema
            if (options.Discover != null)
            {
                if (options.Literal is not null)
                {
                    return DiscoverOutput.Execute(
                        options.Discover,
                        PackageAssemblyQuerySections.CreateSchema(),
                        DiscoveryOutputRequest.Create(
                            options.JsonOutput ? OutputFormat.Json
                                : options.Jsonl ? OutputFormat.Jsonl
                                : options.Tsv ? OutputFormat.Tsv
                                : options.Tabular ? OutputFormat.Table
                                : OutputFormat.Markdown,
                            options.Tree,
                            options.Tabular,
                            options.NoHeader,
                            (int)options.Verbosity,
                            options),
                        semanticRowSelection: options.RowSelection,
                        semanticSelectionName: "Find");
                }

                var schema = options.Members
                    ? new DocumentSchema()
                        .Add("Members", "column", "Pattern", "Member", "Kind", "Type", "Signature", "Library", "Source")
                    : new DocumentSchema()
                        .Add("Results", "column", "Pattern", "Type", "Namespace", "Kind", "Library", "Source", "Match", "Sim");
                return DiscoverOutput.Execute(options.Discover, schema,
                    DiscoveryOutputRequest.Create(
                        options.JsonOutput ? OutputFormat.Json
                            : options.Jsonl ? OutputFormat.Jsonl
                            : options.Tsv ? OutputFormat.Tsv
                            : options.Tabular ? OutputFormat.Table
                            : OutputFormat.Markdown,
                        options.Tree,
                        options.Tabular,
                        options.NoHeader,
                        (int)options.Verbosity,
                        options),
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
    /// Runs assembly-semantic decoded-literal Find over one finite exact or
    /// bounded-prefix package population.
    /// </summary>
    /// <remarks>
    /// The route is deliberately narrow: it evaluates the selector-issued
    /// primary implementation assembly of each admitted candidate and never
    /// falls back to type-search scopes. The returned shared Document is the
    /// sole terminal authority; sink observations are progress only.
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
            || options.Members
            || options.IncludeAll
            || options.TypeFilter is not null)
        {
            CommandError.Write(
                "--literal searches only explicit ID@VERSION packages or one "
                + "bounded package prefix; it cannot be combined with a type "
                + "pattern, other API search scopes, --members, --all, or --type.");
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

        PackageAssemblySemanticFindCliPlan plan;
        try
        {
            plan = PackageAssemblySemanticFindCliPlan.Create(
                options.Literal!,
                options.Packages,
                options.PackagePrefix,
                options.PackagePrefixSpecified,
                options.CandidateTake,
                options.Tfm);
        }
        catch (ArgumentException ex)
        {
            CommandError.Write(PackageAssemblyQueryDiagnostics.Describe(ex));
            return 1;
        }

        PackageAssemblySemanticFindBudget budget =
            PackageAssemblySemanticFindBudget.Default;
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(
                context.HttpClient.Timeout);
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        ConfiguredPackageAuthority galleryAuthority =
            authorization.Authorities[0];
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                galleryAuthority.Association,
                DotnetInspector.Networking.HttpClientFactory
                    .CreateCredentialFreeHandler(),
                fetchOptions);
        await using PackageSourceSettlementLease settlement =
            PackageSourceSettlementService.IssueLease(
                authority =>
                    ReferenceEquals(authority, galleryAuthority)
                        ? source
                        : throw new InvalidOperationException(
                            "Assembly-semantic Find requested an unauthorized package source."));
        using var stores = new AssemblySemanticFindStores();
        PackageSourceOperationLease? operation =
            settlement.IssueOperationLease(
                cancellationToken,
                fetchOptions.RequestTimeout,
                budget.MaximumDuration);

        PackageAssemblySemanticFindDocument document;
        PackageAssemblySemanticFindRequest request;
        try
        {
            PackageAcquisitionPopulation population =
                plan.Population switch
                {
                    PackageAssemblySemanticFindPopulationPlan.Exact exact =>
                        await operation.ResolvePinnedPopulationAsync(
                            new FixedPackageSourceAuthorization(
                                authorization),
                            exact.Coordinates).ConfigureAwait(false),
                    PackageAssemblySemanticFindPopulationPlan.Prefix prefix =>
                        await PackageAcquisitionPopulationResolver
                            .ResolveGalleryPrefixAsync(
                                operation,
                                prefix.Declaration,
                                prefix.MaximumCandidates,
                                authorization).ConfigureAwait(false),
                    _ => throw new InvalidOperationException(
                        "Unknown assembly-semantic Find population plan."),
                };
            request = new(
                population,
                plan.Target,
                plan.Pattern,
                budget);
            PackageSourceOperationLease transferredOperation =
                operation;
            operation = null;
            InspectionEnvelope<
                PackageAssemblySemanticFindDocument> envelope =
                await PackageAssemblySemanticFindInspection.ExecuteAsync(
                    request,
                    transferredOperation,
                    new PackagePayloadAcquisitionPlan(
                        stores.GetStore,
                        log: context.Logger.Log),
                    new AssemblySemanticFindProgressSink(
                        context.Logger,
                        population.Candidates.Length),
                    cancellationToken).ConfigureAwait(false);
            document = envelope.Content;
        }
        finally
        {
            operation?.Dispose();
        }

        if (!TrySelectRows(
                options.RowSelection,
                document.Results,
                "literal-use",
                out IReadOnlyList<PackageAssemblySemanticFindResult>
                    selectedResults))
        {
            PackageAssemblyQueryView failedSelectionView =
                PackageAssemblyQuerySections.CreateDocument(
                    request,
                    document,
                    []);
            WriteAssemblyQueryOutput(
                failedSelectionView,
                options with { Count = false });
            WriteAssemblyQueryDiagnostics(document);
            return 1;
        }

        bool complete =
            document.Completion.IsRequestedPopulationComplete
            && document.Completion.IsSemanticEvaluationComplete;
        if (options.Count
            && (!complete
                || !CliSemanticRowSelection.ProvidesExactCount(
                    options.RowSelection,
                    document.Results.Length,
                    sourceComplete: complete)))
        {
            WriteAssemblyQueryDiagnostics(document);
            CommandError.Write(
                "Cannot count decoded literal-use rows because package "
                + "population formation or candidate evaluation is incomplete; "
                + "omit --count to inspect candidate and population outcomes.");
            return 1;
        }

        PackageAssemblyQueryView view =
            PackageAssemblyQuerySections.CreateDocument(
                request,
                document,
                selectedResults);
        WriteAssemblyQueryOutput(view, options);
        WriteAssemblyQueryDiagnostics(document);

        return complete ? 0 : 1;
    }

    private static void WriteAssemblyQueryDiagnostics(
        PackageAssemblySemanticFindDocument document)
    {
        foreach (PackageAcquisitionPopulationFailure failure
            in document.Population.Failures)
        {
            string subject = failure.Coordinate is { } coordinate
                ? $"{coordinate.PackageId}@{coordinate.Version}"
                : failure.PackageId ?? "Package population";
            CommandError.WriteWarning(
                $"{subject}: {failure.Failure.Authority} "
                + $"({failure.Failure.Kind}): "
                + failure.Failure.Message);
        }

        foreach (PackageAssemblySemanticFindCandidateOutcome.Failure failure
            in document.CandidateOutcomes
                .OfType<
                    PackageAssemblySemanticFindCandidateOutcome.Failure>())
        {
            if (failure.Reason
                is not PackageAssemblySemanticFindFailureReason.Acquisition
                    acquisition)
            {
                continue;
            }

            string coordinate =
                $"{failure.Coordinate.PackageId}@"
                + failure.Coordinate.Version;
            foreach (PackageAuthorityFailure sourceFailure
                in acquisition.Evidence.Failures)
            {
                CommandError.WriteWarning(
                    $"{coordinate}: {sourceFailure.Authority} "
                    + $"({sourceFailure.Kind}): "
                    + sourceFailure.Message);
            }
            foreach (InertText.InertString authority
                in acquisition.Evidence.NotFoundAuthorities)
            {
                CommandError.WriteWarning(
                    $"{coordinate}: {authority}: "
                    + "package payload was not found.");
            }
        }

        if (!document.Completion.IsRequestedPopulationComplete)
        {
            CommandError.WriteWarning(
                $"Package population completion: "
                + $"{document.Population.Completion}; "
                + $"{document.Population.Candidates.Length}/"
                + $"{document.Population.RequestedCandidates} candidates. "
                + "These results do not cover the requested bounded population.");
        }
    }

    internal static void WriteAssemblyQueryOutput(
        PackageAssemblyQueryView view,
        FindOptions options)
    {
        if (options.Count)
        {
            if (!view.IsComplete)
            {
                throw new InvalidOperationException(
                    "Cannot count decoded literal uses because package population "
                    + "formation or candidate evaluation is incomplete; omit "
                    + "--count to inspect candidate and population outcomes.");
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
                    "The assembly-semantic Find count projection was rejected.");
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
            Verbosity.Minimal =>
            [
                PackageAssemblyQuerySections.Candidates,
            ],
            _ => null,
        };
        writerOptions.SectionOrder =
        [
            PackageAssemblyQuerySections.Matches,
            PackageAssemblyQuerySections.Candidates,
            PackageAssemblyQuerySections.PopulationFailures,
        ];
        return writerOptions;
    }

    private sealed class FixedPackageSourceAuthorization(
        PackageSourceAuthorization authorization)
        : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
            return authorization;
        }
    }

    private sealed class AssemblySemanticFindStores : IDisposable
    {
        private readonly Dictionary<
            ConfiguredPackageAuthority,
            IPackageStore> _stores =
            new(ReferenceEqualityComparer.Instance);
        private string? _temporaryRoot;

        internal IPackageStore GetStore(
            ConfiguredPackageAuthority authority,
            PackageProducerIdentity producer)
        {
            if (!_stores.TryGetValue(authority, out IPackageStore? store))
            {
                store = new AuthorityScopedFileSystemPackageStore(
                    authority,
                    producer,
                    () =>
                        _temporaryRoot ??=
                            Directory.CreateTempSubdirectory(
                                "inspect-find").FullName);
                _stores.Add(authority, store);
            }
            return store;
        }

        public void Dispose() =>
            DotnetInspector.Packages.PackageExtractor.Cleanup(
                _temporaryRoot);
    }

    private sealed class AssemblySemanticFindProgressSink(
        VerboseLogger logger,
        int candidateCount)
        : IPackageAssemblySemanticFindNonterminalSink
    {
        public ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.Log(
                $"Evaluated {outcome.CandidateOrdinal} of "
                + $"{candidateCount} package candidates");
            return ValueTask.CompletedTask;
        }
    }

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
