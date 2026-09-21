using QuerySpace.Rows;

namespace QuerySpace;

/// <summary>
/// The outcome of resolving one intent: this owner's executable plan, or one
/// structured failure. Never both, and never a partial binding.
/// </summary>
public sealed class PortableQueryResolution<TPlan>
{
    private readonly TPlan? _plan;
    private readonly PortableQueryFailure? _failure;

    private PortableQueryResolution(TPlan? plan, PortableQueryFailure? failure)
    {
        _plan = plan;
        _failure = failure;
    }

    public bool IsResolved => _failure is null;

    public TPlan Plan =>
        IsResolved
            ? _plan!
            : throw new InvalidOperationException(
                $"The intent did not resolve: {_failure!.Reason}.");

    public PortableQueryFailure Failure =>
        _failure ?? throw new InvalidOperationException(
            "The intent resolved, so it carries no failure.");

    internal static PortableQueryResolution<TPlan> Resolved(TPlan plan) =>
        new(plan, null);

    internal static PortableQueryResolution<TPlan> Refused(
        PortableQueryFailure failure) => new(default, failure);
}

/// <summary>
/// Resolves one intent against one vocabulary, exactly once.
/// </summary>
/// <remarks>
/// <para>
/// Resolution is atomic and its failure order is fixed by the contract rather
/// than by a host's enumeration: the vocabulary; then terms in semantic order;
/// then bounds in theirs; then the baseline order operation; then the stages in
/// declaration sequence, each ranking stage resolving its own ranking as it is
/// reached. Within one element the checks run existence, then admissibility,
/// then binding, then collision — and within collision, vocabulary compatibility
/// and family exclusivity before duplication, because a contradiction is never
/// collapsible while a duplicate may be.
/// </para>
/// <para>
/// Resolution starts no work. A rejected intent issues no acquisition, no source
/// request, and no package payload fetch, which matters because a package-query
/// term can authorize archive downloads and a malformed restored link must cost
/// nothing.
/// </para>
/// <para>Owner: <c>docs/design/portable-query-intent.md</c>.</para>
/// </remarks>
public static class PortableQueryResolver
{
    /// <summary>
    /// Resolves an intent against the vocabulary its identity names.
    /// </summary>
    /// <param name="vocabularyIdentity">The vocabulary the intent travels with.</param>
    /// <param name="vocabulary">
    /// The vocabulary that identity names, or <see langword="null"/> when this
    /// build does not offer it. A restored intent naming a vocabulary the build
    /// no longer has fails visibly; it is never dropped or defaulted.
    /// </param>
    public static PortableQueryResolution<TPlan> Resolve<TPredicate, TPlan>(
        string vocabularyIdentity,
        PortableQueryVocabulary<TPredicate, TPlan>? vocabulary,
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrEmpty(vocabularyIdentity);
        ArgumentNullException.ThrowIfNull(intent);

        if (vocabulary is null)
        {
            return Refuse<TPlan>(
                vocabularyIdentity,
                PortableQueryFailureReason.UnknownVocabulary,
                PortableQueryLocation.Vocabulary,
                offender: null);
        }

        var state = new Walk<TPredicate, TPlan>(vocabulary, intent);
        return state.Run(cancellationToken);
    }

    private static PortableQueryResolution<TPlan> Refuse<TPlan>(
        string vocabulary,
        PortableQueryFailureReason reason,
        PortableQueryLocation location,
        string? offender) =>
        PortableQueryResolution<TPlan>.Refused(
            PortableQueryFailure.Create(vocabulary, reason, location, offender));

