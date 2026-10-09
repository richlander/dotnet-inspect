
namespace QuerySpace.Rows;

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
            QuerySpaceSnapshot.Empty<
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
            : new(QuerySpaceSnapshot.Own(copy));
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
        return new(QuerySpaceSnapshot.Own(copy));
    }

    /// <summary>
    /// The finite row prefix of the producer's order that this plan can
    /// retain: a producer that stops after that many rows yields the same
    /// selected rows as one that produced the complete population. Null when
    /// the plan needs the complete population, as an unbounded Tail, an
    /// open Window, a Top, or an empty plan does.
    /// </summary>
    public int? RequiredPrefix()
    {
        long offset = 0;
        long? maximumLength = null;
        long required = 0;
        foreach (RowSelectionIntentOperation<TOrderOperand> operation
            in Operations)
        {
            switch (operation.Kind)
            {
                case RowSelectionStageKind.Head:
                    maximumLength = Math.Min(
                        maximumLength ?? operation.Count,
                        operation.Count);
                    break;
                case RowSelectionStageKind.Tail:
                    if (maximumLength is not long boundedTail)
                        return null;
                    required = Math.Max(
                        required,
                        offset + boundedTail);
                    long retainedTail = Math.Min(
                        boundedTail,
                        operation.Count);
                    offset += boundedTail - retainedTail;
                    maximumLength = retainedTail;
                    break;
                case RowSelectionStageKind.Window:
                    int start = operation.Start ?? 1;
                    if (operation.End is int end)
                    {
                        if (maximumLength is long maximum
                            && maximum < end)
                        {
                            return AsPrefix(
                                Math.Max(required, offset + maximum));
                        }
                        required = Math.Max(required, offset + end);
                        maximumLength = end - start + 1;
                        offset = 0;
                        break;
                    }
                    if (operation.Start is null)
                        break;
                    if (maximumLength is long bounded
                        && bounded < start)
                    {
                        return AsPrefix(
                            Math.Max(required, offset + bounded));
                    }
                    offset += start - 1;
                    if (maximumLength is long length)
                        maximumLength = length - start + 1;
                    required = Math.Max(required, offset + 1);
                    break;
                case RowSelectionStageKind.Top:
                    return null;
                default:
                    throw new InvalidOperationException(
                        "Unknown row-selection stage kind.");
            }
        }

        return maximumLength is long maximumPrefix
            ? AsPrefix(Math.Max(required, offset + maximumPrefix))
            : null;
    }

    private static int? AsPrefix(long value) =>
        value <= int.MaxValue
            ? (int)value
            : null;
}
