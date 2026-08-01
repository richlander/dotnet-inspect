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
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void Truncate_LeavesAWellFormedValueAtEveryBudget(string name)
    {
        InertString full = new InertString(TextPolicy.Field, AdversarialCorpus.ByName(name).Payload);
        string text = full.ToString();

        // Past the end as well as inside it, because a budget is caller-supplied and an
        // over-budget request is the common case rather than an edge one.
        for (int budget = 0; budget <= text.Length + 1; budget++)
        {
            InertString cut = full.Truncate(budget);

            Assert.True(cut.Length <= Math.Max(0, Math.Min(budget, text.Length)));
            Assert.Equal(text[..cut.Length], cut.ToString());

            // The whole point: what survives is still readable as encoded text, so the repair
            // path leaves it alone instead of re-encoding its backslashes.
            Assert.True(VisualEncoder.TryDecode(cut.ToString(), out _));
            Assert.Equal(cut, cut.EnsurePermitted(TextPolicy.Field));
        }
    }

    [Theory]
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void Truncate_KeepsAsMuchAsTheBudgetAllows(string name)
    {
        InertString full = new InertString(TextPolicy.Field, AdversarialCorpus.ByName(name).Payload);
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
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void TruncateRange_ReturnsASubsetOfEveryWindowAsked(string name)
    {
        InertString full = new InertString(TextPolicy.Field, AdversarialCorpus.ByName(name).Payload);
        string text = full.ToString();

        for (int start = 0; start <= text.Length; start++)
        {
            for (int end = 0; end <= text.Length; end++)
            {
                InertString window = full.Truncate(start..Math.Max(start, end));
                string kept = window.ToString();

                // Both bounds move inward, so what comes back must appear inside the window
                // that was asked for -- never one character to the left of it, which is the
                // direction a caller cannot check.
                Assert.Contains(kept, text[start..Math.Max(start, end)]);

                Assert.True(VisualEncoder.TryDecode(kept, out _));
                Assert.Equal(window, window.EnsurePermitted(TextPolicy.Field));
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

    [Fact]
    public void TruncateRange_ReportsOnlyTheSpellingsInTheWindow()
    {
        InertString full = new InertString(TextPolicy.Field, "\u202E\u0001");

        Assert.Equal(VisualForm.BmpHex | VisualForm.Caret, full.Forms);
        Assert.Equal(VisualForm.Caret, full.Truncate(6..).Forms);
        Assert.Equal(VisualForm.BmpHex, full.Truncate(..6).Forms);
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
    [MemberData(nameof(AdversarialCorpus.Names), MemberType = typeof(AdversarialCorpus))]
    public void IndexOfFirstEncoded_AgreesWithWasEncoded(string name)
    {
        InertString value = new InertString(TextPolicy.Field, AdversarialCorpus.ByName(name).Payload);

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
}