    private sealed class Walk<TPredicate, TPlan>(
        PortableQueryVocabulary<TPredicate, TPlan> vocabulary,
        PortableQueryIntent intent)
    {
        private readonly List<PortableQueryResolvedTerm<TPredicate>> _terms = [];

        // The order part has no caller-meaningful sequence, so a location in it
        // is a position in the model's semantic order. Reported any other way, a
        // failure would move when the same intent arrived through a codec.
        private readonly IReadOnlyList<PortableQueryOrderOperation> _order =
            [.. PortableQueryModel.InSemanticOrder(intent.Order)];

        public PortableQueryResolution<TPlan> Run(CancellationToken cancellationToken)
        {
            PortableQueryFailure? failure =
                ResolveTerms(cancellationToken)
                ?? ResolveBounds(cancellationToken)
                ?? ResolveBaseline(cancellationToken);
            if (failure is not null)
                return PortableQueryResolution<TPlan>.Refused(failure);

            var rankings = new List<PortableQueryResolvedRanking>();
            failure = ResolveStages(rankings, cancellationToken);
            if (failure is not null)
                return PortableQueryResolution<TPlan>.Refused(failure);

            return PortableQueryResolution<TPlan>.Resolved(
                vocabulary.CreatePlan(
                    new PortableQueryResolvedIntent<TPredicate>(
                        _terms,
                        [.. PortableQueryModel.InSemanticOrder(intent.Bounds)],
                        intent.Stages,
                        Baseline(),
                        rankings)));
        }

        // Terms resolve in semantic order rather than in the order a caller
        // supplied, because a term set has no supplied order. This is the one
        // deliberate divergence from the row owner's resolver, which validates
        // predicates in declaration order; with two unknown keys the row owner
        // reports the one declared first and this reports the one that sorts
        // first, and each adopting host accepts that as part of adoption.
        private PortableQueryFailure? ResolveTerms(CancellationToken cancellationToken)
        {
            var families = new Dictionary<string, PortableQueryFamilyKind>(StringComparer.Ordinal);
            var boundOccurrences = new List<PortableQueryResolvedTerm<TPredicate>>();

            var predicates = new HashSet<string>(StringComparer.Ordinal);
            var compositionOccurrences =
                new HashSet<(string? Family, string Predicate)>();
            int index = 0;

            foreach (PortableQueryTerm term in PortableQueryModel.InSemanticOrder(intent.Terms))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PortableQueryLocation at = PortableQueryLocation.Term(index++);

                if (!vocabulary.TryGetKey(term.Key, out PortableQueryKeyDeclaration<TPredicate>? key))
                    return Failure(PortableQueryFailureReason.UnknownKey, at, term.Key);

                if (!key.AdmitsOperator(term.Operator))
                {
                    return Failure(
                        PortableQueryFailureReason.OperatorNotAdmitted,
                        at,
                        PortableQueryModel.TextOf(term.Operator));
                }

                PortableQueryBinding<TPredicate> binding = key.Bind(term.Operator, term.Value);
                if (!binding.IsBound)
                    return Failure(PortableQueryFailureReason.ValueRejected, at, term.Key);

                var resolved = new PortableQueryResolvedTerm<TPredicate>(
                    term,
                    binding.PredicateIdentity,
                    binding.Predicate);

                // Compatibility and family exclusivity precede duplication: a
                // contradiction is never collapsible, while a duplicate may be.
                foreach (PortableQueryResolvedTerm<TPredicate> previous in boundOccurrences)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!vocabulary.AreTermsCompatible(previous, resolved))
                    {
                        return Failure(
                            PortableQueryFailureReason.TermsIncompatible,
                            at,
                            term.Key);
                    }
                }

                bool repeatedComposition =
                    key.Family is { } repeatedFamily
                    && compositionOccurrences.Contains(
                        (repeatedFamily, binding.PredicateIdentity));
                if (key.Family is { } family
                    && key.FamilyKind is PortableQueryFamilyKind.Exclusive
                    && families.ContainsKey(family)
                    && !(vocabulary.CollapsesDuplicateBindings
                        && repeatedComposition))
                {
                    return Failure(PortableQueryFailureReason.TermsIncompatible, at, term.Key);
                }

                // The term bound, so its family membership and compatibility
                // occurrence stand whatever happens to its predicate next.
                // An equivalent occurrence in the same exclusive family may
                // collapse, but it still participates in later compatibility.
                if (key.Family is { } declared) families[declared] = key.FamilyKind;
                boundOccurrences.Add(resolved);

                bool collided = !predicates.Add(binding.PredicateIdentity);
                if (collided && !vocabulary.CollapsesDuplicateBindings)
                    return Failure(PortableQueryFailureReason.DuplicateAfterBinding, at, term.Key);

                // Collapse rests on idempotence, and idempotence holds only
                // inside one composition context: A AND A is A, and so is
                // A OR A. Across contexts the vocabulary still owns whether
                // one predicate may occur more than once, but an allowed
                // collision must keep both occurrences because A AND (A OR B)
                // is not equivalent to A AND B.
                if (!compositionOccurrences.Add((key.Family, binding.PredicateIdentity)))
                {
                    continue;
                }

