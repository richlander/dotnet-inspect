using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BinaryFetch;

namespace ZipFetch.Tests;

public sealed class ZipArchiveReaderTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    // --- Directory reads ---------------------------------------------------

    [Fact]
    public async Task Directory_WithinTheTail_MatchesTheZipArchiveOracle()
    {
        byte[] archive = Archive(("lib/net8.0/a.dll", Bytes(3_000)), ("readme.md", Text("hello")));
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));

        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token);

        AssertMatchesOracle(archive, directory);
        Assert.Equal(archive.Length, directory.ArchiveLength);
        Assert.Equal(1, source.Reads); // the tail only
    }

    [Fact]
    public async Task Directory_LargerThanTheTail_UsesExactlyOneMoreRead()
    {
        var entries = Enumerable.Range(0, 1_500)
            .Select(index => ($"lib/net8.0/{new string('n', 60)}{index:D5}.dll", Bytes(8)))
            .ToArray();
        byte[] archive = Archive(entries);
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));

        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token);

        Assert.True(directory.DirectoryLength > 65_557, "the directory must not fit the tail");
        AssertMatchesOracle(archive, directory);
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public async Task Directory_OverTheCaps_IsOverBound_BeforeAnyFurtherRead()
    {
        byte[] archive = Archive(("a", Bytes(1)), ("b", Bytes(1)), ("c", Bytes(1)));
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));

        ZipReadException refused = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadDirectoryAsync(source, new ZipReadLimits(maxEntryCount: 2), Token));
        Assert.Equal(ZipReadFailure.OverBound, refused.Failure);
        Assert.Equal(1, source.Reads);

        ZipReadException tooLarge = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadDirectoryAsync(
                new StreamRandomAccessSource(new MemoryStream(archive)),
                new ZipReadLimits(maxArchiveBytes: archive.Length - 1),
                Token));
        Assert.Equal(ZipReadFailure.OverBound, tooLarge.Failure);
    }

    [Fact]
    public async Task Directory_NoRecord_OrTrailingBytes_IsMalformed()
    {
        byte[] noise = Bytes(500);
        ZipReadException missing = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadDirectoryAsync(
                new StreamRandomAccessSource(new MemoryStream(noise)), ZipReadLimits.Default, Token));
        Assert.Equal(ZipReadFailure.Malformed, missing.Failure);

        byte[] trailing = [.. Archive(("a", Bytes(10))), 1, 2, 3];
        ZipReadException garbage = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadDirectoryAsync(
                new StreamRandomAccessSource(new MemoryStream(trailing)), ZipReadLimits.Default, Token));
        Assert.Equal(ZipReadFailure.Malformed, garbage.Failure);
    }

    [Fact]
    public async Task Zip64Sentinel_IsUnsupported_OrOverBoundWhenItAlsoCrossesACap()
    {
        byte[] archive = Archive(("a", Bytes(10)));
        int record = FindEndOfCentralDirectory(archive);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(record + 10), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(record + 8), ushort.MaxValue);

        ZipReadException capped = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadDirectoryAsync(
                new StreamRandomAccessSource(new MemoryStream(archive)), ZipReadLimits.Default, Token));
        Assert.Equal(ZipReadFailure.OverBound, capped.Failure);

        ZipReadException zip64 = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadDirectoryAsync(
                new StreamRandomAccessSource(new MemoryStream(archive)),
                new ZipReadLimits(maxEntryCount: ushort.MaxValue),
                Token));
        Assert.Equal(ZipReadFailure.Unsupported, zip64.Failure);
    }

    // --- Entry reads -------------------------------------------------------

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Optimal)]
    public async Task Entry_Read_MatchesTheOriginalBytes(CompressionLevel level)
    {
        byte[] payload = Bytes(40_000);
        byte[] archive = Archive(level, ("lib/net8.0/a.dll", payload), ("z.txt", Text("tail")));
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token);
        int before = source.Reads;

        byte[] content = await ZipArchiveReader.ReadEntryAsync(
            source, directory, directory.Find("lib/net8.0/a.dll")!, ZipReadLimits.Default, cancellationToken: Token);

        Assert.Equal(payload, content);
        Assert.Equal(1, source.Reads - before);
    }

    [Fact]
    public async Task Entry_DeclaredAboveTheBound_IsRefusedBeforeAnyTransfer()
    {
        byte[] archive = Archive(("big", Bytes(10_000)));
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token);
        int before = source.Reads;

        ZipReadException refused = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadEntryAsync(
                source, directory, directory.Entries[0], ZipReadLimits.Default, maxExpandedBytes: 9_999, cancellationToken: Token));

        Assert.Equal(ZipReadFailure.OverBound, refused.Failure);
        Assert.Equal(0, source.Reads - before);
    }

    [Fact]
    public async Task Entry_CrossingTheBoundWhileExpanding_IsOverBound()
    {
        // The directory under-declares the expansion so the bound is crossed mid-stream.
        byte[] archive = Archive(CompressionLevel.Optimal, ("big", new byte[60_000]));
        PatchExpandedLength(archive, "big", 1_000);
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(
            new StreamRandomAccessSource(new MemoryStream(archive)), ZipReadLimits.Default, Token);

        ZipReadException crossed = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadEntryAsync(
                new StreamRandomAccessSource(new MemoryStream(archive)),
                directory, directory.Entries[0], ZipReadLimits.Default, maxExpandedBytes: 2_000, cancellationToken: Token));

        Assert.Equal(ZipReadFailure.OverBound, crossed.Failure);
    }

    [Fact]
    public async Task Entry_FinishingAtAnotherLengthThanDeclared_IsMalformed()
    {
        byte[] archive = Archive(CompressionLevel.Optimal, ("big", new byte[60_000]));
        PatchExpandedLength(archive, "big", 1_000);
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(
            new StreamRandomAccessSource(new MemoryStream(archive)), ZipReadLimits.Default, Token);

        ZipReadException malformed = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadEntryAsync(
                new StreamRandomAccessSource(new MemoryStream(archive)),
                directory, directory.Entries[0], ZipReadLimits.Default, cancellationToken: Token));

        Assert.Equal(ZipReadFailure.Malformed, malformed.Failure);
    }

    [Fact]
    public async Task Entry_ExtentPastTheDirectory_IsMalformed_BeforeAnyTransfer()
    {
        byte[] archive = Archive(("a", Bytes(100)), ("b", Bytes(100)));
        PatchCentralField(archive, "a", 20, 1_000_000); // compressed length
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token);
        int before = source.Reads;

        ZipReadException malformed = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadEntryAsync(
                source, directory, directory.Find("a")!, ZipReadLimits.Default, cancellationToken: Token));

        Assert.Equal(ZipReadFailure.Malformed, malformed.Failure);
        Assert.Equal(0, source.Reads - before);
    }

    [Fact]
    public async Task Entry_WithAnUnsupportedMethod_IsUnsupported()
    {
        byte[] archive = Archive(("a", Bytes(100)));
        PatchCentralField16(archive, "a", 10, 12); // bzip2
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(
            new StreamRandomAccessSource(new MemoryStream(archive)), ZipReadLimits.Default, Token);

        ZipReadException unsupported = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadEntryAsync(
                new StreamRandomAccessSource(new MemoryStream(archive)),
                directory, directory.Entries[0], ZipReadLimits.Default, cancellationToken: Token));

        Assert.Equal(ZipReadFailure.Unsupported, unsupported.Failure);
    }

    [Fact]
    public async Task Entry_CrcMismatch_IsMalformed()
    {
        byte[] archive = Archive(("a", Bytes(100)));
        PatchCentralField(archive, "a", 16, 0xDEADBEEF);
        PatchLocalField(archive, "a", 14, 0xDEADBEEF);
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(
            new StreamRandomAccessSource(new MemoryStream(archive)), ZipReadLimits.Default, Token);

        ZipReadException malformed = await Assert.ThrowsAsync<ZipReadException>(
            () => ZipArchiveReader.ReadEntryAsync(
                new StreamRandomAccessSource(new MemoryStream(archive)),
                directory, directory.Entries[0], ZipReadLimits.Default, cancellationToken: Token));

        Assert.Equal(ZipReadFailure.Malformed, malformed.Failure);
    }

    [Fact]
    public async Task LastEntry_EndingAtTheDirectory_IsNeverRequestedPastIt()
    {
        byte[] payload = Bytes(2_000);
        byte[] archive = Archive(CompressionLevel.NoCompression, ("last", payload));
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));
        var limits = new ZipReadLimits(entryReadSlack: ZipReadLimits.MaxEntryReadSlack);
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, limits, Token);
        int before = source.Reads;

        byte[] content = await ZipArchiveReader.ReadEntryAsync(
            source, directory, directory.Entries[0], limits, cancellationToken: Token);

        Assert.Equal(payload, content);
        Assert.Equal(1, source.Reads - before);
        Assert.All(source.Ranges, range => Assert.True(range.End <= directory.DirectoryOffset || range.IsTail));
    }

    [Fact]
    public void Limits_RefuseASlackAboveTheMaximum_AtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ZipReadLimits(entryReadSlack: ZipReadLimits.MaxEntryReadSlack + 1));
        _ = new ZipReadLimits(entryReadSlack: ZipReadLimits.MaxEntryReadSlack);
    }

    // --- Real asset: local extra field longer than the directory declares --

    [Fact]
    public async Task RealAsset_LocalExtraLongerThanCentral_NeedsExactlyOneFollowUpAtSlackZero_AndNoneWithSlack()
    {
        byte[] archive = await File.ReadAllBytesAsync(FixturePath("pclstorage.1.0.2.nupkg"), Token);
        var source = new CountingSource(new StreamRandomAccessSource(new MemoryStream(archive)));
        var slackless = new ZipReadLimits(entryReadSlack: 0);
        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, slackless, Token);
        using var oracle = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);

        int followUps = 0;
        foreach (ZipEntry entry in directory.Entries.Where(entry => entry.ExpandedLength > 0))
        {
            int before = source.Reads;
            byte[] content = await ZipArchiveReader.ReadEntryAsync(source, directory, entry, slackless, cancellationToken: Token);
            int reads = source.Reads - before;
            Assert.InRange(reads, 1, 2);
            followUps += reads - 1;
            using Stream expected = oracle.GetEntry(entry.Name)!.Open();
            using var expectedBytes = new MemoryStream();
            await expected.CopyToAsync(expectedBytes, Token);
            Assert.Equal(expectedBytes.ToArray(), content);
        }

        Assert.True(followUps > 0, "the asset must exercise the follow-up read");

        var slack = new ZipReadLimits(entryReadSlack: 1024);
        foreach (ZipEntry entry in directory.Entries.Where(entry => entry.ExpandedLength > 0))
        {
            int before = source.Reads;
            await ZipArchiveReader.ReadEntryAsync(source, directory, entry, slack, cancellationToken: Token);
            Assert.Equal(1, source.Reads - before);
        }
    }

    // --- Over HTTP, including headers hidden -------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverHttp_SmallArchive_IsReadWholeFromTheTail(bool hideContentRange)
    {
        byte[] payload = Bytes(5_000);
        byte[] archive = Archive(("lib/net8.0/a.dll", payload));
        Assert.True(archive.Length < 65_557);
        var handler = new RangeHandler(archive) { HideContentRange = hideContentRange };
        await using var source = new HttpRangeSource(new HttpClient(handler), new Uri("https://feed.example/a.nupkg"));

        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token);
        byte[] content = await ZipArchiveReader.ReadEntryAsync(
            source, directory, directory.Entries[0], ZipReadLimits.Default, cancellationToken: Token);

        Assert.Equal(archive.Length, source.Length);
        Assert.Equal(payload, content);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverHttp_LargeArchive_ReadsTheTailThenOneEntry(bool hideContentRange)
    {
        byte[] payload = Bytes(20_000);
        byte[] archive = Archive(("filler", Bytes(200_000)), ("lib/net8.0/a.dll", payload));
        var handler = new RangeHandler(archive) { HideContentRange = hideContentRange, ETag = "\"v1\"" };
        await using var source = new HttpRangeSource(new HttpClient(handler), new Uri("https://feed.example/a.nupkg"));

        ZipDirectory directory = await ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token);
        byte[] content = await ZipArchiveReader.ReadEntryAsync(
            source, directory, directory.Find("lib/net8.0/a.dll")!, ZipReadLimits.Default, cancellationToken: Token);

        Assert.Equal(archive.Length, source.Length);
        Assert.Equal(payload, content);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotNull(handler.Requests[1].Headers.IfRange);
    }

    [Fact]
    public async Task OverHttp_TailTotalDisagreeingWithTheRecord_IsInvalidResponse()
    {
        byte[] archive = Archive(("filler", Bytes(200_000)), ("a", Bytes(10)));
        var handler = new RangeHandler(archive) { ReportedTotal = archive.Length + 5 };
        await using var source = new HttpRangeSource(new HttpClient(handler), new Uri("https://feed.example/a.nupkg"));

        RangeFetchException refused = await Assert.ThrowsAsync<RangeFetchException>(
            () => ZipArchiveReader.ReadDirectoryAsync(source, ZipReadLimits.Default, Token));

        Assert.Equal(RangeFetchFailure.InvalidResponse, refused.Failure);
    }

    // --- Helpers -----------------------------------------------------------

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        for (int index = 0; index < length; index++)
            bytes[index] = (byte)(index * 131 + 17);
        return bytes;
    }

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Archive(params (string Name, byte[] Content)[] entries) =>
        Archive(CompressionLevel.Optimal, entries);

    private static byte[] Archive(CompressionLevel level, params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(name, level).Open();
                entry.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static void AssertMatchesOracle(byte[] archive, ZipDirectory directory)
    {
        using var oracle = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        Assert.Equal(oracle.Entries.Count, directory.Entries.Count);
        foreach (ZipArchiveEntry expected in oracle.Entries)
        {
            ZipEntry actual = directory.Find(expected.FullName)!;
            Assert.NotNull(actual);
            Assert.Equal(expected.Length, actual.ExpandedLength);
            Assert.Equal(expected.CompressedLength, actual.CompressedLength);
            Assert.Equal(expected.Crc32, actual.Crc);
        }
    }

    private static int FindEndOfCentralDirectory(byte[] archive)
    {
        for (int offset = archive.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offset)) == 0x06054b50)
                return offset;
        }

        throw new InvalidOperationException("no record");
    }

    private static int FindCentralEntry(byte[] archive, string name)
    {
        int record = FindEndOfCentralDirectory(archive);
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(record + 16));
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        while (offset < record)
        {
            Assert.Equal(0x02014b50u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offset)));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(offset + 28));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(offset + 30));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(offset + 32));
            if (archive.AsSpan(offset + 46, nameLength).SequenceEqual(nameBytes))
                return offset;
            offset += 46 + nameLength + extraLength + commentLength;
        }

        throw new InvalidOperationException("no entry");
    }

    private static int FindLocalHeader(byte[] archive, string name)
    {
        int central = FindCentralEntry(archive, name);
        return (int)BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(central + 42));
    }

    private static void PatchCentralField(byte[] archive, string name, int fieldOffset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(FindCentralEntry(archive, name) + fieldOffset), value);

    private static void PatchCentralField16(byte[] archive, string name, int fieldOffset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(FindCentralEntry(archive, name) + fieldOffset), value);
        // Keep the local header in agreement so the method check, not the header check, fires.
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(FindLocalHeader(archive, name) + fieldOffset - 2), value);
    }

    private static void PatchLocalField(byte[] archive, string name, int fieldOffset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(FindLocalHeader(archive, name) + fieldOffset), value);

    private static void PatchExpandedLength(byte[] archive, string name, uint value)
    {
        PatchCentralField(archive, name, 24, value);
        PatchLocalField(archive, name, 22, value);
    }

    /// <summary>Counts reads and records their extents so clamping can be asserted.</summary>
    private sealed class CountingSource(RandomAccessSource inner) : RandomAccessSource
    {
        public int Reads { get; private set; }
        public List<(long Start, long End, bool IsTail)> Ranges { get; } = [];

        public override async ValueTask<ReadOnlyMemory<byte>> ReadTailAsync(int maxLength, CancellationToken cancellationToken)
        {
            Reads++;
            ReadOnlyMemory<byte> tail = await inner.ReadTailAsync(maxLength, cancellationToken);
            Length = inner.Length;
            Ranges.Add((0, 0, true));
            return tail;
        }

        public override async ValueTask ReadRangeAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
        {
            Reads++;
            Ranges.Add((offset, offset + destination.Length, false));
            await inner.ReadRangeAsync(offset, destination, cancellationToken);
        }

        public override void ConfirmLength(long length)
        {
            inner.ConfirmLength(length);
            base.ConfirmLength(length);
        }
    }

    /// <summary>A byte-range server with the deviations the ZIP gates need.</summary>
    private sealed class RangeHandler(byte[] representation) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public bool HideContentRange { get; set; }
        public string? ETag { get; set; }
        public long? ReportedTotal { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RangeItemHeaderValue range = request.Headers.Range!.Ranges.First();
            long from;
            long to;
            if (range.From is null)
            {
                from = Math.Max(0, representation.Length - range.To!.Value);
                to = representation.Length - 1;
            }
            else
            {
                from = range.From.Value;
                to = Math.Min(range.To ?? representation.Length - 1, representation.Length - 1);
            }

            var content = new ByteArrayContent(representation[(int)from..(int)(to + 1)]);
            if (!HideContentRange)
                content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, ReportedTotal ?? representation.Length);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            if (ETag is { } etag)
                response.Headers.ETag = new EntityTagHeaderValue(etag);
            return Task.FromResult(response);
        }
    }
}
