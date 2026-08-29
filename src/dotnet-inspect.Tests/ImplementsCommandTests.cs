using System.Linq;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

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
                () => ImplementsCommand.ExecuteAsync(options));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            typeof(WorkspaceImplementation).FullName!,
            output);
        Assert.Contains(
            "\"source\": \"dotnet-inspect.Tests.dll\"",
            output);
    }

    [Fact]
    public async Task ExecuteAsync_JsonWithRows_WindowsSortedOrderMatchingMarkdown()
    {
        // Round 8 finding: implements --json windowed the raw/unsorted discovery-order list,
        // while markdown/table sort by TypeName first -- so -n N --json could return a
        // different item set than -n N markdown/table for the same invocation.
        var unwindowedOptions = new ImplementsOptions
        {
            TargetType = typeof(IOrderedImplementationMarker).FullName!,
            Assemblies = [typeof(ImplementsCommandTests).Assembly.Location],
            IncludeAll = true,
            JsonOutput = true,
        };
        var windowedOptions = unwindowedOptions with { Rows = RowWindow.Head(1) };

        var (unwindowedExitCode, unwindowedOutput, unwindowedError) =
            await ConsoleCapture.RunAsync(() => ImplementsCommand.ExecuteAsync(unwindowedOptions));
        var (windowedExitCode, windowedOutput, windowedError) =
            await ConsoleCapture.RunAsync(() => ImplementsCommand.ExecuteAsync(windowedOptions));

        Assert.Equal(0, unwindowedExitCode);
        Assert.Empty(unwindowedError);
        Assert.Equal(0, windowedExitCode);
        Assert.Empty(windowedError);

        using var unwindowedDocument = JsonDocument.Parse(unwindowedOutput);
        using var windowedDocument = JsonDocument.Parse(windowedOutput);
        var unwindowedRows = unwindowedDocument.RootElement.EnumerateArray().ToList();
        var windowedRows = windowedDocument.RootElement.EnumerateArray().ToList();

        Assert.True(unwindowedRows.Count > 1, "fixture must produce more than one row to prove windowing");
        Assert.Equal(typeof(AlphaOrderedImplementation).FullName!, unwindowedRows[0].GetProperty("type").GetString());
        Assert.Single(windowedRows);
        Assert.Equal(typeof(AlphaOrderedImplementation).FullName!, windowedRows[0].GetProperty("type").GetString());
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
                    () => ImplementsCommand.ExecuteAsync(options));

            Assert.Equal(0, exitCode);
            Assert.Equal("[]", output.Trim());
            Assert.Contains($"Error scanning {path}", error);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public interface IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementation :
    IWorkspaceImplementationMarker;

// Declared in reverse-alphabetical order so metadata discovery order (declaration order)
// differs from the sorted-by-TypeName order implements --json must match.
public interface IOrderedImplementationMarker;

public sealed class ZetaOrderedImplementation : IOrderedImplementationMarker;

public sealed class AlphaOrderedImplementation : IOrderedImplementationMarker;
