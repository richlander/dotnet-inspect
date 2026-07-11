using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

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
}
