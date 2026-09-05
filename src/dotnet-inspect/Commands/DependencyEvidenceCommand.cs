using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Commands;

/// <summary>
/// Presents one normalized Package Dependency Evidence snapshot for explicitly named package,
/// nuspec, restored-project, or package-prefix roots.
/// </summary>
/// <remarks>
/// This is not a dependency walk. <c>depends</c> stays authoritative for transitive traversal;
/// this command reports the direct dependencies these roots declare, under which normalized
/// scopes and constraints, plus whatever restored resolution evidence and completion accompany
/// them. See <c>docs/design/dependency-evidence-cli.md</c>.
/// </remarks>
public static class DependencyEvidenceCommand
{
    public const string Name = "dependency-evidence";

    /// <summary>
    /// The document's root-set and phase-completion fields render at every verbosity, including
    /// <c>-v:q</c> where no section is selected, so the evidence query is demanded by the host
    /// rather than by section selection alone.
    /// </summary>
    private static readonly HostQueryDemand[] DocumentFieldDemand =
    [
        new("Document completion fields", PackageDependencyEvidenceQuery.Definition),
    ];

    public static async Task<int> ExecuteAsync(
        DependencyEvidenceOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        DependencyEvidenceSectionCatalog catalog =
            DependencyEvidenceSections.CreateCatalog();
        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            catalog.Sections.SelectableSectionNames,
            catalog.Sections.InfoSectionNames,
            catalog.Sections.SelectionCategoryMap,
            options.SelectDefault);
        if (SelectOutput.WriteUnresolved(selection))
            return 1;

        if (options.Discover is { } discover)
        {
            return DiscoverOutput.Execute(
                discover,
                DependencyEvidenceSections.CreateSchema(),
                tree: options.Tree,
                json: options.JsonOutput,
                tsv: options.Tsv,
                jsonl: options.Jsonl,
                sectionCostAnnotations: catalog.Pipeline.GetCostAnnotations(),
                sectionCategories: catalog.Sections.SelectionCategoryMap,
                projection: options);
        }

        if (options.Schema)
        {
            CommandError.Write("--schema requires -D/--discover.");
            return 1;
        }

        if (options.Tree)
        {
            CommandError.Write("--tree requires -D/--discover.");
            return 1;
        }

        if (!Validate(options, selection.Sections))
            return 1;

        // The candidate section set is structural, so it is known before any root is acquired.
        // Deriving it here lets a request that cannot produce a single-schema row stream fail
        // before it downloads a manifest or reads an assets document.
        HashSet<string> includeSections =
            catalog.Pipeline.GetCandidateSections(
                options.Verbosity,
                selection.Sections,
                fixedOverview: options.SelectDefault);
        if (!ValidateTabularArity(options, includeSections))
            return 1;

        var context = new CommandContext(options.Verbose);
        try
        {
            PackageDependencyEvidenceRequest request;
            PackageProfileSummary? profileSummary = null;
            if (options.PackagePrefix is { } prefix)
            {
                (request, profileSummary) = await AcquirePrefixAsync(
                    options,
                    prefix,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                request = await DependencyEvidenceAcquisition
                    .AcquireExplicitRootsAsync(
                        options,
                        context.HttpClient,
                        context.Logger.Log,
                        cancellationToken).ConfigureAwait(false);
            }

            CompiledInspectionPlan<DependencyEvidenceQueryContext> plan =
                catalog.Lens.Plan(
                    options.Verbosity,
                    selection.Sections,
                    fixedOverview: options.SelectDefault,
                    hostDemand: DocumentFieldDemand);
            InspectionQueryResults results = await plan.RunAsync(
                new DependencyEvidenceQueryContext(request),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            PackageDependencyEvidenceOutcome outcome =
                results.Get(PackageDependencyEvidenceQuery.Definition);
            DependencyEvidenceProjection projection =
                DependencyEvidenceProjection.Create(outcome);

            if (!Write(projection, options, includeSections))
                return 1;

            WriteDiagnostics(projection, profileSummary, selection.Sections);
            return ExitCode(projection);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is the caller's decision, not an inspection failure. Reporting it as
            // one would turn an aborted request into a diagnosed error with an exit status.
            throw;
        }
        catch (Exception exception)
        {
            CommandError.Write(exception);
            return 1;
        }
    }

    /// <summary>
    /// Enforces the one-table rule for <c>--table</c>, <c>--tsv</c>, and <c>--jsonl</c>.
    /// </summary>
    /// <remarks>
    /// A parsed row stream carries exactly one row schema (see
    /// <c>docs/design/output-shapes.md</c>), so zero or several selected tables reject rather
    /// than emitting a stream whose rows change shape partway through. <c>--count</c> is exempt:
    /// its multi-section form is the ordered section/count table.
    /// </remarks>
    internal static bool ValidateTabularArity(
        DependencyEvidenceOptions options,
        IReadOnlyCollection<string> candidateSections)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(candidateSections);
        if (!options.Tabular || options.Count)
            return true;

        string format = options.Jsonl ? "--jsonl" : options.Tsv ? "--tsv" : "--table";
        if (candidateSections.Count == 1)
            return true;

        CommandError.Write(
            candidateSections.Count == 0
                ? $"{format} requires exactly one selected table section; this view selects none. Use -S {DependencyEvidenceSections.Dependencies} or another section."
                : $"{format} requires exactly one selected table section; this view selects {candidateSections.Count}: {string.Join(", ", DependencyEvidenceSections.SectionOrder.Where(candidateSections.Contains))}.");
        return false;
    }

