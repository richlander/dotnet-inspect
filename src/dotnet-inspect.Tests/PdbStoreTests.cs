using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

public class PdbStoreTests
{
    [Fact]
    public async Task InMemoryPdbStore_PutThenOpen_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryPdbStore();
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        Assert.Null(await store.TryOpenAsync("pkg/1.0.0/KEY/Foo.pdb", ct));

        using (var input = new MemoryStream(payload))
            await store.PutAsync("pkg/1.0.0/KEY/Foo.pdb", input, ct);

        await using var opened = await store.TryOpenAsync("pkg/1.0.0/KEY/Foo.pdb", ct);
        Assert.NotNull(opened);
        using var buffer = new MemoryStream();
        await opened!.CopyToAsync(buffer, ct);
        Assert.Equal(payload, buffer.ToArray());

        // In-memory content never has a filesystem path.
        Assert.Null(store.TryGetLocalPath("pkg/1.0.0/KEY/Foo.pdb"));
    }

    [Fact]
    public async Task FileSystemPdbStore_PutThenOpen_RoundTripsAndExposesLocalPath()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), $"pdbstore-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSystemPdbStore(root);
            var payload = new byte[] { 9, 8, 7, 6 };
            const string key = "example.package/1.0.0/AABBCCDDFFFFFFFF/Example.pdb";

            Assert.Null(store.TryGetLocalPath(key));
            Assert.Null(await store.TryOpenAsync(key, ct));

            using (var input = new MemoryStream(payload))
                await store.PutAsync(key, input, ct);

            var localPath = store.TryGetLocalPath(key);
            Assert.NotNull(localPath);
            Assert.True(File.Exists(localPath));
            // Key segments map verbatim onto the on-disk layout.
            Assert.Equal(
                Path.Combine(root, "example.package", "1.0.0", "AABBCCDDFFFFFFFF", "Example.pdb"),
                localPath);

            await using var opened = await store.TryOpenAsync(key, ct);
            Assert.NotNull(opened);
            using var buffer = new MemoryStream();
            await opened!.CopyToAsync(buffer, ct);
            Assert.Equal(payload, buffer.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemPdbStore_AllowsDottedPdbFileNameSegment()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), $"pdbstore-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSystemPdbStore(root);
            // A real assembly PDB name has interior dots; it must not be rejected.
            const string key = "servers/System.Text.Json.pdb/KEY/System.Text.Json.pdb";
            using (var input = new MemoryStream(new byte[] { 1 }))
                await store.PutAsync(key, input, ct);
            Assert.NotNull(store.TryGetLocalPath(key));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape.pdb")]
    [InlineData("pkg/../../escape.pdb")]
    [InlineData("pkg/./escape.pdb")]
    [InlineData("pkg/sub\\escape.pdb")]
    public async Task FileSystemPdbStore_TraversalKey_Throws(string key)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), $"pdbstore-{Guid.NewGuid():N}");
        var store = new FileSystemPdbStore(root);

        Assert.Throws<ArgumentException>(() => store.TryGetLocalPath(key));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            using var input = new MemoryStream(new byte[] { 1 });
            await store.PutAsync(key, input, ct);
        });
    }
}
