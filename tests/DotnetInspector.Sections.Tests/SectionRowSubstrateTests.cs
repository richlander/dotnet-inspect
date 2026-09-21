using System.Collections;

namespace DotnetInspector.Sections.Tests;

public sealed class SectionRowSubstrateTests
{
    private static readonly RowSetIdentity Primary =
        new("primary");
    private static readonly RowSetIdentity Words =
        new("words");
    private static readonly RowSetIdentity Secondary =
        new("secondary");
    private static readonly RowSetIdentity Later =
        new("later");

    [Fact]
    public void HeterogeneousCompleteSourceCohortsPreserveTypedRowsAndCount()
    {
        SectionRowSchemaIdentity<NumberRow> numbers =
            SectionRowSchemaIdentity<NumberRow>.Create();
        SectionRowSchemaIdentity<WordRow> words =
            SectionRowSchemaIdentity<WordRow>.Create();
        NumberRow[] primaryRows =
        [
            new(1),
            new(3),
            new(2),
        ];
        var declarations =
            new List<
                SectionRowSetDeclaration<
                    RowSetIdentity,
                    TestProjection>>
            {
                new SectionRowSetDeclaration<
                    RowSetIdentity,
                    TestProjection,
                    NumberRow>(
                        Primary,
                        numbers,
                        primaryRows,
                        static (projection, rows) =>
                            projection with
                            {
                                Primary =
                                    rows.Select(
                                        static row => row.Value)
                                        .ToArray(),
                                BindingOrder =
                                    [
                                        .. projection.BindingOrder,
                                        "primary",
                                    ],
                            }),
                new SectionRowSetDeclaration<
                    RowSetIdentity,
                    TestProjection,
                    WordRow>(
                        Words,
                        words,
                        [new("first"), new("second")],
                        static (projection, rows) =>
                            projection with
                            {
                                Words =
                                    rows.Select(
                                        static row => row.Value)
                                        .ToArray(),
                                BindingOrder =
                                    [
                                        .. projection.BindingOrder,
                                        "words",
                                    ],
                            }),
                new SectionRowSetDeclaration<
                    RowSetIdentity,
                    TestProjection,
                    NumberRow>(
                        Secondary,
                        numbers,
                        [new(4), new(6), new(5)],
                        static (projection, rows) =>
                            projection with
                            {
                                Secondary =
                                    rows.Select(
                                        static row => row.Value)
                                        .ToArray(),
                                BindingOrder =
                                    [
                                        .. projection.BindingOrder,
                                        "secondary",
                                    ],
                            }),
            };

        int numberResolverCalls = 0;
        var numberBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                NumberRow>(
                    numbers,
                    sequences =>
                        RowsCohortExecutor.Apply(
                            sequences,
                            Plan(
                                RowSelectionStage<string>.Top(
                                    2,
                                    "descending")),
                            order =>
                            {
                                Assert.Equal(
                                    "descending",
                                    order);
                                numberResolverCalls++;
                                return Comparer<NumberRow>.Create(
                                    static (left, right) =>
                                        right.Value.CompareTo(
                                            left.Value));
                            }));
        var wordBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                WordRow>(
                    words,
                    sequences =>
                        RowsCohortExecutor.Apply(
                            sequences,
                            Plan(
                                RowSelectionStage<string>.Head(
                                    1))));
        var associationRowSets =
            new List<RowSetIdentity>
            {
                Words,
                Secondary,
                Primary,
            };
        var bindings =
            new List<SectionRowSchemaBinding<RowSetIdentity>>
            {
                wordBinding,
                numberBinding,
            };
        var association =
            new SectionRowIntentAssociation<RowSetIdentity>(
                associationRowSets,
                bindings);
        SectionRowExecutionRequest<
            RowSetIdentity,
            TestProjection> request =
                SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        declarations,
                        association);

        declarations.Clear();
        associationRowSets.Clear();
        bindings.Clear();
        primaryRows[0] = new(100);

        Assert.Equal(
            [Primary, Words, Secondary],
            request.RowSets.Select(
                static rowSet => rowSet.Identity));
        Assert.Equal(2, request.Cohorts.Count);
        Assert.Equal(
            [Primary, Secondary],
            request.Cohorts[0].RowSets);
        Assert.Equal(
            [Words],
            request.Cohorts[1].RowSets);
        Assert.Same(
            request.IntentBinding,
            request.Cohorts[0].IntentBinding);
        Assert.Same(
            request.IntentBinding,
            request.Cohorts[1].IntentBinding);
        Assert.NotSame(
            request.Cohorts[0].Identity,
            request.Cohorts[1].Identity);
        Assert.Same(numbers, request.Cohorts[0].Schema);
        Assert.Same(words, request.Cohorts[1].Schema);

