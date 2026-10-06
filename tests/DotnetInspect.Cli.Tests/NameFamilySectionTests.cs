using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;

using ILInspector.Research.NameFamilyFixtures;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class NameFamilySectionTests
{
    private static string FixturePath =>
        typeof(CustomerValidator).Assembly.Location;

    [Fact]
    public async Task ExactSelection_RendersQuerySelectedFamilyRows()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilies],
                Markdown = true,
                NameFamilyRowSelection =
                    RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(5)]),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Name Families", result.Output);
        Assert.Contains("| `Validator` | one word | 8 |", result.Output);
        Assert.Equal(
            5,
            result.Output.Split('\n').Count(
                static line => line.StartsWith("| `", StringComparison.Ordinal)));
    }

    [Fact]
    public void NameFamilies_IsExcludedFromAutomaticSelection()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.DoesNotContain(
            SectionNames.NameFamilies,
            pipeline.GetCandidateSections(Verbosity.Detailed));
        Assert.Contains(
            SectionNames.NameFamilies,
            pipeline.GetCandidateSections(
                Verbosity.Minimal,
                [SectionNames.NameFamilies]));
    }

    [Fact]
    public async Task Count_EqualsCompleteFamilyPopulation()
    {
        var json = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilies],
                JsonOutput = true,
            }));
        var count = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilies],
                Count = true,
            }));

        Assert.Equal(0, json.ExitCode);
        Assert.Equal(0, count.ExitCode);
        using JsonDocument document = JsonDocument.Parse(json.Output);
        JsonElement all = Assert.Single(
            document.RootElement
                .GetProperty("populations")
                .EnumerateArray(),
            static population =>
                population.GetProperty("kind").GetString()
                    == "AllTypes");
        Assert.Equal(
            all.GetProperty("families").GetArrayLength(),
            int.Parse(count.Output.Trim()));
    }

    [Fact]
    public async Task ContentJson_EqualsEnvelopeContent()
    {
        var content = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilies],
                JsonOutput = true,
            }));
        var envelope = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilies],
                EnvelopeOutput = true,
            }));

        Assert.Equal(0, content.ExitCode);
        Assert.Equal(0, envelope.ExitCode);
        using JsonDocument contentDocument =
            JsonDocument.Parse(content.Output);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelope.Output);
        JsonElement envelopeContent =
            envelopeDocument.RootElement.GetProperty("content");
        Assert.True(
            JsonElement.DeepEquals(
                contentDocument.RootElement,
                envelopeContent));
        Assert.Equal(
            contentDocument.RootElement
                .GetProperty("receipt")
                .GetProperty("typeCount")
                .GetInt32(),
            contentDocument.RootElement
                .GetProperty("types")
                .GetArrayLength());
        Assert.Equal(
            5,
            contentDocument.RootElement
                .GetProperty("populations")
                .GetArrayLength());
        Assert.Equal(
            "library-name-families",
            envelopeDocument.RootElement
                .GetProperty("result_kind")
                .GetString());
    }
}
