using System.Diagnostics.CodeAnalysis;
using QuerySpace.Rows;

namespace DotnetInspector.PortableQueries.Tests;

/// <summary>What this vocabulary's binder makes of a term.</summary>
public sealed record TestPredicate(string Key, string Value);

/// <summary>
/// What this vocabulary builds when an intent resolves. Holding the resolved
/// intent lets a test assert what reached the plan, and that nothing did when
/// resolution refused.
/// </summary>
public sealed record TestPlan(PortableQueryResolvedIntent<TestPredicate> Resolved);

/// <summary>
/// A capability that stands in for acquisition: source requests, archive
/// downloads, anything a rejected intent must not cost.
/// </summary>
/// <remarks>
/// Invoking it fails the test. It is reachable only from
/// <see cref="TestVocabulary.CreatePlan"/>, which resolution reaches only after
/// every check has passed.
/// </remarks>
public sealed class AcquisitionCapability
{
    public int Invocations { get; private set; }

    public void Acquire()
    {
        Invocations++;
        throw new InvalidOperationException(
            "Resolution started acquisition. A rejected intent must cost nothing.");
    }
}

/// <summary>
/// A vocabulary shaped to reach every rule the resolution boundary declares.
/// </summary>
/// <remarks>
/// It is deliberately not Package Query. The contract under test is the
/// resolver's, and a purpose-built vocabulary can reach states no single real
/// vocabulary does — admitting a ranking stage and refusing one, declaring a
/// default ranking and declaring none, collapsing a duplicate binding and
/// refusing one — without pretending any of those are Package Query's choices.
/// </remarks>
public sealed class TestVocabulary(AcquisitionCapability? acquisition = null)
    : PortableQueryVocabulary<TestPredicate, TestPlan>
{
    /// <summary>A key whose binder folds case, so two spellings can collide.</summary>
    public const string DependsKey = "depends";

    /// <summary>A broad tool-presence key outside the format family.</summary>
    public const string ToolKey = "tool";

    /// <summary>A key in the combining tool-format family.</summary>
    public const string ToolFormatKey = "tool-format";

    /// <summary>A key in an exclusive family.</summary>
    public const string DependenciesKey = "dependencies";

    /// <summary>
    /// A key outside the exclusive family whose binder lands on the same
    /// predicate one of that family's terms does. Two distinct terms, one
    /// predicate, and only one of them carrying the family.
    /// </summary>
    /// <remarks>
    /// It sorts <em>before</em> <see cref="DependenciesKey"/>, which is the
    /// whole point: terms resolve in semantic order, so the alias binds first
    /// and the family member is the one whose predicate collapses.
    /// </remarks>
    public const string DependenciesAliasKey = "alias-dependencies";

    /// <summary>
    /// An ungrouped key binding where a combining-family member binds, and
    /// sorting before it. The requested expression is then
    /// <c>v1 AND (v1 OR v2)</c>, which a collapse across contexts would narrow
    /// to <c>v1 AND v2</c>.
    /// </summary>
    public const string ToolAliasKey = "alias-tool";

    /// <summary>
    /// A second spelling in the tool-format family that binds to the same
    /// predicate as <see cref="ToolFormatKey"/>.
    /// </summary>
    public const string ToolFormatAliasKey = "alias-tool-format";

    /// <summary>
    /// A later key used to prove compatibility sees collapsed occurrences.
    /// </summary>
    public const string ToolFormatConsumerKey = "tool-user";

    /// <summary>A key whose presence narrows the candidate dimension's range.</summary>
    public const string ContentKey = "content";

    /// <summary>A key that exists and can be ordered by.</summary>
    public const string NameKey = "name";

    /// <summary>A key that exists and cannot be ordered by.</summary>
    public const string OpaqueKey = "opaque";

    /// <summary>A key whose known value domain differs by operator.</summary>
    public const string ModeKey = "mode";

    public const string CandidatesDimension = "candidates";
    public const string MatchesDimension = "matches";

    public const string RankingOrder = "relevance";
    public const string SequenceOrder = "alphabetical";

    /// <summary>The candidate ceiling once a package-content term is bound.</summary>
    public const int ContentCandidateCeiling = 20;

    public const int CandidateCeiling = 200;

    public string VocabularyIdentity { get; init; } = "test.query";

    public bool AdmitsRankingStages { get; init; }

    public string? DeclaredDefaultRanking { get; init; }

    public bool CollapsesDuplicates { get; init; }

    public IReadOnlyList<string> RequiredFamilies { get; init; } = [];

    public IReadOnlyList<string> RequiredBounds { get; init; } = [];

    public int PlansCreated { get; private set; }

    public override string Identity => VocabularyIdentity;

    public override IReadOnlyList<string> RequiredTermFamilies =>
        RequiredFamilies;

    public override IReadOnlyList<string> RequiredDimensions =>
        RequiredBounds;

    public override string? DefaultRanking => DeclaredDefaultRanking;

    public override bool CollapsesDuplicateBindings => CollapsesDuplicates;

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)] out PortableQueryKeyDeclaration<TestPredicate>? declaration)
    {
        declaration = key switch
        {
            DependsKey => new TestKey(
                DependsKey,
                [PortableQueryOperator.Equal, PortableQueryOperator.NotEqual],
                // Case folding lives in the binder, never in the intent: two
                // spellings the vocabulary treats as equal collide here.
                value => value.Length == 0 ? null : value.ToLowerInvariant()),
            ToolKey => new TestKey(
                ToolKey,
                [PortableQueryOperator.Equal],
                value => value is "true" ? value : null),
            ToolFormatKey => new TestKey(
                ToolFormatKey,
                [PortableQueryOperator.Equal],
                value => value is "v1" or "v2" ? value : null)
            {
                DeclaredFamily = "tool-format",
                DeclaredFamilyKind = PortableQueryFamilyKind.Combining,
            },
            DependenciesKey => new TestKey(
                DependenciesKey,
                [PortableQueryOperator.Equal],
                value => value is "none" or "any" ? value : null)
            {
                DeclaredFamily = "dependency",
                DeclaredFamilyKind = PortableQueryFamilyKind.Exclusive,
            },
            DependenciesAliasKey => new TestKey(
                DependenciesAliasKey,
                [PortableQueryOperator.Equal],
                value => value is "none" or "any" ? value : null)
            {
                // Its own key, and no family — but the same predicate.
                PredicateKey = DependenciesKey,
            },
            ToolAliasKey => new TestKey(
                ToolAliasKey,
                [PortableQueryOperator.Equal],
                value => value is "v1" or "v2" ? value : null)
            {
                PredicateKey = ToolFormatKey,
            },
            ToolFormatAliasKey => new TestKey(
                ToolFormatAliasKey,
                [PortableQueryOperator.Equal],
                value => value is "v1" or "v2" ? value : null)
            {
                DeclaredFamily = "tool-format",
                DeclaredFamilyKind = PortableQueryFamilyKind.Combining,
                PredicateKey = ToolFormatKey,
            },
            ToolFormatConsumerKey => new TestKey(
                ToolFormatConsumerKey,
                [PortableQueryOperator.Equal],
                value => value is "true" ? value : null),
            ContentKey => new TestKey(
                ContentKey,
                [PortableQueryOperator.Equal],
                value => value),
            NameKey => new TestKey(
                NameKey,
                [PortableQueryOperator.Equal],
                value => value),
            OpaqueKey => new TestKey(
                OpaqueKey,
                [PortableQueryOperator.Equal],
                value => value),
            ModeKey => new OperatorSpecificValueKey(),
            _ => null
        };

        return declaration is not null;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)] out PortableQueryDimensionDeclaration<TestPredicate>? declaration)
    {
        declaration = dimension switch
        {
            CandidatesDimension => new TestDimension(CandidatesDimension),
            MatchesDimension => new TestDimension(MatchesDimension, ceiling: 100, contentAware: false),
            _ => null
        };

        return declaration is not null;
    }

    public override bool AdmitsStageKind(RowSelectionStageKind kind) =>
        kind is not RowSelectionStageKind.Top || AdmitsRankingStages;

    public override bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose)
    {
        switch (reference)
        {
            case RankingOrder: purpose = PortableQueryOrderPurpose.Ranking; return true;
            case SequenceOrder: purpose = PortableQueryOrderPurpose.Sequence; return true;
            default: purpose = default; return false;
        }
    }

    public override bool IsOrderable(string key) => key is NameKey or DependsKey;

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<TestPredicate> first,
        PortableQueryResolvedTerm<TestPredicate> second) =>
        !IsToolPresenceAndFormat(first.Term.Key, second.Term.Key)
        && !IsCollapsedFormatAndConsumer(first.Term.Key, second.Term.Key);

    public override TestPlan CreatePlan(PortableQueryResolvedIntent<TestPredicate> resolved)
    {
        PlansCreated++;
        acquisition?.Acquire();
        return new TestPlan(resolved);
    }

    private static bool IsToolPresenceAndFormat(string first, string second) =>
        first is ToolKey && second is ToolFormatKey
        || first is ToolFormatKey && second is ToolKey;

    private static bool IsCollapsedFormatAndConsumer(string first, string second) =>
        first is ToolFormatKey && second is ToolFormatConsumerKey
        || first is ToolFormatConsumerKey && second is ToolFormatKey;

    private sealed class TestKey(
        string key,
        PortableQueryOperator[] operators,
        Func<string, string?> bind)
        : PortableQueryKeyDeclaration<TestPredicate>
    {
        public string? DeclaredFamily { get; init; }

        public PortableQueryFamilyKind DeclaredFamilyKind { get; init; }

        /// <summary>The key this one's predicates are named for, if not itself.</summary>
        public string? PredicateKey { get; init; }

        public override string Key => key;

        public override string? Family => DeclaredFamily;

        public override PortableQueryFamilyKind FamilyKind => DeclaredFamilyKind;

        public override bool AdmitsOperator(PortableQueryOperator @operator) =>
            Array.IndexOf(operators, @operator) >= 0;

        public override PortableQueryBinding<TestPredicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            string? bound = bind(value);
            return bound is null
                ? PortableQueryBinding<TestPredicate>.Rejected
                : PortableQueryBinding<TestPredicate>.Bound(
                    $"{PredicateKey ?? key}:{bound}",
                    new TestPredicate(PredicateKey ?? key, bound));
        }
    }

    private sealed class TestDimension(
        string dimension,
        int ceiling = CandidateCeiling,
        bool contentAware = true)
        : PortableQueryDimensionDeclaration<TestPredicate>
    {
        public override string Dimension => dimension;

        public override bool Admits(
            int requestedMaximum,
            IReadOnlyList<PortableQueryResolvedTerm<TestPredicate>> boundTerms)
        {
            if (requestedMaximum < 1) return false;

            // The range depends on what was bound: once a content term is
            // present each candidate costs an archive, so the ceiling drops.
            bool content = false;
            if (contentAware)
            {
                foreach (PortableQueryResolvedTerm<TestPredicate> term in boundTerms)
                    if (term.Predicate.Key == ContentKey) content = true;
            }

            return requestedMaximum <= (content ? ContentCandidateCeiling : ceiling);
        }
    }

    private sealed class OperatorSpecificValueKey
        : PortableQueryKeyDeclaration<TestPredicate>
    {
        public override string Key => ModeKey;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator is PortableQueryOperator.Equal
                or PortableQueryOperator.NotEqual;

        public override PortableQueryBinding<TestPredicate> Bind(
            PortableQueryOperator @operator,
            string value) =>
            @operator is PortableQueryOperator.Equal
                && value is "fast"
                    ? PortableQueryBinding<TestPredicate>.Bound(
                        "mode:fast",
                        new TestPredicate(ModeKey, value))
                    : PortableQueryBinding<TestPredicate>.Rejected;
    }
}
