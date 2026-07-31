using System.Buffers;
using System.Globalization;
using System.Text;

using InertText.Encoder;

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
            string encoded = new InertString(TextPolicy.Field, original).ToString();

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
            string encoded = new InertString(TextPolicy.Field, original).ToString();

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
            string encoded = new InertString(TextPolicy.Field, original).ToString();

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
        Assert.Equal(@"A\U00013430B", new InertString(TextPolicy.Field, "A\U00013430B").ToString());
        Assert.False(InertString.IsPermitted(TextPolicy.Field, "A\U00013430B", out var violation));
        Assert.Equal(0x13430, violation!.Value.Scalar);
        Assert.Equal(UnicodeCategory.Format, violation.Value.Category);
    }

    [Fact]
    public void Encode_EveryEncodedScalarAboveTheBmp_IsEncoded()
    {
        int encodedCount = 0;

        for (int cp = 0x10000; cp <= 0x10FFFF; cp++)
        {
            Rune scalar = new(cp);

            if (!ScalarPolicies.IsNonGraphic(scalar))
            {
                continue;
            }

            encodedCount++;
            string original = scalar.ToString();
            Assert.NotEqual(original, new InertString(TextPolicy.Field, original).ToString());
            Assert.False(InertString.IsPermitted(TextPolicy.Field, original));
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
            string encoded = new InertString(TextPolicy.Field, text).ToString();
            Assert.DoesNotContain(@"\uD8", encoded, StringComparison.Ordinal);
            Assert.True(VisualEncoder.TryDecode(encoded, out string? decoded));
            Assert.Equal(text, decoded);
        }

        Assert.Equal("\U0001F600", new InertString(TextPolicy.Field, "\U0001F600").ToString());
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
            Assert.True(ScalarPolicies.IsNonGraphic(scalar), $"U+{cp:X4}");
            Assert.False(InertString.IsPermitted(TextPolicy.Field, scalar.ToString()));
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
            Assert.True(ScalarPolicies.IsNonGraphic(new Rune(cp)), $"U+{cp:X4}");
        }
    }

    [Fact]
    public void Spelling_MatchesCaretNotation()
    {
        Assert.Equal(@"\^[", new InertString(TextPolicy.Field, "\u001B").ToString());
        Assert.Equal(@"\^@", new InertString(TextPolicy.Field, "\u0000").ToString());
        Assert.Equal(@"\^?", new InertString(TextPolicy.Field, "\u007F").ToString());
        Assert.Equal(@"\\", new InertString(TextPolicy.Field, "\\").ToString());
        Assert.Equal(@"\u202E", new InertString(TextPolicy.Field, "\u202E").ToString());

        // The collision the backslash introducer exists to prevent: U+001E is 0x1E + 0x40 = '^',
        // so a caret-introduced spelling gives RS and a literal caret the same output.
        Assert.NotEqual(
            new InertString(TextPolicy.Field, "\u001E").ToString(),
            new InertString(TextPolicy.Field, "^").ToString());
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
            Assert.Equal(text, new InertString(TextPolicy.Field, text).ToString());
            Assert.True(InertString.IsPermitted(TextPolicy.Field, text));
        }
    }

    [Fact]
    public void IsPermitted_TreatsTheBackslashAsPermitted()
    {
        // A Windows path is the case that makes this matter: it is full of backslashes, none of
        // them is a hazard, and a check derived from "would Encode change this" rejects them all.
        const string path = @"C:\Users\rich\.nuget\packages";

        Assert.True(InertString.IsPermitted(TextPolicy.Field, path));
        Assert.NotEqual(path, new InertString(TextPolicy.Field, path).ToString());
    }

    [Fact]
    public void Policy_IsTheCallersToChoose()
    {
        const string multiline = "line one\nline two\tindented";

        Assert.True(InertString.IsPermitted(TextPolicy.Prose, multiline));
        Assert.Equal(multiline, new InertString(TextPolicy.Prose, multiline).ToString());

        Assert.False(InertString.IsPermitted(TextPolicy.Field, multiline));
        Assert.Equal(@"line one\^Jline two\^Iindented", new InertString(TextPolicy.Field, multiline).ToString());

        // No sink may exempt a bidi control, whatever else it exempts.
        Assert.False(InertString.IsPermitted(TextPolicy.Prose, "\u202E"));
    }

    /// <summary>
    /// The stated limit of every <see cref="TextPolicy"/>, and the reason the set does not try to
    /// grow one that closes it.
    /// </summary>
    /// <remarks>
    /// Recorded as a test so the boundary is a measured fact rather than an assumption a reader
    /// has to make, and so that anyone tempted to add an allow-shaped member finds the argument
    /// against it here.
    /// </remarks>
    [Fact]
    public void NoTextPolicy_CatchesAHomoglyph_BecauseEveryOneIsDenyShaped()
    {
        // Cyrillic е U+0435 and Latin e U+0065 are the same glyph, both category Ll, and neither
        // is a hazard. A deny-shaped rule renders the typosquat raw and the substitution stays
        // invisible -- correctly, because refusing every non-Latin letter would break most of the
        // world's text.
        const string hijacked = "N\u0435wtonsoft.Json";

        foreach (TextPolicy policy in Enum.GetValues<TextPolicy>())
        {
            Assert.True(InertString.IsPermitted(policy, hijacked));
            Assert.Equal(hijacked, new InertString(policy, hijacked).ToString());
        }

        // An allow list over the published grammar would catch it, and that is exactly the shape
        // this library stopped offering. Encoding cannot serve it: the spelling an allow-shaped
        // rule would produce, \u0435, is itself outside the grammar, so the repaired value fails
        // the same check that rejected the original. The operation such a rule wants is
        // rejection, which is a different answer than "here is a safe rendering" and belongs in
        // a different API.
        const string spelled = @"N\u0435wtonsoft.Json";

        static bool PackageId(char c)
            => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                or '.' or '-' or '_';

        Assert.Contains(hijacked, c => !PackageId(c));
        Assert.Contains(spelled, c => !PackageId(c));
    }

    [Fact]
    public void Legend_NamesEveryFormTheOutputContains()
    {
        InertString value = new InertString(TextPolicy.Field, "a\u001B\u007F\u202E\U00013430\\b");
        string encoded = value.ToString();
        VisualForm forms = value.Forms;

        IReadOnlyList<string> legend = value.DescribeLegend();

        // Derived from the enum rather than hand-listed: adding a VisualForm and forgetting
        // either the sample input or its DescribeLegend arm has to fail here, not pass quietly.
        VisualForm[] every = Enum.GetValues<VisualForm>().Where(f => f != VisualForm.None).ToArray();
        Assert.Equal(every.Length, legend.Count);
        foreach (VisualForm form in every)
        {
            Assert.True(forms.HasFlag(form), $"the sample input no longer produces {form}");
            Assert.NotEmpty(VisualEncoder.DescribeLegend(form));
        }

        // The legend names forms, never values.
        Assert.DoesNotContain(legend, line => line.Contains("13430", StringComparison.Ordinal));
        Assert.Contains(@"\U00013430", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Legend_IsEmptyWhenNothingWasEncoded()
    {
        VisualForm forms = new InertString(TextPolicy.Field, "ordinary text").Forms;

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

    [Fact]
    public void Violation_NamesTheActualCodeUnitForAnUnpairedSurrogate()
    {
        Assert.False(InertString.IsPermitted(TextPolicy.Field, "ab\uD800cd", out ScalarViolation? violation));

        // Rune cannot hold a lone surrogate, so reporting through it named U+FFFD here --
        // one wrong answer, identically, for all 2048 values whose identity is the finding.
        Assert.Equal(0xD800, violation!.Value.Scalar);
        Assert.Equal(UnicodeCategory.Surrogate, violation.Value.Category);
        Assert.Equal(2, violation.Value.Index);
        Assert.Equal("U+D800 (Surrogate) at 2", violation.Value.ToString());
    }

    [Fact]
    public void Violation_AgreesWithTheSpellingTheEncoderChooses()
    {
        const string input = "ab\uDFFFcd";
        Assert.False(InertString.IsPermitted(TextPolicy.Field, input, out ScalarViolation? violation));

        string encoded = new InertString(TextPolicy.Field, input).ToString();

        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"\\u{violation!.Value.Scalar:X4}"),
            encoded,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\u005C")]
    [InlineData(@"\u0000")]
    [InlineData(@"\u001B")]
    [InlineData(@"\u007F")]
    public void Decode_RejectsBmpHexForScalarsWithACanonicalSpelling(string encoded)
    {
        // Encode spells these \\, \^X and \^?, so accepting \uXXXX too would mean one scalar
        // with two encodings.
        Assert.False(VisualEncoder.TryDecode(encoded, out _));
    }

    [Fact]
    public void Decode_StillAcceptsBmpHexForScalarsAPolicyMayRefuse()
    {
        // 'A' has no canonical short spelling, and a restrictive policy is free to encode it,
        // so the canonicality check must not reach this far.
        Assert.True(VisualEncoder.TryDecode(@"\u0041", out string? decoded));
        Assert.Equal("A", decoded);
    }

    /// <summary>
    /// The decoder accepts exactly one spelling per scalar, which is what the repair in
    /// <c>EnsurePermitted</c> rests on.
    /// </summary>
    /// <remarks>
    /// A second accepted spelling is not leniency. <c>EnsurePermitted</c> decodes a value and
    /// re-encodes it under the policy in force, so if two inputs decode to the same text they
    /// converge on one output, and a value that survived one repair unchanged can change under
    /// the next. Each case below was accepted before this test existed.
    /// </remarks>
    [Theory]
    [InlineData(@"\u00ad", "lowercase BMP hex; AppendBmpHex emits X4, never x4")]
    [InlineData(@"\U0001f600", "lowercase astral hex; AppendSpelling emits X8")]
    [InlineData(@"\uD83D\uDE00", "a surrogate pair as two escapes; the scalar spells as \\U0001F600")]
    public void Decode_RefusesANonCanonicalSpelling(string encoded, string why)
    {
        Assert.False(VisualEncoder.TryDecode(encoded, out _), why);
    }

    /// <summary>
    /// The canonical spelling of each of those is still accepted, so the refusals above are
    /// about spelling rather than about the scalar.
    /// </summary>
    [Theory]
    [InlineData(@"\u00AD", "\u00ad")]
    [InlineData(@"\U0001F600", "\U0001F600")]
    public void Decode_AcceptsTheCanonicalSpelling(string encoded, string expected)
    {
        Assert.True(VisualEncoder.TryDecode(encoded, out string? decoded));
        Assert.Equal(expected, decoded);
    }

    /// <summary>
    /// A <em>lone</em> surrogate escape stays legal, which is why the pair above is refused
    /// there rather than by banning the surrogate range outright.
    /// </summary>
    /// <remarks>
    /// The obvious fix for the pair case -- reject every <c>\uXXXX</c> in D800-DFFF -- breaks
    /// this, and this is a form <see cref="VisualEncoder.Encode"/> genuinely emits: an unpaired
    /// surrogate is not a scalar, so it has no other representation at all. Encoder output that
    /// its own decoder refuses would make the transform non-invertible on exactly the input
    /// class that most needs it.
    /// </remarks>
    [Fact]
    public void Decode_AcceptsALoneSurrogateEscape_BecauseEncodeEmitsOne()
    {
        InertString encoded = VisualEncoder.Encode(TextPolicy.Field, "a\uD83Db");

        Assert.Equal(@"a\uD83Db", encoded.ToString());
        Assert.True(VisualEncoder.TryDecode(encoded.ToString(), out string? decoded));
        Assert.Equal("a\uD83Db", decoded);
    }

    /// <summary>
    /// A raw unpaired surrogate in decoder input is refused, because <c>Encode</c> spells one as
    /// <c>\uXXXX</c> and accepting both would be a second spelling again.
    /// </summary>
    [Fact]
    public void Decode_RefusesARawUnpairedSurrogate()
    {
        Assert.False(VisualEncoder.TryDecode("a\uD83Db", out _));
    }

    /// <summary>
    /// A raw surrogate <em>pair</em> passes through, because a graphic astral scalar is
    /// permitted and <c>Encode</c> leaves it alone.
    /// </summary>
    [Fact]
    public void Decode_AcceptsARawSurrogatePair()
    {
        const string emoji = "a\U0001F600b";

        Assert.Equal(emoji, VisualEncoder.Encode(TextPolicy.Field, emoji).ToString());
        Assert.True(VisualEncoder.TryDecode(emoji, out string? decoded));
        Assert.Equal(emoji, decoded);
    }
}
