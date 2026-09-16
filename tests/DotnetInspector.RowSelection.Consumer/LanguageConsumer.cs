using DotnetInspector.RowSelection;

namespace DotnetInspector.RowSelectionConsumer;

public sealed record LanguageObservation(
    IReadOnlyList<RowSelectionStageKind> Kinds,
    int HeadCount,
    int TailCount,
    int? WindowStart,
    int? WindowEnd,
    int TopCount,
    string TopOrder,
    int EmptyCount,
    int PriorCount,
    int AppendedCount,
    Type WrongCountAccessorException,
    Type WrongWindowAccessorException,
    Type WrongOrderAccessorException);

public static class LanguageConsumer
{
    public static LanguageObservation Inspect()
    {
        RowSelectionStage<string> head =
            RowSelectionStage<string>.Head(2);
        RowSelectionStage<string> tail =
            RowSelectionStage<string>.Tail(3);
        RowSelectionStage<string> window =
            RowSelectionStage<string>.Window(4, 8);
        RowSelectionStage<string> top =
            RowSelectionStage<string>.Top(
                5,
                "rank");
        RowSelectionPlan<string> plan =
            RowSelectionPlan<string>.Create(
                [head, tail, window, top]);
        RowSelectionPlan<string> appended =
            plan.Append(
                RowSelectionStage<string>.Window(
                    null,
                    null));

        return new LanguageObservation(
            [.. plan.Stages.Select(stage => stage.Kind)],
            head.Count,
            tail.Count,
            window.Start,
            window.End,
            top.Count,
            top.Order,
            RowSelectionPlan<string>.Empty.Stages.Count,
            plan.Stages.Count,
            appended.Stages.Count,
            Capture(() => _ = window.Count),
            Capture(() => _ = head.Start),
            Capture(() => _ = tail.Order));
    }

    private static Type Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception.GetType();
        }

        throw new InvalidOperationException(
            "The invalid accessor unexpectedly succeeded.");
    }
}
