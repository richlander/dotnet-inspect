using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
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
        LibraryCallUseViewSections.ConsumerUseSites;
    internal const string ProviderApiTypesSection =
        LibraryCallUseViewSections.ProviderApiTypes;
    internal const string CallSitesSection =
        LibraryCallUseViewSections.CallSites;

    static readonly string[] SectionOrder =
    [
        ConsumerUseSitesSection,
        ProviderApiTypesSection,
        CallSitesSection,
    ];

    static readonly IReadOnlyDictionary<string, string[]> NoCategories =
        new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase);

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
            selectedNames =
            [
                ConsumerUseSitesSection,
                ProviderApiTypesSection,
            ];
            return true;
        }

        if (options.Select is { Length: 0 })
        {
            CommandError.Write(
                "--select requires at least one name.");
            selectedNames = [];
            return false;
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
        List<LibraryCallUseCallSiteRow> rows =
            CreateCallSiteRows(result.Occurrences);
        IReadOnlyList<AssemblyPairCallUseOccurrence> selectedOccurrences =
            RowWindow.Apply(options.Rows, result.Occurrences);
        var view = new LibraryCallUseCallSitesView
        {
            Title = "Library Call Use",
            Description = CreateDefaultDescription(
                result,
                selectedOccurrences,
                options.Rows),
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
            columns = DefaultCallSiteColumns;
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
        LibraryCallUseOptions options,
        IReadOnlyCollection<string> selectedNames)
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
            CreateSelectedView(projection);
        var writerOptions =
            OutputFormatter.CreateProjectedWriterOptions(
                projectedColumns,
                fields: null,
                options.Rows);
        writerOptions.IncludeSections = renderedNames;

        if (options.Count)
        {
            string[] ordered =
                [.. SectionOrder.Where(selectedNames.Contains)];
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
            options,
            renderedNames,
            humanColumns,
            includeDocumentHeading: selectedNames.Count > 1);
    }

    static void WriteSelectedHuman(
        AssemblyPairCallUseResult result,
        AssemblyPairCallUseProjection projection,
        LibraryCallUseOptions options,
        IReadOnlySet<string> renderedNames,
        string[]? columns,
        bool includeDocumentHeading)
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
        foreach (string section in SectionOrder)
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
                            options.Rows),
                        Console.Out,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                    break;
                case ProviderApiTypesSection:
                    MarkoutSerializer.Serialize(
                        CreateProviderApiTypesView(
                            projection,
                            options.Rows),
                        Console.Out,
                        formatter,
                        LibraryCallUseViewContext.Default,
                        writerOptions);
                    break;
                case CallSitesSection:
                    MarkoutSerializer.Serialize(
                        CreateSelectedCallSitesView(
                            projection,
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
        AssemblyPairCallUseProjection projection) =>
        new()
        {
            ConsumerUseSites =
                [.. projection.ConsumerUseSites.Select(CreateConsumerUseSiteRow)],
            ProviderApiTypes =
                [.. projection.ProviderApiTypes.Select(CreateProviderApiTypeRow)],
            CallSites = CreateCallSiteRows(projection.Pair.Occurrences),
        };

    static LibraryCallUseConsumerUseSitesView CreateConsumerUseSitesView(
        AssemblyPairCallUseProjection projection,
        RowWindow? rows)
    {
        List<LibraryCallUseConsumerUseSiteRow> values =
            [.. projection.ConsumerUseSites.Select(CreateConsumerUseSiteRow)];
        return new()
        {
            Description = CreateSectionDescription(
                "Attributed source methods that directly use the other library. "
                    + "These are use sites, not inferred features or public entry points.",
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
        RowWindow? rows)
    {
        List<LibraryCallUseProviderApiTypeRow> values =
            [.. projection.ProviderApiTypes.Select(CreateProviderApiTypeRow)];
        return new()
        {
            Description = CreateSectionDescription(
                "Structured declaring types of exact selected target methods. "
                    + "These are consumed provider types, not inferred capability clusters or public API boundaries.",
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
        RowWindow? rows)
    {
        List<LibraryCallUseCallSiteRow> values =
            CreateCallSiteRows(projection.Pair.Occurrences);
        return new()
        {
            Title = CallSitesSection,
            Description = CreateSectionDescription(
                "Exact physical call and construction occurrences crossing the library pair.",
                projection.IsComplete
                    ? "No direct pair call use was observed."
                    : "No exact pair call use was observed; the evidence is incomplete.",
                values,
                rows),
            Rows = HasSelectedRows(values, rows) ? values : null,
        };
    }

    static string CreateDefaultDescription(
        AssemblyPairCallUseResult result,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences,
        RowWindow? rows)
    {
        string[] summaries = [.. RelationshipSummaries(occurrences)];
        string detail = summaries.Length > 0
            ? string.Join("\n\n", summaries)
            : result.Occurrences.Length > 0 && rows is not null
                ? "No direct pair call use is selected by the row window."
                : result.IsComplete
                    ? "No direct pair call use was observed."
                    : "No exact pair call use was observed; the evidence is incomplete.";
        return $"{FormatPair(result)}\n\n{detail}";
    }

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
