using System.Security.Cryptography;
using System.IO.Compression;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public sealed class InMemoryPackageContentTests
{
    [Fact]
    public void BoundedOpen_RejectsEntryBeforeExpansion()
    {
        InMemoryPackageContent content = Content(new byte[32]);

        Assert.True(content.TryGetEntryLength("payload.bin", out long length));
        Assert.Equal(32, length);
        Assert.Throws<InvalidDataException>(
            () => content.TryOpenEntry("payload.bin", 16, out _));
    }

    [Fact]
    public void BoundedOpen_ReturnsTheExactEntry()
    {
        byte[] expected = [1, 2, 3, 4];
        InMemoryPackageContent content = Content(expected);

        Assert.True(content.TryOpenEntry("PAYLOAD.BIN", expected.Length, out Stream? stream));
        using (stream)
        using (var output = new MemoryStream())
        {
            Assert.True(stream.CanSeek);
            Assert.Equal(0, stream.Read(Span<byte>.Empty));
            stream.CopyTo(output);
            Assert.Equal(expected, output.ToArray());
        }
    }

    [Fact]
    public void BoundedPullOpen_StreamsWithoutAnEntrySizedAllocation()
    {
        byte[] expected = new byte[4 * 1024 * 1024];
        for (int index = 0; index < expected.Length; index++)
            expected[index] = (byte)(index % 251);
        byte[] expectedHash = SHA256.HashData(expected);
        InMemoryPackageContent content = Content(expected);

        long beforeRead = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(
            ((IPackageHousePayloadSource)content).TryOpenPayloadRead(
                "payload.bin",
                expected.Length,
                out Stream? stream));

        long observed = 0;
        byte[] actualHash;
        using (stream)
        using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            byte[] buffer = new byte[4096];
            while (true)
            {
                int read = stream.Read(buffer);
                if (read == 0)
                    break;

                hash.AppendData(buffer, 0, read);
                observed += read;
            }

            actualHash = hash.GetHashAndReset();
        }

        long readAllocation =
            GC.GetAllocatedBytesForCurrentThread() - beforeRead;
        Assert.True(
            readAllocation < expected.Length / 2,
            $"Pulling allocated {readAllocation:N0} bytes for a {expected.Length:N0}-byte entry.");
        Assert.Equal(expected.LongLength, observed);
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public void BoundedPullOpen_StreamsRealSystemTextJsonEntry()
    {
        byte[] archive = File.ReadAllBytes(
            RealAsset(
                "PackageAdmission",
                "System.Text.Json.10.0.0.nupkg"));
        var content = new InMemoryPackageContent(
            archive,
            fromCache: false,
            producerKey: "nuget.org");

        Assert.True(
            ((IPackageHousePayloadSource)content).TryOpenPayloadRead(
                "lib/net10.0/System.Text.Json.dll",
                maxExpandedBytes: 1024 * 1024,
                out Stream? stream));

        using (stream)
        using (FileStream expected = File.OpenRead(
            RealAsset("PackageHouse", "System.Text.Json.dll")))
        {
            Assert.Equal(
                SHA256.HashData(expected),
                SHA256.HashData(stream));
        }
    }

    [Fact]
    public void EntryManifest_IsCachedWithDeclaredLengths()
    {
        InMemoryPackageContent content = Content(new byte[32]);

        IReadOnlyList<PackageContentEntry> first =
            content.EnumerateEntriesWithLengths();
        IReadOnlyList<PackageContentEntry> second =
            content.EnumerateEntriesWithLengths();

        Assert.Same(first, second);
        PackageContentEntry entry = Assert.Single(first);
        Assert.Equal("payload.bin", entry.Path);
        Assert.Equal(32, entry.Length);
        using PackageContentEntryScanner scanner =
            content.CreateEntryScanner();
        Assert.True(scanner.MoveNext(out PackageContentEntry scanned));
        Assert.Equal(entry, scanned);
        Assert.False(scanner.MoveNext(out _));
        Assert.True(content.TryGetEntryLength("PAYLOAD.BIN", out long length));
        Assert.Equal(32, length);
    }

    [Fact]
    public async Task BoundedReader_RejectsDeclaredOversizeWithoutReading()
    {
        var source = new ThrowOnReadStream();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => BoundedContentReader.ReadAllBytesAsync(
                source,
                maxBytes: 8,
                declaredLength: 9,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(source.ReadAttempted);
    }

    [Fact]
    public async Task BoundedReader_RejectsUnknownLengthAtTheLimit()
    {
        using var source = new MemoryStream(new byte[9], writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => BoundedContentReader.ReadAllBytesAsync(
                source,
                maxBytes: 8,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    static InMemoryPackageContent Content(byte[] payload)
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry("payload.bin").Open();
            entry.Write(payload);
        }

        return new InMemoryPackageContent(
            package.ToArray(),
            fromCache: false,
            producerKey: "bounded-entry-tests");
    }

    static string RealAsset(params string[] segments) =>
        Path.Combine([AppContext.BaseDirectory, "RealAssets", .. segments]);

    sealed class ThrowOnReadStream : Stream
    {
        public bool ReadAttempted { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadAttempted = true;
            throw new InvalidOperationException("The reader crossed the declared-length gate.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadAttempted = true;
            throw new InvalidOperationException("The reader crossed the declared-length gate.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
