using System.IO.Compression;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class CallerScopeResolverTests
{
    [Fact]
    public async Task ResolveAsync_CallerPackageKeepsExtractedAssembliesUntilDisposed()
    {
        var packageDir = Directory.CreateTempSubdirectory("caller-scope-package-test").FullName;
        var packagePath = Path.Combine(packageDir, "CallerScope.1.0.0.nupkg");
        var sourceAssembly = typeof(CallerScopeResolverTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net10.0/CallerScope.dll");
            }

            using var httpClient = new HttpClient();
            var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [],
                projects: [],
                packages: [packagePath],
                tfm: "net10.0",
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            var assemblyPath = Assert.Single(assemblySet.Assemblies);
            Assert.True(File.Exists(assemblyPath));

            assemblySet.Dispose();

            Assert.False(File.Exists(assemblyPath));
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }
}
