using System.Collections.Immutable;
using QuerySpace;
using QuerySpace.Rows;
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
    ResourceBudget,
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

/// <summary>
/// One caller-owned assembly-context participant and its product-issued
/// Library Query metadata.
/// </summary>
public sealed record LibraryQueryParticipant(
    AssemblyContextParticipant Participant,
    string Path,
    string Source,
    string? Version,
    AssemblySetSourceKind SourceKind,
    string? TargetFramework = null);

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
            PortableQueryRowSelection.ToStages(request.RowSelection),
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

        return ExecuteCore(
            population.Select(entry =>
                new Candidate<AssemblySetEntry>(
                    entry,
                    entry.Path,
                    entry.Source,
                    entry.Version,
                    entry.SourceKind,
                    entry.Tfm)),
            populationDiagnostics,
            plan,
            static candidate => Evaluate(candidate.Value),
            cancellationToken);
    }

    public static LibraryQueryDocument ExecuteParticipants(
        AssemblyContextGroup? group,
        IReadOnlyList<LibraryQueryParticipant> population,
        LibraryQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(plan);
        if (population.Count > 0)
            ArgumentNullException.ThrowIfNull(group);

        foreach (LibraryQueryParticipant candidate in population)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(candidate.Participant);
            ArgumentException.ThrowIfNullOrWhiteSpace(candidate.Path);
            ArgumentException.ThrowIfNullOrWhiteSpace(candidate.Source);
            if (group is null
                || !group.Participants.Any(participant =>
                    ReferenceEquals(
                        participant,
                        candidate.Participant)))
            {
                throw new ArgumentException(
                    "Every Library Query participant must belong to the supplied assembly context group.",
                    nameof(population));
            }
        }

        return ExecuteCore(
            population.Select(candidate =>
                new Candidate<LibraryQueryParticipant>(
                    candidate,
                    candidate.Path,
                    candidate.Source,
                    candidate.Version,
                    candidate.SourceKind,
                    candidate.TargetFramework)),
            [],
            plan,
            candidate => Evaluate(group!, candidate.Value),
            cancellationToken);
    }

    private static LibraryQueryDocument ExecuteCore<TValue>(
        IEnumerable<Candidate<TValue>> population,
        IReadOnlyList<AssemblySetDiagnostic> populationDiagnostics,
        LibraryQueryPlan plan,
        Func<Candidate<TValue>, CandidateEvaluation> evaluate,
        CancellationToken cancellationToken)
    {
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

        Candidate<TValue>[] ordered =
        [
            .. population
                .OrderBy(candidate => candidate.Source, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Path, StringComparer.Ordinal),
        ];
        int candidateCount = Math.Min(
            ordered.Length,
            plan.MaximumCandidates);
        if (candidateCount < ordered.Length)
            incomplete |= LibraryQueryIncompleteReason.CandidateLimit;

        for (int index = 0; index < candidateCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Candidate<TValue> candidate = ordered[index];
            switch (evaluate(candidate))
            {
                case CandidateEvaluation.Failed failed:
                    failures.Add(Failure(
                        candidate,
                        library: null,
                        failed.Kind,
                        failed.Message));
                    incomplete |=
                        LibraryQueryIncompleteReason.EvaluationFailure;
                    continue;

                case CandidateEvaluation.Available available:
                    if (string.IsNullOrWhiteSpace(available.Library))
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
                        MatchReferences(
                            plan.References,
                            available.ReferenceNames);
                    if (matched.Length == plan.References.Length)
                    {
                        results.Add(
                            new(
                                Field(available.Library),
                                Field(candidate.Path),
                                Field(candidate.Source),
                                OptionalField(candidate.Version),
                                candidate.SourceKind,
                                OptionalField(candidate.TargetFramework),
                                [
                                    .. matched.Select(Field),
                                ]));
                        continue;
                    }

                    if (!available.ReferencesComplete
                        && plan.References.Length > 0)
                    {
                        failures.Add(Failure(
                            candidate,
                            available.Library,
                            LibraryQueryFailureKind.IncompleteReferences,
                            "The AssemblyRef table is incomplete, so missing requested references cannot be treated as absent."));
                        incomplete |=
                            LibraryQueryIncompleteReason.EvaluationFailure;
                    }
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unknown Library Query candidate evaluation.");
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
    }

    private static CandidateEvaluation Evaluate(
        AssemblySetEntry candidate)
    {
        try
        {
            AssemblyIdentityNames names =
                AssemblyIdentityScanner.Scan(candidate.Path);
            return new CandidateEvaluation.Available(
                names.Name,
                names.ReferenceNames,
                names.ReferencesComplete);
        }
        catch (UnsupportedMetadataFormatException ex)
        {
            return new CandidateEvaluation.Failed(
                LibraryQueryFailureKind.UnsupportedMetadataFormat,
                ex.Message);
        }
        catch (MalformedMetadataRootException ex)
        {
            return new CandidateEvaluation.Failed(
                LibraryQueryFailureKind.InvalidImage,
                ex.Message);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException)
        {
            return new CandidateEvaluation.Failed(
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
            return new CandidateEvaluation.Failed(
                LibraryQueryFailureKind.InvalidImage,
                ex.Message);
        }
    }

    private static CandidateEvaluation Evaluate(
        AssemblyContextGroup group,
        LibraryQueryParticipant candidate)
    {
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>> entry =
            AssemblyContextReferencesQuery.ExecuteParticipant(
                group,
                candidate.Participant);
        return entry switch
        {
            AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Available available =>
                new CandidateEvaluation.Available(
                    candidate.Participant.Assembly.Identity.Name,
                    [
                        .. available.Value.Select(reference =>
                            reference.Name),
                    ],
                    ReferencesComplete: true),
            AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Rejected rejected =>
                new CandidateEvaluation.Failed(
                    FailureKind(rejected.Failure.Kind),
                    rejected.Failure.Detail),
            AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Failed failed =>
                new CandidateEvaluation.Failed(
                    FailureKind(failed.Error),
                    failed.Error.Message),
            _ => throw new InvalidOperationException(
                "Unknown assembly-reference query result."),
        };
    }

    private static LibraryQueryFailureKind FailureKind(
        CandidateOpenFailureKind kind) =>
        kind switch
        {
            CandidateOpenFailureKind.Unreadable =>
                LibraryQueryFailureKind.Unreadable,
            CandidateOpenFailureKind.InvalidImage =>
                LibraryQueryFailureKind.InvalidImage,
            CandidateOpenFailureKind.ResourceBudget =>
                LibraryQueryFailureKind.ResourceBudget,
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                LibraryQueryFailureKind.UnsupportedMetadataFormat,
            _ => throw new InvalidOperationException(
                "Unknown assembly-context candidate failure."),
        };

    private static LibraryQueryFailureKind FailureKind(
        Exception error) =>
        error switch
        {
            IOException or UnauthorizedAccessException =>
                LibraryQueryFailureKind.Unreadable,
            UnsupportedMetadataFormatException =>
                LibraryQueryFailureKind.UnsupportedMetadataFormat,
            _ => LibraryQueryFailureKind.InvalidImage,
        };

    private abstract record CandidateEvaluation
    {
        private CandidateEvaluation()
        {
        }

        internal sealed record Available(
            string? Library,
            ImmutableArray<string> ReferenceNames,
            bool ReferencesComplete)
            : CandidateEvaluation;

        internal sealed record Failed(
            LibraryQueryFailureKind Kind,
            string Message)
            : CandidateEvaluation;
    }

    private sealed record Candidate<TValue>(
        TValue Value,
        string Path,
        string Source,
        string? Version,
        AssemblySetSourceKind SourceKind,
        string? TargetFramework);

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

    private static LibraryQueryFailure Failure<TValue>(
        Candidate<TValue> candidate,
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

}
