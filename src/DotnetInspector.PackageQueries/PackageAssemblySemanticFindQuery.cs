using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using InertText;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>The finite limits for one package assembly-semantic Find operation.</summary>
public sealed class PackageAssemblySemanticFindBudget
{
    public static PackageAssemblySemanticFindBudget Default { get; } = new(
        new PackagePayloadLimits
        {
            MaxArchiveBytes = 32L * 1024 * 1024,
            MaxExpandedBytes = 256L * 1024 * 1024,
            MaxEntryCount = 4_096,
            MaxUniqueDirectories = 8_192,
        },
        PackageAssemblyEvaluationBudget.Default);

    public PackageAssemblySemanticFindBudget(
        PackagePayloadLimits payload,
        PackageAssemblyEvaluationBudget evaluation)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            payload.MaxArchiveBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            payload.MaxExpandedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            payload.MaxEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            payload.MaxUniqueDirectories);
        ArgumentNullException.ThrowIfNull(evaluation);

        Payload = payload;
        Evaluation = evaluation;
    }

    public PackagePayloadLimits Payload { get; }

    public PackageAssemblyEvaluationBudget Evaluation { get; }

    public TimeSpan MaximumDuration => Evaluation.MaximumDuration;
}

/// <summary>
/// Immutable resource-free input for one authority-bearing package
/// assembly-semantic Find operation.
/// </summary>
public sealed class PackageAssemblySemanticFindRequest
{
    public PackageAssemblySemanticFindRequest(
        PackageAcquisitionPopulation population,
        PackageHouseTargetContext target,
        PackageAssemblyPatternRequest pattern,
        PackageAssemblySemanticFindBudget? budget = null,
        int? maximumMatches = null)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Mode != PackageHouseTargetSelectionMode.Exact
            || target.RequestedFramework is null)
        {
            throw new ArgumentException(
                "Assembly-semantic Find requires one exact package target framework.",
                nameof(target));
        }
        ArgumentNullException.ThrowIfNull(pattern);
        if (maximumMatches is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumMatches));

        Population = population;
        Target = target;
        Pattern = pattern;
        Budget = budget ?? PackageAssemblySemanticFindBudget.Default;
        MaximumMatches = maximumMatches;
    }

    public PackageAcquisitionPopulation Population { get; }

    public PackageHouseTargetContext Target { get; }

    public PackageAssemblyPatternRequest Pattern { get; }

    public PackageAssemblySemanticFindBudget Budget { get; }

    public int? MaximumMatches { get; }
}

/// <summary>
/// Resource-free failure evidence for one candidate payload acquisition.
/// </summary>
public sealed record PackageAssemblySemanticFindAcquisitionFailure
{
    internal PackageAssemblySemanticFindAcquisitionFailure(
        ConfiguredPackagePayloadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Payload is not null)
        {
            throw new ArgumentException(
                "An acquisition failure cannot retain an available payload.",
                nameof(result));
        }

        Failures = [.. result.Failures];
        NotFoundAuthorities =
        [
            .. result.NotFoundAuthorities.Select(
                authority =>
                    PackageSourceDisplay.ForDiagnostics(authority.Source)),
        ];
    }

    public ImmutableArray<PackageAuthorityFailure> Failures { get; }

    public ImmutableArray<InertString> NotFoundAuthorities { get; }
}

/// <summary>Why one admitted candidate failed to produce semantic evidence.</summary>
public abstract record PackageAssemblySemanticFindFailureReason
{
    private protected PackageAssemblySemanticFindFailureReason()
    {
    }

    public sealed record Acquisition
        : PackageAssemblySemanticFindFailureReason
    {
        internal Acquisition(
            PackageAssemblySemanticFindAcquisitionFailure evidence) =>
            Evidence = evidence;

        public PackageAssemblySemanticFindAcquisitionFailure Evidence
            { get; }
    }

    public sealed record Evaluation
        : PackageAssemblySemanticFindFailureReason
    {
        internal Evaluation(
            ImmutableArray<PackageAssemblyEvaluationOutcome> evaluations)
        {
            if (evaluations.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "An aggregate evaluation failure requires initialized library outcomes.",
                    nameof(evaluations));
            }
            Evaluations = evaluations;
            Evidence = evaluations
                .OfType<PackageAssemblyEvaluationOutcome.Failure>()
                .FirstOrDefault()
                ?? throw new ArgumentException(
                    "An aggregate evaluation failure requires at least one failed library.",
                    nameof(evaluations));
        }

