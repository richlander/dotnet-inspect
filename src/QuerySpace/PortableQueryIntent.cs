using QuerySpace.Rows;

namespace QuerySpace;

/// <summary>
/// One <c>(key, operator, value)</c> triple.
/// </summary>
/// <remarks>
/// The value is bounded text preserved exactly as supplied. This layer never
/// parses, normalizes, case-folds, or interprets it; interpretation belongs to the
/// vocabulary's binder at resolution.
/// </remarks>
public sealed class PortableQueryTerm : IEquatable<PortableQueryTerm>
{
    public PortableQueryTerm(
        string key,
        PortableQueryOperator @operator,
        string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(@operator))
            throw PortableQueryModel.Undefined(@operator, nameof(@operator));

        Key = key;
        Operator = @operator;
        Value = value;
    }

    public string Key { get; }

    public PortableQueryOperator Operator { get; }

    public string Value { get; }

    /// <summary>
    /// Exact triple equality, ordinal in both texts. Term membership is set-valued,
    /// so an identical triple present twice is one term.
    /// </summary>
    public bool Equals(PortableQueryTerm? other) =>
        other is not null
        && Operator == other.Operator
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PortableQueryTerm);

    public override int GetHashCode() => HashCode.Combine(Key, Operator, Value);
}

/// <summary>
/// One declared execution bound: an owner-named dimension of upstream work and the
/// maximum requested for it.
/// </summary>
/// <remarks>
/// Reaching a bound produces a completion state and proves nothing about
/// exhaustion. A dimension carries at most one bound: two maxima for one dimension
/// are a contradiction, not a narrower request.
/// </remarks>
public sealed class PortableQueryBound
{
    public PortableQueryBound(string dimension, int requestedMaximum)
    {
        ArgumentException.ThrowIfNullOrEmpty(dimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedMaximum);

        Dimension = dimension;
        RequestedMaximum = requestedMaximum;
    }

    public string Dimension { get; }

    public int RequestedMaximum { get; }
}

/// <summary>
/// One stage of the ordered selection pipeline.
/// </summary>
/// <remarks>
/// Positive counts and ordered inclusive window bounds are the stage owner's
/// construction preconditions, enforced here and by the codec at decode; violating
/// them is misuse rather than a resolution failure.
/// </remarks>
public sealed class PortableQueryStage
{
    private readonly int _count;
    private readonly int? _start;
    private readonly int? _end;

    private PortableQueryStage(
        RowSelectionStageKind kind,
        int count,
        int? start,
        int? end)
    {
        Kind = kind;
        _count = count;
        _start = start;
        _end = end;
    }

    public RowSelectionStageKind Kind { get; }

    public int Count =>
        Kind is RowSelectionStageKind.Window
            ? throw WrongKind(nameof(Count))
            : _count;

    public int? Start =>
        Kind is RowSelectionStageKind.Window
            ? _start
            : throw WrongKind(nameof(Start));

    public int? End =>
        Kind is RowSelectionStageKind.Window
            ? _end
            : throw WrongKind(nameof(End));

    public static PortableQueryStage Head(int count) =>
        new(RowSelectionStageKind.Head, ValidateCount(count), null, null);

    public static PortableQueryStage Tail(int count) =>
        new(RowSelectionStageKind.Tail, ValidateCount(count), null, null);

    public static PortableQueryStage Top(int count) =>
        new(RowSelectionStageKind.Top, ValidateCount(count), null, null);

    public static PortableQueryStage Window(int? start, int? end)
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

        if (start is not null && end is not null && end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "A closed window end cannot precede its start.");
        }

        return new(RowSelectionStageKind.Window, 0, start, end);
    }

    private static int ValidateCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return count;
    }

    private InvalidOperationException WrongKind(string property) =>
        new($"{property} is not valid for a {Kind} portable query stage.");
}

/// <summary>
/// Names which operation an order belongs to: the single baseline, or one exact
/// ranking stage by its index in the stage pipeline.
/// </summary>
/// <remarks>
/// <c>default</c> is the baseline, which is the role an intent is most likely to
/// carry alone.
/// </remarks>
public readonly struct PortableQueryOrderRole : IEquatable<PortableQueryOrderRole>
{
    // 0 is the baseline; stage n is stored as n + 1 so that default is the baseline.
    private readonly int _value;

    private PortableQueryOrderRole(int value) => _value = value;

    public static PortableQueryOrderRole Baseline => default;

    public static PortableQueryOrderRole ForStage(int stageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stageIndex);
        return new(stageIndex + 1);
    }

    public bool IsBaseline => _value == 0;

    public int StageIndex =>
        IsBaseline
            ? throw new InvalidOperationException(
                "The baseline role names no stage.")
            : _value - 1;

    public bool Equals(PortableQueryOrderRole other) => _value == other._value;

    public override bool Equals(object? obj) =>
        obj is PortableQueryOrderRole other && Equals(other);

    public override int GetHashCode() => _value;

    public static bool operator ==(
        PortableQueryOrderRole left,
        PortableQueryOrderRole right) => left.Equals(right);

    public static bool operator !=(
        PortableQueryOrderRole left,
        PortableQueryOrderRole right) => !left.Equals(right);
}

