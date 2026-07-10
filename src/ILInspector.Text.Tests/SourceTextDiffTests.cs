using ILInspector.Findings;

namespace ILInspector.Text.Tests;

public class SourceTextDiffTests
{
    static readonly FindingSubject Subject = new("document", "Document");

    [Fact]
    public void Inspect_ProjectsExactContentAndSequentialPositions()
    {
        var findings = SourceTextDiff.Inspect("first\r\n second\nthird", Subject).ToArray();

        Assert.Equal(["first", " second", "third"], findings.Select(f => f.Payload));
        Assert.Equal([0, 1, 2], findings.Select(f => f.Position));
        Assert.All(findings, finding =>
        {
            Assert.Same(Subject, finding.Subject);
            Assert.Same(SourceTextDiff.LineDescriptor, finding.Descriptor);
            Assert.Equal(finding.Payload, finding.Key.IdentityKey);
        });
    }

    [Fact]
    public void Inspect_IsLazyAndCanStopAfterTheFirstLine()
    {
        var findings = SourceTextDiff.Inspect("first\nsecond\nthird", Subject);

        Assert.False(findings is IReadOnlyCollection<Finding<string>>);
        var first = Assert.Single(findings.Take(1));
        Assert.Equal("first", first.Payload);
    }

    [Fact]
    public void EmptyAndWhitespaceOnlyText_AreValidCensuses()
    {
        Assert.Empty(SourceTextDiff.Inspect("", Subject));

        var whitespace = Assert.Single(SourceTextDiff.Inspect(" \t", Subject));
        Assert.Equal(" \t", whitespace.Payload);
        Assert.Equal(0, whitespace.Position);
    }

    [Fact]
    public void CrLfCrAndLf_AreEquivalentLogicalBoundaries()
    {
        var expected = SourceTextDiff.Inspect("one\ntwo\nthree\n", Subject).ToArray();
        var actual = SourceTextDiff.Inspect("one\r\ntwo\rthree\n", Subject).ToArray();

        Assert.Equal(
            expected.Select(f => f.Payload),
            actual.Select(f => f.Payload));
        Assert.True(SourceTextDiff.Compare("one\ntwo\nthree\n", "one\r\ntwo\rthree\r", Subject).IsExact);
    }

    [Fact]
    public void IdenticalText_IsAllPresentAndIdentical()
    {
        var result = SourceTextDiff.Compare("alpha\nbeta", "alpha\nbeta", Subject);

        Assert.True(result.IsExact);
        Assert.All(result.Pairs, pair =>
            Assert.True(pair is PairFinding<string>.Present
            {
                Difference: FindingDifferenceKind.None
            }));
        Assert.Equal(DiffShape.Identical, FindingSummary.Summarize(result.Pairs).Shape);
    }

