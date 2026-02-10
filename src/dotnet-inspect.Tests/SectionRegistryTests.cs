using DotnetInspector.Views;

namespace DotnetInspector.Tests;

public class SectionRegistryTests
{
    private static readonly string[] TestSections =
    [
        "Library Info",
        "Symbols",
        "Extension Methods",
        "Custom Attributes",
    ];

    [Fact]
    public void Resolve_NullRequested_ReturnsNull()
    {
        var result = SectionRegistry.Resolve(TestSections, null, out var hasError);

        Assert.Null(result);
        Assert.False(hasError);
    }

    [Fact]
    public void Resolve_EmptySet_ReturnsEmpty()
    {
        var requested = new HashSet<string>();
        var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

        Assert.NotNull(result);
        Assert.Empty(result);
        Assert.False(hasError);
    }

    [Fact]
    public void Resolve_ExactMatch_ReturnsOriginalCasing()
    {
        var requested = new HashSet<string> { "Symbols" };
        var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Symbols", result);
        Assert.False(hasError);
    }

    [Fact]
    public void Resolve_CaseInsensitive_ReturnsOriginalCasing()
    {
        var requested = new HashSet<string> { "extension methods" };
        var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Extension Methods", result);
        Assert.False(hasError);
    }

    [Fact]
    public void Resolve_Wildcard_MatchesPrefix()
    {
        var requested = new HashSet<string> { "Extension*" };
        var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Extension Methods", result);
        Assert.False(hasError);
    }

    [Fact]
    public void Resolve_Wildcard_MatchesMultiple()
    {
        var requested = new HashSet<string> { "Custom*" };
        var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Custom Attributes", result);
        Assert.False(hasError);
    }

    [Fact]
    public void Resolve_WildcardCaseInsensitive_Matches()
    {
        var requested = new HashSet<string> { "extension*" };
        var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

        Assert.NotNull(result);
        Assert.Contains("Extension Methods", result);
        Assert.False(hasError);
    }

    [Fact]
    public void Resolve_UnknownSection_ReturnsNullWithError()
    {
        var requested = new HashSet<string> { "NotReal" };

        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

            Assert.Null(result);
            Assert.True(hasError);

            var output = stderr.ToString();
            Assert.Contains("Unknown section: NotReal", output);
            Assert.Contains("Available sections:", output);
            Assert.Contains("Library Info", output);
        }
        finally
        {
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
    }

    [Fact]
    public void Resolve_MixedValidAndInvalid_ReportsOnlyInvalid()
    {
        var requested = new HashSet<string> { "Symbols", "FakeSection" };

        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

            Assert.Null(result);
            Assert.True(hasError);
            Assert.Contains("FakeSection", stderr.ToString());
        }
        finally
        {
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
    }

    [Fact]
    public void Resolve_MultipleValidSections_ReturnsAll()
    {
        var requested = new HashSet<string> { "Symbols", "Library Info" };
        var result = SectionRegistry.Resolve(TestSections, requested, out var hasError);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains("Symbols", result);
        Assert.Contains("Library Info", result);
        Assert.False(hasError);
    }

    [Fact]
    public void LibrarySections_ContainsExpectedSections()
    {
        Assert.Contains("Library Info", SectionRegistry.LibrarySections);
        Assert.Contains("Symbols", SectionRegistry.LibrarySections);
        Assert.Contains("Extension Methods", SectionRegistry.LibrarySections);
        Assert.Contains("Custom Attributes", SectionRegistry.LibrarySections);
        Assert.Contains("Type Forwarders", SectionRegistry.LibrarySections);
    }

    [Fact]
    public void PackageCommandSections_ContainsExpectedSections()
    {
        Assert.Contains("Package", SectionRegistry.PackageCommandSections);
        Assert.Contains("Statistics", SectionRegistry.PackageCommandSections);
        Assert.Contains("Files", SectionRegistry.PackageCommandSections);
    }
}
