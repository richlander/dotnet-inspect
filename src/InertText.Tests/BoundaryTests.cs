using InertText.Encoding;

namespace InertText.Tests;

/// <summary>
/// Gates the boundary members: that a bounded value is still a well-formed one.
/// </summary>
/// <remarks>
/// Truncation is where this library is easiest to break, because a spelling is several
/// characters wide and a budget is not aware of that. Cutting <c>\u202E</c> anywhere but at its
/// ends leaves text the decoder rejects, and <see cref="InertString.EnsurePermitted"/> treats
/// text it cannot decode as raw — so the repair path re-encodes the surviving backslash and
/// turns <c>\u2</c> into <c>\\u2</c>, which is also what an unrelated literal encodes to. Two
/// inputs converge on one output, and the transform stops being invertible.
///
/// So the property these tests hold is not "the result is short enough". It is that the result
/// is a fixpoint of <see cref="InertString.EnsurePermitted"/> — the same statement the encoder's
/// own injectivity rests on — checked at every budget rather than at a chosen few.
/// </remarks>
public class BoundaryTests
{
    private const string Hazard = "a\u202Eb\\c";

    [Theory]
    [MemberData(nameof(BoundaryCorpus.Names), MemberType = typeof(BoundaryCorpus))]
    public void Truncate_LeavesAWellFormedValueAtEveryBudget(string name)
    {
        InertString full = BoundaryCorpus.ByName(name);
        string text = full.ToString();

        // The decoded text of the whole value, read once. A cut in the encoded text names a
        // position in this string, and that is the coordinate system the atomicity guarantee
        // is stated in.
        Assert.True(VisualEncoder.TryDecode(text, out string? whole));

        // Past the end as well as inside it, because a budget is caller-supplied and an
        // over-budget request is the common case rather than an edge one.
        for (int budget = 0; budget <= text.Length + 1; budget++)
        {
            InertString cut = full.Truncate(budget);

            Assert.True(cut.Length <= Math.Max(0, Math.Min(budget, text.Length)));
            Assert.Equal(text[..cut.Length], cut.ToString());

            // The whole point: what survives is still readable as encoded text, so the repair
            // path leaves it alone instead of re-encoding its backslashes.
            Assert.True(VisualEncoder.TryDecode(cut.ToString(), out string? kept));
            Assert.Equal(cut, cut.EnsurePermitted(TextPolicy.Field));

            // Decoding a prefix of encoded text gives a prefix of the decoded text, which is
            // what lets the cut be named as a single position in the whole value below.
            Assert.True(kept.Length <= whole.Length);
            Assert.Equal(whole[..kept.Length], kept);

            // Well-formed encoded text is not enough on its own. Each half of a spelled
            // surrogate pair is a well-formed \uXXXX escape, so a cut between them decodes and
            // survives the repair path while still handing a sink a lone surrogate. The harm is
            // only visible on the decoded side, so that is where it is asserted.
            AssertNoScalarDivided(whole, 0, kept.Length);
        }
    }

    /// <summary>
    /// A window of encoded text names a range of the decoded text; that range must not fall
    /// between the halves of a surrogate pair.
    /// </summary>
    /// <remarks>
    /// This is the atomicity guarantee stated where it can be falsified. It says nothing about
    /// lone surrogates the value already contained -- the corpus carries two of those on
    /// purpose -- only that a bound may not create one that the whole value did not have.
    /// </remarks>
    private static void AssertNoScalarDivided(string decoded, int from, int to)
    {
        foreach (int at in (int[])[from, to])
        {
            Assert.False(
                at > 0
                    && at < decoded.Length
                    && char.IsHighSurrogate(decoded[at - 1])
                    && char.IsLowSurrogate(decoded[at]),
                $"bound at {at} divides the surrogate pair at {at - 1}");
        }
    }

