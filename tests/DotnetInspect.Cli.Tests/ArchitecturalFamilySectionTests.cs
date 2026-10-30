using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;

using ILInspector.Research.NameFamilyFixtures;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ArchitecturalFamilySectionTests
{
    private static string FixturePath =>
        typeof(CustomerValidator).Assembly.Location;

    [Fact]
    public async Task ExactFamilySelection_RendersComposedRoleRows()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.ArchitecturalFamilies],
                Markdown = true,
                NameFamilyRowSelection =
                    RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(3)]),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Architectural Families", result.Output);
        Assert.Contains("| `Validator` | one word | 8 |", result.Output);
        Assert.Contains("No Role", result.Output);
        Assert.Equal(
            3,
            result.Output.Split('\n').Count(
                static line => line.StartsWith("| `", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ExactTypeSelection_RendersExactSupportRows()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.ArchitecturalFamilyTypes],
                Markdown = true,
                NameFamilyRowSelection =
                    RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(5)]),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Architectural Family Types", result.Output);
        Assert.Contains(
            "`ILInspector.Research.NameFamilyFixtures.Validator`",
            result.Output);
        Assert.Contains("Type Key", result.Output);
        Assert.Equal(
            5,
            result.Output.Split('\n').Count(
                static line => line.StartsWith("| `", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CountsMatchCompleteFamilyAndTypePopulations()
    {
        var content = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.ArchitecturalFamilies],
                JsonOutput = true,
            }));
        var familyCount = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.ArchitecturalFamilies],
                Count = true,
            }));
        var typeCount = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.ArchitecturalFamilyTypes],
                Count = true,
            }));

        Assert.Equal(0, content.ExitCode);
        Assert.Equal(0, familyCount.ExitCode);
        Assert.Equal(0, typeCount.ExitCode);
        using JsonDocument document = JsonDocument.Parse(content.Output);
        JsonElement all = Assert.Single(
            document.RootElement
                .GetProperty("populations")
                .EnumerateArray(),
            static population =>
                population.GetProperty("kind").GetString()
                    == "AllTypes");
        Assert.Equal(
            all.GetProperty("families").GetArrayLength(),
            int.Parse(familyCount.Output.Trim()));
        Assert.Equal(
            all.GetProperty("typeCount").GetInt32(),
            int.Parse(typeCount.Output.Trim()));
    }

    [Theory]
    [InlineData(SectionNames.ArchitecturalFamilies, 20)]
    [InlineData(SectionNames.ArchitecturalFamilyTypes, 22)]
    public async Task MultiSectionCount_UsesQuerySpaceCount(
        string roleSection,
        int expectedCount)
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections =
                [
                    roleSection,
                    SectionNames.References,
                ],
                Count = true,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            $"| {roleSection} | {expectedCount} |",
            result.Output);
    }

    [Theory]
    [InlineData(SectionNames.ArchitecturalFamilies)]
    [InlineData(SectionNames.ArchitecturalFamilyTypes)]
    public async Task MultiSectionCount_AppliesSharedRowWindow(
        string roleSection)
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections =
                [
                    roleSection,
                    SectionNames.References,
                ],
                Count = true,
                Rows = RowWindow.Range(2, 3),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains($"| {roleSection} | 2 |", result.Output);
    }

    [Fact]
    public async Task ContentJson_EqualsEnvelopeContent()
    {
        var content = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.ArchitecturalFamilies],
                JsonOutput = true,
            }));
        var envelope = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.ArchitecturalFamilies],
                EnvelopeOutput = true,
            }));

        Assert.Equal(0, content.ExitCode);
        Assert.Equal(0, envelope.ExitCode);
        using JsonDocument contentDocument =
            JsonDocument.Parse(content.Output);
        using JsonDocument envelopeDocument =
            JsonDocument.Parse(envelope.Output);
        Assert.True(
            JsonElement.DeepEquals(
                contentDocument.RootElement,
                envelopeDocument.RootElement.GetProperty("content")));
        Assert.Equal(
            "architectural-families",
            envelopeDocument.RootElement
                .GetProperty("result_kind")
                .GetString());
    }

    [Fact]
    public async Task BothRoleRowScopes_AreRejectedVisibly()
    {
        (int exitCode, string _, string error) =
            await ConsoleCapture.RunAsync(
                () => LibraryCommand.ExecuteAsync(
                    new LibraryOptions
                    {
                        AssemblyName = FixturePath,
                        IncludeSections =
                        [
                            SectionNames.ArchitecturalFamilies,
                            SectionNames.ArchitecturalFamilyTypes,
                        ],
                        Markdown = true,
                    }));

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "cannot be selected together",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "one row scope",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeRows_CanRenderWithAnUnrelatedSection()
    {
        (int exitCode, string output, string error) =
            await ConsoleCapture.RunAsync(
                () => LibraryCommand.ExecuteAsync(
                    new LibraryOptions
                    {
                        AssemblyName = FixturePath,
                        IncludeSections =
                        [
                            SectionNames.LibraryInfo,
                            SectionNames.ArchitecturalFamilyTypes,
                        ],
                        Markdown = true,
                    }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("## Library Info", output);
        Assert.Contains("## Architectural Family Types", output);
        Assert.Contains(
            "`ILInspector.Research.NameFamilyFixtures.Validator`",
            output);
    }
}
