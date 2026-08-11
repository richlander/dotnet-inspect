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
            stream.CopyTo(output);
            Assert.Equal(expected, output.ToArray());
        }
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
