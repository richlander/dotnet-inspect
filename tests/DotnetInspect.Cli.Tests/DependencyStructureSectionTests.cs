using System.CommandLine;
using System.Globalization;
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

    private static readonly RootCommand Root =
        CommandLineBuilder.CreateRootCommand();

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
        Assert.Contains("cycle 1 of 1", result.Output);
        string edges = result.Output[
            result.Output.IndexOf(
                "### Namespace Edges",
                StringComparison.Ordinal)..];
        Assert.Equal(
            3,
            edges.Split('\n').Count(
                static line => line.StartsWith("| ", StringComparison.Ordinal)
                    && !line.StartsWith("| From ", StringComparison.Ordinal)
                    && !line.StartsWith("| ---- ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Markdown_StatesCycleCountAndMembership()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                Markdown = true,
                DependencyStructureRowSelection =
                    RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(1)]),
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        // The fixture's unresolved calli qualifies the document, so the
        // count carries its evidence qualification. Edge selection never
        // narrows the count or the membership.
        Assert.Contains(
            "| Cycles | 1 among admitted call evidence |",
            result.Output);
        Assert.Contains("### Cycle Members", result.Output);
        Assert.Contains(
            "| cycle 1 of 1 | 3 | Alpha, Beta, Gamma |",
            result.Output);
        Assert.DoesNotContain("cycle 0", result.Output);
    }

    [Fact]
    public async Task DefaultRepresentation_IsTheTypedEdgeTable()
    {
        // Through the command line, so the native-shape policy selects the
        // representation exactly as it does for a user.
        var table = await ConsoleCapture.RunAsync(
            () =>
            {
                string[] args = CommandLineBuilder.PreprocessArgs(
                    [
                        "library",
                        FixturePath,
                        "-S",
                        SectionNames.DependencyStructure,
                    ],
                    Root);
                return CommandLineBuilder.InvokeAsync(
                    Root.Parse(args),
                    args);
            });
        var json = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                JsonOutput = true,
            }));

        Assert.Equal(0, table.ExitCode);
        Assert.Equal(0, json.ExitCode);
        // The fixture's unresolved calli qualifies the document; Table
        // demand discloses that as a diagnostic, not as a row.
        Assert.Contains(
            "Dependency Structure is qualified",
            table.Error);
        string[] lines = table.Output.TrimEnd('\n').Split('\n');
        Assert.Equal(
            "from\tto\tcalls\tfunction_references\tfrom_level"
                + "\tfrom_cycle\tto_level\tto_cycle",
            lines[0]);
        Assert.DoesNotContain("Cycles", table.Output);
        Assert.DoesNotContain("cycle 1 of", table.Output);

        // One row per namespace edge. Cycle numbers are 1-based and match
        // the Document's zero-based cycleIndex.
        using JsonDocument document = JsonDocument.Parse(json.Output);
        JsonElement root = document.RootElement;
        Assert.Equal(
            root.GetProperty("namespaceEdges").GetArrayLength(),
            lines.Length - 1);
        Dictionary<string, string> cycles = root
            .GetProperty("namespaces")
            .EnumerateArray()
            .ToDictionary(
                node => node.GetProperty("namespace").GetString()
                    is { Length: > 0 } name
                        ? name
                        : "<global namespace>",
                node => node.GetProperty("cycleIndex") is
                    { ValueKind: JsonValueKind.Number } index
                        ? (index.GetInt32() + 1).ToString(
                            CultureInfo.InvariantCulture)
                        : "");
        Assert.Contains(cycles.Values, static cycle => cycle == "1");
        Assert.Contains(
            lines.Skip(1),
            static line => line.Contains(
                "<global namespace>",
                StringComparison.Ordinal));
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split('\t');
            Assert.Equal(8, cells.Length);
            if (cycles.TryGetValue(cells[0], out string? fromCycle))
                Assert.Equal(fromCycle, cells[5]);
            if (cycles.TryGetValue(cells[1], out string? toCycle))
                Assert.Equal(toCycle, cells[7]);
        }
    }

    [Fact]
    public async Task Jsonl_CarriesOnlyEdgeRecords()
    {
        var result = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = FixturePath,
                IncludeSections = [SectionNames.DependencyStructure],
                Tabular = true,
                TabularExplicitlySet = true,
                Jsonl = true,
            }));

        Assert.Equal(0, result.ExitCode);
        string[] records = result.Output.TrimEnd('\n').Split('\n');
        Assert.NotEmpty(records);
        foreach (string record in records)
        {
            using JsonDocument row = JsonDocument.Parse(record);
            Assert.Equal(
                [
                    "from", "to", "calls", "function_references",
                    "from_level", "from_cycle", "to_level", "to_cycle",
                ],
                row.RootElement.EnumerateObject().Select(
                    static property => property.Name));
        }
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
            "Alpha (1 type, level 1, cycle 1 of 1)",
            result.Output);
        Assert.Contains(
            "|\"1 relationship (1 call, 0 function references)\"|",
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