/// <summary>
/// One key-and-direction term inside a field-list order operation.
/// </summary>
public sealed class PortableQueryOrderTerm
{
    public PortableQueryOrderTerm(string key, PortableQueryDirection direction)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!Enum.IsDefined(direction))
            throw PortableQueryModel.Undefined(direction, nameof(direction));

        Key = key;
        Direction = direction;
    }

    public string Key { get; }

    public PortableQueryDirection Direction { get; }
}

/// <summary>
/// One unresolved order operation: either a named order plus a direction, or an
/// ordered list of key-and-direction terms composing lexicographically.
/// </summary>
/// <remarks>
/// Every operation carries its own role, so an operation's internal boundary is
/// never lost by flattening it against a neighbour, and the set has no meaningful
/// outer sequence.
/// </remarks>
public sealed class PortableQueryOrderOperation
{
    private readonly string? _reference;
    private readonly PortableQueryDirection _direction;
    private readonly IReadOnlyList<PortableQueryOrderTerm> _terms;

    private PortableQueryOrderOperation(
        PortableQueryOrderRole role,
        PortableQueryOrderKind kind,
        string? reference,
        PortableQueryDirection direction,
        IReadOnlyList<PortableQueryOrderTerm> terms)
    {
        Role = role;
        Kind = kind;
        _reference = reference;
        _direction = direction;
        _terms = terms;
    }

    public PortableQueryOrderRole Role { get; }

    public PortableQueryOrderKind Kind { get; }

    public string Reference =>
        Kind is PortableQueryOrderKind.Named
            ? _reference!
            : throw WrongKind(nameof(Reference));

    public PortableQueryDirection Direction =>
        Kind is PortableQueryOrderKind.Named
            ? _direction
            : throw WrongKind(nameof(Direction));

    public IReadOnlyList<PortableQueryOrderTerm> Terms =>
        Kind is PortableQueryOrderKind.Fields
            ? _terms
            : throw WrongKind(nameof(Terms));

    public static PortableQueryOrderOperation Named(
        PortableQueryOrderRole role,
        string reference,
        PortableQueryDirection direction)
    {
        ArgumentException.ThrowIfNullOrEmpty(reference);
        if (!Enum.IsDefined(direction))
            throw PortableQueryModel.Undefined(direction, nameof(direction));

        return new(
            role,
            PortableQueryOrderKind.Named,
            reference,
            direction,
            []);
    }

    public static PortableQueryOrderOperation Fields(
        PortableQueryOrderRole role,
        IReadOnlyList<PortableQueryOrderTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.Count == 0)
        {
            throw new ArgumentException(
                "A field-list order operation carries at least one term.",
                nameof(terms));
        }

        return new(
            role,
            PortableQueryOrderKind.Fields,
            null,
            default,
            PortableQuerySnapshot.Copy(terms, nameof(terms)));
    }

    private InvalidOperationException WrongKind(string property) =>
        new($"{property} is not valid for a {Kind} portable query order operation.");
}

/// <summary>
/// The serializable representation of one query: the single layer that crosses a
/// persistence, host, or version boundary.
/// </summary>
/// <remarks>
/// <para>
/// The model's fifth part, the vocabulary these terms resolve against, is not a
/// property here. It travels beside the intent — see
/// <see cref="PortableQueryIdentity"/> — so it appears exactly once and cannot
/// disagree with itself.
/// </para>
/// <para>
/// This type holds a request, never an outcome, a resolved binding, or any
/// presentation text. Declared limits belong to the codec, not to construction: an
/// intent may be built with more parts than a payload admits, and
/// <see cref="PortableQueryPayloadCodec.Encode"/> refuses it.
/// </para>
/// <para>Owner: <c>docs/design/portable-query-intent.md</c>.</para>
/// </remarks>
public sealed class PortableQueryIntent
{
    private PortableQueryIntent(
        IReadOnlyList<PortableQueryTerm> terms,
        IReadOnlyList<PortableQueryBound> bounds,
        IReadOnlyList<PortableQueryStage> stages,
        IReadOnlyList<PortableQueryOrderOperation> order)
    {
        Terms = terms;
        Bounds = bounds;
        Stages = stages;
        Order = order;
    }

