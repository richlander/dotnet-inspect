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
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SectionCountEntry<string>("alpha", -1));
    }
}
