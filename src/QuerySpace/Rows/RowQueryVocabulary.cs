using System.Diagnostics.CodeAnalysis;

namespace QuerySpace.Rows;

public enum RowQueryOrderPurpose
{
    Sequence,
    Ranking
}

public readonly struct RowQueryValue<T>
    where T : notnull
{
    private readonly T? _value;

    private RowQueryValue(bool hasValue, T? value)
    {
        HasValue = hasValue;
        _value = value;
    }

    public bool HasValue { get; }

    public T Value =>
        HasValue
            ? _value!
            : throw new InvalidOperationException(
                "A missing row-query value has no typed value.");

    public static RowQueryValue<T> Missing => default;

    public static RowQueryValue<T> Present(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(true, value);
    }
}

public static class RowQueryValueOrder
{
    public static IComparer<RowQueryValue<T>> Create<T>(
        IComparer<T> comparer,
        RowQueryOrderDirection direction,
        bool missingLast)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(comparer);
        ValidateEnum(direction, nameof(direction));
        return Comparer<RowQueryValue<T>>.Create(
            (left, right) =>
            {
                if (!left.HasValue)
                    return !right.HasValue ? 0 : missingLast ? 1 : -1;
                if (!right.HasValue)
                    return missingLast ? -1 : 1;

                return direction
                    is RowQueryOrderDirection.Ascending
                        ? comparer.Compare(left.Value, right.Value)
                        : comparer.Compare(right.Value, left.Value);
            });
    }

    private static void ValidateEnum<TEnum>(
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

public sealed class RowQueryKey<TRow>
{
    private readonly Func<
        RowQueryOperator,
        RowQueryValueToken,
        Predicate<TRow>?> _predicateBinder;
    private readonly Func<
        RowQueryOrderDirection,
        IComparer<TRow>>? _orderComparerFactory;

    private RowQueryKey(
        RowQueryKeyIdentity identity,
        string key,
        IReadOnlyList<RowQueryOperator> operators,
        Func<
            RowQueryOperator,
            RowQueryValueToken,
            Predicate<TRow>?> predicateBinder,
        Func<
            RowQueryOrderDirection,
            IComparer<TRow>>? orderComparerFactory)
    {
        Identity = identity;
        Key = key;
        Operators = operators;
        _predicateBinder = predicateBinder;
        _orderComparerFactory = orderComparerFactory;
    }

    public RowQueryKeyIdentity Identity { get; }

    public string Key { get; }

    public IReadOnlyList<RowQueryOperator> Operators { get; }

    public bool SupportsOrdering => _orderComparerFactory is not null;

    public static RowQueryKey<TRow> Create<TValue>(
        RowQueryKeyIdentity identity,
        string key,
        IReadOnlyList<RowQueryOperator> operators,
        Func<TRow, RowQueryValue<TValue>> accessor,
        Func<
            RowQueryOperator,
            RowQueryValueToken,
            Predicate<TValue>?> predicateBinder,
        Func<
            RowQueryOrderDirection,
            IComparer<RowQueryValue<TValue>>>? orderComparerFactory = null)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(predicateBinder);

        var operatorSet = new HashSet<RowQueryOperator>();
        var operatorCopy = new RowQueryOperator[operators.Count];
        for (int index = 0; index < operators.Count; index++)
        {
            RowQueryOperator @operator = operators[index];
            if (!Enum.IsDefined(@operator))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(operators),
                    @operator,
                    "A key declares an unsupported predicate operator.");
            }

            if (!operatorSet.Add(@operator))
            {
                throw new ArgumentException(
                    $"Predicate operator {@operator} is duplicated.",
                    nameof(operators));
            }

            operatorCopy[index] = @operator;
        }

        Predicate<TRow>? Bind(
            RowQueryOperator @operator,
            RowQueryValueToken token)
        {
            Predicate<TValue>? predicate =
                predicateBinder(@operator, token);
            if (predicate is null)
                return null;

            return row =>
            {
                RowQueryValue<TValue> value = accessor(row);
                return value.HasValue
                    && predicate(value.Value);
            };
        }

        IComparer<TRow> Compare(
            RowQueryOrderDirection direction)
        {
            IComparer<RowQueryValue<TValue>> comparer =
                orderComparerFactory!(direction)
                ?? throw new InvalidOperationException(
                    "A row-query key order factory returned no comparer.");
            return Comparer<TRow>.Create(
                (left, right) =>
                    comparer.Compare(
                        accessor(left),
                        accessor(right)));
        }

        return new(
            identity,
            key,
            QuerySpaceSnapshot.Own(operatorCopy),
            Bind,
            orderComparerFactory is null
                ? null
                : Compare);
    }

    internal Predicate<TRow>? BindPredicate(
        RowQueryOperator @operator,
        RowQueryValueToken value) =>
        _predicateBinder(@operator, value);

    internal Func<IComparer<TRow>>? CreateComparerFactory(
        RowQueryOrderDirection direction) =>
        _orderComparerFactory is null
            ? null
            : () => _orderComparerFactory(direction);
}

