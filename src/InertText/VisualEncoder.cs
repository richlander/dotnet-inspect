using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace InertText.Encoder;

/// <summary>
/// The transform that makes text inert, and the decoder that recovers what it was given.
/// </summary>
/// <remarks>
/// Deliberately in its own namespace, because that is what makes the guarantee auditable.
/// <see cref="InertString"/> can be carried, composed and printed without ever naming this
/// type: text enters through its constructor, which calls in here, and leaves through
/// <c>ToString</c> already spelled. So a file that imports <c>InertText</c> and not
/// <c>InertText.Encoder</c> has no way to recover the original text of any value it handles,
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
    /// Encodes <paramref name="value"/> under <paramref name="permits"/>, visually spelling every
    /// scalar the policy refuses.
    /// </summary>
    /// <remarks>
    /// Returns the currency type rather than a <see cref="string"/> and a form set, because the
    /// two are one result: <see cref="InertString.Forms"/> is exactly what an <c>out</c>
    /// parameter would have reported, and splitting them lets a caller keep the text while
    /// dropping the record of what was done to it — which is the confusion this library exists to
    /// remove. <see cref="InertString"/>'s constructor forwards here, so the two spellings cannot
    /// diverge.
    ///
    /// A literal backslash is always rewritten, whatever <paramref name="permits"/> says, because
    /// it introduces every other spelling and the transform would not otherwise invert. An
    /// unpaired surrogate is likewise always encoded: it is not a scalar at all, so no policy is
    /// consulted for it.
    /// </remarks>
    /// <param name="value">The text to encode.</param>
    /// <param name="permits">The per-sink policy deciding what may pass through.</param>
    /// <returns>
    /// The encoded text as an <see cref="InertString"/>, which carries the spellings that were
    /// emitted. Text that needed no encoding is returned as the original instance.
    /// </returns>
    public static InertString Encode(string value, ScalarPolicy permits)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(permits);

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

                    builder.Append((char)bmp);
                    i += 4;
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

        // An unpaired surrogate decoded from a \uXXXX arm is deliberately accepted. Cs is one of
        // the encoded categories, so Encode emits exactly this form for an unpaired surrogate,
        // and rejecting it here would make the encoder non-invertible on the one input class
        // that cannot be represented any other way.
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

    private static bool TryReadHex(string text, int start, int length, out uint value)
    {
        value = 0;

        return start + length <= text.Length
            && uint.TryParse(
                text.AsSpan(start, length),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out value);
    }
}
