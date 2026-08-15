using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// Parses the canonical CLR generic-arity suffix of a metadata name. This is the
/// product-owned answer to "does this metadata name end in <c>`N</c>, and what is
/// the name without it?"; consumers must not re-derive it by truncating at a
/// backtick, because a backtick is a legal metadata-name character and truncation
/// collapses distinct identities (<c>Widget`Literal</c> and <c>Widget</c>) into one.
/// </summary>
/// <remarks>
/// <para>
/// A name carries an arity suffix only in the canonical form a metadata writer
/// emits for a generic type or method: a backtick followed by the decimal arity,
/// at the end of one metadata-name segment. Specifically, all of the following
/// must hold, measured from the <em>last</em> backtick in the segment:
/// </para>
/// <list type="bullet">
/// <item><description>the part before the backtick is non-empty;</description></item>
/// <item><description>the part after it is one or more ASCII decimal digits and
/// nothing else;</description></item>
/// <item><description>those digits have no leading zero, so <c>`0</c> and
/// <c>`01</c> are not arity suffixes — a metadata writer emits no suffix at all
/// for a non-generic name;</description></item>
/// <item><description>the value is at most <see cref="MaxArity"/>.</description></item>
/// </list>
/// <para>
/// Anything else — a literal suffix (<c>Widget`Literal</c>), digits followed by
/// more text (<c>Widget`1Extra</c>), a bare trailing backtick, or an
/// out-of-range count — is not an arity suffix, and the name is preserved
/// unchanged. Preserving it is what keeps such names visible: a backtick has no
/// C# spelling, so the spellability and identifier gates that consume these
/// results report the name rather than silently accepting a truncated one.
/// </para>
/// <para>
/// The parse is per segment. A nested metadata name spells its own arity on each
/// segment (<c>Outer`1+Inner`2</c>), so callers that hold a nested or dotted name
/// use <see cref="StripFromNestedName(string)"/>, which applies the same rule to
/// each <c>.</c>/<c>+</c>-delimited segment.
/// </para>
/// <para>
/// <c>MetadataNameArityTests</c> in <c>ILInspector.Metadata.Tests</c> is the gate
/// for every rule above, including the identity collisions that first-backtick
/// truncation produced.
/// </para>
/// </remarks>
public static class MetadataNameArity
{
    /// <summary>
    /// The largest arity a metadata image can encode. ECMA-335 II.22.20 gives a
    /// <c>GenericParam</c> row a 2-byte <c>Number</c>, so no image declares more
    /// than 65535 parameters on one name; a larger decimal suffix is not an
    /// arity.
    /// </summary>
    public const int MaxArity = ushort.MaxValue;

    // MaxArity is five digits, so a longer digit run is out of range by length
    // alone — checked before accumulating so the accumulation cannot overflow.
    const int MaxArityDigits = 5;

    /// <summary>
    /// Reads the canonical generic-arity suffix of one metadata-name segment.
    /// Returns false — leaving <paramref name="arity"/> zero and
    /// <paramref name="simpleNameLength"/> at the full length — when the segment
    /// carries no such suffix, including when it contains a non-arity backtick.
    /// </summary>
    public static bool TryReadSuffix(
        ReadOnlySpan<char> segment,
        out int arity,
        out int simpleNameLength)
    {
        arity = 0;
        simpleNameLength = segment.Length;

        int tick = segment.LastIndexOf('`');
        if (tick < 1 || tick == segment.Length - 1)
            return false;

        ReadOnlySpan<char> digits = segment[(tick + 1)..];
        if (digits.Length > MaxArityDigits || digits[0] == '0')
            return false;

        int value = 0;
        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c))
                return false;
            value = (value * 10) + (c - '0');
        }

        if (value > MaxArity)
            return false;

        arity = value;
        simpleNameLength = tick;
        return true;
    }

    /// <summary>
    /// The generic arity one metadata-name segment declares, or 0 when it
    /// declares none.
    /// </summary>
    public static int OfSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return TryReadSuffix(segment, out int arity, out _) ? arity : 0;
    }

    /// <summary>
    /// One metadata-name segment without its canonical generic-arity suffix
    /// (<c>List`1</c> becomes <c>List</c>). A segment with no such suffix is
    /// returned unchanged, backtick and all.
    /// </summary>
    public static string StripFromSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return TryReadSuffix(segment, out _, out int simpleNameLength)
            ? segment[..simpleNameLength]
            : segment;
    }

    /// <summary>
    /// A nested or namespace-qualified metadata name with each segment's
    /// canonical generic-arity suffix removed
    /// (<c>Outer`1+Inner`2</c> becomes <c>Outer+Inner</c>). Segment boundaries
    /// are <c>.</c> and <c>+</c>; a segment with no canonical suffix — including
    /// one holding a literal backtick — is preserved unchanged.
    /// </summary>
    public static string StripFromNestedName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!name.Contains('`', StringComparison.Ordinal))
            return name;

        var builder = new StringBuilder(name.Length);
        int start = 0;
        for (int i = 0; i <= name.Length; i++)
        {
            if (i != name.Length && name[i] is not ('.' or '+'))
                continue;

            ReadOnlySpan<char> segment = name.AsSpan(start, i - start);
            TryReadSuffix(segment, out _, out int simpleNameLength);
            builder.Append(segment[..simpleNameLength]);
            if (i != name.Length)
                builder.Append(name[i]);
            start = i + 1;
        }

        return builder.ToString();
    }
}