        Assert.True(
            request.TryGetSequenceKey(
                Primary,
                out RowSequenceKey? primaryKey));
        Assert.True(
            request.TryGetSequenceKey(
                Words,
                out RowSequenceKey? wordsKey));
        Assert.True(
            request.TryGetSequenceKey(
                Secondary,
                out RowSequenceKey? secondaryKey));
        Assert.Equal(0, primaryKey.Value);
        Assert.Equal(1, wordsKey.Value);
        Assert.Equal(2, secondaryKey.Value);
        Assert.True(
            request.TryGetRowSetIdentity(
                secondaryKey,
                out RowSetIdentity? reboundIdentity));
        Assert.Equal(Secondary, reboundIdentity);

        SectionRowsOutcome<RowSetIdentity, TestProjection>
            selected =
                SectionRowExecutor.ApplyRows(request);

        Assert.True(selected.IsSuccess);
        Assert.Null(selected.Failure);
        Assert.Equal(1, numberResolverCalls);
        Assert.Equal(
            [Primary, Words, Secondary],
            selected.RowSets.Select(
                static rowSet => rowSet.Identity));
        var selectedPrimary =
            Assert.IsType<
                SectionRowSetResult<
                    RowSetIdentity,
                    TestProjection,
                    NumberRow>>(selected.RowSets[0]);
        var selectedWords =
            Assert.IsType<
                SectionRowSetResult<
                    RowSetIdentity,
                    TestProjection,
                    WordRow>>(selected.RowSets[1]);
        var selectedSecondary =
            Assert.IsType<
                SectionRowSetResult<
                    RowSetIdentity,
                    TestProjection,
                    NumberRow>>(selected.RowSets[2]);
        Assert.Equal(
            [3, 2],
            selectedPrimary.Rows.Select(
                static row => row.Value));
        Assert.Equal(
            ["first"],
            selectedWords.Rows.Select(
                static row => row.Value));
        Assert.Equal(
            [6, 5],
            selectedSecondary.Rows.Select(
                static row => row.Value));
        Assert.Same(
            primaryRows[1],
            selectedPrimary.Rows[0]);
        AssertReadOnly(selected.RowSets);
        AssertReadOnly(selectedPrimary.Rows);

        TestProjection projection =
            selected.Rebind(TestProjection.Empty);
        Assert.Equal([3, 2], projection.Primary);
        Assert.Equal(["first"], projection.Words);
        Assert.Equal([6, 5], projection.Secondary);
        Assert.Equal(
            ["primary", "words", "secondary"],
            projection.BindingOrder);

        SectionCountOutcome<RowSetIdentity, string> count =
            SectionRowExecutor.ApplyCount<
                RowSetIdentity,
                TestProjection,
                string>(request);

