using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using InertText;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One literal occurrence and the exact selected implementation asset that
/// produced it.
/// </summary>
public sealed record PackageAssemblySemanticQueryOccurrence(
    PackageAssemblySelectedAssetContext SelectedAsset,
    StringLiteralUseOccurrence Evidence);

/// <summary>One package whose selected implementation libraries matched.</summary>
public sealed record PackageAssemblySemanticQueryResult
{
    internal PackageAssemblySemanticQueryResult(
        PackageAssemblySemanticFindCandidateOutcome.Matched outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        CandidateOrdinal = outcome.CandidateOrdinal;
        Coordinate = outcome.Coordinate;
        Correspondence = outcome.Correspondence;
        LibraryOccurrences =
        [
            .. outcome.Evaluations
                .OfType<PackageAssemblyEvaluationOutcome.Matched>()
                .SelectMany(evaluation =>
                {
                    PackageAssemblySelectedAssetContext selectedAsset =
                        evaluation.SelectedAsset
                        ?? throw new InvalidOperationException(
                            "A matched library must retain its selected asset.");
                    return evaluation.Evidence.Occurrences.Select(occurrence =>
                        new PackageAssemblySemanticQueryOccurrence(
                            selectedAsset,
                            occurrence));
                }),
        ];
        if (LibraryOccurrences.IsEmpty)
        {
            throw new ArgumentException(
                "A matched package requires at least one literal occurrence.",
                nameof(outcome));
        }
        SelectedAsset = LibraryOccurrences[0].SelectedAsset;
        Occurrences =
        [
            .. LibraryOccurrences.Select(value => value.Evidence),
        ];
        EvaluatedLibraryCount = outcome.Evaluations.Length;
        MatchedLibraryCount =
            outcome.Evaluations.Count(value =>
                value is PackageAssemblyEvaluationOutcome.Matched);
    }

    internal PackageAssemblySemanticQueryResult(
        PackageAssemblySemanticFindCandidateOutcome.Matched outcome,
        IEnumerable<PackageAssemblySemanticFindResult> results)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(results);

        CandidateOrdinal = outcome.CandidateOrdinal;
        Coordinate = outcome.Coordinate;
        Correspondence = outcome.Correspondence;
        LibraryOccurrences =
        [
            .. results.Select(result =>
            {
                if (result.CandidateOrdinal != CandidateOrdinal
                    || result.Coordinate != Coordinate
                    || result.Correspondence != Correspondence)
                {
                    throw new ArgumentException(
                        "Matched package occurrences must retain candidate correspondence.",
                        nameof(results));
                }
                return new PackageAssemblySemanticQueryOccurrence(
                    result.SelectedAsset,
                    result.Evidence);
            }),
        ];
        if (LibraryOccurrences.IsEmpty)
        {
            throw new ArgumentException(
                "A matched package requires at least one literal occurrence.",
                nameof(results));
        }
        SelectedAsset = LibraryOccurrences[0].SelectedAsset;
        Occurrences =
        [
            .. LibraryOccurrences.Select(value => value.Evidence),
        ];
        EvaluatedLibraryCount = outcome.Evaluations.Length;
        MatchedLibraryCount =
            outcome.Evaluations.Count(value =>
                value is PackageAssemblyEvaluationOutcome.Matched);
    }

    public int CandidateOrdinal { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageAcquisitionCandidateCorrespondence Correspondence { get; }

    /// <summary>
    /// The first matching implementation Library. Complete occurrence
    /// provenance is retained by <see cref="LibraryOccurrences"/>.
    /// </summary>
    public PackageAssemblySelectedAssetContext SelectedAsset { get; }

    public PackageRootReacquisitionRequest RootRequest =>
        SelectedAsset.Subject.RootRequest;

    public ImmutableArray<PackageAssemblySemanticQueryOccurrence>
        LibraryOccurrences { get; }

    public ImmutableArray<StringLiteralUseOccurrence> Occurrences { get; }

    public int EvaluatedLibraryCount { get; }

    public int MatchedLibraryCount { get; }
}

/// <summary>
/// Resource-free failure evidence for one package-query candidate acquisition.
/// </summary>
public sealed record PackageAssemblySemanticQueryAcquisitionFailure(
    ImmutableArray<PackageAuthorityFailure> Failures,
    ImmutableArray<InertString> NotFoundAuthorities)
{
    internal static PackageAssemblySemanticQueryAcquisitionFailure From(
        PackageAssemblySemanticFindAcquisitionFailure evidence) =>
        new(evidence.Failures, evidence.NotFoundAuthorities);
}

/// <summary>Why one admitted package failed semantic qualification.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryFailureReason.Acquisition),
    "acquisition")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryFailureReason.Evaluation),
    "evaluation")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryFailureReason.AggregateOccurrenceLimit),
    "aggregateOccurrenceLimit")]
