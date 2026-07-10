using ILInspector.Findings;

namespace ILInspector.Text.Tests;

public class TextFindingsTests
{
    static readonly FindingSubject Subject = new("document", "Document");

    [Fact]
    public void Inspect_ProjectsExactContentAndSequentialPositions()
    {
        var findings = TextFindings.Inspect("first\r\n second\nthird", Subject).ToArray();

        Assert.Equal(["first", " second", "third"], findings.Select(f => f.Payload));
        Assert.Equal([0, 1, 2], findings.Select(f => f.Ordinal));
        Assert.All(findings, finding =>
        {
            Assert.Same(Subject, finding.Subject);
            Assert.Same(TextFindings.LineDescriptor, finding.Descriptor);
            Assert.Equal(finding.Payload, finding.Key.IdentityKey);
        });
    }

    [Fact]
    public void Inspect_IsLazyAndCanStopAfterTheFirstLine()
    {
        var findings = TextFindings.Inspect("first\nsecond\nthird", Subject);

        Assert.False(findings is IReadOnlyCollection<Finding<string>>);
        var first = Assert.Single(findings.Take(1));
        Assert.Equal("first", first.Payload);
    }

    [Fact]
    public void EmptyAndWhitespaceOnlyText_AreValidCensuses()
    {
        Assert.Empty(TextFindings.Inspect("", Subject));

        var whitespace = Assert.Single(TextFindings.Inspect(" \t", Subject));
        Assert.Equal(" \t", whitespace.Payload);
        Assert.Equal(0, whitespace.Ordinal);
    }

    [Fact]
    public void CrLfCrAndLf_AreEquivalentLogicalBoundaries()
    {
        var expected = TextFindings.Inspect("one\ntwo\nthree\n", Subject).ToArray();
        var actual = TextFindings.Inspect("one\r\ntwo\rthree\n", Subject).ToArray();

        Assert.Equal(
            expected.Select(f => f.Payload),
            actual.Select(f => f.Payload));
        Assert.True(Compare("one\ntwo\nthree\n", "one\r\ntwo\rthree\r").IsExact);
    }

    [Fact]
    public void IdenticalText_IsAllPresentAndIdentical()
    {
        var result = Compare("alpha\nbeta", "alpha\nbeta");

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
        var result = Compare("shared\nremoved", "shared\nadded");

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

        var result = Compare(oldText, newText);

        Assert.Equal(2, result.Pairs.Count(pair => pair.Difference == FindingDifferenceKind.Moved));
        Assert.All(result.Pairs, pair => Assert.Equal(PairKind.Present, pair.Kind));
        Assert.Equal(DiffShape.ReorderOnly, FindingSummary.Summarize(result.Pairs).Shape);
    }

    [Fact]
    public void WhitespaceDifference_IsStructuralText()
    {
        var result = Compare("value ", "value");

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
        var withoutNewline = TextFindings.Inspect("value", Subject).ToArray();
        var withNewline = TextFindings.Inspect("value\n", Subject).ToArray();

        Assert.Equal(["value"], withoutNewline.Select(f => f.Payload));
        Assert.Equal(["value", ""], withNewline.Select(f => f.Payload));

        var result = Compare("value", "value\n");
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
        var result = Compare(oldText, newText);

        Assert.Equal(DiffShape.Structural, FindingSummary.Summarize(result.Pairs).Shape);
        Assert.Contains(
            result.Pairs,
            pair => pair is PairFinding<string>.Added added
                && added.New.Payload == expectedNewLine);
    }

    [Fact]
    public void NullInputOrSubject_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => TextFindings.Inspect(null!, Subject));
        Assert.Throws<ArgumentNullException>(() => TextFindings.Inspect("", null!));
        Assert.Throws<ArgumentNullException>(() => TextFindings.Compare(null!, "", Subject));
        Assert.Throws<ArgumentNullException>(() => TextFindings.Compare("", null!, Subject));
        Assert.Throws<ArgumentNullException>(() => TextFindings.Compare("", "", null!));
    }

    [Fact]
    public void Compare_NormalizesTotalCensusesToCompleteInspections()
    {
        var comparison = TextFindings.Compare("", "", Subject);
        var complete = CompleteComparison(comparison);

        Assert.True(comparison is FindingComparison<string>.Complete);
        Assert.True(complete.OldInspection is FindingInspection<string>.Complete);
        Assert.True(complete.NewInspection is FindingInspection<string>.Complete);
        Assert.Empty(complete.Pairs);
        Assert.True(complete.IsExact);
    }

    static FindingComparison<string>.Complete Compare(string oldText, string newText)
        => CompleteComparison(TextFindings.Compare(oldText, newText, Subject));

    static FindingComparison<string>.Complete CompleteComparison(
        FindingComparison<string> comparison)
        => comparison switch
        {
            FindingComparison<string>.Complete complete => complete,
            FindingComparison<string>.Failed failed => throw new InvalidOperationException(
                $"Expected a completed text comparison: {failed.Failure}"),
        };
}
