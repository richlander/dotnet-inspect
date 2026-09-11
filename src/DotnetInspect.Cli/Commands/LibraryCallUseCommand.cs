using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Analysis;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

public static class LibraryCallUseCommand
{
    public const string Name = "libraries";

    internal const string ConsumerUseSitesSection =
        "Consumer Use Sites";
    internal const string ProviderApiTypesSection =
        "Provider API Types";
    internal const string CallSitesSection =
        "Call Sites";

    static readonly string[] SectionOrder =
    [
        ConsumerUseSitesSection,
        ProviderApiTypesSection,
        CallSitesSection,
    ];

    static readonly IReadOnlyDictionary<string, string[]> NoCategories =
        new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase);

    static readonly string[] CallSiteLabels =
    [
        "Source Library",
        "Source MVID",
        "Source Member",
        "Source Token",
        "Target Library",
        "Target MVID",
        "Target Member",
        "Target Token",
        "Call",
        "Evidence Method",
        "Evidence MVID",
        "Evidence Token",
        "IL Offset",
        "Operand Token",
        "Exact Target",
    ];

    static readonly string[] CallSiteIds =
    [
        "source-library",
        "source-mvid",
        "source-member",
        "source-token",
        "target-library",
        "target-mvid",
        "target-member",
        "target-token",
        "call",
        "evidence-method",
        "evidence-mvid",
        "evidence-token",
        "il-offset",
        "operand-token",
        "exact-target",
    ];

    static readonly string[] ConsumerUseSiteLabels =
    [
        "Source Library",
        "Source MVID",
        "Source Member",
        "Source Token",
        "Target Library",
        "Target MVID",
        "Provider Types",
        "Target Members",
        "Call Sites",
        "Call Site Rows",
    ];

    static readonly string[] ConsumerUseSiteIds =
    [
        "source-library",
        "source-mvid",
        "source-member",
        "source-token",
        "target-library",
        "target-mvid",
        "provider-types",
        "target-members",
        "call-sites",
        "call-site-rows",
    ];

    static readonly string[] ProviderApiTypeLabels =
    [
        "Source Library",
        "Source MVID",
        "Target Library",
        "Target MVID",
        "Target Type",
        "Source Members",
        "Target Members",
        "Call Sites",
        "Call Site Rows",
    ];

    static readonly string[] ProviderApiTypeIds =
    [
        "source-library",
        "source-mvid",
        "target-library",
        "target-mvid",
        "target-type",
        "source-members",
        "target-members",
        "call-sites",
        "call-site-rows",
    ];

    static readonly string[] DefaultCallSiteColumns =
    [
        "Source Member",
        "Target Member",
        "Call",
        "Evidence Method",
        "IL Offset",
    ];

    static readonly string[] DefaultSelectedColumns =
    [
        "Source Library",
        "Source Member",
        "Target Library",
        "Target Member",
        "Target Type",
        "Provider Types",
        "Source Members",
        "Target Members",
        "Call Sites",
        "Call",
        "Evidence Method",
        "IL Offset",
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

        if (options.Discover is { } discover)
        {
            return DiscoverOutput.Execute(
                discover,
                schema,
                projection: options,
                tree: options.Tree,
                json: options.Format == OutputFormat.Json,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                markdown: options.Format == OutputFormat.Markdown,
                plainText: options.Format == OutputFormat.PlainText,
                rootLabel: "Library Call Use");
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
        var unavailable = new List<string>();
        using var workspace = new AssemblySetInspectionWorkspace();
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
                }
                catch (AssemblyPairCallUseRequestException exception)
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

        AssemblyPairCallUseProjection projection =
            AssemblyPairCallUseProjection.Create(result);
        Write(
            result,
            projection,
            options,
            selectedNames,
            defaultCallSiteView);
        if (!result.IsComplete)
        {
            CommandError.Write(
                "Pairwise call-use evidence is incomplete.",
                [.. FailureDetails(result)]);
            return 1;
        }

        return 0;
    }

    static DocumentSchema CreateSchema()
    {
        var schema = new DocumentSchema();
        schema.Add(
            ConsumerUseSitesSection,
            "column",
            ConsumerUseSiteLabels);
        schema.Add(
            ProviderApiTypesSection,
            "column",
            ProviderApiTypeLabels);
        schema.Add(
            CallSitesSection,
            "column",
            CallSiteLabels);
        return schema;
    }

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
            selectedNames =
            [
                ConsumerUseSitesSection,
                ProviderApiTypesSection,
            ];
            return true;
        }

        SelectResult selection = SelectResolver.ResolveSelectAsSections(
            options.Select,
            SectionOrder,
            infoSections: [CallSitesSection],
            NoCategories,
            selectDefault: false);
        if (SelectOutput.WriteUnresolved(selection))
        {
            selectedNames = [];
            return false;
        }

        selectedNames =
        [
            .. SectionOrder.Where(
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
            OutputFormat.Tsv => "--tsv",
            OutputFormat.Jsonl => "--jsonl",
            _ => "--table",
        };
        CommandError.Write(
            $"{format} requires exactly one selected table section; "
            + $"this view selects {selectedNames.Count}: "
            + $"{string.Join(", ", selectedNames)}.");
        CommandError.WriteLine(
            "Use -S with one section name, or --markdown/--json for multi-section output.");
        return false;
    }

    static void Write(
        AssemblyPairCallUseResult result,
        AssemblyPairCallUseProjection projection,
        LibraryCallUseOptions options,
        string[] selectedNames,
        bool defaultCallSiteView)
    {
        if (defaultCallSiteView)
        {
            WriteDefaultCallSites(result, options);
            return;
        }

        WriteSelected(
            result,
            projection,
            options,
            selectedNames);
    }

    static void WriteDefaultCallSites(
        AssemblyPairCallUseResult result,
        LibraryCallUseOptions options)
    {
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences =
            RowWindow.Apply(
                options.Rows,
                result.Occurrences);
        if (options.Count)
        {
            CountOutput.WriteCount(occurrences.Count);
            return;
        }

        string[]? columns = options.Columns;
        string[]? fields = options.Fields;
        if (columns is null
            && fields is null
            && options.Format is
                OutputFormat.Markdown
                or OutputFormat.PlainText)
        {
            columns = DefaultCallSiteColumns;
        }

        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions>
            serialize = (writer, formatter, writerOptions) =>
            {
                var markout = new MarkoutWriter(
                    writer,
                    formatter,
                    writerOptions);
                WriteCallSiteTable(markout, occurrences);
                markout.Flush();
            };
        switch (options.Format)
        {
            case OutputFormat.Json:
                OutputFormatter.WriteProjectedJson(
                    Console.Out,
                    columns,
                    fields,
                    serialize,
                    maxRows: null);
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
                    maxRows: null);
                break;
            default:
                var writerOptions =
                    OutputFormatter.CreateProjectedWriterOptions(
                        columns,
                        fields,
                        rows: null);
                var markout = new MarkoutWriter(
                    Console.Out,
                    options.Format == OutputFormat.PlainText
                        ? new PlainTextFormatter()
                        : new MarkdownFormatter(),
                    writerOptions);
                markout.WriteHeading(1, "Library Call Use");
                markout.WriteParagraph(
                    $"{FormatAssembly(result.Subjects[0])} "
                    + "\u2194 "
                    + FormatAssembly(
                        result.Subjects[1]));
                string[] summaries =
                    [.. RelationshipSummaries(occurrences)];
                if (summaries.Length == 0)
                {
                    markout.WriteParagraph(
                        result.Occurrences.Length > 0
                            && options.Rows is not null
                            ? "No direct pair call use is selected by the row window."
                            : result.IsComplete
                            ? "No direct pair call use was observed."
                            : "No exact pair call use was observed; the evidence is incomplete.");
                }
                else
                    foreach (string summary in summaries)
                        markout.WriteParagraph(summary);
                WriteCallSiteTable(markout, occurrences);
                markout.Flush();
                break;
        }
    }

    static void WriteSelected(
        AssemblyPairCallUseResult result,
        AssemblyPairCallUseProjection projection,
        LibraryCallUseOptions options,
        IReadOnlyCollection<string> selectedNames)
    {
        LibraryCallUseSection[] sections =
            CreateSections(projection);
        LibraryCallUseSection[] selected =
        [
            .. sections.Where(
                section => selectedNames.Contains(
                    section.Name,
                    StringComparer.OrdinalIgnoreCase)),
        ];
        DocumentSchema schema = CreateSchema();
        string[]? projectedColumns =
            ResolveProjectedColumns(options);
        HashSet<string> renderedNames =
            projectedColumns is { Length: > 0 }
                ? selected
                    .Where(section =>
                        schema.ValidateProjection(
                            section.Name,
                            projectedColumns)
                            .Resolved.Length > 0)
                    .Select(section => section.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : selectedNames.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
        LibraryCallUseSection[] rendered =
        [
            .. selected.Where(
                section => renderedNames.Contains(section.Name)),
        ];
        LibraryCallUseSection[] windowed =
            ApplyRowWindow(rendered, options.Rows);

        if (options.Count)
        {
            if (selected.Length == 1)
            {
                CountOutput.WriteCount(
                    renderedNames.Contains(selected[0].Name)
                        ? RowWindow.Apply(
                            options.Rows,
                            selected[0].Rows).Count
                        : 0);
                return;
            }

            string[] ordered =
                [.. selected.Select(section => section.Name)];
            if (!CountOutput.ValidateMapFormat(
                    options.Format,
                    ordered))
            {
                return;
            }

            var counts = new CountProjection();
            foreach (LibraryCallUseSection section in selected)
            {
                counts.SetRows(
                    section.Name,
                    renderedNames.Contains(section.Name)
                        ? RowWindow.Apply(
                            options.Rows,
                            section.Rows).Count
                        : 0);
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
                    WriteSelectedDocument(
                        new MarkoutWriter(
                            writer,
                            formatter,
                            writerOptions),
                        result,
                        windowed,
                        includeDocumentHeading:
                            selected.Length > 1,
                        renderEmptyTables: true),
                maxRows: null);
            return;
        }

        if (options.Format
                is OutputFormat.Table
                or OutputFormat.Tsv
                or OutputFormat.Jsonl)
        {
            LibraryCallUseSection section = windowed[0];
            OutputFormatter.WriteProjectedTable(
                Console.Out,
                showHeader: !options.NoHeader,
                tsv: options.Format == OutputFormat.Tsv,
                jsonl: options.Format == OutputFormat.Jsonl,
                projectedColumns,
                fields: null,
                (writer, formatter, writerOptions) =>
                {
                    var markout = new MarkoutWriter(
                        writer,
                        formatter,
                        writerOptions);
                    WriteSectionTable(markout, section);
                    markout.Flush();
                },
                maxRows: null);
            return;
        }

        string[]? humanColumns =
            options.Columns is null && options.Fields is null
                ? DefaultSelectedColumns
                : projectedColumns;
        var projectedWriterOptions =
            OutputFormatter.CreateProjectedWriterOptions(
                humanColumns,
                fields: null,
                rows: null);
        var document = new MarkoutWriter(
            Console.Out,
            options.Format == OutputFormat.PlainText
                ? new PlainTextFormatter()
                : new MarkdownFormatter(),
            projectedWriterOptions);
        WriteSelectedDocument(
            document,
            result,
            windowed,
            includeDocumentHeading: selected.Length > 1);
        document.Flush();
    }

    static LibraryCallUseSection[] CreateSections(
        AssemblyPairCallUseProjection projection) =>
    [
        new(
            ConsumerUseSitesSection,
            "Attributed source methods that directly use the other library. "
                + "These are use sites, not inferred features or public entry points.",
            ConsumerUseSiteLabels,
            ConsumerUseSiteIds,
            [
                .. projection.ConsumerUseSites.Select(site => new[]
                {
                    AssemblyIdentityFormatter.Format(
                        site.Source.Identity),
                    site.SourceModuleVersionId.ToString("D"),
                    LibraryMetadataService.FormatMethod(
                        site.SourceMethod),
                    $"0x{site.SourceMethod.MetadataToken:X8}",
                    AssemblyIdentityFormatter.Format(
                        site.Target.Identity),
                    site.TargetModuleVersionId.ToString("D"),
                    site.TargetTypes.Length.ToString(),
                    site.TargetMethods.Length.ToString(),
                    site.CallSiteCount.ToString(),
                    FormatOccurrenceRows(site.OccurrenceIndexes),
                }),
            ],
            projection.IsComplete
                ? "No direct consumer use sites were observed."
                : "No exact consumer use sites were observed; the evidence is incomplete."),
        new(
            ProviderApiTypesSection,
            "Structured declaring types of exact selected target methods. "
                + "These are consumed provider types, not inferred capability clusters or public API boundaries.",
            ProviderApiTypeLabels,
            ProviderApiTypeIds,
            [
                .. projection.ProviderApiTypes.Select(type => new[]
                {
                    AssemblyIdentityFormatter.Format(
                        type.Source.Identity),
                    type.SourceModuleVersionId.ToString("D"),
                    AssemblyIdentityFormatter.Format(
                        type.Target.Identity),
                    type.TargetModuleVersionId.ToString("D"),
                    type.TargetType.Resolution?.Type.ToMetadataFullName()
                        ?? type.TargetType.ToQualifiedDisplayString(),
                    type.SourceMethods.Length.ToString(),
                    type.TargetMethods.Length.ToString(),
                    type.CallSiteCount.ToString(),
                    FormatOccurrenceRows(type.OccurrenceIndexes),
                }),
            ],
            projection.IsComplete
                ? "No provider API types were observed."
                : "No exact provider API types were observed; the evidence is incomplete."),
        new(
            CallSitesSection,
            "Exact physical call and construction occurrences crossing the library pair.",
            CallSiteLabels,
            CallSiteIds,
            CreateCallSiteRows(projection.Pair.Occurrences),
            projection.IsComplete
                ? "No direct pair call use was observed."
                : "No exact pair call use was observed; the evidence is incomplete."),
    ];

    static LibraryCallUseSection[] ApplyRowWindow(
        IEnumerable<LibraryCallUseSection> sections,
        RowWindow? rows) =>
    [
        .. sections.Select(section => section with
        {
            Rows = [.. RowWindow.Apply(rows, section.Rows)],
            WasLogicallyEmpty = section.Rows.Length == 0,
        }),
    ];

    static void WriteSelectedDocument(
        MarkoutWriter writer,
        AssemblyPairCallUseResult result,
        IReadOnlyList<LibraryCallUseSection> sections,
        bool includeDocumentHeading,
        bool renderEmptyTables = false)
    {
        if (includeDocumentHeading)
        {
            writer.WriteHeading(1, "Library Call Use");
            writer.WriteParagraph(
                $"{FormatAssembly(result.Subjects[0])} "
                + "\u2194 "
                + FormatAssembly(result.Subjects[1]));
        }

        bool first = true;
        foreach (LibraryCallUseSection section in sections)
        {
            if (!first || includeDocumentHeading)
                writer.WriteBlankLine();
            first = false;
            writer.WriteHeading(2, section.Name);
            writer.WriteParagraph(section.Summary);
            if (section.Rows.Length == 0
                && !renderEmptyTables)
            {
                writer.WriteParagraph(
                    section.WasLogicallyEmpty
                        ? section.EmptyText
                        : "No rows are selected by the row window.");
                continue;
            }
            WriteSectionTable(writer, section);
        }
    }

    static void WriteSectionTable(
        MarkoutWriter writer,
        LibraryCallUseSection section) =>
        writer.WriteTable(
            section.Labels,
            section.Ids,
            section.Rows);

    static void WriteCallSiteTable(
        MarkoutWriter writer,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences) =>
        writer.WriteTable(
            CallSiteLabels,
            CallSiteIds,
            CreateCallSiteRows(occurrences));

    static string[][] CreateCallSiteRows(
        IEnumerable<AssemblyPairCallUseOccurrence> occurrences) =>
    [
        .. occurrences.Select(occurrence => new[]
        {
            AssemblyIdentityFormatter.Format(
                occurrence.Source.Identity),
            occurrence.SourceModuleVersionId.ToString("D"),
            LibraryMetadataService.FormatMethod(
                occurrence.SourceMethod),
            $"0x{occurrence.SourceMethod.MetadataToken:X8}",
            AssemblyIdentityFormatter.Format(
                occurrence.Target.Identity),
            occurrence.TargetModuleVersionId.ToString("D"),
            LibraryMetadataService.FormatMethod(
                occurrence.TargetMethod),
            $"0x{occurrence.TargetMethod.MetadataToken:X8}",
            FormatCallKind(occurrence.Call.Kind),
            LibraryMetadataService.FormatMethod(
                occurrence.Call.EvidenceMethod),
            occurrence.Call.EvidenceMethod.ModuleVersionId
                .ToString("D"),
            $"0x{occurrence.Call.EvidenceMethod.MetadataToken:X8}",
            $"0x{occurrence.Call.ILOffset:X4}",
            $"0x{occurrence.Call.OperandToken:X8}",
            occurrence.Call.ExactTarget ? "yes" : "no",
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

    static string FormatAssembly(AssemblyContextSubject subject) =>
        subject.Identity.Version is { } version
            ? $"{subject.Identity.Name}@{version}"
            : subject.Identity.Name;

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

    sealed record LibraryCallUseSection(
        string Name,
        string Summary,
        string[] Labels,
        string[] Ids,
        string[][] Rows,
        string EmptyText)
    {
        internal bool WasLogicallyEmpty { get; init; }
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
