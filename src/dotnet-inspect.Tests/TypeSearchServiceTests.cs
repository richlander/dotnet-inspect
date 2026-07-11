using System.IO.Compression;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class TypeSearchServiceTests
{
    [Fact]
    public async Task CollectTypesAsync_BinPathUsesAssemblySetDirectorySource()
    {
        var directory = Directory.CreateTempSubdirectory("type-search-bin-test").FullName;
        var copiedAssembly = Path.Combine(directory, "CopiedSearchAssembly.dll");
        File.Copy(typeof(TypeSearchServiceTests).Assembly.Location, copiedAssembly);

        try
        {
            using var httpClient = new HttpClient();
            var results = await TypeSearchService.CollectTypesAsync(
                new FindOptions
                {
                    Pattern = nameof(TypeSearchServiceTests),
                    BinPaths = [directory],
                    IncludeAll = true,
                },
                nameof(TypeSearchServiceTests),
                new VerboseLogger(enabled: false),
                httpClient);

            var result = Assert.Single(results, r => r.FullName == typeof(TypeSearchServiceTests).FullName);
            Assert.Equal("CopiedSearchAssembly", result.Assembly);
            Assert.Equal(Path.GetFileName(directory), result.Source);
            Assert.Null(result.SourceVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CollectTypesAsync_PackageFallbackKeepsRuntimeAssembliesForFind()
    {
        var packageDir = Directory.CreateTempSubdirectory("type-search-runtime-package-test").FullName;
        var packagePath = Path.Combine(packageDir, "RuntimeOnly.1.0.0.nupkg");
        var sourceAssembly = typeof(TypeSearchServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "runtimes/any/lib/net10.0/RuntimeOnly.dll");
            }

            using var httpClient = new HttpClient();
            var results = await TypeSearchService.CollectTypesAsync(
                new FindOptions
                {
                    Pattern = nameof(TypeSearchServiceTests),
                    Packages = [packagePath],
                    IncludeAll = true,
                },
                nameof(TypeSearchServiceTests),
                new VerboseLogger(enabled: false),
                httpClient);

            Assert.Contains(results, r => r.FullName == typeof(TypeSearchServiceTests).FullName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task CollectTypesAsync_WithLimitDoesNotResolveLaterSources()
    {
        using var httpClient = new HttpClient();
        List<TypeSearchResult>? results = null;
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"missing-type-search-{Guid.NewGuid():N}");

        var capture = await ConsoleCapture.RunAsync(async () =>
        {
            results = await TypeSearchService.CollectTypesAsync(
                new FindOptions
                {
                    Pattern = nameof(TypeSearchServiceTests),
                    Assemblies = [typeof(TypeSearchServiceTests).Assembly.Location],
                    BinPaths = [missingDirectory],
                    IncludeAll = true,
                    Limit = 1,
                },
                nameof(TypeSearchServiceTests),
                new VerboseLogger(enabled: false),
                httpClient);
            return 0;
        });

        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.DoesNotContain("Directory not found", capture.Error);
    }
}
