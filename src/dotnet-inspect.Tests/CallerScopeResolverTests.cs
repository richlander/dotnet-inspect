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

    /// <summary>
    /// Two assemblies in a scope directory whose names differ only in case are two assemblies.
    ///
    /// <para>The resolver deduplicates by full path, which is right, but it compared those paths
    /// case-insensitively — so on a case-sensitive volume the second file was discarded before any
    /// caller analysis ran and its callers could never be found. This is the first path comparison
    /// in the chain that #3419's forwarded-caller work depends on; the layers above it cannot
    /// recover a candidate that never arrives. Found in review of <c>32951519</c>.</para>
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KeepsTwoAssembliesWhosePathsDifferOnlyInCase()
    {
        var directory = Directory.CreateTempSubdirectory("caller-scope-case-test").FullName;
        try
        {
            Assert.SkipUnless(
                IsCaseSensitive(directory),
                "Needs a case-sensitive filesystem; CI runs one.");

            var source = typeof(CallerScopeResolverTests).Assembly.Location;
            File.Copy(source, Path.Combine(directory, "Alpha.dll"));
            File.Copy(source, Path.Combine(directory, "alpha.dll"));

            using var httpClient = new HttpClient();
            using var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [directory],
                projects: [],
                packages: [],
                tfm: null,
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            Assert.Equal(2, assemblySet.Assemblies.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Whether <paramref name="directory"/> distinguishes names that differ only in case, asked of
    /// the filesystem rather than of the operating system: Windows carries per-directory case
    /// sensitivity, so the answer is a property of the path and not of the platform.
    /// </summary>
    static bool IsCaseSensitive(string directory)
    {
        string probe = Path.Combine(directory, "case-probe.tmp");
        File.WriteAllText(probe, "");
        try
        {
            return !File.Exists(Path.Combine(directory, "CASE-PROBE.TMP"));
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