public abstract record PackageAssemblySemanticQueryFailureReason
{
    private protected PackageAssemblySemanticQueryFailureReason()
    {
    }

    public sealed record Acquisition(
        PackageAssemblySemanticQueryAcquisitionFailure Evidence)
        : PackageAssemblySemanticQueryFailureReason;

    public sealed record Evaluation(
        PackageAssemblyEvaluationOutcome.Failure Evidence)
        : PackageAssemblySemanticQueryFailureReason;

    public sealed record AggregateOccurrenceLimit(
        int MaximumOccurrences,
        long ObservedOccurrences,
        int EvaluatedLibraries,
        int SelectedLibraries)
        : PackageAssemblySemanticQueryFailureReason;
}

/// <summary>Why one admitted candidate did not enter semantic evaluation.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryNonEvaluationReason.OperationDeadline),
    "operationDeadline")]
public abstract record PackageAssemblySemanticQueryNonEvaluationReason
{
    private protected PackageAssemblySemanticQueryNonEvaluationReason()
    {
    }

    public sealed record OperationDeadline
        : PackageAssemblySemanticQueryNonEvaluationReason
    {
        internal OperationDeadline(PackageSourceTimeout timeout)
        {
            ArgumentNullException.ThrowIfNull(timeout);
            if (timeout.Kind != PackageSourceTimeoutKind.Operation)
            {
                throw new ArgumentException(
                    "A semantic non-evaluation deadline must describe the complete operation.",
                    nameof(timeout));
            }

            Timeout = timeout;
        }

        public PackageSourceTimeout Timeout { get; }
    }
}

/// <summary>One terminal outcome for one admitted package candidate.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.Matched),
    "matched")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.NoMatch),
    "noMatch")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.NotApplicable),
    "notApplicable")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.Failure),
    "failure")]
[JsonDerivedType(
    typeof(PackageAssemblySemanticQueryCandidateOutcome.NotEvaluated),
    "notEvaluated")]
public abstract record PackageAssemblySemanticQueryCandidateOutcome
{
    private protected PackageAssemblySemanticQueryCandidateOutcome(
        int candidateOrdinal,
        PackageSourceCoordinate coordinate,
        PackageAcquisitionCandidateCorrespondence correspondence,
        ImmutableArray<PackageAssemblyEvaluationOutcome> libraryEvaluations)
    {
        if (libraryEvaluations.IsDefault)
        {
            throw new ArgumentException(
                "Library evaluations must be initialized.",
                nameof(libraryEvaluations));
        }
        CandidateOrdinal = candidateOrdinal;
        Coordinate = coordinate;
        Correspondence = correspondence;
        LibraryEvaluations = libraryEvaluations;
    }

