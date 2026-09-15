namespace DotnetInspector.RowSelection;

public enum RowSelectionStageKind
{
    Head,
    Tail,
    Window,
    Top
}

public sealed class RowSelectionStage<TOrder>
    where TOrder : notnull
{
    private readonly int _count;
    private readonly int? _start;
    private readonly int? _end;
    private readonly TOrder _order;

    private RowSelectionStage(
        RowSelectionStageKind kind,
        int count,
        int? start,
        int? end,
        TOrder order)
    {
        Kind = kind;
        _count = count;
        _start = start;
        _end = end;
        _order = order;
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

    public TOrder Order =>
        Kind is RowSelectionStageKind.Top
            ? _order
            : throw WrongKind(nameof(Order));

    public static RowSelectionStage<TOrder> Head(int count) =>
        new(
            RowSelectionStageKind.Head,
            ValidateCount(count),
            null,
            null,
            default!);

    public static RowSelectionStage<TOrder> Tail(int count) =>
        new(
            RowSelectionStageKind.Tail,
            ValidateCount(count),
            null,
            null,
            default!);

    public static RowSelectionStage<TOrder> Window(
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
            default!);
    }

    public static RowSelectionStage<TOrder> Top(
        int count,
        TOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(
            RowSelectionStageKind.Top,
            ValidateCount(count),
            null,
            null,
            order);
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

    private InvalidOperationException WrongKind(string property) =>
        new(
            $"{property} is not valid for a {Kind} row-selection stage.");
}
