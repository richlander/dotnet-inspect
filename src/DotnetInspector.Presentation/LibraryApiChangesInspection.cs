using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Inspector.Findings;
using Markout;

namespace DotnetInspector.Presentation;

internal readonly record struct LibraryApiDiffTypeEvidence(
    string Identifier,
    string Display,
    LibraryApiTypeDiff Comparison);

public sealed record LibraryApiChangesSelection(
    ApiTypeSelection? Types = null,
    ImmutableArray<ChangeClassification> Classifications = default,
    MarkoutRowWindow? Rows = null);

public sealed record LibraryApiTypeChangeRow(
    string Identifier,
    string Display,
    LibraryApiTypeDiff Comparison,
    ImmutableArray<LibraryApiCompatibilityChange> Changes,
    ImmutableArray<LibraryApiUnclassifiedEvidence> Unclassified);

public sealed record LibraryApiChangedType(
    string Identifier,
    string Display,
    LibraryApiTypeIdentity? Before,
    LibraryApiTypeIdentity? After);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "rowKind")]
[JsonDerivedType(typeof(LibraryApiChangeRow.Classified), "classified")]
[JsonDerivedType(typeof(LibraryApiChangeRow.Unclassified), "unclassified")]
public abstract record LibraryApiChangeRow(LibraryApiChangedType Type)
{
    public sealed record Classified(
        LibraryApiChangedType Type,
        LibraryApiCompatibilityChange Change) : LibraryApiChangeRow(Type);

    public sealed record Unclassified(
        LibraryApiChangedType Type,
        LibraryApiUnclassifiedEvidence Evidence) : LibraryApiChangeRow(Type);
}

public sealed record LibraryApiChangesTable<TRow>(ImmutableArray<TRow> Rows);

public sealed record LibraryApiChangesDocument<TRow>(
    LibraryApiDiffEndpointSummary Before,
    LibraryApiDiffEndpointSummary After,
    LibraryApiDiffSummary Summary,
    ComparisonDocument<LibraryApiTypeDiff> Comparison,
    ImmutableArray<TRow> Rows,
    int OutsideOrdinaryRowCount)
{
    public string SummaryScope => "selectedTypesBeforeRowWindow";
}

public abstract record LibraryApiChangesOutcome<T>
{
    private LibraryApiChangesOutcome() { }

    public sealed record Available(T Content) : LibraryApiChangesOutcome<T>;
    public sealed record Unavailable(LibraryApiDiffOutcome.Unavailable Failure)
        : LibraryApiChangesOutcome<T>;
    public sealed record Rejected(LibraryApiDiffOutcome.Rejected Failure)
        : LibraryApiChangesOutcome<T>;
}

public static class LibraryApiChangesInspection
{
    public static InspectionEnvelope<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiTypeChangeRow>>>
        TypeTable(AssemblyContextApiComparisonResult result, LibraryApiChangesSelection selection)
        => Execute(result, selection, static (_, _, _, rows, _, _) =>
            new LibraryApiChangesTable<LibraryApiTypeChangeRow>(rows));

    public static InspectionEnvelope<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>>
        ChangesTable(AssemblyContextApiComparisonResult result, LibraryApiChangesSelection selection)
        => Execute(result, selection, static (_, _, _, rows, _, _) =>
            new LibraryApiChangesTable<LibraryApiChangeRow>(rows), DetailedRows);

    public static InspectionEnvelope<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiTypeChangeRow>>>
        TypeDocument(AssemblyContextApiComparisonResult result, LibraryApiChangesSelection selection)
        => Execute(result, selection, static (before, after, types, rows, all, outside) =>
            new LibraryApiChangesDocument<LibraryApiTypeChangeRow>(
                before, after, Summarize(all, Ordinary(before, after)),
                LibraryApiDiffPresentationAdapter.CreateComparison(before, types), rows, outside));

    public static InspectionEnvelope<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiChangeRow>>>
        ChangesDocument(AssemblyContextApiComparisonResult result, LibraryApiChangesSelection selection)
        => Execute(result, selection, static (before, after, types, rows, all, outside) =>
            new LibraryApiChangesDocument<LibraryApiChangeRow>(
                before, after, Summarize(all, Ordinary(before, after)),
                LibraryApiDiffPresentationAdapter.CreateComparison(before, types), rows, outside), DetailedRows);

    private static InspectionEnvelope<LibraryApiChangesOutcome<T>> Execute<T>(
        AssemblyContextApiComparisonResult result,
        LibraryApiChangesSelection selection,
        Func<LibraryApiDiffEndpointSummary, LibraryApiDiffEndpointSummary,
            ImmutableArray<LibraryApiDiffTypeEvidence>, ImmutableArray<LibraryApiTypeChangeRow>,
            ImmutableArray<LibraryApiTypeChangeRow>, int, T> create)
        => Execute(result, selection, create, static rows => rows);