        public PackageAssemblyEvaluationOutcome.Failure Evidence { get; }

        public ImmutableArray<PackageAssemblyEvaluationOutcome> Evaluations
            { get; }
    }
}

/// <summary>One terminal outcome for one admitted package candidate.</summary>
public abstract record PackageAssemblySemanticFindCandidateOutcome
{
    private protected PackageAssemblySemanticFindCandidateOutcome(
        int candidateOrdinal,
        PackageAcquisitionCandidate candidate,
        ImmutableArray<PackageAssemblyEvaluationOutcome> evaluations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateOrdinal);
        ArgumentNullException.ThrowIfNull(candidate);
        if (evaluations.IsDefault)
        {
            throw new ArgumentException(
                "Library evaluations must be initialized.",
                nameof(evaluations));
        }

        CandidateOrdinal = candidateOrdinal;
        Coordinate = candidate.Coordinate;
        Correspondence = candidate.Correspondence;
        Evaluations = evaluations;
    }

    public int CandidateOrdinal { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageAcquisitionCandidateCorrespondence Correspondence
        { get; }

    public ImmutableArray<PackageAssemblyEvaluationOutcome> Evaluations
        { get; }

    public sealed record Matched
        : PackageAssemblySemanticFindCandidateOutcome
    {
        internal Matched(
            int candidateOrdinal,
            PackageAcquisitionCandidate candidate,
            PackageAssemblyEvaluationOutcome.Matched evaluation,
            ImmutableArray<PackageAssemblyEvaluationOutcome> evaluations)
            : base(candidateOrdinal, candidate, evaluations) =>
            Evaluation = evaluation;

        public PackageAssemblyEvaluationOutcome.Matched Evaluation { get; }
    }

    public sealed record NoMatch
        : PackageAssemblySemanticFindCandidateOutcome
    {
        internal NoMatch(
            int candidateOrdinal,
            PackageAcquisitionCandidate candidate,
            PackageAssemblyEvaluationOutcome.NoMatch evaluation,
            ImmutableArray<PackageAssemblyEvaluationOutcome> evaluations)
            : base(candidateOrdinal, candidate, evaluations) =>
            Evaluation = evaluation;

        public PackageAssemblyEvaluationOutcome.NoMatch Evaluation { get; }
    }

    public sealed record NotApplicable
        : PackageAssemblySemanticFindCandidateOutcome
    {
        internal NotApplicable(
            int candidateOrdinal,
            PackageAcquisitionCandidate candidate,
            PackageAssemblyEvaluationOutcome.NotApplicable evaluation)
            : base(candidateOrdinal, candidate, [evaluation]) =>
            Evaluation = evaluation;

        public PackageAssemblyEvaluationOutcome.NotApplicable Evaluation
            { get; }
    }

    public sealed record Failure
        : PackageAssemblySemanticFindCandidateOutcome
    {
        internal Failure(
            int candidateOrdinal,
            PackageAcquisitionCandidate candidate,
            PackageAssemblySemanticFindFailureReason reason)
            : base(
                candidateOrdinal,
                candidate,
                reason is PackageAssemblySemanticFindFailureReason.Evaluation
                    evaluation
                    ? evaluation.Evaluations
                    : [])
        {
            Reason = reason;
        }

        public PackageAssemblySemanticFindFailureReason Reason { get; }
    }
}

/// <summary>
/// One occurrence in candidate-population order and semantic-producer order.
/// </summary>
public sealed record PackageAssemblySemanticFindResult
{
    internal PackageAssemblySemanticFindResult(
        int candidateOrdinal,
        PackageAcquisitionCandidate candidate,
        PackageAssemblySelectedAssetContext selectedAsset,
        StringLiteralUseOccurrence evidence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateOrdinal);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selectedAsset);
        ArgumentNullException.ThrowIfNull(evidence);

        CandidateOrdinal = candidateOrdinal;
        Coordinate = candidate.Coordinate;
        Correspondence = candidate.Correspondence;
        SelectedAsset = selectedAsset;
        Evidence = evidence;
    }

    public int CandidateOrdinal { get; }

    public PackageSourceCoordinate Coordinate { get; }

    public PackageAcquisitionCandidateCorrespondence Correspondence
        { get; }

    public PackageAssemblySelectedAssetContext SelectedAsset { get; }

    public StringLiteralUseOccurrence Evidence { get; }
}

