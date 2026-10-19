using System.Buffers;
using System.Text;

using InertText.Encoding;

namespace InertText.Tests;

/// <summary>
/// Gates that <see cref="Utf8VisualEncoder"/> and <see cref="VisualEncoder"/> spell the same text
/// the same way, with the same evidence.
/// </summary>
/// <remarks>
/// The two encoders keep separate writers, so parity is checked on output rather than assumed.
/// Ill-formed UTF-8 is compared against its surrogate-escaped UTF-16 string, which is the
/// documented meaning of its spelling.
/// </remarks>
public sealed class SpellingParityTests
{
    private static readonly TextPolicy[] Policies = Enum.GetValues<TextPolicy>();

    [Fact]
    public void EveryScalar_HasTheSameSpellingAndEvidenceInBothEncoders()
    {
        List<string> mismatches = [];
        Span<char> utf16 = stackalloc char[2];
        Span<byte> utf8 = stackalloc byte[4];

        for (int value = 0; value <= 0x10FFFF; value++)
        {
            if (value is >= 0xD800 and <= 0xDFFF)
                continue;

            var scalar = new Rune(value);
            int chars = scalar.EncodeToUtf16(utf16);
            int bytes = scalar.EncodeToUtf8(utf8);
            foreach (TextPolicy policy in Policies)
            {
                Compare(
                    policy,
                    utf16[..chars],
                    utf8[..bytes],
                    $"U+{value:X4}",
                    mismatches);
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void EveryIllFormedByte_IsSpelledAsItsSurrogateEscape()
    {
        List<string> mismatches = [];
        for (int value = 0x80; value <= 0xFF; value++)
        {
            byte[] source = [(byte)value];
            Assert.NotEqual(
                OperationStatus.Done,
                Rune.DecodeFromUtf8(source, out _, out _));
            char[] escaped = [(char)(0xDC00 + value)];
            foreach (TextPolicy policy in Policies)
            {
                Compare(
                    policy,
                    escaped,
                    source,
                    $"0x{value:X2}",
                    mismatches);
            }
        }

        Assert.Empty(mismatches);
    }

    [Theory]
    [InlineData(new byte[] { 0xE2, 0x82 })]                   // truncated three-byte sequence
    [InlineData(new byte[] { 0xF0, 0x9F, 0x98 })]             // truncated four-byte sequence
    [InlineData(new byte[] { 0xC0, 0xAF })]                   // overlong '/'
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]             // encoded high surrogate
    [InlineData(new byte[] { 0xED, 0xB2, 0x80 })]             // encoded U+DC80
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 })]       // above U+10FFFF
    [InlineData(new byte[] { 0x61, 0xE2, 0x41, 0x5C, 0x80 })] // 'a', lead, 'A', '\', continuation
    public void IllFormedSequences_EscapeEachByte(byte[] source)
    {
        List<string> mismatches = [];
        foreach (TextPolicy policy in Policies)
        {
            Compare(
                policy,
                SurrogateEscape(source),
                source,
                Convert.ToHexString(source),
                mismatches);
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void EscapeLikeText_RetainsOrDoublesBackslashesTheSameWay()
    {
        List<string> mismatches = [];
        List<string> sources = [];
        for (char c = '\0'; c < (char)0x80; c++)
        {
            sources.Add($"\\{c}");
            sources.Add($"\\^{c}");
        }

        for (int value = 0; value <= 0xFFFF; value++)
            sources.Add($"x\\u{value:X4}");

        sources.Add(@"\\u0041 \\\u202E \u202e");
        foreach (int value in (int[])
            [0, 0x1F, 0x7F, 0x5C, 0xFFFF, 0x10000, 0x1D173, 0xE0001, 0x10FFFF, 0x110000, 0xFFFFFFF])
        {
            sources.Add($"\\U{value:X8}");
        }

        foreach (string source in sources)
        {
            foreach (TextPolicy policy in Policies)
            {
                Compare(
                    policy,
                    source,
                    System.Text.Encoding.UTF8.GetBytes(source),
                    source,
                    mismatches);
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void GeneratedByteStrings_HaveTheSameSpellingAndEvidence()
    {
        // Weighted toward the bytes that drive spelling decisions: the escape introducers,
        // hex digits, controls, lead and continuation bytes.
        byte[] alphabet =
            [.. "\\\\\\uU^?@_09AFaf -"u8, 0x00, 0x09, 0x0A, 0x1B, 0x7F,
             0x80, 0xBF, 0xC2, 0xAD, 0xE2, 0x80, 0xAE, 0xED, 0xA0, 0xF0, 0x9F, 0xF4, 0xFF];
        var random = new Random(10169);
        List<string> mismatches = [];
        byte[] buffer = new byte[12];

        for (int i = 0; i < 50_000; i++)
        {
            Span<byte> source = buffer.AsSpan(0, random.Next(1, buffer.Length + 1));
            foreach (ref byte b in source)
            {
                b = random.Next(4) == 0
                    ? (byte)random.Next(256)
                    : alphabet[random.Next(alphabet.Length)];
            }

            foreach (TextPolicy policy in Policies)
            {
                Compare(
                    policy,
                    SurrogateEscape(source),
                    source,
                    Convert.ToHexString(source),
                    mismatches);
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Composition_KeepsAByteEscapeBesideASurrogateEscapeVisibleUntilRespelled()
    {
        // A UTF-8 value projects to a surrogate-escaped UTF-16 value, so composition treats it as
        // UTF-16. Join keeps the escapes distinct; decoding reads the adjacent pair as the one
        // scalar that UTF-16 text denotes, as for any two separately encoded fragments, so a
        // stricter re-spelling shows that scalar and recomputes the evidence.
        InertString high = new(TextPolicy.Field, "\uD834");
        InertString illFormed = new InertUtf8String(
                TextPolicy.Field,
                Utf8RangeClassification.Read(OpaqueByteRange.Copy([0x80])))
            .ToInertString();

        InertString joined = InertString.Join("", TextPolicy.Field, [high, illFormed]);

        Assert.Equal(@"\uD834\uDC80", joined.ToString());
        Assert.Equal(TextConcern.Surrogate, joined.Concerns);
        Assert.True(VisualEncoder.TryDecode(joined.ToString(), out string? decoded));
        Assert.Equal("\uD834\uDC80", decoded);

        InertString prose = InertString.Join(
            "",
            TextPolicy.Prose,
            [new InertString(TextPolicy.Prose, "\n\uD834"), illFormed]);
        InertString field = prose.EnsurePermitted(TextPolicy.Field);

        Assert.Equal("\\^J\U0001D080", field.ToString());
        Assert.Equal(VisualForm.Caret, field.Forms);
        Assert.Equal(TextConcern.Control, field.Concerns);
    }

    private static void Compare(
        TextPolicy policy,
        ReadOnlySpan<char> utf16Source,
        ReadOnlySpan<byte> utf8Source,
        string label,
        List<string> mismatches)
    {
        if (mismatches.Count >= 20)
            return;

        InertString utf16 = VisualEncoder.Encode(policy, utf16Source);
        InertUtf8String utf8 = Utf8VisualEncoder.Encode(
            policy,
            Utf8RangeClassification.Read(OpaqueByteRange.Copy(utf8Source)));
        string projected = utf8.ToInertString().ToString();

        if (projected != utf16.ToString()
            || utf8.Forms != utf16.Forms
            || utf8.Concerns != utf16.Concerns)
        {
            mismatches.Add(
                $"{policy} {label}: UTF-8 '{projected}' {utf8.Forms}/{utf8.Concerns}, "
                + $"UTF-16 '{utf16}' {utf16.Forms}/{utf16.Concerns}");
        }
    }

    // The surrogate-escape decoding of a byte string: well-formed scalars decode normally, and
    // each byte outside a well-formed sequence becomes U+DC00 plus the byte.
    private static string SurrogateEscape(ReadOnlySpan<byte> source)
    {
        var builder = new StringBuilder();
        for (int offset = 0; offset < source.Length;)
        {
            if (Rune.DecodeFromUtf8(source[offset..], out Rune scalar, out int width)
                == OperationStatus.Done)
            {
                builder.Append(scalar.ToString());
                offset += width;
            }
            else
            {
                builder.Append((char)(0xDC00 + source[offset]));
                offset++;
            }
        }

        return builder.ToString();
    }
}
