using System.IO.Compression;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

public class DependencyGraphServiceTests
{
    public DependencyGraphServiceTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

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

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputUsesLibDescendingRootOrder()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-root-test").FullName;
        var packagePath = Path.Combine(packageDir, "DependsRoot.1.0.0.nupkg");
        var sourceAssembly = typeof(DependencyGraphServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net6.0/A.dll");
                archive.CreateEntryFromFile(sourceAssembly, "lib/netstandard2.0/Z.dll");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger);

            var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
            Assert.Equal("Z", graph.AssemblyName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputWithNoLibAssembliesReportsSpecificError()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-no-lib-test").FullName;
        var packagePath = Path.Combine(packageDir, "NoLib.1.0.0.nupkg");
        var sourceAssembly = typeof(DependencyGraphServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "tools/NoLib.dll");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger);

            var error = Assert.IsType<LibraryDependencyGraphResult.Error>(result);
            Assert.Contains("No libraries found in package", error.Message);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }
}
