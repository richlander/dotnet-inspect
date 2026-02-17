using DotnetInspector.Commands;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for FindCommand output formatting via OneLineWriter.
/// </summary>
public class FindCommandTests
{
    [Fact]
    public void OneLineWriter_MultiPattern_OutputsColumnarResults()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Pattern1"] = [
                new TypeSearchResult { TypeName = "Zebra", Namespace = "Animals", Kind = "class", Assembly = "Zoo" },
                new TypeSearchResult { TypeName = "Alpha", Namespace = "Greek", Kind = "struct", Assembly = "Letters" }
            ],
            ["Pattern2"] = [
                new TypeSearchResult { TypeName = "Beta", Namespace = "Greek", Kind = "interface", Assembly = "Letters" }
            ]
        };

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: false);
        new MarkoutContext().Serialize(view, writer);
        var lines = sw.ToString().TrimEnd().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Contains("Zebra", lines[0]);
        Assert.Contains("Alpha", lines[1]);
        Assert.Contains("Beta", lines[2]);
    }

    [Fact]
    public void OneLineWriter_EmptyResults_NoOutput()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["NoMatch*"] = []
        };

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: false);
        new MarkoutContext().Serialize(view, writer);

        Assert.Equal("", sw.ToString().TrimEnd());
    }

    [Fact]
    public void OneLineWriter_WithHeader_IncludesColumnHeaders()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Test*"] = [
                new TypeSearchResult { TypeName = "TestA", Namespace = "Ns", Kind = "class", Assembly = "Lib" }
            ]
        };

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: true);
        new MarkoutContext().Serialize(view, writer);
        var output = sw.ToString();

        Assert.Contains("TYPE", output);
        Assert.Contains("TestA", output);
    }

    [Fact]
    public void OneLineWriter_NoHeader_OmitsColumnHeaders()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Test*"] = [
                new TypeSearchResult { TypeName = "TestA", Namespace = "Ns", Kind = "class", Assembly = "Lib" }
            ]
        };

        var view = FindOutputFormatter.BuildMultiPatternView(results);
        var sw = new StringWriter();
        var writer = new OneLineWriter(sw, showHeader: false);
        new MarkoutContext().Serialize(view, writer);
        var output = sw.ToString();

        Assert.DoesNotContain("TYPE", output);
        Assert.Contains("TestA", output);
    }
}
