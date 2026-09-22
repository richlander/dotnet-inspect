namespace QuerySpace.Rows;

public readonly struct RowSelectionCountResult
{
    private readonly bool _hasResult;

    private RowSelectionCountResult(
        int count,
        RowWindowFailure? failure)
    {
        _hasResult = true;
        Count = count;
        Failure = failure;
    }

    public bool IsSuccess => _hasResult && Failure is null;

    public int Count { get; }

    public RowWindowFailure? Failure { get; }

    internal static RowSelectionCountResult Success(int count) =>
        new(count, null);

    internal static RowSelectionCountResult Failed(
        RowWindowFailure failure) =>
        new(0, failure);
}

public static class RowSelectionCountExecutor
{
    internal static bool CanApply<TOrder>(
        RowSelectionPlan<TOrder> plan)
        where TOrder : notnull
    {
        ArgumentNullException.ThrowIfNull(plan);

        for (int stageIndex = 0;
             stageIndex < plan.Stages.Count;
             stageIndex++)
        {
            if (plan.Stages[stageIndex].Kind
                is RowSelectionStageKind.Top)
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryApply<TOrder>(
        int sourceCount,
        RowSelectionPlan<TOrder> plan,
        out RowSelectionCountResult result)
        where TOrder : notnull
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceCount);
        ArgumentNullException.ThrowIfNull(plan);

        if (!CanApply(plan))
        {
            result = default;
            return false;
        }

        int count = sourceCount;
        for (int stageIndex = 0;
             stageIndex < plan.Stages.Count;
             stageIndex++)
        {
            RowSelectionStage<TOrder> stage =
                plan.Stages[stageIndex];
            switch (stage.Kind)
            {
                case RowSelectionStageKind.Head:
                case RowSelectionStageKind.Tail:
                    count = Math.Min(count, stage.Count);
                    break;
                case RowSelectionStageKind.Top:
                    throw new InvalidOperationException(
                        "A preflighted Count plan cannot contain Top.");
                case RowSelectionStageKind.Window:
                {
                    if (stage.Start is null
                        && stage.End is null)
                    {
                        break;
                    }

                    int requiredPosition =
                        stage.End ?? stage.Start!.Value;
                    if (requiredPosition > count)
                    {
                        result = RowSelectionCountResult.Failed(
                            new RowWindowFailure(
                                stageIndex + 1,
                                requiredPosition,
                                count));
                        return true;
                    }

                    int firstIndex = (stage.Start ?? 1) - 1;
                    int endExclusive = stage.End ?? count;
                    count = endExclusive - firstIndex;
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported row-selection stage kind {stage.Kind}.");
            }
        }

        result = RowSelectionCountResult.Success(count);
        return true;
    }
}
