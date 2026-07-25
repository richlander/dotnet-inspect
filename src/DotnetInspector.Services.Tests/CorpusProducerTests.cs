using System.IO.Compression;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class CorpusProducerTests
{
    private static readonly string SelfAssembly = typeof(CorpusProducerTests).Assembly.Location;

    [Fact]
    public async Task PopulateAsync_LocalPackage_ProducesCorpusMemberAndManifestEntry()
    {
        var packageDir = Directory.CreateTempSubdirectory("corpus-producer-populate").FullName;
        var packagePath = Path.Combine(packageDir, "TestPackage.1.0.0.nupkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(SelfAssembly, "lib/net10.0/TestPackage.dll");
            }

            using var httpClient = new HttpClient();
            using var populated = await CorpusProducer.PopulateAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Packages = [packagePath],
                    Tfm = "net10.0",
                    TempDirPrefix = "corpus-producer-test",
                });

            var member = Assert.Single(populated.Corpus.Members);
            Assert.Equal("TestPackage", member.Source);
            Assert.Equal("1.0.0", member.Version);
            Assert.Equal("net10.0", member.Tfm);
            Assert.True(File.Exists(member.AssemblyPath));

            var entry = Assert.Single(populated.Manifest.Entries);
            Assert.Equal(new CorpusManifestEntry(AssemblySetSourceKind.Package, "TestPackage", "1.0.0", "net10.0"), entry);
            Assert.Empty(populated.Diagnostics);

            // Diagnostics is a genuine read-only view, not a downcastable backing list.
            Assert.Throws<NotSupportedException>(() =>
                ((IList<AssemblySetDiagnostic>)populated.Diagnostics).Add(
                    new AssemblySetDiagnostic(AssemblySetDiagnosticSeverity.Warning, "x")));

            // The produced corpus searches offline over exactly the resolved assembly.
            var typeSearch = populated.Corpus.SearchTypes([typeof(CorpusProducerTests).FullName!]);
            Assert.Empty(typeSearch.SkippedAssemblies);
            var match = Assert.Single(typeSearch.Results);
            Assert.Equal("TestPackage", match.Source);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task PopulateAsync_PackageWithManyAssemblies_DedupesManifestButKeepsEveryMember()
    {
        var packageDir = Directory.CreateTempSubdirectory("corpus-producer-dedup").FullName;
        var packagePath = Path.Combine(packageDir, "MultiSurface.2.0.0.nupkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(SelfAssembly, "lib/net10.0/Alpha.dll");
                archive.CreateEntryFromFile(SelfAssembly, "lib/net10.0/Zeta.dll");
            }

            using var httpClient = new HttpClient();
            using var populated = await CorpusProducer.PopulateAsync(
                httpClient,
                new AssemblySetRequest { Packages = [packagePath], Tfm = "net10.0" });

            Assert.Equal(2, populated.Corpus.Count);
            Assert.All(populated.Corpus.Members, m => Assert.Equal("MultiSurface", m.Source));

            // Two assemblies, one logical origin.
            var entry = Assert.Single(populated.Manifest.Entries);
            Assert.Equal(AssemblySetSourceKind.Package, entry.Kind);
            Assert.Equal("MultiSurface", entry.Id);
            Assert.Equal("2.0.0", entry.Version);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task PopulateAsync_DisposeReleasesOwnedExtractionDirectory()
    {
        var packageDir = Directory.CreateTempSubdirectory("corpus-producer-dispose").FullName;
        var packagePath = Path.Combine(packageDir, "Disposable.1.0.0.nupkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(SelfAssembly, "lib/net10.0/Disposable.dll");
            }

            using var httpClient = new HttpClient();
            var populated = await CorpusProducer.PopulateAsync(
                httpClient,
                new AssemblySetRequest
                {
                    Packages = [packagePath],
                    Tfm = "net10.0",
                    TempDirPrefix = "corpus-producer-dispose-test",
                });

            var memberPath = Assert.Single(populated.Corpus.Members).AssemblyPath;
            Assert.True(File.Exists(memberPath));

            populated.Dispose();

            Assert.False(File.Exists(memberPath));
            // Dispose is idempotent.
            populated.Dispose();
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task ToManifest_NormalizesPathBoundSourceToReloadableAssemblyEntry()
    {
        var directory = Directory.CreateTempSubdirectory("corpus-producer-dir").FullName;
        var copiedAssembly = Path.Combine(directory, "Copied.dll");
        File.Copy(SelfAssembly, copiedAssembly);

        try
        {
            using var httpClient = new HttpClient();
            using var set = await AssemblySetResolver.CollectAsync(
                httpClient,
                new AssemblySetRequest { Directories = [directory] });

            var manifest = CorpusProducer.ToManifest(set);

            var entry = Assert.Single(manifest.Entries);
            Assert.Equal(AssemblySetSourceKind.Assembly, entry.Kind);
            Assert.Equal(copiedAssembly, entry.Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PopulateFromManifestAsync_AssemblyEntry_ReloadsEquivalentCorpus()
    {
        var manifest = new CorpusManifest
        {
            Entries = [new CorpusManifestEntry(AssemblySetSourceKind.Assembly, SelfAssembly)],
        };

        using var httpClient = new HttpClient();
        using var populated = await CorpusProducer.PopulateFromManifestAsync(httpClient, manifest);

        var member = Assert.Single(populated.Corpus.Members);
        Assert.Equal(SelfAssembly, member.AssemblyPath);

        // Rebuilt manifest describes the reloaded corpus and matches the input entry.
        var rebuilt = Assert.Single(populated.Manifest.Entries);
        Assert.Equal(manifest.Entries[0], rebuilt);
    }

    [Fact]
    public void ToManifest_MixedSources_PreservesFirstAppearanceOrderAndDedupes()
    {
        var entries = new[]
        {
            new AssemblySetEntry(@"C:\ext\A.dll", "Pkg", "1.0.0", AssemblySetSourceKind.Package, "net8.0"),
            new AssemblySetEntry(@"C:\ext\B.dll", "Pkg", "1.0.0", AssemblySetSourceKind.Package, "net8.0"),
            new AssemblySetEntry(@"C:\loose\Local.dll", "Local.dll", null, AssemblySetSourceKind.Assembly),
            new AssemblySetEntry(@"C:\ext\A.dll", "Pkg", "1.0.0", AssemblySetSourceKind.Package, "net8.0"),
        };

        var manifest = CorpusProducer.ToManifest(entries);

        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal(new CorpusManifestEntry(AssemblySetSourceKind.Package, "Pkg", "1.0.0", "net8.0"), manifest.Entries[0]);
        Assert.Equal(new CorpusManifestEntry(AssemblySetSourceKind.Assembly, @"C:\loose\Local.dll"), manifest.Entries[1]);
    }

    [Fact]
    public void ToManifest_PlatformFrameworkCollapses_ButPlatformAssembliesStayDistinctByPath()
    {
        // Mirrors AssemblySetResolver output: a whole framework's assemblies all carry the framework name
        // in Source (correctly collapsible), while individually resolved platform assemblies ALSO carry the
        // framework name in Source rather than the requested assembly name (so collapsing by Source would
        // wrongly merge them into one un-reloadable entry).
        var entries = new[]
        {
            new AssemblySetEntry(@"C:\packs\App\System.Runtime.dll", "Microsoft.NETCore.App", "10.0.0", AssemblySetSourceKind.PlatformFramework),
            new AssemblySetEntry(@"C:\packs\App\System.Collections.dll", "Microsoft.NETCore.App", "10.0.0", AssemblySetSourceKind.PlatformFramework),
            new AssemblySetEntry(@"C:\packs\App\System.Text.Json.dll", "Microsoft.NETCore.App", "10.0.0", AssemblySetSourceKind.PlatformAssembly),
            new AssemblySetEntry(@"C:\packs\App\System.Linq.dll", "Microsoft.NETCore.App", "10.0.0", AssemblySetSourceKind.PlatformAssembly),
        };

        var manifest = CorpusProducer.ToManifest(entries);

        // Framework -> exactly one reload-by-name entry.
        Assert.Single(
            manifest.Entries,
            e => e.Kind == AssemblySetSourceKind.PlatformFramework && e.Id == "Microsoft.NETCore.App");

        // Platform assemblies stay distinct, captured by full path (not merged into the framework name).
        var assemblyEntries = manifest.Entries.Where(e => e.Kind == AssemblySetSourceKind.Assembly).ToArray();
        Assert.Equal(2, assemblyEntries.Length);
        Assert.Contains(assemblyEntries, e => e.Id == Path.GetFullPath(@"C:\packs\App\System.Text.Json.dll"));
        Assert.Contains(assemblyEntries, e => e.Id == Path.GetFullPath(@"C:\packs\App\System.Linq.dll"));
        Assert.Equal(3, manifest.Entries.Count);
    }

    [Fact]
    public void ToManifest_PathBoundEntry_IsNormalizedToFullPath()
    {
        var relative = Path.Combine("sub", "Rel.dll");
        var entries = new[]
        {
            new AssemblySetEntry(relative, "Rel.dll", null, AssemblySetSourceKind.Assembly),
        };

        var entry = Assert.Single(CorpusProducer.ToManifest(entries).Entries);

        Assert.Equal(AssemblySetSourceKind.Assembly, entry.Kind);
        Assert.True(Path.IsPathFullyQualified(entry.Id));
        Assert.Equal(Path.GetFullPath(relative), entry.Id);
    }

    [Fact]
    public void ToManifest_Entries_AreReadOnlyAndNotDowncastableToMutableList()
    {
        var manifest = CorpusProducer.ToManifest(new[]
        {
            new AssemblySetEntry(@"C:\loose\A.dll", "A.dll", null, AssemblySetSourceKind.Assembly),
        });

        Assert.IsNotType<List<CorpusManifestEntry>>(manifest.Entries);
        Assert.Throws<NotSupportedException>(() => ((IList<CorpusManifestEntry>)manifest.Entries).Clear());
    }

    [Fact]
    public async Task PopulateFromManifestAsync_EmptyManifest_Throws()
    {
        using var httpClient = new HttpClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CorpusProducer.PopulateFromManifestAsync(httpClient, new CorpusManifest()));
    }

    [Fact]
    public async Task PopulateAsync_ResolvesNoAssemblies_ThrowsRatherThanReturningEmptyCorpus()
    {
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"corpus-missing-{Guid.NewGuid():N}");
        using var httpClient = new HttpClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CorpusProducer.PopulateAsync(
                httpClient,
                new AssemblySetRequest { Directories = [missingDirectory] }));

        Assert.Contains("resolved no assemblies", ex.Message);
    }
}
