using DotnetInspector.CommandLine;
using DotnetInspector.Fixtures;
using DotnetInspector.Models;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.Decompiler;
using Markout;
using System.Text.Json;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class BodyShapesSectionTests
{
    static string FixturePath => typeof(BodyShapeFixture).Assembly.Location;

    [Fact]
    public async Task LibraryKindPredicate_AutoSelectsBodyShapesSection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        Assert.Contains("## Body Shapes", result.Output, StringComparison.Ordinal);
        Assert.Contains("ObjectCreationExpression", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            nameof(BodyShapeFixture.PublicCreation),
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryKindPredicate_UsesOrdinaryJsonlProjection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--columns",
                    "Kind;Token",
                    "--rows",
                    "1",
                    "--jsonl",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        using var row = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "ObjectCreationExpression",
            row.RootElement.GetProperty("kind").GetString());
        Assert.StartsWith(
            "0x06",
            row.RootElement.GetProperty("token").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(2, row.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task LibraryKindPredicate_IncludesMatchesInStructuredJson()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=ObjectCreationExpression",
                    "--json",
                ])
                .InvokeAsync());

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Error:", result.Error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(result.Output);
        var matches = document.RootElement.GetProperty("body_shapes");
        Assert.NotEmpty(matches.EnumerateArray());
        Assert.All(
            matches.EnumerateArray(),
            match => Assert.Equal(
                "ObjectCreationExpression",
                match.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task EffectiveDiscovery_RequiresKindBeforeRunningBodyShapes()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(CommandLineBuilder.PreprocessArgs(
                [
                    "library",
                    FixturePath,
                    "-D",
                    "@Decompiler",
                    "--effective",
                ]))
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Could not read library",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyBodyShapesResult_RendersExplicitEmptyState()
    {
        var inspection = new LibraryInspection
        {
            FileName = "Fixture.dll",
            BodyShapeSearchResult = new BodyShapeSearchResult([], [], 0),
        };

        string output = MarkoutSerializer.Serialize(
            new LibraryInspectionView(inspection),
            InspectionContext.Default,
            new MarkoutWriterOptions
            {
                IncludeSections = [SectionNames.BodyShapes],
            });

        Assert.Contains("## Body Shapes", output, StringComparison.Ordinal);
        Assert.Contains(
            "No matching body shapes found.",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BodyShapesSelection_RequiresKindPredicate()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(["library", FixturePath, "-S", "Body Shapes"]).InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "requires --where \"Kind=<C# Body Kinds ID>\"",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BodyKindPredicate_DoesNotLeakIntoAnotherSelectedSection()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "-S",
                    "Library Info",
                    "--where",
                    "Kind=LiteralExpression",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "targets section 'Body Shapes'",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BodyKindPredicate_RejectsPerformancePredicatesInSameQuery()
    {
        var root = CommandLineBuilder.CreateRootCommand();

        var result = await ConsoleCapture.RunAsync(() =>
            root.Parse(
                [
                    "library",
                    FixturePath,
                    "--where",
                    "Kind=LiteralExpression",
                    "--where",
                    "Confidence=high",
                ])
                .InvokeAsync());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "cannot be combined with Performance Triage",
            result.Error,
            StringComparison.Ordinal);
    }
}
