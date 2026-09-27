using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Research;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// <c>graph structure</c>: one library's Library Dependency Structure document
/// (namespace layering, cycles, and explaining type edges), composed from
/// focused Analysis evidence through the host-neutral query and envelope.
/// </summary>
public static class GraphStructureCommand
{
    public const string Name = "structure";

    const string GlobalNamespaceLabel = "<global>";

    static readonly InspectionEnvelopeJsonContract<LibraryDependencyStructureDocument> s_json =
        new(
            "library-dependency-structure",
            1,
            LibraryDependencyStructureInspectionJson.Write);

    public static async Task<int> ExecuteAsync(
        GraphStructureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if ((options.Library is null) == (options.Package is null))
        {
            CommandError.Write("Specify exactly one of --library or --package.");
            CommandError.WriteLine("Run 'dotnet-inspect graph structure --help' for usage.");
            return 1;
        }

        if (!TryResolveSections(options, out string[] sections))
            return 1;

        AssemblySetRequest request = options.Library is { } library
            ? new() { Assemblies = [Path.GetFullPath(library)] }
            : new() { Packages = [options.Package!], Tfm = options.Tfm };
        using AssemblySet assemblies = await AssemblySetResolver.CollectAsync(
            HttpClientFactory.Shared,
            request,
            options.Verbose ? CommandError.WriteLine : null).ConfigureAwait(false);
        foreach (AssemblySetDiagnostic diagnostic in assemblies.Diagnostics)
            CommandError.WriteWarning(diagnostic.Message);
        if (assemblies.Assemblies.Count == 0)
        {
            CommandError.Write("No managed library was found to inspect.");
            return 1;
        }

        AssemblyContextEntry<LibraryDependencyStructureQueryResult>? entry = null;
        string? selectionError = null;
        var unavailable = new List<string>();
        await using var workspace = new AssemblySetInspectionWorkspace();
        workspace.RunGroup(
            assemblies,
            (group, _) =>
            {
                AssemblyContextParticipant? participant =
                    SelectParticipant(group, options.Package, out selectionError);
                if (participant is null)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                entry = AssemblyContextLibraryDependencyStructureQuery.ExecuteParticipant(
                    group,
                    participant,
                    cancellationToken);
            },
            (assemblyEntry, failure) => unavailable.Add($"{assemblyEntry.Path}: {failure}"));

        // A library that could not be opened is always visible, even when
        // another library was selected for inspection.
        foreach (string failure in unavailable)
            CommandError.WriteWarning($"Could not open {failure}");

        if (selectionError is not null)
        {
            CommandError.Write(selectionError);
            return 1;
        }

        switch (entry)
        {
            case AssemblyContextEntry<LibraryDependencyStructureQueryResult>.Available
                { Value: LibraryDependencyStructureQueryResult.Available available } found:
                return Write(found.Subject, available.Document, options, sections);

            case AssemblyContextEntry<LibraryDependencyStructureQueryResult>.Available
                { Value: LibraryDependencyStructureQueryResult.Unavailable unavailableResult }:
                CommandError.Write(
                    "Dependency structure is unavailable: " + unavailableResult.Outcome.Message);
                return 1;

            case AssemblyContextEntry<LibraryDependencyStructureQueryResult>.Available
                { Value: LibraryDependencyStructureQueryResult.NoMetadata }:
                CommandError.Write(
                    "Dependency structure is unavailable because the library contains no managed metadata.");
                return 1;

            case AssemblyContextEntry<LibraryDependencyStructureQueryResult>.Available
                { Value: LibraryDependencyStructureQueryResult.Failed failed }:
                CommandError.Write(failed.Error);
                return 1;

            case AssemblyContextEntry<LibraryDependencyStructureQueryResult>.Rejected rejected:
                CommandError.Write($"The library could not be opened: {rejected.Failure}");
                return 1;

            case AssemblyContextEntry<LibraryDependencyStructureQueryResult>.Failed failedEntry:
                CommandError.Write(failedEntry.Error);
                return 1;

            default:
                CommandError.Write("The library could not be inspected.", [.. unavailable]);
                return 1;
        }
    }

