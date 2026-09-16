using DotnetInspector.RowSelection;

namespace DotnetInspector.RowSelectionConsumer;

public sealed record ReferenceEvaluatorObservation(
    IReadOnlyList<int> Values,
    IReadOnlyList<int> RankedValues,
    IReadOnlyList<int> NamedKeys,
    IReadOnlyList<IReadOnlyList<int>> NamedValues,
    bool FailureIsSuccess,
    int FailureKey,
    int FailureStage,
    int FailureRequiredPosition,
    int FailureAvailableCount);

public static class ReferenceEvaluatorConsumer
{
    public static ReferenceEvaluatorObservation Evaluate()
    {
        RowSelectionPlan<string> plan =
            RowSelectionPlan<string>.Create(
                [
                    RowSelectionStage<string>.Window(
                        3,
                        6),
                    RowSelectionStage<string>.Tail(2),
                ]);
        RowSelectionResult<int> result =
            RowSelectionExecutor.Apply(
                new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                plan);

        RowSelectionResult<int> ranked =
            RowSelectionExecutor.Apply(
                new[] { 4, 1, 3, 2 },
                RowSelectionPlan<string>.Create(
                    [
                        RowSelectionStage<string>.Top(
                            3,
                            "ascending"),
                    ]),
                comparerResolver:
                    _ => Comparer<int>.Default);

        NamedRowSequence<int> first =
            NamedRowSequence<int>.Create(
                RowSequenceKey.Create(10),
                [1, 2, 3]);
        NamedRowSequence<int> second =
            NamedRowSequence<int>.Create(
                RowSequenceKey.Create(20),
                [4, 5, 6]);
        NamedRowSelectionResult<int> named =
            RowSelectionExecutor.ApplyNamed(
                [first, second],
                RowSelectionPlan<string>.Create(
                    [
                        RowSelectionStage<string>.Head(2),
                    ]));

        NamedRowSelectionResult<int> failure =
            RowSelectionExecutor.ApplyNamed(
                [first, second],
                RowSelectionPlan<string>.Create(
                    [
                        RowSelectionStage<string>.Head(2),
                        RowSelectionStage<string>.Window(
                            2,
                            3),
                    ]),
                comparerResolver: null);
        NamedRowWindowFailure namedFailure =
            failure.Failure
            ?? throw new InvalidOperationException(
                "The strict window unexpectedly succeeded.");

        return new ReferenceEvaluatorObservation(
            [.. result.Values],
            [.. ranked.Values],
            [.. named.Sequences.Select(
                sequence => sequence.Key.Value)],
            [.. named.Sequences.Select(
                sequence =>
                    (IReadOnlyList<int>)[.. sequence.Values])],
            failure.IsSuccess,
            namedFailure.Key.Value,
            namedFailure.Failure.StageNumber,
            namedFailure.Failure.RequiredPosition,
            namedFailure.Failure.AvailableCount);
    }
}