    public int CandidateOrdinal { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageAcquisitionCandidateCorrespondence Correspondence { get; }

    public ImmutableArray<PackageAssemblyEvaluationOutcome> LibraryEvaluations
        { get; }

    private protected PackageAssemblySemanticQueryCandidateOutcome(
        int candidateOrdinal,
        PackageAcquisitionCandidate candidate,
        ImmutableArray<PackageAssemblyEvaluationOutcome> libraryEvaluations)
        : this(
            candidateOrdinal,
            candidate?.Coordinate
                ?? throw new ArgumentNullException(nameof(candidate)),
            candidate.Correspondence,
            libraryEvaluations)
    {
    }

    public sealed record Matched : PackageAssemblySemanticQueryCandidateOutcome
    {
        internal Matched(
            PackageAssemblySemanticQueryResult result,
            ImmutableArray<PackageAssemblyEvaluationOutcome> libraryEvaluations)
            : base(
                result.CandidateOrdinal,
                result.Coordinate,
                result.Correspondence,
                libraryEvaluations) =>
            Result = result;

        public PackageAssemblySemanticQueryResult Result { get; }
    }

    public sealed record NoMatch : PackageAssemblySemanticQueryCandidateOutcome
    {
        internal NoMatch(
            PackageAssemblySemanticFindCandidateOutcome.NoMatch outcome)
            : base(
                outcome.CandidateOrdinal,
                outcome.Coordinate,
                outcome.Correspondence,
                outcome.Evaluations) =>
            Evaluation = outcome.Evaluation;

        public PackageAssemblyEvaluationOutcome.NoMatch Evaluation { get; }
    }

    public sealed record NotApplicable
        : PackageAssemblySemanticQueryCandidateOutcome
    {
        internal NotApplicable(
            PackageAssemblySemanticFindCandidateOutcome.NotApplicable outcome)
            : base(
                outcome.CandidateOrdinal,
                outcome.Coordinate,
                outcome.Correspondence,
                outcome.Evaluations) =>
            Evaluation = outcome.Evaluation;

        public PackageAssemblyEvaluationOutcome.NotApplicable Evaluation
        { get; }
    }

    public sealed record Failure : PackageAssemblySemanticQueryCandidateOutcome
    {
        internal Failure(
            PackageAssemblySemanticFindCandidateOutcome.Failure outcome)
            : base(
                outcome.CandidateOrdinal,
                outcome.Coordinate,
                outcome.Correspondence,
                outcome.Evaluations)
        {
            Reason = outcome.Reason switch
            {
                PackageAssemblySemanticFindFailureReason.Acquisition
                    acquisition =>
                    new PackageAssemblySemanticQueryFailureReason.Acquisition(
                        PackageAssemblySemanticQueryAcquisitionFailure.From(
                            acquisition.Evidence)),
                PackageAssemblySemanticFindFailureReason.Evaluation evaluation =>
                    new PackageAssemblySemanticQueryFailureReason.Evaluation(
                        evaluation.Evidence),
                PackageAssemblySemanticFindFailureReason
                    .AggregateOccurrenceLimit aggregateLimit =>
                    new PackageAssemblySemanticQueryFailureReason
                        .AggregateOccurrenceLimit(
                            aggregateLimit.MaximumOccurrences,
                            aggregateLimit.ObservedOccurrences,
                            aggregateLimit.Evaluations.Length,
                            aggregateLimit.SelectedLibraries),
                _ => throw new InvalidOperationException(
                    "Unknown package assembly-semantic failure."),
            };
        }

        public PackageAssemblySemanticQueryFailureReason Reason { get; }
    }

    public sealed record NotEvaluated
        : PackageAssemblySemanticQueryCandidateOutcome
    {
        internal NotEvaluated(
            int candidateOrdinal,
            PackageAcquisitionCandidate candidate,
            PackageAssemblySemanticQueryNonEvaluationReason reason)
            : base(candidateOrdinal, candidate, []) =>
            Reason = reason
                ?? throw new ArgumentNullException(nameof(reason));

        public PackageAssemblySemanticQueryNonEvaluationReason Reason { get; }
    }

    internal static PackageAssemblySemanticQueryCandidateOutcome From(
        PackageAssemblySemanticFindCandidateOutcome outcome) =>
        outcome switch
        {
            PackageAssemblySemanticFindCandidateOutcome.Matched matched =>
                new Matched(
                    new PackageAssemblySemanticQueryResult(matched),
                    matched.Evaluations),
            PackageAssemblySemanticFindCandidateOutcome.NoMatch noMatch =>
                new NoMatch(noMatch),
            PackageAssemblySemanticFindCandidateOutcome.NotApplicable
                notApplicable =>
                new NotApplicable(notApplicable),
            PackageAssemblySemanticFindCandidateOutcome.Failure failure =>
                new Failure(failure),
            _ => throw new InvalidOperationException(
                "Unknown package assembly-semantic candidate outcome."),
        };

    internal static PackageAssemblySemanticQueryCandidateOutcome From(
        PackageAssemblySemanticFindCandidateOutcome outcome,
        IEnumerable<PackageAssemblySemanticFindResult> results) =>
        outcome switch
        {
            PackageAssemblySemanticFindCandidateOutcome.Matched matched =>
                new Matched(
                    new PackageAssemblySemanticQueryResult(matched, results),
                    matched.Evaluations),
            PackageAssemblySemanticFindCandidateOutcome.NoMatch noMatch =>
                new NoMatch(noMatch),
            PackageAssemblySemanticFindCandidateOutcome.NotApplicable
                notApplicable =>
                new NotApplicable(notApplicable),
            PackageAssemblySemanticFindCandidateOutcome.Failure failure =>
                new Failure(failure),
            _ => throw new InvalidOperationException(
                "Unknown package assembly-semantic candidate outcome."),
        };
}

/// <summary>Completion facts over the package query's admitted population.</summary>
public sealed record PackageAssemblySemanticQueryCompletion(
    PackageAcquisitionPopulationCompletionKind Population,
    bool IsRequestedPopulationComplete,
    bool AllCandidatesHaveTerminalOutcomes,
    bool HasFailures,
    bool IsSemanticEvaluationComplete,
    bool IsOperationDeadlineExpired)
{
    internal static PackageAssemblySemanticQueryCompletion From(
        PackageAssemblySemanticFindCompletion completion,
        PackageAcquisitionPopulation population) =>
        new(
            completion.Population,
            completion.IsRequestedPopulationComplete,
            completion.AllCandidatesCompleted,
            completion.HasFailures || !population.Failures.IsEmpty,
            completion.IsSemanticEvaluationComplete,
            OperationDeadline(population) is not null)
        {
            MatchLimit = completion.MatchLimit,
            IsMatchLimitReached = completion.MatchLimitReached,
        };

    internal static PackageSourceTimeout? OperationDeadline(
        PackageAcquisitionPopulation population) =>
        population.Failures
            .Select(failure => failure.Failure)
            .Where(failure =>
                failure.Kind == PackageAuthorityFailureKind.Timeout
                && failure.Timeout?.Kind
                    == PackageSourceTimeoutKind.Operation)
            .Select(failure => failure.Timeout)
            .FirstOrDefault();

    public int? MatchLimit { get; init; }

    public bool IsMatchLimitReached { get; init; }
}

/// <summary>
/// Completed package-grain semantic qualification over one admitted
/// package population.
/// </summary>
public sealed class PackageAssemblySemanticQueryDocument
{
    internal PackageAssemblySemanticQueryDocument(
        PackageAssemblySemanticFindDocument evidence,
        IReadOnlyList<PackageAssemblySemanticQueryCandidateOutcome>?
            projectedOutcomes = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (projectedOutcomes is not null
            && projectedOutcomes.Count != evidence.CandidateOutcomes.Length)
        {
            throw new ArgumentException(
                "Projected candidate outcomes must cover the completed evidence.",
                nameof(projectedOutcomes));
        }

        Population = evidence.Population;
        CandidateOutcomes = projectedOutcomes is null
            ?
            [
                .. evidence.CandidateOutcomes.Select(outcome =>
                    PackageAssemblySemanticQueryCandidateOutcome.From(
                        outcome,
                        evidence.Results.Where(result =>
                            result.CandidateOrdinal
                                == outcome.CandidateOrdinal))),
            ]
            : [.. projectedOutcomes];
        Results =
        [
            .. CandidateOutcomes
                .OfType<
                    PackageAssemblySemanticQueryCandidateOutcome.Matched>()
                .Select(outcome => outcome.Result),
        ];
        CandidateCount = evidence.CandidateCount;
        EvaluatedCandidateCount = evidence.CandidateCount;
        NotEvaluatedCount = 0;
        MatchedPackageCount = Results.Length;
        OccurrenceCount = evidence.OccurrenceCount;
        SemanticMissCount = evidence.SemanticMissCount;
        NotApplicableCount = evidence.NotApplicableCount;
        FailureCount = evidence.FailureCount;
        Completion =
            PackageAssemblySemanticQueryCompletion.From(
                evidence.Completion,
                evidence.Population);
    }

