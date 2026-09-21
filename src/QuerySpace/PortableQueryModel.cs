using QuerySpace.Rows;

namespace QuerySpace;

/// <summary>
/// The eight comparison identities a term may carry.
/// </summary>
/// <remarks>
/// The identity is the canonical text, not this enum's member name. See
/// <see cref="PortableQueryModel"/> for the text each member spells.
/// </remarks>
public enum PortableQueryOperator
{
    Equal,
    NotEqual,
    StartsWith,
    NotStartsWith,
    Contains,
    NotContains,
    AtLeast,
    AtMost
}

/// <summary>
/// The two order directions.
/// </summary>
public enum PortableQueryDirection
{
    Ascending,
    Descending
}

/// <summary>
/// The two forms an order operation may take.
/// </summary>
public enum PortableQueryOrderKind
{
    /// <summary>One named-order identity plus a direction.</summary>
    Named,

    /// <summary>An ordered list of key-and-direction terms composing lexicographically.</summary>
    Fields
}

/// <summary>
/// The model's identity texts and its semantic-order comparator.
/// </summary>
/// <remarks>
/// <para>
/// Every fixed identity in an intent — the eight operators, the two directions, the
/// four stage kinds, the two order kinds, and the baseline role — <em>is</em> its
/// text. The text is what a vocabulary admits, what orders a term, and what any
/// encoding carries. It is deliberately not derived from an enum member name, a CLI
/// spelling, or a display label: renaming a member here must not change a share
/// link, so the mapping is written out rather than computed.
/// </para>
/// <para>
/// Owner: <c>docs/design/portable-query-intent.md</c>. A codec carries these texts
/// and defines none of them.
/// </para>
/// </remarks>
public static class PortableQueryModel
{
    /// <summary>The baseline order role's identity text.</summary>
    public const string BaselineRoleText = "base";

    /// <summary>
    /// Compares text by Unicode scalar value, which is also UTF-8 byte order.
    /// </summary>
    /// <remarks>
    /// Every sort in an intent uses this. It is not UTF-16 code-unit order — the
    /// default of <see cref="string.CompareOrdinal(string?, string?)"/> and of
    /// JavaScript — because the two disagree above the Basic Multilingual Plane,
    /// and an intent's order must not depend on which host computed it.
    /// </remarks>
    public static IComparer<string> ScalarOrder { get; } = new ScalarOrderComparer();

    /// <summary>
    /// Puts terms in the model's semantic order: key, then operator, then value,
    /// each compared as text by <see cref="ScalarOrder"/> — the key's identity
    /// text, the operator's identity text, and the exact value token, never a
    /// resolved or normalized form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This order is the model's, not an encoding's. An intent held and resolved
    /// in process never touches a codec, yet two hosts must still agree on which
    /// of several defects is reported first, so both the codec and the resolver
    /// read the order from here rather than each implementing it.
    /// </para>
    /// <para>
    /// Exact duplicates collapse, because term membership is set-valued: an
    /// identical triple present twice is one term, so resolution never receives
    /// an exact duplicate and only a binder collision can be a duplicate. What a
    /// caller supplied is still what the codec's declared maxima are charged
    /// against — limits are charged as parsed, before this.
    /// </para>
    /// </remarks>
    public static IEnumerable<PortableQueryTerm> InSemanticOrder(
        IEnumerable<PortableQueryTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return terms
            .Distinct()
            .OrderBy(term => term.Key, ScalarOrder)
            .ThenBy(term => TextOf(term.Operator), ScalarOrder)
            .ThenBy(term => term.Value, ScalarOrder);
    }

    /// <summary>
    /// Puts execution bounds in the model's semantic order: by dimension
    /// identity. Their declaration sequence carries nothing, because bounds in
    /// different dimensions are independent.
    /// </summary>
    public static IEnumerable<PortableQueryBound> InSemanticOrder(
        IEnumerable<PortableQueryBound> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        return bounds.OrderBy(bound => bound.Dimension, ScalarOrder);
    }

    /// <summary>
    /// Puts order operations in the model's semantic order: the baseline first,
    /// then ranking operations by ascending stage index.
    /// </summary>
    /// <remarks>
    /// The set has no meaningful outer sequence, because every operation carries
    /// its own role. A caller's sequence is therefore not a position anyone else
    /// can reproduce, so a failure located by it would move when the same intent
    /// arrived through a codec — which is why both the codec and the resolver
    /// read this order rather than the sequence they were handed.
    /// </remarks>
    public static IEnumerable<PortableQueryOrderOperation> InSemanticOrder(
        IEnumerable<PortableQueryOrderOperation> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.OrderBy(operation =>
            operation.Role.IsBaseline ? -1 : operation.Role.StageIndex);
    }

