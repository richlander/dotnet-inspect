using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserEngineBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteArchivePersistence_FreshStoreReusesValidatedArchive(bool corrupt)
    {
        string package = $"gallery.complete-cache.{Guid.NewGuid():N}";
        const string version = "1.2.3";
        var handler = new GalleryPackageHandler(package, version,
            PackageWithDocuments(package, version, "# Retained README", "# Package", "# Skill"));
        using IPackageSourceClient source = Gallery(handler);
        var persistence = new CompleteArchiveMemoryPersistence();
        Task<BrowserPackageDocumentPayload> Read() => BrowserPackageWorkspace.ReadDocumentAsync(
            package, version, "README.md", source, TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken,
            new BrowserPackageWorkspace.BrowserSessionPackageStore(source, persistence));

        BrowserPackageDocumentPayload first = await Read();
        Assert.NotEmpty(persistence.Archives);
        int requests = handler.Requested.Count;
        Assert.True(requests > 0);
        if (corrupt)
            foreach (string key in persistence.Archives.Keys.ToArray())
                persistence.Archives[key] = [0, 0];
        BrowserPackageDocumentPayload second = await Read();
        Assert.Equal(first, second);
        Assert.True(persistence.ArchiveReads > 0);
        if (corrupt) Assert.True(handler.Requested.Count > requests);
        else Assert.Equal(requests, handler.Requested.Count);
    }

    [Fact]
    public async Task CompleteArchivePersistence_CurrentLimitsAndProducerApplyBeforeBodyRead()
    {
        string package = $"gallery.complete-policy.{Guid.NewGuid():N}";
        var handler = new GalleryPackageHandler(package, "1.2.3",
            PackageWithDocuments(package, "1.2.3", "# README", "# Package", "# Skill"));
        using IPackageSourceClient source = Gallery(handler);
        var persistence = new CompleteArchiveMemoryPersistence();
        await BrowserPackageWorkspace.ReadDocumentAsync(package, "1.2.3", "README.md", source,
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken,
            new BrowserPackageWorkspace.BrowserSessionPackageStore(source, persistence));
        var store = new BrowserPackageWorkspace.BrowserSessionPackageStore(source, persistence);
        var coordinate = PackageSourceCoordinate.Create(package, "1.2.3");
        Assert.Null(await ((IAsyncPackageCacheStore)store).ReadCachedAsync(coordinate,
            "unrelated-producer", PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
        Assert.Null(await ((IAsyncPackageCacheStore)store).ReadCachedAsync(coordinate,
            source.Source.Producer.Key, PackagePayloadLimits.Default with { MaxArchiveBytes = 1 },
            TestContext.Current.CancellationToken));
        Assert.Equal(0, persistence.ArchiveReads);
    }

    sealed class CompleteArchiveMemoryPersistence : IBrowserPackageArchivePersistence
    {
        internal Dictionary<string, byte[]> Archives { get; } = [];
        internal int ArchiveReads { get; private set; }
        public bool IsPersistent => true;
        public ValueTask<byte[]?> ReadAsync(string key) =>
            ValueTask.FromResult<byte[]?>(null);
        public ValueTask PublishAsync(string key, ReadOnlyMemory<byte> content)
        {
            if (key.EndsWith("/archive", StringComparison.Ordinal))
                Archives.TryAdd(key, content.ToArray());
            return ValueTask.CompletedTask;
        }
        public ValueTask<long?> GetArchiveLengthAsync(string key) =>
            ValueTask.FromResult<long?>(Archives.TryGetValue(key, out byte[]? bytes)
                ? bytes.LongLength : null);
        public ValueTask<byte[]?> ReadArchiveAsync(string key, int expectedLength)
        {
            ArchiveReads++;
            byte[]? bytes = Archives.GetValueOrDefault(key);
            Assert.Equal(expectedLength, bytes?.Length);
            return ValueTask.FromResult(bytes?.ToArray());
        }
    }
}
