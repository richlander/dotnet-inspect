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

    [Fact]
    public async Task CollectAsync_UsesRequestedSourceOrder()
    {
        var directory = Directory.CreateTempSubdirectory("assembly-set-order-test").FullName;
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
                    Assemblies = [sourceAssembly],
                    Directories = [directory],
                    SourceOrder =
                    [
                        AssemblySetSourceKind.Directory,
                        AssemblySetSourceKind.Directory,
                    ],
                });

            Assert.Collection(
                assemblySet.Assemblies,
                entry => Assert.Equal(AssemblySetSourceKind.Directory, entry.SourceKind),
                entry => Assert.Equal(AssemblySetSourceKind.Assembly, entry.SourceKind));
            var diagnostic = Assert.Single(assemblySet.Diagnostics);
            Assert.Equal(AssemblySetDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains("Duplicate assembly source order entry", diagnostic.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_PackageRuntimeAssembliesAreOptIn()
    {
        var packageDir = Directory.CreateTempSubdirectory("assembly-set-runtime-package-test").FullName;
        var packagePath = Path.Combine(packageDir, "RuntimeOnly.1.0.0.nupkg");
        var sourceAssembly = typeof(AssemblySetResolverTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "runtimes/any/lib/net10.0/RuntimeOnly.dll");
            }

            using var httpClient = new HttpClient();
            using var defaultSet = await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest { Packages = [packagePath] });
            using var includeRuntimeSet = await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Packages = [packagePath],
                    IncludePackageRuntimeAssemblies = true,
                });

            Assert.Empty(defaultSet.Assemblies);
            var entry = Assert.Single(includeRuntimeSet.Assemblies);
            Assert.Contains($"{Path.DirectorySeparatorChar}runtimes{Path.DirectorySeparatorChar}", entry.Path);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_LibDescendingModeUsesOldLibraryDependencyRootOrder()
    {
        var packageDir = Directory.CreateTempSubdirectory("assembly-set-lib-order-test").FullName;
        var packagePath = Path.Combine(packageDir, "LibOrder.1.0.0.nupkg");
        var sourceAssembly = typeof(AssemblySetResolverTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net6.0/A.dll");
                archive.CreateEntryFromFile(sourceAssembly, "lib/netstandard2.0/Z.dll");
                archive.CreateEntryFromFile(sourceAssembly, "runtimes/any/lib/net10.0/Runtime.dll");
            }

            using var httpClient = new HttpClient();
            using var assemblySet = await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Packages = [packagePath],
                    PackageSelectionMode = AssemblySetPackageSelectionMode.LibAssembliesDescending,
                });

            var paths = assemblySet.Assemblies.Select(static a => a.Path).ToArray();
            Assert.Equal(paths.OrderByDescending(static p => p, StringComparer.Ordinal), paths);
            Assert.All(paths, path => Assert.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", path));
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task CollectAsync_LibDescendingModeReportsErrorWhenPackageHasNoLibAssemblies()
    {
        var packageDir = Directory.CreateTempSubdirectory("assembly-set-no-lib-test").FullName;
        var packagePath = Path.Combine(packageDir, "NoLib.1.0.0.nupkg");
        var sourceAssembly = typeof(AssemblySetResolverTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "tools/NoLib.dll");
            }

            using var httpClient = new HttpClient();
            using var assemblySet = await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Packages = [packagePath],
                    PackageSelectionMode = AssemblySetPackageSelectionMode.LibAssembliesDescending,
                });

            Assert.Empty(assemblySet.Assemblies);
            var diagnostic = Assert.Single(assemblySet.Diagnostics);
            Assert.Equal(AssemblySetDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("No libraries found in package", diagnostic.Message);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }
}
