using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace InertText.Encoding;

/// <summary>
/// The transform that makes text inert, and the decoder that recovers what it was given.
/// </summary>
/// <remarks>
/// Deliberately in its own namespace, because that is what makes the guarantee auditable.
/// <see cref="InertString"/> can be carried, composed and printed without ever naming this
/// type: text enters through its constructor, which calls in here, and leaves through
/// <c>ToString</c> already spelled. So a file that imports <c>InertText</c> and not
/// <c>InertText.Encoding</c> has no way to recover the original text of any value it handles,
/// and that is visible in its using block rather than by tracing calls.
///
/// The separation is an audit boundary, not a capability barrier. Nothing stops a file from
/// adding the import or writing the name out in full — but it cannot do so invisibly, and
/// making the dangerous half impossible to reach by accident is the achievable goal. A
/// reflection test enforces the other half of it: no public member of <see cref="InertString"/>
/// returns text derived from the original.
///
/// The name stays deliberately distinct from the currency type's. Two unrelated names give two
/// independent searches — one for who carries inert text, one for who can recover it — where a
/// shared prefix would blur both into a single noisy result.
/// </remarks>
public static class VisualEncoder
{
    /// <summary>
    /// Encodes <paramref name="value"/> as <paramref name="policy"/> requires, visually spelling
    /// every scalar that kind of text may not show.
    /// </summary>
    /// <remarks>
    /// Returns the currency type rather than a <see cref="string"/> and a form set, because the
    /// two are one result: <see cref="InertString.Forms"/> is exactly what an <c>out</c>
    /// parameter would have reported, and splitting them lets a caller keep the text while
    /// dropping the record of what was done to it — which is the confusion this library exists to
    /// remove. <see cref="InertString"/>'s constructor forwards here, so the two spellings cannot
    /// diverge.
    ///
    /// A literal backslash is always rewritten, whatever <paramref name="policy"/> permits, because
    /// it introduces every other spelling and the transform would not otherwise invert. An
    /// unpaired surrogate is likewise always encoded: it is not a scalar at all, so no policy is
    /// consulted for it.
    /// </remarks>
    /// <param name="policy">The kind of text this is, which decides what may pass through.</param>
    /// <param name="value">The text to encode.</param>
    /// <returns>
    /// The encoded text as an <see cref="InertString"/>, which carries the spellings that were
    /// emitted. Text that needed no encoding is returned as the original instance.
    /// </returns>
    public static InertString Encode(TextPolicy policy, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ScalarPolicy permits = ScalarPolicies.For(policy);

        VisualForm formsUsed = VisualForm.None;
        StringBuilder? builder = null;

        int i = 0;
        while (i < value.Length)
        {
            Rune scalar = DecodeAt(value, i, out int width, out bool isUnpairedSurrogate);
            bool encode = isUnpairedSurrogate || value[i] == '\\' || !permits(scalar);

            if (!encode)
            {
                builder?.Append(value, i, width);
                i += width;
                continue;
            }

            // Only allocate once something actually needs encoding, so ordinary text — which is
            // almost all text — is returned as it came in.
            builder ??= new StringBuilder(value.Length + 8).Append(value, 0, i);

            if (isUnpairedSurrogate)
            {
                AppendBmpHex(builder, value[i]);
                formsUsed |= VisualForm.BmpHex;
            }
            else
            {
                formsUsed |= AppendSpelling(builder, scalar);
            }

            i += width;
        }

        return new InertString(builder?.ToString() ?? value, formsUsed);
    }

