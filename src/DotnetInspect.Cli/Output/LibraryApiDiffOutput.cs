using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Views;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using Inspector.Findings;
using Markout;

namespace DotnetInspect.Cli.Output;

internal static class LibraryApiDiffOutput
{
    static readonly InspectionEnvelopeJsonContract<LibraryApiDiffOutcome> JsonContract =
        new("library-api-diff", 2, LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome);

    internal static int Write(
        InspectionEnvelope<LibraryApiDiffOutcome> envelope,
        string name,
        string beforeVersion,
        string afterVersion,
        DiffOptions options)
    {
        foreach (InspectionDiagnostic diagnostic in envelope.Diagnostics)
            CommandError.WriteNote(diagnostic.Summary.ToString());

        if (options.EnvelopeOutput || options.IsContentJson)
        {
            if (!InspectionEnvelopeOutput.TryWrite(
                    envelope,
                    JsonContract,
                    options.EnvelopeOutput,
                    options.CompactJson))
            {
                return 1;
            }
            return envelope.Content is LibraryApiDiffOutcome.Available ? 0 : 1;
        }

        if (envelope.Content
            is not LibraryApiDiffOutcome.Available available)
        {
            string reason = DescribeNonSuccess(envelope.Content);
            List<DiffInspectionFailureRow> failures =
                InspectionFailures(envelope.Content).ToList();
            if (options.JsonOutput || !options.Tabular && !options.NameOnly)
            {
                var document = new DiffDocumentView(
                    DiffViewText.Field($"Diff: {name}"),
                    DiffViewText.Field($"{beforeVersion} -> {afterVersion}"),
                    DiffViewText.Field(reason),
                    null, null, null, null, null, null, null,
                    null, null,
                    failures.Count == 0 ? null : DiffViewText.Prose(reason))
                {
                    InspectionFailures = failures.Count == 0 ? null : failures,
                };
                Console.WriteLine(options.JsonOutput
                    ? JsonSerializer.Serialize(document, DiffJsonContext.Default.DiffDocumentView)
                    : DiffOutputFormatter.RenderDocumentView(
                        document, OutputFormatter.CreateWindowedOptions(options.Rows)));
            }
            else
                CommandError.Write(reason);
            return 1;
        }

        List<SelectedType> types = Select(available.Document, options);
        string summary = Summary(types);
        var detailed = new DiffDetailedChangesView(
            DiffViewText.Field($"API Diff: {name}"),
            DiffViewText.Field($"{beforeVersion} -> {afterVersion}"),
            DiffViewText.Field(summary))
        {
            Rows = types.SelectMany(DetailedRows).ToList() is { Count: > 0 } rows
                ? rows
                : null,
        };

        if (options.JsonOutput)
        {
            WriteJson(detailed, name, beforeVersion, afterVersion);
        }
        else if (options.NameOnly)
        {
            Console.WriteLine(OutputFormatter.RenderTable(showHeader: false, (writer, formatter) =>
            {
                var nameWriter = new MarkoutWriter(
                    writer, formatter,
                    OutputFormatter.CreateTableWriterOptions(options.Tsv, options.Jsonl));
                foreach (SelectedType type in types)
                    nameWriter.WriteListItem(CSharpIdentifier.ContainRenderedText(type.Subject.Display));
                nameWriter.Flush();
            }));
        }
        else if (options.Tabular)
        {
            if (options.IncludeSections?.Contains("Changes") == true)
            {
                OutputFormatter.WriteProjectedTable(
                    Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(
                            detailed, writer, formatter,
                            DiffViewContext.Default, writerOptions),
                    options.Rows);
            }
            else
            {
                var table = new DiffTableView(
                    detailed.TitleText,
                    detailed.VersionsText,
                    detailed.SummaryText)
                {
                    Rows = types.Count == 0 ? null : types.Select(type =>
                        new DiffTableRow(
                            type.Subject.Comparison.PairKind switch
                            {
                                LibraryApiTypePairKind.Added => "+",
                                LibraryApiTypePairKind.Removed => "-",
                                _ when type.Changes.Any(change =>
                                    change.Classification == ChangeClassification.Breaking) => "x",
                                _ => "~",
                            },
                            TypeMatcher.GetSimpleName(type.Subject.Display),
                            type.Subject.Comparison.PairKind switch
                            {
                                LibraryApiTypePairKind.Added => "added",
                                LibraryApiTypePairKind.Removed => "removed",
                                _ when type.Changes.IsEmpty => "unclassified API changes",
                                _ => ChangeSummary(type.Changes),
                            })).ToList(),
                };
                OutputFormatter.WriteProjectedTable(
                    Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                    options.Columns, options.Fields,
                    (writer, formatter, writerOptions) =>
                        MarkoutSerializer.Serialize(
                            table, writer, formatter,
                            DiffViewContext.Default, writerOptions),
                    options.Rows);
            }
        }
        else
        {
            var view = new DiffFullView(
                detailed.TitleText,
                DiffViewText.Field($"**{beforeVersion}** → **{afterVersion}**"),
                types.Count == 0
                    ? InertString.Empty
                    : DiffViewText.Field($"**Summary:** {summary}"))
            {
                BreakingChanges = ChangeRows(ChangeClassification.Breaking),
                PotentiallyBreakingChanges = ChangeRows(ChangeClassification.PotentiallyBreaking),
                AdditiveChanges = ChangeRows(ChangeClassification.Additive),
                OtherChanges = types.SelectMany(UnclassifiedRows)
                    .Select(row => new DiffChangeRow(row.TypeText, row.DetailText))
                    .ToList() is { Count: > 0 } other ? other : null,
            };
            if (types.Count == 0)
                view.Status = new(CalloutSeverity.Note, "No API changes detected.");
            var writer = new MarkoutWriter(
                new MarkdownFormatter(),
                OutputFormatter.CreateWindowedOptions(options.Rows));
            DiffViewContext.Default.Serialize(view, writer);
            Console.WriteLine(writer.ToString().TrimEnd());
        }
        return 0;

        List<DiffChangeRow>? ChangeRows(ChangeClassification classification)
        {
            List<DiffChangeRow> rows =
            [
                .. types.SelectMany(type => type.Changes
                    .Where(change => change.Classification == classification)
                    .Select(change => DiffOutputFormatter.BuildChangeRow(
                        type.Subject.Display,
                        change.Kind,
                        change.Message,
                        change.OldValue,
                        change.NewValue))),
            ];
            return rows.Count == 0 ? null : rows;
        }
    }

