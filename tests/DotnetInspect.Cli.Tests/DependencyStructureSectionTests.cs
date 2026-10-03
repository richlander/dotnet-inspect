using System.Text.Json;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class DependencyStructureSectionTests
{
    private static string FixturePath =>
        typeof(GlobalType).Assembly.Location;

    [Fact]
    public async Task ExactSelection_RendersQuerySelectedNamespaceEdges()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                Markdown = true,
                DependencyStructureRowSelection =
                    RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(3)]),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("## Dependency Structure", result.Output);
        Assert.Contains(
            "&lt;global namespace&gt; (1 type, level 1)",
            result.Output);
        Assert.Contains("cycle 0", result.Output);
        Assert.Equal(
            3,
            result.Output.Split('\n').Count(
                static line => line.StartsWith("| ", StringComparison.Ordinal)
                    && !line.StartsWith("| From ", StringComparison.Ordinal)
                    && !line.StartsWith("| ---- ", StringComparison.Ordinal)));
    }

    [Fact]
    public void DependencyStructure_IsExcludedFromAutomaticSelection()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.DoesNotContain(
            SectionNames.DependencyStructure,
            pipeline.GetCandidateSections(Verbosity.Detailed));
        Assert.Contains(
            SectionNames.DependencyStructure,
            pipeline.GetCandidateSections(
                Verbosity.Minimal,
                [SectionNames.DependencyStructure]));
    }

    [Fact]
    public async Task Count_EqualsCompleteNamespaceEdgePopulation()
    {
        var json = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                JsonOutput = true,
            }));
        var count = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                Count = true,
            }));

        Assert.Equal(0, json.ExitCode);
        Assert.Equal(0, count.ExitCode);
        using JsonDocument document = JsonDocument.Parse(json.Output);
        Assert.Equal(
            document.RootElement
                .GetProperty("namespaceEdges")
                .GetArrayLength(),
            int.Parse(count.Output.Trim()));
    }

    [Fact]
    public async Task Mermaid_UsesTheSameStructuredGraph()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                Format = OutputFormat.Mermaid,
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.StartsWith("graph TD", result.Output);
        Assert.Contains(
            "Alpha (1 type, level 1, cycle 0)",
            result.Output);
        Assert.Contains(
            "|\"1 relationships (1 calls, 0 function references)\"|",
            result.Output);
    }

    [Fact]
    public async Task ContentJson_EqualsEnvelopeContent()
    {
        var content = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                JsonOutput = true,
            }));
        var envelope = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
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
            "library-dependency-structure",
            envelopeDocument.RootElement
                .GetProperty("result_kind")
                .GetString());
        Assert.Equal(
            contentDocument.RootElement
                .GetProperty("population")
                .GetProperty("namespaceCount")
                .GetInt32(),
            contentDocument.RootElement
                .GetProperty("namespaces")
                .GetArrayLength());
    }

    [Fact]
    public async Task CliEnvelope_RoutesToDependencyStructureTransport()
    {
        var result = await RunCliAsync(
            "library",
            FixturePath,
            "-S",
            SectionNames.DependencyStructure,
            "--envelope");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument envelope = JsonDocument.Parse(result.Output);
        Assert.Equal(
            "library-dependency-structure",
            envelope.RootElement
                .GetProperty("result_kind")
                .GetString());
    }

    private static Task<(int ExitCode, string Output, string Error)>
        RunCliAsync(params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });
}
