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
    internal static readonly InspectionEnvelopeJsonContract<LibraryApiDiffOutcome> JsonContract =
        new("library-api-diff", 1, LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome);

    internal static int Write(
        InspectionEnvelope<LibraryApiDiffOutcome> envelope,
        string name,
        string beforeVersion,
        string afterVersion,
        DiffOptions options,
        string? beforeFramework = null,
        string? afterFramework = null)
    {
        // Each endpoint selects its own framework unless --tfm names one for
        // both; when they differ, the versions name them
        // (docs/design/package-endpoint-scope.md, step 4 part 1).
        string beforeLabel = beforeVersion;
        string afterLabel = afterVersion;
        if (beforeFramework is not null
            && afterFramework is not null
            && !beforeFramework.Equals(afterFramework, StringComparison.OrdinalIgnoreCase))
        {
            beforeLabel = $"{beforeVersion} ({beforeFramework})";
            afterLabel = $"{afterVersion} ({afterFramework})";
        }

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
                    DiffViewText.Field($"{beforeLabel} -> {afterLabel}"),
                    DiffViewText.Field(reason),
                    null, null, null, null, null, null, null,
                    null, null, null,
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
            DiffViewText.Field($"{beforeLabel} -> {afterLabel}"),
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
                            MarkObsolete(
                                TypeMatcher.GetSimpleName(type.Subject.Display),
                                type),
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
                DiffViewText.Field(
                    beforeLabel == beforeVersion
                        ? $"**{beforeVersion}** → **{afterVersion}**"
                        : $"**{beforeVersion}** ({beforeFramework}) → **{afterVersion}** ({afterFramework})"),
                types.Count == 0
                    ? InertString.Empty
                    : DiffViewText.Field($"**Summary:** {summary}"))
            {
                BreakingChanges = ChangeRows(ChangeClassification.Breaking),
                PotentiallyBreakingChanges = ChangeRows(ChangeClassification.PotentiallyBreaking),
                AdditiveChanges = ChangeRows(ChangeClassification.Additive),
                NonBreakingChanges = ChangeRows(ChangeClassification.NonBreaking),
                OtherChanges = types.SelectMany(type => UnclassifiedRows(type)
                    .Select(row => new DiffChangeRow(
                        MarkObsolete(row.TypeText, type),
                        row.DetailText)))
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
                        MarkObsolete(change),
                        change.OldValue,
                        change.NewValue) is var row
                            ? row with { TypeNameText = MarkObsolete(row.TypeNameText, type) }
                            : null!)),
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
        List<ComparisonSubject<LibraryApiTypeDiff>> matched =
        [
            .. subjects.OrderBy(subject => subject.Display, StringComparer.Ordinal),
        ];
        // A Type-filtered comparison extracts only the selected Types, so it
        // cannot tell whether other Types changed; the note reports only that
        // the filter selected no changed Type.
        if (matched.Count == 0 && options.TypeFilter.Count > 0)
        {
            CommandError.WriteNote(
                $"type filter matched no changed types: {string.Join(", ", options.TypeFilter)}.");
        }

        // An API axis comparison shows the ordinary population: a row is shown
        // when either side is ordinary, and the note counts the rows the other
        // selections keep that are outside it
        // (docs/design/library-api-diff-presentation.md#ordinary-presentation).
        bool ordinaryOnly = document.PresentsOrdinaryPopulation;
        bool classified = options.Breaking || options.Additive;
        int outside = 0;
        List<SelectedType> types = [];
        foreach (ComparisonSubject<LibraryApiTypeDiff> subject in matched)
        {
            LibraryApiCompatibilityChange[] kept =
            [
                .. subject.Comparison.CompatibilityChanges.Where(change =>
                    !classified
                    || options.Breaking && change.Classification == ChangeClassification.Breaking
                    || options.Additive && change.Classification == ChangeClassification.Additive),
            ];
            var type = new SelectedType(
                subject,
                [.. kept.Where(change => !ordinaryOnly || change.IsOrdinary)],
                IncludeUnclassified: !classified,
                OrdinaryOnly: ordinaryOnly);
            if (ordinaryOnly)
            {
                outside += kept.Count(change => !change.IsOrdinary)
                    + UnclassifiedCandidates(type).Count(candidate => !candidate.IsOrdinary);
            }
            if (!type.Changes.IsEmpty || UnclassifiedRows(type).Any())
                types.Add(type);
        }
        if (classified && matched.Count > 0 && types.Count == 0)
            CommandError.WriteNote("classification filter removed all changes after type/member filters.");
        if (outside > 0)
        {
            CommandError.WriteNote(
                $"{outside} API change {(outside == 1 ? "row is" : "rows are")} outside the ordinary "
                    + "population (protected or hidden declarations) and not shown; "
                    + "unprojected --json reports the complete comparison.");
        }
        return types;
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
                MarkObsolete(change),
                change.OldValue,
                change.NewValue) is var row
                    ? row with { TypeText = MarkObsolete(row.TypeText, type) }
                    : null!)
            .Concat(UnclassifiedRows(type).Select(row =>
                row with { TypeText = MarkObsolete(row.TypeText, type) }));

    static IEnumerable<DiffDetailedChangeRow> UnclassifiedRows(SelectedType type)
        => UnclassifiedCandidates(type)
            .Where(candidate => !type.OrdinaryOnly || candidate.IsOrdinary)
            .Select(candidate => candidate.Row);

    static IEnumerable<(DiffDetailedChangeRow Row, bool IsOrdinary)> UnclassifiedCandidates(
        SelectedType type)
    {
        if (!type.IncludeUnclassified)
            yield break;

        foreach (LibraryApiUnclassifiedEvidence evidence in
            LibraryApiOrdinaryPresentation.UnclassifiedEvidence(type.Subject.Comparison))
        {
            yield return (
                evidence.Relation is not { } relation
                    ? Unclassified(
                        "", "TypeDefinitionChanged",
                        "Type definition changed without a compatibility classification.")
                    : Unclassified(
                        relation.Before?.Anchor.StableSelector
                            ?? relation.After!.Anchor.StableSelector,
                        $"Member{relation.PairKind}",
                        $"Member {relation.PairKind.ToString().ToLowerInvariant()} without a compatibility classification."),
                evidence.IsOrdinary);
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

    // The compact obsolete marker (D4): a Type column names an obsolete Type, and a row's
    // detail names an obsolete Member, by the attribute's own C# spelling.
    const string ObsoleteMarker = "[Obsolete]";

    static bool IsObsolete(SelectedType type)
        => (type.Subject.Comparison.After ?? type.Subject.Comparison.Before)!
            .Status.IsObsolete;

    static string MarkObsolete(string typeText, SelectedType type)
        => IsObsolete(type) ? $"{typeText} {ObsoleteMarker}" : typeText;

    static InertString MarkObsolete(InertString typeText, SelectedType type)
        => IsObsolete(type)
            ? InertString.Format(TextPolicy.Field, $"{typeText} {ObsoleteMarker}")
            : typeText;

    // An obsolete-status row already says so; only other rows mark an obsolete Member.
    static InertString MarkObsolete(LibraryApiCompatibilityChange change)
        => change.Subject.Kind == ApiChangeSubjectKind.Member
            && change.State != LibraryApiChangeState.Obsoleted
            && (change.Subject.AfterMember ?? change.Subject.BeforeMember)?.Status.IsObsolete
                == true
                ? InertString.Format(TextPolicy.Field, $"{ObsoleteMarker} {change.Message}")
                : change.Message;

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
                rows.Count(change => change.Classification == ChangeClassification.PotentiallyBreaking),
                rows.Count(change => change.Classification == ChangeClassification.NonBreaking));
    }

    static void WriteJson(
        DiffDetailedChangesView changes,
        string name,
        string beforeVersion,
        string afterVersion)
    {
        DiffDocumentView document = DiffOutputFormatter.BuildDocumentView(
            name, beforeVersion, afterVersion,
            changes, null, null, []);
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
        bool IncludeUnclassified = true,
        bool OrdinaryOnly = false);
}
