using DotnetInspect.Cli.Output;
using QuerySpace.Rows;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.CommandLine;

internal static class CliSemanticRowSelection
{
    public static bool TrySelectOrApplyLegacy<T>(
        RowSelectionIntent<string>? intent,
        RowWindow? legacyWindow,
        IReadOnlyList<T> rows,
        string sequenceName,
        Func<RowsCohortSemanticFailure<string>, string> formatFailure,
        out IReadOnlyList<T> selected)
    {
        if (intent is not null)
        {
            return TrySelect(
                intent,
                rows,
                sequenceName,
                formatFailure,
                out selected);
        }

        selected = RowWindow.Apply(
            legacyWindow,
            rows);
        return true;
    }

    public static bool TrySelect<T>(
        RowSelectionIntent<string>? intent,
        IReadOnlyList<T> rows,
        string sequenceName,
        Func<RowsCohortSemanticFailure<string>, string> formatFailure,
        out IReadOnlyList<T> selected)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentNullException.ThrowIfNull(formatFailure);

        if (intent is not { Operations.Count: > 0 })
        {
            selected = rows;
            return true;
        }

        RowsCohortResult<string, T> result =
            RowsCohortExecutor.ApplyUnordered(
                [
                    RowsCohortSequence<string, T>.Create(
                        sequenceName,
                        rows),
                ],
                intent);
        if (result.IsSuccess)
        {
            selected = result.RowSets[0].Values;
            return true;
        }

        CommandError.Write(formatFailure(result.Failure!));
        selected = Array.Empty<T>();
        return false;
    }

    public static bool TrySelectRanked<T>(
        RowSelectionIntent<string>? intent,
        IReadOnlyList<T> rows,
        IComparer<T> ranking,
        string sequenceName,
        Func<RowsCohortSemanticFailure<string>, string> formatFailure,
        out IReadOnlyList<T> selected)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(ranking);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
        ArgumentNullException.ThrowIfNull(formatFailure);

        if (intent is not { Operations.Count: > 0 })
        {
            selected = rows;
            return true;
        }

        var stages =
            new RowSelectionStage<string>[intent.Operations.Count];
        for (int index = 0; index < intent.Operations.Count; index++)
        {
            RowSelectionIntentOperation<string> operation =
                intent.Operations[index];
            stages[index] =
                operation.Kind switch
                {
                    RowSelectionStageKind.Head =>
                        RowSelectionStage<string>.Head(operation.Count),
                    RowSelectionStageKind.Tail =>
                        RowSelectionStage<string>.Tail(operation.Count),
                    RowSelectionStageKind.Window =>
                        RowSelectionStage<string>.Window(
                            operation.Start,
                            operation.End),
                    RowSelectionStageKind.Top
                        when !operation.HasRankingOrderOperand =>
                        RowSelectionStage<string>.Top(
                            operation.Count,
                            sequenceName),
                    RowSelectionStageKind.Top =>
                        throw new InvalidOperationException(
                            "An intrinsically ranked sequence cannot apply "
                                + "an explicit ranking operand."),
                    _ => throw new InvalidOperationException(
                        $"Unsupported semantic row-selection operation "
                            + $"'{operation.Kind}'."),
                };
        }

        RowsCohortResult<string, T> result =
            RowsCohortExecutor.Apply(
                [
                    RowsCohortSequence<string, T>.Create(
                        sequenceName,
                        rows),
                ],
                RowSelectionPlan<string>.Create(stages),
                _ => ranking);
        if (result.IsSuccess)
        {
            selected = result.RowSets[0].Values;
            return true;
        }

        CommandError.Write(formatFailure(result.Failure!));
        selected = Array.Empty<T>();
        return false;
    }

    public static bool TrySelectPreservingContext<T>(
        RowSelectionIntent<string>? intent,
        IReadOnlyList<T> events,
        Func<T, bool> isRow,
        string sequenceName,
        Func<RowsCohortSemanticFailure<string>, string> formatFailure,
        out IReadOnlyList<T> selectedEvents,
        out int availableRowCount)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(isRow);

        T[] rows = [.. events.Where(isRow)];
        availableRowCount = rows.Length;
        if (!TrySelect(
                intent,
                rows,
                sequenceName,
                formatFailure,
                out IReadOnlyList<T> selectedRows))
        {
            selectedEvents =
            [
                .. events.Where(item => !isRow(item)),
            ];
            return false;
        }

        var selectedSet = new HashSet<T>(
            selectedRows,
            ReferenceEqualityComparer.Instance);
        selectedEvents =
        [
            .. events.Where(
                item => !isRow(item) || selectedSet.Contains(item)),
        ];
        return true;
    }

    public static bool ProvidesExactCount(
        RowSelectionIntent<string>? intent,
        int observedCount,
        bool sourceComplete)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedCount);

        bool exact = sourceComplete;
        int currentCount = observedCount;
        if (intent is not { Operations.Count: > 0 })
            return exact;

        foreach (RowSelectionIntentOperation<string> operation
            in intent.Operations)
        {
            switch (operation.Kind)
            {
                case RowSelectionStageKind.Head:
                case RowSelectionStageKind.Tail:
                    if (currentCount >= operation.Count)
                        exact = true;
                    currentCount = Math.Min(
                        currentCount,
                        operation.Count);
                    break;
                case RowSelectionStageKind.Window:
                    if (operation.End is int end)
                    {
                        exact = true;
                        currentCount =
                            end - (operation.Start ?? 1) + 1;
                    }
                    else
                    {
                        currentCount -=
                            operation.Start!.Value - 1;
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported semantic row-selection operation "
                        + $"'{operation.Kind}'.");
            }
        }

        return exact;
    }
}
