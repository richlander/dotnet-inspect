using System.Collections;
using System.Globalization;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace QueryOverflow.Consumer;

public sealed record ApplicationRow(
    string Name,
    int Score);

public sealed class ApplicationBatch :
    IReadOnlyList<ApplicationRow>
{
    private readonly ApplicationRow[] _rows;

    public ApplicationBatch(params ApplicationRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = [.. rows];
    }

    public int Count => _rows.Length;

    public ApplicationRow this[int index] => _rows[index];

    public void Replace(
        int index,
        ApplicationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows[index] = row;
    }

    public IEnumerator<ApplicationRow> GetEnumerator() =>
        ((IEnumerable<ApplicationRow>)_rows).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

public sealed record QueryOverflowConsumerObservation(
    IReadOnlyList<string> Names,
    int Count,
    Type RowType,
    Type BatchType);

public static class QueryOverflowDirectConsumer
{
    public static QueryOverflowConsumerObservation Execute()
    {
        ResolvedRowQueryPlan<ApplicationRow> rowsPlan =
            Resolve(
                predicates: [],
                selection:
                [
                    RowSelectionIntentOperation<
                        RowQueryOrderIntent>.Head(3),
                ]);
        QueryOverflowExecution<ApplicationRow> rows =
            RequirePlan(
                rowsPlan,
                QuerySpaceTerminalRequirement.Rows)
            .Start();

        var first = new ApplicationBatch(
            new("one", 1),
            new("two", 2));
        QueryOverflowStep<ApplicationRow> firstStep =
            Advance(rows, first, sourceCompleted: false);
        first.Replace(0, new("replacement", 100));

        var second = new ApplicationBatch(
            new ApplicationRow("three", 3));
        QueryOverflowStep<ApplicationRow> secondStep =
            Advance(rows, second, sourceCompleted: false);

        ResolvedRowQueryPlan<ApplicationRow> countPlan =
            Resolve(
                predicates:
                [
                    new(
                        "score",
                        RowQueryOperator.GreaterOrEqual,
                        new("2")),
                ],
                selection: []);
        QueryOverflowExecution<ApplicationRow> count =
            RequirePlan(
                countPlan,
                QuerySpaceTerminalRequirement.Count)
            .Start();
        QueryOverflowStep<ApplicationRow> partialCount =
            Advance(
                count,
                new ApplicationBatch(
                    new("low", 1),
                    new("high", 3)),
                sourceCompleted: false);
        if (partialCount.HasCount)
        {
            throw new InvalidOperationException(
                "Count completed before its Head witness.");
        }

        QueryOverflowStep<ApplicationRow> countStep =
            Advance(
                count,
                new ApplicationBatch(
                    new ApplicationRow("middle", 2)),
                sourceCompleted: true);

        return new(
            [
                .. firstStep.Rows
                    .Concat(secondStep.Rows)
                    .Select(static row => row.Name),
            ],
            countStep.Count,
            typeof(ApplicationRow),
            typeof(ApplicationBatch));
    }

    private static QueryOverflowStep<ApplicationRow> Advance(
        QueryOverflowExecution<ApplicationRow> execution,
        ApplicationBatch batch,
        bool sourceCompleted)
    {
        if (!execution.TryRequestInput(
                batch.Count,
                batch.Count,
                out QueryOverflowInputRequest request))
        {
            throw new InvalidOperationException(
                "The execution did not request the expected batch.");
        }

        return execution.Advance(
            request,
            batch,
            sourceCompleted);
    }

    private static QueryOverflowPlan<ApplicationRow> RequirePlan(
        ResolvedRowQueryPlan<ApplicationRow> rowPlan,
        QuerySpaceTerminalRequirement terminal)
    {
        QueryOverflowAdmission<ApplicationRow> admission =
            terminal is QuerySpaceTerminalRequirement.Rows
                ? QueryOverflowPlan<ApplicationRow>.AdmitRows(
                    rowPlan,
                    static row => row)
                : QueryOverflowPlan<ApplicationRow>.AdmitCount(
                    rowPlan);
        return admission.Plan
            ?? throw new InvalidOperationException(
                $"The consumer plan was declined: {admission.DeclineReason}.");
    }

    private static ResolvedRowQueryPlan<ApplicationRow> Resolve(
        IReadOnlyList<RowQueryPredicateIntent> predicates,
        IReadOnlyList<
            RowSelectionIntentOperation<RowQueryOrderIntent>>
            selection)
    {
        RowQueryKey<ApplicationRow> score =
            RowQueryKey<ApplicationRow>.Create(
                RowQueryKeyIdentity.Create(),
                "score",
                [RowQueryOperator.GreaterOrEqual],
                static row =>
                    RowQueryValue<int>.Present(row.Score),
                static (@operator, token) =>
                    @operator
                        is RowQueryOperator.GreaterOrEqual
                    && int.TryParse(
                        token.Text,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int expected)
                        ? actual => actual >= expected
                        : null);
        RowQueryVocabulary<ApplicationRow> vocabulary =
            RowQueryVocabulary<ApplicationRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [score],
                []);
        RowQueryResolutionResult<ApplicationRow> resolution =
            RowQueryResolver.Resolve(
                vocabulary,
                RowQueryIntent.Create(
                    predicates,
                    baselineOrder: null,
                    RowSelectionIntent<
                        RowQueryOrderIntent>.Create(
                            selection)));
        return resolution.Plan
            ?? throw new InvalidOperationException(
                $"The consumer query failed: {resolution.Failure?.Reason}.");
    }
}