    [Theory]
    [MemberData(nameof(BoundaryCorpus.Names), MemberType = typeof(BoundaryCorpus))]
    public void Truncate_KeepsAsMuchAsTheBudgetAllows(string name)
    {
        InertString full = BoundaryCorpus.ByName(name);
        string text = full.ToString();

        for (int budget = 0; budget <= text.Length; budget++)
        {
            int kept = full.Truncate(budget).Length;

            // Snapping down is only defensible if it stops at the first position it can, so
            // every position it skipped has to be one that divides a spelling. Opening a window
            // at a position answers that: the start moves forward only when it has to, so it
            // keeps the whole tail exactly when the position was already a boundary.
            for (int longer = kept + 1; longer <= budget; longer++)
            {
                Assert.NotEqual(text.Length - longer, full.Truncate(longer..).Length);
            }
        }
    }

    [Fact]
    public void Truncate_DoesNotDivideASpelling()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);

        // "a" then \u202E (6) then "b" then \\ (2) then "c".
        Assert.Equal(@"a\u202Eb\\c", full.ToString());

        Assert.Equal("a", full.Truncate(1).ToString());
        Assert.Equal("a", full.Truncate(6).ToString());
        Assert.Equal(@"a\u202E", full.Truncate(7).ToString());
        Assert.Equal(@"a\u202Eb", full.Truncate(8).ToString());
        Assert.Equal(@"a\u202Eb", full.Truncate(9).ToString());
        Assert.Equal(@"a\u202Eb\\", full.Truncate(10).ToString());
    }

    [Fact]
    public void Truncate_KeepsARawSurrogatePairWhole()
    {
        // Grinning face, which Field permits: So is graphic, so it passes through as two raw
        // code units rather than as a spelling.
        InertString full = new InertString(TextPolicy.Field, "a\U0001F600b");

        Assert.Equal("a", full.Truncate(2).ToString());
        Assert.Equal("a\U0001F600", full.Truncate(3).ToString());
    }

    [Fact]
    public void Truncate_KeepsASpelledSurrogatePairWhole()
    {
        // Composition encodes each fragment alone, so the halves of one astral scalar are
        // spelled separately and arrive as two \uXXXX escapes rather than one \U.
        InertString split = InertString.Join(
            string.Empty,
            TextPolicy.Field,
            [
                new InertString(TextPolicy.Field, "\uD83D"),
                new InertString(TextPolicy.Field, "\uDE00"),
            ]);

        Assert.Equal(@"\uD83D\uDE00", split.ToString());

        // Cutting between them would leave a lone surrogate in the decoded text, which is the
        // atomicity the escaper this replaces guaranteed at its own budget boundary.
        Assert.Equal(string.Empty, split.Truncate(11).ToString());
        Assert.Equal(@"\uD83D\uDE00", split.Truncate(12).ToString());
    }

    [Fact]
    public void Truncate_ReportsOnlyTheSpellingsItKept()
    {
        InertString full = new InertString(TextPolicy.Field, "\u202E\u0001");

        Assert.Equal(VisualForm.BmpHex | VisualForm.Caret, full.Forms);

        // A legend drawn from the result must not name a spelling that went with the tail.
        Assert.Equal(VisualForm.BmpHex, full.Truncate(6).Forms);
        Assert.Equal(VisualForm.None, full.Truncate(5).Forms);
    }

    [Fact]
    public void Truncate_ReturnsThisValueWhenTheBudgetIsNotBinding()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);

        Assert.Same(full.ToString(), full.Truncate(full.Length).ToString());
        Assert.Same(full.ToString(), full.Truncate(int.MaxValue).ToString());
    }

    [Fact]
    public void Truncate_ReadsANegativeBudgetAsZero()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);

        Assert.True(full.Truncate(-5).IsEmpty);
    }

    [Fact]
    public void Truncate_AgreesWithTheZeroValue()
    {
        // The zero value and Encode("") are one value everywhere else in the type, so a member
        // that answered differently for the two would be the defect the equality gate exists for.
        Assert.Equal(default(InertString).Truncate(4), InertString.Empty.Truncate(4));
    }

    [Fact]
    public void TruncateRange_LeavesTheOriginalIntact()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);
        string before = full.ToString();

        _ = full.Truncate(1..7);

        Assert.Equal(before, full.ToString());
        Assert.Equal(VisualForm.BmpHex | VisualForm.Backslash, full.Forms);
    }

    [Fact]
    public void MidSpellingCutsAreWhyTheBoundaryIsChecked()
    {
        // The failure this exists to prevent, stated as a fact about the library rather than
        // left implied: a raw mid-spelling cut is not a fixpoint of the repair path, so two
        // unrelated inputs would converge on one encoded form.
        InertString reencoded = new InertString(TextPolicy.Field, @"a\u2");

        Assert.Equal(@"a\\u2", reencoded.ToString());
        Assert.NotEqual(@"a\u2", reencoded.ToString());
    }

    [Theory]
    [MemberData(nameof(BoundaryCorpus.Names), MemberType = typeof(BoundaryCorpus))]
    public void TruncateRange_ReturnsASubsetOfEveryWindowAsked(string name)
    {
        InertString full = BoundaryCorpus.ByName(name);
        string text = full.ToString();

        Assert.True(VisualEncoder.TryDecode(text, out string? whole));

        for (int start = 0; start <= text.Length; start++)
        {
            // Where the window's near bound landed after snapping, named in the decoded text.
            // Opening a window at a position and measuring what is left reports the snapped
            // start; the text before it decodes to everything the window drops.
            int snapped = text.Length - full.Truncate(start..).Length;
            Assert.True(VisualEncoder.TryDecode(text[..snapped], out string? dropped));

            for (int end = 0; end <= text.Length; end++)
            {
                InertString window = full.Truncate(start..Math.Max(start, end));
                string kept = window.ToString();

                // Both bounds move inward, so what comes back must appear inside the window
                // that was asked for -- never one character to the left of it, which is the
                // direction a caller cannot check.
                Assert.Contains(kept, text[start..Math.Max(start, end)]);

                Assert.True(VisualEncoder.TryDecode(kept, out string? inside));
                Assert.Equal(window, window.EnsurePermitted(TextPolicy.Field));

                // Neither bound may divide a surrogate pair of the decoded text. The budget
                // sweep can only ever check the far bound, because it cuts from zero; the near
                // bound exists only here, and the same token walk places both.
                AssertNoScalarDivided(whole, dropped.Length, dropped.Length + inside.Length);
            }
        }
    }

    [Fact]
    public void TruncateRange_MovesTheStartForwardAndTheEndBack()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);

        // \u202E occupies 1..7, so a window opening inside it starts after it, not before.
        Assert.Equal(@"b\\", full.Truncate(2..10).ToString());
        Assert.Equal("b", full.Truncate(2..9).ToString());

        // And a window wholly inside one spelling holds nothing whole, so it is empty rather
        // than widened to the spelling that encloses it.
        Assert.True(full.Truncate(2..6).IsEmpty);
    }

    [Fact]
    public void TruncateRange_AnswersEveryRangeWithoutThrowing()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);

        // The three shapes the exact slicer refuses: past the end, reversed, and empty.
        Assert.Equal(full.ToString(), full.Truncate(0..int.MaxValue).ToString());
        Assert.True(full.Truncate(9..2).IsEmpty);
        Assert.True(full.Truncate(4..4).IsEmpty);

        // From-end indices, including one that runs off the front.
        Assert.Equal(@"b\\c", full.Truncate(^4..).ToString());
        Assert.Equal(full.ToString(), full.Truncate(^99..).ToString());

        // And both bounds past the end, which is what the walker's own bounds check answers
        // rather than a clamp at the call site.
        Assert.True(full.Truncate(99..100).IsEmpty);
        Assert.Equal(full.ToString(), full.Truncate(0..int.MaxValue).ToString());
    }

    [Fact]
    public void TruncateRange_AgreesWithTheBudgetForm()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);

        for (int budget = 0; budget <= full.Length; budget++)
        {
            Assert.Equal(full.Truncate(budget), full.Truncate(..budget));
        }
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void TruncateRange_ReportsTheSpellingsTheWalkerFindsAsTheEncoderWouldSpellThem(string name)
    {
        // The walker reads the speller's output backwards, so its widths and its form mapping
        // are that method's table restated. Nothing makes the two move together, and a drift
        // would not break a boundary -- it would make DescribeLegend name a spelling the text
        // does not contain, which no length assertion can see.
        //
        // Ground truth has to come from outside the walker, so it is taken by decoding the
        // window and encoding it again from scratch: that path runs AppendSpelling and never
        // consults NextToken.
        //
        // That oracle is only sound for DIRECTLY-ENCODED values, which is why this sweep does
        // not run over BoundaryCorpus like the others. Re-encoding asks what a fresh encode of
        // the decoded text would spell, and for a composed value that is a different question
        // from what the value was spelled with -- see
        // TruncateRange_KeepsTheSpellingsCompositionEmitted, where the two disagree and the
        // walker is right.
        InertString full = new InertString(TextPolicy.Field, AdversarialCorpus.ByName(name).Payload);
        string text = full.ToString();

        for (int start = 0; start <= text.Length; start++)
        {
            for (int end = start; end <= text.Length; end++)
            {
                InertString window = full.Truncate(start..end);

                Assert.True(VisualEncoder.TryDecode(window.ToString(), out string? original));
                Assert.Equal(VisualEncoder.Encode(TextPolicy.Field, original).Forms, window.Forms);
            }
        }
    }

    [Fact]
    public void TruncateRange_ReportsOnlyTheSpellingsInTheWindow()
    {
        InertString full = new InertString(TextPolicy.Field, "\u202E\u0001");

        Assert.Equal(VisualForm.BmpHex | VisualForm.Caret, full.Forms);
        Assert.Equal(VisualForm.Caret, full.Truncate(6..).Forms);
        Assert.Equal(VisualForm.BmpHex, full.Truncate(..6).Forms);
    }

    [Fact]
    public void TruncateRange_KeepsTheSpellingsCompositionEmitted()
    {
        // Forms names the spellings emitted while producing the value, and composition unions
        // them -- so a window's Forms has to describe what a reader of that window actually
        // sees. Here the reader sees two \uXXXX escapes, and the walker says BmpHex.
        InertString split = BoundaryCorpus.ByName("ComposedSurrogatePair");

        Assert.Equal(@"\uD83D\uDE00", split.ToString());
        Assert.Equal(VisualForm.BmpHex, split.Forms);
        Assert.Equal(VisualForm.BmpHex, split.Truncate(..split.Length).Forms);

        // Decode-then-re-encode, the oracle the directly-encoded sweep uses, disagrees: the two
        // escapes decode to one astral scalar, and Field permits So, so a fresh encode emits it
        // raw and reports None. That is the answer to a different question -- what encoding
        // this text again would spell, not what this value was spelled with -- and it is the
        // wrong one here, because nothing re-encodes a value on its way to a sink.
        Assert.True(VisualEncoder.TryDecode(split.ToString(), out string? decoded));
        Assert.Equal(VisualForm.None, VisualEncoder.Encode(TextPolicy.Field, decoded).Forms);
    }

    [Fact]
    public void IndexOfFirstEncoded_LocatesTheFirstSpelling()
    {
        Assert.Equal(1, new InertString(TextPolicy.Field, Hazard).IndexOfFirstEncoded());
        Assert.Equal(0, new InertString(TextPolicy.Field, "\u202Ea").IndexOfFirstEncoded());
    }

    [Fact]
    public void IndexOfFirstEncoded_NamesABoundThatNeedsNoAdjustment()
    {
        // The two members compose: the index is always a boundary, so taking up to it returns
        // the untreated prefix whole rather than a shortened one.
        InertString full = new InertString(TextPolicy.Field, Hazard);
        int first = full.IndexOfFirstEncoded();

        Assert.Equal("a", full.Truncate(..first).ToString());
        Assert.Equal(first, full.Truncate(..first).Length);
        Assert.Equal(VisualForm.None, full.Truncate(..first).Forms);
    }

    [Theory]
    [MemberData(nameof(BoundaryCorpus.Names), MemberType = typeof(BoundaryCorpus))]
    public void IndexOfFirstEncoded_AgreesWithWasEncoded(string name)
    {
        InertString value = BoundaryCorpus.ByName(name);

        Assert.Equal(value.WasEncoded, value.IndexOfFirstEncoded() >= 0);

        // Everything before it came through untouched, so a caller may show that much without
        // asking what the value was built from.
        if (value.IndexOfFirstEncoded() is int first and >= 0)
        {
            Assert.DoesNotContain('\\', value.ToString()[..first]);
        }
    }

    [Fact]
    public void IndexOfFirstEncoded_ReportsMinusOneForUntreatedText()
    {
        InertString value = new InertString(TextPolicy.Field, "nothing to encode");

        Assert.Equal(-1, value.IndexOfFirstEncoded());
        Assert.False(value.WasEncoded);
    }

    [Fact]
    public void Length_MeasuresTheEncodedTextRatherThanTheOriginal()
    {
        InertString full = new InertString(TextPolicy.Field, Hazard);

        // A budget bounds what a sink emits, so the number has to be the emitted one: five
        // characters in, eleven out.
        Assert.Equal(11, full.Length);
        Assert.Equal(full.ToString().Length, full.Length);
        Assert.Equal(0, default(InertString).Length);
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void BoundingConstructor_AgreesWithEncodeThenTruncate_AtEveryBudget(string name)
    {
        string payload = AdversarialCorpus.ByName(name).Payload;
        InertString encoded = new InertString(TextPolicy.Field, payload);

        // Past the encoded length as well, so the budget nobody is near is swept too.
        for (int budget = 0; budget <= encoded.Length + 2; budget++)
        {
            InertString bounded = new InertString(TextPolicy.Field, payload, budget);

            Assert.Equal(encoded.Truncate(budget).ToString(), bounded.ToString());
            Assert.Equal(bounded.Length < encoded.Length, bounded.IsTruncated);
        }
    }

    [Fact]
    public void BoundingConstructor_ReportsTruncation_WhereTheRawLengthWouldNot()
    {
        // The reason IsTruncated exists rather than being left to the caller. Bounding in one
        // step does not leave the caller holding the unbounded value, so the comparison it
        // reaches for instead is against the raw input's length — and encoding expands, so that
        // comparison says "complete" about a value clipped mid-hazard.
        InertString bounded = new InertString(TextPolicy.Field, Hazard, 8);

        Assert.Equal(@"a\u202Eb", bounded.ToString());
        Assert.True(bounded.IsTruncated);

        // Eight encoded characters from five raw ones: the fallback compares 8 < 5 and concludes
        // nothing was dropped, while three characters of the value are gone.
        Assert.False(bounded.Length < Hazard.Length);
    }

    [Fact]
    public void IsTruncated_IsFalse_WhereNothingWasDropped()
    {
        Assert.False(default(InertString).IsTruncated);
        Assert.False(InertString.Empty.IsTruncated);
        Assert.False(new InertString(TextPolicy.Field, Hazard).IsTruncated);
        Assert.False(new InertString(TextPolicy.Field, Hazard, 11).IsTruncated);
        Assert.False(new InertString(TextPolicy.Field, Hazard, 99).IsTruncated);
    }

    [Fact]
    public void BoundingConstructor_ReadsANegativeBudgetAsZero()
    {
        // A consumer's budget arrives as configured rather than clamped, so a negative one has
        // to mean "keep nothing" — and keeping nothing of a value that had something is a
        // truncation, not an empty value that was always empty.
        InertString bounded = new InertString(TextPolicy.Field, Hazard, -1);

        Assert.Equal(string.Empty, bounded.ToString());
        Assert.True(bounded.IsTruncated);
        Assert.NotEqual(InertString.Empty, bounded);
    }

    [Fact]
    public void Truncate_KeepsReportingTruncation_ThroughASecondCut()
    {
        // The second cut is not the only one, so the value cannot start calling itself whole
        // again once the first cut is behind it.
        InertString once = new InertString(TextPolicy.Field, Hazard, 10);
        InertString twice = once.Truncate(8);

        Assert.True(once.IsTruncated);
        Assert.True(twice.IsTruncated);

        // A cut that drops nothing leaves the earlier one visible.
        Assert.True(once.Truncate(once.Length).IsTruncated);
    }

    [Fact]
    public void EnsurePermitted_KeepsTruncation_AcrossARespellingThatGrowsTheText()
    {
        // The case a remembered length cannot survive. Prose keeps LF raw at one character
        // each; Field spells it \^J at three. Ten of them cut from eleven characters re-spell
        // to thirty, so a value that compared its length against the eleven it was cut from
        // would find thirty is not less and call a truncated value whole.
        InertString prose = new InertString(TextPolicy.Prose, new string('\n', 10) + "X", 10);

        Assert.Equal(10, prose.Length);
        Assert.True(prose.IsTruncated);

        InertString conformed = prose.EnsurePermitted(TextPolicy.Field);

        Assert.Equal(30, conformed.Length);
        Assert.True(conformed.IsTruncated);
    }

    [Fact]
    public void EnsurePermitted_LeavesAWholeValueWhole()
    {
        InertString prose = new InertString(TextPolicy.Prose, "a\nb");

        Assert.False(prose.IsTruncated);
        Assert.False(prose.EnsurePermitted(TextPolicy.Field).IsTruncated);
    }

    [Fact]
    public void Join_ReportsTruncation_WhenAPartWasClipped()
    {
        // A composed value missing part of what it was built from cannot claim to be whole.
        // The single-element case also pins the identity equality would otherwise break: the
        // join of one value renders exactly as that value, so it must compare equal to it.
        InertString clipped = new InertString(TextPolicy.Field, Hazard, 8);
        InertString joined = InertString.Join(string.Empty, TextPolicy.Field, [clipped]);

        Assert.Equal(clipped.ToString(), joined.ToString());
        Assert.True(joined.IsTruncated);
        Assert.Equal(clipped, joined);

        // And a part clipped anywhere in the sequence, not only at the end.
        InertString whole = new InertString(TextPolicy.Field, "tail");
        Assert.True(InertString.Join("-", TextPolicy.Field, [clipped, whole]).IsTruncated);
        Assert.True(InertString.Join("-", TextPolicy.Field, [whole, clipped]).IsTruncated);
        Assert.False(InertString.Join("-", TextPolicy.Field, [whole, whole]).IsTruncated);
    }

    [Fact]
    public void Interpolation_ReportsTruncation_WhenASplicedValueWasClipped()
    {
        InertString clipped = new InertString(TextPolicy.Field, Hazard, 8);
        InertString whole = new InertString(TextPolicy.Field, "name");

        Assert.True(InertString.Format(TextPolicy.Field, $"cell {clipped} end").IsTruncated);
        Assert.False(InertString.Format(TextPolicy.Field, $"cell {whole} end").IsTruncated);
    }

    [Fact]
    public void Equality_SeparatesValuesThatWouldRenderDifferently()
    {
        // Same text, but a sink marks one of them and not the other, so they are not
        // substitutable. Equality on text alone would call them the same value.
        InertString whole = new InertString(TextPolicy.Field, "a\u202Eb");
        InertString clipped = new InertString(TextPolicy.Field, Hazard, 8);

        Assert.Equal(whole.ToString(), clipped.ToString());
        Assert.NotEqual(whole, clipped);
        Assert.False(whole == clipped);
        Assert.NotEqual(whole.GetHashCode(), clipped.GetHashCode());
    }

    [Fact]
    public void Equality_JoinsValuesBoundedFromDifferentSourcesToTheSameText()
    {
        // How much was dropped is not part of the value. These were cut from different sources
        // and render identically, so nothing about them can separate them.
        InertString fromShorter = new InertString(TextPolicy.Field, "a\u202Eb", 7);
        InertString fromLonger = new InertString(TextPolicy.Field, Hazard, 7);

        Assert.Equal(@"a\u202E", fromShorter.ToString());
        Assert.Equal(fromShorter.ToString(), fromLonger.ToString());
        Assert.True(fromShorter.IsTruncated);
        Assert.True(fromLonger.IsTruncated);
        Assert.Equal(fromShorter, fromLonger);
        Assert.Equal(fromShorter.GetHashCode(), fromLonger.GetHashCode());
    }
}
