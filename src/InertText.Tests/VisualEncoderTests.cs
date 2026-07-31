using System.Buffers;
using System.Globalization;
using System.Text;

namespace InertText.Tests;

/// <summary>
/// Gates the properties the encoding asserts: total, inert, lossless, and invertible.
/// </summary>
/// <remarks>
/// Invertibility is the one that cannot be checked by inspection. A caret-introduced spelling
/// looks correct and passes casual review, and only fails once a decoder exists and the sweep
/// below runs it over the alphabet that can collide.
/// </remarks>
public class VisualEncoderTests
{
    [Fact]
    public void RoundTrip_EveryScalar_RecoversTheOriginal()
    {
        // The whole plane, not just the BMP: 127 scalars in the encoded categories live above
        // it, and a sweep that stops at U+FFFF is exactly why that gap survived review once.
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF)
            {
                continue;
            }

            string original = new Rune(cp).ToString();
            string encoded = VisualEncoder.Encode(original, TextPolicy.Field);

            Assert.True(
                VisualEncoder.TryDecode(encoded, out string? decoded),
                $"U+{cp:X4} encoded as '{encoded}' and did not decode");
            Assert.Equal(original, decoded);
        }
    }

    [Fact]
    public void RoundTrip_UnpairedSurrogates_RecoverExactly()
    {
        for (int cp = 0xD800; cp <= 0xDFFF; cp++)
        {
            string original = ((char)cp).ToString();
            string encoded = VisualEncoder.Encode(original, TextPolicy.Field);

            Assert.NotEqual(original, encoded);
            Assert.True(VisualEncoder.TryDecode(encoded, out string? decoded), encoded);
            Assert.Equal(original, decoded);
        }
    }

    [Fact]
    public void Encode_IsInjective_OverTheCollidingAlphabet()
    {
        // Every character that can appear in a spelling, so a collision between two different
        // inputs has somewhere to show up. 'U' is in here because the astral form introduced
        // with it must not be confusable with the BMP form introduced with 'u'.
        char[] alphabet = ['\\', '^', 'u', 'U', '?', '@', '[', '0', '9', 'A', 'F',
                           '\u001B', '\u001E', '\u007F', '\u202E', '\uFEFF', '\u2028'];

        Dictionary<string, string> seen = new(StringComparer.Ordinal);

        foreach (string original in Strings(alphabet, 3))
        {
            string encoded = VisualEncoder.Encode(original, TextPolicy.Field);

            Assert.True(VisualEncoder.TryDecode(encoded, out string? decoded), encoded);
            Assert.Equal(original, decoded);

            if (seen.TryGetValue(encoded, out string? collision))
            {
                Assert.Fail(
                    $"'{Describe(collision)}' and '{Describe(original)}' both encode to '{encoded}'");
            }

            seen[encoded] = original;
        }
    }

    [Fact]
    public void Encode_AstralScalars_UseTheEightDigitForm()
    {
        // U+13430 EGYPTIAN HIEROGLYPH VERTICAL JOINER is Cf and lives above the BMP. \uXXXX
        // cannot express it, so an encoder built on four hex digits returns it untouched while
        // reporting success.
        Assert.Equal(@"A\U00013430B", VisualEncoder.Encode("A\U00013430B", TextPolicy.Field));
        Assert.False(VisualEncoder.IsPermitted("A\U00013430B", TextPolicy.Field, out var violation));
        Assert.Equal(0x13430, violation!.Value.Scalar.Value);
        Assert.Equal(UnicodeCategory.Format, violation.Value.Category);
    }

    [Fact]
    public void Encode_EveryEncodedScalarAboveTheBmp_IsEncoded()
    {
        int encodedCount = 0;

        for (int cp = 0x10000; cp <= 0x10FFFF; cp++)
        {
            Rune scalar = new(cp);

            if (!TextPolicy.IsNonGraphic(scalar))
            {
                continue;
            }

            encodedCount++;
            string original = scalar.ToString();
            Assert.NotEqual(original, VisualEncoder.Encode(original, TextPolicy.Field));
            Assert.False(VisualEncoder.IsPermitted(original, TextPolicy.Field));
        }

        // Pinned so a narrowing of the category rule fails here rather than quietly shrinking.
        Assert.Equal(127, encodedCount);
    }

    [Fact]
    public void Encode_PairedSurrogates_AreNotHazards()
    {
        // A loop over char calling GetUnicodeCategory per unit encodes every emoji, because
        // both halves of a pair are Cs. Only an unpaired surrogate is a hazard.
        foreach (string text in new[] { "\U0001F600", "\U0001F468\u200D\U0001F469", "\U00020BB7" })
        {
            string encoded = VisualEncoder.Encode(text, TextPolicy.Field);
            Assert.DoesNotContain(@"\uD8", encoded, StringComparison.Ordinal);
            Assert.True(VisualEncoder.TryDecode(encoded, out string? decoded));
            Assert.Equal(text, decoded);
        }

        Assert.Equal("\U0001F600", VisualEncoder.Encode("\U0001F600", TextPolicy.Field));
    }

    [Fact]
    public void CategoryRule_CoversTheRustcAndBidiControlSets_ByName()
    {
        // Named individually rather than looked up by category, so a future narrowing of the
        // rule fails here instead of silently stopping at nine, or at zero.
        int[] rustc =
        [
            0x202A, 0x202B, 0x202C, 0x202D, 0x202E,
            0x2066, 0x2067, 0x2068, 0x2069,
        ];
        int[] bidiControlOnly = [0x200E, 0x200F, 0x061C];

        foreach (int cp in rustc.Concat(bidiControlOnly))
        {
            Rune scalar = new(cp);
            Assert.True(TextPolicy.IsNonGraphic(scalar), $"U+{cp:X4}");
            Assert.False(VisualEncoder.IsPermitted(scalar.ToString(), TextPolicy.Field));
        }

        Assert.Equal(9, rustc.Length);
        Assert.Equal(12, rustc.Length + bidiControlOnly.Length);
    }

    [Fact]
    public void CategoryRule_CoversTheInvisibleFormatCharacters()
    {
        // Past bidi, the encoded set is all of Cf. These are the ones ApiOutputFormatter
        // deliberately excludes; the divergence is recorded in the spec and resolved upward.
        foreach (int cp in new[] { 0x200C, 0x200D, 0x2060, 0x00AD, 0xFEFF })
        {
            Assert.True(TextPolicy.IsNonGraphic(new Rune(cp)), $"U+{cp:X4}");
        }
    }

    [Fact]
    public void Spelling_MatchesCaretNotation()
    {
        Assert.Equal(@"\^[", VisualEncoder.Encode("\u001B", TextPolicy.Field));
        Assert.Equal(@"\^@", VisualEncoder.Encode("\u0000", TextPolicy.Field));
        Assert.Equal(@"\^?", VisualEncoder.Encode("\u007F", TextPolicy.Field));
        Assert.Equal(@"\\", VisualEncoder.Encode("\\", TextPolicy.Field));
        Assert.Equal(@"\u202E", VisualEncoder.Encode("\u202E", TextPolicy.Field));

        // The collision the backslash introducer exists to prevent: U+001E is 0x1E + 0x40 = '^',
        // so a caret-introduced spelling gives RS and a literal caret the same output.
        Assert.NotEqual(
            VisualEncoder.Encode("\u001E", TextPolicy.Field),
            VisualEncoder.Encode("^", TextPolicy.Field));
    }

    [Fact]
    public void Encode_OrdinaryText_IsUnchanged()
    {
        // Encoding everything would be safe and useless. Ordinary text has to survive intact,
        // including scripts and marks that are not ASCII.
        foreach (string text in new[]
        {
            "https://pkgs.dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json",
            "Ünïcödé", "日本語", "emoji \U0001F600", "a\u0301",
        })
        {
            Assert.Equal(text, VisualEncoder.Encode(text, TextPolicy.Field));
            Assert.True(VisualEncoder.IsPermitted(text, TextPolicy.Field));
        }
    }

    [Fact]
    public void IsPermitted_TreatsTheBackslashAsPermitted()
    {
        // A Windows path is the case that makes this matter: it is full of backslashes, none of
        // them is a hazard, and a check derived from "would Encode change this" rejects them all.
        const string path = @"C:\Users\rich\.nuget\packages";

        Assert.True(VisualEncoder.IsPermitted(path, TextPolicy.Field));
        Assert.NotEqual(path, VisualEncoder.Encode(path, TextPolicy.Field));
    }

    [Fact]
    public void Policy_IsTheCallersToChoose()
    {
        const string multiline = "line one\nline two\tindented";

        Assert.True(VisualEncoder.IsPermitted(multiline, TextPolicy.Prose));
        Assert.Equal(multiline, VisualEncoder.Encode(multiline, TextPolicy.Prose));

        Assert.False(VisualEncoder.IsPermitted(multiline, TextPolicy.Field));
        Assert.Equal(@"line one\^Jline two\^Iindented", VisualEncoder.Encode(multiline, TextPolicy.Field));

        // No sink may exempt a bidi control, whatever else it exempts.
        Assert.False(VisualEncoder.IsPermitted("\u202E", TextPolicy.Prose));
    }

    [Fact]
    public void Policy_AllowShaped_CatchesAHomoglyphThatNoCategoryRuleCan()
    {
        // The attack the whole split exists for. Cyrillic е U+0435 and Latin e U+0065 are the
        // same glyph, both category Ll, and neither is a hazard — so a deny-shaped policy
        // renders the typosquat raw and the substitution stays invisible.
        const string hijacked = "N\u0435wtonsoft.Json";

        Assert.True(VisualEncoder.IsPermitted(hijacked, TextPolicy.Field));
        Assert.Equal(hijacked, VisualEncoder.Encode(hijacked, TextPolicy.Field));

        // An allow list over the published grammar catches it, and the speller — which never
        // learns why — spells it without needing a hazard set of its own.
        static bool PackageId(Rune scalar)
            => scalar.Value is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z')
                or (>= '0' and <= '9') or '.' or '-' or '_';

        Assert.False(VisualEncoder.IsPermitted(hijacked, PackageId, out var violation));
        Assert.Equal(0x0435, violation!.Value.Scalar.Value);
        Assert.Equal(@"N\u0435wtonsoft.Json", VisualEncoder.Encode(hijacked, PackageId));
    }

    [Fact]
    public void Legend_NamesEveryFormTheOutputContains()
    {
        string encoded = VisualEncoder.Encode(
            "a\u001B\u007F\u202E\U00013430\\b",
            TextPolicy.Field,
            out VisualForm forms);

        IReadOnlyList<string> legend = VisualEncoder.DescribeLegend(forms);

        Assert.Equal(5, legend.Count);
        foreach (string introducer in new[] { @"\^X", @"\^?", @"\uXXXX", @"\UXXXXXXXX", @"\\" })
        {
            Assert.Contains(legend, line => line.StartsWith(introducer, StringComparison.Ordinal));
        }

        // The legend names forms, never values.
        Assert.DoesNotContain(legend, line => line.Contains("13430", StringComparison.Ordinal));
        Assert.Contains(@"\U00013430", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Legend_IsEmptyWhenNothingWasEncoded()
    {
        VisualEncoder.Encode("ordinary text", TextPolicy.Field, out VisualForm forms);

        Assert.Equal(VisualForm.None, forms);
        Assert.Empty(VisualEncoder.DescribeLegend(forms));
    }

    [Fact]
    public void TryDecode_MalformedInput_IsRejected()
    {
        foreach (string malformed in new[]
        {
            @"\", @"\q", @"\^", @"\^!", @"\u12", @"\uZZZZ",
            @"\U0001", @"\UZZZZZZZZ",
            @"\U0000202E",  // an astral form naming a BMP scalar would be a second spelling
            @"\U00110000",  // past the last scalar
        })
        {
            Assert.False(VisualEncoder.TryDecode(malformed, out string? value), malformed);
            Assert.Null(value);
        }
    }

    private static string Describe(string value)
        => string.Join(" ", value.Select(c => $"U+{(int)c:X4}"));

    private static IEnumerable<string> Strings(char[] alphabet, int maxLength)
    {
        List<string> current = [string.Empty];

        for (int length = 1; length <= maxLength; length++)
        {
            List<string> next = [];

            foreach (string prefix in current)
            {
                foreach (char c in alphabet)
                {
                    string s = prefix + c;
                    next.Add(s);
                    yield return s;
                }
            }

            current = next;
        }
    }
}
