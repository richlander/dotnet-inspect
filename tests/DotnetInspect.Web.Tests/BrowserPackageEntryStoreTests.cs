using System.Collections.Concurrent;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserPackageEntryStoreTests
{
    [Fact]
    public void InitializationFailure_IsVisibleInCacheDiagnostics()
    {
        BrowserPackageWorkspace.ReportEntryStoreFailure(
            "initialization",
            new BrowserPackageEntryPersistenceException(
                "Cache Storage initialization failed.",
                new InvalidOperationException("fixture")));

        Assert.Equal(
            "Browser package-entry cache initialization failed: "
                + "Cache Storage initialization failed.",
            BrowserPackageWorkspace.Stats().EntryStoreError);
    }

    [Fact]
    public async Task RecreatedStore_ReadsPublishedDirectoryAndEntry()
    {
        using IPackageSourceClient source =
            BrowserPackageWorkspace.CreateGallerySource(
                new RejectingHandler(),
                new NuGetFetchOptions());
        var persistence = new MemoryPersistence();
        IPackageEntryStore first =
            new BrowserPackageWorkspace.BrowserSessionPackageStore(
                source,
                persistence);

        await first.PublishDirectoryAsync(
            "System.Text.Json",
            "11.0.0-rc.1.26425.128",
            new byte[] { 1, 2, 3 },
            42_322_665);
        await first.PublishEntryAsync(
            "System.Text.Json",
            "11.0.0-rc.1.26425.128",
            "runtimes/linux-x64/lib/net11.0/System.Text.Json.dll",
            new byte[] { 4, 5, 6 });

        IPackageEntryStore recreated =
            new BrowserPackageWorkspace.BrowserSessionPackageStore(
                source,
                persistence);
        PackageEntryDirectory? directory =
            await recreated.ReadDirectoryAsync(
                "System.Text.Json",
                "11.0.0-rc.1.26425.128");
        byte[]? entry = await recreated.ReadEntryAsync(
            "System.Text.Json",
            "11.0.0-rc.1.26425.128",
            "runtimes/linux-x64/lib/net11.0/System.Text.Json.dll");

        Assert.NotNull(directory);
        Assert.Equal(42_322_665, directory.ArchiveLength);
        Assert.Equal(new byte[] { 1, 2, 3 }, directory.Region.ToArray());
        Assert.Equal(new byte[] { 4, 5, 6 }, entry);
        Assert.Equal(2, persistence.Writes);
        Assert.Equal(2, persistence.Reads);
        Assert.All(
            persistence.Keys,
            key => Assert.Matches(
                "^[0-9a-f]{64}/(?:directory|entries/[0-9a-f]{64})$",
                key));
        Assert.DoesNotContain(
            persistence.Keys,
            key => key.Contains("System.Text.Json", StringComparison.Ordinal)
                || key.Contains("runtimes", StringComparison.Ordinal));
    }

    sealed class MemoryPersistence : IBrowserPackageEntryPersistence
    {
        readonly ConcurrentDictionary<string, byte[]> _items =
            new(StringComparer.Ordinal);

        internal int Reads { get; private set; }

        internal int Writes { get; private set; }

        internal IReadOnlyCollection<string> Keys => [.. _items.Keys];

        public bool IsPersistent => true;

        public ValueTask<byte[]?> ReadAsync(string key)
        {
            Reads++;
            return ValueTask.FromResult<byte[]?>(
                _items.TryGetValue(key, out byte[]? content)
                    ? content.ToArray()
                    : null);
        }

        public ValueTask PublishAsync(
            string key,
            ReadOnlyMemory<byte> content)
        {
            Writes++;
            _items.TryAdd(key, content.ToArray());
            return ValueTask.CompletedTask;
        }
    }

    sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The package-entry store gate must not make a network request.");
    }
}
