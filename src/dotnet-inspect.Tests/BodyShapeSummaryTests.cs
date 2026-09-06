using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.Decompiler;
using Markout;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class BodyShapeSummaryTests
{
    static string FixturePath => typeof(BodyShapeFixture).Assembly.Location;

    public BodyShapeSummaryTests() => NuGetCache.Initialize("dotnet-inspect");

    [Fact]
    public void Summary_GroupsExactTextAcrossAndWithinMembersInFirstOccurrenceOrder()
    {
        BodyShapeMatch[] matches =
        [
            Match("new object()", "First", 0),
            Match("new()", "Second", 0),
            Match("new object()", "First", 1),
            Match("new object()", "Third", 0),
            Match("new Object()", "Third", 1),
        ];

        var summary = BodyShapeSummary.FromMatches(matches);

        Assert.Equal(
            [("new object()", 3), ("new()", 1), ("new Object()", 1)],
            summary.Select(row => (row.Match, row.Count)));
        Assert.Equal(matches.Length, summary.Sum(row => row.Count));
        Assert.All(summary, row => Assert.Equal("ObjectCreationExpression", row.Kind));
    }

    [Fact]
    public void Summary_GroupsBeforePresentationEscaping()
    {
        var summary = BodyShapeSummary.FromMatches(
        [
            Match("\"a|b\"", "First", 0) with { Kind = "LiteralExpression" },
            Match("\"a|b\"", "Second", 0) with { Kind = "LiteralExpression" },
            Match("\"A|B\"", "Third", 0) with { Kind = "LiteralExpression" },
        ]);

        Assert.Equal([2, 1], summary.Select(row => row.Count));
        Assert.Equal(["\"a|b\"", "\"A|B\""], summary.Select(row => row.Match));
    }

    [Fact]
    public void EmptySummary_RetainsExplicitEmptyState()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Fixture.dll",
            BodyShapeSearchResult = new BodyShapeSearchResult([], [], 0),
        };
        string output = MarkoutSerializer.Serialize(
            new LibraryInspectionView(inspection),
            InspectionContext.Default,
            new MarkoutWriterOptions { IncludeSections = [SectionNames.BodyShapeSummary] });

        Assert.Contains("## Body Shape Summary", output);
        Assert.Contains("No matching body shapes found.", output);
    }

    [Fact]
    public void FailedSearch_IsNotASuccessfulEmptySummary()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Fixture.dll",
            BodyShapesQueryResult = new BodyShapesResult.Failed(new IOException("unreadable body")),
            BodyShapeSections = new HashSet<string> { SectionNames.BodyShapeSummary },
        };

        Assert.Null(inspection.BodyShapeSummary);
        Assert.Null(new LibraryInspectionView(inspection).BodyShapeSummarySection);
        Assert.Contains(inspection.InspectionFailures!, failure => failure.Reason == "unreadable body");
        Assert.Equal(1, LibraryCommand.SelectedInspectionFailureExitCode(
            new LibraryOptions { IncludeSections = [SectionNames.BodyShapeSummary] },
            LibrarySections.CreatePipeline(),
            inspection));
    }

    [Fact]
    public async Task LibrarySummary_PreservesCountsAcrossProjectionAndRowWindow()
    {
        var complete = await Library("--json");
        Assert.Equal(0, complete.ExitCode);
        using var document = JsonDocument.Parse(complete.Output);
        var groups = document.RootElement.GetProperty("body_shape_summary");
        Assert.NotEmpty(groups.EnumerateArray());
        Assert.False(document.RootElement.TryGetProperty("body_shapes", out _));
        var first = groups[0];
        Assert.True(first.GetProperty("count").GetInt32() > 0);

        var projected = await Library("--columns", "Match;Count", "--rows", "1", "--jsonl");
        Assert.Equal(0, projected.ExitCode);
        using var row = JsonDocument.Parse(projected.Output);
        Assert.Equal(first.GetProperty("match").GetString(), row.RootElement.GetProperty("match").GetString());
        Assert.Equal(first.GetProperty("count").ToString(), row.RootElement.GetProperty("count").ToString());
        Assert.Equal(2, row.RootElement.EnumerateObject().Count());

        var count = await Library("--columns", "Match", "--rows", "1", "--count");
        Assert.Equal(0, count.ExitCode);
        Assert.Equal("1", count.Output.Trim());
    }

    [Fact]
    public async Task LibraryBothViews_ExposeTheSameOccurrenceEvidence()
    {
        var result = await Run(
            "library", FixturePath, "-S", "Body Shapes,Body Shape Summary",
            "--where", "Kind=ObjectCreationExpression", "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var occurrences = document.RootElement.GetProperty("body_shapes");
        var groups = document.RootElement.GetProperty("body_shape_summary");
        var expected = occurrences.EnumerateArray()
            .GroupBy(row => (row.GetProperty("kind").GetString(), row.GetProperty("text").GetString()))
            .Select(group => (group.Key, group.Count()));
        var actual = groups.EnumerateArray()
            .Select(row => ((row.GetProperty("kind").GetString(), row.GetProperty("match").GetString()),
                row.GetProperty("count").GetInt32()));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LibrarySummary_FiltersMethodsBeforeGrouping()
    {
        var result = await Run(
            "library", FixturePath, "-S", "Body Shapes,Body Shape Summary",
            "--where", "Kind=ArrayCreationExpression", "--where", "Shape=small-array", "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var occurrences = document.RootElement.GetProperty("body_shapes");
        var groups = document.RootElement.GetProperty("body_shape_summary");
        Assert.NotEmpty(groups.EnumerateArray());
        Assert.Equal(occurrences.GetArrayLength(),
            groups.EnumerateArray().Sum(row => row.GetProperty("count").GetInt32()));
        Assert.Contains(occurrences.EnumerateArray(),
            row => row.GetProperty("method_name").GetString() == nameof(BodyShapeFixture.PublicSmallArray));
        Assert.False(document.RootElement.TryGetProperty("performance", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibrarySummary_RequiresKindBeforeInspection(bool effective)
    {
        var result = await Run(
            ["library", FixturePath, effective ? "-D" : "-S", SectionNames.BodyShapeSummary,
                .. effective ? new[] { "--effective" } : []]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("requires --where \"Kind=<C# Body Kinds ID>\"", result.Error);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("type")]
    [InlineData("member")]
    public async Task SummaryQueryDiscovery_UsesExistingKindContractWithoutInspection(string command)
    {
        var result = await Run(command, "--package", "/missing/summary.nupkg",
            "-Q", SectionNames.BodyShapeSummary, "--json");
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        var section = Assert.Single(document.RootElement.GetProperty("sections").EnumerateArray());
        Assert.Equal(SectionNames.BodyShapeSummary, section.GetProperty("section").GetString());
        Assert.Equal("Kind", section.GetProperty("facets")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task LibrarySummary_DiscoveryDescribesGroupedColumns()
    {
        var structural = await Run("library", "-D", SectionNames.BodyShapeSummary, "--schema");
        Assert.Equal(0, structural.ExitCode);
        Assert.Contains("| Count |", structural.Output);
        Assert.DoesNotContain("| Member |", structural.Output);

        var effective = await Run("library", FixturePath, "-D", SectionNames.BodyShapeSummary,
            "--effective", "--where", "Kind=ObjectCreationExpression");
        Assert.Equal(0, effective.ExitCode);
        Assert.Contains("| Count |", effective.Output);
    }

    static BodyShapeMatch Match(string text, string member, int line)
        => new("Fixture", member, "Fixture.Type", member, 0x06000001,
            "ObjectCreationExpression", new PrintedExtent(line, 0, line, text.Length), text);

    static Task<(int ExitCode, string Output, string Error)> Library(params string[] extra)
        => Run(["library", FixturePath, "-S", SectionNames.BodyShapeSummary,
            "--where", "Kind=ObjectCreationExpression", .. extra]);

    static Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
        => ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });
}
