using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

internal static class LibraryQueryCommand
{
    private static readonly InspectionEnvelopeJsonContract<
        LibraryQueryDocument> JsonContract =
            new(
                "library-query",
                1,
                LibraryQueryJsonContext.Default.LibraryQueryDocument);

    internal static async Task<int> ExecuteAsync(
        LibraryQueryOptions options,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        if (options.Discover is not null)
        {
            return DiscoverOutput.Execute(
                options.Discover,
                LibraryQuerySections.CreateSchema(),
                DiscoveryOutputRequest.Create(
                    options.JsonOutput
                        ? OutputFormat.Json
                        : options.Jsonl
                            ? OutputFormat.Jsonl
                            : options.Tsv
                                ? OutputFormat.Tsv
                                : options.Tabular
                                    ? OutputFormat.Table
                                    : OutputFormat.Markdown,
                    options.Tree,
                    options.Tabular,
                    options.NoHeader,
                    projection: options),
                sectionCostAnnotations:
                    LibraryQuerySections.Catalog.Pipeline
                        .GetCostAnnotations(),
                sectionCategories:
                    LibraryQuerySections.Catalog.SelectionCategoryMap,
                catalogHiddenSections:
                    LibraryQuerySections.Catalog.Pipeline
                        .GetCatalogHiddenSections(),
                listedCategoryDoors:
                    LibraryQuerySections.Catalog.Pipeline
                        .GetListedCategoryDoors(),
                semanticRowSelection: options.Plan.RowSelection,
                semanticSelectionName: "Library Query");
        }

        if (options.Population is null)
        {
            CommandError.Write(
                "Library Query requires one directory or --platform framework.");
            return 1;
        }

        AssemblySet population;
        try
        {
            population = await AssemblySetResolver.CollectAsync(
                context.HttpClient,
                options.CreateAssemblySetRequest(cancellationToken),
                context.Logger.Log).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException)
        {
            CommandError.Write(
                $"Library Query could not form its population: {ex.Message}");
            return 1;
        }

        using (population)
        {
            InspectionEnvelope<LibraryQueryDocument> envelope =
                LibraryQueryInspection.Execute(
                    population,
                    options.Plan,
                    cancellationToken);
            return CompleteExecution(options, envelope);
        }
    }

    internal static int CompleteExecution(
        LibraryQueryOptions options,
        InspectionEnvelope<LibraryQueryDocument> envelope)
    {
        // A reached explicit --take is the answer's scope, not an incomplete
        // reason, in every format (cli-execution-bounds.md#disclosure). A
        // default candidate ceiling that stopped evaluation stays incomplete.
        LibraryQueryDocument observed = envelope.Content;
        bool takeScopeReached =
            options.ExplicitTake
            && (observed.Summary.IncompleteReasons
                & LibraryQueryIncompleteReason.CandidateLimit) != 0;
        LibraryQueryDocument document = takeScopeReached
            ? observed with
            {
                Summary = observed.Summary with
                {
                    IncompleteReasons = observed.Summary.IncompleteReasons
                        & ~LibraryQueryIncompleteReason.CandidateLimit,
                },
            }
            : observed;
        if (options.EnvelopeOutput || options.IsContentJson)
        {
            bool wrote = InspectionEnvelopeOutput.TryWrite(
                takeScopeReached
                    ? new InspectionEnvelope<LibraryQueryDocument>(
                        document,
                        envelope.Share,
                        envelope.Diagnostics)
                    : envelope,
                JsonContract,
                options.EnvelopeOutput,
                options.CompactJson);
            WriteDiagnostics(document, takeScopeReached);
            return wrote && document.Summary.IsComplete ? 0 : 1;
        }

        if (!CliSemanticRowSelection.TrySelect(
                options.Plan.RowSelection,
                document.Results,
                "library",
                failure =>
                    $"Library Query row selection stage "
                    + $"{failure.Failure.StageNumber} requires Library row "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} Library rows are available.",
                out IReadOnlyList<LibraryQueryMatch> displayResults))
        {
            WriteDiagnostics(document, takeScopeReached);
            return 1;
        }

