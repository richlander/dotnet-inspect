using System.Collections.Immutable;
using QuerySpace;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Queries;

public sealed record LibraryQueryRequest(
    IReadOnlyCollection<PortableQueryTerm>? Terms = null,
    int MaximumCandidates = LibraryQuery.DefaultMaximumCandidates,
    RowSelectionIntent<string>? RowSelection = null);

public sealed record LibraryQueryReferencePredicate(
    string DisplayName,
    string NormalizedName);

public sealed class LibraryQueryPlan
{
    internal LibraryQueryPlan(
        PortableQueryIntent intent,
        ImmutableArray<LibraryQueryReferencePredicate> references,
        int maximumCandidates,
        RowSelectionIntent<string> rowSelection)
    {
        Intent = intent;
        References = references;
        MaximumCandidates = maximumCandidates;
        RowSelection = rowSelection;
    }

    public PortableQueryIntent Intent { get; }
    public ImmutableArray<LibraryQueryReferencePredicate> References { get; }
    public int MaximumCandidates { get; }
    public RowSelectionIntent<string> RowSelection { get; }
}

public abstract record LibraryQueryPlanResult
{
    private LibraryQueryPlanResult()
    {
    }

    public sealed record Accepted(LibraryQueryPlan Plan)
        : LibraryQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : LibraryQueryPlanResult;
}

public enum LibraryQueryFailureKind
{
    Population,
    Unreadable,
    InvalidImage,
    UnsupportedMetadataFormat,
    MissingAssemblyIdentity,
    IncompleteReferences,
}

public sealed record LibraryQueryFailure(
    InertString? Library,
    InertString? Path,
    InertString? Source,
    LibraryQueryFailureKind Kind,
    InertString Message);

[Flags]
public enum LibraryQueryIncompleteReason
{
    None = 0,
    CandidateLimit = 1,
    PopulationFailure = 2,
    EvaluationFailure = 4,
}

public sealed record LibraryQuerySummary(
    int PopulationCandidates,
    int CandidateLimit,
    int Candidates,
    int Matches,
    int Failures,
    LibraryQueryIncompleteReason IncompleteReasons)
{
    public bool IsComplete =>
        IncompleteReasons == LibraryQueryIncompleteReason.None;
}

public sealed record LibraryQueryMatch(
    InertString Library,
    InertString Path,
    InertString Source,
    InertString? Version,
    AssemblySetSourceKind SourceKind,
    InertString? TargetFramework,
    ImmutableArray<InertString> MatchedReferences);

public sealed record LibraryQueryDocument(
    ImmutableArray<LibraryQueryMatch> Results,
    ImmutableArray<LibraryQueryFailure> Failures,
    LibraryQuerySummary Summary)
{
    public bool HasLibraries => !Results.IsEmpty;
}

public static partial class LibraryQuery
{
    public const int DefaultMaximumCandidates = 256;
    public const int MaximumCandidates = 4_096;

    public static LibraryQueryPlanResult Plan(
        LibraryQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bounds = new[]
        {
            new PortableQueryBound(
                CandidatesDimension,
                request.MaximumCandidates),
        };
        PortableQueryIntent intent = PortableQueryIntent.Create(
            request.Terms is null ? [] : [.. request.Terms],
            bounds,
            ToPortableStages(request.RowSelection),
            []);
        return ResolveIntent(intent);
    }