    /// <summary>
    /// Validates the input family, exclusivity, source authorization, and count exactness rules
    /// this command owns.
    /// </summary>
    internal static bool Validate(
        DependencyEvidenceOptions options,
        HashSet<string>? selectedSections)
    {
        // The prefix option is an explicit gesture as soon as it is present. Treating an empty
        // value as absence would silently accept '--package-prefix ""' alone as "no root at
        // all", and silently ignore it when combined with an explicit root, instead of
        // reporting the malformed prefix the caller actually named.
        bool hasPrefix = options.PackagePrefix is not null;
        if (!options.HasExplicitRoots && !hasPrefix)
        {
            CommandError.Write(
                $"{Name} requires at least one --package, --nuspec, --project, or --package-prefix root.");
            return false;
        }

        if (hasPrefix && options.HasExplicitRoots)
        {
            CommandError.Write(
                "--package-prefix cannot be combined with --package, --nuspec, or --project; its root-set accounting owns the whole request.");
            return false;
        }

        bool hasSourceOverrides = options.SourceOptions is { } sourceOptions
            && (sourceOptions.Sources.Length > 0
                || sourceOptions.AdditionalSources.Length > 0
                || sourceOptions.ConfigFile is not null);
        if (hasPrefix)
        {
            // The prefix's own shape is checked before the gestures that only make sense
            // alongside a usable prefix, so a malformed prefix is reported as such.
            if (!PackageProfileQuery.IsValidPrefix(options.PackagePrefix))
            {
                CommandError.Write(
                    "--package-prefix must be 1 to 100 characters without surrounding whitespace or control characters.");
                return false;
            }

            if (options.IncludePrerelease)
            {
                CommandError.Write(
                    "--preview applies only to latest remote --package resolution; --package-prefix admits the versions its profile producer returns.");
                return false;
            }

            if (hasSourceOverrides)
            {
                CommandError.Write(
                    "--package-prefix currently uses the NuGet Gallery source and cannot be combined with source overrides.");
                return false;
            }

            int maximumPackages = options.MaxPackages
                ?? DependencyEvidenceAcquisition.PackageProfileDefaultLimit;
            if (maximumPackages is <= 0
                or > DependencyEvidenceAcquisition.PackageProfileMaximumLimit)
            {
                CommandError.Write(
                    $"--max-packages must be between 1 and {DependencyEvidenceAcquisition.PackageProfileMaximumLimit} (got {maximumPackages}).");
                return false;
            }
        }
        else
        {
            if (options.MaxPackages is not null)
            {
                CommandError.Write(
                    "--max-packages bounds --package-prefix discovery and cannot be used without it.");
                return false;
            }

            PackageRootTargets targets = ClassifyPackageTargets(options.Packages);
            if (hasSourceOverrides && !targets.HasRemote)
            {
                CommandError.Write(
                    "--source, --add-source, and --nugetconfig apply only to a remote --package target.");
                return false;
            }

            if (options.IncludePrerelease && !targets.HasLatestRemote)
            {
                CommandError.Write(
                    targets.HasRemote
                        ? "--preview applies only to latest remote --package resolution; every remote --package target already names an exact version."
                        : "--preview applies only to latest remote --package resolution.");
                return false;
            }
        }

        return !options.Count
            || ValidateCount(options, selectedSections, hasPrefix);
    }