    /// <summary>
    /// Recovers the original text from <see cref="Encode"/>'s output.
    /// </summary>
    /// <remarks>
    /// The reason this namespace exists. Every other operation in the library moves text
    /// <em>toward</em> being inert; this is the one that moves it back, so it is the one whose
    /// presence in a file is worth being able to see at a glance.
    ///
    /// It also exists because invertibility is an asserted property, and an encoder without a
    /// decoder cannot demonstrate it: a caret-introduced spelling survives casual inspection
    /// and only fails a round-trip that a decoder makes possible.
    /// </remarks>
    /// <returns>True when <paramref name="encoded"/> is well-formed.</returns>
    public static bool TryDecode(string encoded, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        value = null;
        StringBuilder builder = new(encoded.Length);

        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] != '\\')
            {
                if (char.IsHighSurrogate(encoded[i])
                    && i + 1 < encoded.Length
                    && char.IsLowSurrogate(encoded[i + 1]))
                {
                    // A raw astral scalar, which Encode passes through whenever the policy
                    // permits it: an emoji is So, and So is graphic.
                    builder.Append(encoded[i]).Append(encoded[++i]);
                    continue;
                }

                if (char.IsSurrogate(encoded[i]))
                {
                    // Encode never emits a raw unpaired surrogate; it spells one as \uXXXX.
                    // Accepting it here would give that input two encodings.
                    return false;
                }

                builder.Append(encoded[i]);
                continue;
            }

            if (++i == encoded.Length)
            {
                return false;
            }

            switch (encoded[i])
            {
                case '\\':
                    builder.Append('\\');
                    break;
                case '^':
                    if (++i == encoded.Length)
                    {
                        return false;
                    }

                    char caret = encoded[i];

                    if (caret == '?')
                    {
                        builder.Append('\u007F');
                    }
                    else if (caret is >= '@' and <= '_')
                    {
                        builder.Append((char)(caret - 0x40));
                    }
                    else
                    {
                        return false;
                    }

                    break;
                case 'u':
                    if (!TryReadHex(encoded, i + 1, 4, out uint bmp))
                    {
                        return false;
                    }

                    if (bmp <= 0x1F || bmp is 0x7F or '\\')
                    {
                        // Those three ranges have a canonical spelling — \^X, \^? and \\ — so
                        // accepting \uXXXX for them too would give one scalar two encodings,
                        // the same loss of injectivity the \U arm rejects below. Encode never
                        // emits these, so no round trip depends on them.
                        return false;
                    }

                    // \uD83D\uDE00 decodes to the astral scalar rather than being refused as a
                    // second spelling of \U0001F600. Refusing it looks like canonicalization but
                    // is not expressible: a .NET string is UTF-16, so "\uD83D" + "\uDE00" *is*
                    // "\U0001F600" -- there is no string in which those halves stay apart, and
                    // no other text the spelling could denote. Encode still only ever emits \U
                    // for a pair, so this arm is not a second output form; it is the input form
                    // composition produces. Join and the interpolation handler concatenate
                    // fragments that were each encoded alone, so a lone high surrogate encoded
                    // in one fragment lands beside a lone low surrogate encoded in the next.
                    // Refusing that made a value this library had just produced fail to decode,
                    // and EnsurePermitted then re-encoded the escapes as literal text -- turning
                    // \uD834\uDD73 into \\uD834\\uDD73, which is also what the ASCII text a user
                    // typed encodes to. Two unrelated inputs converged on one output.
                    i += 4;
                    builder.Append((char)bmp);
                    break;
                case 'U':
                    if (!TryReadHex(encoded, i + 1, 8, out uint astral)
                        || !Rune.IsValid((int)astral)
                        || astral <= 0xFFFF)
                    {
                        // Rejecting the BMP range keeps the transform injective: a scalar has
                        // exactly one spelling, so \U0000202E is not a second way to write
                        // \u202E.
                        return false;
                    }

                    builder.Append(new Rune((int)astral));
                    i += 8;
                    break;
                default:
                    return false;
            }
        }

        // An *unpaired* surrogate decoded from a \uXXXX arm is deliberately accepted. Cs is one
        // of the encoded categories, so Encode emits exactly this form for an unpaired surrogate,
        // and rejecting it here would make the encoder non-invertible on the one input class that
        // cannot be represented any other way. Two escapes forming a pair are accepted as well and
        // decode to the astral scalar, which is the only text that spelling can denote; injectivity
        // survives because Encode never *emits* that form -- a paired scalar always comes back as a
        // single \U escape -- so the two-escape spelling is an input form only.
        value = builder.ToString();
        return true;
    }

    /// <summary>
    /// Names the spellings in <paramref name="forms"/>, one line each.
    /// </summary>
    /// <remarks>
    /// Projected from the encoder rather than maintained separately, because a legend written
    /// independently drifts. It names forms and never values: artifact data does not belong on
    /// the diagnostic channel.
    /// </remarks>
    public static IReadOnlyList<string> DescribeLegend(VisualForm forms)
    {
        List<string> lines = [];

        if (forms.HasFlag(VisualForm.Caret))
        {
            lines.Add(@"\^X         a C0 control in caret notation; \^[ is ESC");
        }

        if (forms.HasFlag(VisualForm.CaretDelete))
        {
            lines.Add(@"\^?         DEL");
        }

        if (forms.HasFlag(VisualForm.BmpHex))
        {
            lines.Add(@"\uXXXX      the scalar at code point U+XXXX");
        }

        if (forms.HasFlag(VisualForm.AstralHex))
        {
            lines.Add(@"\UXXXXXXXX  the scalar at code point U+XXXXXXXX");
        }

        if (forms.HasFlag(VisualForm.Backslash))
        {
            lines.Add(@"\\          a literal backslash");
        }

        return lines;
    }

    private static VisualForm AppendSpelling(StringBuilder builder, Rune scalar)
    {
        switch (scalar.Value)
        {
            case '\\':
                builder.Append(@"\\");
                return VisualForm.Backslash;
            case <= 0x1F:
                // Caret notation, introduced by a backslash. Introducing with the caret instead
                // collides: U+001E is 0x1E + 0x40 = '^', so RS and a literal caret would spell
                // the same thing.
                builder.Append(@"\^").Append((char)(scalar.Value + 0x40));
                return VisualForm.Caret;
            case 0x7F:
                builder.Append(@"\^?");
                return VisualForm.CaretDelete;
            case <= 0xFFFF:
                AppendBmpHex(builder, (char)scalar.Value);
                return VisualForm.BmpHex;
            default:
                // \uXXXX reaches only the BMP, and 127 scalars in the encoded categories live
                // above it — U+110BD, the Egyptian hieroglyph format controls, the musical
                // symbol format characters. A speller that stops at \uXXXX is neither total nor
                // invertible on exactly the inputs an attacker would reach for.
                builder.Append(@"\U").Append(scalar.Value.ToString("X8", CultureInfo.InvariantCulture));
                return VisualForm.AstralHex;
        }
    }

    private static void AppendBmpHex(StringBuilder builder, char c)
        => builder.Append(@"\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));

    // Rune.DecodeFromUtf16 reports an unpaired surrogate as InvalidData and hands back the
    // replacement character, so the caller has to keep the original code unit itself.
    // string.EnumerateRunes() cannot be used here: it silently substitutes U+FFFD, which would
    // make the transform lossy on exactly the input that needs it most.
    internal static Rune DecodeAt(string value, int index, out int width, out bool isUnpairedSurrogate)
    {
        OperationStatus status = Rune.DecodeFromUtf16(
            value.AsSpan(index),
            out Rune scalar,
            out width);

        isUnpairedSurrogate = status != OperationStatus.Done;

        if (isUnpairedSurrogate)
        {
            width = 1;
        }

        return scalar;
    }

    // Uppercase only, and hand-rolled for that reason: uint.TryParse with HexNumber accepts
    // "00ad" as readily as "00AD", which would give U+00AD two spellings when AppendBmpHex only
    // ever emits the second. Injectivity is what EnsurePermitted's repair rests on, so a second
    // accepted spelling is a defect rather than leniency.
    private static bool TryReadHex(string text, int start, int length, out uint value)
    {
        value = 0;

        if (start + length > text.Length)
        {
            return false;
        }

        for (int i = start; i < start + length; i++)
        {
            uint digit = text[i] switch
            {
                >= '0' and <= '9' => (uint)(text[i] - '0'),
                >= 'A' and <= 'F' => (uint)(text[i] - 'A' + 10),
                _ => uint.MaxValue,
            };

            if (digit == uint.MaxValue)
            {
                return false;
            }

            value = (value << 4) | digit;
        }

        return true;
    }

    // Walks encoder output one token at a time, where a token is whatever the boundary members
    // on InertString must not cut through: one escape, one raw scalar, or one raw character.
    //
    // Deliberately here rather than on the currency type, beside AppendSpelling, because the
    // widths below are that method's output read backwards. A new spelling that lands there and
    // not here would make every boundary past it wrong, and the two are only obviously coupled
    // when they are adjacent. Internal, so it adds nothing to the capability surface: it reports
    // where the text can be divided, never what any of it decodes to.
    private static int NextToken(string encoded, int index, out VisualForm form)
    {
        char c = encoded[index];

        if (c != '\\')
        {
            form = VisualForm.None;

            // Encode never emits a raw unpaired surrogate -- it spells one as \uXXXX -- so a
            // high surrogate here is always the first half of a scalar and is never divisible.
            return char.IsHighSurrogate(c)
                && index + 1 < encoded.Length
                && char.IsLowSurrogate(encoded[index + 1])
                    ? 2
                    : 1;
        }

        int remaining = encoded.Length - index;

        // Widths are clamped rather than trusted. Encode cannot emit a truncated escape, but the
        // internal constructor asserts the invariant instead of establishing it, so a walk that
        // read past the end here would turn a malformed value into an IndexOutOfRangeException
        // at a boundary check rather than at the point it was built.
        switch (remaining > 1 ? encoded[index + 1] : '\0')
        {
            case '\\':
                form = VisualForm.Backslash;
                return Math.Min(2, remaining);
            case '^':
                form = remaining > 2 && encoded[index + 2] == '?'
                    ? VisualForm.CaretDelete
                    : VisualForm.Caret;
                return Math.Min(3, remaining);
            case 'u':
                form = VisualForm.BmpHex;

                // Composition encodes each fragment on its own, so a surrogate pair split across
                // two of them arrives as two \uXXXX escapes that together spell one astral
                // scalar. Cutting between them would leave a lone surrogate in the text this
                // decodes to, which is the atomicity the escaper this replaces guaranteed.
                return IsEscapedSurrogatePair(encoded, index) ? 12 : Math.Min(6, remaining);
            case 'U':
                form = VisualForm.AstralHex;
                return Math.Min(10, remaining);
            default:
                form = VisualForm.None;
                return 1;
        }
    }

    private static bool IsEscapedSurrogatePair(string encoded, int index)
        => index + 12 <= encoded.Length
            && encoded[index + 6] == '\\'
            && encoded[index + 7] == 'u'
            && TryReadHex(encoded, index + 2, 4, out uint high)
            && TryReadHex(encoded, index + 8, 4, out uint low)
            && char.IsHighSurrogate((char)high)
            && char.IsLowSurrogate((char)low);

    /// <summary>
    /// The largest window inside <paramref name="start"/>..<paramref name="end"/> whose bounds
    /// both fall between tokens, reporting the spellings it contains.
    /// </summary>
    /// <remarks>
    /// Both bounds move inward — the start forward, the end back — so the window is always a
    /// subset of the one asked for. Moving either bound outward would hand back text the caller
    /// did not ask for, which is the one direction it has no way to check.
    ///
    /// Total in both bounds, which is what lets a caller pass a range straight through without
    /// clamping it first: a bound below zero or past the end is read as the nearest end of the
    /// text, and an end below the start gives an empty window, because the end walks from the
    /// start and only ever advances.
    /// </remarks>
    internal static (int Start, int End, VisualForm Forms) WindowWithin(string encoded, int start, int end)
    {
        int from = 0;

        while (from < start && from < encoded.Length)
        {
            from += NextToken(encoded, from, out _);
        }

        VisualForm forms = VisualForm.None;
        int to = from;

        while (to < end && to < encoded.Length)
        {
            int width = NextToken(encoded, to, out VisualForm form);

            if (to + width > end)
            {
                break;
            }

            forms |= form;
            to += width;
        }

        return (from, to, forms);
    }
}
