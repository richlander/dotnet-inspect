using System.Runtime.CompilerServices;
using System.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// A piece of printed text that remembers where the nodes inside it landed.
/// </summary>
/// <remarks>
/// <para>
/// The expression printer composes bottom-up by returning strings, so a node's
/// characters have no knowable offset until an enclosing statement is appended.
/// Recovering the offset afterwards means <em>searching</em> the statement for
/// the node's text, which cannot distinguish two identical spellings and has to
/// refuse whenever it finds more than one.
/// </para>
/// <para>
/// A <see cref="Frag"/> removes the search. The only code that knows where a
/// child's characters landed is the parent that concatenated them, so the
/// parent records it at the moment of composition. Composers keep their
/// interpolated-string bodies verbatim: <see cref="FragInterpolatedStringHandler"/>
/// intercepts each hole and notes the offset the value was written at.
/// </para>
/// <para>
/// Conversion runs in both directions on purpose. <c>string</c> to
/// <see cref="Frag"/> lets an unconverted composer feed a converted one — the
/// text is still correct, it simply carries no interior positions.
/// <see cref="Frag"/> to <c>string</c> lets a converted composer feed an
/// unconverted one. That is what makes the migration incremental rather than a
/// single 203-method commit: every intermediate state compiles and prints
/// identical text, and each conversion only adds positions.
/// </para>
/// <para>
/// When <see cref="Spans"/> is null nothing is being collected and a
/// <see cref="Frag"/> is exactly a <c>string</c> plus a null reference; the
/// shipped print path allocates no list and pays no bookkeeping.
/// </para>
/// </remarks>
public readonly struct Frag
{
    /// <summary>The printed text.</summary>
    public string Text { get; }

    /// <summary>
    /// Where each node inside <see cref="Text"/> starts and how long it is,
    /// relative to the start of <see cref="Text"/>. Null when positions are not
    /// being collected.
    /// </summary>
    internal List<(IrNode Node, int Start, int Length)>? Spans { get; }

    internal Frag(string text, List<(IrNode Node, int Start, int Length)>? spans)
    {
        Text = text;
        Spans = spans;
    }

    /// <summary>Wraps text that carries no interior positions.</summary>
    public Frag(string text)
    {
        Text = text;
        Spans = null;
    }

    /// <summary>
    /// Marks this fragment as the printed form of <paramref name="node"/>, so an
    /// enclosing fragment records the node when it appends this one.
    /// </summary>
    internal Frag Attribute(IrNode node)
    {
        if (Text.Length == 0)
            return this;
        var spans = Spans ?? [];
        spans.Add((node, 0, Text.Length));
        return new Frag(Text, spans);
    }

    /// <summary>
    /// Builds a fragment from an interpolated string, recording where each
    /// interpolated <see cref="Frag"/> landed.
    /// </summary>
    /// <remarks>
    /// This is the only form that preserves positions, and the reason there is
    /// no <c>string</c>-to-<see cref="Frag"/> conversion. An interpolated string
    /// is converted by a handler only when the target is a parameter of the
    /// handler type; assigning <c>$"..."</c> to a <see cref="Frag"/> would bind
    /// through <c>string</c> instead, producing correct text with every interior
    /// position silently dropped. Without the implicit conversion that mistake
    /// does not compile, so an unconverted composer is a build error rather than
    /// an invisible reversion to searching for the text afterwards.
    /// </remarks>
    /// <param name="handler">The interpolation, which accumulates the positions.</param>
    /// <returns>The composed fragment and the offsets of its interpolated parts.</returns>
    public static Frag Of(FragInterpolatedStringHandler handler) => handler.Build();

    /// <summary>The printed text, discarding interior positions.</summary>
    public static implicit operator string(Frag fragment) => fragment.Text;

    /// <inheritdoc/>
    public override string ToString() => Text;
}

/// <summary>
/// Builds a <see cref="Frag"/> from an interpolated string, recording the offset
/// at which each interpolated <see cref="Frag"/> was written.
/// </summary>
/// <remarks>
/// This is the whole point of the type: the offset is captured by the code doing
/// the concatenation, which is the only code that knows it. Nothing is searched
/// for afterwards, so two identical spellings in one expression are no longer
/// ambiguous — each was written at a different, recorded offset.
/// </remarks>
[InterpolatedStringHandler]
public ref struct FragInterpolatedStringHandler
{
    readonly StringBuilder _builder;
    List<(IrNode Node, int Start, int Length)>? _spans;

    /// <summary>Creates a handler for an interpolated string.</summary>
    public FragInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _builder = new StringBuilder(literalLength + (formattedCount * 8));
        _spans = null;
    }

    /// <summary>Appends literal text between holes.</summary>
    public void AppendLiteral(string value) => _builder.Append(value);

    /// <summary>
    /// Appends a fragment, recording where it landed and lifting the positions it
    /// already carried into this fragment's coordinates.
    /// </summary>
    public void AppendFormatted(Frag value)
    {
        int at = _builder.Length;
        _builder.Append(value.Text);
        if (value.Spans is not { Count: > 0 } inner)
            return;
        _spans ??= [];
        foreach (var (node, start, length) in inner)
            _spans.Add((node, at + start, length));
    }

    /// <summary>Appends text that carries no interior positions.</summary>
    public void AppendFormatted(string? value) => _builder.Append(value);

    /// <summary>Appends a value that carries no interior positions.</summary>
    public void AppendFormatted<T>(T value) => _builder.Append(value?.ToString());

    /// <summary>Appends a value with a format string.</summary>
    public void AppendFormatted<T>(T value, string? format) where T : IFormattable
        => _builder.Append(value?.ToString(format, null));

    /// <summary>
    /// The fragment this handler accumulated, carrying the offset every
    /// interpolated <see cref="Frag"/> was written at.
    /// </summary>
    public Frag Build() => new(_builder.ToString(), _spans);
}
