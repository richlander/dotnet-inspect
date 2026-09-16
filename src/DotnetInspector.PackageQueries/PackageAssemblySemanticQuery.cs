using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using InertText;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>One package whose selected implementation library matched.</summary>
public sealed record PackageAssemblySemanticQueryResult
{
    internal PackageAssemblySemanticQueryResult(
        PackageAssemblySemanticFindCandidateOutcome.Matched outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        CandidateOrdinal = outcome.CandidateOrdinal;
        Coordinate = outcome.Coordinate;
        Correspondence = outcome.Correspondence;
        SelectedAsset = outcome.Evaluation.SelectedAsset
            ?? throw new ArgumentException(
                "A matched package requires selected-asset evidence.",
                nameof(outcome));
        Occurrences = outcome.Evaluation.Evidence.Occurrences;
    }

    public int CandidateOrdinal { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageAcquisitionCandidateCorrespondence Correspondence { get; }

    public PackageAssemblySelectedAssetContext SelectedAsset { get; }

    public PackageRootReacquisitionRequest RootRequest =>
        SelectedAsset.Subject.RootRequest;

    public ImmutableArray<StringLiteralUseOccurrence> Occurrences { get; }
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
}

/// <summary>One terminal outcome for one admitted package candidate.</summary>
public abstract record PackageAssemblySemanticQueryCandidateOutcome
{
    private protected PackageAssemblySemanticQueryCandidateOutcome(
        int candidateOrdinal,
        PackageSourceCoordinate coordinate,
        PackageAcquisitionCandidateCorrespondence correspondence)
    {
        CandidateOrdinal = candidateOrdinal;
        Coordinate = coordinate;
        Correspondence = correspondence;
    }

    public int CandidateOrdinal { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageAcquisitionCandidateCorrespondence Correspondence { get; }

    public sealed record Matched : PackageAssemblySemanticQueryCandidateOutcome
    {
        internal Matched(PackageAssemblySemanticQueryResult result)
            : base(
                result.CandidateOrdinal,
                result.Coordinate,
                result.Correspondence) =>
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
                outcome.Correspondence) =>
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
                outcome.Correspondence) =>
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
                outcome.Correspondence)
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
                _ => throw new InvalidOperationException(
                    "Unknown package assembly-semantic failure."),
            };
        }

        public PackageAssemblySemanticQueryFailureReason Reason { get; }
    }

    internal static PackageAssemblySemanticQueryCandidateOutcome From(
        PackageAssemblySemanticFindCandidateOutcome outcome) =>
        outcome switch
        {
            PackageAssemblySemanticFindCandidateOutcome.Matched matched =>
                new Matched(new PackageAssemblySemanticQueryResult(matched)),
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
    bool AllCandidatesCompleted,
    bool HasFailures,
    bool IsSemanticEvaluationComplete)
{
    internal static PackageAssemblySemanticQueryCompletion From(
        PackageAssemblySemanticFindCompletion completion) =>
        new(
            completion.Population,
            completion.IsRequestedPopulationComplete,
            completion.AllCandidatesCompleted,
            completion.HasFailures,
            completion.IsSemanticEvaluationComplete);
}

/// <summary>
/// Completed package-grain semantic qualification over one admitted
/// package population.
/// </summary>
public sealed class PackageAssemblySemanticQueryDocument
{
    internal PackageAssemblySemanticQueryDocument(
        PackageAssemblySemanticFindDocument evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        Population = evidence.Population;
        CandidateOutcomes =
        [
            .. evidence.CandidateOutcomes.Select(
                PackageAssemblySemanticQueryCandidateOutcome.From),
        ];
        Results =
        [
            .. CandidateOutcomes
                .OfType<
                    PackageAssemblySemanticQueryCandidateOutcome.Matched>()
                .Select(outcome => outcome.Result),
        ];
        CandidateCount = evidence.CandidateCount;
        MatchedPackageCount = Results.Length;
        OccurrenceCount = evidence.OccurrenceCount;
        SemanticMissCount = evidence.SemanticMissCount;
        NotApplicableCount = evidence.NotApplicableCount;
        FailureCount = evidence.FailureCount;
        Completion =
            PackageAssemblySemanticQueryCompletion.From(evidence.Completion);
    }

    public PackageAcquisitionPopulation Population { get; }

    public ImmutableArray<PackageAssemblySemanticQueryResult> Results { get; }

    public ImmutableArray<PackageAssemblySemanticQueryCandidateOutcome>
        CandidateOutcomes
    { get; }

    public int CandidateCount { get; }

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
