using System.Collections.Immutable;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
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

    internal static int Execute(
        LibraryQueryOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Discover is not null)
        {
            return DiscoverOutput.Execute(
                options.Discover,
                LibraryQuerySections.CreateSchema(),
                DiscoveryOutputRequest.Create(
                    options.JsonOutput ? OutputFormat.Json
                        : options.Jsonl ? OutputFormat.Jsonl
                        : options.Tsv ? OutputFormat.Tsv
                        : options.Tabular ? OutputFormat.Table
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
                semanticRowSelection: options.RowSelection,
                semanticSelectionName: "Library Query");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using LibraryQueryExecution execution =
            LibraryQueryExecution.Create(
                options.Sources,
                options.Plan.MaximumCandidates,
                cancellationToken);
        InspectionEnvelope<LibraryQueryDocument> envelope =
            LibraryQueryInspection.Execute(
                execution.Population,
                options.Plan);
        LibraryQueryDocument document = envelope.Content;

        if (options.EnvelopeOutput)
        {
            bool wrote = InspectionEnvelopeOutput.TryWrite(
                envelope,
                JsonContract,
                includeEnvelope: true,
                compactJson: false);
            WriteDiagnostics(document);
            return wrote && document.Summary.IsExact ? 0 : 1;
        }

        if (options.Count && !document.Summary.IsExact)
        {
            CommandError.Write(
                "Library Query cannot produce an exact count because "
                + $"completion is {document.Summary.Completion}.");
            WriteDiagnostics(document);
            return 1;
        }

        if (!CliSemanticRowSelection.TrySelect(
                options.RowSelection,
                document.Results,
                LibraryQuerySections.LibrariesName,
                FormatRowSelectionFailure,
                out IReadOnlyList<LibraryQueryMatch> selected))
        {
            return 1;
        }

        if (options.Count)
        {
            CountOutput.WriteCount(selected.Count);
            return 0;
        }

        LibraryQueryView view = LibraryQuerySections.CreateDocument(
            selected,
            document.Summary);
        WriteOutput(view, options);
        WriteDiagnostics(document);
        return document.Summary.IsExact ? 0 : 1;
    }

    private static void WriteOutput(
        LibraryQueryView view,
        LibraryQueryOptions options)
    {
        HashSet<string> includeSections =
        [
            LibraryQuerySections.LibrariesName,
        ];
        EmptyLibraryQueryView? empty =
            view.Results.Count == 0
                ? new()
                {
                    QuerySummary = view.QuerySummary,
                }
                : null;

        void Serialize(
            TextWriter writer,
            IMarkoutFormatter formatter,
            MarkoutWriterOptions writerOptions)
        {
            writerOptions.IncludeSections = includeSections;
            if (empty is null)
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
                    empty,
                    writer,
                    formatter,
                    SearchViewContext.Default,
                    writerOptions);
            }
        }

        if (options.JsonOutput)
        {
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                Serialize,
                indented: true,
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
                    return empty is null
                        ? MarkoutSerializer.Serialize(
                            view,
                            SearchViewContext.Default,
                            writerOptions)
                        : MarkoutSerializer.Serialize(
                            empty,
                            SearchViewContext.Default,
                            writerOptions);
                },
                options.Columns,
                options.Fields);
        }
    }

    private static void WriteDiagnostics(LibraryQueryDocument document)
    {
        foreach (LibraryQueryFailure failure in document.Failures)
        {
            CommandError.WriteWarning(
                $"{failure.Source}: {failure.Kind}: {failure.Message}");
        }

        if (!document.Summary.IsExact)
        {
            CommandError.WriteWarning(
                $"Library Query completion: {document.Summary.Completion}; "
                + $"{document.Summary.Evaluated}/"
                + $"{document.Summary.Population} occurrences evaluated, "
                + $"{document.Summary.Matches} matches, "
                + $"{document.Summary.Failures} failures.");
        }
    }

    private static string FormatRowSelectionFailure(
        RowsCohortSemanticFailure<string> failure) =>
        $"Library Query row selection stage "
        + $"{failure.Failure.StageNumber} for '{failure.Identity}' "
        + $"requires row {failure.Failure.RequiredPosition}, but only "
        + $"{failure.Failure.AvailableCount} rows are available.";

    private sealed class LibraryQueryExecution : IDisposable
    {
        private readonly InspectionWorkspace _workspace;
        private readonly AssemblyContextGroup? _group;

        private LibraryQueryExecution(
            InspectionWorkspace workspace,
            AssemblyContextGroup? group,
            LibraryQueryPopulation population)
        {
            _workspace = workspace;
            _group = group;
            Population = population;
        }

        internal LibraryQueryPopulation Population { get; }

        internal static LibraryQueryExecution Create(
            IReadOnlyList<string> sources,
            int maximumCandidates,
            CancellationToken cancellationToken)
        {
            var workspace = new InspectionWorkspace();
            try
            {
                string[] paths = ExpandSources(
                    sources,
                    cancellationToken);
                var available = new List<(
                    int Ordinal,
                    string Source,
                    ResolvedAssemblyReference Assembly)>();
                var unavailable = new Dictionary<
                    int,
                    LibraryQueryPopulationOccurrence.Unavailable>();

                for (int ordinal = 0; ordinal < paths.Length; ordinal++)
                {
                    string path = paths[ordinal];
                    if (ordinal >= maximumCandidates)
                    {
                        unavailable[ordinal] = new(
                            ordinal,
                            path,
                            new(
                                CandidateOpenFailureKind.ResourceBudget,
                                "The candidate was not opened because the "
                                + "Library Query candidate limit was reached."));
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryCreateAssembly(
                            path,
                            out ResolvedAssemblyReference? assembly,
                            out CandidateOpenFailure? failure))
                    {
                        available.Add((ordinal, path, assembly!));
                    }
                    else
                    {
                        unavailable[ordinal] =
                            new(ordinal, path, failure!);
                    }
                }

                AssemblyContextGroup? group = null;
                var byOrdinal =
                    new Dictionary<int, AssemblyContextParticipant>();
                if (available.Count > 0)
                {
                    var sourcePolicies = available.Select(candidate => (
                        candidate.Assembly,
                        Policy: (IAssemblyBindingPolicy)
                            new AssemblyDependencyResolver(
                                new(candidate.Source))))
                        .ToArray();
                    var groupPolicy =
                        new SourceRelativeAssemblyGroupBindingPolicy(
                            sourcePolicies);
                    foreach (var candidate in available)
                    {
                        byOrdinal.Add(
                            candidate.Ordinal,
                            new(candidate.Assembly, groupPolicy));
                    }

                    group = workspace.CreateAssemblyContextGroup(
                        [.. byOrdinal.Values]);
                }
                ImmutableArray<LibraryQueryPopulationOccurrence> occurrences =
                [
                    .. Enumerable.Range(0, paths.Length).Select(ordinal =>
                        unavailable.TryGetValue(ordinal, out var failed)
                            ? (LibraryQueryPopulationOccurrence)failed
                            : new LibraryQueryPopulationOccurrence.Available(
                                ordinal,
                                byOrdinal[ordinal])),
                ];
                return new(
                    workspace,
                    group,
                    new(group, occurrences));
            }
            catch
            {
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }
        }

        private static string[] ExpandSources(
            IReadOnlyList<string> sources,
            CancellationToken cancellationToken)
        {
            var paths = new List<string>();
            foreach (string source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(source))
                {
                    foreach (string path in Directory
                        .EnumerateFiles(
                            source,
                            "*",
                            SearchOption.TopDirectoryOnly)
                        .Where(path => path.EndsWith(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase))
                        .Order(StringComparer.Ordinal))
                    {
                        paths.Add(Path.GetFullPath(path));
                    }
                }
                else
                {
                    paths.Add(Path.GetFullPath(source));
                }
            }

            return [.. paths];
        }

        private static bool TryCreateAssembly(
            string path,
            out ResolvedAssemblyReference? assembly,
            out CandidateOpenFailure? failure)
        {
            assembly = null;
            try
            {
                AssemblyDescriptorSelectionResult selected =
                    ResolvedAssemblyReference.SelectFromPath(
                        path,
                        AssemblyResolutionProvenance.Designated(
                            "library query"));
                switch (selected)
                {
                    case AssemblyDescriptorSelectionResult.Ready ready:
                        assembly = ready.Reference;
                        failure = null;
                        return true;
                    case AssemblyDescriptorSelectionResult.Rejected rejected:
                        failure = rejected.Failure;
                        return false;
                    case AssemblyDescriptorSelectionResult.Descriptorless:
                        failure = new(
                            CandidateOpenFailureKind.InvalidImage,
                            "The selected file does not contain managed metadata.");
                        return false;
                    default:
                        throw new InvalidOperationException(
                            "Unknown assembly descriptor selection result.");
                }
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or System.Security.SecurityException)
            {
                failure = new(
                    CandidateOpenFailureKind.Unreadable,
                    ex.Message);
                return false;
            }
        }

        public void Dispose()
        {
            _group?.Dispose();
            _workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
