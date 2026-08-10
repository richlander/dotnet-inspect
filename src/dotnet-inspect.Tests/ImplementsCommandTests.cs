using DotnetInspector.Commands;
using DotnetInspector.Options;

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
