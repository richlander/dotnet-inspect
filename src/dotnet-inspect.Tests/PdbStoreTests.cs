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

    [Fact]
    public async Task FileSystemPdbStore_FailedReplacementPreservesPublishedContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var root =
            Path.Combine(
                Path.GetTempPath(),
                $"pdbstore-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSystemPdbStore(root);
            const string Key =
                "servers/symbols.nuget.org/Foo.pdb/KEY/Foo.pdb";
            byte[] published = [1, 2, 3, 4];
            using (var initial = new MemoryStream(published))
                await store.PutAsync(Key, initial, ct);

            await Assert.ThrowsAsync<IOException>(
                () => store.PutAsync(
                        Key,
                        new ThrowingReadStream(),
                        ct)
                    .AsTask());

            await using Stream? reopened =
                await store.TryOpenAsync(Key, ct);
            Assert.NotNull(reopened);
            using var buffer = new MemoryStream();
            await reopened!.CopyToAsync(buffer, ct);
            Assert.Equal(published, buffer.ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemPdbStore_ReplacesContentWhileReaderIsOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        var root =
            Path.Combine(
                Path.GetTempPath(),
                $"pdbstore-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSystemPdbStore(root);
            const string Key =
                "servers/symbols.nuget.org/Foo.pdb/KEY/Foo.pdb";
            byte[] original = [1, 2, 3, 4];
            byte[] replacement = [5, 6, 7, 8];
            using (var input = new MemoryStream(original))
                await store.PutAsync(Key, input, ct);

            await using (Stream? opened =
                await store.TryOpenAsync(Key, ct))
            {
                Assert.NotNull(opened);
                using (var input = new MemoryStream(replacement))
                    await store.PutAsync(Key, input, ct);

                using var originalBuffer = new MemoryStream();
                await opened!.CopyToAsync(originalBuffer, ct);
                Assert.Equal(original, originalBuffer.ToArray());
            }

            await using Stream? reopened =
                await store.TryOpenAsync(Key, ct);
            Assert.NotNull(reopened);
            using var replacementBuffer = new MemoryStream();
            await reopened!.CopyToAsync(replacementBuffer, ct);
            Assert.Equal(replacement, replacementBuffer.ToArray());
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
    [InlineData("C:/escape.pdb")]
    [InlineData("C:..")]
    [InlineData("servers/C:../x.pdb")]
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

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length =>
            throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
            => throw new IOException("Injected read failure.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(
                new IOException("Injected read failure."));

        public override void Flush()
            => throw new NotSupportedException();

        public override long Seek(
            long offset,
            SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
            => throw new NotSupportedException();
    }
}
