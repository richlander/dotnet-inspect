using System.Globalization;

namespace ILInspector.Metadata;

/// <summary>
/// What a projection does with artifact text that is not permitted as it is.
/// </summary>
/// <remarks>
/// The three values are a deliberate ladder, and the order of the members is the order of
/// decreasing safety. Containment is the property the projection guarantees; refusal and raw
/// output are policies layered either side of it, and only a caller — not a library — knows
/// which one its situation wants.
/// </remarks>
public enum UntrustedTextMode
{
    /// <summary>
    /// Render every scalar the policy refuses as an inert spelling. The default, and the only
    /// value that is safe without qualification: output is legible, faithful about what the
    /// artifact contains, and cannot reprogram a terminal or break the selected format.
    /// </summary>
    Contain,

    /// <summary>
    /// Fail rather than render an artifact containing text the policy refuses.
    /// <para>
    /// Strictly stronger than <see cref="Contain"/>, and different in kind: containment answers
    /// "can this hurt the terminal", refusal answers "do I want to look at this at all". A
    /// consumer piping metadata into something that is not a terminal — a diff, an index, a
    /// build step — usually wants to hear that an artifact carries a bidi override rather than
    /// to receive a faithful rendering of one.
    /// </para>
    /// <para>
    /// Output is byte-identical to <see cref="Contain"/> whenever it succeeds, so this costs
    /// nothing on the ordinary artifacts that carry no such text.
    /// </para>
    /// </summary>
    Refuse,

    /// <summary>
    /// Render artifact text exactly as the artifact spells it, with no containment at all.
    /// <para>
    /// This is the unsafe value and it exists for one reason: a person studying a hostile
    /// artifact sometimes needs to see the bytes, not a description of them. It reintroduces
    /// precisely the terminal-control and format-injection exposure the rest of this type
    /// exists to remove, so nothing selects it by default and nothing should select it on a
    /// caller's behalf.
    /// </para>
    /// </summary>
    Raw,
}

/// <summary>
/// Thrown under <see cref="UntrustedTextMode.Refuse"/> when artifact text carries a scalar the
/// policy does not permit.
/// </summary>
/// <remarks>
/// Carries the location and the classification, never the offending text. That is the whole
/// point of refusing: a caller that wanted to see the characters would have asked for them, and
/// a diagnostic that echoed them would reintroduce the exposure through the error path — which
/// is the more dangerous route of the two, because errors are read on terminals and are rarely
/// piped through the same containment the output is.
/// <para>
/// The heap coordinate is kept structured rather than formatted because the stream spelling
/// (<c>#Strings</c>, <c>#US</c>) belongs to the rendering layer, which sits above this one.
/// </para>
/// </remarks>
public sealed class UntrustedTextException : Exception
{
    internal UntrustedTextException(TextOrigin origin, int index, int scalar, UnicodeCategory category)
        : base(Describe(origin, index, scalar, category))
    {
        Heap = origin.Heap;
        Offset = origin.Offset;
        Index = index;
        Scalar = scalar;
        Category = category;
    }

    /// <summary>The heap the text was read from, or <see langword="null"/> for a resolved display.</summary>
    public HeapKind? Heap { get; }

    /// <summary>The heap address the text was read from, when <see cref="Heap"/> is known.</summary>
    public int Offset { get; }

    /// <summary>The UTF-16 index of the refused scalar within the decoded text.</summary>
    public int Index { get; }

    /// <summary>The refused scalar's code point.</summary>
    public int Scalar { get; }

    /// <summary>The refused scalar's Unicode general category.</summary>
    public UnicodeCategory Category { get; }

    static string Describe(TextOrigin origin, int index, int scalar, UnicodeCategory category)
    {
        string where = origin.Heap is { } heap
            ? $"the {heap} heap value at address 0x{origin.Offset:X}"
            : origin.Description ?? "artifact text";

        return $"{where} contains U+{scalar:X4} ({category}) at index {index}, " +
            "which is not permitted as rendered text.";
    }
}

/// <summary>
/// Where a piece of artifact text came from, for a diagnostic that must locate it without
/// quoting it.
/// </summary>
/// <remarks>
/// A struct carrying two integers and a literal, so naming the origin of every cell costs no
/// allocation on the path that never fails — which is every path on an ordinary artifact.
/// </remarks>
internal readonly record struct TextOrigin(HeapKind? Heap, int Offset, string? Description)
{
    /// <summary>Text read from a heap at a known address, which a reader can go and inspect.</summary>
    public static TextOrigin At(HeapKind heap, int offset) => new(heap, offset, null);

    /// <summary>Text with no address of its own, named by what produced it.</summary>
    public static TextOrigin Named(string description) => new(null, 0, description);
}
