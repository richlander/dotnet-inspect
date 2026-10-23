using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Views;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class LibraryApiDiffOutput
{
    internal static readonly InspectionEnvelopeJsonContract<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiTypeChangeRow>>>
        TypeTableContract = new("library-api-diff", 2, LibraryApiChangesJsonContext.Default.TypeTableOutcome);
    internal static readonly InspectionEnvelopeJsonContract<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>>
        ChangesTableContract = new("library-api-diff", 2, LibraryApiChangesJsonContext.Default.ChangesTableOutcome);
    internal static readonly InspectionEnvelopeJsonContract<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiTypeChangeRow>>>
        TypeDocumentContract = new("library-api-diff", 2, LibraryApiChangesJsonContext.Default.TypeDocumentOutcome);
    internal static readonly InspectionEnvelopeJsonContract<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiChangeRow>>>
        ChangesDocumentContract = new("library-api-diff", 2, LibraryApiChangesJsonContext.Default.ChangesDocumentOutcome);

    internal static void WriteTypeRows(
        LibraryApiChangesTable<LibraryApiTypeChangeRow> table, DiffOptions options)
    {
        if (options.NameOnly)
        {
            Console.WriteLine(OutputFormatter.RenderTable(showHeader: false, (output, formatter) =>
            {
                var writer = new MarkoutWriter(output, formatter,
                    OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
                foreach (LibraryApiTypeChangeRow row in table.Rows)
                    writer.WriteListItem(CSharpIdentifier.ContainRenderedText(row.Display));
                writer.Flush();
            }));
            return;
        }
        WriteRows(["Change", "Type", "Detail"], ["change", "type", "detail"],
            table.Rows.Select(type => new[]
            {
                type.Comparison.PairKind switch
                {
                    LibraryApiTypePairKind.Added => "+",
                    LibraryApiTypePairKind.Removed => "-",
                    _ when type.Changes.Any(change =>
                        change.Classification == ChangeClassification.Breaking) => "x",
                    _ => "~",
                },
                MarkObsolete(TypeMatcher.GetSimpleName(type.Display), type),
                type.Comparison.PairKind switch
                {
                    LibraryApiTypePairKind.Added => "added",
                    LibraryApiTypePairKind.Removed => "removed",
                    _ => ChangeSummary(type.Changes),
                },
            }).ToArray(), options);
    }

    internal static void WriteChangesRows(
        LibraryApiChangesTable<LibraryApiChangeRow> table, DiffOptions options)
        => WriteRows(
            ["Change", "Classification", "Type", "Member", "Kind", "Detail", "Old", "New"],
            ["change", "classification", "type", "member", "kind", "detail", "old", "new"],
            table.Rows.Select(DetailedRow).Select(row => new[]
            {
                row.Change, row.Classification, row.Type, row.Member,
                row.Kind, row.Detail, row.Old, row.New,
            }).ToArray(), options);

    private static void WriteRows(
        string[] headers, string[] keys, string[][] rows, DiffOptions options)
        => OutputFormatter.WriteProjectedTable(
            Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
            options.Columns, options.Fields,
            (output, formatter, writerOptions) =>
            {
                var writer = new MarkoutWriter(output, formatter, writerOptions);
                writer.WriteTable(headers, keys, rows.Select(row =>
                    row.Select(value => DiffViewText.Field(value).ToString()).ToArray()).ToArray());
                writer.Flush();
            });

    internal static int Write<T>(
        InspectionEnvelope<LibraryApiChangesOutcome<T>> envelope,
        InspectionEnvelopeJsonContract<LibraryApiChangesOutcome<T>> contract,
        string name,
        DiffOptions options,
        Action<T> render)
    {
        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
            CommandError.WriteNote(diagnostic.Summary.ToString());

        if (options.EnvelopeOutput || options.JsonOutput)
            return InspectionEnvelopeOutput.TryWrite(
                envelope, contract, options.EnvelopeOutput, options.CompactJson)
                && envelope.Content is LibraryApiChangesOutcome<T>.Available ? 0 : 1;

        if (envelope.Content is LibraryApiChangesOutcome<T>.Available available)
        {
            render(available.Content);
            return 0;
        }
        LibraryApiDiffOutcome failure = envelope.Content switch
        {
            LibraryApiChangesOutcome<T>.Unavailable unavailable => unavailable.Failure,
            LibraryApiChangesOutcome<T>.Rejected rejected => rejected.Failure,
            _ => throw new InvalidOperationException("Unknown Library API representation outcome."),
        };
        string reason = DescribeNonSuccess(failure);
        if (options.Tabular || options.Tsv || options.Jsonl || options.NameOnly)
            CommandError.Write(reason);
        else
        {
            List<DiffInspectionFailureRow> failures = InspectionFailures(failure).ToList();
            var document = new DiffDocumentView(
                DiffViewText.Field($"Diff: {name}"), InertString.Empty,
                DiffViewText.Field(reason),
                null, null, null, null, null, null, null, null, null, null,
                failures.Count == 0 ? null : DiffViewText.Prose(reason))
            {
                InspectionFailures = failures.Count == 0 ? null : failures,
            };
            Console.WriteLine(DiffOutputFormatter.RenderDocumentView(
                document, OutputFormatter.CreateWindowedOptions(options.Rows)));
        }
        return 1;
    }

    internal static void WriteTypeDocument(
        LibraryApiChangesDocument<LibraryApiTypeChangeRow> document,
        string name, string beforeVersion, string afterVersion, DiffOptions options,
        string? beforeFramework, string? afterFramework)
        => WriteDocument(document.Summary,
            LibraryApiChangesInspection.DetailedRows(document.Rows),
            name, beforeVersion, afterVersion, options, beforeFramework, afterFramework);

    internal static void WriteChangesDocument(
        LibraryApiChangesDocument<LibraryApiChangeRow> document,
        string name, string beforeVersion, string afterVersion, DiffOptions options,
        string? beforeFramework, string? afterFramework)
        => WriteDocument(document.Summary, document.Rows,
            name, beforeVersion, afterVersion, options, beforeFramework, afterFramework);

    private static void WriteDocument(
        LibraryApiDiffSummary summary, ImmutableArray<LibraryApiChangeRow> rows,
        string name, string beforeVersion, string afterVersion, DiffOptions options,
        string? beforeFramework, string? afterFramework)
    {
        bool differingFrameworks = beforeFramework is not null && afterFramework is not null
            && !beforeFramework.Equals(afterFramework, StringComparison.OrdinalIgnoreCase);
        string versions = differingFrameworks
            ? $"**{beforeVersion}** ({beforeFramework}) → **{afterVersion}** ({afterFramework})"
            : $"**{beforeVersion}** → **{afterVersion}**";
        string counts = summary.BreakingCount + summary.AdditiveCount
            + summary.PotentiallyBreakingCount + summary.NonBreakingCount == 0
                ? "unclassified API changes"
                : DiffOutputFormatter.FormatSummaryCounts(
                    summary.BreakingCount, summary.AdditiveCount,
                    summary.PotentiallyBreakingCount, summary.NonBreakingCount);
        var view = new DiffFullView(
            DiffViewText.Field($"API Diff: {name}"),
            DiffViewText.Field(versions),
            summary.ChangedTypeCount == 0 ? InertString.Empty
                : DiffViewText.Field($"**Summary:** {counts} across {summary.ChangedTypeCount} types"))
        {
            BreakingChanges = ChangeRows(ChangeClassification.Breaking),
            PotentiallyBreakingChanges = ChangeRows(ChangeClassification.PotentiallyBreaking),
            AdditiveChanges = ChangeRows(ChangeClassification.Additive),
            NonBreakingChanges = ChangeRows(ChangeClassification.NonBreaking),
            OtherChanges = rows.OfType<LibraryApiChangeRow.Unclassified>()
                .Select(row => DetailedRow(row))
                .Select(row => new DiffChangeRow(row.TypeText, row.DetailText))
                .ToList() is { Count: > 0 } other ? other : null,
        };
        if (summary.ChangedTypeCount == 0)
            view.Status = new(CalloutSeverity.Note, "No API changes detected.");
        var writer = new MarkoutWriter(new MarkdownFormatter());
        DiffViewContext.Default.Serialize(view, writer);
        Console.WriteLine(writer.ToString().TrimEnd());

        List<DiffChangeRow>? ChangeRows(ChangeClassification classification)
        {
            List<DiffChangeRow> projected =
            [
                .. rows.OfType<LibraryApiChangeRow.Classified>()
                    .Where(row => row.Change.Classification == classification)
                    .Select(row => DiffOutputFormatter.BuildChangeRow(
                        row.Type.Display, row.Change.Kind, MarkObsolete(row.Change),
                        row.Change.OldValue, row.Change.NewValue) is var display
                            ? display with { TypeNameText = MarkObsolete(display.TypeNameText, row.Type) }
                            : null!),
            ];
            return projected.Count == 0 ? null : projected;
        }
    }

    private static DiffDetailedChangeRow DetailedRow(LibraryApiChangeRow row)
    {
        DiffDetailedChangeRow display = row switch
        {
            LibraryApiChangeRow.Classified classified =>
                DiffOutputFormatter.BuildDetailedRow(
                    row.Type.Display, classified.Change.Kind, classified.Change.Classification,
                    classified.Change.Subject.BeforeMember?.Anchor.StableSelector
                        ?? classified.Change.Subject.AfterMember?.Anchor.StableSelector ?? "",
                    MarkObsolete(classified.Change), classified.Change.OldValue, classified.Change.NewValue),
            LibraryApiChangeRow.Unclassified { Evidence.Relation: { } relation } =>
                Unclassified(
                    relation.Before?.Anchor.StableSelector ?? relation.After!.Anchor.StableSelector,
                    $"Member{relation.PairKind}",
                    $"Member {relation.PairKind.ToString().ToLowerInvariant()} without a compatibility classification."),
            LibraryApiChangeRow.Unclassified =>
                Unclassified("", "TypeDefinitionChanged",
                    "Type definition changed without a compatibility classification."),
            _ => throw new InvalidOperationException("Unknown Library API change row."),
        };
        return display with { TypeText = MarkObsolete(display.TypeText, row.Type) };

        DiffDetailedChangeRow Unclassified(string member, string kind, string detail)
            => new(DiffViewText.Field("~"), DiffViewText.Field("unclassified"),
                DiffViewText.Field(TypeMatcher.GetSimpleName(row.Type.Display)),
                DiffViewText.Field(member), DiffViewText.Field(kind), DiffViewText.Field(detail),
                InertString.Empty, InertString.Empty);
    }

    const string ObsoleteMarker = "[Obsolete]";

    private static bool IsObsolete(LibraryApiTypeChangeRow type)
        => (type.Comparison.After ?? type.Comparison.Before)!.Status.IsObsolete;

    private static string MarkObsolete(string text, LibraryApiTypeChangeRow type)
        => IsObsolete(type) ? $"{text} {ObsoleteMarker}" : text;

    private static InertString MarkObsolete(InertString text, LibraryApiChangedType type)
        => (type.After ?? type.Before)!.Status.IsObsolete
            ? InertString.Format(TextPolicy.Field, $"{text} {ObsoleteMarker}") : text;

    private static InertString MarkObsolete(LibraryApiCompatibilityChange change)
        => change.Subject.Kind == ApiChangeSubjectKind.Member
            && change.State != LibraryApiChangeState.Obsoleted
            && (change.Subject.AfterMember ?? change.Subject.BeforeMember)?.Status.IsObsolete == true
                ? InertString.Format(TextPolicy.Field, $"{ObsoleteMarker} {change.Message}")
                : change.Message;

    private static string ChangeSummary(IEnumerable<LibraryApiCompatibilityChange> changes)
    {
        LibraryApiCompatibilityChange[] rows = [.. changes];
        return rows.Length == 0 ? "unclassified API changes"
            : DiffOutputFormatter.FormatSummaryCounts(
                rows.Count(change => change.Classification == ChangeClassification.Breaking),
                rows.Count(change => change.Classification == ChangeClassification.Additive),
                rows.Count(change => change.Classification == ChangeClassification.PotentiallyBreaking),
                rows.Count(change => change.Classification == ChangeClassification.NonBreaking));
    }

    private static IEnumerable<DiffInspectionFailureRow> InspectionFailures(LibraryApiDiffOutcome result)
    {
        if (result is not LibraryApiDiffOutcome.Unavailable unavailable)
            yield break;
        foreach (var (side, endpoint) in new[] { ("old", unavailable.Before), ("new", unavailable.After) })
            foreach (LibraryApiDiffInspectionFailure failure in endpoint.Issues
                .OfType<LibraryApiDiffEndpointIssue.InspectionFailures>().SelectMany(issue => issue.Details))
                yield return new(side,
                    AssemblyIdentityFormatter.Format(failure.SubjectAssembly ?? endpoint.Identity),
                    failure.Operation.ToString(), $"0x{failure.SubjectToken:X8}",
                    failure.Mechanism.ToString(), failure.Kind.ToString(), failure.Detail.ToString(),
                    failure.DependencyAssembly is null ? null
                        : AssemblyIdentityFormatter.Format(failure.DependencyAssembly));
    }

    private static string DescribeNonSuccess(LibraryApiDiffOutcome result)
        => result switch
        {
            LibraryApiDiffOutcome.Unavailable unavailable =>
                $"API comparison is incomplete; not compared ({unavailable.Kind}). "
                    + $"Before: {Describe(unavailable.Before)}. After: {Describe(unavailable.After)}.",
            LibraryApiDiffOutcome.Rejected rejected => $"API comparison not compared: {rejected.Kind}.",
            _ => throw new InvalidOperationException("Unknown Library API Diff outcome."),
        };

    private static string Describe(LibraryApiDiffEndpointSummary endpoint)
        => endpoint.IsComplete ? "complete" : string.Join("; ", endpoint.Issues.Select(issue => issue switch
        {
            LibraryApiDiffEndpointIssue.Truncated truncated => $"projection truncated: {truncated.Truncation}",
            LibraryApiDiffEndpointIssue.Rejected rejected => rejected.Detail.ToString(),
            LibraryApiDiffEndpointIssue.Failed failed => failed.Detail.ToString(),
            LibraryApiDiffEndpointIssue.InspectionFailures failures => $"{failures.Count} metadata inspection failure(s)",
            LibraryApiDiffEndpointIssue.DegradedSignatures signatures => $"{signatures.Count} degraded signature(s)",
            LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation population => $"unexpected assembly population: {population.Count}",
            _ => throw new InvalidOperationException("Unknown Library API endpoint issue."),
        }));
}
