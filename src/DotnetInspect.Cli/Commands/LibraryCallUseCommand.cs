using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;
using QuerySpace.Composition;

namespace DotnetInspect.Cli.Commands;

public static class LibraryCallUseCommand
{
    public const string Name = "libraries";

    internal const string ConsumerUseSitesSection =
        LibraryCallUseViewSections.ConsumerUseSites;
    internal const string ProviderApiTypesSection =
        LibraryCallUseViewSections.ProviderApiTypes;
    internal const string DirectUseClustersSection =
        LibraryCallUseViewSections.DirectUseClusters;
    internal const string CallSitesSection =
        LibraryCallUseViewSections.CallSites;
    internal const string PublicRootPathsSection =
        LibraryCallUseViewSections.PublicRootPaths;

    static readonly AssemblyPairClusterRootPathLimits RootPathLimits =
        new(
            new PublicMethodRootInventoryLimits(
                MaximumTypeDefinitions: 100_000,
                MaximumMethodDefinitions: 1_000_000,
                MaximumRoots: 1_000_000),
            new LibraryBodyRootPathLimits(
                MaximumDepth: 64,
                MaximumNodes: 1_000_000,
                MaximumEdges: 10_000_000,
                MaximumPaths: 100_000));

    static readonly string[] DefaultCallSiteColumns =
    [
        "Source Member",
        "Target Member",
        "Call",
        "Evidence Method",
        "IL Offset",
    ];

    static readonly string[] DefaultClusterCallSiteColumns =
    [
        "Source Member",
        "Source Token",
        "Target Member",
        "Target Token",
        "Call",
        "Evidence Method",
        "Evidence Token",
        "IL Offset",
    ];

    static readonly string[] DefaultSelectedColumns =
    [
        "Source Library",
        "Source Member",
        "Target Library",
        "Target Member",
        "Target Type",
        "Cluster",
        "Provider Types",
        "Source Members",
        "Target Members",
        "Extension Methods",
        "Call Sites",
        "Call",
        "Evidence Method",
        "IL Offset",
        "Public Root",
        "Public Root Token",
        "Direct Use Destination",
        "Destination Token",
        "Depth",
        "Method Path",
        "Physical Receipts",
    ];

    public static async Task<int> ExecuteAsync(
        LibraryCallUseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        DocumentSchema schema = CreateSchema();
        if (options.Schema && options.Discover is null)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }

        if (options.QueryPlan.Cluster is not null
            && options.Discover is not null)
        {
            CommandError.Write(
                "--where Cluster=... cannot be combined with -D/--discover.");
            return 1;
        }

        if (options.Discover is { } discover)
        {
            SectionCatalog<LibraryCallUseDiscoveryModel> catalog =
                LibraryCallUseSections.Catalog;
            SectionPipeline<LibraryCallUseDiscoveryModel> pipeline =
                catalog.Pipeline;
            return DiscoverOutput.Execute(
                discover,
                schema,
                DiscoveryOutputRequest.Create(
                    options.Format,
                    options.Tree,
                    options.Format == OutputFormat.Table,
                    options.NoHeader,
                    projection: options),
                rootLabel: "Library Call Use",
                sectionCostAnnotations: pipeline.GetCostAnnotations(),
                sectionCategories: catalog.SelectionCategoryMap,
                catalogHiddenSections:
                    options.Schema ? null : pipeline.GetCatalogHiddenSections(),
                listedCategoryDoors: pipeline.GetListedCategoryDoors(),
                exactOnlySections:
                    LibraryCallUseSections.ExactOnlySectionNames);
        }

        if (options.Tree)
        {
            CommandError.Write(
                "--tree is supported only with -D/--discover for library call-use schema.");
            return 1;
        }