    /// <summary>Spells one operator identity.</summary>
    public static string TextOf(PortableQueryOperator value) => value switch
    {
        PortableQueryOperator.Equal => "eq",
        PortableQueryOperator.NotEqual => "ne",
        PortableQueryOperator.StartsWith => "starts-with",
        PortableQueryOperator.NotStartsWith => "not-starts-with",
        PortableQueryOperator.Contains => "contains",
        PortableQueryOperator.NotContains => "not-contains",
        PortableQueryOperator.AtLeast => "gte",
        PortableQueryOperator.AtMost => "lte",
        _ => throw Undefined(value, nameof(value))
    };

    /// <summary>Spells one direction identity.</summary>
    public static string TextOf(PortableQueryDirection value) => value switch
    {
        PortableQueryDirection.Ascending => "asc",
        PortableQueryDirection.Descending => "desc",
        _ => throw Undefined(value, nameof(value))
    };

    /// <summary>Spells one order-kind identity.</summary>
    public static string TextOf(PortableQueryOrderKind value) => value switch
    {
        PortableQueryOrderKind.Named => "named",
        PortableQueryOrderKind.Fields => "fields",
        _ => throw Undefined(value, nameof(value))
    };

    /// <summary>
    /// Spells one stage-kind identity. The kinds themselves belong to
    /// <see cref="RowSelectionStageKind"/>, whose owner is semantic row selection;
    /// this model carries their texts.
    /// </summary>
    public static string TextOf(RowSelectionStageKind value) => value switch
    {
        RowSelectionStageKind.Head => "head",
        RowSelectionStageKind.Tail => "tail",
        RowSelectionStageKind.Window => "window",
        RowSelectionStageKind.Top => "top",
        _ => throw Undefined(value, nameof(value))
    };

    /// <summary>Reads one operator identity, or reports that no operator has this text.</summary>
    public static bool TryParseOperator(string text, out PortableQueryOperator value)
    {
        switch (text)
        {
            case "eq": value = PortableQueryOperator.Equal; return true;
            case "ne": value = PortableQueryOperator.NotEqual; return true;
            case "starts-with": value = PortableQueryOperator.StartsWith; return true;
            case "not-starts-with": value = PortableQueryOperator.NotStartsWith; return true;
            case "contains": value = PortableQueryOperator.Contains; return true;
            case "not-contains": value = PortableQueryOperator.NotContains; return true;
            case "gte": value = PortableQueryOperator.AtLeast; return true;
            case "lte": value = PortableQueryOperator.AtMost; return true;
            default: value = default; return false;
        }
    }

    /// <summary>Reads one direction identity, or reports that no direction has this text.</summary>
    public static bool TryParseDirection(string text, out PortableQueryDirection value)
    {
        switch (text)
        {
            case "asc": value = PortableQueryDirection.Ascending; return true;
            case "desc": value = PortableQueryDirection.Descending; return true;
            default: value = default; return false;
        }
    }

    /// <summary>Reads one order-kind identity, or reports that no order kind has this text.</summary>
    public static bool TryParseOrderKind(string text, out PortableQueryOrderKind value)
    {
        switch (text)
        {
            case "named": value = PortableQueryOrderKind.Named; return true;
            case "fields": value = PortableQueryOrderKind.Fields; return true;
            default: value = default; return false;
        }
    }

    /// <summary>Reads one stage-kind identity, or reports that no stage kind has this text.</summary>
    public static bool TryParseStageKind(string text, out RowSelectionStageKind value)
    {
        switch (text)
        {
            case "head": value = RowSelectionStageKind.Head; return true;
            case "tail": value = RowSelectionStageKind.Tail; return true;
            case "window": value = RowSelectionStageKind.Window; return true;
            case "top": value = RowSelectionStageKind.Top; return true;
            default: value = default; return false;
        }
    }

    internal static ArgumentOutOfRangeException Undefined<TEnum>(
        TEnum value,
        string parameterName)
        where TEnum : struct, Enum =>
        new(parameterName, value, $"Unsupported {typeof(TEnum).Name} value.");

    private sealed class ScalarOrderComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            int shared = Math.Min(x.Length, y.Length);
            for (int index = 0; index < shared; index++)
            {
                char left = x[index];
                char right = y[index];
                if (left != right) return Weight(left).CompareTo(Weight(right));
            }

            return x.Length.CompareTo(y.Length);
        }

        // UTF-16 storage orders a surrogate pair — a scalar at U+10000 or above —
        // below U+E000..U+FFFF, while UTF-8 bytes and scalar values order it above.
        // Shifting the two ranges past each other restores scalar order without
        // encoding either string: below U+D800 is unchanged, U+E000..U+FFFF moves
        // down into the vacated block, and a surrogate code unit moves to the top.
        private static int Weight(char value) => value switch
        {
            < '\ud800' => value,
            <= '\udfff' => value + 0x2000,
            _ => value - 0x0800
        };
    }
}