    internal PackageAssemblySemanticQueryDocument(
        PackageAcquisitionPopulation population,
        PackageSourceTimeout operationDeadline)
    {
        ArgumentNullException.ThrowIfNull(population);
        var reason =
            new PackageAssemblySemanticQueryNonEvaluationReason
                .OperationDeadline(operationDeadline);

        Population = population;
        CandidateOutcomes =
        [
            .. population.Candidates.Select(
                (candidate, index) =>
                    (PackageAssemblySemanticQueryCandidateOutcome)
                    new PackageAssemblySemanticQueryCandidateOutcome
                        .NotEvaluated(
                            index + 1,
                            candidate,
                            reason)),
        ];
        Results = [];
        CandidateCount = population.Candidates.Length;
        EvaluatedCandidateCount = 0;
        NotEvaluatedCount = CandidateCount;
        MatchedPackageCount = 0;
        OccurrenceCount = 0;
        SemanticMissCount = 0;
        NotApplicableCount = 0;
        FailureCount = 0;
        Completion = new(
            population.Completion,
            population.IsRequestedPopulationComplete,
            AllCandidatesHaveTerminalOutcomes: true,
            HasFailures: true,
            IsSemanticEvaluationComplete: false,
            IsOperationDeadlineExpired: true);
    }

    public PackageAcquisitionPopulation Population { get; }

    public ImmutableArray<PackageAssemblySemanticQueryResult> Results { get; }

    public ImmutableArray<PackageAssemblySemanticQueryCandidateOutcome>
        CandidateOutcomes
    { get; }

    public int CandidateCount { get; }

    public int EvaluatedCandidateCount { get; }

    public int NotEvaluatedCount { get; }

    public int MatchedPackageCount { get; }

    public int OccurrenceCount { get; }

    public int SemanticMissCount { get; }

    public int NotApplicableCount { get; }

    public int FailureCount { get; }

    public PackageAssemblySemanticQueryCompletion Completion { get; }
}

/// <summary>
/// Receives candidate outcomes established before terminal package-query
/// settlement.
/// </summary>
public interface IPackageAssemblySemanticQueryNonterminalSink
{
    ValueTask ReportAsync(
        PackageAssemblySemanticQueryCandidateOutcome outcome,
        CancellationToken cancellationToken);
}
