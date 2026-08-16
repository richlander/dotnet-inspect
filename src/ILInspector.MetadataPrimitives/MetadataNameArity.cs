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
/// The parse is per segment, and the segment boundary depends on the spelling the
/// caller holds — which is why there is no single "strip a name" entry point:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="StripFromSegment(string)"/> for one metadata
/// name (a <c>TypeDef</c>/<c>TypeRef</c> row's <c>Name</c>);</description></item>
/// <item><description><see cref="StripFromNestedName(string)"/> for a nested
/// metadata chain, where <c>+</c> is the only boundary and <c>.</c> is a literal
/// name character — the spelling <c>TypeRef.Name</c> carries;</description></item>
/// <item><description><see cref="StripFromDottedChain(string)"/> for a
/// namespace-free type-name chain whose nesting boundary is spelled <c>.</c> —
/// the spelling <c>ApiType.Name</c> carries;</description></item>
/// <item><description><see cref="StripFromFlattenedName(string)"/> for already
/// flattened display or search text, where the nesting spelling is no longer
/// known. It treats both delimiters as boundaries and is therefore not an
/// identity contract.</description></item>
/// </list>
/// <para>
/// A namespace is never part of these inputs. Namespace text is not a type-name
/// segment and carries no arity, so a caller holding a namespace-qualified name
/// keeps the namespace aside and parses only the type-name chain.
/// </para>
/// <para>
/// <c>MetadataNameArityTests</c> in <c>ILInspector.Metadata.Tests</c> is the gate
/// for every rule above, including the identity collisions that first-backtick
/// truncation and boundary conflation produced.
/// </para>
/// </remarks>
public static class MetadataNameArity
{
    /// <summary>
    /// The largest arity a metadata image can encode. ECMA-335 II.22.20 gives a
    /// <c>GenericParam</c> row a 2-byte zero-based <c>Number</c>, so the highest
    /// index is 65535 and a name can declare 65536 parameters; a larger decimal
    /// suffix is not an arity.
    /// </summary>
    public const int MaxArity = ushort.MaxValue + 1;

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
    /// A nested metadata name with each segment's canonical generic-arity suffix
    /// removed (<c>Outer`1+Inner`2</c> becomes <c>Outer+Inner</c>). Only <c>+</c>
    /// is a segment boundary: a <c>.</c> is an ordinary metadata-name character
    /// here, so <c>&lt;&gt;c__DisplayClass1`1.Foo</c> — one name whose text
    /// contains a dot — keeps its suffix and its identity.
    /// </summary>
    public static string StripFromNestedName(string name)
        => StripDelimited(name, dotIsBoundary: false, plusIsBoundary: true);

    /// <summary>
    /// A namespace-free type-name chain whose nesting boundary is spelled
    /// <c>.</c> — the flattening <c>ApiType.Name</c> uses — with each segment's
    /// canonical generic-arity suffix removed (<c>Outer`1.Inner`2</c> becomes
    /// <c>Outer.Inner</c>). Never pass a namespace-qualified name: namespace text
    /// is not a type-name segment.
    /// </summary>
    public static string StripFromDottedChain(string chain)
        => StripDelimited(chain, dotIsBoundary: true, plusIsBoundary: false);

    /// <summary>
    /// Already flattened display or search text, with the canonical
    /// generic-arity suffix removed from every <c>.</c>/<c>+</c>-delimited
    /// component. The two delimiters cannot be told apart once a name is
    /// flattened, so this is a display and matching convenience, not an identity
    /// contract; identity paths use the boundary-exact members above.
    /// </summary>
    public static string StripFromFlattenedName(string name)
        => StripDelimited(name, dotIsBoundary: true, plusIsBoundary: true);

    /// <summary>
    /// Walks a name's components and the canonical arity suffix of each, so a
    /// consumer that must rewrite or inspect parts of a name in place shares this
    /// boundary walk and this grammar instead of re-deriving either.
    /// </summary>
    public static MetadataNameComponentEnumerator EnumerateComponents(
        string name,
        bool dotIsBoundary = true,
        bool plusIsBoundary = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new MetadataNameComponentEnumerator(name, dotIsBoundary, plusIsBoundary);
    }

    static string StripDelimited(string name, bool dotIsBoundary, bool plusIsBoundary)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!name.Contains('`', StringComparison.Ordinal))
            return name;

        var builder = new StringBuilder(name.Length);
        foreach (MetadataNameComponent component in
            EnumerateComponents(name, dotIsBoundary, plusIsBoundary))
        {
            builder.Append(name, component.Start, component.SimpleNameLength);
            if (component.Delimiter is { } delimiter)
                builder.Append(delimiter);
        }

        return builder.ToString();
    }
}

/// <summary>
/// One component of a metadata name, its canonical generic arity, and the
/// delimiter that ended it. <see cref="SimpleNameLength"/> equals
/// <see cref="Length"/> when the component carries no canonical arity suffix, so
/// a consumer that copies <see cref="SimpleNameLength"/> characters preserves a
/// non-arity backtick exactly.
/// </summary>
/// <param name="Start">Index of the component's first character in the name.</param>
/// <param name="Length">Length of the component, excluding its delimiter.</param>
/// <param name="SimpleNameLength">Length of the component without a canonical arity suffix.</param>
/// <param name="Arity">The canonical arity, or 0 when the component declares none.</param>
/// <param name="Delimiter">The delimiter that ended the component, or null at the end of the name.</param>
public readonly record struct MetadataNameComponent(
    int Start,
    int Length,
    int SimpleNameLength,
    int Arity,
    char? Delimiter)
{
    /// <summary>Index just past the component's arity suffix, i.e. its end.</summary>
    public int End => Start + Length;

    /// <summary>Index just past the component's simple name.</summary>
    public int SimpleNameEnd => Start + SimpleNameLength;
}

/// <summary>Enumerates the components of a metadata name. See <see cref="MetadataNameArity.EnumerateComponents"/>.</summary>
public struct MetadataNameComponentEnumerator
{
    readonly string name;
    readonly bool dotIsBoundary;
    readonly bool plusIsBoundary;
    int next;

    internal MetadataNameComponentEnumerator(string name, bool dotIsBoundary, bool plusIsBoundary)
    {
        this.name = name;
        this.dotIsBoundary = dotIsBoundary;
        this.plusIsBoundary = plusIsBoundary;
        next = 0;
        Current = default;
    }

    public MetadataNameComponent Current { get; private set; }

    public readonly MetadataNameComponentEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        if (next > name.Length)
            return false;

        int start = next;
        int end = start;
        while (end < name.Length && !IsBoundary(name[end]))
            end++;

        MetadataNameArity.TryReadSuffix(
            name.AsSpan(start, end - start),
            out int arity,
            out int simpleNameLength);
        Current = new MetadataNameComponent(
            start,
            end - start,
            simpleNameLength,
            arity,
            end < name.Length ? name[end] : null);
        next = end + 1;
        return true;
    }

    readonly bool IsBoundary(char value)
        => (dotIsBoundary && value == '.') || (plusIsBoundary && value == '+');
}
