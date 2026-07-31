using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace InertText;

/// <summary>
/// Text that has already been through <see cref="VisualEncoder"/>, carried as a value rather
/// than as a <see cref="string"/>.
/// </summary>
/// <remarks>
/// This is the currency form, and it exists for auditability. The static encoder is
/// transactional — a <see cref="string"/> goes in and a <see cref="string"/> comes out — so a
/// treated value and an untreated one are the same type, and the only way to tell whether a
/// sink is safe is to trace every call path that reaches it. A distinct type inverts that: the
/// question becomes a type search, and a sink that accepts only <see cref="InertString"/>
/// cannot be handed raw text by accident.
///
/// There is deliberately no conversion <em>from</em> <see cref="string"/>, implicit or
/// explicit. One would restore exactly the confusion the type removes. Text enters through
/// <see cref="Encode"/> or <see cref="Format"/>, both of which apply a policy, so the invariant
/// holds by construction: an <see cref="InertString"/> contains no scalar its policy refused.
///
/// Conversion <em>to</em> <see cref="string"/> through <see cref="ToString"/> is unrestricted,
/// which is safe here in a way it usually is not. The customary objection to a wrapper — that
/// <c>ToString()</c> launders it — assumes the payload is dangerous and the wrapper is what
/// holds it back. Here the payload is already inert, and the wrapper only records that fact.
/// Losing the wrapper loses provenance, not protection.
/// </remarks>
public readonly struct InertString : IEquatable<InertString>
{
    private readonly string? _text;

    internal InertString(string text, VisualForm forms)
    {
        _text = text;
        Forms = forms;
    }

    /// <summary>The empty value, which trivially satisfies the invariant.</summary>
    public static InertString Empty => default;

    /// <summary>The spellings <see cref="VisualEncoder"/> emitted while producing this value.</summary>
    /// <remarks>
    /// Retained so a sink can print a legend for what it is about to show without re-deriving
    /// it. Composition unions the flags, so a message assembled from several pieces reports
    /// every spelling it contains.
    /// </remarks>
    public VisualForm Forms { get; }

    /// <summary>Whether this value carries no text.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_text);

    /// <summary>Whether any scalar was encoded on the way in.</summary>
    public bool WasEncoded => Forms != VisualForm.None;

    /// <summary>The encoded text.</summary>
    public override string ToString() => _text ?? string.Empty;

    /// <summary>
    /// Encodes <paramref name="value"/> under <paramref name="permits"/>.
    /// </summary>
    /// <param name="value">The untreated text.</param>
    /// <param name="permits">The per-sink policy deciding what may pass through.</param>
    public static InertString Encode(string value, ScalarPolicy permits)
    {
        string encoded = VisualEncoder.Encode(value, permits, out VisualForm forms);
        return new InertString(encoded, forms);
    }

    /// <summary>
    /// Builds a value from an interpolated string, encoding every part of it.
    /// </summary>
    /// <remarks>
    /// The composition path, and the reason the type is usable at a message-building site. A
    /// sink that takes an <see cref="InertString"/> would otherwise force callers back to
    /// <c>$"...{value}..."</c> on the encoded text, which produces a bare <see cref="string"/>
    /// and drops the guarantee at the one moment it is most needed.
    ///
    /// Interpolation holes are encoded, which is the point. Literals are encoded too, even
    /// though they come from source and are normally harmless, because an invariant with an
    /// exception in it has to be reasoned about at every use, and a bidi override is as
    /// invisible in a C# source file as it is anywhere else.
    /// </remarks>
    /// <param name="permits">The per-sink policy deciding what may pass through.</param>
    /// <param name="handler">The interpolated string, encoded piecewise as it is appended.</param>
    public static InertString Format(
        ScalarPolicy permits,
        [InterpolatedStringHandlerArgument("permits")] ref InertStringHandler handler)
    {
        ArgumentNullException.ThrowIfNull(permits);
        return handler.ToInertString();
    }

    /// <summary>
    /// Concatenates <paramref name="values"/>, separated by <paramref name="separator"/>.
    /// </summary>
    /// <remarks>
    /// The separator is encoded under <paramref name="permits"/> like everything else, so a
    /// multi-line message must pass a policy that permits the line break it joins with.
    /// Exempting the separator would be a hole exactly the size of one caller's mistake.
    /// </remarks>
    /// <param name="separator">The text placed between values.</param>
    /// <param name="permits">The per-sink policy applied to <paramref name="separator"/>.</param>
    /// <param name="values">The values to join.</param>
    public static InertString Join(string separator, ScalarPolicy permits, IEnumerable<InertString> values)
    {
        ArgumentNullException.ThrowIfNull(separator);
        ArgumentNullException.ThrowIfNull(values);

        string encodedSeparator = VisualEncoder.Encode(separator, permits, out VisualForm separatorForms);
        StringBuilder builder = new();
        VisualForm forms = VisualForm.None;
        bool first = true;

        foreach (InertString value in values)
        {
            // The separator's spellings are folded in only when one is actually emitted, so a
            // single-element join cannot report a form the output does not contain.
            if (!first)
            {
                builder.Append(encodedSeparator);
                forms |= separatorForms;
            }

            builder.Append(value.ToString());
            forms |= value.Forms;
            first = false;
        }

        return new InertString(builder.ToString(), forms);
    }

    /// <summary>Names the spellings this value contains, one line each.</summary>
    public IReadOnlyList<string> DescribeLegend() => VisualEncoder.DescribeLegend(Forms);

    /// <inheritdoc/>
    public bool Equals(InertString other)
        => string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InertString other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

    /// <summary>Compares two values by their encoded text.</summary>
    public static bool operator ==(InertString left, InertString right) => left.Equals(right);

    /// <summary>Compares two values by their encoded text.</summary>
    public static bool operator !=(InertString left, InertString right) => !left.Equals(right);
}

