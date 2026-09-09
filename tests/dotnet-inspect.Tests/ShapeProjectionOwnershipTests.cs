namespace DotnetInspector.Tests;

public class ShapeProjectionOwnershipTests
{
    /// <summary>
    /// Non-vacuity gate for structured count ownership: production count paths must not recover
    /// row cardinality from rendered Markdown.
    /// </summary>
    [Fact]
    public void CountProjection_DoesNotParseRenderedMarkdown()
    {
        string productRoot = Path.Combine(FindRepositoryRoot(), "src", "dotnet-inspect");
        var source = Directory
            .EnumerateFiles(productRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(source, text =>
            text.Contains("CountMarkdownTableRows", StringComparison.Ordinal));
        Assert.DoesNotContain(source, text =>
            text.Contains("CountMarkdownTableRowsBySection", StringComparison.Ordinal));
        Assert.DoesNotContain(
            "MarkdownScan",
            File.ReadAllText(Path.Combine(productRoot, "Output", "CountOutput.cs")),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-vacuity gate for printable-row ownership: row selection and payload acquisition stay on
    /// the typed printable contracts and do not depend on a Markout render.
    /// </summary>
    [Fact]
    public void PrintProjection_DoesNotDependOnMarkoutRendering()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "dotnet-inspect",
            "Output",
            "PrintProjectionOutput.cs"));

        Assert.Contains("PrintableRow", source, StringComparison.Ordinal);
        Assert.Contains("PrintableDocument", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Markout", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
