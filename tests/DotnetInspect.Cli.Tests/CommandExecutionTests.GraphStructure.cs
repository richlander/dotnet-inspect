using System.Text.Json;
using DotnetInspector.Fixtures;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    static string DependencyStructureFixture() =>
        FixtureCatalog.ResearchDependencyStructure.AssemblyPath();

    [Fact]
    public async Task GraphStructure_DefaultViewShowsLevelsCyclesAndNamespaceEdges()
    {
        var (exit, output, error) = await RunAppAsync(
            "graph", "structure", "--library", DependencyStructureFixture());

        Assert.True(exit == 0, error);
        Assert.Contains("# Dependency Structure", output);
        Assert.Contains("Qualified: 1 unresolved calls and 0 diagnosed bodies", output);
        Assert.Contains("## Namespaces", output);
        Assert.Contains("## Namespace Edges", output);
        Assert.Contains("## Cycles", output);
        Assert.Contains("| 1 | 3 | Alpha, Beta, Gamma |", output);
        Assert.Contains("| 2 | Delta |", output);
        Assert.Contains("&lt;global&gt;", output);
        Assert.DoesNotContain("## Type Edges", output);
        Assert.DoesNotContain("## External Dependencies", output);
    }

    [Fact]
    public async Task GraphStructure_EnvelopeCarriesCompleteDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "graph", "structure", "--library", DependencyStructureFixture(), "--envelope");

        Assert.True(exit == 0, error);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement content = json.RootElement.GetProperty("content");
        Assert.Equal("qualified", content.GetProperty("completeness").GetString());
        Assert.Equal(
            "library-dependency-structure.v1",
            content.GetProperty("methodologyVersion").GetString());
        JsonElement cycle = Assert.Single(content.GetProperty("cycles").EnumerateArray());
        Assert.Equal(
            ["Alpha", "Beta", "Gamma"],
            cycle.EnumerateArray().Select(static ns => ns.GetString()));
        Assert.NotEmpty(content.GetProperty("typeEdges").EnumerateArray());
        Assert.NotEmpty(content.GetProperty("externalNamespaceEdges").EnumerateArray());
        Assert.Equal(
            "nonProjectable",
            json.RootElement.GetProperty("share").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GraphStructure_SelectsSectionsAndRequiresOneForTables()
    {
        var (exit, output, error) = await RunAppAsync(
            "graph", "structure", "--library", DependencyStructureFixture(),
            "-S", "Type Edges", "--tsv");

        Assert.True(exit == 0, error);
        Assert.Contains("BoxUser.User\tBoxes.Box`1\t4\t0", output);

        var (tableExit, _, tableError) = await RunAppAsync(
            "graph", "structure", "--library", DependencyStructureFixture(), "--tsv");
        Assert.Equal(1, tableExit);
        Assert.Contains("Tabular output requires exactly one section", tableError);

        var (unknownExit, _, unknownError) = await RunAppAsync(
            "graph", "structure", "--library", DependencyStructureFixture(), "-S", "Nope");
        Assert.Equal(1, unknownExit);
        Assert.Contains("Unknown section 'Nope'", unknownError);
    }

    [Fact]
    public async Task GraphStructure_RequiresExactlyOneSource()
    {
        var (exit, _, error) = await RunAppAsync("graph", "structure");

        Assert.Equal(1, exit);
        Assert.Contains("Specify exactly one of --library or --package", error);
    }
}
