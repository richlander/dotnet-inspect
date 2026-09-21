using System.Reflection;
using QuerySpace.Rows;

namespace DotnetInspector.Sections.Tests;

public sealed class RowQueryContractTests
{
    [Fact]
    public void RowQueryResolvesVocabularyIdentitiesOnce()
    {
        int binderCalls = 0;
        int scoreOrderCalls = 0;
        int nameOrderCalls = 0;
        RowQueryVocabularyIdentity vocabularyIdentity =
            RowQueryVocabularyIdentity.Create();
        RowQueryKeyIdentity scoreIdentity =
            RowQueryKeyIdentity.Create();
        RowQueryNamedOrderIdentity scoreOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
        RowQueryNamedOrderIdentity nameOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
        RowQueryKey<QueryRow> score =
            IntKey(
                scoreIdentity,
                "score",
                row => row.Score,
                () => binderCalls++);
        var scoreOrder =
            new RowQueryNamedOrder<QueryRow>(
                scoreOrderIdentity,
                "score-order",
                RowQueryOrderPurpose.Ranking,
                direction =>
                {
                    scoreOrderCalls++;
                    return CompareRows(
                        (left, right) =>
                            left.Score.CompareTo(right.Score),
                        direction);
                });
        var nameOrder =
            new RowQueryNamedOrder<QueryRow>(
                nameOrderIdentity,
                "name-order",
                RowQueryOrderPurpose.Ranking,
                direction =>
                {
                    nameOrderCalls++;
                    return CompareRows(
                        (left, right) =>
                            StringComparer.Ordinal.Compare(
                                left.Name,
                                right.Name),
                        direction);
                });
        RowQueryVocabulary<QueryRow> vocabulary =
            RowQueryVocabulary<QueryRow>.Create(
                vocabularyIdentity,
                [score],
                [scoreOrder, nameOrder]);
        RowQueryResolutionResult<QueryRow> resolution =
            RowQueryResolver.Resolve(
                vocabulary,
                Intent(
                    predicates:
                    [
                        Predicate(
                            "score",
                            RowQueryOperator.GreaterOrEqual,
                            "2")
                    ],
                    baseline:
                        RowQueryOrderIntent.Named(
                            "score-order",
                            RowQueryOrderDirection.Descending),
                    selection:
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(
                                1,
                                RowQueryOrderIntent.Named(
                                    "name-order",
                                    RowQueryOrderDirection.Ascending))
                    ]));

        ResolvedRowQueryPlan<QueryRow> plan =
            AssertSuccess(resolution);
        Assert.Same(vocabularyIdentity, plan.VocabularyIdentity);
        Assert.Equal([scoreIdentity], plan.PredicateKeyIdentities);
        ResolvedRowQueryOrderBinding baseline =
            Assert.IsType<ResolvedRowQueryOrderBinding>(
                plan.BaselineOrder);
        Assert.Same(
            scoreOrderIdentity,
            baseline.NamedOrderIdentity);
        Assert.Equal(
            RowQueryOrderDirection.Descending,
            baseline.NamedOrderDirection);
        Assert.Empty(baseline.KeyIdentities);
        ResolvedRowQueryOrderBinding ranking =
            plan.ResolvedOrders[1];
        Assert.Same(nameOrderIdentity, ranking.NamedOrderIdentity);
        Assert.Same(
            ranking.Identity,
            Assert.Single(
                plan.SelectionPlan.Stages).Order);
        Assert.Equal(1, binderCalls);
        Assert.Equal(0, scoreOrderCalls);
        Assert.Equal(0, nameOrderCalls);
        Assert.DoesNotContain(
            typeof(string),
            plan.GetType()
                .GetProperties()
                .Select(property => property.PropertyType));