    /// <summary>
    /// Splits the named package roots into the acquisition forms whose options differ.
    /// </summary>
    /// <remarks>
    /// Uses the shared package-target grammar rather than a second local parser, so the CLI's
    /// admissibility rules and its acquisition agree on what <c>ID</c>, <c>ID@VERSION</c>, and a
    /// local <c>.nupkg</c> mean.
    /// </remarks>
    private static PackageRootTargets ClassifyPackageTargets(
        IReadOnlyList<string> packages)
    {
        bool hasRemote = false;
        bool hasLatestRemote = false;
        foreach (string package in packages)
        {
            if (DependencyEvidenceAcquisition.IsLocalArchiveTarget(package))
                continue;

            hasRemote = true;
            (_, string? version) = DotnetInspector.Packages.PackageExtractor
                .ParsePackageReference(package);
            if (string.IsNullOrWhiteSpace(version))
                hasLatestRemote = true;
        }

        return new PackageRootTargets(hasRemote, hasLatestRemote);
    }

    private readonly record struct PackageRootTargets(
        bool HasRemote,
        bool HasLatestRemote);

    /// <summary>
    /// Rejects, before acquisition, a package-prefix <c>--count</c> whose selected row sets are
    /// not structurally exact.
    /// </summary>
    /// <remarks>
    /// A bounded profile is not an exhaustive package set, so only the owner-returned
    /// <c>Failures</c> record collection is exact for prefix input. That makes exactly one
    /// selected <c>Failures</c> section the only admissible prefix count: the default, any
    /// other single section, and every multi-section set — including one that merely contains
    /// <c>Failures</c> — would report a count for at least one inexact row set. Every other
    /// input family decides exactness after acquisition, from typed completion.
    /// </remarks>
    private static bool ValidateCount(
        DependencyEvidenceOptions options,
        HashSet<string>? selectedSections,
        bool hasPrefix)
    {
        if (!hasPrefix)
            return true;

        string[] ordered = selectedSections is { Count: > 0 }
            ? [.. DependencyEvidenceSections.SectionOrder
                .Where(selectedSections.Contains)]
            : [DependencyEvidenceSections.Dependencies];
        string[] selected = ordered.Length > 0
            ? ordered
            : [.. selectedSections!];
        if (selected is [string only]
            && only.Equals(
                DependencyEvidenceSections.Failures,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        CommandError.Write(
            $"--count cannot report an exact '{string.Join(", ", selected)}' count for --package-prefix input: a bounded profile is not an exhaustive package set. Only '-S {DependencyEvidenceSections.Failures} --count' on its own is exact, for the returned failure records.");
        return false;
    }

    private static async Task<(
        PackageDependencyEvidenceRequest Request,
        PackageProfileSummary Summary)> AcquirePrefixAsync(
            DependencyEvidenceOptions options,
            string prefix,
            CommandContext context,
            CancellationToken cancellationToken)
    {
        NuGetFetchOptions fetchOptions =
            NuGetFetchOptions.FromRequestTimeout(context.HttpClient.Timeout);
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
        return await DependencyEvidenceAcquisition.AcquirePackagePrefixAsync(
            source,
            new PackagePrefixProfileRequest(
                prefix,
                options.MaxPackages
                    ?? DependencyEvidenceAcquisition.PackageProfileDefaultLimit),
            options.Tfm,
            operationContext,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders the projection through the selected sink. Returns false when a projection or
    /// count request could not be honored.
    /// </summary>
    internal static bool Write(
        DependencyEvidenceProjection projection,
        DependencyEvidenceOptions options,
        HashSet<string> includeSections)
    {
        DocumentSchema schema = options.Tabular && !options.Count
            ? DependencyEvidenceSections.CreateTableSchema()
            : DependencyEvidenceSections.CreateSchema();

        if (options.Count)
            return WriteCount(projection, options, includeSections, schema);

        if (options.JsonOutput && !IsColumnProjectionRequested(options))
        {
            DependencyEvidenceDocument document =
                DependencyEvidenceDocument.Create(
                    projection,
                    includeSections,
                    options.Rows);
            Console.WriteLine(
                JsonSerializer.Serialize(
                    document,
                    options.CompactJson
                        ? DependencyEvidenceCompactJsonContext.Default
                            .DependencyEvidenceDocument
                        : DependencyEvidenceJsonContext.Default
                            .DependencyEvidenceDocument));
            return true;
        }

        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                includeSections,
                options.Fields,
                options.Columns))
        {
            return false;
        }

        // Every document-shaped sink receives already-windowed section arrays and no Markout
        // window: this document's summary fields render as a field table, which a writer-level
        // window would truncate along with the rows.
        DependencyEvidenceView view =
            BuildView(projection, includeSections, options.Rows);
        if (options.JsonOutput)
        {
            // Reached only under a projection; plain --json returned the typed document above.
            // The summary is written directly rather than through the document view: Markout
            // renders a FieldLayout.Table document's fields as a two-column table, which the
            // caller's --columns would filter away and which lowers to an anonymous JSON key.
            // Naming it keeps the summary at a stable 'summary' key whose members are the same
            // labels Markdown shows.
            DependencyEvidenceTableView jsonTables =
                BuildTableView(projection, includeSections, options.Rows);
            MarkoutField[] summary = SummaryFields(projection, options.Rows);
            OutputFormatter.WriteProjectedJson(
                Console.Out,
                options.Columns,
                options.Fields,
                (writer, formatter, writerOptions) =>
                {
                    WriteSummarySection(writer, formatter, writerOptions, summary);
                    writerOptions.IncludeSections = includeSections;
                    MarkoutSerializer.Serialize(
                        jsonTables,
                        writer,
                        formatter,
                        DependencyEvidenceViewContext.Default,
                        writerOptions);
                },
                !options.CompactJson);
            return true;
        }

        if (options.Tabular)
        {
            // One selected table, one row schema: the table-only wrapper renders the same row
            // views without the document summary fields, which are a differently shaped record.
            DependencyEvidenceTableView tables =
                BuildTableView(projection, includeSections, options.Rows);
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
                        tables,
                        writer,
                        formatter,
                        DependencyEvidenceViewContext.Default,
                        writerOptions);
                });
            return true;
        }

