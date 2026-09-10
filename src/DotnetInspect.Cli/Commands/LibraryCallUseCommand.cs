using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Analysis;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Commands;

public static class LibraryCallUseCommand
{
    public const string Name = "libraries";

    static readonly string[] Labels =
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

    static readonly string[] Ids =
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

    static readonly string[] DefaultColumns =
    [
        "Source Member",
        "Target Member",
        "Call",
        "Evidence Method",
        "IL Offset",
    ];

    public static async Task<int> ExecuteAsync(
        LibraryCallUseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Libraries.Length != 2)
        {
            CommandError.Write(
                "Exactly two --library values are required.");
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

        Write(result, options);
        if (!result.IsComplete)
        {
            CommandError.Write(
                "Pairwise call-use evidence is incomplete.",
                [.. FailureDetails(result)]);
            return 1;
        }

        return 0;
    }

    static void Write(
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
            columns = DefaultColumns;
        }

        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions>
            serialize = (writer, formatter, writerOptions) =>
            {
                var markout = new MarkoutWriter(
                    writer,
                    formatter,
                    writerOptions);
                WriteTable(markout, occurrences);
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
                WriteTable(markout, occurrences);
                markout.Flush();
                break;
        }
    }

    static void WriteTable(
        MarkoutWriter writer,
        IReadOnlyList<AssemblyPairCallUseOccurrence> occurrences) =>
        writer.WriteTable(
            Labels,
            Ids,
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
            ]);

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