        Assert.Equal(2, numberResolverCalls);
        var completed =
            Assert.IsType<
                SectionCountOutcome<
                    RowSetIdentity,
                    string>.Completed>(count);
        Assert.Equal(
            [Primary, Words, Secondary],
            completed.Counts.Select(
                static entry => entry.Identity));
        Assert.Equal(
            [2, 1, 2],
            completed.Counts.Select(
                static entry => entry.Value));
    }

    [Fact]
    public void SectionRowSingleAssociationRejectsInvalidBinding()
    {
        SectionRowSchemaIdentity<NumberRow> numbers =
            SectionRowSchemaIdentity<NumberRow>.Create();
        SectionRowSchemaIdentity<WordRow> words =
            SectionRowSchemaIdentity<WordRow>.Create();
        int executorCalls = 0;
        var numberBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                NumberRow>(
                    numbers,
                    sequences =>
                    {
                        executorCalls++;
                        return RowsCohortExecutor.Apply(
                            sequences,
                            RowSelectionPlan<string>.Empty);
                    });
        var wordBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                WordRow>(
                    words,
                    sequences =>
                    {
                        executorCalls++;
                        return RowsCohortExecutor.Apply(
                            sequences,
                            RowSelectionPlan<string>.Empty);
                    });
        SectionRowSetDeclaration<
            RowSetIdentity,
            TestProjection,
            NumberRow> primary =
                NumberDeclaration(
                    Primary,
                    numbers,
                    [new(1)]);
        SectionRowSetDeclaration<
            RowSetIdentity,
            TestProjection,
            WordRow> wordSet =
                WordDeclaration(
                    Words,
                    words,
                    [new("first")]);
        SectionRowSetDeclaration<
            RowSetIdentity,
            TestProjection>[] declarations =
        [
            primary,
            wordSet,
        ];

        Assert.Throws<ArgumentException>(
            () => SectionRowExecutionRequest<
                RowSetIdentity,
                TestProjection>.Create(
                    [],
                    Association(
                        [Primary],
                        [numberBinding])));
        Assert.Contains(
            "duplicated",
            Assert.Throws<ArgumentException>(
                () => SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        [primary, primary],
                        Association(
                            [Primary],
                            [numberBinding])))
                .Message);
        Assert.Contains(
            "unknown",
            Assert.Throws<ArgumentException>(
                () => SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        declarations,
                        Association(
                            [Primary, Later],
                            [numberBinding, wordBinding])))
                .Message);
        Assert.Contains(
            "more than once",
            Assert.Throws<ArgumentException>(
                () => SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        declarations,
                        Association(
                            [Primary, Primary, Words],
                            [numberBinding, wordBinding])))
                .Message);
        Assert.Contains(
            "no row-intent association",
            Assert.Throws<ArgumentException>(
                () => SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        declarations,
                        Association(
                            [Primary],
                            [numberBinding, wordBinding])))
                .Message);
        Assert.Contains(
            "more than one execution binding",
            Assert.Throws<ArgumentException>(
                () => SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        declarations,
                        Association(
                            [Primary, Words],
                            [
                                numberBinding,
                                numberBinding,
                                wordBinding,
                            ])))
                .Message);
        Assert.Contains(
            "has no execution binding",
            Assert.Throws<ArgumentException>(
                () => SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        declarations,
                        Association(
                            [Primary, Words],
                            [numberBinding])))
                .Message);

        SectionRowSchemaIdentity<WordRow> unused =
            SectionRowSchemaIdentity<WordRow>.Create();
        var unusedBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                WordRow>(
                    unused,
                    sequences =>
                    {
                        executorCalls++;
                        return RowsCohortExecutor.Apply(
                            sequences,
                            RowSelectionPlan<string>.Empty);
                    });
        Assert.Contains(
            "not used",
            Assert.Throws<ArgumentException>(
                () => SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        declarations,
                        Association(
                            [Primary, Words],
                            [
                                numberBinding,
                                wordBinding,
                                unusedBinding,
                            ])))
                .Message);

        Assert.Equal(0, executorCalls);
    }

    [Fact]
    public void CrossCohortCompleteSourceFailureIsAtomic()
    {
        SectionRowSchemaIdentity<NumberRow> numbers =
            SectionRowSchemaIdentity<NumberRow>.Create();
        SectionRowSchemaIdentity<WordRow> words =
            SectionRowSchemaIdentity<WordRow>.Create();
        SectionRowSchemaIdentity<DecimalRow> decimals =
            SectionRowSchemaIdentity<DecimalRow>.Create();
        int numberCalls = 0;
        int wordCalls = 0;
        int decimalCalls = 0;
        var numberBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                NumberRow>(
                    numbers,
                    sequences =>
                    {
                        numberCalls++;
                        return RowsCohortExecutor.Apply(
                            sequences,
                            RowSelectionPlan<string>.Empty);
                    });
        var wordBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                WordRow>(
                    words,
                    sequences =>
                    {
                        wordCalls++;
                        return RowsCohortExecutor.Apply(
                            sequences,
                            Plan(
                                RowSelectionStage<string>.Window(
                                    1,
                                    2)));
                    });
        var decimalBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                DecimalRow>(
                    decimals,
                    sequences =>
                    {
                        decimalCalls++;
                        return RowsCohortExecutor.Apply(
                            sequences,
                            RowSelectionPlan<string>.Empty);
                    });
        SectionRowExecutionRequest<
            RowSetIdentity,
            TestProjection> request =
                SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        [
                            NumberDeclaration(
                                Primary,
                                numbers,
                                [new(1)]),
                            WordDeclaration(
                                Words,
                                words,
                                [new("only")]),
                            DecimalDeclaration(
                                Later,
                                decimals,
                                [new(1.5m)]),
                        ],
                        Association(
                            [Primary, Words, Later],
                            [
                                decimalBinding,
                                wordBinding,
                                numberBinding,
                            ]));

        SectionRowsOutcome<RowSetIdentity, TestProjection>
            selected =
                SectionRowExecutor.ApplyRows(request);

        Assert.False(selected.IsSuccess);
        Assert.Empty(selected.RowSets);
        RowsCohortSemanticFailure<RowSetIdentity> failure =
            Assert.IsType<
                RowsCohortSemanticFailure<RowSetIdentity>>(
                    selected.Failure);
        Assert.Equal(Words, failure.Identity);
        Assert.Equal(1, failure.Failure.StageNumber);
        Assert.Equal(2, failure.Failure.RequiredPosition);
        Assert.Equal(1, failure.Failure.AvailableCount);
        Assert.Equal(1, numberCalls);
        Assert.Equal(1, wordCalls);
        Assert.Equal(0, decimalCalls);
        Assert.Throws<InvalidOperationException>(
            () => selected.Rebind(TestProjection.Empty));

        SectionCountOutcome<RowSetIdentity, string> count =
            SectionRowExecutor.ApplyCount<
                RowSetIdentity,
                TestProjection,
                string>(request);

        var semantic =
            Assert.IsType<
                SectionCountOutcome<
                    RowSetIdentity,
                    string>.Semantic>(count);
        Assert.Equal(Words, semantic.Identity);
        Assert.Equal(2, numberCalls);
        Assert.Equal(2, wordCalls);
        Assert.Equal(0, decimalCalls);
    }

    [Fact]
    public void CrossCohortExceptionsPropagateAndSkipLaterWork()
    {
        SectionRowSchemaIdentity<NumberRow> numbers =
            SectionRowSchemaIdentity<NumberRow>.Create();
        SectionRowSchemaIdentity<WordRow> words =
            SectionRowSchemaIdentity<WordRow>.Create();
        SectionRowSchemaIdentity<DecimalRow> decimals =
            SectionRowSchemaIdentity<DecimalRow>.Create();
        var sentinel = new SentinelException();
        int laterCalls = 0;
        var numberBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                NumberRow>(
                    numbers,
                    sequences =>
                        RowsCohortExecutor.Apply(
                            sequences,
                            RowSelectionPlan<string>.Empty));
        var wordBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                WordRow>(
                    words,
                    _ => throw sentinel);
        var decimalBinding =
            new SectionRowSchemaBinding<
                RowSetIdentity,
                DecimalRow>(
                    decimals,
                    sequences =>
                    {
                        laterCalls++;
                        return RowsCohortExecutor.Apply(
                            sequences,
                            RowSelectionPlan<string>.Empty);
                    });
        SectionRowExecutionRequest<
            RowSetIdentity,
            TestProjection> request =
                SectionRowExecutionRequest<
                    RowSetIdentity,
                    TestProjection>.Create(
                        [
                            NumberDeclaration(
                                Primary,
                                numbers,
                                [new(1)]),
                            WordDeclaration(
                                Words,
                                words,
                                [new("first")]),
                            DecimalDeclaration(
                                Later,
                                decimals,
                                [new(1.5m)]),
                        ],
                        Association(
                            [Primary, Words, Later],
                            [
                                numberBinding,
                                wordBinding,
                                decimalBinding,
                            ]));

        SentinelException observed =
            Assert.Throws<SentinelException>(
                () => SectionRowExecutor.ApplyRows(request));

        Assert.Same(sentinel, observed);
        Assert.Equal(0, laterCalls);
    }

    private static SectionRowIntentAssociation<RowSetIdentity>
        Association(
            IReadOnlyList<RowSetIdentity> rowSets,
            IReadOnlyList<SectionRowSchemaBinding<RowSetIdentity>>
                bindings) =>
        new(rowSets, bindings);

    private static SectionRowSetDeclaration<
        RowSetIdentity,
        TestProjection,
        NumberRow> NumberDeclaration(
            RowSetIdentity identity,
            SectionRowSchemaIdentity<NumberRow> schema,
            IReadOnlyList<NumberRow> rows) =>
        new(
            identity,
            schema,
            rows,
            static (projection, selected) =>
                projection with
                {
                    Primary =
                        selected.Select(
                            static row => row.Value)
                            .ToArray(),
                });

    private static SectionRowSetDeclaration<
        RowSetIdentity,
        TestProjection,
        WordRow> WordDeclaration(
            RowSetIdentity identity,
            SectionRowSchemaIdentity<WordRow> schema,
            IReadOnlyList<WordRow> rows) =>
        new(
            identity,
            schema,
            rows,
            static (projection, selected) =>
                projection with
                {
                    Words =
                        selected.Select(
                            static row => row.Value)
                            .ToArray(),
                });

    private static SectionRowSetDeclaration<
        RowSetIdentity,
        TestProjection,
        DecimalRow> DecimalDeclaration(
            RowSetIdentity identity,
            SectionRowSchemaIdentity<DecimalRow> schema,
            IReadOnlyList<DecimalRow> rows) =>
        new(
            identity,
            schema,
            rows,
            static (projection, selected) => projection);

    private static RowSelectionPlan<string> Plan(
        params RowSelectionStage<string>[] stages) =>
        RowSelectionPlan<string>.Create(stages);

    private static void AssertReadOnly<T>(
        IReadOnlyList<T> values)
    {
        IList list =
            Assert.IsAssignableFrom<IList>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => list[0] = values[0]);
    }

    private sealed record RowSetIdentity(string Value);

    private sealed record NumberRow(int Value);

    private sealed record WordRow(string Value);

    private sealed record DecimalRow(decimal Value);

    private sealed class SentinelException : Exception
    {
    }

    private sealed record TestProjection(
        IReadOnlyList<int> Primary,
        IReadOnlyList<string> Words,
        IReadOnlyList<int> Secondary,
        IReadOnlyList<string> BindingOrder)
    {
        public static TestProjection Empty { get; } =
            new([], [], [], []);
    }
}