        if (IsColumnProjectionRequested(options))
        {
            // A --columns/--fields projection selects within the section row sets. The summary
            // fields are not rows, and Markout's field table is a table, so one projected pass
            // over the whole document would let a column projection delete the completion
            // fields that keep a partial outcome visible. The summary is therefore rendered
            // unprojected and the row sets are rendered projected.
            string summaryDocument = MarkoutSerializer.Serialize(
                BuildView(projection, NoSections, options.Rows),
                DependencyEvidenceViewContext.Default,
                new MarkoutWriterOptions());
            MarkoutWriterOptions sectionOptions =
                OutputFormatter.CreateWindowedOptions(
                    rows: null,
                    options.Columns,
                    options.Fields);
            sectionOptions.IncludeSections = includeSections;
            string sectionDocument = MarkoutSerializer.Serialize(
                BuildTableView(projection, includeSections, options.Rows),
                DependencyEvidenceViewContext.Default,
                sectionOptions);
            Console.Out.WriteLine(
                Join(summaryDocument, sectionDocument));
            return true;
        }

        OutputFormatter.WriteWindowedMarkdown(
            Console.Out,
            rows: null,
            writerOptions =>
            {
                writerOptions.IncludeSections = includeSections;
                return MarkoutSerializer.Serialize(
                    view,
                    DependencyEvidenceViewContext.Default,
                    writerOptions);
            },
            options.Columns,
            options.Fields);
        return true;
    }

    /// <summary>The section-free selection used to render the document summary on its own.</summary>
    private static readonly HashSet<string> NoSections =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The JSON key the lowered view gives the document summary.</summary>
    internal const string SummarySection = "Summary";

    /// <summary>
    /// The document's summary fields, read back from the same generated serializer that renders
    /// them as Markdown so the two sinks cannot declare different labels.
    /// </summary>
    private static MarkoutField[] SummaryFields(
        DependencyEvidenceProjection projection,
        RowWindow? rows) =>
        MarkoutFieldRecorder.Record(
            BuildView(projection, NoSections, rows),
            DependencyEvidenceViewContext.Default);

    /// <summary>
    /// Writes the document summary as one named, unprojected field group ahead of the row
    /// sections.
    /// </summary>
    private static void WriteSummarySection(
        TextWriter writer,
        Markout.Formatting.IMarkoutFormatter formatter,
        MarkoutWriterOptions writerOptions,
        MarkoutField[] summary)
    {
        if (summary.Length == 0)
            return;

        MarkoutWriter summaryWriter = MarkoutWriter.Create(
            writer,
            formatter,
            new MarkoutWriterOptions
            {
                HeadingLevelOffset = writerOptions.HeadingLevelOffset,
            });
        summaryWriter.WriteSectionStart(2, SummarySection);
        summaryWriter.WriteFields(summary);
        summaryWriter.WriteSectionEnd();
        summaryWriter.Flush();
    }

    /// <summary>Joins two rendered Markdown fragments with exactly one blank line.</summary>
    private static string Join(string summary, string sections)
    {
        string head = summary.TrimEnd();
        string tail = sections.TrimEnd();
        return tail.Length == 0
            ? head
            : head.Length == 0
                ? tail
                : head + Environment.NewLine + Environment.NewLine + tail;
    }

    private static bool WriteCount(
        DependencyEvidenceProjection projection,
        DependencyEvidenceOptions options,
        HashSet<string> includeSections,
        DocumentSchema schema)
    {
        string[] ordered =
        [
            .. DependencyEvidenceSections.SectionOrder
                .Where(includeSections.Contains),
        ];
        if (ordered.Length == 0)
            ordered = [DependencyEvidenceSections.Dependencies];

        if (!ProjectionDiagnostics.ValidateProjection(
                schema,
                ordered,
                options.Fields,
                options.Columns))
        {
            return false;
        }

        foreach (string section in ordered)
        {
            if (IsExactRowSet(projection, section))
                continue;

            CommandError.Write(
                $"--count cannot report an exact '{section}' count: the acquired evidence is incomplete. Use '-S {DependencyEvidenceSections.Failures}' to see why.");
            return false;
        }

        var counts = new CountProjection();
        foreach (string section in ordered)
        {
            int rows = DependencyEvidenceSections.CountRows(projection, section);
            counts.SetRows(
                section,
                options.Rows is { IsUnlimited: false } window
                    ? Window(window, rows)
                    : rows);
        }

        CountOutput.Write(
            counts,
            ordered.Length > 1 ? ordered : null,
            options.JsonOutput ? OutputFormat.Json
                : options.Jsonl ? OutputFormat.Jsonl
                : options.Tsv ? OutputFormat.Tsv
                : options.Tabular ? OutputFormat.Table
                : OutputFormat.Markdown,
            options.NoHeader);
        return true;
    }

    private static int Window(RowWindow window, int rows)
    {
        (int keepStart, int keepEnd) = window.Resolve(rows);
        return keepEnd - keepStart;
    }

    /// <summary>
    /// The design's per-row-set exactness rule. A selected row set that is not exact rejects
    /// rather than returning a plausible scalar.
    /// </summary>
    internal static bool IsExactRowSet(
        DependencyEvidenceProjection projection,
        string section)
    {
        DependencyEvidenceSummary summary = projection.Summary;
        if (section.Equals(
                DependencyEvidenceSections.Failures,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool exactRootSet =
            summary.RootSetCompletion
                == PackageDependencyEvidenceRootSetCompletion.Complete
            && summary.PackagePrefix is null;
        if (!exactRootSet)
            return false;

        return section switch
        {
            DependencyEvidenceSections.Roots => true,
            DependencyEvidenceSections.Dependencies
                or DependencyEvidenceSections.DependencyGroups =>
                summary.Phases.IncompleteDeclarations == 0
                && summary.Phases.FailedDeclarations == 0
                && summary.Phases.UnavailableDeclarations == 0,
            DependencyEvidenceSections.RestoredEdges
                or DependencyEvidenceSections.RestoredPackages =>
                summary.Phases.IncompleteGraphs == 0
                && summary.Phases.FailedGraphs == 0
                && summary.Phases.UnavailableGraphs == 0,
            _ => false,
        };
    }

    /// <summary>
    /// Builds the Markout document for one already-selected section set, windowing each selected
    /// row set with <paramref name="rows"/>.
    /// </summary>
    /// <remarks>
    /// The window is applied to the section arrays here rather than handed to Markout, because
    /// this document renders its root-set and completion fields as a field table. A writer-level
    /// window cannot tell that table apart from a section's rows, so <c>--rows 1</c> would drop
    /// the completion fields that exist precisely to keep a partial outcome visible.
    /// </remarks>
    internal static DependencyEvidenceView BuildView(
        DependencyEvidenceProjection projection,
        IReadOnlySet<string> includeSections,
        RowWindow? rows = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(includeSections);

        DependencyEvidenceSummary summary = projection.Summary;
        return new DependencyEvidenceView
        {
            Description = projection.Dependencies.IsEmpty
                ? "No normalized direct dependencies."
                : null,
            RootCount = summary.AdmittedRootCount,
            RootSet = summary.RootSetCompletion.ToString(),
            RejectedRootCount = summary.RejectedRootCount,
            FailedRootCount = summary.FailedRootCount,
            Truncated = summary.IsTruncated,
            CompleteDeclarations = summary.Phases.CompleteDeclarations,
            IncompleteDeclarations = summary.Phases.IncompleteDeclarations,
            UnavailableDeclarations = summary.Phases.UnavailableDeclarations,
            FailedDeclarations = summary.Phases.FailedDeclarations,
            NotApplicableGraphs = summary.Phases.NotApplicableGraphs,
            CompleteGraphs = summary.Phases.CompleteGraphs,
            IncompleteGraphs = summary.Phases.IncompleteGraphs,
            UnavailableGraphs = summary.Phases.UnavailableGraphs,
            FailedGraphs = summary.Phases.FailedGraphs,
            PrefixText = summary.PackagePrefix?.Prefix,
            PrefixSourceText = summary.PackagePrefix?.Source.Producer.Display,
            PrefixCandidates = summary.PackagePrefix?.Candidates,
            PrefixMatches = summary.PackagePrefix?.Matches,
            PrefixFailures = summary.PackagePrefix?.Failures,
            TruncationReason =
                summary.PackagePrefix?.TruncationReason is { } reason
                && reason != PackageSearchTruncationReason.None
                    ? reason.ToString()
                    : null,
            Dependencies = Rows(
                includeSections,
                DependencyEvidenceSections.Dependencies,
                projection.Dependencies,
                rows,
                DependencyEvidenceDependencyView.From),
            Roots = Rows(
                includeSections,
                DependencyEvidenceSections.Roots,
                projection.Roots,
                rows,
                DependencyEvidenceRootView.From),
            RestoredEdges = Rows(
                includeSections,
                DependencyEvidenceSections.RestoredEdges,
                projection.RestoredEdges,
                rows,
                DependencyEvidenceRestoredEdgeView.From),
            Failures = Rows(
                includeSections,
                DependencyEvidenceSections.Failures,
                projection.Failures,
                rows,
                DependencyEvidenceFailureView.From),
            DependencyGroups = Rows(
                includeSections,
                DependencyEvidenceSections.DependencyGroups,
                projection.DependencyGroups,
                rows,
                DependencyEvidenceGroupView.From),
            RestoredPackages = Rows(
                includeSections,
                DependencyEvidenceSections.RestoredPackages,
                projection.RestoredPackages,
                rows,
                DependencyEvidenceRestoredPackageView.From),
        };
    }

    /// <summary>
    /// Builds the table-only Markout document for one already-selected section set, windowed by
    /// the same per-row-set rule the document view uses.
    /// </summary>
    internal static DependencyEvidenceTableView BuildTableView(
        DependencyEvidenceProjection projection,
        IReadOnlySet<string> includeSections,
        RowWindow? rows = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(includeSections);

        return new DependencyEvidenceTableView
        {
            Dependencies = Rows(
                includeSections,
                DependencyEvidenceSections.Dependencies,
                projection.Dependencies,
                rows,
                DependencyEvidenceDependencyView.From),
            Roots = Rows(
                includeSections,
                DependencyEvidenceSections.Roots,
                projection.Roots,
                rows,
                DependencyEvidenceRootView.From),
            RestoredEdges = Rows(
                includeSections,
                DependencyEvidenceSections.RestoredEdges,
                projection.RestoredEdges,
                rows,
                DependencyEvidenceRestoredEdgeView.From),
            Failures = Rows(
                includeSections,
                DependencyEvidenceSections.Failures,
                projection.Failures,
                rows,
                DependencyEvidenceFailureView.From),
            DependencyGroups = Rows(
                includeSections,
                DependencyEvidenceSections.DependencyGroups,
                projection.DependencyGroups,
                rows,
                DependencyEvidenceGroupView.From),
            RestoredPackages = Rows(
                includeSections,
                DependencyEvidenceSections.RestoredPackages,
                projection.RestoredPackages,
                rows,
                DependencyEvidenceRestoredPackageView.From),
        };
    }

    /// <summary>
    /// The exit status this command's failure contract requires.
    /// </summary>
    /// <remarks>
    /// Unavailable optional evidence stays visible without turning a usable snapshot into an
    /// execution failure, and requested-limit prefix truncation succeeds when nothing else
    /// failed.
    /// </remarks>
    internal static int ExitCode(DependencyEvidenceProjection projection)
    {
        DependencyEvidenceSummary summary = projection.Summary;
        bool truncatedBeyondRequest =
            summary.PackagePrefix is { } prefix
                ? prefix.TruncationReason
                    is not PackageSearchTruncationReason.None
                        and not PackageSearchTruncationReason.RequestedLimit
                : summary.IsTruncated;
        return summary.FailedRootCount > 0
            || summary.RejectedRootCount > 0
            || truncatedBeyondRequest
            || summary.Phases.IncompleteDeclarations > 0
            || summary.Phases.FailedDeclarations > 0
            || summary.Phases.IncompleteGraphs > 0
            || summary.Phases.FailedGraphs > 0
                ? 1
                : 0;
    }

    private static void WriteDiagnostics(
        DependencyEvidenceProjection projection,
        PackageProfileSummary? profileSummary,
        HashSet<string>? explicitSections)
    {
        DependencyEvidenceSummary summary = projection.Summary;
        int failureRecords = projection.Failures.Length;
        if (failureRecords > 0)
        {
            CommandError.WriteWarning(
                $"{failureRecords} typed failure record(s) are reported; run with '-S {DependencyEvidenceSections.Failures}' for the rows.");
        }

        if (summary.Phases.UnavailableDeclarations > 0
            || summary.Phases.UnavailableGraphs > 0)
        {
            CommandError.WriteWarning(
                "Some optional declaration or restored-graph evidence is unavailable for the supplied roots.");
        }

        foreach (string section in explicitSections ?? [])
        {
            if (DependencyEvidenceSections.CountRows(projection, section) != 0)
                continue;

            CommandError.WriteNote(
                $"'{section}' has no rows for this evidence: {DescribeEmptySection(projection, section)}");
        }

        if (profileSummary is { Truncated: true })
        {
            CommandError.WriteWarning(
                profileSummary.TruncationReason
                    == PackageSearchTruncationReason.RequestedLimit
                        ? "Package discovery reached the requested package limit; this profile is bounded evidence, not an exhaustive package set."
                        : "Package discovery was truncated by a pagination limit; narrow the prefix.");
        }
    }

    /// <summary>
    /// Explains an empty selected section from typed phase state rather than inventing a
    /// synthetic row that would read as evidence.
    /// </summary>
    private static string DescribeEmptySection(
        DependencyEvidenceProjection projection,
        string section)
    {
        PackageDependencyEvidencePhaseSummary phases = projection.Summary.Phases;
        return section switch
        {
            DependencyEvidenceSections.RestoredEdges
                or DependencyEvidenceSections.RestoredPackages =>
                phases.CompleteGraphs + phases.IncompleteGraphs == 0
                    ? "no admitted root carries restored graph evidence."
                    : "the restored graph projected no nodes or edges.",
            DependencyEvidenceSections.Dependencies
                or DependencyEvidenceSections.DependencyGroups =>
                phases.CompleteDeclarations + phases.IncompleteDeclarations == 0
                    ? "no admitted root carries a declaration projection."
                    : "the admitted roots declare no direct dependencies.",
            DependencyEvidenceSections.Failures =>
                "no typed failure was recorded.",
            _ => "no root was admitted.",
        };
    }

    /// <summary>
    /// Projects one selected row set, windowed independently of every other row set and of the
    /// document's summary fields.
    /// </summary>
    private static List<TView>? Rows<TRow, TView>(
        IReadOnlySet<string> includeSections,
        string section,
        ImmutableArray<TRow> rows,
        RowWindow? window,
        Func<TRow, TView> select)
    {
        if (!includeSections.Contains(section) || rows.IsEmpty)
            return null;

        IReadOnlyList<TRow> selected = window is { IsUnlimited: false } bounded
            ? bounded.Apply(rows)
            : rows;
        return [.. selected.Select(select)];
    }

    private static bool IsColumnProjectionRequested(
        DependencyEvidenceOptions options) =>
        options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };
}
