using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    private static readonly string LibraryTypeHierarchyFixturePath =
        typeof(World.Blue.Nodes.Foo).Assembly.Location;

    [Fact]
    public async Task Library_Bare_RendersTypeHierarchyTree()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            LibraryTypeHierarchyFixturePath);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string[] lines = output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
        Assert.StartsWith("DotnetInspector.Fixtures ", lines[0]);
        Assert.Contains(
            lines,
            line => line.EndsWith(
                "World.Blue.Nodes (1 type)",
                StringComparison.Ordinal));
        Assert.Contains(
            lines,
            line => line.EndsWith(
                "class Foo",
                StringComparison.Ordinal));
        Assert.Contains(
            lines,
            line => line.EndsWith(
                "World.Blue.Nodes.More (1 type)",
                StringComparison.Ordinal));
        Assert.DoesNotContain("member", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Library Info", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--tree")]
    [InlineData("-v:m")]
    public async Task Library_ExplicitTreeOrMinimal_MatchesBareTree(
        string gesture)
    {
        var bare = await RunAppAsync(
            "library",
            LibraryTypeHierarchyFixturePath);
        var explicitTree = await RunAppAsync(
            "library",
            LibraryTypeHierarchyFixturePath,
            gesture);

        Assert.Equal(0, explicitTree.Exit);
        Assert.Empty(explicitTree.Error);
        Assert.Equal(bare.Output, explicitTree.Output);
    }

    [Fact]
    public async Task Library_Mermaid_LowersTypeHierarchy()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            LibraryTypeHierarchyFixturePath,
            "--mermaid");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("graph TD", output, StringComparison.Ordinal);
        Assert.Contains("World.Blue.Nodes (1 type)", output, StringComparison.Ordinal);
        Assert.Contains("class Foo", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_TreeAndMermaid_FailsVisibly()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            LibraryTypeHierarchyFixturePath,
            "--tree",
            "--mermaid");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            LibraryCommandPlanner.TreeAndMermaidError,
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-S", "Library Info")]
    [InlineData("-v:n")]
    [InlineData("--json")]
    [InlineData("--markdown")]
    public async Task Library_CompetingDemand_KeepsStandardView(
        params string[] competing)
    {
        var (exit, output, _) = await RunAppAsync(
            ["library", LibraryTypeHierarchyFixturePath, .. competing]);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("World.Blue.Nodes (1 type)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_TreeWithCompetingDemand_KeepsSectionTreeContract()
    {
        var (exit, _, error) = await RunAppAsync(
            "library",
            LibraryTypeHierarchyFixturePath,
            "--tree",
            "-S",
            "Library Info");

        Assert.Equal(1, exit);
        Assert.Contains(
            "--tree requires exactly '-S \"Reference Hierarchy\"'.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_TypeHierarchyDefault_RequestsAndReturnsNoMemberCounts()
    {
        LibraryInspectionPlan plan =
            LibraryTypeHierarchyCommand.CreateInspectionPlan(
                LibraryTypeHierarchyPresentation.CreateDefaultPlan(
                    LibraryTypeHierarchyPresentationFormat.Tree));
        Assert.Null(plan.Types!.Rows!.MemberCount);

        InspectionEnvelope<LibraryInspectionOutcome>? envelope =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                LibraryTypeHierarchyFixturePath,
                "Library Type hierarchy work-bound test",
                session => session.Execute(plan, CancellationToken.None),
                CancellationToken.None);

        var available =
            Assert.IsType<LibraryInspectionOutcome.Available>(
                envelope?.Content);
        var rows =
            Assert.IsType<LibraryTypePopulationRowsOutcome.Read>(
                available.Document.Types!.Rows);
        Assert.Null(rows.Continuation);
        Assert.NotEmpty(rows.Items);
        Assert.All(rows.Items, row => Assert.Null(row.MemberCount));
    }
}
