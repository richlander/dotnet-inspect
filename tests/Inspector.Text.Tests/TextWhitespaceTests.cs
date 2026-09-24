namespace Inspector.Text.Tests;

/// <summary>
/// Pair-characterization rows from the text whitespace characterization design's pathological
/// demonstration.
/// </summary>
public class TextWhitespaceTests
{
    [Theory]
    [InlineData(' ', true)]
    [InlineData('\t', true)]
    [InlineData('\r', true)]
    [InlineData('\n', true)]
    [InlineData('\u00A0', false)]
    [InlineData('\u2003', false)]
    [InlineData('\u200B', false)]
    [InlineData('\u000B', false)]
    [InlineData('\u000C', false)]
    [InlineData('\uFEFF', false)]
    public void Whitespace_IsExactlySpaceTabAndLineBoundaries(char character, bool expected)
        => Assert.Equal(expected, TextWhitespace.IsWhitespace(character));

    [Fact]
    public void Identical_HasNoEdits()
    {
        TextPairCharacterization result = TextWhitespace.Characterize("a b\n", "a b\n");

        Assert.Equal(TextPairOutcome.Identical, result.Outcome);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void BraceReflow_IsOneLineBreaksEdit()
    {
        TextPairCharacterization result = TextWhitespace.Characterize("class Foo {", "class Foo\n{");

        Assert.Equal(TextPairOutcome.WhitespaceOnly, result.Outcome);
        TextWhitespaceEdit edit = Assert.Single(result.Edits);
        Assert.Equal(TextWhitespaceEditKinds.LineBreaks, edit.Kinds);
        Assert.Equal(new TextPosition(0, 9), edit.BeforeStart);
        Assert.Equal(new TextPosition(0, 10), edit.BeforeEnd);
        Assert.Equal(new TextPosition(0, 9), edit.AfterStart);
        Assert.Equal(new TextPosition(1, 0), edit.AfterEnd);
    }

    [Fact]
    public void LineJoin_IsLineBreaks()
        => AssertSingleKind("f(a,\n  b)", "f(a, b)", TextWhitespaceEditKinds.LineBreaks);

    [Fact]
    public void TabsVersusSpaces_IsIndentation()
        => AssertSingleKind("\tx", "    x", TextWhitespaceEditKinds.Indentation);

    [Fact]
    public void ReIndentation_IsIndentationOnEachLine()
    {
        TextPairCharacterization result = TextWhitespace.Characterize("{\n  a;\n  b;\n}", "{\n    a;\n    b;\n}");

        Assert.Equal(TextPairOutcome.WhitespaceOnly, result.Outcome);
        Assert.Equal(2, result.Edits.Length);
        Assert.All(result.Edits, edit => Assert.Equal(TextWhitespaceEditKinds.Indentation, edit.Kinds));
    }

    [Fact]
    public void BlankLineSpaces_IsBlankLineContent()
        => AssertSingleKind("a\n        \nb", "a\n\nb", TextWhitespaceEditKinds.BlankLineContent);

    [Fact]
    public void Separation_IsSeparation()
        => AssertSingleKinds("foo( x )", "foo(x)", TextWhitespaceEditKinds.Separation, expectedEdits: 2);

    [Fact]
    public void TokenFusion_IsSeparationWithoutALayoutClaim()
        => AssertSingleKind("x - -y", "x --y", TextWhitespaceEditKinds.Separation);

    [Fact]
    public void LiteralWhitespace_IsSpacing()
        => AssertSingleKind("\"a b\"", "\"a  b\"", TextWhitespaceEditKinds.Spacing);

    [Fact]
    public void MarkdownHardBreak_IsTrailing()
        => AssertSingleKind("line  \nnext", "line\nnext", TextWhitespaceEditKinds.Trailing);

    [Fact]
    public void MarkdownHardBreakAtTextEnd_IsTrailing()
        => AssertSingleKind("line  ", "line", TextWhitespaceEditKinds.Trailing);

    [Fact]
    public void YamlNesting_IsIndentationWithoutAStructuralClaim()
        => AssertSingleKind("a:\nb: 1", "a:\n  b: 1", TextWhitespaceEditKinds.Indentation);

    [Fact]
    public void NoBreakSpace_IsChanged()
        => Assert.Equal(TextPairOutcome.Changed, TextWhitespace.Characterize("a b", "a\u00A0b").Outcome);

    [Fact]
    public void WordMerge_IsSeparation()
        => AssertSingleKind("foo bar", "foobar", TextWhitespaceEditKinds.Separation);

    [Fact]
    public void FinalNewline_IsLineBreaksAndFinalLineTerminator()
        => AssertSingleKind(
            "x",
            "x\n",
            TextWhitespaceEditKinds.LineBreaks | TextWhitespaceEditKinds.FinalLineTerminator);

    [Fact]
    public void TerminatorSpelling_IsLocated()
    {
        TextPairCharacterization result = TextWhitespace.Characterize("a\r\nb", "a\nb");

        TextWhitespaceEdit edit = Assert.Single(result.Edits);
        Assert.Equal(TextWhitespaceEditKinds.TerminatorSpelling, edit.Kinds);
        Assert.Equal(new TextPosition(0, 1), edit.BeforeStart);
        Assert.Equal(new TextPosition(1, 0), edit.BeforeEnd);
    }

    [Fact]
    public void SurrogateAdjacency_KeepsValidUtf16Boundaries()
    {
        const string before = "\uD83D\uDE00 x";
        const string after = "\uD83D\uDE00x";
        TextPairCharacterization result = TextWhitespace.Characterize(before, after);

        TextWhitespaceEdit edit = Assert.Single(result.Edits);
        Assert.Equal(TextWhitespaceEditKinds.Separation, edit.Kinds);
        Assert.Equal(new TextPosition(0, 2), edit.BeforeStart);
        Assert.False(char.IsLowSurrogate(before[edit.BeforeStart.Column]));
    }

    [Fact]
    public void Changed_IssuesNoEdits()
    {
        TextPairCharacterization result = TextWhitespace.Characterize("a  b", "a  c");

        Assert.Equal(TextPairOutcome.Changed, result.Outcome);
        Assert.Empty(result.Edits);
    }

    static void AssertSingleKind(string before, string after, TextWhitespaceEditKinds kind)
        => AssertSingleKinds(before, after, kind, expectedEdits: 1);

    static void AssertSingleKinds(string before, string after, TextWhitespaceEditKinds kind, int expectedEdits)
    {
        TextPairCharacterization result = TextWhitespace.Characterize(before, after);

        Assert.Equal(TextPairOutcome.WhitespaceOnly, result.Outcome);
        Assert.Equal(expectedEdits, result.Edits.Length);
        Assert.All(result.Edits, edit => Assert.Equal(kind, edit.Kinds));
    }
}
