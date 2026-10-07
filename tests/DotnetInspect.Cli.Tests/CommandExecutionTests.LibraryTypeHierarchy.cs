using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Presentation;
using DotnetInspector.Sections;
using InertText;

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

    [Fact]
    public async Task Library_EmptyPublicPopulation_RendersAsEmpty()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-hierarchy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string path = Path.Combine(tempDir, "Empty.Library.dll");
            WriteReferenceFixtureAssembly(path, "Empty.Library");

            var (exit, output, error) = await RunAppAsync("library", path);

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(
                "Empty.Library 1.0.0.0 (no public types)",
                output.TrimEnd());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("markdown")]
    [InlineData("mermaid")]
    public async Task Library_EnvironmentFormat_KeepsStandardViewUnlessTreeIsExplicit(
        string format)
    {
        string? original =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", format);
            var environment = await RunAppAsync(
                "library",
                LibraryTypeHierarchyFixturePath);
            var explicitTree = await RunAppAsync(
                "library",
                LibraryTypeHierarchyFixturePath,
                "--tree");

            Assert.DoesNotContain(
                "World.Blue.Nodes (1 type)",
                environment.Output,
                StringComparison.Ordinal);
            Assert.False(
                environment.Output.StartsWith(
                    "DotnetInspector.Fixtures ",
                    StringComparison.Ordinal));
            Assert.Equal(0, explicitTree.Exit);
            Assert.StartsWith(
                "DotnetInspector.Fixtures ",
                explicitTree.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                original);
        }
    }

    [Theory]
    [InlineData("--tree")]
    [InlineData("--mermaid")]
    public async Task Library_MultiLibraryPackage_ExplicitHierarchyFails(
        string gesture)
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var bare = await RunAppAsync("library", "--package", packagePath);
            var (exit, output, error) = await RunAppAsync(
                "library",
                "--package",
                packagePath,
                gesture);

            Assert.Equal(0, bare.Exit);
            Assert.Contains("Latest.One.dll", bare.Output, StringComparison.Ordinal);
            Assert.Contains("Latest.Two.dll", bare.Output, StringComparison.Ordinal);
            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                LibraryCommandPlanner.OneLibraryError,
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Library_TypeHierarchyRowsFailures_AreVisible()
    {
        LibraryTypePopulationRowsOutcome[] failures =
        [
            new LibraryTypePopulationRowsOutcome.Read(
                LibraryTypePopulationOrdering.Metadata,
                [],
                new LibraryTypePopulationContinuation(
                    new InertString(TextPolicy.Field, "receipt"))),
            new LibraryTypePopulationRowsOutcome.Incomplete(
                LibraryTypePopulationRowsBound.RetainedDeclarations,
                Limit: 10,
                Measured: 11),
            new LibraryTypePopulationRowsOutcome.Unavailable(
                LibraryTypePopulationRowsUnavailableReason
                    .UnsupportedModuleExport),
            new LibraryTypePopulationRowsOutcome.Rejected(
                LibraryTypePopulationRowsRejection.InvalidContinuation),
            new LibraryTypePopulationRowsOutcome.Failed(
                LibraryTypePopulationRowsFailure.MalformedMetadata),
        ];

        Assert.All(
            failures,
            failure => Assert.NotNull(
                LibraryTypeHierarchyCommand.DescribeRowsFailure(failure)));
        Assert.NotNull(LibraryTypeHierarchyCommand.DescribeRowsFailure(null));
        Assert.Null(
            LibraryTypeHierarchyCommand.DescribeRowsFailure(
                new LibraryTypePopulationRowsOutcome.Read(
                    LibraryTypePopulationOrdering.Metadata,
                    [],
                    Continuation: null)));
    }
}