    public static LibraryQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<LibraryQueryPlan> resolution =
            OperationRegistration.Route.Resolve(
                intent,
                cancellationToken);
        return resolution.IsResolved
            ? new LibraryQueryPlanResult.Accepted(resolution.Plan)
            : new LibraryQueryPlanResult.Rejected(resolution.Failure);
    }

    public static LibraryQueryDocument Execute(
        IReadOnlyList<AssemblySetEntry> population,
        IReadOnlyList<AssemblySetDiagnostic> populationDiagnostics,
        LibraryQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(populationDiagnostics);
        ArgumentNullException.ThrowIfNull(plan);

        var results = ImmutableArray.CreateBuilder<LibraryQueryMatch>();
        var failures = ImmutableArray.CreateBuilder<LibraryQueryFailure>();
        LibraryQueryIncompleteReason incomplete =
            LibraryQueryIncompleteReason.None;

        foreach (AssemblySetDiagnostic diagnostic in populationDiagnostics)
        {
            failures.Add(
                new(
                    null,
                    null,
                    null,
                    LibraryQueryFailureKind.Population,
                    Prose(diagnostic.Message)));
        }
        if (populationDiagnostics.Count > 0)
            incomplete |= LibraryQueryIncompleteReason.PopulationFailure;

        AssemblySetEntry[] ordered =
        [
            .. population
                .OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Path, StringComparer.Ordinal),
        ];
        int candidateCount = Math.Min(
            ordered.Length,
            plan.MaximumCandidates);
        if (candidateCount < ordered.Length)
            incomplete |= LibraryQueryIncompleteReason.CandidateLimit;

        for (int index = 0; index < candidateCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblySetEntry candidate = ordered[index];
            try
            {
                AssemblyIdentityNames names =
                    AssemblyIdentityScanner.Scan(candidate.Path);
                if (string.IsNullOrWhiteSpace(names.Name))
                {
                    failures.Add(Failure(
                        candidate,
                        library: null,
                        LibraryQueryFailureKind.MissingAssemblyIdentity,
                        "The candidate contains managed metadata but does not define an assembly identity."));
                    incomplete |=
                        LibraryQueryIncompleteReason.EvaluationFailure;
                    continue;
                }

                ImmutableArray<string> matched =
                    MatchReferences(plan.References, names.ReferenceNames);
                if (matched.Length == plan.References.Length)
                {
                    results.Add(
                        new(
                            Field(names.Name),
                            Field(candidate.Path),
                            Field(candidate.Source),
                            OptionalField(candidate.Version),
                            candidate.SourceKind,
                            OptionalField(candidate.Tfm),
                            [
                                .. matched.Select(Field),
                            ]));
                    continue;
                }

                if (!names.ReferencesComplete
                    && plan.References.Length > 0)
                {
                    failures.Add(Failure(
                        candidate,
                        names.Name,
                        LibraryQueryFailureKind.IncompleteReferences,
                        "The AssemblyRef table is incomplete, so missing requested references cannot be treated as absent."));
                    incomplete |=
                        LibraryQueryIncompleteReason.EvaluationFailure;
                }
            }
            catch (UnsupportedMetadataFormatException ex)
            {
                AddFailure(
                    candidate,
                    LibraryQueryFailureKind.UnsupportedMetadataFormat,
                    ex.Message);
            }
            catch (MalformedMetadataRootException ex)
            {
                AddFailure(
                    candidate,
                    LibraryQueryFailureKind.InvalidImage,
                    ex.Message);
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException)
            {
                AddFailure(
                    candidate,
                    LibraryQueryFailureKind.Unreadable,
                    ex.Message);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or InvalidOperationException
                    or ArgumentException
                    or NotSupportedException
                    or OverflowException
                    or IndexOutOfRangeException)
            {
                AddFailure(
                    candidate,
                    LibraryQueryFailureKind.InvalidImage,
                    ex.Message);
            }
        }

        return new(
            results.ToImmutable(),
            failures.ToImmutable(),
            new(
                ordered.Length,
                plan.MaximumCandidates,
                candidateCount,
                results.Count,
                failures.Count,
                incomplete));

        void AddFailure(
            AssemblySetEntry candidate,
            LibraryQueryFailureKind kind,
            string message)
        {
            failures.Add(Failure(
                candidate,
                library: null,
                kind,
                message));
            incomplete |= LibraryQueryIncompleteReason.EvaluationFailure;
        }
    }

    private static ImmutableArray<string> MatchReferences(
        ImmutableArray<LibraryQueryReferencePredicate> requested,
        ImmutableArray<string> observed)
    {
        if (requested.IsEmpty)
            return [];

        var matched = ImmutableArray.CreateBuilder<string>(
            requested.Length);
        foreach (LibraryQueryReferencePredicate predicate in requested)
        {
            string? reference = observed.FirstOrDefault(candidate =>
                candidate.Equals(
                    predicate.DisplayName,
                    StringComparison.OrdinalIgnoreCase));
            if (reference is null)
                break;
            matched.Add(reference);
        }
        return matched.ToImmutable();
    }

    private static LibraryQueryFailure Failure(
        AssemblySetEntry candidate,
        string? library,
        LibraryQueryFailureKind kind,
        string message) =>
        new(
            OptionalField(library),
            Field(candidate.Path),
            Field(candidate.Source),
            kind,
            Prose(message));

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);

    private static InertString Prose(string value) =>
        new(TextPolicy.Prose, value);

    private static InertString? OptionalField(string? value) =>
        string.IsNullOrEmpty(value) ? null : Field(value);

    private static IReadOnlyList<PortableQueryStage> ToPortableStages(
        RowSelectionIntent<string>? rowSelection) =>
        rowSelection is null
            ? []
            :
            [
                .. rowSelection.Operations.Select(operation =>
                    operation.Kind switch
                    {
                        RowSelectionStageKind.Head =>
                            PortableQueryStage.Head(operation.Count),
                        RowSelectionStageKind.Tail =>
                            PortableQueryStage.Tail(operation.Count),
                        RowSelectionStageKind.Window =>
                            PortableQueryStage.Window(
                                operation.Start,
                                operation.End),
                        RowSelectionStageKind.Top =>
                            PortableQueryStage.Top(operation.Count),
                        _ => throw new InvalidOperationException(
                            "Unknown row-selection stage."),
                    }),
            ];
}
