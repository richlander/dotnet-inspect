using System.Text.Json;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class ImplementsCommandTests
{
    [Fact]
    public async Task ExecuteAsync_UsesWorkspaceQueryAndPreservesProvenance()
    {
        var options = new ImplementsOptions
        {
            TargetType = typeof(IWorkspaceImplementationMarker).FullName!,
            Assemblies = [typeof(ImplementsCommandTests).Assembly.Location],
            IncludeAll = true,
            JsonOutput = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => ImplementsCommand.ExecuteAsync(
                    options,
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            typeof(WorkspaceImplementation).FullName!,
            output);
        Assert.Contains(
            "\"source\": \"DotnetInspect.Cli.Tests.dll\"",
            output);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidAssemblyWarnsWithoutVerbose()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(
            path,
            "not a managed assembly",
            TestContext.Current.CancellationToken);
        try
        {
            var options = new ImplementsOptions
            {
                TargetType = typeof(IDisposable).FullName!,
                Assemblies = [path],
                JsonOutput = true,
            };

            var (exitCode, output, error) =
                await ConsoleCapture.RunAsync(
                    () => ImplementsCommand.ExecuteAsync(
                        options,
                        TestContext.Current.CancellationToken));

            Assert.Equal(0, exitCode);
            Assert.Equal("[]", output.Trim());
            Assert.Contains($"Error scanning {path}", error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_LegacyRowsFallbackWindowsRenderedOutputOnce(
        bool tabular)
    {
        var options = new ImplementsOptions
        {
            TargetType = typeof(IWorkspaceImplementationMarker).FullName!,
            Assemblies = [typeof(ImplementsCommandTests).Assembly.Location],
            IncludeAll = true,
            Rows = RowWindow.Range(2, 3),
            Tabular = tabular,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => ImplementsCommand.ExecuteAsync(
                    options,
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            2,
            CountRenderedWorkspaceImplementationRows(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_LegacyRowsFallbackWindowsRenderedTypeNameOrder(
        bool tabular)
    {
        var options = new ImplementsOptions
        {
            TargetType = typeof(ILegacyRenderedOrderingMarker).FullName!,
            Assemblies = [typeof(ImplementsCommandTests).Assembly.Location],
            IncludeAll = true,
            Rows = RowWindow.Range(1, 1),
            Tabular = tabular,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => ImplementsCommand.ExecuteAsync(
                    options,
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            typeof(LegacyRenderedA).FullName!,
            output);
        Assert.DoesNotContain(
            typeof(LegacyRenderedZ).FullName!,
            output);
    }

    [Fact]
    public async Task CommandLine_DefaultJsonPreservesDiscoveryOrderWithoutSemanticSelection()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        var result = await ExecuteCommandLineAsync(
            "implements",
            typeof(ILegacyRenderedOrderingMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            [
                typeof(LegacyRenderedZ).FullName!,
                typeof(LegacyRenderedA).FullName!,
            ],
            ReadJsonTypes(result.Output));
    }

    [Fact]
    public async Task CommandLine_TypeLimitPreservesDiscoveryOrderWithoutSemanticSelection()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        var result = await ExecuteCommandLineAsync(
            "implements",
            typeof(ILegacyRenderedOrderingMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json",
            "-t",
            "1");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            [typeof(LegacyRenderedZ).FullName!],
            ReadJsonTypes(result.Output));
    }

    [Fact]
    public async Task ExecuteAsync_LegacyRowsFallbackWindowsCountOnce()
    {
        var options = new ImplementsOptions
        {
            TargetType = typeof(IWorkspaceImplementationMarker).FullName!,
            Assemblies = [typeof(ImplementsCommandTests).Assembly.Location],
            IncludeAll = true,
            Rows = RowWindow.Range(2, 3),
            Count = true,
        };

        var (exitCode, output, error) =
            await ConsoleCapture.RunAsync(
                () => ImplementsCommand.ExecuteAsync(
                    options,
                    TestContext.Current.CancellationToken));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public async Task CommandLine_SemanticTailSelectionReturnsOneDeterministicRow()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        string[] arguments =
        [
            "implements",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json",
            "-n",
            "1",
            "--tail",
        ];
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(arguments, root);

        var result = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        JsonElement rows = document.RootElement;
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(
            typeof(WorkspaceImplementationC).FullName,
            rows[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task CommandLine_SemanticPrefixRangeSelectionAcceptsSharedRowsGrammar()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        var result = await ExecuteCommandLineAsync(
            "implements",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json",
            "--rows",
            "..3");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            [
                typeof(WorkspaceImplementation).FullName!,
                typeof(WorkspaceImplementationA).FullName!,
                typeof(WorkspaceImplementationB).FullName!,
            ],
            ReadJsonTypes(result.Output));
    }

    [Fact]
    public async Task CommandLine_SemanticSuffixRangeSelectionAcceptsSharedRowsGrammar()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        var result = await ExecuteCommandLineAsync(
            "implements",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json",
            "--rows",
            "2..");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            [
                typeof(WorkspaceImplementationA).FullName!,
                typeof(WorkspaceImplementationB).FullName!,
                typeof(WorkspaceImplementationC).FullName!,
            ],
            ReadJsonTypes(result.Output));
    }

    [Fact]
    public async Task CommandLine_SemanticOrderedSelectionAcceptsLimitThenRange()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        var result = await ExecuteCommandLineAsync(
            "implements",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json",
            "-n",
            "1",
            "--rows",
            "2..3");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Implementers row selection stage 2 requires row 3, but only 1 implementer rows are available.",
            result.Error);
    }

    [Fact]
    public async Task CommandLine_SemanticOrderedSelectionPreservesArgumentOrder()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        var result = await ExecuteCommandLineAsync(
            "implements",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json",
            "--rows",
            "2..3",
            "-n",
            "1");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(
            [typeof(WorkspaceImplementationA).FullName!],
            ReadJsonTypes(result.Output));
    }

    [Fact]
    public async Task CommandLine_SemanticRangeSelectionAppliesBeforeCount()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        string[] arguments =
        [
            "implements",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--count",
            "--rows",
            "2..3",
        ];
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(arguments, root);

        var result = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal("2", result.Output.Trim());
    }

    [Fact]
    public async Task CommandLine_SemanticRangeSelectionReportsStrictFailure()
    {
        string assembly = typeof(ImplementsCommandTests).Assembly.Location;
        string[] arguments =
        [
            "implements",
            typeof(IWorkspaceImplementationMarker).FullName!,
            "--library",
            assembly,
            "--all",
            "--json",
            "--rows",
            "1..10",
        ];
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(arguments, root);

        var result = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("requires row 10", result.Error);
    }

    [Fact]
    public async Task CommandLine_SemanticSelectionRejectsCountFormRows()
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] arguments =
        [
            "implements",
            nameof(IDisposable),
            "--platform",
            "--rows",
            "3",
        ];
        string[] processed = CommandLineBuilder.PreprocessArgs(arguments, root);
        var result = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--rows requires N..M", result.Error);
    }

    private static int CountRenderedWorkspaceImplementationRows(string output)
    {
        const string implementationPrefix =
            "DotnetInspect.Cli.Tests.WorkspaceImplementation";
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(
                implementationPrefix,
                StringComparison.Ordinal));
    }

    private static async Task<(int ExitCode, string Output, string Error)> ExecuteCommandLineAsync(
        params string[] arguments)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(arguments, root);
        return await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed));
    }

    private static string[] ReadJsonTypes(string output)
    {
        using JsonDocument document = JsonDocument.Parse(output);
        return
        [
            .. document.RootElement
                .EnumerateArray()
                .Select(row => row.GetProperty("type").GetString()!)
        ];
    }
}

public interface IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementation :
    IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementationA :
    IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementationB :
    IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementationC :
    IWorkspaceImplementationMarker;

public interface ILegacyRenderedOrderingMarker;

public sealed class LegacyRenderedZ :
    ILegacyRenderedOrderingMarker;

public sealed class LegacyRenderedA :
    ILegacyRenderedOrderingMarker;