    private static InspectionEnvelope<LibraryApiChangesOutcome<T>> Execute<T, TRow>(
        AssemblyContextApiComparisonResult result,
        LibraryApiChangesSelection selection,
        Func<LibraryApiDiffEndpointSummary, LibraryApiDiffEndpointSummary,
            ImmutableArray<LibraryApiDiffTypeEvidence>, ImmutableArray<TRow>,
            ImmutableArray<LibraryApiTypeChangeRow>, int, T> create,
        Func<ImmutableArray<LibraryApiTypeChangeRow>, ImmutableArray<TRow>> project)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var diagnostics = ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        LibraryApiChangesOutcome<T> content = LibraryApiDiffPresentationAdapter.Create<LibraryApiChangesOutcome<T>>(
            result,
            (before, after, evidence) =>
            {
                var (types, outside) = Select(
                    evidence, Ordinary(before, after), selection, diagnostics);
                ImmutableArray<TRow> rows = project(types);
                var (start, end) = selection.Rows?.Resolve(rows.Length) ?? (0, rows.Length);
                return new LibraryApiChangesOutcome<T>.Available(create(
                    before, after, evidence,
                    rows[start..end], types, outside));
            },
            static unavailable => new LibraryApiChangesOutcome<T>.Unavailable(unavailable),
            static rejected => new LibraryApiChangesOutcome<T>.Rejected(rejected));
        return new(content,
            new InspectionShare.NonProjectable(
                "comparison/endpoints",
                "Workspace Share does not yet represent a selected-Library comparison's ordered endpoints and API scope."),
            diagnostics.ToImmutable());
    }

    private static bool Ordinary(
        LibraryApiDiffEndpointSummary before, LibraryApiDiffEndpointSummary after)
        => before.Scope == ApiSurfaceScope.ApiAxis && after.Scope == ApiSurfaceScope.ApiAxis;

    private static (ImmutableArray<LibraryApiTypeChangeRow> Rows, int OutsideRowCount) Select(
        ImmutableArray<LibraryApiDiffTypeEvidence> evidence,
        bool ordinaryOnly,
        LibraryApiChangesSelection selection,
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics)
    {
        LibraryApiDiffTypeEvidence[] matched =
        [
            .. evidence
                .Where(subject => selection.Types is null || selection.Types.MatchesFullName(subject.Display))
                .OrderBy(subject => subject.Display, StringComparer.Ordinal)
                .ThenBy(subject => subject.Identifier, StringComparer.Ordinal),
        ];
        if (matched.Length == 0 && selection.Types is not null)
            Note("diff.type-selection-empty", "type filter matched no changed types.");

        bool classified = !selection.Classifications.IsDefaultOrEmpty;
        int outside = 0;
        var rows = ImmutableArray.CreateBuilder<LibraryApiTypeChangeRow>();
        foreach (LibraryApiDiffTypeEvidence subject in matched)
        {
            LibraryApiCompatibilityChange[] changes =
            [
                .. subject.Comparison.CompatibilityChanges.Where(change =>
                    !classified || selection.Classifications.Contains(change.Classification)),
            ];
            LibraryApiUnclassifiedEvidence[] unclassified = classified
                ? [] : [.. LibraryApiOrdinaryPresentation.UnclassifiedEvidence(subject.Comparison)];
            if (ordinaryOnly)
                outside += changes.Count(change => !change.IsOrdinary)
                    + unclassified.Count(evidence => !evidence.IsOrdinary);
            var row = new LibraryApiTypeChangeRow(
                subject.Identifier, subject.Display, subject.Comparison,
                [.. changes.Where(change => !ordinaryOnly || change.IsOrdinary)],
                [.. unclassified.Where(evidence => !ordinaryOnly || evidence.IsOrdinary)]);
            if (!row.Changes.IsEmpty || !row.Unclassified.IsEmpty)
                rows.Add(row);
        }
        if (classified && matched.Length > 0 && rows.Count == 0)
            Note("diff.classification-selection-empty",
                "classification filter removed all changes after type/member filters.");
        if (outside > 0)
            Note("diff.outside-ordinary",
                $"{outside} API change {(outside == 1 ? "row is" : "rows are")} outside the ordinary "
                + "population (protected or hidden declarations) and not shown; "
                + "unprojected --json reports the complete comparison.");
        return (rows.ToImmutable(), outside);

        void Note(string code, string message)
            => diagnostics.Add(new(code, InspectionDiagnosticSeverity.Information, message));
    }

    public static ImmutableArray<LibraryApiChangeRow> DetailedRows(
        ImmutableArray<LibraryApiTypeChangeRow> types)
    {
        var rows = ImmutableArray.CreateBuilder<LibraryApiChangeRow>();
        foreach (LibraryApiTypeChangeRow type in types)
        {
            var identity = new LibraryApiChangedType(
                type.Identifier, type.Display, type.Comparison.Before, type.Comparison.After);
            foreach (LibraryApiCompatibilityChange change in type.Changes)
                rows.Add(new LibraryApiChangeRow.Classified(identity, change));
            foreach (LibraryApiUnclassifiedEvidence evidence in type.Unclassified)
                rows.Add(new LibraryApiChangeRow.Unclassified(identity, evidence));
        }
        return rows.ToImmutable();
    }

    internal static LibraryApiDiffSummary Summarize(
        IEnumerable<LibraryApiTypeChangeRow> types,
        bool ordinaryOnly)
    {
        LibraryApiTypeChangeRow[] rows = [.. types];
        // A compatibility occurrence can be associated with both Types of a correspondence.
        var changes = new HashSet<LibraryApiCompatibilityChange>(ReferenceEqualityComparer.Instance);
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (LibraryApiTypeChangeRow row in rows)
        {
            changes.UnionWith(row.Changes);
            foreach (LibraryApiMemberDiff member in row.Comparison.Members)
                if (!ordinaryOnly || member.Relation.IsOrdinary)
                    members.Add(member.Relation.Identifier);
        }
        return new(
            rows.Length,
            rows.Count(row => row.Comparison.PairKind == LibraryApiTypePairKind.Added),
            rows.Count(row => row.Comparison.PairKind == LibraryApiTypePairKind.Removed),
            members.Count,
            changes.Count(change => change.Classification == ChangeClassification.Breaking),
            changes.Count(change => change.Classification == ChangeClassification.Additive),
            changes.Count(change => change.Classification == ChangeClassification.PotentiallyBreaking),
            changes.Count(change => change.Classification == ChangeClassification.NonBreaking));
    }
}