    [Fact]
    public void Compare_ReportsAddedAndRemovedLines()
    {
        var result = SourceTextDiff.Compare("shared\nremoved", "shared\nadded", Subject);

        Assert.False(result.IsExact);
        Assert.Equal(1, result.Pairs.Count(pair => pair is PairFinding<string>.Present));
        var removed = Assert.Single(
            result.Pairs.Where(pair => pair is PairFinding<string>.Removed)) switch
        {
            PairFinding<string>.Removed value => value,
            _ => throw new InvalidOperationException(),
        };
        var added = Assert.Single(
            result.Pairs.Where(pair => pair is PairFinding<string>.Added)) switch
        {
            PairFinding<string>.Added value => value,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal("removed", removed.Old.Payload);
        Assert.Equal("added", added.New.Payload);
    }

    [Fact]
    public void ReorderedContiguousBlock_IsReportedAsMoved()
    {
        const string oldText = "A\nB\nC\nmoved-one\nmoved-two\nD\nE";
        const string newText = "moved-one\nmoved-two\nA\nB\nC\nD\nE";

        var result = SourceTextDiff.Compare(oldText, newText, Subject);

        Assert.Equal(2, result.Pairs.Count(pair => pair.Difference == FindingDifferenceKind.Moved));
        Assert.All(result.Pairs, pair => Assert.Equal(PairKind.Present, pair.Kind));
        Assert.Equal(DiffShape.ReorderOnly, FindingSummary.Summarize(result.Pairs).Shape);
    }

    [Fact]
    public void WhitespaceDifference_IsStructuralText()
    {
        var result = SourceTextDiff.Compare("value ", "value", Subject);

        Assert.False(result.IsExact);
        Assert.Equal(1, result.Pairs.Count(pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, result.Pairs.Count(pair => pair.Kind == PairKind.Removed));
        Assert.All(
            result.Pairs,
            pair => Assert.Equal(FindingDifferenceKind.None, pair.Difference));
    }

    [Fact]
    public void FinalNewline_IsRepresentedByAFinalEmptyLogicalLine()
    {
        var withoutNewline = SourceTextDiff.Inspect("value", Subject).ToArray();
        var withNewline = SourceTextDiff.Inspect("value\n", Subject).ToArray();

        Assert.Equal(["value"], withoutNewline.Select(f => f.Payload));
        Assert.Equal(["value", ""], withNewline.Select(f => f.Payload));

        var result = SourceTextDiff.Compare("value", "value\n", Subject);
        var added = Assert.Single(
            result.Pairs.Where(pair => pair is PairFinding<string>.Added)) switch
        {
            PairFinding<string>.Added value => value,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal("", added.New.Payload);
    }

    [Theory]
    [InlineData(
        "int M()\n{\n    return 1;\n}",
        "public int M()\n{\n    return this.value;\n}",
        "    return this.value;")]
    [InlineData(
        "# Project\n\nOld summary\n\n## Usage",
        "# Project\n\nNew summary\n\n## Usage",
        "New summary")]
    public void Compare_IsDomainNeutral(string oldText, string newText, string expectedNewLine)
    {
        var result = SourceTextDiff.Compare(oldText, newText, Subject);

        Assert.Equal(DiffShape.Structural, FindingSummary.Summarize(result.Pairs).Shape);
        Assert.Contains(
            result.Pairs,
            pair => pair is PairFinding<string>.Added added
                && added.New.Payload == expectedNewLine);
    }

    [Fact]
    public void NullInputOrSubject_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SourceTextDiff.Inspect(null!, Subject));
        Assert.Throws<ArgumentNullException>(() => SourceTextDiff.Inspect("", null!));
        Assert.Throws<ArgumentNullException>(() => SourceTextDiff.Compare(null!, "", Subject));
        Assert.Throws<ArgumentNullException>(() => SourceTextDiff.Compare("", null!, Subject));
        Assert.Throws<ArgumentNullException>(() => SourceTextDiff.Compare("", "", null!));
    }

    [Fact]
    public void Result_RejectsInvalidPublicStates()
    {
        var valid = SourceTextDiff.Compare("", "", Subject);

        Assert.Throws<ArgumentException>(() =>
            new SourceTextDiffResult(default, valid.Match, valid.OldAtoms, valid.NewAtoms));
        Assert.Throws<ArgumentNullException>(() =>
            new SourceTextDiffResult(valid.Pairs, null!, valid.OldAtoms, valid.NewAtoms));
        Assert.Throws<ArgumentException>(() =>
            new SourceTextDiffResult(
                valid.Pairs,
                new FindingMatch(default, []),
                valid.OldAtoms,
                valid.NewAtoms));
        Assert.Throws<ArgumentException>(() =>
            new SourceTextDiffResult(
                valid.Pairs,
                new FindingMatch([], default),
                valid.OldAtoms,
                valid.NewAtoms));
        Assert.Throws<ArgumentException>(() =>
            new SourceTextDiffResult(valid.Pairs, valid.Match, default, valid.NewAtoms));
        Assert.Throws<ArgumentException>(() =>
            new SourceTextDiffResult(valid.Pairs, valid.Match, valid.OldAtoms, default));
    }
}
