using System.Collections;
using System.Globalization;
using QuerySpace.Rows;

namespace QuerySpace.Consumer;

public sealed record ApplicationRow(
    string Name,
    int Score);

public sealed class ApplicationRows : IReadOnlyList<ApplicationRow>
{
    private readonly ApplicationRow[] _rows;

    public ApplicationRows(params ApplicationRow[] rows)
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

public sealed record QuerySpaceConsumerObservation(
    IReadOnlyList<string> Names,
    Type RowType,
    Type CollectionType);

public static class QuerySpaceDirectConsumer
{
    public static QuerySpaceConsumerObservation Execute()
    {
        var rows = new ApplicationRows(
            new("low", 1),
            new("high", 3),
            new("middle", 2));

        RowQueryKey<ApplicationRow> score =
            RowQueryKey<ApplicationRow>.Create(
                RowQueryKeyIdentity.Create(),
                "score",
                [
                    RowQueryOperator.GreaterOrEqual
                ],
                static row =>
                    RowQueryValue<int>.Present(row.Score),
                static (@operator, token) =>
                {
                    if (@operator
                            is not RowQueryOperator.GreaterOrEqual
                        || !int.TryParse(
                            token.Text,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int expected))
                    {
                        return null;
                    }

                    return actual => actual >= expected;
                },
                static direction =>
                    RowQueryValueOrder.Create(
                        Comparer<int>.Default,
                        direction,
                        missingLast: true));

        RowQueryVocabulary<ApplicationRow> descriptor =
            RowQueryVocabulary<ApplicationRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [score],
                []);
        RowQueryIntent request =
            RowQueryIntent.Create(
                [
                    new RowQueryPredicateIntent(
                        "score",
                        RowQueryOperator.GreaterOrEqual,
                        new RowQueryValueToken("2"))
                ],
                RowQueryOrderIntent.Keys(
                    [
                        new RowQueryOrderTermIntent(
                            "score",
                            RowQueryOrderDirection.Descending)
                    ]),
                RowSelectionIntent<RowQueryOrderIntent>.Create(
                    [
                        RowSelectionIntentOperation<
                            RowQueryOrderIntent>.Head(2)
                    ]));
        RowQueryResolutionResult<ApplicationRow> resolution =
            RowQueryResolver.Resolve(
                descriptor,
                request);
        ResolvedRowQueryPlan<ApplicationRow> plan =
            resolution.Plan
            ?? throw new InvalidOperationException(
                $"The direct-consumer request failed: {resolution.Failure?.Reason}.");
        RowSelectionResult<ApplicationRow> result =
            RowQueryExecutor.Apply(
                rows,
                plan);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                "The direct-consumer execution failed.");
        }

        rows.Replace(
            1,
            new ApplicationRow("replacement", 100));

        return new(
            [.. result.Values.Select(static row => row.Name)],
            typeof(ApplicationRow),
            typeof(ApplicationRows));
    }
}
