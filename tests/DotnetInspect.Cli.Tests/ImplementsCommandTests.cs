using System.Text.Json;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;

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