    static List<SelectedType> Select(
        LibraryApiDiffDocument document,
        DiffOptions options)
    {
        IEnumerable<ComparisonSubject<LibraryApiTypeDiff>> subjects =
            document.Comparison.Subjects;
        if (options.TypeFilter.Count > 0)
        {
            subjects = subjects.Where(subject =>
                DiffCommand.MatchesAnyDiffTypeFilter(
                    subject.Display, options.TypeFilter));
        }
        List<SelectedType> types =
        [
            .. subjects.OrderBy(subject => subject.Display, StringComparer.Ordinal)
                .Select(subject => new SelectedType(
                    subject,
                    subject.Comparison.CompatibilityChanges)),
        ];
        if (types.Count == 0
            && document.Comparison.Subjects.Length > 0
            && options.TypeFilter.Count > 0)
        {
            CommandError.WriteNote(
                $"type filter matched no changed types: {string.Join(", ", options.TypeFilter)}.");
        }
        if (!options.Breaking && !options.Additive)
            return types;

        List<SelectedType> filtered =
        [
            .. types.Select(type => type with
            {
                Changes =
                [
                    .. type.Changes.Where(change =>
                        options.Breaking && change.Classification == ChangeClassification.Breaking
                        || options.Additive && change.Classification == ChangeClassification.Additive),
                ],
                IncludeUnclassified = false,
            }).Where(type => !type.Changes.IsEmpty),
        ];
        if (types.Count > 0 && filtered.Count == 0)
            CommandError.WriteNote("classification filter removed all changes after type/member filters.");
        return filtered;
    }

    static IEnumerable<DiffDetailedChangeRow> DetailedRows(SelectedType type)
        => type.Changes.Select(change =>
            DiffOutputFormatter.BuildDetailedRow(
                type.Subject.Display,
                change.Kind,
                change.Classification,
                change.Subject.BeforeMember?.Anchor.StableSelector
                    ?? change.Subject.AfterMember?.Anchor.StableSelector
                    ?? "",
                change.Message,
                change.OldValue,
                change.NewValue))
            .Concat(UnclassifiedRows(type));

    static IEnumerable<DiffDetailedChangeRow> UnclassifiedRows(SelectedType type)
    {
        if (!type.IncludeUnclassified)
            yield break;

        LibraryApiTypeDiff value = type.Subject.Comparison;
        if (value.TypeDefinitionChanged is true
            && !type.Changes.Any(change => change.Subject.Kind == ApiChangeSubjectKind.Type))
        {
            yield return Unclassified(
                "", "TypeDefinitionChanged",
                "Type definition changed without a compatibility classification.");
        }
        if (value.PairKind is LibraryApiTypePairKind.Added or LibraryApiTypePairKind.Removed)
            yield break;
        foreach (LibraryApiMemberDiff member in value.Members)
        {
            LibraryApiMemberRelation relation = member.Relation;
            if (type.Changes.Any(change =>
                relation.Before is not null && change.Subject.BeforeMember == relation.Before
                || relation.After is not null && change.Subject.AfterMember == relation.After))
            {
                continue;
            }
            yield return Unclassified(
                relation.Before?.Anchor.StableSelector
                    ?? relation.After!.Anchor.StableSelector,
                $"Member{relation.PairKind}",
                $"Member {relation.PairKind.ToString().ToLowerInvariant()} without a compatibility classification.");
        }

        DiffDetailedChangeRow Unclassified(string member, string kind, string detail)
            => new(
                DiffViewText.Field("~"),
                DiffViewText.Field("unclassified"),
                DiffViewText.Field(TypeMatcher.GetSimpleName(type.Subject.Display)),
                DiffViewText.Field(member),
                DiffViewText.Field(kind),
                DiffViewText.Field(detail),
                InertString.Empty,
                InertString.Empty);
    }

