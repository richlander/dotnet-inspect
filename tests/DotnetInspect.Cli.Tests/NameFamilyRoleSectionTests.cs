using System.Text.Json;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;

using ILInspector.Research.NameFamilyFixtures;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class NameFamilyRoleSectionTests
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
                IncludeSections = [SectionNames.NameFamilyRoles],
                Markdown = true,
                NameFamilyRowSelection =
                    RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(3)]),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Name Family Roles", result.Output);
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
                IncludeSections = [SectionNames.NameFamilyRoleTypes],
                Markdown = true,
                NameFamilyRowSelection =
                    RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(5)]),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Name Family Role Types", result.Output);
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
                IncludeSections = [SectionNames.NameFamilyRoles],
                JsonOutput = true,
            }));
        var familyCount = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilyRoles],
                Count = true,
            }));
        var typeCount = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilyRoleTypes],
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

    [Fact]
    public async Task ContentJson_EqualsEnvelopeContent()
    {
        var content = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilyRoles],
                JsonOutput = true,
            }));
        var envelope = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.NameFamilyRoles],
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
            "library-family-roles",
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
                            SectionNames.NameFamilyRoles,
                            SectionNames.NameFamilyRoleTypes,
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
                            SectionNames.NameFamilyRoleTypes,
                        ],
                        Markdown = true,
                    }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("## Library Info", output);
        Assert.Contains("## Name Family Role Types", output);
        Assert.Contains(
            "`ILInspector.Research.NameFamilyFixtures.Validator`",
            output);
    }
}
