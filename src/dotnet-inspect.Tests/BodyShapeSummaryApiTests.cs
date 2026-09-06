using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class BodyShapeSummaryApiTests
{
    static string FixturePath => typeof(BodyShapeFixture).Assembly.Location;
    static string FixtureType => typeof(BodyShapeFixture).FullName!;

    public BodyShapeSummaryApiTests() => NuGetCache.Initialize("dotnet-inspect");

    [Fact]
    public async Task TypeSummary_GroupsCompleteEvidenceInFirstOccurrenceOrder()
    {
        var result = await Query("type", SectionNames.BodyShapeSummary, "--jsonl");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error);
        var rows = ParseRows(result.Output);
        Assert.Equal(
            [("new object()", "3"), ("new()", "1")],
            rows.Select(row => (row.GetProperty("match").GetString(), row.GetProperty("count").ToString())));
        Assert.All(rows, row =>
        {
            Assert.Equal("ObjectCreationExpression", row.GetProperty("kind").GetString());
            Assert.Equal(3, row.EnumerateObject().Count());
        });
    }

    [Fact]
    public async Task MemberSummary_MatchesOneLocatableOccurrence()
    {
        var summary = await Query("member", SectionNames.BodyShapeSummary, "--jsonl");
        var occurrences = await Query("member", SectionNames.BodyShapes, "--jsonl");

        Assert.Equal(0, summary.ExitCode);
        Assert.Equal(0, occurrences.ExitCode);
        var group = Assert.Single(ParseRows(summary.Output));
        var occurrence = Assert.Single(ParseRows(occurrences.Output));
        Assert.Equal("1", group.GetProperty("count").ToString());
        Assert.Equal(group.GetProperty("kind").GetString(), occurrence.GetProperty("kind").GetString());
        Assert.Equal("new object()", group.GetProperty("match").GetString());
        Assert.Equal(group.GetProperty("match").GetString(), occurrence.GetProperty("match").GetString());
        Assert.Contains(nameof(BodyShapeFixture.PublicCreation), occurrence.GetProperty("member").GetString());
        Assert.Equal(
            $"0x{typeof(BodyShapeFixture).GetMethod(nameof(BodyShapeFixture.PublicCreation))!.MetadataToken:X8}",
            occurrence.GetProperty("token").GetString());
        Assert.All(new[] { "start_line", "start_column", "end_line", "end_column" },
            name => Assert.True(int.Parse(occurrence.GetProperty(name).ToString()) >= 1));
        Assert.False(occurrence.TryGetProperty("il_offset", out _));
    }

    [Theory]
    [InlineData("type", SectionNames.BodyShapeSummary, "Kind", 2)]
    [InlineData("type", SectionNames.BodyShapes, "Kind", 4)]
    [InlineData("type", SectionNames.BodyShapes, "Kind;Match", 4)]
    [InlineData("member", SectionNames.BodyShapeSummary, "Kind", 1)]
    [InlineData("member", SectionNames.BodyShapes, "Kind", 1)]
    public async Task ColumnProjection_PreservesViewCardinality(
        string command, string section, string columns, int expected)
    {
        var result = await Query(command, section, "--columns", columns, "--jsonl");

        Assert.Equal(0, result.ExitCode);
        var rows = ParseRows(result.Output);
        Assert.Equal(expected, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal(columns.Split(';').Length, row.EnumerateObject().Count());
            Assert.Equal("ObjectCreationExpression", row.GetProperty("kind").GetString());
        });
    }

    [Fact]
    public async Task SummaryCountColumnProjection_KeepsGroupsWithEqualCounts()
    {
        var result = await Query("type", SectionNames.BodyShapeSummary,
            "--all", "--columns", "Count", "--jsonl");

        Assert.Equal(0, result.ExitCode);
        var rows = ParseRows(result.Output);
        Assert.Equal(3, rows.Length);
        Assert.Equal(2, rows.Count(row => row.GetProperty("count").ToString() == "1"));
        Assert.All(rows, row => Assert.Single(row.EnumerateObject()));
    }

    [Theory]
    [InlineData("1", "new object()", "3")]
    [InlineData("2..2", "new()", "1")]
    public async Task SummaryRowWindow_SelectsGroupsWithoutTruncatingCounts(
        string window, string match, string count)
    {
        var result = await Query("type", SectionNames.BodyShapeSummary,
            "--columns", "Match;Count", "--rows", window, "--jsonl");

        Assert.Equal(0, result.ExitCode);
        var row = Assert.Single(ParseRows(result.Output));
        Assert.Equal(match, row.GetProperty("match").GetString());
        Assert.Equal(count, row.GetProperty("count").ToString());
        Assert.Equal(2, row.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("type", null, "2")]
    [InlineData("type", "1", "1")]
    [InlineData("type", "2..2", "1")]
    [InlineData("member", null, "1")]
    public async Task SummaryCount_CountsSurvivingGroupsNotOccurrences(
        string command, string? window, string expected)
    {
        var result = await Query(command, SectionNames.BodyShapeSummary,
            ["--columns", "Count", "--count", "--json",
                .. window is null ? Array.Empty<string>() : ["--rows", window]]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output.Trim());
        using var document = JsonDocument.Parse(result.Output);
        Assert.Equal(expected, document.RootElement.ToString());
    }

    [Fact]
    public async Task TypeSummary_AppliesMemberFilterBeforeGrouping()
    {
        var summary = await Query("type", SectionNames.BodyShapeSummary,
            "--member", nameof(BodyShapeFixture.PublicCreation), "--jsonl");
        var occurrences = await Query("type", SectionNames.BodyShapes,
            "--member", nameof(BodyShapeFixture.PublicCreation), "--jsonl");

        Assert.Equal(0, summary.ExitCode);
        Assert.Equal(0, occurrences.ExitCode);
        var group = Assert.Single(ParseRows(summary.Output));
        var occurrence = Assert.Single(ParseRows(occurrences.Output));
        Assert.Equal("1", group.GetProperty("count").ToString());
        Assert.Equal(group.GetProperty("match").GetString(), occurrence.GetProperty("match").GetString());
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task BothViews_RenderTogetherButKindAloneKeepsOccurrenceDefault(string command)
    {
        var both = await Query(command, "Body*");
        var automatic = await Run(
            [.. Target(command), "--where", "Kind=ObjectCreationExpression"]);

        Assert.Equal(0, both.ExitCode);
        Assert.Contains("## Body Shapes", both.Output);
        Assert.Contains("## Body Shape Summary", both.Output);
        Assert.Equal(0, automatic.ExitCode);
        Assert.Contains("## Body Shapes", automatic.Output);
        Assert.DoesNotContain("## Body Shape Summary", automatic.Output);
    }

    [Theory]
    [InlineData("type", SectionNames.BodyShapeSummary, false)]
    [InlineData("member", SectionNames.BodyShapeSummary, false)]
    [InlineData("type", "Body*", false)]
    [InlineData("member", "Body*,Signature", false)]
    [InlineData("type", SectionNames.BodyShapeSummary, true)]
    [InlineData("member", SectionNames.BodyShapeSummary, true)]
    [InlineData("type", "Body*", true)]
    [InlineData("member", "Body*,Signature", true)]
    public async Task ExplicitBodySelection_RequiresKind(
        string command, string selector, bool effective)
    {
        var result = await Run(
            [.. Target(command), effective ? "-D" : "-S", selector]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("requires --where \"Kind=<C# Body Kinds ID>\"", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task MemberStructuralSummaryDiscovery_DoesNotAcquireTargetOrRequireKind()
    {
        var result = await Run(
            ["member", FixtureType, "--member", $"{nameof(BodyShapeFixture.PublicCreation)}:1",
                "--library", "missing-body-shape-summary.dll",
                "-D", SectionNames.BodyShapeSummary, "--schema"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("| Kind |", result.Output);
        Assert.Contains("| Match |", result.Output);
        Assert.Contains("| Count |", result.Output);
        Assert.DoesNotContain("| Member |", result.Output);
        Assert.DoesNotContain("Error:", result.Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task EffectiveSummaryDiscovery_UsesGroupedSchema(string command)
    {
        var result = await Run(
            [.. Target(command), "-D", SectionNames.BodyShapeSummary,
                "--where", "Kind=ObjectCreationExpression"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("| Count |", result.Output);
        Assert.DoesNotContain("| Member |", result.Output);
        Assert.DoesNotContain("Error:", result.Error);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task BareEffectiveDiscovery_DoesNotRunOrAdvertiseEitherBodyViewWithoutKind(string command)
    {
        var result = await Run([.. Target(command), "-D"]);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(SectionNames.BodyShapes, result.Output);
        Assert.DoesNotContain(SectionNames.BodyShapeSummary, result.Output);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task SummaryDocumentJson_FailsClosedInsteadOfDroppingEvidence(string command)
    {
        var result = await Query(command, SectionNames.BodyShapeSummary, "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Document --json cannot represent Body Shapes analysis.", result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("member")]
    public async Task EmptySummary_RetainsExplicitEmptyState(string command)
    {
        var result = await Run([.. Target(command), "-S", SectionNames.BodyShapeSummary,
            "--where", "Kind=FixedStatement"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Body Shape Summary", result.Output);
        Assert.Contains("No matching body shapes found.", result.Output);
    }

    [Fact]
    public void SummaryCatalogs_KeepExplicitOnlyMethodBackedRequirements()
    {
        var type = new ApiType { Name = "NoBodies", Kind = "class" };
        var pipelines = new[]
        {
            ApiMemberSectionDescriptors.CreatePipeline(),
            ApiMemberOverloadSectionDescriptors.CreatePipeline(),
            ApiMemberDetailSectionDescriptors.CreatePipeline(),
        };
        Assert.All(pipelines, pipeline =>
        {
            Assert.Contains(SectionNames.BodyShapeSummary, pipeline.SelectableSectionNames);
            Assert.DoesNotContain(SectionNames.BodyShapeSummary, pipeline.InfoSectionNames);
            Assert.DoesNotContain(
                pipeline.GetCategoryMap().Where(pair =>
                    pair.Key is not SectionPipeline<ApiType>.AllCategory
                        and not SectionPipeline<ApiType>.HiddenCategory),
                pair => pair.Value.Contains(SectionNames.BodyShapeSummary));
            Assert.DoesNotContain(SectionNames.BodyShapeSummary,
                pipeline.GetEffectiveSections(type, Verbosity.Detailed, [SectionNames.BodyShapeSummary]));
        });
        Assert.True(ApiMemberDetailSectionDescriptors.BodyShapeSummary.ExplicitOnly);
        Assert.False(ApiMemberDetailSectionDescriptors.BodyShapeSummary.ProbeEffectiveness);
        Assert.Equal(ApiMemberDetailSectionDescriptors.BodyShapes.Capabilities,
            ApiMemberDetailSectionDescriptors.BodyShapeSummary.Capabilities);
        Assert.Equal(
            ["Kind", "Match", "Count"],
            ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema()
                .GetSection(SectionNames.BodyShapeSummary)!.Items.Select(item => item.Name));
    }

    static string[] Target(string command, string? path = null)
        => [command, FixtureType,
            .. command == "member" ? new[] { nameof(BodyShapeFixture.PublicCreation) } : [],
            "--library", path ?? FixturePath];

    static Task<(int ExitCode, string Output, string Error)> Query(
        string command, string section, params string[] extra)
        => Run([.. Target(command), "-S", section,
            "--where", "Kind=ObjectCreationExpression", .. extra]);

    static JsonElement[] ParseRows(string output)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToArray();

    static Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
        => ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });
}
