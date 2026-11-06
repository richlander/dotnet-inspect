using System.IO.Compression;
using System.Text;

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
        Assert.Equal("DotnetInspector.Fixtures.dll (File)", lines[0]);
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
                "Empty.Library.dll (File) (no public types)",
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
                    "DotnetInspector.Fixtures.dll (File)",
                    StringComparison.Ordinal));
            Assert.Equal(0, explicitTree.Exit);
            Assert.StartsWith(
                "DotnetInspector.Fixtures.dll (File)",
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
    [InlineData(null)]
    [InlineData("--tree")]
    [InlineData("--mermaid")]
    public async Task Library_MultiLibraryPackage_HierarchyShowsFirstLibraryWithWarning(
        string? gesture)
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            string[] gestures = gesture is null ? [] : [gesture];
            var unnarrowed = await RunAppAsync(
                ["library", "--package", packagePath, .. gestures]);
            var first = await RunAppAsync(
            [
                "library",
                "--package",
                packagePath,
                "--first-library",
                .. gestures,
            ]);

            Assert.Equal(0, unnarrowed.Exit);
            Assert.Equal(0, first.Exit);
            Assert.Equal(
                $"Warning: {packagePath} has 2 libraries; one is shown. "
                    + $"Use `dotnet-inspect package {packagePath}` to list them.",
                unnarrowed.Error.Trim());
            Assert.Empty(first.Error);
            Assert.NotEmpty(unnarrowed.Output);
            Assert.DoesNotContain(
                "Library Info",
                unnarrowed.Output,
                StringComparison.Ordinal);
            Assert.Equal(first.Output, unnarrowed.Output);
            Assert.Contains(
                "Latest.One.dll (Test.LibraryFiles@1.0.0; File; net10.0; lib)",
                unnarrowed.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Library_MultiLibraryPackage_WarningGestureCarriesTfm()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "--package",
                packagePath,
                "--tfm",
                "net10.0");

            Assert.Equal(0, exit);
            Assert.NotEmpty(output);
            Assert.StartsWith(
                "Latest.One.dll (Test.LibraryFiles@1.0.0; File; net10.0; lib)",
                output,
                StringComparison.Ordinal);
            Assert.Equal(
                $"Warning: {packagePath} has 2 libraries; one is shown. "
                    + $"Use `dotnet-inspect package {packagePath} --tfm net10.0` "
                    + "to list them.",
                error.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("Older.dll", "net8.0")]
    [InlineData("Latest.Two.dll", "net10.0")]
    public async Task Library_PackageTitle_FollowsExactAssetAndTarget(
        string libraryName,
        string target)
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var result = await RunAppAsync(
                "library", libraryName, "--package", packagePath, "--tfm", target);

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.StartsWith(
                $"{libraryName} (Test.LibraryFiles@1.0.0; File; {target}; lib)",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackageAndLibrary_CompactTitles_UseResolvedSignalRCoordinate(
        bool mermaid)
    {
        string[] format = mermaid ? ["--mermaid"] : [];
        var package = await RunAppAsync(
            "package", "Microsoft.Azure.SignalR@1.33.1");
        var library = await RunAppAsync(
            ["library", "--package", "Microsoft.Azure.SignalR@1.33.1", .. format]);

        Assert.Equal(0, package.Exit);
        Assert.Empty(package.Error);
        Assert.Equal(0, library.Exit);
        Assert.Equal(
            "Warning: Microsoft.Azure.SignalR@1.33.1 has 2 libraries; one is shown. "
                + "Use `dotnet-inspect package Microsoft.Azure.SignalR@1.33.1` to list them.",
            library.Error.Trim());
        const string packageTitle =
            "Microsoft.Azure.SignalR@1.33.1 (NuGet; net8.0; lib)";
        const string libraryTitle =
            "Microsoft.Azure.SignalR.dll (Microsoft.Azure.SignalR@1.33.1; NuGet; net8.0; lib)";
        Assert.Equal(packageTitle, package.Output.Split('\n')[0].TrimEnd());
        if (mermaid)
        {
            Assert.Contains(libraryTitle, library.Output, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(libraryTitle, library.Output.Split('\n')[0].TrimEnd());
        }
    }

    [Fact]
    public async Task Library_LocalToolShell_WarningNamesSiblingPayload()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"package-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string shell = Path.Combine(tempDir, "Test.Shell.1.0.0.nupkg");
            string payload = Path.Combine(tempDir, "Test.Shell.any.1.0.0.nupkg");
            WriteToolFixturePackage(
                shell,
                "Test.Shell",
                ("tools/any/any/DotnetToolSettings.xml", Encoding.UTF8.GetBytes("""
                    <DotNetCliTool Version="2">
                      <Commands><Command Name="test-shell" /></Commands>
                      <RuntimeIdentifierPackages>
                        <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="Test.Shell.any" />
                      </RuntimeIdentifierPackages>
                    </DotNetCliTool>
                    """)));
            byte[] assembly = File.ReadAllBytes(TestAssemblyPath);
            WriteToolFixturePackage(
                payload,
                "Test.Shell.any",
                ("tools/net10.0/any/DotnetToolSettings.xml", Encoding.UTF8.GetBytes("""
                    <DotNetCliTool Version="1">
                      <Commands><Command Name="test-shell" EntryPoint="One.dll" Runner="dotnet" /></Commands>
                    </DotNetCliTool>
                    """)),
                ("tools/net10.0/any/One.dll", assembly),
                ("tools/net10.0/any/Two.dll", assembly));

            var (exit, output, error) = await RunAppAsync(
                "library",
                "--package",
                shell);

            Assert.True(exit == 0, error);
            Assert.StartsWith(
                "One.dll (Test.Shell.any@1.0.0; File; net10.0; tools)",
                output,
                StringComparison.Ordinal);
            Assert.Equal(
                $"Warning: {payload} has 2 libraries; one is shown. "
                    + $"Use `dotnet-inspect package {payload}` to list them.",
                error.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void WriteToolFixturePackage(
        string path,
        string id,
        params (string Path, byte[] Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(
            archive.CreateEntry($"{id}.nuspec").Open()))
        {
            writer.Write($"""
                <package><metadata>
                  <id>{id}</id><version>1.0.0</version>
                  <authors>Fixture</authors><description>Tool shell fixture</description>
                  <packageTypes><packageType name="DotnetTool" /></packageTypes>
                </metadata></package>
                """);
        }
        foreach ((string entryPath, byte[] content) in entries)
        {
            using Stream stream = archive.CreateEntry(entryPath).Open();
            stream.Write(content);
        }
    }

    [Fact]
    public async Task Library_MultiLibraryPackage_ExplicitSectionsKeepAggregate()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "--package",
                packagePath,
                "-S",
                "Library Info");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Latest.One.dll", output, StringComparison.Ordinal);
            Assert.Contains("Latest.Two.dll", output, StringComparison.Ordinal);
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
