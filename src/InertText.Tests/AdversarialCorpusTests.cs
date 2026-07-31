using System.Text;

using InertText.Encoder;

namespace InertText.Tests;

/// <summary>
/// Holds the named attacks in <see cref="AdversarialCorpus"/> to what the library claims.
/// </summary>
/// <remarks>
/// One test per claim rather than one per payload, so a new fixture is a single line and is
/// immediately subject to every claim at once. The claims are the four the design rests on:
/// the output is inert, nothing is dropped, the original is recoverable, and the caller is told
/// what happened.
/// </remarks>
public class AdversarialCorpusTests
{
    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void EveryAdversary_EncodesToSomethingThePolicyPermits(string name)
    {
        Adversary adversary = AdversarialCorpus.ByName(name);
        InertString inert = new(TextPolicy.Field, adversary.Payload);

        // The output is the whole point: whatever went in, what comes out contains nothing the
        // sink's policy refuses. Asserted through the predicate rather than by looking for
        // specific characters, so the claim cannot drift from the policy it is stated against.
        Assert.True(
            InertString.IsPermitted(TextPolicy.Field, inert.ToString(), out ScalarViolation? violation),
            $"{name} left U+{violation?.Scalar:X4} ({violation?.Category}) at index {violation?.Index}");
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void EveryAdversary_RoundTripsExactly(string name)
    {
        Adversary adversary = AdversarialCorpus.ByName(name);
        InertString inert = new(TextPolicy.Field, adversary.Payload);

        // Losslessness is what separates this from neutralization. A filter that dropped the
        // hostile scalars would pass the test above and destroy the evidence, which matters
        // because the reader is usually trying to work out what the artifact actually says.
        Assert.True(VisualEncoder.TryDecode(inert.ToString(), out string? original));
        Assert.Equal(adversary.Payload, original);
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void EveryAdversary_IsRecognisedAndReported(string name)
    {
        Adversary adversary = AdversarialCorpus.ByName(name);

        Assert.False(InertString.IsPermitted(TextPolicy.Field, adversary.Payload));

        InertString inert = new(TextPolicy.Field, adversary.Payload);

        Assert.True(inert.WasEncoded);
        Assert.NotEqual(VisualForm.None, inert.Forms);

        // A legend the caller can print. Every form reported has a line, so a sink can explain
        // its own output without keeping a second copy of the spelling table.
        Assert.NotEmpty(inert.DescribeLegend());
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void EveryAdversary_BecomesASingleLine(string name)
    {
        Adversary adversary = AdversarialCorpus.ByName(name);
        string encoded = new InertString(TextPolicy.Field, adversary.Payload).ToString();

        // Stated separately from the policy check because line injection is the attack with a
        // consequence beyond display: a forged line is indistinguishable from a real one once
        // it reaches a log. Covers the terminators a CR/LF check misses -- NEL, LS and PS.
        Assert.DoesNotContain('\n', encoded);
        Assert.DoesNotContain('\r', encoded);
        Assert.DoesNotContain('\u0085', encoded);
        Assert.DoesNotContain('\u2028', encoded);
        Assert.DoesNotContain('\u2029', encoded);
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void EveryAdversary_SurvivesSplicingIntoAField(string name)
    {
        Adversary adversary = AdversarialCorpus.ByName(name);

        // The composition path, which is where the reviewed defect lived. A value built under
        // the laxer prose policy and spliced into a field message must be re-spelled, not
        // trusted -- prose permits the line feed that field exists to remove.
        InertString asProse = new(TextPolicy.Prose, adversary.Payload);
        InertString message = InertString.Format(TextPolicy.Field, $"source '{asProse}' rejected");

        Assert.True(InertString.IsPermitted(TextPolicy.Field, message.ToString()));
        Assert.DoesNotContain('\n', message.ToString());
    }

    [Fact]
    public void EveryAdversary_IsDistinctAfterEncoding()
    {
        // Injectivity, restated over the corpus rather than over an alphabet. Two different
        // attacks must not collapse to the same rendering, or the encoded form cannot be used
        // as evidence of which one arrived.
        Dictionary<string, string> byEncoding = new(StringComparer.Ordinal);

        foreach (Adversary adversary in AdversarialCorpus.All)
        {
            string encoded = new InertString(TextPolicy.Field, adversary.Payload).ToString();

            Assert.False(
                byEncoding.TryGetValue(encoded, out string? collidesWith),
                $"{adversary.Name} encodes identically to {collidesWith}");

            byEncoding[encoded] = adversary.Name;
        }
    }

    [Fact]
    public void EveryAdversary_EncodesToAsciiWhereItsPayloadIsAscii()
    {
        // A weaker claim than it looks, and deliberately so. The encoded spellings are ASCII,
        // but the policy permits any graphic scalar, so text mixing hazards with CJK stays
        // non-ASCII and should. Asserted only for payloads whose non-hostile part is ASCII,
        // which is every fixture here, to catch a speller that emitted a non-ASCII escape.
        foreach (Adversary adversary in AdversarialCorpus.All)
        {
            string encoded = new InertString(TextPolicy.Field, adversary.Payload).ToString();

            Assert.True(
                Ascii.IsValid(encoded),
                $"{adversary.Name} produced a non-ASCII rendering: {encoded}");
        }
    }

    [Fact]
    public void Homoglyph_IsNotCaughtByAnyTextPolicy()
    {
        Adversary homoglyph = AdversarialCorpus.Homoglyph;

        // The documented limit. Cyrillic a is Ll, exactly like Latin a, so a category rule
        // passes it -- correctly, because refusing every non-Latin letter would break most of
        // the world's text. Recorded as a test so the boundary of the policy set is a stated
        // fact rather than an assumption a reader has to make.
        //
        // Every member, not just Field: the set is deny-shaped throughout, so this is a property
        // of the set rather than of one policy, and adding a member does not quietly change it.
        foreach (TextPolicy policy in Enum.GetValues<TextPolicy>())
        {
            Assert.True(InertString.IsPermitted(policy, homoglyph.Payload));
            Assert.Equal(homoglyph.Payload, new InertString(policy, homoglyph.Payload).ToString());
        }
    }
}
