using DotnetInspector.Output;

namespace DotnetInspector.Tests;

// SelectOutput writes to Console.Error directly; without the Console
// collection these tests run in parallel with ConsoleCapture-based tests
// and their stderr lands in whichever capture is active (CI flake:
// Member_NonMethodRows saw "No sections match 'Source*'").
[Collection("Console")]
public class SelectResolverTests
{
    private static readonly string[] TestSections =
    [
        "Package Info",
        "Statistics",
        "Dependencies",
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
        var result = SelectResolver.ResolveSelectAsSections(["Package Info"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Contains("Package Info", result.Sections);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ResolveSelect_GlobMatch_ReturnsSections()
    {
        var result = SelectResolver.ResolveSelectAsSections(["Pack*"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Contains("Package Info", result.Sections);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ResolveSelect_AllSelector_ReturnsAllSections()
    {
        var result = SelectResolver.ResolveSelectAsSections(["@All"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Empty(result.Unresolved);
        Assert.Equal(TestSections.OrderBy(s => s), result.Sections!.OrderBy(s => s));
    }

    [Fact]
    public void ResolveSelect_DefaultSelector_ReturnsDefaultSections()
    {
        var result = SelectResolver.ResolveSelectAsSections(["@Default"], TestSections, ["Package Info"]);

        Assert.NotNull(result.Sections);
        Assert.Empty(result.Unresolved);
        Assert.Equal(["Package Info"], result.Sections!.ToArray());
    }

    [Fact]
    public void ResolveSelect_CustomCategory_ReturnsCategorySections()
    {
        Dictionary<string, string[]> categories = new(StringComparer.OrdinalIgnoreCase)
        {
            ["@Signals"] = ["Vulnerabilities", "Statistics"]
        };

        var result = SelectResolver.ResolveSelectAsSections(["@Signals"], TestSections, ["Package Info"], categories);

        Assert.NotNull(result.Sections);
        Assert.Empty(result.Unresolved);
        Assert.Equal(["Statistics", "Vulnerabilities"], result.Sections!.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void ResolveSelect_PlainAllIsNotSpecial()
    {
        var result = SelectResolver.ResolveSelectAsSections(["All"], TestSections);

        Assert.Null(result.Sections);
        Assert.Single(result.Unresolved);
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
        var result = SelectResolver.ResolveSelectAsSections(["Package Info", "Source*"], TestSections);

        Assert.NotNull(result.Sections);
        Assert.Contains("Package Info", result.Sections);
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
        Assert.Contains("Package Info", result.Unresolved[0].Suggestions);
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
    public async Task WriteUnresolved_PartialMatch_ReturnsFalse()
    {
        var result = new SelectResult(
            new HashSet<string> { "Package Info" },
            [new SelectMiss("Source*", TestSections.ToList(), IsGlob: true)]);

        // Partial match: some resolved, some not — should not be a total failure.
        // Captured so the warning doesn't leak to the real console.
        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SelectOutput.WriteUnresolved(result) ? 1 : 0));

        Assert.Equal(0, exit);
        Assert.Contains("Warning: No sections match 'Source*'.", error);
    }

    [Fact]
    public async Task WriteUnresolved_TotalFailure_ReturnsTrue()
    {
        var result = new SelectResult(
            null,
            [new SelectMiss("Source*", TestSections.ToList(), IsGlob: true)]);

        // Total failure: nothing matched — should be a hard error.
        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => Task.FromResult(SelectOutput.WriteUnresolved(result) ? 1 : 0));

        Assert.Equal(1, exit);
        Assert.Contains("Error: No sections match 'Source*'.", error);
        Assert.Contains("Available sections:", error);
    }
}