    /// <summary>The intent with no parts. Its canonical payload is <c>{}</c>.</summary>
    public static PortableQueryIntent Empty { get; } = new([], [], [], []);

    /// <summary>A set; an identical triple present twice is one term.</summary>
    public IReadOnlyList<PortableQueryTerm> Terms { get; }

    /// <summary>Unordered; declaration sequence carries nothing.</summary>
    public IReadOnlyList<PortableQueryBound> Bounds { get; }

    /// <summary>Ordered and position-significant: the sequence is the question.</summary>
    public IReadOnlyList<PortableQueryStage> Stages { get; }

    /// <summary>Role-keyed; the outer sequence carries nothing.</summary>
    public IReadOnlyList<PortableQueryOrderOperation> Order { get; }

    public static PortableQueryIntent Create(
        IReadOnlyList<PortableQueryTerm> terms,
        IReadOnlyList<PortableQueryBound> bounds,
        IReadOnlyList<PortableQueryStage> stages,
        IReadOnlyList<PortableQueryOrderOperation> order)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(order);

        if (terms.Count == 0
            && bounds.Count == 0
            && stages.Count == 0
            && order.Count == 0)
        {
            return Empty;
        }

        IReadOnlyList<PortableQueryBound> boundsCopy =
            PortableQuerySnapshot.Copy(bounds, nameof(bounds));
        IReadOnlyList<PortableQueryStage> stagesCopy =
            PortableQuerySnapshot.Copy(stages, nameof(stages));
        IReadOnlyList<PortableQueryOrderOperation> orderCopy =
            PortableQuerySnapshot.Copy(order, nameof(order));

        RequireOneBoundPerDimension(boundsCopy);
        RequireOneOperationPerRole(orderCopy, stagesCopy);

        return new(
            PortableQuerySnapshot.Copy(terms, nameof(terms)),
            boundsCopy,
            stagesCopy,
            orderCopy);
    }

    // A dimension bounded twice and a role claimed twice are contradictions
    // rather than narrower requests, and a ranking that names no ranking stage
    // is not a request at all. They are the model's invariants, so an intent
    // cannot hold one even in a host that never serializes it. The codec's
    // declared maxima are a separate matter and stay with the codec.
    private static void RequireOneBoundPerDimension(
        IReadOnlyList<PortableQueryBound> bounds)
    {
        if (bounds.Count < 2) return;

        var dimensions = new HashSet<string>(StringComparer.Ordinal);
        foreach (PortableQueryBound bound in bounds)
        {
            if (!dimensions.Add(bound.Dimension))
            {
                throw new ArgumentException(
                    $"Dimension '{bound.Dimension}' carries more than one execution bound.",
                    nameof(bounds));
            }
        }
    }

    private static void RequireOneOperationPerRole(
        IReadOnlyList<PortableQueryOrderOperation> order,
        IReadOnlyList<PortableQueryStage> stages)
    {
        if (order.Count == 0) return;

        bool sawBaseline = false;
        var stageRoles = new HashSet<int>();
        foreach (PortableQueryOrderOperation operation in order)
        {
            if (operation.Role.IsBaseline)
            {
                if (sawBaseline)
                {
                    throw new ArgumentException(
                        "At most one order operation carries the baseline role.",
                        nameof(order));
                }

                sawBaseline = true;
                continue;
            }

            int index = operation.Role.StageIndex;
            if (index >= stages.Count
                || stages[index].Kind is not RowSelectionStageKind.Top)
            {
                throw new ArgumentException(
                    $"Role {index} names no ranking stage.",
                    nameof(order));
            }

            if (!stageRoles.Add(index))
            {
                throw new ArgumentException(
                    $"Stage {index} carries more than one ranking operation.",
                    nameof(order));
            }
        }
    }
}

internal static class PortableQuerySnapshot
{
    public static IReadOnlyList<T> Copy<T>(
        IReadOnlyList<T> source,
        string parameterName)
        where T : class
    {
        if (source.Count == 0) return [];

        var copy = new T[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            copy[index] = source[index]
                ?? throw new ArgumentNullException(
                    parameterName,
                    $"Element {index + 1} is null.");
        }

        return copy;
    }
}
