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
}

public interface IWorkspaceImplementationMarker;

public sealed class WorkspaceImplementation :
    IWorkspaceImplementationMarker;