        // Count reports the rows Rows would render, with the same
        // completion warnings and exit status (section-row-shaping.md#
        // incomplete-evaluation). Only a population that failed before
        // admitting any candidate observed nothing to count.
        if (options.Count
            && (document.Summary.IncompleteReasons
                & LibraryQueryIncompleteReason.PopulationFailure) != 0
            && document.Summary.Candidates == 0)
        {
            WriteDiagnostics(document, takeScopeReached);
            CommandError.Write(
                "Cannot count Library Query rows because population formation "
                + "failed before any candidate was evaluated.");
            return 1;
        }

        LibraryQueryView view = LibraryQuerySections.CreateDocument(
            options.Population?.DisplayName ?? "population",
            displayResults,
            document.Summary,
            takeScopeReached);
        HashSet<string> includeSections =
            options.IncludeSections
            ?? (options.SelectDefault
                ? [.. LibraryQuerySections.BareSelectSectionNames]
                :
                [
                    document.HasLibraries
                        ? LibraryQuerySections.LibrariesName
                        : LibraryQuerySections.QuerySummaryName,
                ]);
        WriteOutput(view, options, includeSections);
        WriteDiagnostics(document, takeScopeReached);
        return document.Summary.IsComplete ? 0 : 1;
    }

    private static void WriteDiagnostics(
        LibraryQueryDocument document,
        bool takeScopeReached)
    {
        foreach (LibraryQueryFailure failure in document.Failures)
        {
            string subject =
                failure.Library?.ToString()
                ?? failure.Path?.ToString()
                ?? failure.Source?.ToString()
                ?? "Library Query";
            CommandError.WriteWarning(
                $"{subject}: {failure.Kind}: {failure.Message}");
        }

        if (takeScopeReached)
        {
            CommandError.WriteNote(
                "Library Query scope: the first "
                + $"{document.Summary.Candidates} of "
                + $"{document.Summary.PopulationCandidates} Libraries "
                + "(--take); matches cover only that population.");
        }
        else if ((document.Summary.IncompleteReasons
                & LibraryQueryIncompleteReason.CandidateLimit)
            != 0)
        {
            CommandError.WriteWarning(
                "Library Query completion: candidate limit reached; "
                + $"scanned {document.Summary.Candidates}/"
                + $"{document.Summary.PopulationCandidates} Libraries. "
                + "These results do not exhaust the explicit population.");
        }
    }

    private static void WriteOutput(
        LibraryQueryView view,
        LibraryQueryOptions options,
        HashSet<string> includeSections)
    {
        EmptyLibraryQueryView? emptyView =
            view.Results.Count == 0
            && includeSections.Contains(
                LibraryQuerySections.LibrariesName)
                ? EmptyLibraryQueryView.From(view)
                : null;

        void Serialize(
            TextWriter writer,
            IMarkoutFormatter formatter,
            MarkoutWriterOptions writerOptions)
        {
            writerOptions.IncludeSections = includeSections;
            writerOptions.SectionOrder =
                LibraryQuerySections.Catalog.AlphabeticalSectionOrder;
            if (emptyView is null)
            {
                MarkoutSerializer.Serialize(
                    view,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions);
            }
            else
            {
                MarkoutSerializer.Serialize(
                    emptyView,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions);
            }
        }

        if (options.Count)
        {
            CountOutput.WriteCount(view.Results.Count);
        }
        else if (options.JsonOutput)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                Serialize,
                !options.CompactJson,
                maxRows: null,
                sectionOrder:
                    LibraryQuerySections.Catalog.AlphabeticalSectionOrder);
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
                Serialize,
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
                    return emptyView is null
                        ? MarkoutSerializer.Serialize(
                            view,
                            SearchViewContext.Default,
                            writerOptions)
                        : MarkoutSerializer.Serialize(
                            emptyView,
                            SearchViewContext.Default,
                            writerOptions);
                },
                options.Columns,
                options.Fields);
        }
    }
}
