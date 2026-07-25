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
}