/// <summary>Completion facts over one frozen admitted population.</summary>
public sealed class PackageAssemblySemanticFindCompletion
{
    internal PackageAssemblySemanticFindCompletion(
        PackageAcquisitionPopulation population,
        int candidateCount,
        int failureCount,
        int matchedCandidateCount,
        int? maximumMatches)
    {
        Population = population.Completion;
        IsRequestedPopulationComplete =
            population.IsRequestedPopulationComplete;
        AllCandidatesCompleted =
            candidateCount == population.Candidates.Length;
        HasFailures = failureCount != 0;
        MatchLimit = maximumMatches;
        MatchLimitReached =
            maximumMatches is int limit
            && matchedCandidateCount >= limit;
        IsSemanticEvaluationComplete =
            (AllCandidatesCompleted || MatchLimitReached)
            && !HasFailures;
    }

    public PackageAcquisitionPopulationCompletionKind Population { get; }

    public bool IsRequestedPopulationComplete { get; }

    public bool AllCandidatesCompleted { get; }

    public bool HasFailures { get; }

    public int? MatchLimit { get; }

    public bool MatchLimitReached { get; }

    public bool IsSemanticEvaluationComplete { get; }
}

/// <summary>
/// Completed resource-free semantic evidence over one admitted package
/// population.
/// </summary>
public sealed class PackageAssemblySemanticFindDocument
{
    internal PackageAssemblySemanticFindDocument(
        PackageAcquisitionPopulation population,
        ImmutableArray<PackageAssemblySemanticFindCandidateOutcome> outcomes,
        ImmutableArray<PackageAssemblySemanticFindResult> results,
        int? maximumMatches)
    {
        ArgumentNullException.ThrowIfNull(population);
        if (outcomes.IsDefault)
            throw new ArgumentException(
                "Candidate outcomes must be initialized.",
                nameof(outcomes));
        if (results.IsDefault)
            throw new ArgumentException(
                "Find results must be initialized.",
                nameof(results));
        if (outcomes.Length > population.Candidates.Length)
        {
            throw new ArgumentException(
                "Candidate outcomes cannot exceed the admitted population.",
                nameof(outcomes));
        }

        for (int index = 0; index < outcomes.Length; index++)
        {
            PackageAssemblySemanticFindCandidateOutcome outcome =
                outcomes[index];
            PackageAcquisitionCandidate candidate =
                population.Candidates[index];
            if (outcome.CandidateOrdinal != index + 1
                || outcome.Coordinate != candidate.Coordinate
                || outcome.Correspondence != candidate.Correspondence)
            {
                throw new ArgumentException(
                    "Candidate outcomes must preserve population order and correspondence.",
                    nameof(outcomes));
            }
        }

        Population = population;
        CandidateOutcomes = outcomes;
        Results = results;
        CandidateCount = outcomes.Length;
        MatchedCandidateCount =
            outcomes.Count(static outcome =>
                outcome
                    is PackageAssemblySemanticFindCandidateOutcome.Matched);
        OccurrenceCount = results.Length;
        SemanticMissCount =
            outcomes.Count(static outcome =>
                outcome
                    is PackageAssemblySemanticFindCandidateOutcome.NoMatch);
        NotApplicableCount =
            outcomes.Count(static outcome =>
                outcome
                    is PackageAssemblySemanticFindCandidateOutcome.NotApplicable);
        FailureCount =
            outcomes.Count(static outcome =>
                outcome
                    is PackageAssemblySemanticFindCandidateOutcome.Failure);
        Completion = new(
            population,
            CandidateCount,
            FailureCount,
            MatchedCandidateCount,
            maximumMatches);
    }

    public PackageAcquisitionPopulation Population { get; }

    public ImmutableArray<PackageAssemblySemanticFindCandidateOutcome>
        CandidateOutcomes { get; }

    public ImmutableArray<PackageAssemblySemanticFindResult> Results
        { get; }

    public int CandidateCount { get; }

    public int MatchedCandidateCount { get; }

    public int OccurrenceCount { get; }

    public int SemanticMissCount { get; }

    public int NotApplicableCount { get; }

    public int FailureCount { get; }

    public PackageAssemblySemanticFindCompletion Completion { get; }
}

/// <summary>
/// Optional nonterminal sink for a host presenting candidate outcomes while
/// the completed Find Document is being produced.
/// </summary>
public interface IPackageAssemblySemanticFindNonterminalSink
{
    ValueTask ReportAsync(
        PackageAssemblySemanticFindCandidateOutcome outcome,
        CancellationToken cancellationToken);
}