                _terms.Add(resolved);
            }

            foreach (string family in vocabulary.RequiredTermFamilies
                .Distinct(StringComparer.Ordinal)
                .Order(PortableQueryModel.ScalarOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!families.ContainsKey(family))
                {
                    return Failure(
                        PortableQueryFailureReason.RequiredTermFamilyMissing,
                        PortableQueryLocation.Term(index),
                        family);
                }
            }

            return null;
        }

        // Bounds resolve after terms, so a dimension whose range depends on what
        // was bound sees a complete set — and it is asked before any acquisition
        // could have started.
        private PortableQueryFailure? ResolveBounds(CancellationToken cancellationToken)
        {
            var dimensions = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (PortableQueryBound bound in PortableQueryModel.InSemanticOrder(intent.Bounds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PortableQueryLocation at = PortableQueryLocation.Bound(index++);

                if (!vocabulary.TryGetDimension(
                    bound.Dimension,
                    out PortableQueryDimensionDeclaration<TPredicate>? dimension))
                {
                    return Failure(PortableQueryFailureReason.UnknownDimension, at, bound.Dimension);
                }

                if (!dimension.Admits(bound.RequestedMaximum, _terms))
                    return Failure(PortableQueryFailureReason.MaximumOutsideRange, at, bound.Dimension);

                dimensions.Add(bound.Dimension);
            }

            foreach (string dimension in vocabulary.RequiredDimensions
                .Distinct(StringComparer.Ordinal)
                .Order(PortableQueryModel.ScalarOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!dimensions.Contains(dimension))
                {
                    return Failure(
                        PortableQueryFailureReason.RequiredDimensionMissing,
                        PortableQueryLocation.Bound(index),
                        dimension);
                }
            }

            return null;
        }

        private PortableQueryFailure? ResolveBaseline(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PortableQueryOrderOperation? baseline = Baseline();
            return baseline is null
                ? null
                : ResolveOperation(baseline, OperationIndex(baseline), ranking: false);
        }

        // A ranking stage takes the operation bound to it, else the vocabulary's
        // declared default, else fails — at the stage, when the stage is reached,
        // so nothing in resolution spans parts out of sequence.
        private PortableQueryFailure? ResolveStages(
            List<PortableQueryResolvedRanking> rankings,
            CancellationToken cancellationToken)
        {
            for (int index = 0; index < intent.Stages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PortableQueryStage stage = intent.Stages[index];
                PortableQueryLocation at = PortableQueryLocation.Stage(index);

                if (!vocabulary.AdmitsStageKind(stage.Kind))
                {
                    return Failure(
                        PortableQueryFailureReason.StageNotAdmitted,
                        at,
                        PortableQueryModel.TextOf(stage.Kind));
                }

                if (stage.Kind is not RowSelectionStageKind.Top) continue;

                PortableQueryOrderOperation? bound = RankingFor(index);
                if (bound is not null)
                {
                    PortableQueryFailure? failure =
                        ResolveOperation(bound, OperationIndex(bound), ranking: true);
                    if (failure is not null) return failure;
                    rankings.Add(new PortableQueryResolvedRanking(index, bound, null));
                    continue;
                }

                if (vocabulary.DefaultRanking is { } declared)
                {
                    rankings.Add(new PortableQueryResolvedRanking(index, null, declared));
                    continue;
                }

                return Failure(PortableQueryFailureReason.RankingMissing, at, offender: null);
            }

            return null;
        }

        // An order reference is checked for existence, then orderability, then
        // the purpose its role requires.
        private PortableQueryFailure? ResolveOperation(
            PortableQueryOrderOperation operation,
            int index,
            bool ranking)
        {
            if (operation.Kind is PortableQueryOrderKind.Named)
            {
                PortableQueryLocation at = PortableQueryLocation.Operation(index);
                if (!vocabulary.TryGetNamedOrder(
                    operation.Reference,
                    out PortableQueryOrderPurpose purpose))
                {
                    return Failure(
                        PortableQueryFailureReason.UnknownOrderReference,
                        at,
                        operation.Reference);
                }

                return ranking && purpose is PortableQueryOrderPurpose.Sequence
                    ? Failure(PortableQueryFailureReason.OrderNotARanking, at, operation.Reference)
                    : null;
            }

            for (int term = 0; term < operation.Terms.Count; term++)
            {
                string key = operation.Terms[term].Key;
                PortableQueryLocation at = PortableQueryLocation.FieldTerm(index, term);

                if (!vocabulary.TryGetKey(key, out _))
                    return Failure(PortableQueryFailureReason.UnknownOrderReference, at, key);

                if (!vocabulary.IsOrderable(key))
                    return Failure(PortableQueryFailureReason.OrderReferenceNotOrderable, at, key);
            }

            return null;
        }

        private PortableQueryOrderOperation? Baseline()
        {
            foreach (PortableQueryOrderOperation operation in _order)
                if (operation.Role.IsBaseline) return operation;
            return null;
        }

        private PortableQueryOrderOperation? RankingFor(int stageIndex)
        {
            foreach (PortableQueryOrderOperation operation in _order)
            {
                if (!operation.Role.IsBaseline && operation.Role.StageIndex == stageIndex)
                    return operation;
            }

            return null;
        }

        private int OperationIndex(PortableQueryOrderOperation operation)
        {
            for (int index = 0; index < _order.Count; index++)
                if (ReferenceEquals(_order[index], operation)) return index;
            return 0;
        }

        private PortableQueryFailure Failure(
            PortableQueryFailureReason reason,
            PortableQueryLocation location,
            string? offender) =>
            PortableQueryFailure.Create(vocabulary.Identity, reason, location, offender);
    }
}
