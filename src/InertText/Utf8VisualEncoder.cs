using System.Buffers;
using System.Globalization;
using System.Text;

namespace InertText.Encoding;

/// <summary>
/// Contains classified UTF-8 as an inert, valid UTF-8 spelling.
/// </summary>
public static class Utf8VisualEncoder
{
    private static ReadOnlySpan<byte> Hex => "0123456789ABCDEF"u8;

    /// <summary>
    /// Scans exact classified bytes, applies the selected policy, and returns contained UTF-8.
    /// </summary>
    public static InertUtf8String Encode(
        TextPolicy policy,
        Utf8RangeReader value)
    {
        ReadOnlySpan<byte> source = value.Source;
        ScalarPolicy permits = ScalarPolicies.For(policy);
        Scan scan = Measure(permits, source);
        if (scan.RetainsSource)
        {
            return new(
                value.RetainSource(),
                VisualForm.None,
                TextConcern.None);
        }

        int encodedLength = scan.EncodedUtf8Length;
        byte[] encoded = GC.AllocateUninitializedArray<byte>(
            encodedLength);
        int written = 0;
        for (int offset = 0; offset < source.Length;)
        {
            OperationStatus status =
                Rune.DecodeFromUtf8(
                    source[offset..],
                    out Rune scalar,
                    out int width);
            if (status != OperationStatus.Done)
            {
                WriteBmpHex(
                    encoded.AsSpan(written, 6),
                    ScalarSpelling.SurrogateEscape(source[offset]));
                written += 6;
                offset++;
                continue;
            }

            if (scalar.Value != '\\' && permits(scalar))
            {
                source.Slice(offset, width)
                    .CopyTo(encoded.AsSpan(written, width));
                written += width;
            }
            else
            {
                written += WriteSpelling(
                    encoded.AsSpan(written),
                    scalar);
            }
            offset += width;
        }

        return new(
            OpaqueByteRange.FromOwned(encoded),
            scan.Forms,
            scan.Concerns);
    }

    /// <summary>
    /// Measures the UTF-16 length of the spelling <see cref="Encode"/> produces, which is the
    /// <see cref="InertString.Length"/> of its <see cref="InertUtf8String.ToInertString"/>,
    /// without constructing it.
    /// </summary>
    /// <remarks>
    /// Lets a producer that retains bytes charge a budget counted in contained UTF-16
    /// characters, and defer containment until a sink asks for it.
    /// </remarks>
    public static int MeasureInertStringLength(
        TextPolicy policy,
        Utf8RangeReader value) =>
        Measure(ScalarPolicies.For(policy), value.Source)
            .InertStringLength;

    private static Scan Measure(
        ScalarPolicy permits,
        ReadOnlySpan<byte> source)
    {
        VisualForm forms = VisualForm.None;
        TextConcern concerns = TextConcern.None;
        int encodedUtf8 = 0;
        int encodedUtf16 = 0;
        int sourceUtf16 = 0;

        for (int offset = 0; offset < source.Length;)
        {
            OperationStatus status =
                Rune.DecodeFromUtf8(
                    source[offset..],
                    out Rune scalar,
                    out int width);
            if (status != OperationStatus.Done)
            {
                // Each ill-formed byte is spelled on its own, as its surrogate escape.
                encodedUtf8 = checked(encodedUtf8 + 6);
                encodedUtf16 = checked(encodedUtf16 + 6);
                forms |= VisualForm.BmpHex;
                concerns |= TextConcern.Surrogate;
                offset++;
                continue;
            }

            sourceUtf16 = checked(
                sourceUtf16 + scalar.Utf16SequenceLength);
            bool encode = scalar.Value == '\\' || !permits(scalar);
            if (!encode)
            {
                encodedUtf8 = checked(encodedUtf8 + width);
                encodedUtf16 = checked(
                    encodedUtf16 + scalar.Utf16SequenceLength);
            }
            else
            {
                (int length, VisualForm form) =
                    ScalarSpelling.For(scalar.Value);
                encodedUtf8 = checked(encodedUtf8 + length);
                encodedUtf16 = checked(encodedUtf16 + length);
                forms |= form;
                if (scalar.Value != '\\')
                {
                    concerns |= ScalarPolicies.ConcernFor(
                        Rune.GetUnicodeCategory(scalar));
                }
            }
            offset += width;
        }

        bool retainsSource =
            forms == VisualForm.None
            || (forms == VisualForm.Backslash
                && CanRetainLiteralBackslashes(source));
        return new(
            retainsSource,
            encodedUtf8,
            retainsSource ? sourceUtf16 : encodedUtf16,
            forms,
            concerns);
    }