        if (!TryResolveSelection(
                options,
                out string[] selectedNames,
                out bool defaultCallSiteView))
        {
            return 1;
        }
        var selectedNameSet = selectedNames.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        bool requiresPublicRootPaths =
            selectedNameSet.Contains(PublicRootPathsSection);
        if (requiresPublicRootPaths
            && options.QueryPlan.Cluster is null)
        {
            CommandError.Write(
                "'Public Root Paths' requires exactly one "
                    + "--where \"Cluster=<positive ordinal>\" predicate.");
            return 1;
        }

        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                selectedNameSet,
                fields: options.Fields,
                columns: options.Columns)
            || !ValidateTabularArity(
                options,
                selectedNames))
        {
            return 1;
        }

        LibraryCallUseSections.SemanticRowDeclaration? semanticRows =
            null;
        if (options.RowSelection is not null
            && !LibraryCallUseSections.TryGetSemanticRows(
                options.Select,
                out semanticRows))
        {
            throw new InvalidOperationException(
                "Semantic row selection was activated without a Graph "
                    + "Libraries row declaration.");
        }

        if (options.Libraries.Length != 2)
        {
            CommandError.Write(
                "Exactly two --library values are required.");
            CommandError.WriteLine(
                "Run 'dotnet-inspect graph libraries --help' for usage.");
            return 1;
        }

        string firstPath = Path.GetFullPath(options.Libraries[0]);
        string secondPath = Path.GetFullPath(options.Libraries[1]);
        if (string.Equals(
                firstPath,
                secondPath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            CommandError.Write(
                "Pairwise call use requires two distinct library paths.");
            return 1;
        }

        using AssemblySet assemblies =
            await AssemblySetResolver.CollectAsync(
                HttpClientFactory.Shared,
                new AssemblySetRequest
                {
                    Assemblies = [firstPath, secondPath],
                },
                options.Verbose
                    ? CommandError.WriteLine
                    : null).ConfigureAwait(false);
        foreach (AssemblySetDiagnostic diagnostic
            in assemblies.Diagnostics)
        {
            CommandError.WriteWarning(diagnostic.Message);
        }
        if (assemblies.Assemblies.Count != 2)
        {
            CommandError.Write(
                "Both libraries must resolve to local files.");
            return 1;
        }

        AssemblyPairCallUseResult? result = null;
        AssemblyPairCallUseResult? selectedResult = null;
        AssemblyPairDirectUseClusterProjection? allClusters = null;
        AssemblyPairDirectUseClusterProjection? selectedClusters = null;
        InspectionEnvelope<AssemblyPairClusterRootPathResult>?
            rootPathInspection = null;
        int? unavailableCluster = null;
        var unavailable = new List<string>();
        await using var workspace = new AssemblySetInspectionWorkspace();
        workspace.RunGroup(
            assemblies,
            (group, _) =>
            {
                if (group.Participants.Length != 2)
                {
                    unavailable.Add(
                        "Both libraries must contain managed metadata.");
                    return;
                }

                try
                {
                    result = AssemblyPairCallUseQuery.Execute(
                        group,
                        group.Participants[0].Assembly,
                        group.Participants[1].Assembly);
                    bool requiresClusters =
                        options.QueryPlan.Cluster is not null
                        || semanticRows?.Rows
                            .RequiresDirectUseClusters is true
                        || (semanticRows is null
                            && selectedNameSet.Contains(
                                DirectUseClustersSection));
                    allClusters = requiresClusters
                        ? AssemblyPairDirectUseClusterProjection
                            .Create(result)
                        : new(result, []);
                    selectedResult = result;
                    selectedClusters = allClusters;
                    if (options.QueryPlan.Cluster is int clusterOrdinal)
                    {
                        AssemblyPairDirectUseClusterProjection? selected =
                            GraphLibrariesQuery.Apply(
                                options.QueryPlan,
                                allClusters);
                        if (selected is null)
                        {
                            unavailableCluster = clusterOrdinal;
                            return;
                        }

                        selectedResult = selected.Pair;
                        selectedClusters =
                            selectedNameSet.Contains(
                                DirectUseClustersSection)
                                ? selected
                                : new(selected.Pair, []);
                        if (requiresPublicRootPaths)
                        {
                            rootPathInspection =
                                AssemblyPairClusterRootPathInspection
                                    .Execute(
                                        group,
                                        selected,
                                        RootPathLimits,
                                        cancellationToken);
                        }
                    }
                    else if (!selectedNameSet.Contains(
                            DirectUseClustersSection))
                    {
                        selectedClusters = new(result, []);
                    }
                }
                catch (ArgumentException exception)
                    when (exception
                        is AssemblyPairCallUseRequestException
                            or AssemblyPairClusterRootPathRequestException)
                {
                    unavailable.Add(exception.Message);
                }
            },
            (entry, failure) =>
                unavailable.Add(
                    $"{entry.Path}: {failure}"));
        if (result is null)
        {
            CommandError.Write(
                "The library pair could not be inspected.",
                [.. unavailable]);
            return 1;
        }

        if (unavailableCluster is int clusterOrdinal)
        {
            WriteClusterNotFound(
                result,
                allClusters!,
                clusterOrdinal);
            return 1;
        }

        AssemblyPairCallUseProjection projection =
            AssemblyPairCallUseProjection.Create(selectedResult!);
        IReadOnlyList<AssemblyPairCallUseOccurrence> selectedOccurrences =
            selectedResult!.Occurrences;
        bool wroteCount = false;
        if (semanticRows is not null)
        {
            var rowProjection =
                new GraphLibrariesSectionRowProjection(
                    projection,
                    selectedClusters!,
                    selectedOccurrences);
            QuerySpaceTerminalRequirement terminal =
                options.Count
                    ? QuerySpaceTerminalRequirement.Count
                    : QuerySpaceTerminalRequirement.Rows;
            QuerySpaceSectionRowResolutionResult<
                GraphLibrariesSectionRowProjection> resolution =
                    semanticRows.Rows.Resolve(
                        options.QueryPlan,
                        options.RowSelection!,
                        terminal,
                        rowProjection);
            if (!resolution.IsSuccess)
            {
                CommandError.Write(
                    "Graph Libraries row selection could not be resolved: "
                        + $"{resolution.Failure!.RowQueryFailure.Reason}.");
                return 1;
            }

            if (options.Count)
            {
                SectionCountOutcome<string, string> count =
                    QuerySpaceSectionRowExecutor.ApplyCount<
                        GraphLibrariesSectionRowProjection,
                        string>(resolution.Request!);
                if (count is SectionCountOutcome<
                        string,
                        string>.Semantic failure)
                {
                    CommandError.Write(
                        semanticRows.FormatFailure(
                            failure.StageNumber,
                            failure.RequiredPosition,
                            failure.AvailableCount));
                    return 1;
                }

                if (count is not SectionCountOutcome<
                        string,
                        string>.Completed completed)
                {
                    throw new InvalidOperationException(
                        "Graph Libraries Count did not produce an exact "
                            + "row-set cardinality.");
                }
                CountOutput.WriteCount(
                    AssertSingleCount(
                        completed,
                        semanticRows.Rows.RowSet));
                wroteCount = true;
            }
            else
            {
                SectionRowsOutcome<
                    string,
                    GraphLibrariesSectionRowProjection> rows =
                        QuerySpaceSectionRowExecutor.ApplyRows(
                            resolution.Request!);
                if (!rows.IsSuccess)
                {
                    CommandError.Write(
                        semanticRows.FormatFailure(
                            rows.Failure!.Failure.StageNumber,
                            rows.Failure.Failure.RequiredPosition,
                            rows.Failure.Failure.AvailableCount));
                    return 1;
                }

                rowProjection = rows.Rebind(rowProjection);
                projection = rowProjection.Summaries;
                selectedClusters = rowProjection.Clusters;
                selectedOccurrences = rowProjection.CallSites;
            }
        }
        if (!wroteCount)
        {
            Write(
                selectedResult!,
                projection,
                selectedClusters!,
                rootPathInspection,
                options,
                selectedNames,
                defaultCallSiteView,
                selectedOccurrences);
        }
        if (rootPathInspection is { Content.IsComplete: false })
        {
            CommandError.Write(
                "Public root-path evidence is incomplete.",
                [
                    .. rootPathInspection.Diagnostics.Select(
                        diagnostic =>
                            diagnostic.Summary.ToString()),
                ]);
            return 1;
        }

        if (!selectedResult!.IsComplete)
        {
            CommandError.Write(
                "Pairwise call-use evidence is incomplete.",
                [.. FailureDetails(selectedResult)]);
            return 1;
        }

        return 0;
    }

    static int AssertSingleCount(
        SectionCountOutcome<string, string>.Completed completed,
        string expectedRowSet)
    {
        SectionCountEntry<string> count =
            completed.Counts is [var single]
                && string.Equals(
                    single.Identity,
                    expectedRowSet,
                    StringComparison.Ordinal)
                    ? single
                    : throw new InvalidOperationException(
                        "Graph Libraries Count returned an unexpected "
                            + "row-set identity.");
        return count.Value;
    }

    static DocumentSchema CreateSchema() =>
        LibraryCallUseViewContext.Default
            .GetSchemaInfo<LibraryCallUseSelectedView>()!
            .ToDocumentSchema();

    static bool TryResolveSelection(
        LibraryCallUseOptions options,
        out string[] selectedNames,
        out bool defaultCallSiteView)
    {
        defaultCallSiteView =
            options.Select is null && !options.SelectDefault;
        if (defaultCallSiteView)
        {
            selectedNames = [CallSitesSection];
            return true;
        }

        if (options.SelectDefault)
        {
            selectedNames = LibraryCallUseSections.BareSelectSectionNames;
            return true;
        }

        if (options.Select is { Length: 0 })
        {
            CommandError.Write(
                "--select requires at least one name.");
            selectedNames = [];
            return false;
        }

        SectionCatalog<LibraryCallUseDiscoveryModel> catalog =
            LibraryCallUseSections.Catalog;
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            catalog.SelectableSectionNames,
            infoSections: [CallSitesSection],
            catalog.SelectionCategoryMap,
            selectDefault: false,
            exactOnlySections:
                LibraryCallUseSections.ExactOnlySectionNames);
        if (SelectOutput.WriteUnresolved(selection))
        {
            selectedNames = [];
            return false;
        }
        if (options.Select is { Length: > 0 } selectors
            && selection.Sections is null)
        {
            SelectOutput.WriteUnresolved(
                new SelectResult(
                    null,
                    selectors
                        .Select(selector =>
                            new SelectMiss(
                                selector,
                                [],
                                IsGlob: true))
                        .ToArray()));
            selectedNames = [];
            return false;
        }

        selectedNames =
        [
            .. catalog.AlphabeticalSectionOrder.Where(
                name => selection.Sections!.Contains(name)),
        ];
        return true;
    }

    static bool ValidateTabularArity(
        LibraryCallUseOptions options,
        IReadOnlyCollection<string> selectedNames)
    {
        if (options.Count
            || options.Format is not (
                OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl))
        {
            return true;
        }

        if (selectedNames.Count == 1)
            return true;

        string format = options.Format switch
        {
            OutputFormat.Tsv => "--format tsv",
            OutputFormat.Jsonl => "--format jsonl",
            _ => "--format table",
        };
        CommandError.Write(
            $"{format} requires exactly one selected table section; "
            + $"this view selects {selectedNames.Count}: "
            + $"{string.Join(", ", selectedNames)}.");
        CommandError.WriteLine(
            "Use -S with one section name, or --format markdown or --format json for multi-section output.");
        return false;
    }

    static void Write(
        AssemblyPairCallUseResult result,
        AssemblyPairCallUseProjection projection,
        AssemblyPairDirectUseClusterProjection clusters,
        InspectionEnvelope<AssemblyPairClusterRootPathResult>?
            rootPathInspection,
        LibraryCallUseOptions options,
        string[] selectedNames,
        bool defaultCallSiteView,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences)
    {
        if (defaultCallSiteView)
        {
            WriteDefaultCallSites(
                result,
                occurrences,
                options);
            return;
        }

        WriteSelected(
            result,
            projection,
            clusters,
            rootPathInspection,
            options,
            selectedNames,
            occurrences);
    }

    static void WriteDefaultCallSites(
        AssemblyPairCallUseResult result,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences,
        LibraryCallUseOptions options)
    {
        List<LibraryCallUseCallSiteRow> rows =
            CreateCallSiteRows(occurrences);
        IReadOnlyList<AssemblyPairCallUseOccurrence> selectedOccurrences =
            RowWindow.Apply(options.Rows, occurrences);
        var view = new LibraryCallUseCallSitesView
        {
            Title = "Library Call Use",
            Description = CreateDefaultDescription(
                result,
                selectedOccurrences,
                options.Rows,
                options.Cluster),
            Rows = rows,
        };

        string[]? columns = options.Columns;
        string[]? fields = options.Fields;
        if (columns is null
            && fields is null
            && options.Format is
                OutputFormat.Markdown
                or OutputFormat.PlainText)
        {
            columns = options.Cluster is null
                ? DefaultCallSiteColumns
                : DefaultClusterCallSiteColumns;
        }

        var writerOptions =
            OutputFormatter.CreateProjectedWriterOptions(
                columns,
                fields,
                options.Rows);
        if (options.Count)
        {
            CountProjection count = CountProjectionFormatter.Capture(
                view,
                LibraryCallUseViewContext.Default,
                writerOptions);
            CountOutput.WriteCount(count.Total);
            return;
        }

        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions>
            serialize = (writer, formatter, projectedOptions) =>
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    LibraryCallUseViewContext.Default,
                    projectedOptions);
        switch (options.Format)
        {
            case OutputFormat.Json:
                OutputFormatter.WriteProjectedJson(
                    Console.Out,
                    columns,
                    fields,
                    serialize,
                    maxRows: options.Rows);
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
            case OutputFormat.Jsonl:
                OutputFormatter.WriteProjectedTable(
                    Console.Out,
                    showHeader: !options.NoHeader,
                    tsv: options.Format == OutputFormat.Tsv,
                    jsonl: options.Format == OutputFormat.Jsonl,
                    columns,
                    fields,
                    serialize,
                    maxRows: options.Rows);
                break;
            default:
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    options.Format == OutputFormat.PlainText
                        ? new PlainTextFormatter()
                        : new MarkdownFormatter(),
                    LibraryCallUseViewContext.Default,
                    writerOptions);
                break;
        }
    }

    static void WriteSelected(
        AssemblyPairCallUseResult result,
        AssemblyPairCallUseProjection projection,
        AssemblyPairDirectUseClusterProjection clusters,
        InspectionEnvelope<AssemblyPairClusterRootPathResult>?
            rootPathInspection,
        LibraryCallUseOptions options,
        IReadOnlyCollection<string> selectedNames,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences)
    {
        DocumentSchema schema = CreateSchema();
        string[]? projectedColumns =
            ResolveProjectedColumns(options);
        HashSet<string> renderedNames =
            projectedColumns is { Length: > 0 }
                ? selectedNames
                    .Where(section =>
                        schema.ValidateProjection(
                            section,
                            projectedColumns)
                            .Resolved.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : selectedNames.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
        LibraryCallUseSelectedView view =
            CreateSelectedView(
                projection,
                clusters,
                rootPathInspection?.Content,
                occurrences);
        IReadOnlyList<string> sectionOrder =
            LibraryCallUseSections.Catalog.AlphabeticalSectionOrder;
        var writerOptions =
            OutputFormatter.CreateProjectedWriterOptions(
                projectedColumns,
                fields: null,
                options.Rows);
        writerOptions.IncludeSections = renderedNames;
        writerOptions.SectionOrder = sectionOrder;

        if (options.Count)
        {
            string[] ordered =
            [
                .. LibraryCallUseSections.Catalog.AlphabeticalSectionOrder
                    .Where(selectedNames.Contains),
            ];
            CountProjection counts = CountProjectionFormatter.Capture(
                view,
                LibraryCallUseViewContext.Default,
                writerOptions);
            if (ordered.Length == 1)
            {
                CountOutput.WriteCount(counts.Total);
                return;
            }

            if (!CountOutput.ValidateMapFormat(
                    options.Format,
                    ordered))
            {
                return;
            }

            CountOutput.Write(
                counts,
                ordered,
                options.Format,
                options.NoHeader);
            return;
        }

        if (options.Format == OutputFormat.Json)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = renderedNames;
                    writerOptions.SectionOrder = sectionOrder;
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                },
                maxRows: options.Rows,
                sectionOrder: sectionOrder);
            return;
        }

        if (options.Format
                is OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl)
        {
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                showHeader: !options.NoHeader,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    writerOptions.IncludeSections = renderedNames;
                    writerOptions.SectionOrder = sectionOrder;
                    MarkoutSerializer.Serialize(
                        view,
                        writer,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                },
                maxRows: options.Rows);
            return;
        }

        string[]? humanColumns =
            options.Columns is null && options.Fields is null
                ? DefaultSelectedColumns
                : projectedColumns;
        WriteSelectedHuman(
            result,
            projection,
            clusters,
            rootPathInspection?.Content,
            options,
            renderedNames,
            humanColumns,
            includeDocumentHeading: selectedNames.Count > 1,
            occurrences);
    }

    static void WriteSelectedHuman(
        AssemblyPairCallUseResult result,
        AssemblyPairCallUseProjection projection,
        AssemblyPairDirectUseClusterProjection clusters,
        AssemblyPairClusterRootPathResult? rootPaths,
        LibraryCallUseOptions options,
        IReadOnlySet<string> renderedNames,
        string[]? columns,
        bool includeDocumentHeading,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences)
    {
        IMarkoutFormatter formatter =
            options.Format == OutputFormat.PlainText
                ? new PlainTextFormatter()
                : new MarkdownFormatter();
        if (includeDocumentHeading)
        {
            MarkoutSerializer.Serialize(
                new LibraryCallUseHeaderView
                {
                    Description = FormatPair(result),
                },
                Console.Out,
                formatter,
                LibraryCallUseViewContext.Default);
        }

        var writerOptions =
            OutputFormatter.CreateProjectedWriterOptions(
                columns,
                fields: null,
                options.Rows);
        writerOptions.HeadingLevelOffset = 1;
        bool wroteDocument = includeDocumentHeading;
        foreach (string section
            in LibraryCallUseSections.Catalog.AlphabeticalSectionOrder)
        {
            if (!renderedNames.Contains(section))
                continue;

            if (wroteDocument)
            {
                Console.WriteLine();
                Console.WriteLine();
            }

            switch (section)
            {
                case ConsumerUseSitesSection:
                    MarkoutSerializer.Serialize(
                        CreateConsumerUseSitesView(
                            projection,
                            options.Rows,
                            options.Cluster),
                        Console.Out,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                    break;
                case ProviderApiTypesSection:
                    MarkoutSerializer.Serialize(
                        CreateProviderApiTypesView(
                            projection,
                            options.Rows,
                            options.Cluster),
                        Console.Out,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                    break;
                case DirectUseClustersSection:
                    MarkoutSerializer.Serialize(
                        CreateDirectUseClustersView(
                            clusters,
                            options.Rows,
                            options.Cluster),
                        Console.Out,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                    break;
                case CallSitesSection:
                    MarkoutSerializer.Serialize(
                        CreateSelectedCallSitesView(
                            projection,
                            occurrences,
                            options.Rows,
                            options.Cluster),
                        Console.Out,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                    break;
                case PublicRootPathsSection:
                    MarkoutSerializer.Serialize(
                        CreatePublicRootPathsView(
                            rootPaths!,
                            options.Rows),
                        Console.Out,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                    break;
            }
            wroteDocument = true;
        }
    }

    static LibraryCallUseSelectedView CreateSelectedView(
        AssemblyPairCallUseProjection projection,
        AssemblyPairDirectUseClusterProjection clusters,
        AssemblyPairClusterRootPathResult? rootPaths,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences) =>
        new()
        {
            ConsumerUseSites =
                [.. projection.ConsumerUseSites.Select(CreateConsumerUseSiteRow)],
            ProviderApiTypes =
                [.. projection.ProviderApiTypes.Select(CreateProviderApiTypeRow)],
            DirectUseClusters =
                [.. clusters.Clusters.Select(
                    CreateDirectUseClusterRow)],
            CallSites = CreateCallSiteRows(occurrences),
            PublicRootPaths = rootPaths is null
                ? []
                : CreatePublicRootPathRows(rootPaths),
        };

    static LibraryCallUseConsumerUseSitesView CreateConsumerUseSitesView(
        AssemblyPairCallUseProjection projection,
        RowWindow? rows,
        int? cluster)
    {
        List<LibraryCallUseConsumerUseSiteRow> values =
            [.. projection.ConsumerUseSites.Select(CreateConsumerUseSiteRow)];
        return new()
        {
            Description = CreateSectionDescription(
                ScopeToCluster(
                    "Attributed source methods that directly use the other library. "
                        + "These are use sites, not inferred features or public entry points.",
                    cluster),
                projection.IsComplete
                    ? "No direct consumer use sites were observed."
                    : "No exact consumer use sites were observed; the evidence is incomplete.",
                values,
                rows),
            Rows = HasSelectedRows(values, rows) ? values : null,
        };
    }

    static LibraryCallUseProviderApiTypesView CreateProviderApiTypesView(
        AssemblyPairCallUseProjection projection,
        RowWindow? rows,
        int? cluster)
    {
        List<LibraryCallUseProviderApiTypeRow> values =
            [.. projection.ProviderApiTypes.Select(CreateProviderApiTypeRow)];
        return new()
        {
            Description = CreateSectionDescription(
                ScopeToCluster(
                    "Structured declaring types of exact selected target methods. "
                        + "These are consumed provider types, not inferred capability clusters or public API boundaries.",
                    cluster),
                projection.IsComplete
                    ? "No provider API types were observed."
                    : "No exact provider API types were observed; the evidence is incomplete.",
                values,
                rows),
            Rows = HasSelectedRows(values, rows) ? values : null,
        };
    }

    static LibraryCallUseCallSitesView CreateSelectedCallSitesView(
        AssemblyPairCallUseProjection projection,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences,
        RowWindow? rows,
        int? cluster)
    {
        List<LibraryCallUseCallSiteRow> values =
            CreateCallSiteRows(occurrences);
        return new()
        {
            Title = CallSitesSection,
            Description = CreateSectionDescription(
                ScopeToCluster(
                    "Exact physical call and construction occurrences crossing the library pair.",
                    cluster),
                projection.IsComplete
                    ? "No direct pair call use was observed."
                    : "No exact pair call use was observed; the evidence is incomplete.",
                values,
                rows),
            Rows = HasSelectedRows(values, rows) ? values : null,
        };
    }

    static LibraryCallUseDirectUseClustersView CreateDirectUseClustersView(
        AssemblyPairDirectUseClusterProjection projection,
        RowWindow? rows,
        int? cluster)
    {
        List<LibraryCallUseDirectUseClusterRow> values =
        [
            .. projection.Clusters.Select(
                CreateDirectUseClusterRow),
        ];
        return new()
        {
            Description = CreateSectionDescription(
                ScopeToCluster(
                    "Connected components of exact source-method to target-method use. "
                        + "These are direct-use clusters, not semantic features or source-inlining recommendations.",
                    cluster),
                projection.IsComplete
                    ? "No direct-use clusters were observed."
                    : "No exact direct-use clusters were observed; the evidence is incomplete.",
                values,
                rows),
            Rows = HasSelectedRows(values, rows) ? values : null,
        };
    }

    static LibraryCallUsePublicRootPathsView CreatePublicRootPathsView(
        AssemblyPairClusterRootPathResult result,
        RowWindow? rows)
    {
        List<LibraryCallUsePublicRootPathRow> values =
            CreatePublicRootPathRows(result);
        return new()
        {
            Description = CreateSectionDescription(
                "Shortest local static MethodDef paths from exact public "
                    + $"roots to the consumer use sites in Direct Use "
                    + $"Cluster {result.Cluster.Ordinal}.",
                result.IsComplete
                    ? "No selected public root has a local static path "
                        + "to this cluster's consumer use sites."
                    : "No retained public-root path was observed; the "
                        + "evidence is incomplete.",
                values,
                rows),
            Rows = HasSelectedRows(values, rows) ? values : null,
        };
    }

    static string CreateDefaultDescription(
        AssemblyPairCallUseResult result,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences,
        RowWindow? rows,
        int? cluster)
    {
        string[] summaries = [.. RelationshipSummaries(occurrences)];
        string detail = summaries.Length > 0
            ? string.Join("\n\n", summaries)
            : result.Occurrences.Length > 0 && rows is not null
                ? "No direct pair call use is selected by the row window."
                : result.IsComplete
                    ? "No direct pair call use was observed."
                    : "No exact pair call use was observed; the evidence is incomplete.";
        string subject = cluster is int clusterOrdinal
            ? $"Direct Use Cluster {clusterOrdinal} in {FormatPair(result)}"
            : FormatPair(result);
        return $"{subject}\n\n{detail}";
    }

    static string ScopeToCluster(
        string summary,
        int? cluster) =>
        cluster is int clusterOrdinal
            ? $"{summary}\n\nRestricted to Direct Use Cluster {clusterOrdinal}."
            : summary;

    static string CreateSectionDescription<T>(
        string summary,
        string emptyText,
        IReadOnlyList<T> values,
        RowWindow? rows) =>
        HasSelectedRows(values, rows)
            ? summary
            : $"{summary}\n\n{(values.Count == 0 ? emptyText : "No rows are selected by the row window.")}";

    static bool HasSelectedRows<T>(
        IReadOnlyList<T> values,
        RowWindow? rows) =>
        RowWindow.Apply(rows, values).Count > 0;

    static LibraryCallUseConsumerUseSiteRow CreateConsumerUseSiteRow(
        AssemblyPairCallUseConsumerUseSite site) =>
        new()
        {
            SourceLibrary = AssemblyIdentityFormatter.Format(site.Source.Identity),
            SourceMvid = site.SourceModuleVersionId.ToString("D"),
            SourceMember = LibraryMetadataService.FormatMethod(site.SourceMethod),
            SourceToken = $"0x{site.SourceMethod.MetadataToken:X8}",
            TargetLibrary = AssemblyIdentityFormatter.Format(site.Target.Identity),
            TargetMvid = site.TargetModuleVersionId.ToString("D"),
            ProviderTypes = site.TargetTypes.Length,
            TargetMembers = site.TargetMethods.Length,
            CallSites = site.CallSiteCount,
            CallSiteRows = FormatOccurrenceRows(site.OccurrenceIndexes),
        };

    static LibraryCallUseProviderApiTypeRow CreateProviderApiTypeRow(
        AssemblyPairCallUseProviderApiType type) =>
        new()
        {
            SourceLibrary = AssemblyIdentityFormatter.Format(type.Source.Identity),
            SourceMvid = type.SourceModuleVersionId.ToString("D"),
            TargetLibrary = AssemblyIdentityFormatter.Format(type.Target.Identity),
            TargetMvid = type.TargetModuleVersionId.ToString("D"),
            TargetType = type.TargetType.Resolution?.Type.ToMetadataFullName()
                ?? type.TargetType.ToQualifiedDisplayString(),
            SourceMembers = type.SourceMethods.Length,
            TargetMembers = type.TargetMethods.Length,
            CallSites = type.CallSiteCount,
            CallSiteRows = FormatOccurrenceRows(type.OccurrenceIndexes),
        };

    static LibraryCallUseDirectUseClusterRow CreateDirectUseClusterRow(
        AssemblyPairDirectUseCluster cluster) =>
        new()
        {
            SourceLibrary = AssemblyIdentityFormatter.Format(
                cluster.Identity.Source.Identity),
            SourceMvid =
                cluster.Identity.SourceModuleVersionId.ToString("D"),
            TargetLibrary = AssemblyIdentityFormatter.Format(
                cluster.Identity.Target.Identity),
            TargetMvid =
                cluster.Identity.TargetModuleVersionId.ToString("D"),
            Cluster = cluster.Ordinal,
            Derivation = cluster.Derivation switch
            {
                AssemblyPairDirectUseClusterDerivation
                    .ExactBipartiteConnectedComponent =>
                    "exact-bipartite-connected-component",
                _ => cluster.Derivation.ToString(),
            },
            AnchorSourceToken =
                $"0x{cluster.Identity.AnchorSourceMethodToken:X8}",
            AnchorTargetToken =
                $"0x{cluster.Identity.AnchorTargetMethodToken:X8}",
            SourceMembers = cluster.SourceMethods.Length,
            ProviderTypes = cluster.TargetTypes.Length,
            TargetMembers = cluster.TargetMethods.Length,
            ExtensionMethods = cluster.ExtensionMethodCount,
            CallSites = cluster.CallSiteCount,
            CallSiteRows =
                FormatOccurrenceRows(cluster.OccurrenceIndexes),
        };

    static List<LibraryCallUseCallSiteRow> CreateCallSiteRows(
        IEnumerable<AssemblyPairCallUseOccurrence> occurrences) =>
    [
        .. occurrences.Select(occurrence => new LibraryCallUseCallSiteRow
        {
            SourceLibrary = AssemblyIdentityFormatter.Format(occurrence.Source.Identity),
            SourceMvid = occurrence.SourceModuleVersionId.ToString("D"),
            SourceMember = LibraryMetadataService.FormatMethod(occurrence.SourceMethod),
            SourceToken = $"0x{occurrence.SourceMethod.MetadataToken:X8}",
            TargetLibrary = AssemblyIdentityFormatter.Format(occurrence.Target.Identity),
            TargetMvid = occurrence.TargetModuleVersionId.ToString("D"),
            TargetMember = LibraryMetadataService.FormatMethod(occurrence.TargetMethod),
            TargetToken = $"0x{occurrence.TargetMethod.MetadataToken:X8}",
            Call = FormatCallKind(occurrence.Call.Kind),
            EvidenceMethod = LibraryMetadataService.FormatMethod(
                occurrence.Call.EvidenceMethod),
            EvidenceMvid = occurrence.Call.EvidenceMethod.ModuleVersionId.ToString("D"),
            EvidenceToken =
                $"0x{occurrence.Call.EvidenceMethod.MetadataToken:X8}",
            IlOffset = $"0x{occurrence.Call.ILOffset:X4}",
            OperandToken = $"0x{occurrence.Call.OperandToken:X8}",
            ExactTarget = occurrence.Call.ExactTarget ? "yes" : "no",
        }),
    ];

    static List<LibraryCallUsePublicRootPathRow>
        CreatePublicRootPathRows(
            AssemblyPairClusterRootPathResult result) =>
        result.Paths is null
            ? []
            :
            [
                .. result.Paths.Witnesses.Select(
                    witness =>
                        new LibraryCallUsePublicRootPathRow
                        {
                            SourceLibrary =
                                AssemblyIdentityFormatter.Format(
                                    result.Cluster.Identity.Source
                                        .Identity),
                            Cluster = result.Cluster.Ordinal,
                            PublicRoot =
                                LibraryMetadataService.FormatMethod(
                                    witness.Root),
                            PublicRootToken =
                                $"0x{witness.Root.MetadataToken:X8}",
                            DirectUseDestination =
                                LibraryMetadataService.FormatMethod(
                                    witness.Destination),
                            DestinationToken =
                                $"0x{witness.Destination.MetadataToken:X8}",
                            Depth = witness.Depth,
                            MethodPath = string.Join(
                                " -> ",
                                witness.Steps.IsEmpty
                                    ? [LibraryMetadataService.FormatMethod(
                                        witness.Root)]
                                    :
                                    [
                                        LibraryMetadataService.FormatMethod(
                                            witness.Root),
                                        .. witness.Steps.Select(
                                            step =>
                                                LibraryMetadataService
                                                    .FormatMethod(
                                                        step.Callee)),
                                    ]),
                            PhysicalReceipts = string.Join(
                                "; ",
                                witness.Steps.SelectMany(
                                    (step, stepIndex) =>
                                        step.CallSites.Select(
                                            call =>
                                                $"{stepIndex + 1}:"
                                                + $"0x{call.EvidenceMethod.MetadataToken:X8}"
                                                + $"+0x{call.ILOffset:X4}"
                                                + $"->0x{call.OperandToken:X8}"))),
                        }),
            ];

    static string FormatOccurrenceRows(
        IEnumerable<int> indexes) =>
        string.Join(
            ",",
            indexes.Select(index => index + 1));

    static string[]? ResolveProjectedColumns(
        LibraryCallUseOptions options)
    {
        if (options.Columns is not { Length: > 0 })
            return options.Fields is { Length: > 0 }
                ? options.Fields
                : null;
        if (options.Fields is not { Length: > 0 })
            return options.Columns;

        return
        [
            .. options.Columns
                .Concat(options.Fields)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    static string FormatAssembly(AssemblyContextSubject subject)
    {
        string name =
            LibraryCallUseViewText.Contain(subject.Identity.Name);
        return subject.Identity.Version is { } version
            ? $"{name}@{version}"
            : name;
    }

    static string FormatPair(AssemblyPairCallUseResult result) =>
        $"{FormatAssembly(result.Subjects[0])} "
        + "\u2194 "
        + FormatAssembly(result.Subjects[1]);

    static string FormatCallKind(CallKind kind) => kind switch
    {
        CallKind.Call => "call",
        CallKind.CallVirtual => "callvirt",
        CallKind.NewObject => "newobj",
        _ => kind.ToString(),
    };

    static IEnumerable<string> RelationshipSummaries(
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences) =>
        occurrences
            .GroupBy(occurrence => (
                occurrence.Source,
                occurrence.Target),
                AssemblySubjectPairComparer.Instance)
            .OrderBy(
                group => group.Key.Source.Identity.Name,
                StringComparer.Ordinal)
            .ThenBy(
                group => group.Key.Target.Identity.Name,
                StringComparer.Ordinal)
            .Select(group =>
            {
                int sourceMembers = group
                    .Select(occurrence => (
                        occurrence.SourceModuleVersionId,
                        occurrence.SourceMethod.MetadataToken))
                    .Distinct()
                    .Count();
                int targetMembers = group
                    .Select(occurrence => (
                        occurrence.TargetModuleVersionId,
                        occurrence.TargetMethod.MetadataToken))
                    .Distinct()
                    .Count();
                return
                    $"{FormatAssembly(group.Key.Source)} -> "
                    + $"{FormatAssembly(group.Key.Target)}: "
                    + $"{sourceMembers} source members, "
                    + $"{targetMembers} target members, "
                    + $"{group.Count()} call sites.";
            });

    static void WriteClusterNotFound(
        AssemblyPairCallUseResult result,
        AssemblyPairDirectUseClusterProjection clusters,
        int requested)
    {
        string availability = clusters.Clusters.Length == 0
            ? "No direct-use clusters were observed."
            : "Observed pair-wide cluster ordinals: "
                + $"1..{clusters.Clusters[^1].Ordinal}.";
        if (result.IsComplete)
        {
            CommandError.Write(
                $"Direct Use Cluster {requested} does not exist.",
                [availability]);
            return;
        }

        CommandError.Write(
            $"Direct Use Cluster {requested} was not observed; "
                + "pairwise call-use evidence is incomplete.",
            [availability, .. FailureDetails(result)]);
    }

    static IEnumerable<string> FailureDetails(
        AssemblyPairCallUseResult result)
    {
        foreach (AssemblyPairCallUseFailure failure in result.Failures)
        {
            yield return failure switch
            {
                AssemblyPairCallUseFailure.Rejected rejected =>
                    $"{FormatAssembly(rejected.Subject)}: "
                    + rejected.Failure.Detail,
                AssemblyPairCallUseFailure.InvalidImage invalid =>
                    $"{FormatAssembly(invalid.Subject)}: "
                    + invalid.Error.Message,
                _ => $"{FormatAssembly(failure.Subject)}: unavailable",
            };
        }

        foreach (AssemblyPairCallUseParticipant participant
            in result.Participants)
        {
            foreach (AnalysisDiagnostic diagnostic
                in participant.Diagnostics)
            {
                yield return
                    $"{FormatAssembly(participant.Subject)} "
                    + $"method 0x{diagnostic.MethodToken:X8}: "
                    + diagnostic.Message;
            }
        }

        if (result.Diagnostics.IsIncomplete)
        {
            yield return
                "Pair correspondence: "
                + $"{result.Diagnostics.UnresolvedCandidateCallCount} "
                + "call sites name the other library but could not be matched.";
        }
    }

    sealed class AssemblySubjectPairComparer
        : IEqualityComparer<(
            AssemblyContextSubject Source,
            AssemblyContextSubject Target)>
    {
        internal static AssemblySubjectPairComparer Instance { get; } =
            new();

        public bool Equals(
            (
                AssemblyContextSubject Source,
                AssemblyContextSubject Target) left,
            (
                AssemblyContextSubject Source,
                AssemblyContextSubject Target) right) =>
            ReferenceEquals(
                left.Source.Registration,
                right.Source.Registration)
            && ReferenceEquals(
                left.Target.Registration,
                right.Target.Registration);

        public int GetHashCode(
            (
                AssemblyContextSubject Source,
                AssemblyContextSubject Target) value) =>
            HashCode.Combine(
                value.Source.Registration,
                value.Target.Registration);
    }
}