    static AssemblyContextParticipant? SelectParticipant(
        AssemblyContextGroup group,
        string? package,
        out string? error)
    {
        error = null;
        if (group.Participants.Length == 1)
            return group.Participants[0];

        string? packageId = package?.Split('@')[0];
        AssemblyContextParticipant[] namesake =
        [
            .. group.Participants.Where(participant =>
                packageId is not null
                && string.Equals(
                    participant.Assembly.Identity.Name,
                    packageId,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (namesake.Length == 1)
            return namesake[0];

        error = group.Participants.Length == 0
            ? "The selection contains no managed library."
            : "The package contains several libraries and none is its namesake: "
                + string.Join(
                    ", ",
                    group.Participants.Select(participant =>
                        LibraryViewText.Contain(participant.Assembly.Identity.Name)))
                + ". Pass one with --library.";
        return null;
    }

    static bool TryResolveSections(GraphStructureOptions options, out string[] sections)
    {
        if ((options.Envelope || options.Format == OutputFormat.Json)
            && options.Select is { Length: > 0 })
        {
            CommandError.Write(
                "-S cannot be combined with --json or --envelope; they emit the complete document.");
            sections = [];
            return false;
        }

        if (options.Select is not { Length: > 0 } select)
        {
            sections = GraphStructureViewSections.Default;
        }
        else
        {
            var resolved = new List<string>();
            foreach (string requested in select)
            {
                string? match = GraphStructureViewSections.All.FirstOrDefault(section =>
                    string.Equals(section, requested, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    CommandError.Write(
                        $"Unknown section '{requested}'. Sections: "
                            + string.Join(", ", GraphStructureViewSections.All) + ".");
                    sections = [];
                    return false;
                }
                if (!resolved.Contains(match))
                    resolved.Add(match);
            }
            sections = [.. resolved];
        }

        if (options.Format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl
            && sections.Length != 1)
        {
            CommandError.Write("Tabular output requires exactly one section; select it with -S.");
            return false;
        }
        return true;
    }

    static int Write(
        AssemblyContextSubject subject,
        LibraryDependencyStructureDocument document,
        GraphStructureOptions options,
        string[] sections)
    {
        if (options.Envelope || options.Format == OutputFormat.Json)
        {
            InspectionEnvelope<LibraryDependencyStructureDocument> envelope =
                LibraryDependencyStructureInspection.Execute(document);
            return InspectionEnvelopeOutput.TryWrite(
                envelope,
                s_json,
                options.Envelope,
                options.CompactJson,
                options.OutputPath)
                    ? 0
                    : 1;
        }

        GraphStructureView view = CreateView(subject, document, sections);
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize =
            (writer, formatter, writerOptions) => MarkoutSerializer.Serialize(
                view,
                writer,
                formatter,
                GraphStructureViewContext.Default,
                writerOptions);
        OutputDestination.Write(
            options.OutputPath,
            rowWindow: null,
            output =>
            {
                switch (options.Format)
                {
                    case OutputFormat.Table:
                    case OutputFormat.Tsv:
                    case OutputFormat.Jsonl:
                        OutputFormatter.WriteProjectedTable(
                            output,
                            showHeader: !options.NoHeader,
                            tsv: options.Format == OutputFormat.Tsv,
                            jsonl: options.Format == OutputFormat.Jsonl,
                            columns: null,
                            fields: null,
                            serialize,
                            maxRows: null);
                        break;
                    default:
                        MarkoutSerializer.Serialize(
                            view,
                            output,
                            options.Format == OutputFormat.PlainText
                                ? new PlainTextFormatter()
                                : new MarkdownFormatter(),
                            GraphStructureViewContext.Default,
                            OutputFormatter.CreateProjectedWriterOptions(null, null, null));
                        break;
                }
            });
        return 0;
    }

    internal static GraphStructureView CreateView(
        AssemblyContextSubject subject,
        LibraryDependencyStructureDocument document,
        IReadOnlyCollection<string> sections)
    {
        bool Include(string section) => sections.Contains(section);
        Dictionary<string, LibraryDependencyExternalNode> externals =
            document.ExternalNodes.ToDictionary(static node => node.Key, StringComparer.Ordinal);

        return new GraphStructureView
        {
            Description = Describe(subject, document),
            Namespaces = !Include(GraphStructureViewSections.Namespaces) ? null :
            [
                .. document.Namespaces
                    .OrderByDescending(static node => node.Level)
                    .ThenBy(static node => node.Namespace, StringComparer.Ordinal)
                    .Select(static node => new GraphStructureNamespaceRow
                    {
                        Level = node.Level,
                        Namespace = Label(node.Namespace),
                        Types = node.TypeCount,
                        InternalCalls = node.IntraNamespaceRelationshipCount,
                        Cycle = node.CycleIndex is int cycle ? $"{cycle + 1}" : "",
                    }),
            ],
            NamespaceEdges = !Include(GraphStructureViewSections.NamespaceEdges) ? null :
            [
                .. document.NamespaceEdges
                    .OrderByDescending(static edge => edge.Counts.Total)
                    .ThenBy(static edge => edge.SourceNamespace, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.TargetNamespace, StringComparer.Ordinal)
                    .Select(static edge => new GraphStructureNamespaceEdgeRow
                    {
                        Source = Label(edge.SourceNamespace),
                        Target = Label(edge.TargetNamespace),
                        Calls = edge.Counts.Invocations,
                        FunctionReferences = edge.Counts.FunctionReferences,
                        TypeEdges = edge.ContributingTypeEdgeCount,
                        StrongestTypeEdge = edge.ExplainingTypeEdges is [var top, ..]
                            ? $"{top.SourceTypeKey} → {top.TargetTypeKey} ({top.Counts.Total})"
                            : "",
                    }),
            ],
            Cycles = !Include(GraphStructureViewSections.Cycles) ? null :
            [
                .. document.Cycles.Select(static (cycle, index) => new GraphStructureCycleRow
                {
                    Cycle = index + 1,
                    Size = cycle.Namespaces.Length,
                    Namespaces = string.Join(", ", cycle.Namespaces.Select(Label)),
                }),
            ],
            ExternalDependencies = !Include(GraphStructureViewSections.ExternalDependencies) ? null :
            [
                .. document.ExternalNamespaceEdges
                    .OrderByDescending(static edge => edge.Counts.Total)
                    .ThenBy(static edge => edge.SourceNamespace, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.ExternalKey, StringComparer.Ordinal)
                    .Select(edge => new GraphStructureExternalEdgeRow
                    {
                        Source = Label(edge.SourceNamespace),
                        Assembly = externals[edge.ExternalKey] switch
                        {
                            { IsIntrinsicCoreLibrary: true } => "<intrinsic core library>",
                            { Assembly: { } assembly } => assembly.Name,
                            _ => "",
                        },
                        Namespace = Label(externals[edge.ExternalKey].Namespace),
                        Calls = edge.Counts.Invocations,
                        FunctionReferences = edge.Counts.FunctionReferences,
                        TypeEdges = edge.ContributingTypeEdgeCount,
                    }),
            ],
            TypeEdges = !Include(GraphStructureViewSections.TypeEdges) ? null :
            [
                .. document.TypeEdges
                    .OrderByDescending(static edge => edge.Counts.Total)
                    .ThenBy(static edge => edge.SourceTypeKey, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.TargetTypeKey, StringComparer.Ordinal)
                    .Select(static edge => new GraphStructureTypeEdgeRow
                    {
                        SourceType = edge.SourceTypeKey,
                        TargetType = edge.TargetTypeKey,
                        Calls = edge.Counts.Invocations,
                        FunctionReferences = edge.Counts.FunctionReferences,
                    }),
            ],
        };
    }

    static string Describe(
        AssemblyContextSubject subject,
        LibraryDependencyStructureDocument document)
    {
        LibraryDependencyPopulationReceipt population = document.Population;
        string library = subject.Identity.Version is { } version
            ? $"{subject.Identity.Name}@{version}"
            : subject.Identity.Name;
        string completeness = document.Completeness == LibraryDependencyCompleteness.Complete
            ? "Complete: absence of a cycle or call dependency is stated over all call evidence."
            : $"Qualified: {population.UnresolvedCallCount} unresolved calls and "
                + $"{population.IncompleteBodyCount} diagnosed bodies; absence holds only among admitted call evidence.";
        return $"{library}: {population.TypeCount} types in {population.NamespaceCount} namespaces, "
            + $"{document.Cycles.Length} namespace cycle(s), {population.ExaminedCallCount} calls "
            + $"({population.InternalCallCount} internal, {population.ExternalCallCount} external).\n"
            + completeness;
    }

    static string Label(string ns) => ns.Length == 0 ? GlobalNamespaceLabel : ns;
}
