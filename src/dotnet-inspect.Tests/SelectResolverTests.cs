using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class SelectResolverTests
{
    private static readonly string[] TestSections =
    [
        "Package",
        "Statistics",
        "Package Dependencies",
        "Files",
        "Vulnerabilities",
    ];

    [Fact]
    public void ResolveSelect_NullSelect_ReturnsNullSections()
    {
        var result = SelectResolver.ResolveSelectAsSections(null, TestSections);

        Assert.Null(result.Sections);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ResolveSelect_ExactMatch_ReturnsSections()
    {
        var result = SelectResolver.ResolveSelectAsSections(["Package"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Contains("Package", result.Sections);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ResolveSelect_GlobMatch_ReturnsSections()
    {
        var result = SelectResolver.ResolveSelectAsSections(["Pack*"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Contains("Package", result.Sections);
        Assert.Contains("Package Dependencies", result.Sections);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ResolveSelect_GlobNoMatch_ReturnsUnresolved()
    {
        var result = SelectResolver.ResolveSelectAsSections(["Source*"], TestSections);

        Assert.Null(result.Sections);
        Assert.Single(result.Unresolved);
        Assert.Equal("Source*", result.Unresolved[0].Value);
        Assert.True(result.Unresolved[0].IsGlob);
    }

    [Fact]
    public void ResolveSelect_PartialMatch_ReturnsSectionsAndUnresolved()
    {
        var result = SelectResolver.ResolveSelectAsSections(["Package", "Source*"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Contains("Package", result.Sections);
        Assert.Single(result.Unresolved);
        Assert.True(result.Unresolved[0].IsGlob);
    }

    [Fact]
    public void ResolveSelect_ExactMiss_ReturnsUnresolvedWithSuggestions()
    {
        var result = SelectResolver.ResolveSelectAsSections(["Packge"], TestSections);

        Assert.Null(result.Sections);
        Assert.Single(result.Unresolved);
        Assert.False(result.Unresolved[0].IsGlob);
        Assert.Contains("Package", result.Unresolved[0].Suggestions);
    }

    [Fact]
    public void ResolveSelect_PartialExactMiss_ContinuesWithMatched()
    {
        var result = SelectResolver.ResolveSelectAsSections(["Files", "Foo"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Contains("Files", result.Sections);
        Assert.Single(result.Unresolved);
        Assert.Equal("Foo", result.Unresolved[0].Value);
    }

    [Fact]
    public void WriteUnresolved_PartialMatch_ReturnsFalse()
    {
        var result = new SelectResult(
            new HashSet<string> { "Package" },
            [new SelectMiss("Source*", TestSections.ToList(), IsGlob: true)]);

        // Partial match: some resolved, some not — should not be a total failure
        bool totalFailure = SelectOutput.WriteUnresolved(result);

        Assert.False(totalFailure);
    }

    [Fact]
    public void WriteUnresolved_TotalFailure_ReturnsTrue()
    {
        var result = new SelectResult(
            null,
            [new SelectMiss("Source*", TestSections.ToList(), IsGlob: true)]);

        // Total failure: nothing matched — should be a hard error
        bool totalFailure = SelectOutput.WriteUnresolved(result);

        Assert.True(totalFailure);
    }
}