public sealed class RowQueryNamedOrder<TRow>
{
    private readonly Func<
        RowQueryOrderDirection,
        IComparer<TRow>> _comparerFactory;

    public RowQueryNamedOrder(
        RowQueryNamedOrderIdentity identity,
        string key,
        RowQueryOrderPurpose purpose,
        Func<
            RowQueryOrderDirection,
            IComparer<TRow>> comparerFactory)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(comparerFactory);
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "Unsupported row-query order purpose.");
        }

        Identity = identity;
        Key = key;
        Purpose = purpose;
        _comparerFactory = comparerFactory;
    }

    public RowQueryNamedOrderIdentity Identity { get; }

    public string Key { get; }

    public RowQueryOrderPurpose Purpose { get; }

    internal Func<IComparer<TRow>> CreateComparerFactory(
        RowQueryOrderDirection direction) =>
        () => _comparerFactory(direction)
            ?? throw new InvalidOperationException(
                "A named row-query order factory returned no comparer.");
}

public sealed class RowQueryNamedOrderDefault<TRow>
{
    public RowQueryNamedOrderDefault(
        RowQueryNamedOrder<TRow> order,
        RowQueryOrderDirection direction)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction),
                direction,
                "Unsupported row-query order direction.");
        }

        Order = order;
        Direction = direction;
    }

    public RowQueryNamedOrder<TRow> Order { get; }

    public RowQueryOrderDirection Direction { get; }
}

public sealed class RowQueryVocabulary<TRow>
{
    private readonly IReadOnlyDictionary<
        string,
        RowQueryKey<TRow>> _keysByKey;
    private readonly IReadOnlyDictionary<
        string,
        RowQueryNamedOrder<TRow>> _ordersByKey;

    private RowQueryVocabulary(
        RowQueryVocabularyIdentity identity,
        IReadOnlyList<RowQueryKey<TRow>> keys,
        IReadOnlyList<RowQueryNamedOrder<TRow>> namedOrders,
        RowQueryNamedOrderDefault<TRow>? defaultBaselineOrder,
        RowQueryNamedOrderDefault<TRow>? defaultTopRanking,
        IReadOnlyDictionary<
            string,
            RowQueryKey<TRow>> keysByKey,
        IReadOnlyDictionary<
            string,
            RowQueryNamedOrder<TRow>> ordersByKey)
    {
        Identity = identity;
        Keys = keys;
        NamedOrders = namedOrders;
        DefaultBaselineOrder = defaultBaselineOrder;
        DefaultTopRanking = defaultTopRanking;
        _keysByKey = keysByKey;
        _ordersByKey = ordersByKey;
    }

    public RowQueryVocabularyIdentity Identity { get; }

    public IReadOnlyList<RowQueryKey<TRow>> Keys { get; }

    public IReadOnlyList<RowQueryNamedOrder<TRow>> NamedOrders { get; }

    public RowQueryNamedOrderDefault<TRow>? DefaultBaselineOrder { get; }

    public RowQueryNamedOrderDefault<TRow>? DefaultTopRanking { get; }

