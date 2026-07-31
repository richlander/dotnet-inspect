using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace InertText;

/// <summary>
/// The spellings <see cref="InertString"/> can emit.
/// </summary>
/// <remarks>
/// Reported so a caller can print a legend derived from what was actually emitted rather than
/// from a second copy of the table. A legend written independently drifts; one projected from
/// the encoder cannot.
/// </remarks>
[Flags]
public enum VisualForm
{
    /// <summary>Nothing was encoded.</summary>
    None = 0,

    /// <summary>Caret notation for a C0 control, as in <c>\^[</c> for <c>ESC</c>.</summary>
    Caret = 1 << 0,

    /// <summary>Caret notation for <c>DEL</c>, spelled <c>\^?</c>.</summary>
    CaretDelete = 1 << 1,

    /// <summary>A scalar in the BMP, spelled <c>\uXXXX</c>.</summary>
    BmpHex = 1 << 2,

    /// <summary>A scalar above the BMP, spelled <c>\UXXXXXXXX</c>.</summary>
    AstralHex = 1 << 3,

    /// <summary>A literal backslash, doubled so the transform stays invertible.</summary>
    Backslash = 1 << 4,
}

// The speller half of InertString: the transform that makes text inert, the predicate that
// asks whether it already is, and the decoder that proves the transform is invertible. Split
// into its own file for readability only -- the type's documentation lives on the other part.
public readonly partial struct InertString
{
    /// <summary>
    /// Encodes <paramref name="value"/> under <paramref name="permits"/>, yielding a value that
    /// can be carried to a sink.
    /// </summary>
    /// <remarks>
    /// The only way into the type. There is deliberately no public form that hands back a bare
    /// <see cref="string"/>: that would return treated text with its provenance stripped, which
    /// is the confusion this type exists to remove.
    /// </remarks>
    public static InertString Encode(string value, ScalarPolicy permits)
    {
        string encoded = EncodeCore(value, permits, out VisualForm forms);
        return new InertString(encoded, forms);
    }

    /// <summary>
    /// Returns <paramref name="value"/> with every scalar <paramref name="permits"/> refuses
    /// visually encoded, reporting which spellings were used.
    /// </summary>
    /// <remarks>
    /// A literal backslash is always rewritten, whatever <paramref name="permits"/> says, because
    /// it introduces every other spelling and the transform would not otherwise invert. An
    /// unpaired surrogate is likewise always encoded: it is not a scalar at all, so no policy is
    /// consulted for it.
    /// </remarks>
    /// <param name="value">The text to encode.</param>
    /// <param name="permits">The per-sink policy deciding what may pass through.</param>
    /// <param name="formsUsed">The spellings actually emitted, for a caller-written legend.</param>
    internal static string EncodeCore(string value, ScalarPolicy permits, out VisualForm formsUsed)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(permits);

        formsUsed = VisualForm.None;
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

        return builder?.ToString() ?? value;
    }

    /// <summary>
    /// Reports whether every scalar in <paramref name="value"/> is permitted as it is.
    /// </summary>
    /// <remarks>
    /// The early-fail check, for callers that would rather reject text than display an encoded
    /// rendering of it. This is deliberately not "would <see cref="Encode"/> change it": a
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
            Rune scalar = DecodeAt(value, i, out int width, out bool isUnpairedSurrogate);

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
    /// Recovers the original text from <see cref="Encode"/>'s output.
    /// </summary>
    /// <remarks>
    /// Exists because invertibility is an asserted property, and an encoder without a decoder
    /// cannot demonstrate it: a caret-introduced spelling survives casual inspection and only
    /// fails a round-trip that a decoder makes possible.
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
    private static Rune DecodeAt(string value, int index, out int width, out bool isUnpairedSurrogate)
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
