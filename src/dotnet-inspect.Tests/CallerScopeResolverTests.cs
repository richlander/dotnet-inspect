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
    /// One physical file supplied under two spellings is one caller, even when the filesystem
    /// resolved both spellings to it.
    ///
    /// <para>Round 12 changed this dedup from <c>OrdinalIgnoreCase</c> to <c>Ordinal</c> so that
    /// two genuinely distinct files on a case-sensitive volume would both be scanned. That is
    /// right, but comparing the strings exactly is the wrong way to get it: on a case-insensitive
    /// volume <c>Out\Caller.dll</c> and <c>out\caller.dll</c> are two strings for one file, so the
    /// resolver returned both, the same image was opened twice, and every call site in it was
    /// reported twice. Two scope arguments differing only in case is enough to trigger it. Found in
    /// review of <c>37a4444b</c>.</para>
    ///
    /// <para>The fix asks the filesystem which spelling the entry actually has rather than
    /// assuming a platform rule, so this test is meaningful on either kind of volume: where the two
    /// directory spellings name one directory there is one caller, and the companion test above
    /// keeps genuinely distinct files apart where they can exist.</para>
    /// </summary>
    [Fact]
    public async Task ResolveAsync_CountsOnePhysicalFileOnceAcrossTwoSpellingsOfItsDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("caller-scope-spelling-test").FullName;
        try
        {
            var source = typeof(CallerScopeResolverTests).Assembly.Location;
            File.Copy(source, Path.Combine(directory, "Caller.dll"));

            string swapped = SwapLeafCase(directory);
            Assert.SkipUnless(
                Directory.Exists(swapped),
                "Needs a volume that resolves the directory under both spellings.");

            using var httpClient = new HttpClient();
            using var assemblySet = await CallerScopeResolver.ResolveAsync(
                directories: [directory, swapped],
                projects: [],
                packages: [],
                tfm: null,
                ownAssemblyPath: null,
                httpClient,
                new VerboseLogger(enabled: false));

            Assert.Single(assemblySet.Assemblies);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The same path with the case of its last segment inverted.</summary>
    static string SwapLeafCase(string path)
    {
        string leaf = Path.GetFileName(path);
        string swapped = new([.. leaf.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))]);
        return Path.Combine(Path.GetDirectoryName(path)!, swapped);
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