    public static RowQueryVocabulary<TRow> Create(
        RowQueryVocabularyIdentity identity,
        IReadOnlyList<RowQueryKey<TRow>> keys,
        IReadOnlyList<RowQueryNamedOrder<TRow>> namedOrders,
        RowQueryNamedOrderDefault<TRow>? defaultBaselineOrder = null,
        RowQueryNamedOrderDefault<TRow>? defaultTopRanking = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(namedOrders);

        var keyCopy = new RowQueryKey<TRow>[keys.Count];
        var keysByKey =
            new Dictionary<string, RowQueryKey<TRow>>(
                StringComparer.Ordinal);
        var keyIdentities =
            new HashSet<RowQueryKeyIdentity>();
        for (int index = 0; index < keys.Count; index++)
        {
            RowQueryKey<TRow> key =
                keys[index]
                ?? throw new ArgumentNullException(
                    nameof(keys),
                    $"Key {index + 1} is null.");
            if (!keysByKey.TryAdd(key.Key, key))
            {
                throw new ArgumentException(
                    $"Query key {key.Key} is duplicated.",
                    nameof(keys));
            }

            if (!keyIdentities.Add(key.Identity))
            {
                throw new ArgumentException(
                    "A key identity is duplicated.",
                    nameof(keys));
            }

            keyCopy[index] = key;
        }

        var orderCopy =
            new RowQueryNamedOrder<TRow>[namedOrders.Count];
        var ordersByKey =
            new Dictionary<string, RowQueryNamedOrder<TRow>>(
                StringComparer.Ordinal);
        var orderIdentities =
            new HashSet<RowQueryNamedOrderIdentity>();
        for (int index = 0; index < namedOrders.Count; index++)
        {
            RowQueryNamedOrder<TRow> order =
                namedOrders[index]
                ?? throw new ArgumentNullException(
                    nameof(namedOrders),
                    $"Named order {index + 1} is null.");
            if (!ordersByKey.TryAdd(order.Key, order))
            {
                throw new ArgumentException(
                    $"Named order key {order.Key} is duplicated.",
                    nameof(namedOrders));
            }

            if (!orderIdentities.Add(order.Identity))
            {
                throw new ArgumentException(
                    "A named-order identity is duplicated.",
                    nameof(namedOrders));
            }

            orderCopy[index] = order;
        }

        ValidateDefault(
            defaultBaselineOrder,
            ordersByKey,
            requireRanking: false,
            nameof(defaultBaselineOrder));
        ValidateDefault(
            defaultTopRanking,
            ordersByKey,
            requireRanking: true,
            nameof(defaultTopRanking));

        return new(
            identity,
            QuerySpaceSnapshot.Own(keyCopy),
            QuerySpaceSnapshot.Own(orderCopy),
            defaultBaselineOrder,
            defaultTopRanking,
            keysByKey,
            ordersByKey);
    }

    internal bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out RowQueryKey<TRow>? queryKey) =>
        _keysByKey.TryGetValue(key, out queryKey);

    internal bool TryGetNamedOrder(
        string key,
        [NotNullWhen(true)]
        out RowQueryNamedOrder<TRow>? order) =>
        _ordersByKey.TryGetValue(key, out order);

    private static void ValidateDefault(
        RowQueryNamedOrderDefault<TRow>? defaultOrder,
        IReadOnlyDictionary<
            string,
            RowQueryNamedOrder<TRow>> ordersByKey,
        bool requireRanking,
        string parameterName)
    {
        if (defaultOrder is null)
            return;

        if (!ordersByKey.TryGetValue(
                defaultOrder.Order.Key,
                out RowQueryNamedOrder<TRow>? declared)
            || !ReferenceEquals(declared, defaultOrder.Order))
        {
            throw new ArgumentException(
                "A default order must be declared by the vocabulary.",
                parameterName);
        }

        if (requireRanking
            && defaultOrder.Order.Purpose
                is not RowQueryOrderPurpose.Ranking)
        {
            throw new ArgumentException(
                "The default Top order must be a ranking.",
                parameterName);
        }
    }
}