        QueryRow[] rows =
        [
            new("C", 3, 1, "keep", null, "three"),
            new("A", 2, 1, "keep", null, "two"),
            new("B", 1, 1, "keep", null, "one")
        ];
        Assert.True(RowQueryExecutor.Apply(rows, plan).IsSuccess);
        Assert.True(RowQueryExecutor.Apply(rows, plan).IsSuccess);
        Assert.Equal(1, binderCalls);
        Assert.Equal(2, scoreOrderCalls);
        Assert.Equal(2, nameOrderCalls);
    }

    [Fact]
    public void RowPredicatesUseTypedValues()
    {
        VocabularyFixture fixture = Vocabulary();
        RowQueryResolutionResult<QueryRow> resolution =
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    predicates:
                    [
                        Predicate(
                            "score",
                            RowQueryOperator.GreaterOrEqual,
                            "2"),
                        Predicate(
                            "priority",
                            RowQueryOperator.GreaterOrEqual,
                            "2"),
                        Predicate(
                            "group",
                            RowQueryOperator.Equals,
                            "keep")
                    ]));
        QueryRow[] rows =
        [
            new("A", 10, 2, "keep", null, "zero"),
            new("B", 1, 3, "keep", null, "one thousand"),
            new("C", 8, 1, "keep", null, "8"),
            new("D", 5, 3, "drop", null, "5")
        ];

        RowSelectionResult<QueryRow> result =
            RowQueryExecutor.Apply(
                rows,
                AssertSuccess(resolution));

        Assert.True(result.IsSuccess);
        QueryRow row = Assert.Single(result.Values);
        Assert.Equal("A", row.Name);
        Assert.Equal("zero", row.DisplayScore);
    }

    [Fact]
    public void RowPredicatesConjoinAndPreserveOrder()
    {
        VocabularyFixture fixture = Vocabulary();
        QueryRow[] rows =
        [
            new("A", 3, 1, "keep"),
            new("B", 4, 1, "drop"),
            new("C", 2, 1, "keep"),
            new("D", 1, 1, "keep"),
            new("E", 5, 1, "keep")
        ];
        ResolvedRowQueryPlan<QueryRow> plan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        predicates:
                        [
                            Predicate(
                                "score",
                                RowQueryOperator.GreaterOrEqual,
                                "2"),
                            Predicate(
                                "group",
                                RowQueryOperator.Equals,
                                "keep")
                        ])));

        RowSelectionResult<QueryRow> result =
            RowQueryExecutor.Apply(rows, plan);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["A", "C", "E"],
            result.Values.Select(row => row.Name));
        Assert.Same(rows[0], result.Values[0]);
        Assert.Same(rows[2], result.Values[1]);
        Assert.Same(rows[4], result.Values[2]);
    }

    [Fact]
    public void PredicateFreeUnorderedSelectionPreservesSnapshots()
    {
        VocabularyFixture fixture = Vocabulary();
        ResolvedRowQueryPlan<QueryRow> plan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Tail(2)
                        ])));
        QueryRow[] rows =
        [
            new("A", 1, 1, "x"),
            new("B", 2, 1, "x"),
            new("C", 3, 1, "x")
        ];
        NamedRowSequence<QueryRow>[] named =
        [
            NamedRowSequence<QueryRow>.Create(
                RowSequenceKey.Create(1),
                rows),
        ];

        RowSelectionResult<QueryRow> result =
            RowQueryExecutor.Apply(rows, plan);
        NamedRowSelectionResult<QueryRow> namedResult =
            RowQueryExecutor.ApplyNamed(named, plan);
        rows[1] = new("Changed", 4, 1, "x");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["B", "C"],
            result.Values.Select(row => row.Name));
        Assert.True(namedResult.IsSuccess);
        Assert.Equal(
            ["B", "C"],
            Assert.Single(namedResult.Sequences)
                .Values
                .Select(row => row.Name));
    }

    [Fact]
    public void CountAccelerationAdmitsOnlyPredicateFreeUnorderedPlans()
    {
        VocabularyFixture fixture = Vocabulary();
        ResolvedRowQueryPlan<QueryRow> selection =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Tail(2)
                        ])));

        Assert.True(
            RowQueryExecutor.TryApplyCount(
                5,
                selection,
                out RowSelectionCountResult count));
        Assert.True(count.IsSuccess);
        Assert.Equal(2, count.Count);

        ResolvedRowQueryPlan<QueryRow> predicate =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        predicates:
                        [
                            Predicate(
                                "score",
                                RowQueryOperator.GreaterOrEqual,
                                "2")
                        ])));
        Assert.False(
            RowQueryExecutor.TryApplyCount(
                5,
                predicate,
                out _));

        ResolvedRowQueryPlan<QueryRow> baseline =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "score-order",
                                RowQueryOrderDirection.Ascending))));
        Assert.False(
            RowQueryExecutor.TryApplyCount(
                5,
                baseline,
                out _));

        ResolvedRowQueryPlan<QueryRow> top =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                2,
                                RowQueryOrderIntent.Named(
                                    "score-order",
                                    RowQueryOrderDirection.Ascending))
                        ])));
        Assert.False(
            RowQueryExecutor.TryApplyCount(
                5,
                top,
                out _));
    }

    [Fact]
    public void EffectiveBaselineOrderFollowsPrecedence()
    {
        QueryRow[] rows =
        [
            new("B", 2, 1, "x"),
            new("A", 1, 1, "x"),
            new("C", 2, 1, "x")
        ];
        VocabularyFixture withDefault =
            Vocabulary(defaultBaseline: "score-order");
        ResolvedRowQueryPlan<QueryRow> defaultPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    withDefault.Vocabulary,
                    RowQueryIntent.Empty));
        ResolvedRowQueryPlan<QueryRow> explicitPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    withDefault.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "name-order",
                                RowQueryOrderDirection.Descending))));
        VocabularyFixture withoutDefault = Vocabulary();
        ResolvedRowQueryPlan<QueryRow> incomingPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    withoutDefault.Vocabulary,
                    RowQueryIntent.Empty));

        Assert.Equal(
            ["A", "B", "C"],
            Apply(rows, defaultPlan));
        Assert.Equal(
            ["C", "B", "A"],
            Apply(rows, explicitPlan));
        Assert.Equal(
            ["B", "A", "C"],
            Apply(rows, incomingPlan));
    }

    [Fact]
    public void TopRankingDoesNotBecomeBaselineOrder()
    {
        VocabularyFixture fixture = Vocabulary();
        QueryRow[] rows =
        [
            new("A", 1, 1, "x"),
            new("B", 1, 1, "x"),
            new("C", 1, 1, "x"),
            new("D", 5, 1, "x")
        ];
        RowSelectionIntentOperation<RowQueryOrderIntent>[] stages =
        [
            RowSelectionIntentOperation<RowQueryOrderIntent>.Head(3),
            RowSelectionIntentOperation<RowQueryOrderIntent>.Top(
                2,
                RowQueryOrderIntent.Named(
                    "score-order",
                    RowQueryOrderDirection.Descending))
        ];
        ResolvedRowQueryPlan<QueryRow> nameBaseline =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "name-order",
                                RowQueryOrderDirection.Ascending),
                        selection: stages)));
        ResolvedRowQueryPlan<QueryRow> scoreBaseline =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "score-order",
                                RowQueryOrderDirection.Descending),
                        selection: stages)));

        Assert.Equal(
            ["A", "B"],
            Apply(rows, nameBaseline));
        Assert.Equal(
            ["D", "A"],
            Apply(rows, scoreBaseline));
    }

    [Fact]
    public void EachTopCarriesItsResolvedRankingIdentity()
    {
        VocabularyFixture fixture = Vocabulary();
        ResolvedRowQueryPlan<QueryRow> plan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    3,
                                    RowQueryOrderIntent.Named(
                                        "score-order",
                                        RowQueryOrderDirection.Descending)),
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    1,
                                    RowQueryOrderIntent.Keys(
                                    [
                                        new(
                                            "name",
                                            RowQueryOrderDirection.Descending)
                                    ]))
                        ])));

        Assert.Null(plan.BaselineOrder);
        Assert.Equal(2, plan.ResolvedOrders.Count);
        RowSelectionStage<ResolvedRowQueryOrderIdentity> first =
            plan.SelectionPlan.Stages[0];
        RowSelectionStage<ResolvedRowQueryOrderIdentity> second =
            plan.SelectionPlan.Stages[1];
        Assert.NotSame(first.Order, second.Order);
        Assert.Same(
            fixture.ScoreOrderIdentity,
            plan.ResolvedOrders[0].NamedOrderIdentity);
        Assert.Equal(
            [fixture.NameKeyIdentity],
            plan.ResolvedOrders[1].KeyIdentities);
        Assert.Equal(
            [RowQueryOrderDirection.Descending],
            plan.ResolvedOrders[1].KeyDirections);
        Assert.Equal(
            ["C"],
            Apply(
                [
                    new("A", 3, 1, "x"),
                    new("B", 2, 1, "x"),
                    new("C", 1, 1, "x")
                ],
                plan));
    }

    [Fact]
    public void BaselineAndTopDefaultsAreIndependent()
    {
        VocabularyFixture baselineOnly =
            Vocabulary(defaultBaseline: "name-order");
        RowQueryResolutionResult<QueryRow> missingTop =
            RowQueryResolver.Resolve(
                baselineOnly.Vocabulary,
                Intent(
                    selection:
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(1)
                    ]));
        AssertFailure(
            missingTop,
            RowQueryOperationKind.TopRanking,
            1,
            RowQueryFailureReason.MissingTopRanking,
            semanticStageNumber: 1);

        VocabularyFixture topOnly =
            Vocabulary(defaultTop: "score-order");
        ResolvedRowQueryPlan<QueryRow> topOnlyPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    topOnly.Vocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Head(3),
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(1)
                        ])));
        Assert.Null(topOnlyPlan.BaselineOrder);
        Assert.Equal(
            ["A"],
            Apply(
                [
                    new("A", 1, 1, "x"),
                    new("B", 1, 1, "x"),
                    new("C", 1, 1, "x"),
                    new("D", 5, 1, "x")
                ],
                topOnlyPlan));

        VocabularyFixture both =
            Vocabulary(
                defaultBaseline: "score-order",
                defaultTop: "score-order");
        ResolvedRowQueryPlan<QueryRow> bothPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    both.Vocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(1)
                        ])));
        ResolvedRowQueryOrderBinding baseline =
            Assert.IsType<ResolvedRowQueryOrderBinding>(
                bothPlan.BaselineOrder);
        ResolvedRowQueryOrderBinding top =
            bothPlan.ResolvedOrders[1];
        Assert.NotSame(baseline.Identity, top.Identity);
        Assert.Same(
            both.ScoreOrderIdentity,
            baseline.NamedOrderIdentity);
        Assert.Same(
            both.ScoreOrderIdentity,
            top.NamedOrderIdentity);
    }

    [Fact]
    public void RowQueryResolutionIsAtomic()
    {
        VocabularyFixture fixture = Vocabulary();

        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    predicates:
                    [
                        Predicate(
                            "unknown",
                            RowQueryOperator.Equals,
                            "1")
                    ])),
            RowQueryOperationKind.Predicate,
            1,
            RowQueryFailureReason.UnknownKey);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    predicates:
                    [
                        Predicate(
                            "group",
                            RowQueryOperator.GreaterOrEqual,
                            "x")
                    ])),
            RowQueryOperationKind.Predicate,
            1,
            RowQueryFailureReason.UnsupportedPredicateOperator);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    predicates:
                    [
                        Predicate(
                            "score",
                            RowQueryOperator.Equals,
                            "not-an-integer")
                    ])),
            RowQueryOperationKind.Predicate,
            1,
            RowQueryFailureReason.InvalidValue);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    baseline:
                        RowQueryOrderIntent.Keys(
                        [
                            new(
                                "predicate-only",
                                RowQueryOrderDirection.Ascending)
                        ]))),
            RowQueryOperationKind.BaselineOrder,
            1,
            RowQueryFailureReason.UnsupportedKeyOrder,
            termPosition: 1);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    baseline:
                        RowQueryOrderIntent.Named(
                            "unknown",
                            RowQueryOrderDirection.Ascending))),
            RowQueryOperationKind.BaselineOrder,
            1,
            RowQueryFailureReason.UnknownNamedOrder);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    selection:
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(
                                1,
                                RowQueryOrderIntent.Named(
                                    "sequence-order",
                                    RowQueryOrderDirection.Ascending))
                    ])),
            RowQueryOperationKind.TopRanking,
            1,
            RowQueryFailureReason.NamedOrderIsNotRanking,
            semanticStageNumber: 1);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    baseline:
                        RowQueryOrderIntent.Named(
                            "unknown",
                            RowQueryOrderDirection.Ascending),
                    selection:
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(
                                1,
                                RowQueryOrderIntent.Named(
                                    "also-unknown",
                                    RowQueryOrderDirection.Ascending))
                    ])),
            RowQueryOperationKind.BaselineOrder,
            1,
            RowQueryFailureReason.UnknownNamedOrder);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    selection:
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Head(2),
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(
                                1,
                                RowQueryOrderIntent.Named(
                                    "sequence-order",
                                    RowQueryOrderDirection.Ascending)),
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(
                                1,
                                RowQueryOrderIntent.Named(
                                    "unknown",
                                    RowQueryOrderDirection.Ascending))
                    ])),
            RowQueryOperationKind.TopRanking,
            1,
            RowQueryFailureReason.NamedOrderIsNotRanking,
            semanticStageNumber: 2);

        RowQueryResolutionResult<QueryRow> firstFailureWins =
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    predicates:
                    [
                        Predicate(
                            "score",
                            RowQueryOperator.Equals,
                            "1"),
                        Predicate(
                            "score",
                            RowQueryOperator.Equals,
                            "bad")
                    ],
                    baseline:
                        RowQueryOrderIntent.Named(
                            "unknown",
                            RowQueryOrderDirection.Ascending),
                    selection:
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Top(1)
                    ]));
        AssertFailure(
            firstFailureWins,
            RowQueryOperationKind.Predicate,
            2,
            RowQueryFailureReason.InvalidValue);
    }

    [Fact]
    public void RowQueryFailureShapeIsPresentationFree()
    {
        VocabularyFixture fixture = Vocabulary();
        RowQueryResolutionResult<QueryRow> resolution =
            RowQueryResolver.Resolve(
                fixture.Vocabulary,
                Intent(
                    baseline:
                        RowQueryOrderIntent.Keys(
                        [
                            new(
                                "score",
                                RowQueryOrderDirection.Ascending),
                            new(
                                "unknown",
                                RowQueryOrderDirection.Descending)
                        ])));
        RowQueryFailure failure =
            Assert.IsType<RowQueryFailure>(
                resolution.Failure);

        Assert.False(resolution.IsSuccess);
        Assert.Null(resolution.Plan);
        Assert.Same(
            fixture.Vocabulary.Identity,
            failure.VocabularyIdentity);
        Assert.Equal(
            RowQueryOperationKind.BaselineOrder,
            failure.OperationKind);
        Assert.Equal(1, failure.OperationPosition);
        Assert.Equal(2, failure.TermPosition);
        Assert.Null(failure.SemanticStageNumber);
        Assert.Equal(
            RowQueryFailureReason.UnknownKey,
            failure.Reason);
        Assert.Null(failure.KeyIdentity);
        Assert.Null(failure.NamedOrderIdentity);

        PropertyInfo[] properties =
            typeof(RowQueryFailure).GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);
        Assert.Equal(
            [
                nameof(RowQueryFailure.KeyIdentity),
                nameof(RowQueryFailure.NamedOrderIdentity),
                nameof(RowQueryFailure.OperationKind),
                nameof(RowQueryFailure.OperationPosition),
                nameof(RowQueryFailure.Reason),
                nameof(RowQueryFailure.SemanticStageNumber),
                nameof(RowQueryFailure.TermPosition),
                nameof(RowQueryFailure.VocabularyIdentity)
            ],
            properties
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            properties,
            property =>
                property.PropertyType == typeof(string)
                || typeof(Exception).IsAssignableFrom(
                    property.PropertyType));

        RowQueryFailure knownKey =
            Assert.IsType<RowQueryFailure>(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        predicates:
                        [
                            Predicate(
                                "score",
                                RowQueryOperator.Equals,
                                "bad")
                        ]))
                    .Failure);
        Assert.Same(
            fixture.ScoreKeyIdentity,
            knownKey.KeyIdentity);
        Assert.Null(knownKey.NamedOrderIdentity);

        RowQueryFailure knownOrder =
            Assert.IsType<RowQueryFailure>(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    1,
                                    RowQueryOrderIntent.Named(
                                        "sequence-order",
                                        RowQueryOrderDirection.Ascending))
                        ]))
                    .Failure);
        Assert.Null(knownOrder.KeyIdentity);
        Assert.Same(
            fixture.SequenceOrderIdentity,
            knownOrder.NamedOrderIdentity);
    }

    [Fact]
    public void RowQueryExecutionFailuresStayVisible()
    {
        var accessorException =
            new SentinelException("accessor");
        RowQueryKey<QueryRow> accessorKey =
            RowQueryKey<QueryRow>.Create(
                RowQueryKeyIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                _ => throw accessorException,
                IntBinder);
        ResolvedRowQueryPlan<QueryRow> accessorPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQueryVocabulary<QueryRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [accessorKey],
                        []),
                    Intent(
                        predicates:
                        [
                            Predicate(
                                "value",
                                RowQueryOperator.Equals,
                                "1")
                        ])));
        Assert.Same(
            accessorException,
            Assert.Throws<SentinelException>(
                () => RowQueryExecutor.Apply(
                    [new("A", 1, 1, "x")],
                    accessorPlan)));

        var predicateException =
            new SentinelException("predicate");
        RowQueryKey<QueryRow> predicateKey =
            RowQueryKey<QueryRow>.Create(
                RowQueryKeyIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                row => RowQueryValue<int>.Present(row.Score),
                (_, _) => _ => throw predicateException);
        ResolvedRowQueryPlan<QueryRow> predicatePlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQueryVocabulary<QueryRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [predicateKey],
                        []),
                    Intent(
                        predicates:
                        [
                            Predicate(
                                "value",
                                RowQueryOperator.Equals,
                                "1")
                        ])));
        Assert.Same(
            predicateException,
            Assert.Throws<SentinelException>(
                () => RowQueryExecutor.Apply(
                    [new("A", 1, 1, "x")],
                    predicatePlan)));

        var baselineFactoryException =
            new SentinelException("baseline factory");
        int baselineFactoryCalls = 0;
        var baselineFactoryOrder =
            new RowQueryNamedOrder<QueryRow>(
                RowQueryNamedOrderIdentity.Create(),
                "baseline-throwing",
                RowQueryOrderPurpose.Sequence,
                _ =>
                {
                    baselineFactoryCalls++;
                    throw baselineFactoryException;
                });
        ResolvedRowQueryPlan<QueryRow> baselineFactoryPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQueryVocabulary<QueryRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [],
                        [baselineFactoryOrder]),
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "baseline-throwing",
                                RowQueryOrderDirection.Ascending))));
        Assert.Equal(0, baselineFactoryCalls);
        Assert.Same(
            baselineFactoryException,
            Assert.Throws<SentinelException>(
                () => RowQueryExecutor.Apply(
                    [],
                    baselineFactoryPlan)));
        Assert.Same(
            baselineFactoryException,
            Assert.Throws<SentinelException>(
                () => RowQueryExecutor.Apply(
                    [new("A", 1, 1, "x")],
                    baselineFactoryPlan)));
        Assert.Equal(2, baselineFactoryCalls);

        ResolvedRowQueryPlan<QueryRow> competingFailurePlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQueryVocabulary<QueryRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [predicateKey],
                        [baselineFactoryOrder]),
                    Intent(
                        predicates:
                        [
                            Predicate(
                                "value",
                                RowQueryOperator.Equals,
                                "1")
                        ],
                        baseline:
                            RowQueryOrderIntent.Named(
                                "baseline-throwing",
                                RowQueryOrderDirection.Ascending))));
        baselineFactoryCalls = 0;
        Assert.Same(
            predicateException,
            Assert.Throws<SentinelException>(
                () => RowQueryExecutor.Apply(
                    [new("A", 1, 1, "x")],
                    competingFailurePlan)));
        Assert.Equal(0, baselineFactoryCalls);

        var comparerException =
            new SentinelException("comparer");
        RowQueryKey<QueryRow> comparerKey =
            RowQueryKey<QueryRow>.Create(
                RowQueryKeyIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                row => RowQueryValue<int>.Present(row.Score),
                IntBinder,
                _ => Comparer<RowQueryValue<int>>.Create(
                    (_, _) => throw comparerException));
        ResolvedRowQueryPlan<QueryRow> comparerPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQueryVocabulary<QueryRow>.Create(
                        RowQueryVocabularyIdentity.Create(),
                        [comparerKey],
                        []),
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Keys(
                            [
                                new(
                                    "value",
                                    RowQueryOrderDirection.Ascending)
                            ]))));
        Assert.Same(
            comparerException,
            Assert.Throws<SentinelException>(
                () => RowQueryExecutor.Apply(
                    [
                        new("A", 1, 1, "x"),
                        new("B", 2, 1, "x")
                    ],
                    comparerPlan)));

        var resolverException =
            new SentinelException("resolver");
        int resolverCalls = 0;
        var throwingOrder =
            new RowQueryNamedOrder<QueryRow>(
                RowQueryNamedOrderIdentity.Create(),
                "throwing",
                RowQueryOrderPurpose.Ranking,
                _ =>
                {
                    resolverCalls++;
                    throw resolverException;
                });
        RowQueryVocabulary<QueryRow> resolverVocabulary =
            RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [],
                [throwingOrder]);
        ResolvedRowQueryPlan<QueryRow> deferredPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    resolverVocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Window(
                                    1,
                                    2),
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    1,
                                    RowQueryOrderIntent.Named(
                                        "throwing",
                                        RowQueryOrderDirection.Descending))
                        ])));
        Assert.Equal(0, resolverCalls);
        RowSelectionResult<QueryRow> windowFailure =
            RowQueryExecutor.Apply(
                [new("A", 1, 1, "x")],
                deferredPlan);
        Assert.False(windowFailure.IsSuccess);
        Assert.Equal(0, resolverCalls);

        ResolvedRowQueryPlan<QueryRow> reachedPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    resolverVocabulary,
                    Intent(
                        selection:
                        [
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Head(1),
                            RowSelectionIntentOperation<
                                RowQueryOrderIntent>.Top(
                                    1,
                                    RowQueryOrderIntent.Named(
                                        "throwing",
                                        RowQueryOrderDirection.Descending))
                        ])));
        Assert.Same(
            resolverException,
            Assert.Throws<SentinelException>(
                () => RowQueryExecutor.Apply(
                    [new("A", 1, 1, "x")],
                    reachedPlan)));
        Assert.Equal(1, resolverCalls);
    }

    [Fact]
    public void NamedOrderDirectionsPreserveStableTies()
    {
        VocabularyFixture fixture = Vocabulary();
        QueryRow[] rows =
        [
            new("A", 2, 1, "x"),
            new("B", 1, 1, "x"),
            new("C", 2, 1, "x"),
            new("D", 1, 1, "x")
        ];
        ResolvedRowQueryPlan<QueryRow> ascending =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "score-order",
                                RowQueryOrderDirection.Ascending))));
        ResolvedRowQueryPlan<QueryRow> descending =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "score-order",
                                RowQueryOrderDirection.Descending))));

        Assert.Equal(
            ["B", "D", "A", "C"],
            Apply(rows, ascending));
        Assert.Equal(
            ["A", "C", "B", "D"],
            Apply(rows, descending));
    }

    [Fact]
    public void KeyOrdersComposeLexicographicallyAndPreserveTies()
    {
        VocabularyFixture fixture = Vocabulary();
        QueryRow[] rows =
        [
            new("A", 2, 1, "x"),
            new("B", 1, 1, "y"),
            new("C", 2, 1, "x"),
            new("D", 2, 1, "y"),
            new("E", 2, 1, "x")
        ];
        ResolvedRowQueryPlan<QueryRow> plan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Keys(
                            [
                                new(
                                    "score",
                                    RowQueryOrderDirection.Descending),
                                new(
                                    "group",
                                    RowQueryOrderDirection.Ascending)
                            ]))));

        Assert.Equal(
            ["A", "C", "E", "D", "B"],
            Apply(rows, plan));
    }

    [Fact]
    public void BinderExceptionsPropagateUnchanged()
    {
        var binderException =
            new SentinelException("binder");
        RowQueryKey<QueryRow> key =
            RowQueryKey<QueryRow>.Create(
                RowQueryKeyIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                row => RowQueryValue<int>.Present(row.Score),
                (_, _) => throw binderException);
        RowQueryVocabulary<QueryRow> vocabulary =
            RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key],
                []);

        SentinelException observed =
            Assert.Throws<SentinelException>(
                () => RowQueryResolver.Resolve(
                    vocabulary,
                    Intent(
                        predicates:
                        [
                            Predicate(
                                "value",
                                RowQueryOperator.Equals,
                                "1")
                        ])));

        Assert.Same(binderException, observed);
    }

    [Fact]
    public void MissingValuePlacementIsVocabularyDefinedAcrossDirections()
    {
        VocabularyFixture fixture = Vocabulary();
        QueryRow[] rows =
        [
            new("A", 1, 1, "x", 2),
            new("B", 1, 1, "x", null),
            new("C", 1, 1, "x", 1)
        ];
        ResolvedRowQueryPlan<QueryRow> ascending =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Keys(
                            [
                                new(
                                    "optional",
                                    RowQueryOrderDirection.Ascending)
                            ]))));
        ResolvedRowQueryPlan<QueryRow> descending =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Vocabulary,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Keys(
                            [
                                new(
                                    "optional",
                                    RowQueryOrderDirection.Descending)
                            ]))));

        Assert.Equal(
            ["C", "A", "B"],
            Apply(rows, ascending));
        Assert.Equal(
            ["A", "C", "B"],
            Apply(rows, descending));
    }

    [Fact]
    public void RowQueryVocabularysRejectInvalidDeclarations()
    {
        RowQueryKeyIdentity keyIdentity =
            RowQueryKeyIdentity.Create();
        RowQueryKey<QueryRow> key =
            IntKey(
                keyIdentity,
                "score",
                row => row.Score);
        RowQueryKey<QueryRow> duplicateKey =
            IntKey(
                RowQueryKeyIdentity.Create(),
                "score",
                row => row.Score);
        RowQueryKey<QueryRow> duplicateIdentity =
            IntKey(
                keyIdentity,
                "other",
                row => row.Score);
        RowQueryNamedOrderIdentity orderIdentity =
            RowQueryNamedOrderIdentity.Create();
        var ranking =
            new RowQueryNamedOrder<QueryRow>(
                orderIdentity,
                "rank",
                RowQueryOrderPurpose.Ranking,
                _ => Comparer<QueryRow>.Create((_, _) => 0));
        var duplicateOrderKey =
            new RowQueryNamedOrder<QueryRow>(
                RowQueryNamedOrderIdentity.Create(),
                "rank",
                RowQueryOrderPurpose.Ranking,
                _ => Comparer<QueryRow>.Create((_, _) => 0));
        var duplicateOrderIdentity =
            new RowQueryNamedOrder<QueryRow>(
                orderIdentity,
                "other",
                RowQueryOrderPurpose.Ranking,
                _ => Comparer<QueryRow>.Create((_, _) => 0));
        var sequence =
            new RowQueryNamedOrder<QueryRow>(
                RowQueryNamedOrderIdentity.Create(),
                "sequence",
                RowQueryOrderPurpose.Sequence,
                _ => Comparer<QueryRow>.Create((_, _) => 0));

        Assert.Throws<ArgumentException>(
            () => RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key, duplicateKey],
                [ranking]));
        Assert.Throws<ArgumentException>(
            () => RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key, duplicateIdentity],
                [ranking]));
        Assert.Throws<ArgumentException>(
            () => RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key],
                [ranking, duplicateOrderKey]));
        Assert.Throws<ArgumentException>(
            () => RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key],
                [ranking, duplicateOrderIdentity]));
        Assert.Throws<ArgumentException>(
            () => RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key],
                [ranking],
                defaultBaselineOrder:
                    new(
                        new RowQueryNamedOrder<QueryRow>(
                            orderIdentity,
                            "rank",
                            RowQueryOrderPurpose.Ranking,
                            _ =>
                                Comparer<QueryRow>.Create(
                                    (_, _) => 0)),
                        RowQueryOrderDirection.Ascending)));
        Assert.Throws<ArgumentException>(
            () => RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [key],
                [sequence],
                defaultTopRanking:
                    new(
                        sequence,
                        RowQueryOrderDirection.Descending)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RowQueryPredicateIntent(
                "score",
                (RowQueryOperator)int.MaxValue,
                new RowQueryValueToken("1")));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RowQueryOrderTermIntent(
                "score",
                (RowQueryOrderDirection)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RowQueryOrderIntent.Named(
                "rank",
                (RowQueryOrderDirection)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RowQueryNamedOrder<QueryRow>(
                RowQueryNamedOrderIdentity.Create(),
                "rank",
                (RowQueryOrderPurpose)int.MaxValue,
                _ => Comparer<QueryRow>.Create((_, _) => 0)));
    }

    private static VocabularyFixture Vocabulary(
        string? defaultBaseline = null,
        string? defaultTop = null)
    {
        RowQueryKeyIdentity scoreIdentity =
            RowQueryKeyIdentity.Create();
        RowQueryKeyIdentity nameIdentity =
            RowQueryKeyIdentity.Create();
        RowQueryKey<QueryRow> score =
            IntKey(
                scoreIdentity,
                "score",
                row => row.Score);
        RowQueryKey<QueryRow> priority =
            IntKey(
                RowQueryKeyIdentity.Create(),
                "priority",
                row => row.Priority);
        RowQueryKey<QueryRow> group =
            TextKey(
                RowQueryKeyIdentity.Create(),
                "group",
                row => row.Group);
        RowQueryKey<QueryRow> name =
            TextKey(
                nameIdentity,
                "name",
                row => row.Name);
        RowQueryKey<QueryRow> optional =
            RowQueryKey<QueryRow>.Create(
                RowQueryKeyIdentity.Create(),
                "optional",
                [
                    RowQueryOperator.Equals,
                    RowQueryOperator.NotEquals,
                    RowQueryOperator.GreaterOrEqual,
                    RowQueryOperator.LessOrEqual
                ],
                row =>
                    row.OptionalScore is int value
                        ? RowQueryValue<int>.Present(value)
                        : RowQueryValue<int>.Missing,
                IntBinder,
                direction =>
                    RowQueryValueOrder.Create(
                        Comparer<int>.Default,
                        direction,
                        missingLast: true));
        RowQueryKey<QueryRow> predicateOnly =
            RowQueryKey<QueryRow>.Create(
                RowQueryKeyIdentity.Create(),
                "predicate-only",
                [RowQueryOperator.Equals],
                row =>
                    RowQueryValue<string>.Present(
                        row.Group),
                TextBinder);

        RowQueryNamedOrderIdentity scoreOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
        var scoreOrder =
            new RowQueryNamedOrder<QueryRow>(
                scoreOrderIdentity,
                "score-order",
                RowQueryOrderPurpose.Ranking,
                direction =>
                    CompareRows(
                        (left, right) =>
                            left.Score.CompareTo(right.Score),
                        direction));
        var nameOrder =
            new RowQueryNamedOrder<QueryRow>(
                RowQueryNamedOrderIdentity.Create(),
                "name-order",
                RowQueryOrderPurpose.Ranking,
                direction =>
                    CompareRows(
                        (left, right) =>
                            StringComparer.Ordinal.Compare(
                                left.Name,
                                right.Name),
                        direction));
        var sequenceOrder =
            new RowQueryNamedOrder<QueryRow>(
                RowQueryNamedOrderIdentity.Create(),
                "sequence-order",
                RowQueryOrderPurpose.Sequence,
                _ => Comparer<QueryRow>.Create((_, _) => 0));
        RowQueryNamedOrder<QueryRow>[] orders =
        [
            scoreOrder,
            nameOrder,
            sequenceOrder
        ];

        RowQueryNamedOrderDefault<QueryRow>? baseline =
            defaultBaseline is null
                ? null
                : new(
                    Assert.Single(
                        orders,
                        order => order.Key == defaultBaseline),
                    RowQueryOrderDirection.Ascending);
        RowQueryNamedOrderDefault<QueryRow>? top =
            defaultTop is null
                ? null
                : new(
                    Assert.Single(
                        orders,
                        order => order.Key == defaultTop),
                    RowQueryOrderDirection.Descending);

        return new(
            RowQueryVocabulary<QueryRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    score,
                    priority,
                    group,
                    name,
                    optional,
                    predicateOnly
                ],
                orders,
                baseline,
                top),
            scoreIdentity,
            nameIdentity,
            scoreOrderIdentity,
            sequenceOrder.Identity);
    }

    private static RowQueryKey<QueryRow> IntKey(
        RowQueryKeyIdentity identity,
        string key,
        Func<QueryRow, int> accessor,
        Action? onBind = null) =>
        RowQueryKey<QueryRow>.Create(
            identity,
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals,
                RowQueryOperator.GreaterOrEqual,
                RowQueryOperator.LessOrEqual
            ],
            row =>
                RowQueryValue<int>.Present(
                    accessor(row)),
            (@operator, token) =>
            {
                onBind?.Invoke();
                return IntBinder(@operator, token);
            },
            direction =>
                RowQueryValueOrder.Create(
                    Comparer<int>.Default,
                    direction,
                    missingLast: true));

    private static RowQueryKey<QueryRow> TextKey(
        RowQueryKeyIdentity identity,
        string key,
        Func<QueryRow, string> accessor) =>
        RowQueryKey<QueryRow>.Create(
            identity,
            key,
            [
                RowQueryOperator.Equals,
                RowQueryOperator.NotEquals
            ],
            row =>
                RowQueryValue<string>.Present(
                    accessor(row)),
            TextBinder,
            direction =>
                RowQueryValueOrder.Create(
                    Comparer<string>.Create(
                        (left, right) =>
                            StringComparer.Ordinal.Compare(
                                left,
                                right)),
                    direction,
                    missingLast: true));

    private static Predicate<int>? IntBinder(
        RowQueryOperator @operator,
        RowQueryValueToken token)
    {
        if (!int.TryParse(token.Text, out int expected))
            return null;

        return @operator switch
        {
            RowQueryOperator.Equals =>
                actual => actual == expected,
            RowQueryOperator.NotEquals =>
                actual => actual != expected,
            RowQueryOperator.GreaterOrEqual =>
                actual => actual >= expected,
            RowQueryOperator.LessOrEqual =>
                actual => actual <= expected,
            _ => null
        };
    }

    private static Predicate<string>? TextBinder(
        RowQueryOperator @operator,
        RowQueryValueToken token) =>
        @operator switch
        {
            RowQueryOperator.Equals =>
                actual =>
                    string.Equals(
                        actual,
                        token.Text,
                        StringComparison.OrdinalIgnoreCase),
            RowQueryOperator.NotEquals =>
                actual =>
                    !string.Equals(
                        actual,
                        token.Text,
                        StringComparison.OrdinalIgnoreCase),
            _ => null
        };

    private static IComparer<QueryRow> CompareRows(
        Comparison<QueryRow> comparison,
        RowQueryOrderDirection direction) =>
        Comparer<QueryRow>.Create(
            direction is RowQueryOrderDirection.Ascending
                ? comparison
                : (left, right) => comparison(right, left));

    private static RowQueryPredicateIntent Predicate(
        string key,
        RowQueryOperator @operator,
        string value) =>
        new(
            key,
            @operator,
            new RowQueryValueToken(value));

    private static RowQueryIntent Intent(
        IReadOnlyList<RowQueryPredicateIntent>? predicates = null,
        RowQueryOrderIntent? baseline = null,
        IReadOnlyList<
            RowSelectionIntentOperation<RowQueryOrderIntent>>?
                selection = null) =>
        RowQueryIntent.Create(
            predicates ?? [],
            baseline,
            RowSelectionIntent<RowQueryOrderIntent>.Create(
                selection ?? []));

    private static ResolvedRowQueryPlan<QueryRow> AssertSuccess(
        RowQueryResolutionResult<QueryRow> resolution)
    {
        Assert.True(resolution.IsSuccess);
        Assert.Null(resolution.Failure);
        return Assert.IsType<ResolvedRowQueryPlan<QueryRow>>(
            resolution.Plan);
    }

    private static void AssertFailure(
        RowQueryResolutionResult<QueryRow> resolution,
        RowQueryOperationKind operationKind,
        int operationPosition,
        RowQueryFailureReason reason,
        int? termPosition = null,
        int? semanticStageNumber = null)
    {
        Assert.False(resolution.IsSuccess);
        Assert.Null(resolution.Plan);
        RowQueryFailure failure =
            Assert.IsType<RowQueryFailure>(
                resolution.Failure);
        Assert.Equal(operationKind, failure.OperationKind);
        Assert.Equal(
            operationPosition,
            failure.OperationPosition);
        Assert.Equal(termPosition, failure.TermPosition);
        Assert.Equal(
            semanticStageNumber,
            failure.SemanticStageNumber);
        Assert.Equal(reason, failure.Reason);
    }

    private static IReadOnlyList<string> Apply(
        IReadOnlyList<QueryRow> rows,
        ResolvedRowQueryPlan<QueryRow> plan)
    {
        RowSelectionResult<QueryRow> result =
            RowQueryExecutor.Apply(rows, plan);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
        return result.Values
            .Select(row => row.Name)
            .ToArray();
    }

    private sealed record QueryRow(
        string Name,
        int Score,
        int Priority,
        string Group,
        int? OptionalScore = null,
        string DisplayScore = "");

    private sealed record VocabularyFixture(
        RowQueryVocabulary<QueryRow> Vocabulary,
        RowQueryKeyIdentity ScoreKeyIdentity,
        RowQueryKeyIdentity NameKeyIdentity,
        RowQueryNamedOrderIdentity ScoreOrderIdentity,
        RowQueryNamedOrderIdentity SequenceOrderIdentity);

    private sealed class SentinelException(string message)
        : Exception(message);
}