/// <summary>
/// Serial authority-bearing composition over a frozen package population.
/// </summary>
internal static class PackageAssemblySemanticFindQuery
{
    internal static Task<PackageAssemblySemanticFindDocument>
        ExecuteToDocumentAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            IPackageAssemblySemanticFindNonterminalSink? nonterminalSink = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return ExecuteCoreAsync(
            request,
            sourceOperation,
            payloadAcquisition,
            nonterminalSink,
            cancellationToken);
    }

    private static async Task<PackageAssemblySemanticFindDocument>
        ExecuteCoreAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            IPackageAssemblySemanticFindNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken)
    {
        using (sourceOperation)
        {
            ValidateExecution(
                request,
                sourceOperation,
                payloadAcquisition,
                cancellationToken);

            CancellationToken callerCancellation =
                sourceOperation.CancellationToken;
            CancellationToken operationCancellation =
                sourceOperation.OperationCancellationToken;
            var outcomes =
                ImmutableArray.CreateBuilder<
                    PackageAssemblySemanticFindCandidateOutcome>(
                    request.Population.Candidates.Length);
            var results =
                ImmutableArray.CreateBuilder<
                    PackageAssemblySemanticFindResult>();
            int matchedCandidates = 0;

            try
            {
                ObserveCancellation();
                for (int index = 0;
                     index < request.Population.Candidates.Length;
                     index++)
                {
                    PackageAcquisitionCandidate candidate =
                        request.Population.Candidates[index];
                    ConfiguredPackagePayloadResult acquired =
                        await sourceOperation.AcquireCandidatePayloadAsync(
                            candidate,
                            payloadAcquisition.GetStore,
                            payloadAcquisition.Log,
                            request.Budget.Payload,
                            payloadAcquisition.TransferPolicy)
                        .ConfigureAwait(false);
                    ObserveCancellation();

                    CandidateEvaluation evaluated;
                    try
                    {
                        evaluated = await EvaluateCandidateAsync(
                            index + 1,
                            candidate,
                            acquired,
                            request,
                            operationCancellation).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException failure)
                    {
                        Exception classified = ClassifyCancellation(
                            failure,
                            sourceOperation,
                            callerCancellation);
                        ExceptionDispatchInfo.Capture(classified).Throw();
                        throw;
                    }
                    PackageAssemblySemanticFindCandidateOutcome outcome =
                        evaluated.Outcome;
                    outcomes.Add(outcome);
                    if (outcome
                        is PackageAssemblySemanticFindCandidateOutcome.Matched)
                    {
                        matchedCandidates++;
                        results.AddRange(evaluated.Results);
                    }

                    if (nonterminalSink is not null)
                    {
                        try
                        {
                            await nonterminalSink.ReportAsync(
                                outcome,
                                operationCancellation).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException failure)
                        {
                            Exception classified = ClassifyCancellation(
                                failure,
                                sourceOperation,
                                callerCancellation);
                            ExceptionDispatchInfo.Capture(classified).Throw();
                            throw;
                        }
                    }
                    ObserveCancellation();
                    if (request.MaximumMatches is int maximumMatches
                        && matchedCandidates >= maximumMatches)
                    {
                        break;
                    }
                }
                ObserveCancellation();
            }
            catch (OperationCanceledException failure)
                when (failure.CancellationToken
                    == operationCancellation)
            {
                Exception classified = ClassifyCancellation(
                    failure,
                    sourceOperation,
                    callerCancellation);
                ExceptionDispatchInfo.Capture(classified).Throw();
                throw;
            }

            return new(
                request.Population,
                outcomes.Count == outcomes.Capacity
                    ? outcomes.MoveToImmutable()
                    : outcomes.ToImmutable(),
                results.ToImmutable(),
                request.MaximumMatches);

            void ObserveCancellation()
            {
                callerCancellation.ThrowIfCancellationRequested();
                sourceOperation.ThrowIfExpired();
                operationCancellation.ThrowIfCancellationRequested();
            }
        }
    }

    internal static void ValidateExecution(
        PackageAssemblySemanticFindRequest request,
        PackageSourceOperationLease sourceOperation,
        PackagePayloadAcquisitionPlan payloadAcquisition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        ArgumentNullException.ThrowIfNull(payloadAcquisition);
        if (payloadAcquisition.Limits is not null)
        {
            throw new ArgumentException(
                "The semantic Find request owns payload limits; its acquisition plan must not declare another limit set.",
                nameof(payloadAcquisition));
        }
        if (sourceOperation.OperationTimeout
            != request.Budget.MaximumDuration)
        {
            throw new ArgumentException(
                "The Package Source operation deadline must match the semantic Find request.",
                nameof(sourceOperation));
        }
        if (cancellationToken != default
            && cancellationToken != sourceOperation.CancellationToken)
        {
            throw new ArgumentException(
                "The Package Source operation and semantic Find execution must carry the same caller cancellation.",
                nameof(cancellationToken));
        }
    }

    private static Exception ClassifyCancellation(
        OperationCanceledException failure,
        PackageSourceOperationLease sourceOperation,
        CancellationToken callerCancellation)
    {
        if (callerCancellation.IsCancellationRequested)
        {
            return CopyExceptionData(
                failure,
                new OperationCanceledException(
                    failure.Message,
                    failure,
                    callerCancellation));
        }

        try
        {
            sourceOperation.ThrowIfExpired();
        }
        catch (Exception classified)
            when (classified
                is OperationCanceledException
                or NuGetOperationTimeoutException)
        {
            return CopyExceptionData(failure, classified);
        }

        return failure;
    }

    private static TException CopyExceptionData<TException>(
        Exception source,
        TException target)
        where TException : Exception
    {
        foreach (object key in source.Data.Keys)
            target.Data[key] = source.Data[key];
        return target;
    }

    private sealed record CandidateEvaluation(
        PackageAssemblySemanticFindCandidateOutcome Outcome,
        ImmutableArray<PackageAssemblySemanticFindResult> Results);

    private static async Task<CandidateEvaluation>
        EvaluateCandidateAsync(
            int candidateOrdinal,
            PackageAcquisitionCandidate candidate,
            ConfiguredPackagePayloadResult acquired,
            PackageAssemblySemanticFindRequest request,
            CancellationToken cancellationToken)
    {
        if (acquired.Payload is null)
        {
            return new(
                new PackageAssemblySemanticFindCandidateOutcome.Failure(
                    candidateOrdinal,
                    candidate,
                    new PackageAssemblySemanticFindFailureReason.Acquisition(
                        new(
                            acquired))),
                []);
        }

        PackageRootBinding binding = PackageRootBinding.CreateFromSource(
            acquired.Payload,
            request.Target.RequestedFramework,
            request.Target.RuntimeIdentifier);
        if (request.Pattern.Pattern.LibraryScope
            == PackageAssemblyPatternLibraryScope.AggregateRole)
        {
            return await EvaluateAggregateAsync(
                candidateOrdinal,
                candidate,
                binding,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        PackageAssemblyEvaluationOutcome evaluation =
            await PackageAssemblyEvaluator.EvaluateAsync(
                binding,
                request.Pattern,
                request.Budget.Evaluation,
                cancellationToken).ConfigureAwait(false);
        PackageAssemblySemanticFindCandidateOutcome outcome =
            evaluation switch
            {
                PackageAssemblyEvaluationOutcome.Matched matched =>
                    new PackageAssemblySemanticFindCandidateOutcome.Matched(
                        candidateOrdinal,
                        candidate,
                        matched,
                        [matched]),
                PackageAssemblyEvaluationOutcome.NoMatch noMatch =>
                    new PackageAssemblySemanticFindCandidateOutcome.NoMatch(
                        candidateOrdinal,
                        candidate,
                        noMatch,
                        [noMatch]),
                PackageAssemblyEvaluationOutcome.NotApplicable notApplicable =>
                    new PackageAssemblySemanticFindCandidateOutcome.NotApplicable(
                        candidateOrdinal,
                        candidate,
                        notApplicable),
                PackageAssemblyEvaluationOutcome.Failure failure =>
                    new PackageAssemblySemanticFindCandidateOutcome.Failure(
                        candidateOrdinal,
                        candidate,
                        new PackageAssemblySemanticFindFailureReason.Evaluation(
                            [failure])),
                _ => throw new InvalidOperationException(
                    "Unknown package assembly evaluation outcome."),
            };
        ImmutableArray<PackageAssemblySemanticFindResult> results =
            evaluation is PackageAssemblyEvaluationOutcome.Matched matchedResult
                ? Results(candidateOrdinal, candidate, [matchedResult])
                : [];
        return new(outcome, results);
    }

    private static async Task<CandidateEvaluation> EvaluateAggregateAsync(
        int candidateOrdinal,
        PackageAcquisitionCandidate candidate,
        PackageRootBinding binding,
        PackageAssemblySemanticFindRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Pattern.Pattern.Role
            != PackageAssemblyPatternRole.ImplementationBody)
        {
            throw new InvalidOperationException(
                "Aggregate package assembly evaluation currently requires the implementation-body role.");
        }

        PackageCompileAssetSelection selection = binding.Root.AssetSelection;
        if (selection.Status
                == PackageCompileAssetSelectionStatus.InvalidImplementationAssets
            || selection.ImplementationAssets.Count == 0)
        {
            PackageAssemblyEvaluationOutcome selectionOutcome =
                await PackageAssemblyEvaluator.EvaluateAsync(
                    binding,
                    request.Pattern,
                    request.Budget.Evaluation,
                    cancellationToken).ConfigureAwait(false);
            PackageAssemblySemanticFindCandidateOutcome outcome =
                selectionOutcome switch
                {
                    PackageAssemblyEvaluationOutcome.NotApplicable
                        notApplicable =>
                        new PackageAssemblySemanticFindCandidateOutcome
                            .NotApplicable(
                                candidateOrdinal,
                                candidate,
                                notApplicable),
                    PackageAssemblyEvaluationOutcome.Failure failure =>
                        new PackageAssemblySemanticFindCandidateOutcome.Failure(
                            candidateOrdinal,
                            candidate,
                            new PackageAssemblySemanticFindFailureReason
                                .Evaluation([failure])),
                    _ => throw new InvalidOperationException(
                        "An empty implementation role produced an applicable semantic outcome."),
                };
            return new(outcome, []);
        }

        var evaluations =
            ImmutableArray.CreateBuilder<PackageAssemblyEvaluationOutcome>(
                selection.ImplementationAssets.Count);
        foreach (PackageCompileAsset asset in selection.ImplementationAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluations.Add(
                await PackageAssemblyEvaluator
                    .EvaluateImplementationAssetAsync(
                        binding,
                        request.Pattern,
                        request.Budget.Evaluation,
                        asset,
                        cancellationToken).ConfigureAwait(false));
        }

        ImmutableArray<PackageAssemblyEvaluationOutcome> completed =
            evaluations.MoveToImmutable();
        if (completed.OfType<PackageAssemblyEvaluationOutcome.Failure>().Any())
        {
            return new(
                new PackageAssemblySemanticFindCandidateOutcome.Failure(
                    candidateOrdinal,
                    candidate,
                    new PackageAssemblySemanticFindFailureReason.Evaluation(
                        completed)),
                []);
        }

        ImmutableArray<PackageAssemblyEvaluationOutcome.Matched> matches =
        [
            .. completed
                .OfType<PackageAssemblyEvaluationOutcome.Matched>(),
        ];
        if (!matches.IsEmpty)
        {
            return new(
                new PackageAssemblySemanticFindCandidateOutcome.Matched(
                    candidateOrdinal,
                    candidate,
                    matches[0],
                    completed),
                Results(candidateOrdinal, candidate, matches));
        }

        PackageAssemblyEvaluationOutcome.NoMatch firstNoMatch =
            completed
                .OfType<PackageAssemblyEvaluationOutcome.NoMatch>()
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "A completed aggregate evaluation produced no match, miss, or failure.");
        return new(
            new PackageAssemblySemanticFindCandidateOutcome.NoMatch(
                candidateOrdinal,
                candidate,
                firstNoMatch,
                completed),
            []);
    }

    private static ImmutableArray<PackageAssemblySemanticFindResult> Results(
        int candidateOrdinal,
        PackageAcquisitionCandidate candidate,
        IEnumerable<PackageAssemblyEvaluationOutcome.Matched> matches)
    {
        var results =
            ImmutableArray.CreateBuilder<
                PackageAssemblySemanticFindResult>();
        foreach (PackageAssemblyEvaluationOutcome.Matched match in matches)
        {
            PackageAssemblySelectedAssetContext selectedAsset =
                match.SelectedAsset
                ?? throw new InvalidOperationException(
                    "A matched library must retain its selected asset.");
            foreach (StringLiteralUseOccurrence occurrence
                in match.Evidence.Occurrences)
            {
                results.Add(
                    new(
                        candidateOrdinal,
                        candidate,
                        selectedAsset,
                        occurrence));
            }
        }
        return results.ToImmutable();
    }
}
