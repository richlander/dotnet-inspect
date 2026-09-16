using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections;

public sealed class RowSelectionIntentOperation<TOrderOperand>
    where TOrderOperand : notnull
{
    private readonly int _count;
    private readonly int? _start;
    private readonly int? _end;
    private readonly bool _hasRankingOrderOperand;
    private readonly TOrderOperand _rankingOrderOperand;

    private RowSelectionIntentOperation(
        RowSelectionStageKind kind,
        int count,
        int? start,
        int? end,
        bool hasRankingOrderOperand,
        TOrderOperand rankingOrderOperand)
    {
        Kind = kind;
        _count = count;
        _start = start;
        _end = end;
        _hasRankingOrderOperand = hasRankingOrderOperand;
        _rankingOrderOperand = rankingOrderOperand;
    }

    public RowSelectionStageKind Kind { get; }

    public int Count =>
        Kind is RowSelectionStageKind.Head
            or RowSelectionStageKind.Tail
            or RowSelectionStageKind.Top
            ? _count
            : throw WrongKind(nameof(Count));

    public int? Start =>
        Kind is RowSelectionStageKind.Window
            ? _start
            : throw WrongKind(nameof(Start));

    public int? End =>
        Kind is RowSelectionStageKind.Window
            ? _end
            : throw WrongKind(nameof(End));

    public bool HasRankingOrderOperand =>
        Kind is RowSelectionStageKind.Top
            ? _hasRankingOrderOperand
            : throw WrongKind(nameof(HasRankingOrderOperand));

    public TOrderOperand RankingOrderOperand =>
        Kind is not RowSelectionStageKind.Top
            ? throw WrongKind(nameof(RankingOrderOperand))
            : _hasRankingOrderOperand
                ? _rankingOrderOperand
                : throw new InvalidOperationException(
                    "The Top operation has no explicit ranking-order operand.");

    public static RowSelectionIntentOperation<TOrderOperand> Head(
        int count) =>
        new(
            RowSelectionStageKind.Head,
            ValidateCount(count),
            null,
            null,
            false,
            default!);

    public static RowSelectionIntentOperation<TOrderOperand> Tail(
        int count) =>
        new(
            RowSelectionStageKind.Tail,
            ValidateCount(count),
            null,
            null,
            false,
            default!);

    public static RowSelectionIntentOperation<TOrderOperand> Window(
        int? start,
        int? end)
    {
        if (start is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start),
                start,
                "A present window start must be positive.");
        }

        if (end is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A present window end must be positive.");
        }

        if (start is not null
            && end is not null
            && end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A closed window end cannot precede its start.");
        }

        return new(
            RowSelectionStageKind.Window,
            0,
            start,
            end,
            false,
            default!);
    }

    public static RowSelectionIntentOperation<TOrderOperand> Top(
        int count) =>
        new(
            RowSelectionStageKind.Top,
            ValidateCount(count),
            null,
            null,
            false,
            default!);

    public static RowSelectionIntentOperation<TOrderOperand> Top(
        int count,
        TOrderOperand rankingOrderOperand)
    {
        ArgumentNullException.ThrowIfNull(rankingOrderOperand);
        return new(
            RowSelectionStageKind.Top,
            ValidateCount(count),
            null,
            null,
            true,
            rankingOrderOperand);
    }

    private static int ValidateCount(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "A row-selection count must be positive.");
        }

        return count;
    }

    private InvalidOperationException WrongKind(
        string property) =>
        new(
            $"{property} is not valid for a {Kind} row-selection intent operation.");
}

public sealed class RowSelectionIntent<TOrderOperand>
    where TOrderOperand : notnull
{
    private RowSelectionIntent(
        IReadOnlyList<RowSelectionIntentOperation<TOrderOperand>>
            operations)
    {
        Operations = operations;
    }

    public static RowSelectionIntent<TOrderOperand> Empty { get; } =
        new(
            SectionContractSnapshot.Empty<
                RowSelectionIntentOperation<TOrderOperand>>());

    public IReadOnlyList<RowSelectionIntentOperation<TOrderOperand>>
        Operations { get; }

    public static RowSelectionIntent<TOrderOperand> Create(
        IReadOnlyList<RowSelectionIntentOperation<TOrderOperand>>
            operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var copy =
            new RowSelectionIntentOperation<TOrderOperand>[
                operations.Count];
        for (int index = 0; index < operations.Count; index++)
        {
            copy[index] = operations[index]
                ?? throw new ArgumentNullException(
                    nameof(operations),
                    $"Operation {index + 1} is null.");
        }

        return copy.Length == 0
            ? Empty
            : new(SectionContractSnapshot.Own(copy));
    }

    public RowSelectionIntent<TOrderOperand> Append(
        RowSelectionIntentOperation<TOrderOperand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var copy =
            new RowSelectionIntentOperation<TOrderOperand>[
                Operations.Count + 1];
        for (int index = 0; index < Operations.Count; index++)
            copy[index] = Operations[index];
        copy[^1] = operation;
        return new(SectionContractSnapshot.Own(copy));
    }
}
