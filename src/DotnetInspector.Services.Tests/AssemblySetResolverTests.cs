using System.IO.Compression;

namespace DotnetInspector.Services.Tests;

public class AssemblySetResolverTests
{
    [Fact]
    public async Task CollectAsync_LocalPackageOwnsExtractionUntilDisposed()
    {
        var packageDir = Directory.CreateTempSubdirectory("assembly-set-package-test").FullName;
        var packagePath = Path.Combine(packageDir, "TestPackage.1.0.0.nupkg");
        var sourceAssembly = typeof(AssemblySetResolverTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net10.0/TestPackage.dll");
            }

            using var httpClient = new HttpClient();
            var assemblySet = await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Packages = [packagePath],
                    Tfm = "net10.0",
                    TempDirPrefix = "assembly-set-test",
                });

            var entry = Assert.Single(assemblySet.Assemblies);
            Assert.Equal(AssemblySetSourceKind.Package, entry.SourceKind);
            Assert.Equal("TestPackage", entry.Source);
            Assert.Equal("1.0.0", entry.Version);
            Assert.True(File.Exists(entry.Path));

            var tempDir = Assert.Single(assemblySet.OwnedTemporaryDirectories);
            Assert.True(Directory.Exists(tempDir));

            assemblySet.Dispose();

            Assert.False(Directory.Exists(tempDir));
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_DirectoryUsesTopLevelDllsAndReportsMissingDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("assembly-set-dir-test").FullName;
        var sourceAssembly = typeof(AssemblySetResolverTests).Assembly.Location;
        var copiedAssembly = Path.Combine(directory, "Copied.dll");
        File.Copy(sourceAssembly, copiedAssembly);

        try
        {
            using var httpClient = new HttpClient();
            using var assemblySet = await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Directories = [directory, Path.Combine(directory, "missing")],
                });

            var entry = Assert.Single(assemblySet.Assemblies);
            Assert.Equal(copiedAssembly, entry.Path);
            Assert.Equal(Path.GetFileName(directory), entry.Source);
            Assert.Equal(AssemblySetSourceKind.Directory, entry.SourceKind);

            var diagnostic = Assert.Single(assemblySet.Diagnostics);
            Assert.Equal(AssemblySetDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains("Directory not found", diagnostic.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
