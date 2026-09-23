using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NuGetFetch;
using ZipFetch;

namespace NuGetFetch.Tests;

/// <summary>
/// The Package archive range access capability on the v3 and gallery
/// clients (docs/design/package-archive-range-access.md): the adapter's
/// outcome mapping, its credential and validator handling, the non-ranged
/// memory, and the deadline gate.
/// </summary>
public sealed partial class PackageSourceClientTests
{
    private static readonly string ServiceIndexWithFlatContainer = $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "{{FlatContainer}}", "@type": "PackageBaseAddress/3.0.0" }
          ]
        }
        """;

    private static byte[] FixtureArchive() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "pclstorage.1.0.2.nupkg"));

    private static byte[] SmallArchive(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static IPackageArchiveRangeSource Ranged(IPackageSourceClient client) =>
        Assert.IsAssignableFrom<IPackageArchiveRangeSource>(client);

    private static async Task<T> Opened<T>(PackageArchiveReadResult<T> result)
        where T : class
    {
        await Task.CompletedTask;
        Assert.Null(result.Failure);
        Assert.Null(result.Refusal);
        return Assert.IsType<T>(result.Value);
    }

    [Fact]
    public async Task ArchiveRange_V3_ReadsDirectoryAndEntry_WithCredentialAndValidator()
    {
        byte[] archive = FixtureArchive();
        var server = new RangeServer(archive) { ETag = "\"v1\"" };
        var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
        handler.SetResponse(Package, server.Respond);
        var source = new PackageSource("corporate", ServiceIndex, new PackageSourceCredential("user", "token"));
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(source, handler);

        await using PackageArchiveReader reader = await Opened(
            await Ranged(runtime).OpenArchiveAsync(
                "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken));
        ZipEntry nuspec = reader.Directory.Entries.Single(entry => entry.Name.EndsWith(".nuspec", StringComparison.Ordinal));
        PackageArchiveEntryContent content = await Opened(
            await reader.ReadEntryAsync(nuspec, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(archive.Length, reader.Directory.ArchiveLength);
        Assert.Equal(BinaryFetch.RangeValidatorKind.EntityTag, reader.Validator);
        Assert.Contains("PCLStorage", Encoding.UTF8.GetString(content.Content.Span));
        Assert.Equal(runtime.Source, reader.Source);
        // Every ranged request carried the credential; the entry request carried If-Range.
        int[] ranged = handler.Requested
            .Select((url, index) => (url, index))
            .Where(item => item.url.Equals(Package, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .ToArray();
        Assert.True(ranged.Length >= 2, "the tail and the entry are separate requests");
        Assert.All(ranged, index => Assert.NotNull(handler.Authentication[index]));
        Assert.True(handler.Headers[ranged[^1]].ContainsKey("If-Range"));
    }

    [Fact]
    public async Task ArchiveRange_Gallery_ReadsTheDirectory()
    {
        byte[] archive = SmallArchive(("lib/net8.0/contoso.dll", Encoding.UTF8.GetBytes("MZ-not-really")));
        var handler = new RecordingHandler();
        handler.SetResponse(GalleryPackage, new RangeServer(archive).Respond);
        using IPackageSourceClient runtime = PackageSourceClientFactory.CreateGallery(handler);

        await using PackageArchiveReader reader = await Opened(
            await Ranged(runtime).OpenArchiveAsync(
                "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken));
        PackageArchiveEntryContent content = await Opened(
            await reader.ReadEntryAsync(reader.Directory.Entries[0], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("MZ-not-really", Encoding.UTF8.GetString(content.Content.Span));
        Assert.Equal(BinaryFetch.RangeValidatorKind.None, reader.Validator);
    }

    [Fact]
    public async Task ArchiveRange_RangeIgnored_IsRememberedOnTheClient()
    {
        var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
        handler.SetResponse(Package, new RangeServer(SmallArchive(("a", [1, 2, 3]))) { IgnoreRange = true }.Respond);
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(new PackageSource("feed", ServiceIndex), handler);

        PackageArchiveReadResult<PackageArchiveReader> first = await Ranged(runtime).OpenArchiveAsync(
            "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken);
        int requestsAfterFirst = handler.Requested.Count;
        PackageArchiveReadResult<PackageArchiveReader> second = await Ranged(runtime).OpenArchiveAsync(
            "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken);

        Assert.Equal(PackageArchiveReadRefusal.RangeIgnored, first.Refusal);
        Assert.Equal(PackageArchiveReadRefusal.RangeIgnored, second.Refusal);
        Assert.Equal(requestsAfterFirst, handler.Requested.Count);
        // The full fetch on the same client still works: the memory is range-specific.
        PackageSourcePayload payload = Succeeded(await runtime.GetPackageAsync("contoso", "1.0.0", TestContext.Current.CancellationToken));
        await payload.Content.DisposeAsync();
    }

    [Fact]
    public async Task ArchiveRange_TransientStatus_IsRetried_LikeTheFullFetch()
    {
        var server = new RangeServer(SmallArchive(("a", [1, 2, 3]))) { TransientFailuresFirst = 1 };
        var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
        handler.SetResponse(Package, server.Respond);
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(new PackageSource("feed", ServiceIndex), handler);

        await using PackageArchiveReader reader = await Opened(
            await Ranged(runtime).OpenArchiveAsync(
                "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken));

        // The first 503 was retried inside the send; the open still succeeded.
        Assert.Equal(2, server.Requests);
        Assert.Single(reader.Directory.Entries);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PackageSourceFailureKind.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, PackageSourceFailureKind.AuthenticationRequired)]
    [InlineData(HttpStatusCode.Forbidden, PackageSourceFailureKind.AuthenticationRequired)]
    [InlineData(HttpStatusCode.InternalServerError, PackageSourceFailureKind.Transport)]
    public async Task ArchiveRange_TransportStatus_MapsToTheOrdinarySourceFailure(HttpStatusCode status, PackageSourceFailureKind expected)
    {
        var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
        handler.SetResponse(Package, new RangeServer(SmallArchive(("a", [1]))) { Status = status }.Respond);
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(new PackageSource("feed", ServiceIndex), handler);

        PackageArchiveReadResult<PackageArchiveReader> result = await Ranged(runtime).OpenArchiveAsync(
            "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken);

        Assert.Null(result.Value);
        Assert.Equal(expected, result.Failure!.Kind);
        Assert.Equal(PackageSourceCapabilities.PackagePayload, result.Failure.Capability);
        Assert.Equal("contoso", result.Failure.Coordinate!.PackageId);
        Assert.Same(runtime.Source, result.Failure.Source);
    }

    [Fact]
    public async Task ArchiveRange_ValidatorChangeBetweenRequests_IsArchiveChanged()
    {
        byte[] archive = FixtureArchive();
        var server = new RangeServer(archive) { ETag = "\"v1\"" };
        var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
        handler.SetResponse(Package, server.Respond);
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(new PackageSource("feed", ServiceIndex), handler);
        await using PackageArchiveReader reader = await Opened(
            await Ranged(runtime).OpenArchiveAsync(
                "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken));

        server.ETag = "\"v2\"";
        ZipEntry outsideTail = reader.Directory.Entries.First(entry => entry.LocalHeaderOffset < reader.Directory.ArchiveLength - 65_557);
        PackageArchiveReadResult<PackageArchiveEntryContent> result = await reader.ReadEntryAsync(
            outsideTail, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PackageArchiveReadRefusal.ArchiveChanged, result.Refusal);
    }

    [Fact]
    public async Task ArchiveRange_ArchiveDefects_MapToTheZipVocabulary()
    {
        byte[] malformed = new byte[500];
        byte[] tooMany = SmallArchive(("a", [1]), ("b", [2]));
        byte[] zip64 = SmallArchive(("a", [1]));
        int record = zip64.Length - 22;
        BinaryPrimitives.WriteUInt16LittleEndian(zip64.AsSpan(record + 10), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(zip64.AsSpan(record + 8), ushort.MaxValue);

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, (await OpenV3Async(malformed, ZipReadLimits.Default)).Failure!.Kind);
        Assert.Equal(PackageSourceFailureKind.ResponseRejected, (await OpenV3Async(tooMany, new ZipReadLimits(maxEntryCount: 1))).Failure!.Kind);
        Assert.Equal(PackageArchiveReadRefusal.ArchiveUnsupported, (await OpenV3Async(zip64, new ZipReadLimits(maxEntryCount: ushort.MaxValue))).Refusal);

        static async Task<PackageArchiveReadResult<PackageArchiveReader>> OpenV3Async(byte[] archive, ZipReadLimits limits)
        {
            var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
            handler.SetResponse(Package, new RangeServer(archive).Respond);
            using IPackageSourceClient runtime = PackageSourceClientFactory.Create(new PackageSource("feed", ServiceIndex), handler);
            PackageArchiveReadResult<PackageArchiveReader> result = await Ranged(runtime).OpenArchiveAsync(
                "contoso", "1.0.0", limits, TestContext.Current.CancellationToken);
            if (result.Value is { } reader)
                await reader.DisposeAsync();
            return result;
        }
    }

    [Fact]
    public async Task ArchiveRange_OperationCeilingDuringAnEntryRead_IsATypedTimeout_WithNoContent()
    {
        byte[] archive = FixtureArchive();
        var server = new RangeServer(archive) { StallRanges = true };
        var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
        handler.SetResponse(Package, server.Respond);
        // The request deadline is far above the operation ceiling, so the
        // ceiling is what ends the stalled body read.
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(20),
            OperationTimeout = TimeSpan.FromMilliseconds(800),
        };
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(new PackageSource("feed", ServiceIndex), handler, options);
        await using PackageArchiveReader reader = await Opened(
            await Ranged(runtime).OpenArchiveAsync(
                "contoso", "1.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken));
        ZipEntry outsideTail = reader.Directory.Entries.First(entry => entry.LocalHeaderOffset < reader.Directory.ArchiveLength - 65_557);

        PackageArchiveReadResult<PackageArchiveEntryContent> result = await reader.ReadEntryAsync(
            outsideTail, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result.Value);
        Assert.Equal(PackageSourceFailureKind.Timeout, result.Failure!.Kind);
    }

    [Fact]
    public async Task ArchiveRange_CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler { [ServiceIndex] = ServiceIndexWithFlatContainer };
        handler.SetResponse(Package, request =>
        {
            cancellation.Cancel();
            return new RangeServer(SmallArchive(("a", [1]))).Respond(request);
        });
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(new PackageSource("feed", ServiceIndex), handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Ranged(runtime).OpenArchiveAsync("contoso", "1.0.0", ZipReadLimits.Default, cancellation.Token));
    }

    [Fact]
    [Trait("Network", "Live")]
    [Trait("Speed", "Slow")]
    public async Task ArchiveRange_MotivatingAsset_ReadsOneReferenceAssemblyFromNuGetOrg()
    {
        using IPackageSourceClient runtime = PackageSourceClientFactory.Create(
            new PackageSource("nuget.org", "https://api.nuget.org/v3/index.json"));

        await using PackageArchiveReader reader = await Opened(
            await Ranged(runtime).OpenArchiveAsync(
                "Microsoft.NETCore.App.Ref", "9.0.0", ZipReadLimits.Default, TestContext.Current.CancellationToken));
        ZipEntry runtimeAssembly = reader.Directory.Find("ref/net9.0/System.Runtime.dll")!;
        PackageArchiveEntryContent content = await Opened(
            await reader.ReadEntryAsync(runtimeAssembly, cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(reader.Directory.Entries.Count > 200);
        Assert.Equal(runtimeAssembly.ExpandedLength, (uint)content.Content.Length);
        Assert.Equal((byte)'M', content.Content.Span[0]);
        Assert.Equal((byte)'Z', content.Content.Span[1]);
    }

    /// <summary>Serves one archive by byte range, with the deviations the adapter gates need.</summary>
    private sealed class RangeServer(byte[] representation)
    {
        public bool IgnoreRange { get; set; }
        public bool StallRanges { get; set; }
        public string? ETag { get; set; }
        public HttpStatusCode? Status { get; set; }

        /// <summary>How many leading requests answer 503 before the server serves ranges.</summary>
        public int TransientFailuresFirst { get; set; }

        public int Requests { get; private set; }

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Requests++;
            if (Status is { } status)
                return new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };
            if (TransientFailuresFirst > 0)
            {
                TransientFailuresFirst--;
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new ByteArrayContent([]) };
            }
            RangeItemHeaderValue? range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (IgnoreRange || range is null)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(representation) };

            long from;
            long to;
            if (range.From is null)
            {
                from = Math.Max(0, representation.Length - range.To!.Value);
                to = representation.Length - 1;
            }
            else
            {
                if (StallRanges)
                    return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(new StallingStream()) };
                from = range.From.Value;
                to = Math.Min(range.To ?? representation.Length - 1, representation.Length - 1);
            }

            var content = new ByteArrayContent(representation[(int)from..(int)(to + 1)]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, representation.Length);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            if (ETag is { } etag)
                response.Headers.ETag = new EntityTagHeaderValue(etag);
            return response;
        }
    }

    /// <summary>A body that never delivers a byte and honors cancellation, so a deadline ends it.</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
