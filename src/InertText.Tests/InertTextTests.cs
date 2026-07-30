using System.Globalization;

namespace Inert.Tests;

/// <summary>
/// Gates the three properties the encoding asserts: inert, lossless, and invertible.
/// </summary>
/// <remarks>
/// Invertibility is the one that cannot be checked by inspection. A caret-introduced spelling
/// looks correct and passes casual review, and only fails once a decoder exists and the sweep
/// below runs it over the alphabet that can collide.
/// </remarks>
public class InertTextTests
{
    [Fact]
    public void Encode_EveryCodeUnit_RoundTrips()
    {
        for (int i = 0; i <= 0xFFFF; i++)
        {
            string original = ((char)i).ToString();
            string encoded = InertText.Encode(original);

            Assert.True(InertText.TryDecode(encoded, out string? decoded),
                $"U+{i:X4} encoded to '{encoded}', which did not decode");
            Assert.Equal(original, decoded);
        }
    }

    [Fact]
    public void Encode_CollidingAlphabet_RoundTripsAndStaysInjective()
    {
        // The characters that can collide: the introducer, the caret image, the \u marker, the
        // DEL spelling, the ends of the caret range, hex digits, and a representative of each
        // encoded category.
        char[] alphabet =
        [
            '\\', '^', 'u', '?', '@', '[', '_', '0', 'A', 'f',
            '\u001B', '\u001E', '\u007F', '\u0085', '\u202E', '\uFEFF', '\u2028', '\uD800',
        ];

        Dictionary<string, string> seen = [];

        void Sweep(string prefix, int depth)
        {
            if (depth == 0)
            {
                string encoded = InertText.Encode(prefix);

                Assert.True(InertText.TryDecode(encoded, out string? decoded));
                Assert.Equal(prefix, decoded);

                // Injective: two different inputs must never encode to the same output, or the
                // "lossless" claim is false however well the round-trip reads.
                Assert.False(seen.TryGetValue(encoded, out string? other) && other != prefix,
                    $"'{Describe(prefix)}' and '{Describe(other ?? "")}' both encode to '{encoded}'");
                seen[encoded] = prefix;
                return;
            }

            foreach (char c in alphabet)
            {
                Sweep(prefix + c, depth - 1);
            }
        }

        for (int length = 1; length <= 3; length++)
        {
            Sweep("", length);
        }
    }

    [Fact]
    public void Encode_TrojanSourceCodePoints_AreEncodedByName()
    {
        // Named rather than looked up by category, so narrowing the category rule fails here
        // instead of silently ceasing to cover the set rustc made a hard error.
        char[] trojanSource =
        [
            '\u202A', '\u202B', '\u202C', '\u202D', '\u202E',
            '\u2066', '\u2067', '\u2068', '\u2069',
            '\u200E', '\u200F', '\u061C',
        ];

        foreach (char c in trojanSource)
        {
            string encoded = InertText.Encode(c.ToString());

            Assert.Equal($@"\u{(int)c:X4}", encoded);
            Assert.DoesNotContain(encoded, e => char.IsControl(e));
        }
    }

    [Fact]
    public void Encode_C0AndDel_UseCaretNotationIntroducedByBackslash()
    {
        Assert.Equal(@"\^[", InertText.Encode("\u001B"));
        Assert.Equal(@"\^@", InertText.Encode("\u0000"));
        Assert.Equal(@"\^?", InertText.Encode("\u007F"));
        Assert.Equal(@"\\", InertText.Encode("\\"));

        // The collision the backslash introducer exists to prevent: U+001E is 0x1E + 0x40 = '^',
        // so a caret-introduced spelling gives RS and a literal caret the same output.
        Assert.NotEqual(InertText.Encode("\u001E"), InertText.Encode("^"));
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
            Assert.Equal(text, InertText.Encode(text));
        }
    }

    [Fact]
    public void TryDecode_MalformedInput_IsRejected()
    {
        foreach (string malformed in new[] { @"\", @"\q", @"\^", @"\^!", @"\u12", @"\uZZZZ" })
        {
            Assert.False(InertText.TryDecode(malformed, out string? value), malformed);
            Assert.Null(value);
        }
    }

    [Fact]
    public void IsInert_AgreesWithEncode_ExceptOnTheIntroducer()
    {
        // The two predicates answer different questions, and the backslash is the only place
        // they are allowed to differ: it is inert, but Encode still rewrites it to stay
        // invertible. Anything else Encode touches must be reported as non-inert, or the
        // early-fail check would pass text the encoder considers active.
        for (int cp = 0; cp <= 0xFFFF; cp++)
        {
            if (char.IsSurrogate((char)cp))
            {
                continue;
            }

            string text = ((char)cp).ToString();
            bool encodeChangedIt = InertText.Encode(text) != text;
            bool expected = cp != '\\' && encodeChangedIt;

            Assert.Equal(expected, !InertText.IsInert(text));
        }
    }

    [Fact]
    public void IsInert_OrdinaryTextIncludingPaths_IsAccepted()
    {
        // A Windows path is the case that makes the distinction matter: it is full of
        // backslashes, it is completely inert, and a check derived from "would Encode change
        // this" would reject every one of them.
        foreach (string text in new[]
        {
            @"C:\Users\rich\.nuget\packages",
            "https://pkgs.dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json",
            "日本語", "emoji \U0001F600",
        })
        {
            Assert.True(InertText.IsInert(text), text);
        }
    }

    [Fact]
    public void IsInert_UnpairedSurrogate_IsRejectedButValidPairIsNot()
    {
        Assert.False(InertText.IsInert("\uD83D"));
        Assert.False(InertText.IsInert("\uDE00"));
        Assert.True(InertText.IsInert("\U0001F600"));
    }

    [Fact]
    public void Inertness_IsTheOnlyGuarantee()
    {
        // Pins the boundary the class documents, so a later reader does not mistake this for
        // protection against text that lies about what it is. Every character below is an
        // ordinary graphic character, and all of them are meant to pass through untouched:
        // a confusable host name is a real attack that this deliberately does not address.
        foreach (string spoof in new[]
        {
            "https://p\u0430ypal.com/v3/index.json",     // Cyrillic a
            "Newt\u03BFnsoft.Json",                       // Greek omicron
            "https://evil.com\uFF0Fnuget.org/index.json", // fullwidth solidus
        })
        {
            Assert.True(InertText.IsInert(spoof), spoof);
            Assert.Equal(spoof, InertText.Encode(spoof));
        }
    }

    private static string Describe(string value)
        => string.Join(" ", value.Select(c => $"U+{(int)c:X4}"));}
