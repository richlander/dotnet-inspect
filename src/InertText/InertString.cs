using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using InertText.Encoder;

namespace InertText;

/// <summary>
/// Text with a <see cref="ScalarPolicy"/> applied to it, carried as a value rather than as a
/// bare <see cref="string"/>.
/// </summary>
/// <remarks>
/// This is the currency form, and it exists for auditability. Encoding on its own is
/// transactional — a <see cref="string"/> goes in and a <see cref="string"/> comes out — so a
/// treated value and an untreated one are the same type, and the only way to tell whether a
/// sink is safe is to trace every call path that reaches it. A distinct type inverts that: the
/// question becomes a type search, and a sink that accepts only <see cref="InertString"/>
/// cannot be handed raw text by accident.
///
/// The second half of that is what this type does <em>not</em> offer. Holding one of these
/// gives no way back to the text it was built from: the decoder lives in
/// <c>InertText.Encoder</c>, in its own namespace, and nothing here reaches it. So a file that
/// imports <c>InertText</c> and not <c>InertText.Encoder</c> cannot recover the original of any
/// value it handles, and that fact is legible in its using block rather than by tracing calls.
/// A reflection test enumerates the public surface of this namespace and accounts for every
/// member that returns text, so the property is enforced rather than merely intended.
///
/// The boundary is an audit aid, not a capability barrier — a file can always add the import.
/// The goal it does meet is that the dangerous half cannot arrive by accident or unnoticed.
///
/// Also genuinely separate is <see cref="ScalarPolicy"/>: the encoder never learns <em>why</em>
/// a scalar was refused, which is what lets it serve a deny-shaped sink and an allow-shaped one
/// alike. An encoder with a built-in hazard set has absorbed policy.
///
/// The term and the contract are borrowed from BSD <c>vis(3)</c> ("visually encode
/// characters"): the output is inert, lossless (nothing is dropped, so the reader still sees
/// what was actually there), and invertible (<c>VisualEncoder.TryDecode</c> recovers the original
/// exactly). This is not neutralization, which has none of the three.
///
/// "Inert" is scoped, and the scope matters: no terminal interprets the output as control and
/// no bidi algorithm reorders it. It does <em>not</em> mean the output is safe to drop into a
/// structured format. A <c>|</c> still breaks a Markdown cell, a backtick still opens a span,
/// and a <c>"</c> still terminates a JSON string — none is in any encoded category, and none
/// should be, because escaping those for its own grammar is the serializer's job. Visual
/// encoding and structural escaping compose; neither substitutes for the other.
///
/// There is deliberately no conversion <em>from</em> <see cref="string"/>, implicit or
/// explicit. One would restore exactly the confusion the type removes. Text enters through
/// the constructor, <see cref="Format"/> or <see cref="Join"/>, all of which apply a policy,
/// so every value has been spelled under some policy.
///
/// Note the "some": the type records that a policy was applied, not which one, because a value
/// is routinely built for one sink and spliced into a message bound for another. That makes
/// the useful invariant a property of composition rather than of storage — see
/// <see cref="Conform"/>, which re-spells a spliced value under the policy actually in force.
///
/// Conversion <em>to</em> <see cref="string"/> through <see cref="ToString"/> is unrestricted,
/// which is safe here in a way it usually is not. The customary objection to a wrapper — that
/// <c>ToString()</c> launders it — assumes the payload is dangerous and the wrapper is what
/// holds it back. Here the payload is already inert, and the wrapper only records that fact.
/// Losing the wrapper loses provenance, not protection.
/// </remarks>
public readonly struct InertString : IEquatable<InertString>
{
    // A constructor is the only thing that assigns this, and both constructors assign encoder
    // output, so every value that a caller can build carries text. The `?` describes the single
    // state no constructor can reach: default(InertString), whose field the CLR zeroes without
    // running any constructor at all. A struct cannot suppress its zero value, so a non-nullable
    // annotation here would be unverifiable -- it compiles without a warning, because the
    // compiler does not track default(T) through to fields -- and would promise every reader
    // something the runtime is free to contradict.
    //
    // No downstream code is defensive about it. Text is the sole reader and maps the zero value
    // to empty; a reflection test fails any public member that reads around it.
    private readonly string? _text;

    /// <summary>
    /// Encodes <paramref name="value"/> under <paramref name="permits"/>, yielding a value that
    /// can be carried to a sink.
    /// </summary>
    /// <remarks>
    /// The only way text enters the type, and the reason no member of it can take text without
    /// also taking a policy — a reflection test enforces that.
    ///
    /// Forwards to <see cref="VisualEncoder"/> rather than duplicating the loop, and exists so
    /// that producing inert text does not require naming the capability namespace. That is what
    /// keeps the decoder out of the files that merely make inert text, and it is gated.
    /// </remarks>
    /// <param name="value">The untreated text.</param>
    /// <param name="permits">The per-sink policy deciding what may pass through.</param>
    public InertString(string value, ScalarPolicy permits) => this = VisualEncoder.Encode(value, permits);

    // Takes text already spelled by the encoder, so it asserts rather than establishes the
    // invariant. Internal because composition needs it: Join and the interpolation handler
    // build their result piecewise and would otherwise have to re-encode an encoded string.
    internal InertString(string text, VisualForm forms)
    {
        _text = text;
        Forms = forms;
    }

    /// <summary>The text, with the zero value read as empty.</summary>
    /// <remarks>
    /// The single point where that translation happens. Every other member reads this rather
    /// than the field, because spelling the translation at each use site is what let equality
    /// disagree with the rest of the type about whether the zero value and <c>Encode("")</c>
    /// are the same value. A reflection test enumerates the public surface and fails if any
    /// member answers differently for the two, which is the gate that keeps this honest.
    /// </remarks>
    private string Text => _text ?? string.Empty;

    /// <summary>The empty value, which trivially satisfies the invariant.</summary>
    /// <remarks>
    /// Constructed, not <c>default</c>. The zero value of a struct is an artifact of the CLR
    /// rather than a statement of intent, and naming it as the definition of "empty" describes
    /// how the runtime zeroes memory instead of what this value is. Stated properly, the
    /// contract is: no text, and no spellings emitted.
    ///
    /// <c>default(InertString)</c> is still reachable — a struct cannot suppress it — and it is
    /// still harmless, because empty text satisfies every policy <em>vacuously</em>: there is
    /// no scalar for a policy to refuse. So it is tolerated rather than blessed. The one place
    /// that tolerates it is <see cref="Text"/>, and a reflection test enumerates the public
    /// surface to catch any member that reads around it. Spelling that translation at four
    /// separate reads is what let equality disagree with <see cref="IsEmpty"/>,
    /// <see cref="ToString"/> and <see cref="GetHashCode"/> about whether the zero value and
    /// <c>Encode("")</c> are the same value.
    /// </remarks>
    public static InertString Empty { get; } = new(string.Empty, VisualForm.None);

    /// <summary>The spellings <see cref="InertString"/> emitted while producing this value.</summary>
    /// <remarks>
    /// Retained so a sink can print a legend for what it is about to show without re-deriving
    /// it. Composition unions the flags, so a message assembled from several pieces reports
    /// every spelling it contains.
    /// </remarks>
    public VisualForm Forms { get; }

    /// <summary>Whether this value carries no text.</summary>
    public bool IsEmpty => Text.Length == 0;

    /// <summary>Whether any scalar was encoded on the way in.</summary>
    public bool WasEncoded => Forms != VisualForm.None;

    /// <summary>The encoded text.</summary>
    public override string ToString() => Text;

    /// <summary>
    /// Encodes <paramref name="value"/> under <paramref name="permits"/>.
    /// </summary>
    /// <param name="value">The untreated text.</param>
    /// <param name="permits">The per-sink policy deciding what may pass through.</param>
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
    /// <param name="permits">
    /// The per-sink policy, applied to <paramref name="separator"/> and to any value that does
    /// not already satisfy it.
    /// </param>
    /// <param name="values">The values to join.</param>
    public static InertString Join(string separator, ScalarPolicy permits, IEnumerable<InertString> values)
    {
        ArgumentNullException.ThrowIfNull(separator);
        ArgumentNullException.ThrowIfNull(values);

        InertString encodedSeparator = VisualEncoder.Encode(separator, permits);
        StringBuilder builder = new();
        VisualForm forms = VisualForm.None;
        bool first = true;

        foreach (InertString value in values)
        {
            // The separator's spellings are folded in only when one is actually emitted, so a
            // single-element join cannot report a form the output does not contain.
            if (!first)
            {
                builder.Append(encodedSeparator.ToString());
                forms |= encodedSeparator.Forms;
            }

            InertString conformed = Conform(value, permits);
            builder.Append(conformed.ToString());
            forms |= conformed.Forms;
            first = false;
        }

        return new InertString(builder.ToString(), forms);
    }

    /// <summary>Names the spellings this value contains, one line each.</summary>
    public IReadOnlyList<string> DescribeLegend() => VisualEncoder.DescribeLegend(Forms);

        /// <summary>
    /// Reports whether every scalar in <paramref name="value"/> is permitted as it is.
    /// </summary>
    /// <remarks>
    /// The early-fail check, for callers that would rather reject text than display an encoded
    /// rendering of it. This is deliberately not "would encoding change it": a
    /// backslash is permitted by any sane policy but is still rewritten, and a check derived
    /// from the encoder would reject every Windows path.
    /// </remarks>
    public static bool IsPermitted(string value, ScalarPolicy permits)
        => IsPermitted(value, permits, out _);

    /// <summary>
    /// Reports whether every scalar in <paramref name="value"/> is permitted as it is, naming
    /// the first that is not.
    /// </summary>
    /// <remarks>
    /// The violation names a position and a classification, never the rendered character, which
    /// is what lets a survey mode report a finding without echoing artifact text.
    /// </remarks>
    public static bool IsPermitted(
        string value,
        ScalarPolicy permits,
        [NotNullWhen(false)] out ScalarViolation? violation)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(permits);

        int i = 0;
        while (i < value.Length)
        {
            Rune scalar = VisualEncoder.DecodeAt(value, i, out int width, out bool isUnpairedSurrogate);

            if (isUnpairedSurrogate)
            {
                // The raw code unit, not the decoded scalar: DecodeAt yields U+FFFD here,
                // and the whole point of the report is to name the code point exactly.
                violation = new ScalarViolation(i, value[i], UnicodeCategory.Surrogate);
                return false;
            }

            if (!permits(scalar))
            {
                violation = new ScalarViolation(i, scalar.Value, Rune.GetUnicodeCategory(scalar));
                return false;
            }

            i += width;
        }

        violation = null;
        return true;
    }

    /// <summary>
    /// Restates <paramref name="value"/> under <paramref name="permits"/>, re-encoding it if it
    /// carries anything that policy refuses.
    /// </summary>
    /// <remarks>
    /// The type records that <em>a</em> policy was applied, not <em>which</em> one, so a value
    /// produced under a laxer policy can carry a scalar a stricter sink refuses — <see
    /// cref="TextPolicy.Prose"/> permits the line feed that <see cref="TextPolicy.Field"/>
    /// exists to remove. Splicing such a value in unexamined would put a raw newline into a
    /// single-line message and report <see cref="VisualForm.None"/> for it, which is the log
    /// injection this library exists to prevent, with the type appearing to vouch for it.
    ///
    /// This is the second thing invertibility buys. Because <c>VisualEncoder.TryDecode</c>
    /// recovers the original exactly, a mismatched piece can be taken back to its source text
    /// and re-spelled under the policy actually in force, rather than rejected or trusted.
    ///
    /// The repair only ever tightens. A piece encoded under a stricter policy keeps its
    /// spellings when spliced into a laxer sink, because composition making a value <em>less</em>
    /// inert would let a caller launder one by quoting it somewhere permissive. The cost is that
    /// splice path is observable — the same source text can render differently depending on
    /// where it was encoded — which is a deliberate trade, not an oversight.
    /// </remarks>
    internal static InertString Conform(InertString value, ScalarPolicy permits)
    {
        string text = value.ToString();

        if (IsPermitted(text, permits))
        {
            return value;
        }

        // Falling back to the encoded text when decoding fails cannot happen for a value this
        // library produced; it is here so the failure mode is over-encoding rather than a leak.
        string original = VisualEncoder.TryDecode(text, out string? decoded) ? decoded : text;
        return VisualEncoder.Encode(original, permits);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reads through <see cref="Text"/> rather than the field, so the zero value and
    /// <c>Encode("")</c> compare equal. Comparing <c>_text</c> directly is the defect this
    /// replaced: <see langword="null"/> and <c>""</c> are not ordinally equal.
    /// </remarks>
    public bool Equals(InertString other) => string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is InertString other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);

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
    /// <remarks>
    /// Formatted under the invariant culture, matching the format-specifier overload. A message
    /// whose decimal separator depends on the ambient culture is a message that cannot be
    /// grepped, and these are diagnostics rather than presentation.
    /// </remarks>
    public void AppendFormatted<T>(T value)
        => Append(value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString());

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
    /// been through it, doubling every backslash the first pass introduced. The value is still
    /// checked against this sink's policy, because being inert under some policy is not the
    /// same as being inert under this one; see <see cref="InertString.Conform"/>.
    /// </remarks>
    public void AppendFormatted(InertString value)
    {
        InertString conformed = InertString.Conform(value, _permits);
        _builder.Append(conformed.ToString());
        _forms |= conformed.Forms;
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

        InertString encoded = VisualEncoder.Encode(value, _permits);
        _builder.Append(encoded.ToString());
        _forms |= encoded.Forms;
    }
}
