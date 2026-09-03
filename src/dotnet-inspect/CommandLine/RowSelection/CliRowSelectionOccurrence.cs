namespace DotnetInspector.CommandLine;

internal enum CliRowSelectionOccurrenceKind
{
    Limit,
    Rows,
    Top,
    OrderBy,
    Head,
    Tail,
    Lines,
    TailLines
}

internal sealed class CliRowSelectionOccurrence<TOrderOperand>
    where TOrderOperand : notnull
{
    private readonly string? _value;
    private readonly TOrderOperand _orderOperand;

    private CliRowSelectionOccurrence(
        CliRowSelectionOccurrenceKind kind,
        int position,
        string? value,
        TOrderOperand orderOperand)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "An argv position cannot be negative.");
        }

        Kind = kind;
        Position = position;
        _value = value;
        _orderOperand = orderOperand;
    }

    public CliRowSelectionOccurrenceKind Kind { get; }

    public int Position { get; }

    public string Value =>
        Kind is CliRowSelectionOccurrenceKind.Limit
            or CliRowSelectionOccurrenceKind.Rows
            or CliRowSelectionOccurrenceKind.Top
            ? _value!
            : throw WrongKind(nameof(Value));

    public TOrderOperand OrderOperand =>
        Kind is CliRowSelectionOccurrenceKind.OrderBy
            ? _orderOperand
            : throw WrongKind(nameof(OrderOperand));

    public static CliRowSelectionOccurrence<TOrderOperand> Limit(
        int position,
        string value) =>
        WithValue(
            CliRowSelectionOccurrenceKind.Limit,
            position,
            value);

    public static CliRowSelectionOccurrence<TOrderOperand> Rows(
        int position,
        string value) =>
        WithValue(
            CliRowSelectionOccurrenceKind.Rows,
            position,
            value);

    public static CliRowSelectionOccurrence<TOrderOperand> Top(
        int position,
        string value) =>
        WithValue(
            CliRowSelectionOccurrenceKind.Top,
            position,
            value);

    public static CliRowSelectionOccurrence<TOrderOperand> OrderBy(
        int position,
        TOrderOperand orderOperand)
    {
        ArgumentNullException.ThrowIfNull(orderOperand);
        return new(
            CliRowSelectionOccurrenceKind.OrderBy,
            position,
            null,
            orderOperand);
    }

    public static CliRowSelectionOccurrence<TOrderOperand> Head(
        int position) =>
        Modifier(
            CliRowSelectionOccurrenceKind.Head,
            position);

    public static CliRowSelectionOccurrence<TOrderOperand> Tail(
        int position) =>
        Modifier(
            CliRowSelectionOccurrenceKind.Tail,
            position);

    public static CliRowSelectionOccurrence<TOrderOperand> Lines(
        int position) =>
        Modifier(
            CliRowSelectionOccurrenceKind.Lines,
            position);

    public static CliRowSelectionOccurrence<TOrderOperand> TailLines(
        int position) =>
        Modifier(
            CliRowSelectionOccurrenceKind.TailLines,
            position);

    private static CliRowSelectionOccurrence<TOrderOperand> WithValue(
        CliRowSelectionOccurrenceKind kind,
        int position,
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(
            kind,
            position,
            value,
            default!);
    }

    private static CliRowSelectionOccurrence<TOrderOperand> Modifier(
        CliRowSelectionOccurrenceKind kind,
        int position) =>
        new(
            kind,
            position,
            null,
            default!);

    private InvalidOperationException WrongKind(
        string property) =>
        new(
            $"{property} is not valid for a {Kind} CLI row-selection occurrence.");
}
