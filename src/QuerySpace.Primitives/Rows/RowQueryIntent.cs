namespace QuerySpace.Rows;

public enum RowQueryOperator
{
    Equals,
    NotEquals,
    GreaterOrEqual,
    LessOrEqual
}

public enum RowQueryOrderDirection
{
    Ascending,
    Descending
}

public enum RowQueryOrderIntentKind
{
    Named,
    Keys
}

public sealed class RowQueryValueToken
{
    public RowQueryValueToken(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    public string Text { get; }
}

public sealed class RowQueryPredicateIntent
{
    public RowQueryPredicateIntent(
        string key,
        RowQueryOperator @operator,
        RowQueryValueToken value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        RowQueryContractGuard.ValidateDefined(
            @operator,
            nameof(@operator));
        Key = key;
        Operator = @operator;
        Value = value;
    }

    public string Key { get; }

    public RowQueryOperator Operator { get; }

    public RowQueryValueToken Value { get; }
}

public sealed class RowQueryOrderTermIntent
{
    public RowQueryOrderTermIntent(
        string key,
        RowQueryOrderDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        RowQueryContractGuard.ValidateDefined(
            direction,
            nameof(direction));
        Key = key;
        Direction = direction;
    }

    public string Key { get; }

    public RowQueryOrderDirection Direction { get; }
}

public sealed class RowQueryOrderIntent
{
    private readonly string? _namedOrderKey;
    private readonly RowQueryOrderDirection _namedOrderDirection;
    private readonly IReadOnlyList<RowQueryOrderTermIntent> _terms;

    private RowQueryOrderIntent(
        RowQueryOrderIntentKind kind,
        string? namedOrderKey,
        RowQueryOrderDirection namedOrderDirection,
        IReadOnlyList<RowQueryOrderTermIntent> terms)
    {
        Kind = kind;
        _namedOrderKey = namedOrderKey;
        _namedOrderDirection = namedOrderDirection;
        _terms = terms;
    }

    public RowQueryOrderIntentKind Kind { get; }

    public string NamedOrderKey =>
        Kind is RowQueryOrderIntentKind.Named
            ? _namedOrderKey!
            : throw WrongKind(nameof(NamedOrderKey));

    public RowQueryOrderDirection NamedOrderDirection =>
        Kind is RowQueryOrderIntentKind.Named
            ? _namedOrderDirection
            : throw WrongKind(nameof(NamedOrderDirection));

    public IReadOnlyList<RowQueryOrderTermIntent> Terms =>
        Kind is RowQueryOrderIntentKind.Keys
            ? _terms
            : throw WrongKind(nameof(Terms));

    public static RowQueryOrderIntent Named(
        string namedOrderKey,
        RowQueryOrderDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namedOrderKey);
        RowQueryContractGuard.ValidateDefined(
            direction,
            nameof(direction));
        return new(
            RowQueryOrderIntentKind.Named,
            namedOrderKey,
            direction,
            QuerySpaceSnapshot.Empty<RowQueryOrderTermIntent>());
    }

    public static RowQueryOrderIntent Keys(
        IReadOnlyList<RowQueryOrderTermIntent> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.Count == 0)
        {
            throw new ArgumentException(
                "A key order must contain at least one term.",
                nameof(terms));
        }

        var copy = new RowQueryOrderTermIntent[terms.Count];
        for (int index = 0; index < terms.Count; index++)
        {
            copy[index] = terms[index]
                ?? throw new ArgumentNullException(
                    nameof(terms),
                    $"Order term {index + 1} is null.");
        }

        return new(
            RowQueryOrderIntentKind.Keys,
            null,
            default,
            QuerySpaceSnapshot.Own(copy));
    }

    private InvalidOperationException WrongKind(string property) =>
        new(
            $"{property} is not valid for a {Kind} row-query order intent.");
}

public sealed class RowQueryIntent
{
    private RowQueryIntent(
        IReadOnlyList<RowQueryPredicateIntent> predicates,
        RowQueryOrderIntent? baselineOrder,
        RowSelectionIntent<RowQueryOrderIntent> selection)
    {
        Predicates = predicates;
        BaselineOrder = baselineOrder;
        Selection = selection;
    }

    public static RowQueryIntent Empty { get; } =
        new(
            QuerySpaceSnapshot.Empty<RowQueryPredicateIntent>(),
            null,
            RowSelectionIntent<RowQueryOrderIntent>.Empty);

    public IReadOnlyList<RowQueryPredicateIntent> Predicates { get; }

    public RowQueryOrderIntent? BaselineOrder { get; }

    public RowSelectionIntent<RowQueryOrderIntent> Selection { get; }

    public static RowQueryIntent Create(
        IReadOnlyList<RowQueryPredicateIntent> predicates,
        RowQueryOrderIntent? baselineOrder,
        RowSelectionIntent<RowQueryOrderIntent> selection)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        ArgumentNullException.ThrowIfNull(selection);

        var copy = new RowQueryPredicateIntent[predicates.Count];
        for (int index = 0; index < predicates.Count; index++)
        {
            copy[index] = predicates[index]
                ?? throw new ArgumentNullException(
                    nameof(predicates),
                    $"Predicate {index + 1} is null.");
        }

        return copy.Length == 0
            && baselineOrder is null
            && selection.Operations.Count == 0
                ? Empty
                : new(
                    QuerySpaceSnapshot.Own(copy),
                    baselineOrder,
                    selection);
    }
}

internal static class RowQueryContractGuard
{
    public static void ValidateDefined<TEnum>(
        TEnum value,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Unsupported {typeof(TEnum).Name} value.");
        }
    }
}