/// <summary>
/// Assembles an <see cref="InertString"/> from an interpolated string, applying the policy to
/// each piece as it arrives.
/// </summary>
/// <remarks>
/// Encoding per piece rather than once at the end is what makes composition safe. Encoding the
/// assembled string would be indistinguishable from encoding a concatenation, and would
/// re-encode any already-inert part that was spliced in — turning <c>\u202E</c> into
/// <c>\\u202E</c> and breaking invertibility.
/// </remarks>
[InterpolatedStringHandler]
public ref struct InertStringHandler
{
    private readonly ScalarPolicy _permits;
    private readonly StringBuilder _builder;
    private VisualForm _forms;

    /// <summary>Called by the compiler for an interpolated string argument.</summary>
    /// <param name="literalLength">The total length of the literal parts.</param>
    /// <param name="formattedCount">The number of interpolation holes.</param>
    /// <param name="permits">The per-sink policy deciding what may pass through.</param>
    public InertStringHandler(int literalLength, int formattedCount, ScalarPolicy permits)
    {
        ArgumentNullException.ThrowIfNull(permits);

        _permits = permits;
        _builder = new StringBuilder(literalLength + (formattedCount * 12));
        _forms = VisualForm.None;
    }

    /// <summary>Appends a literal part of the interpolated string.</summary>
    public void AppendLiteral(string value) => Append(value);

    /// <summary>Appends an interpolation hole.</summary>
    public void AppendFormatted<T>(T value) => Append(value?.ToString());

    /// <summary>Appends an interpolation hole that carries a format specifier.</summary>
    public void AppendFormatted<T>(T value, string? format)
        => Append(value is IFormattable formattable
            ? formattable.ToString(format, CultureInfo.InvariantCulture)
            : value?.ToString());

    /// <summary>Appends a string-valued interpolation hole.</summary>
    public void AppendFormatted(string? value) => Append(value);

    /// <summary>
    /// Appends a value that is already inert, without encoding it a second time.
    /// </summary>
    /// <remarks>
    /// Without this overload the generic case would run the encoder over text that has already
    /// been through it, doubling every backslash the first pass introduced.
    /// </remarks>
    public void AppendFormatted(InertString value)
    {
        _builder.Append(value.ToString());
        _forms |= value.Forms;
    }

    /// <summary>
    /// Appends an optional already-inert value, without encoding it a second time.
    /// </summary>
    /// <remarks>
    /// Needed as its own overload because a <c>InertString?</c> hole would otherwise bind to the
    /// generic case, whose <c>ToString</c> yields the encoded text and hands it back to the
    /// encoder. Redaction returns this shape, so the trap is on a live path rather than
    /// hypothetical.
    /// </remarks>
    public void AppendFormatted(InertString? value)
    {
        if (value is { } inert)
            AppendFormatted(inert);
    }

    internal InertString ToInertString() => new(_builder.ToString(), _forms);

    private void Append(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        _builder.Append(VisualEncoder.Encode(value, _permits, out VisualForm forms));
        _forms |= forms;
    }
}
