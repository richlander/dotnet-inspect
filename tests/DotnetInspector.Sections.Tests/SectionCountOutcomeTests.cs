namespace DotnetInspector.Sections.Tests;

public sealed class SectionCountOutcomeTests
{
    [Fact]
    public void ResultBranchesPreserveRowSetIdentityAndEvidence()
    {
        var counts = new[]
        {
            new SectionCountEntry<string>("alpha", 2),
        };
        var completed =
            new SectionCountOutcome<string, string>.Completed(counts);
        counts[0] = new("replacement", 9);

        SectionCountEntry<string> count =
            Assert.Single(completed.Counts);
        Assert.Equal("alpha", count.Identity);
        Assert.Equal(2, count.Value);
        Assert.True(count.IsExact);
        Assert.Empty(completed.Sources);

        var observedEvidence = new[]
        {
            new SectionCountSourceEvidence<string, string>(
                "alpha",
                "candidate limit"),
        };
        var observed =
            new SectionCountOutcome<string, string>.Completed(
                [new("alpha", 3, isExact: false)],
                observedEvidence);
        observedEvidence[0] = new("alpha", "replacement");

        Assert.False(Assert.Single(observed.Counts).IsExact);
        Assert.Equal(
            "candidate limit",
            Assert.Single(observed.Sources).Evidence);

        var sources = new[]
        {
            new SectionCountSourceEvidence<string, string>(
                "alpha",
                "incomplete"),
        };
        var sourceFailure =
            new SectionCountOutcome<string, string>.SourceForCount(
                sources);
        sources[0] = new("replacement", "complete");

        SectionCountSourceEvidence<string, string> source =
            Assert.Single(sourceFailure.Sources);
        Assert.Equal("alpha", source.Identity);
        Assert.Equal("incomplete", source.Evidence);

        var semantic =
            new SectionCountOutcome<string, string>.Semantic(
                "alpha",
                stageNumber: 2,
                requiredPosition: 4,
                availableCount: 3);
        Assert.Equal("alpha", semantic.Identity);
        Assert.Equal(2, semantic.StageNumber);
        Assert.Equal(4, semantic.RequiredPosition);
        Assert.Equal(3, semantic.AvailableCount);
    }

    [Fact]
    public void ResultBranchesRejectMalformedEntries()
    {
        Assert.Throws<ArgumentException>(
            () => new SectionCountOutcome<string, string>.Completed([]));
        Assert.Throws<ArgumentException>(
            () => new SectionCountOutcome<string, string>.Completed(
                [
                    new("alpha", 1),
                    new("alpha", 2),
                ]));
        Assert.Throws<ArgumentException>(
            () => new SectionCountOutcome<string, string>.SourceForCount(
                [
                    new("alpha", "first"),
                    new("alpha", "second"),
                ]));
        Assert.Throws<ArgumentException>(
            () => new SectionCountOutcome<string, string>.Completed(
                [new("alpha", 1), new("beta", 2)],
                [new("alpha", "complete")]));
        Assert.Throws<ArgumentException>(
            () => new SectionCountOutcome<string, string>.Completed(
                [new("alpha", 1)],
                [new("beta", "complete")]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SectionCountEntry<string>("alpha", -1));
    }
}
