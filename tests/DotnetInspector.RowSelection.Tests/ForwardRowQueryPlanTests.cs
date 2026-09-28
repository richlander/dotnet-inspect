using QuerySpace.Rows;

namespace DotnetInspector.RowSelection.Tests;

public sealed class ForwardRowQueryPlanTests
{
    [Fact]
    public void ForwardPlanMatchesReferenceForPredicatesAndHead()
    {
        ResolvedRowQueryPlan<TextRow> resolved =
            Resolve(
                [
                    new(
                        "Text",
                        RowQueryOperator.Equals,
                        new("*hit*")),
                ],
                selection:
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Head(2),
                ]);
        Assert.True(
            ForwardRowQueryPlanner.TryCreate(
                resolved,
                out ForwardRowQueryPlan<TextRow>? forward));
        Assert.Same(resolved, forward.ResolvedPlan);
        Assert.Equal(2, forward.MaximumResultRows);

        TextRow[] source =
        [
            new("miss"),
            new("first-hit"),
            new("other"),
            new("second-hit"),
            new("unrequested-hit"),
        ];
        var selected = new List<TextRow>();
        int scanned = 0;
        foreach (TextRow row in source)
        {
            scanned++;
            if (!forward.Matches(row))
                continue;

            selected.Add(row);
            if (forward.IsSatisfied(selected.Count))
                break;
        }

        RowSelectionResult<TextRow> reference =
            RowQueryExecutor.Apply(source, resolved);
        Assert.True(reference.IsSuccess);
        Assert.Equal(reference.Values, selected);
        Assert.Equal(4, scanned);
    }

    [Fact]
    public void ForwardPlanPreservesUnboundedPredicateExecution()
    {
        ResolvedRowQueryPlan<TextRow> resolved =
            Resolve(
                [
                    new(
                        "Text",
                        RowQueryOperator.NotEquals,
                        new("skip")),
                ]);
        Assert.True(
            ForwardRowQueryPlanner.TryCreate(
                resolved,
                out ForwardRowQueryPlan<TextRow>? forward));
        Assert.Null(forward.MaximumResultRows);
        Assert.False(forward.IsSatisfied(int.MaxValue));

        TextRow[] source =
        [
            new("keep"),
            new("skip"),
            new("also-keep"),
        ];
        TextRow[] selected =
        [
            .. source.Where(forward.Matches),
        ];
        RowSelectionResult<TextRow> reference =
            RowQueryExecutor.Apply(source, resolved);

        Assert.True(reference.IsSuccess);
        Assert.Equal(reference.Values, selected);
    }

    [Fact]
    public void ForwardPlanCollapsesHeadOnlyStages()
    {
        ResolvedRowQueryPlan<TextRow> resolved =
            Resolve(
                [],
                selection:
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Head(4),
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Head(2),
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Head(3),
                ]);

        Assert.True(
            ForwardRowQueryPlanner.TryCreate(
                resolved,
                out ForwardRowQueryPlan<TextRow>? forward));
        Assert.Equal(2, forward.MaximumResultRows);
        Assert.False(forward.IsSatisfied(1));
        Assert.True(forward.IsSatisfied(2));
        Assert.True(forward.IsSatisfied(3));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => forward.IsSatisfied(-1));
    }

    [Fact]
    public void ForwardPlanRejectsWorkThatNeedsCompletePopulation()
    {
        ResolvedRowQueryPlan<TextRow>[] plans =
        [
            Resolve(
                [],
                baseline: RowQueryOrderIntent.Keys(
                [
                    new(
                        "Text",
                        RowQueryOrderDirection.Ascending),
                ])),
            Resolve(
                [],
                selection:
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Tail(1),
                ]),
            Resolve(
                [],
                selection:
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Window(1, 2),
                ]),
            Resolve(
                [],
                selection:
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Top(
                            1,
                            RowQueryOrderIntent.Keys(
                            [
                                new(
                                    "Text",
                                    RowQueryOrderDirection.Ascending),
                            ])),
                ]),
            Resolve(
                [],
                selection:
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Head(2),
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Tail(1),
                ]),
        ];

        foreach (ResolvedRowQueryPlan<TextRow> plan in plans)
        {
            Assert.False(
                ForwardRowQueryPlanner.TryCreate(
                    plan,
                    out ForwardRowQueryPlan<TextRow>? forward));
            Assert.Null(forward);
        }

        Assert.Throws<ArgumentNullException>(
            () => ForwardRowQueryPlanner.TryCreate<TextRow>(
                null!,
                out _));
    }

    private static ResolvedRowQueryPlan<TextRow> Resolve(
        IReadOnlyList<RowQueryPredicateIntent> predicates,
        RowQueryOrderIntent? baseline = null,
        IReadOnlyList<
            RowSelectionIntentOperation<RowQueryOrderIntent>>? selection =
                null)
    {
        RowQueryVocabulary<TextRow> vocabulary =
            RowQueryVocabulary<TextRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [
                    RowQueryText.Key<TextRow>(
                        "Text",
                        static row => row.Text),
                ],
                []);
        RowQueryResolutionResult<TextRow> resolution =
            RowQueryResolver.Resolve(
                vocabulary,
                RowQueryIntent.Create(
                    predicates,
                    baseline,
                    RowSelectionIntent<RowQueryOrderIntent>.Create(
                        selection ?? [])));
        Assert.True(resolution.IsSuccess);
        return Assert.IsType<ResolvedRowQueryPlan<TextRow>>(
            resolution.Plan);
    }

    private sealed record TextRow(string Text);
}
