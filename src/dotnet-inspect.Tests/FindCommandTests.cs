using DotnetInspector.Commands;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for FindCommand output formatting.
/// </summary>
public class FindCommandTests
{
    [Fact]
    public void FormatOneLineOutput_Flat_ReturnsSpaceSeparatedNames()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Option*"] = [
                new TypeSearchResult { TypeName = "Option" },
                new TypeSearchResult { TypeName = "OptionResult" }
            ],
            ["Command*"] = [
                new TypeSearchResult { TypeName = "Command" },
                new TypeSearchResult { TypeName = "CommandResult" }
            ]
        };

        var output = FindCommand.FormatOneLineOutput(results, grouped: false);

        Assert.Equal("Command CommandResult Option OptionResult", output);
    }

    [Fact]
    public void FormatOneLineOutput_Grouped_ReturnsGroupedLines()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Option*"] = [
                new TypeSearchResult { TypeName = "Option" },
                new TypeSearchResult { TypeName = "OptionResult" }
            ],
            ["Command*"] = [
                new TypeSearchResult { TypeName = "Command" }
            ]
        };

        var output = FindCommand.FormatOneLineOutput(results, grouped: true);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(2, lines.Length);
        Assert.Equal("Option*: Option, OptionResult", lines[0]);
        Assert.Equal("Command*: Command", lines[1]);
    }

    [Fact]
    public void FormatOneLineOutput_Flat_DeduplicatesTypes()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["*Option*"] = [
                new TypeSearchResult { TypeName = "Option" },
                new TypeSearchResult { TypeName = "VersionOption" }
            ],
            ["Version*"] = [
                new TypeSearchResult { TypeName = "VersionOption" },
                new TypeSearchResult { TypeName = "Version" }
            ]
        };

        var output = FindCommand.FormatOneLineOutput(results, grouped: false);

        // Should be deduplicated and sorted
        Assert.Equal("Option Version VersionOption", output);
    }

    [Fact]
    public void FormatOneLineOutput_Flat_SortsAlphabetically()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["*"] = [
                new TypeSearchResult { TypeName = "Zebra" },
                new TypeSearchResult { TypeName = "Alpha" },
                new TypeSearchResult { TypeName = "Middle" }
            ]
        };

        var output = FindCommand.FormatOneLineOutput(results, grouped: false);

        Assert.Equal("Alpha Middle Zebra", output);
    }

    [Fact]
    public void FormatOneLineOutput_EmptyResults_ReturnsEmptyString()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["NoMatch*"] = []
        };

        var output = FindCommand.FormatOneLineOutput(results, grouped: false);

        Assert.Equal("", output);
    }

    [Fact]
    public void FormatNameOnlyOutput_ReturnsOnePerLine()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["*"] = [
                new TypeSearchResult { TypeName = "TypeA" },
                new TypeSearchResult { TypeName = "TypeB" },
                new TypeSearchResult { TypeName = "TypeC" }
            ]
        };

        var output = FindCommand.FormatNameOnlyOutput(results);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Equal("TypeA", lines[0]);
        Assert.Equal("TypeB", lines[1]);
        Assert.Equal("TypeC", lines[2]);
    }

    [Fact]
    public void FormatNameOnlyOutput_DeduplicatesAndSorts()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Pattern1"] = [
                new TypeSearchResult { TypeName = "Zebra" },
                new TypeSearchResult { TypeName = "Alpha" }
            ],
            ["Pattern2"] = [
                new TypeSearchResult { TypeName = "Alpha" },
                new TypeSearchResult { TypeName = "Beta" }
            ]
        };

        var output = FindCommand.FormatNameOnlyOutput(results);
        var lines = output.Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Alpha", lines[0]);
        Assert.Equal("Beta", lines[1]);
        Assert.Equal("Zebra", lines[2]);
    }

    [Fact]
    public void FormatNameOnlyOutput_EmptyResults_ReturnsEmptyString()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["NoMatch*"] = []
        };

        var output = FindCommand.FormatNameOnlyOutput(results);

        Assert.Equal("", output);
    }

    [Fact]
    public void FormatOneLineOutput_Grouped_SortsWithinGroups()
    {
        var results = new Dictionary<string, List<TypeSearchResult>>
        {
            ["Test*"] = [
                new TypeSearchResult { TypeName = "TestZ" },
                new TypeSearchResult { TypeName = "TestA" },
                new TypeSearchResult { TypeName = "TestM" }
            ]
        };

        var output = FindCommand.FormatOneLineOutput(results, grouped: true);

        Assert.Equal("Test*: TestA, TestM, TestZ", output);
    }
}
