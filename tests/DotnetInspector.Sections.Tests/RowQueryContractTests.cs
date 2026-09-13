using System.Reflection;
using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections.Tests;

public sealed class RowQueryContractTests
{
    [Fact]
    public void RowQueryResolvesSchemaIdentitiesOnce()
    {
        int binderCalls = 0;
        int scoreOrderCalls = 0;
        int nameOrderCalls = 0;
        RowQuerySchemaIdentity schemaIdentity =
            RowQuerySchemaIdentity.Create();
        RowQueryFieldIdentity scoreIdentity =
            RowQueryFieldIdentity.Create();
        RowQueryNamedOrderIdentity scoreOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
        RowQueryNamedOrderIdentity nameOrderIdentity =
            RowQueryNamedOrderIdentity.Create();
        RowQueryField<QueryRow> score =
            IntField(
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
        RowQuerySchema<QueryRow> schema =
            RowQuerySchema<QueryRow>.Create(
                schemaIdentity,
                [score],
                [scoreOrder, nameOrder]);
        RowQueryResolutionResult<QueryRow> resolution =
            RowQueryResolver.Resolve(
                schema,
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
        Assert.Same(schemaIdentity, plan.SchemaIdentity);
        Assert.Equal([scoreIdentity], plan.PredicateFieldIdentities);
        ResolvedRowQueryOrderBinding baseline =
            Assert.IsType<ResolvedRowQueryOrderBinding>(
                plan.BaselineOrder);
        Assert.Same(
            scoreOrderIdentity,
            baseline.NamedOrderIdentity);
        Assert.Equal(
            RowQueryOrderDirection.Descending,
            baseline.NamedOrderDirection);
        Assert.Empty(baseline.FieldIdentities);
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
        SchemaFixture fixture = Schema();
        RowQueryResolutionResult<QueryRow> resolution =
            RowQueryResolver.Resolve(
                fixture.Schema,
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
        SchemaFixture fixture = Schema();
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
                    fixture.Schema,
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
    public void EffectiveBaselineOrderFollowsPrecedence()
    {
        QueryRow[] rows =
        [
            new("B", 2, 1, "x"),
            new("A", 1, 1, "x"),
            new("C", 2, 1, "x")
        ];
        SchemaFixture withDefault =
            Schema(defaultBaseline: "score-order");
        ResolvedRowQueryPlan<QueryRow> defaultPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    withDefault.Schema,
                    RowQueryIntent.Empty));
        ResolvedRowQueryPlan<QueryRow> explicitPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    withDefault.Schema,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "name-order",
                                RowQueryOrderDirection.Descending))));
        SchemaFixture withoutDefault = Schema();
        ResolvedRowQueryPlan<QueryRow> incomingPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    withoutDefault.Schema,
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
        SchemaFixture fixture = Schema();
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
                    fixture.Schema,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "name-order",
                                RowQueryOrderDirection.Ascending),
                        selection: stages)));
        ResolvedRowQueryPlan<QueryRow> scoreBaseline =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Schema,
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
        SchemaFixture fixture = Schema();
        ResolvedRowQueryPlan<QueryRow> plan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Schema,
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
                                    RowQueryOrderIntent.Fields(
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
            [fixture.NameFieldIdentity],
            plan.ResolvedOrders[1].FieldIdentities);
        Assert.Equal(
            [RowQueryOrderDirection.Descending],
            plan.ResolvedOrders[1].FieldDirections);
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
        SchemaFixture baselineOnly =
            Schema(defaultBaseline: "name-order");
        RowQueryResolutionResult<QueryRow> missingTop =
            RowQueryResolver.Resolve(
                baselineOnly.Schema,
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

        SchemaFixture topOnly =
            Schema(defaultTop: "score-order");
        ResolvedRowQueryPlan<QueryRow> topOnlyPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    topOnly.Schema,
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

        SchemaFixture both =
            Schema(
                defaultBaseline: "score-order",
                defaultTop: "score-order");
        ResolvedRowQueryPlan<QueryRow> bothPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    both.Schema,
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
        SchemaFixture fixture = Schema();

        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Schema,
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
            RowQueryFailureReason.UnknownField);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Schema,
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
                fixture.Schema,
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
                fixture.Schema,
                Intent(
                    baseline:
                        RowQueryOrderIntent.Fields(
                        [
                            new(
                                "predicate-only",
                                RowQueryOrderDirection.Ascending)
                        ]))),
            RowQueryOperationKind.BaselineOrder,
            1,
            RowQueryFailureReason.UnsupportedFieldOrder,
            termPosition: 1);
        AssertFailure(
            RowQueryResolver.Resolve(
                fixture.Schema,
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
                fixture.Schema,
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
                fixture.Schema,
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
                fixture.Schema,
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
                fixture.Schema,
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
        SchemaFixture fixture = Schema();
        RowQueryResolutionResult<QueryRow> resolution =
            RowQueryResolver.Resolve(
                fixture.Schema,
                Intent(
                    baseline:
                        RowQueryOrderIntent.Fields(
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
            fixture.Schema.Identity,
            failure.SchemaIdentity);
        Assert.Equal(
            RowQueryOperationKind.BaselineOrder,
            failure.OperationKind);
        Assert.Equal(1, failure.OperationPosition);
        Assert.Equal(2, failure.TermPosition);
        Assert.Null(failure.SemanticStageNumber);
        Assert.Equal(
            RowQueryFailureReason.UnknownField,
            failure.Reason);
        Assert.Null(failure.FieldIdentity);
        Assert.Null(failure.NamedOrderIdentity);

        PropertyInfo[] properties =
            typeof(RowQueryFailure).GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);
        Assert.Equal(
            [
                nameof(RowQueryFailure.FieldIdentity),
                nameof(RowQueryFailure.NamedOrderIdentity),
                nameof(RowQueryFailure.OperationKind),
                nameof(RowQueryFailure.OperationPosition),
                nameof(RowQueryFailure.Reason),
                nameof(RowQueryFailure.SchemaIdentity),
                nameof(RowQueryFailure.SemanticStageNumber),
                nameof(RowQueryFailure.TermPosition)
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

        RowQueryFailure knownField =
            Assert.IsType<RowQueryFailure>(
                RowQueryResolver.Resolve(
                    fixture.Schema,
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
            fixture.ScoreFieldIdentity,
            knownField.FieldIdentity);
        Assert.Null(knownField.NamedOrderIdentity);

        RowQueryFailure knownOrder =
            Assert.IsType<RowQueryFailure>(
                RowQueryResolver.Resolve(
                    fixture.Schema,
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
        Assert.Null(knownOrder.FieldIdentity);
        Assert.Same(
            fixture.SequenceOrderIdentity,
            knownOrder.NamedOrderIdentity);
    }

    [Fact]
    public void RowQueryExecutionFailuresStayVisible()
    {
        var accessorException =
            new SentinelException("accessor");
        RowQueryField<QueryRow> accessorField =
            RowQueryField<QueryRow>.Create(
                RowQueryFieldIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                _ => throw accessorException,
                IntBinder);
        ResolvedRowQueryPlan<QueryRow> accessorPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQuerySchema<QueryRow>.Create(
                        RowQuerySchemaIdentity.Create(),
                        [accessorField],
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
        RowQueryField<QueryRow> predicateField =
            RowQueryField<QueryRow>.Create(
                RowQueryFieldIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                row => RowQueryValue<int>.Present(row.Score),
                (_, _) => _ => throw predicateException);
        ResolvedRowQueryPlan<QueryRow> predicatePlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQuerySchema<QueryRow>.Create(
                        RowQuerySchemaIdentity.Create(),
                        [predicateField],
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

        var comparerException =
            new SentinelException("comparer");
        RowQueryField<QueryRow> comparerField =
            RowQueryField<QueryRow>.Create(
                RowQueryFieldIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                row => RowQueryValue<int>.Present(row.Score),
                IntBinder,
                _ => Comparer<RowQueryValue<int>>.Create(
                    (_, _) => throw comparerException));
        ResolvedRowQueryPlan<QueryRow> comparerPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    RowQuerySchema<QueryRow>.Create(
                        RowQuerySchemaIdentity.Create(),
                        [comparerField],
                        []),
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Fields(
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
        RowQuerySchema<QueryRow> resolverSchema =
            RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [],
                [throwingOrder]);
        ResolvedRowQueryPlan<QueryRow> deferredPlan =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    resolverSchema,
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
                    resolverSchema,
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
        SchemaFixture fixture = Schema();
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
                    fixture.Schema,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Named(
                                "score-order",
                                RowQueryOrderDirection.Ascending))));
        ResolvedRowQueryPlan<QueryRow> descending =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Schema,
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
    public void FieldOrdersComposeLexicographicallyAndPreserveTies()
    {
        SchemaFixture fixture = Schema();
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
                    fixture.Schema,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Fields(
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
        RowQueryField<QueryRow> field =
            RowQueryField<QueryRow>.Create(
                RowQueryFieldIdentity.Create(),
                "value",
                [RowQueryOperator.Equals],
                row => RowQueryValue<int>.Present(row.Score),
                (_, _) => throw binderException);
        RowQuerySchema<QueryRow> schema =
            RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [field],
                []);

        SentinelException observed =
            Assert.Throws<SentinelException>(
                () => RowQueryResolver.Resolve(
                    schema,
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
    public void MissingValuePlacementIsSchemaDefinedAcrossDirections()
    {
        SchemaFixture fixture = Schema();
        QueryRow[] rows =
        [
            new("A", 1, 1, "x", 2),
            new("B", 1, 1, "x", null),
            new("C", 1, 1, "x", 1)
        ];
        ResolvedRowQueryPlan<QueryRow> ascending =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Schema,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Fields(
                            [
                                new(
                                    "optional",
                                    RowQueryOrderDirection.Ascending)
                            ]))));
        ResolvedRowQueryPlan<QueryRow> descending =
            AssertSuccess(
                RowQueryResolver.Resolve(
                    fixture.Schema,
                    Intent(
                        baseline:
                            RowQueryOrderIntent.Fields(
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
    public void RowQuerySchemasRejectInvalidDeclarations()
    {
        RowQueryFieldIdentity fieldIdentity =
            RowQueryFieldIdentity.Create();
        RowQueryField<QueryRow> field =
            IntField(
                fieldIdentity,
                "score",
                row => row.Score);
        RowQueryField<QueryRow> duplicateKey =
            IntField(
                RowQueryFieldIdentity.Create(),
                "score",
                row => row.Score);
        RowQueryField<QueryRow> duplicateIdentity =
            IntField(
                fieldIdentity,
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
            () => RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [field, duplicateKey],
                [ranking]));
        Assert.Throws<ArgumentException>(
            () => RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [field, duplicateIdentity],
                [ranking]));
        Assert.Throws<ArgumentException>(
            () => RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [field],
                [ranking, duplicateOrderKey]));
        Assert.Throws<ArgumentException>(
            () => RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [field],
                [ranking, duplicateOrderIdentity]));
        Assert.Throws<ArgumentException>(
            () => RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [field],
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
            () => RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
                [field],
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

    private static SchemaFixture Schema(
        string? defaultBaseline = null,
        string? defaultTop = null)
    {
        RowQueryFieldIdentity scoreIdentity =
            RowQueryFieldIdentity.Create();
        RowQueryFieldIdentity nameIdentity =
            RowQueryFieldIdentity.Create();
        RowQueryField<QueryRow> score =
            IntField(
                scoreIdentity,
                "score",
                row => row.Score);
        RowQueryField<QueryRow> priority =
            IntField(
                RowQueryFieldIdentity.Create(),
                "priority",
                row => row.Priority);
        RowQueryField<QueryRow> group =
            TextField(
                RowQueryFieldIdentity.Create(),
                "group",
                row => row.Group);
        RowQueryField<QueryRow> name =
            TextField(
                nameIdentity,
                "name",
                row => row.Name);
        RowQueryField<QueryRow> optional =
            RowQueryField<QueryRow>.Create(
                RowQueryFieldIdentity.Create(),
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
        RowQueryField<QueryRow> predicateOnly =
            RowQueryField<QueryRow>.Create(
                RowQueryFieldIdentity.Create(),
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
            RowQuerySchema<QueryRow>.Create(
                RowQuerySchemaIdentity.Create(),
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

    private static RowQueryField<QueryRow> IntField(
        RowQueryFieldIdentity identity,
        string key,
        Func<QueryRow, int> accessor,
        Action? onBind = null) =>
        RowQueryField<QueryRow>.Create(
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

    private static RowQueryField<QueryRow> TextField(
        RowQueryFieldIdentity identity,
        string key,
        Func<QueryRow, string> accessor) =>
        RowQueryField<QueryRow>.Create(
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

    private sealed record SchemaFixture(
        RowQuerySchema<QueryRow> Schema,
        RowQueryFieldIdentity ScoreFieldIdentity,
        RowQueryFieldIdentity NameFieldIdentity,
        RowQueryNamedOrderIdentity ScoreOrderIdentity,
        RowQueryNamedOrderIdentity SequenceOrderIdentity);

    private sealed class SentinelException(string message)
        : Exception(message);
}
