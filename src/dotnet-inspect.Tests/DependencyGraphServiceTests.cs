using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class DependencyGraphServiceTests
{
    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_FileInput_ReturnsAssemblyReferenceGraph()
    {
        var assemblyPath = typeof(DependencyGraphServiceTests).Assembly.Location;
        using var httpClient = new HttpClient();
        var logger = new VerboseLogger(enabled: false);

        var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
            httpClient, assemblyPath, sourceOptions: null, logger);

        var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
        Assert.Equal("dotnet-inspect.Tests", graph.AssemblyName);
        Assert.NotEmpty(graph.References);
    }
}