    private readonly record struct Scan(
        bool RetainsSource,
        int EncodedUtf8Length,
        int InertStringLength,
        VisualForm Forms,
        TextConcern Concerns);

    private static int WriteSpelling(
        Span<byte> destination,
        Rune scalar)
    {
        switch (scalar.Value)
        {
            case '\\':
                destination[0] = (byte)'\\';
                destination[1] = (byte)'\\';
                return 2;
            case <= 0x1F:
                destination[0] = (byte)'\\';
                destination[1] = (byte)'^';
                destination[2] =
                    (byte)(scalar.Value + 0x40);
                return 3;
            case 0x7F:
                "\\^?"u8.CopyTo(destination);
                return 3;
            case <= 0xFFFF:
                WriteBmpHex(destination, scalar.Value);
                return 6;
            default:
                destination[0] = (byte)'\\';
                destination[1] = (byte)'U';
                WriteHex(
                    destination[2..10],
                    (uint)scalar.Value);
                return 10;
        }
    }

    private static void WriteBmpHex(
        Span<byte> destination,
        int value)
    {
        destination[0] = (byte)'\\';
        destination[1] = (byte)'u';
        WriteHex(destination[2..6], (uint)value);
    }

    private static void WriteHex(
        Span<byte> destination,
        uint value)
    {
        for (int i = destination.Length - 1; i >= 0; i--)
        {
            destination[i] = Hex[(int)(value & 0xF)];
            value >>= 4;
        }
    }

    private static bool CanRetainLiteralBackslashes(
        ReadOnlySpan<byte> value)
    {
        for (int index = value.IndexOf((byte)'\\');
            index >= 0;)
        {
            if (IntroducesSpelling(value[index..]))
                return false;

            int next =
                value[(index + 1)..].IndexOf((byte)'\\');
            index = next < 0
                ? -1
                : index + 1 + next;
        }

        return true;
    }

    private static bool IntroducesSpelling(
        ReadOnlySpan<byte> value)
    {
        if (value.Length < 2)
            return false;

        return value[1] switch
        {
            (byte)'\\' => true,
            (byte)'^' =>
                value.Length >= 3
                && (value[2] == (byte)'?'
                    || value[2] is >= (byte)'@' and <= (byte)'_'),
            (byte)'u' =>
                TryReadHex(value[2..], 4, out uint bmp)
                && bmp > 0x1F
                && bmp is not (0x7F or '\\'),
            (byte)'U' =>
                TryReadHex(value[2..], 8, out uint astral)
                && astral > 0xFFFF
                && Rune.IsValid((int)astral),
            _ => false,
        };
    }

    private static bool TryReadHex(
        ReadOnlySpan<byte> source,
        int length,
        out uint value)
    {
        value = 0;
        if (source.Length < length)
            return false;

        for (int i = 0; i < length; i++)
        {
            uint digit = source[i] switch
            {
                >= (byte)'0' and <= (byte)'9' =>
                    (uint)(source[i] - (byte)'0'),
                >= (byte)'A' and <= (byte)'F' =>
                    (uint)(source[i] - (byte)'A' + 10),
                _ => uint.MaxValue,
            };
            if (digit == uint.MaxValue)
                return false;
            value = (value << 4) | digit;
        }

        return true;
    }
}