    static string Summary(List<SelectedType> types)
        => types.Count == 0
            ? "no changes"
            : $"{ChangeSummary(types.SelectMany(type => type.Changes))} across {types.Count} types";

    static string ChangeSummary(IEnumerable<LibraryApiCompatibilityChange> changes)
    {
        LibraryApiCompatibilityChange[] rows = [.. changes];
        return rows.Length == 0
            ? "unclassified API changes"
            : DiffOutputFormatter.FormatSummaryCounts(
                rows.Count(change => change.Classification == ChangeClassification.Breaking),
                rows.Count(change => change.Classification == ChangeClassification.Additive),
                rows.Count(change => change.Classification == ChangeClassification.PotentiallyBreaking));
    }

    static void WriteJson(
        DiffDetailedChangesView changes,
        string name,
        string beforeVersion,
        string afterVersion)
    {
        DiffDocumentView document = DiffOutputFormatter.BuildDocumentView(
            name, beforeVersion, afterVersion,
            changes, null, null, null, []);
        Console.WriteLine(JsonSerializer.Serialize(
            document, DiffJsonContext.Default.DiffDocumentView));
    }

    static IEnumerable<DiffInspectionFailureRow> InspectionFailures(
        LibraryApiDiffOutcome result)
    {
        if (result is not LibraryApiDiffOutcome.Unavailable unavailable)
            yield break;

        foreach (var (side, endpoint) in new[]
        {
            ("old", unavailable.Before),
            ("new", unavailable.After),
        })
        {
            foreach (LibraryApiDiffInspectionFailure failure in endpoint.Issues
                .OfType<LibraryApiDiffEndpointIssue.InspectionFailures>()
                .SelectMany(issue => issue.Details))
            {
                yield return new(
                    side,
                    AssemblyIdentityFormatter.Format(failure.SubjectAssembly ?? endpoint.Identity),
                    failure.Operation.ToString(),
                    $"0x{failure.SubjectToken:X8}",
                    failure.Mechanism.ToString(),
                    failure.Kind.ToString(),
                    failure.Detail.ToString(),
                    failure.DependencyAssembly is null
                        ? null
                        : AssemblyIdentityFormatter.Format(failure.DependencyAssembly));
            }
        }
    }

    static string DescribeNonSuccess(LibraryApiDiffOutcome result)
        => result switch
        {
            LibraryApiDiffOutcome.Unavailable unavailable =>
                $"API comparison is incomplete; not compared ({unavailable.Kind}). "
                    + $"Before: {Describe(unavailable.Before)}. After: {Describe(unavailable.After)}.",
            LibraryApiDiffOutcome.Rejected rejected =>
                $"API comparison not compared: {rejected.Kind}.",
            _ => throw new InvalidOperationException("Unknown Library API Diff outcome."),
        };

    static string Describe(LibraryApiDiffEndpointSummary endpoint)
        => endpoint.IsComplete ? "complete" : string.Join("; ", endpoint.Issues.Select(issue =>
            issue switch
            {
                LibraryApiDiffEndpointIssue.Truncated truncated =>
                    $"projection truncated: {truncated.Truncation}",
                LibraryApiDiffEndpointIssue.Rejected rejected => rejected.Detail.ToString(),
                LibraryApiDiffEndpointIssue.Failed failed => failed.Detail.ToString(),
                LibraryApiDiffEndpointIssue.InspectionFailures failures =>
                    $"{failures.Count} metadata inspection failure(s)",
                LibraryApiDiffEndpointIssue.DegradedSignatures signatures =>
                    $"{signatures.Count} degraded signature(s)",
                LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation population =>
                    $"unexpected assembly population: {population.Count}",
                _ => throw new InvalidOperationException("Unknown Library API endpoint issue."),
            }));

    sealed record SelectedType(
        ComparisonSubject<LibraryApiTypeDiff> Subject,
        ImmutableArray<LibraryApiCompatibilityChange> Changes,
        bool IncludeUnclassified = true);
}
