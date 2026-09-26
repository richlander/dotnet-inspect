using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

using CoreHttpClientFactory = DotnetInspector.Networking.HttpClientFactory;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    /// <summary>
    /// The exact-package search Root is realized from a ranged read: the
    /// archive directory and the selected compile folder only, never the
    /// large entry beside it. The entries are kept in the entry cache, so a
    /// second search makes no package request.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangedRead_TransfersOnlyTheSelectedAssembly()
    {
        string id = $"Workspace.Ranged.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] filler = RandomNumberGenerator.GetBytes(4 * 1024 * 1024);
        byte[] package = CreatePackage(
            id,
            "ranged search package",
            library: assembly,
            libraryName: "Workspace.Ranged.dll",
            extraEntries: [("content/filler.bin", filler)]);
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);

        string[] find =
        [
            "find",
            $".{MemberSearchServiceTests.SearchTargetMemberName}",
            "--package", $"{id}@{Version}",
            "--tfm", "net11.0",
            "--source", FirstFeed,
            "--all",
            "--json",
            "--verbose",
            "--tips", "q",
        ];
        var result = await RunCommandAsync(find);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            MemberSearchServiceTests.SearchTargetMemberName,
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("payload Ranged", result.Error, StringComparison.Ordinal);
        // The feed is a credential-free HTTP authority, so the ranged entries
        // are durable (docs/design/package-cache-policy.md).
        Assert.Contains("kept in the entry cache", result.Error, StringComparison.Ordinal);
        // The one complete response is the size probe, abandoned before its body.
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.True(feed.RangedResponses >= 2, $"ranged responses: {feed.RangedResponses}");
        Assert.True(
            feed.PackageBytesServed < assembly.Length + (1024 * 1024),
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");

        // The same search again answers from the entry cache with no package
        // request of either kind.
        int ranged = feed.RangedResponses;
        long served = feed.PackageBytesServed;
        var warm = await RunCommandAsync(find);

        Assert.True(warm.Exit == 0, warm.Error);
        Assert.Equal(result.Output, warm.Output);
        Assert.Contains("from the entry cache", warm.Error, StringComparison.Ordinal);
        Assert.Contains("no request", warm.Error, StringComparison.Ordinal);
        Assert.Equal(1, feed.FullPackageResponses);
        Assert.Equal(ranged, feed.RangedResponses);
        Assert.Equal(served, feed.PackageBytesServed);
    }

    /// <summary>
    /// The real Avalonia 12.1.2 archive (10.1 MB: net8.0 and net10.0 reference
    /// and implementation assemblies, XML docs, analyzers, designer tools)
    /// searched for net10.0 transfers the directory and the ref/net10.0
    /// folder only: about 2.2 MB, in the tail request and three spans.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangedRead_RealAvaloniaArchive()
    {
        const string Id = "Avalonia";
        const string AvaloniaVersion = "12.1.2";
        string path = Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "ApiMatching", "avalonia.12.1.2.nupkg");
        byte[] package = await File.ReadAllBytesAsync(
            path, TestContext.Current.CancellationToken);
        Assert.Equal(
            "99987414c63ac3993346a84a852006df963ff839f96d140557ee31f8850db08f",
            Convert.ToHexStringLower(SHA256.HashData(package)));
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, Id, package, version: AvaloniaVersion);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "find",
                ".InvalidateMeasure",
                "--package", $"{Id}@{AvaloniaVersion}",
                "--tfm", "net10.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("InvalidateMeasure", result.Output, StringComparison.Ordinal);
        Assert.Contains("payload Ranged", result.Error, StringComparison.Ordinal);
        // The one complete response is the size probe, abandoned before its body.
        Assert.Equal(1, feed.FullPackageResponses);
        // Search reads the surface only: the whole ref/net10.0 folder (11
        // assemblies and their 11 documentation files) and none of lib/. The
        // archive interleaves other folders' documentation into that folder,
        // so it is three spans, sent together after the tail.
        Assert.Contains("22 of 121 entries", result.Error, StringComparison.Ordinal);
        Assert.True(4 == feed.RangedResponses, string.Join("; ", feed.Ranges));
        const long RefFolderStart = 5_688_854;
        const long RefFolderEnd = 8_330_000;
        Assert.All(
            feed.Ranges.Skip(1),
            range => Assert.InRange(long.Parse(range.Split('-')[0]), RefFolderStart, RefFolderEnd));
        Assert.True(
            feed.PackageBytesServed < 2_500_000,
            $"served {feed.PackageBytesServed} of {package.Length} package bytes");
    }

    /// <summary>
    /// A package whose only library targets netstandard2.0 is searched for
    /// net11.0: the ranged read materializes the compatible slice the Root
    /// binds, so no complete download follows.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangedRead_CompatibleSliceNeedsNoCompleteDownload()
    {
        string id = $"Workspace.Compatible.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "compatible slice package",
            library: assembly,
            libraryName: "Workspace.Compatible.dll",
            libraryDirectory: "lib/netstandard2.0");
        var feed = new RangeHonoringFeedHandler(FirstFeed, id, package);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "find",
                $".{MemberSearchServiceTests.SearchTargetMemberName}",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            MemberSearchServiceTests.SearchTargetMemberName,
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("payload Ranged", result.Error, StringComparison.Ordinal);
        // The one complete response is the size probe, abandoned before its body.
        Assert.Equal(1, feed.FullPackageResponses);
    }

    /// <summary>
    /// A source that answers a ranged request with the whole archive is
    /// refused as <c>RangeIgnored</c> and the same authority serves the
    /// complete download, so the search still completes.
    /// </summary>
    [Fact]
    public async Task SearchCommand_RangeIgnored_FallsBackToTheCompleteDownload()
    {
        string id = $"Workspace.RangeIgnored.{Guid.NewGuid():N}";
        byte[] assembly = await File.ReadAllBytesAsync(
            typeof(ConfiguredPayloadAcquisitionTests).Assembly.Location,
            TestContext.Current.CancellationToken);
        byte[] package = CreatePackage(
            id,
            "range-ignoring package",
            library: assembly,
            libraryName: "Workspace.RangeIgnored.dll");
        var feed = new RangeHonoringFeedHandler(
            FirstFeed, id, package, ignoreRange: true);
        UseFeed(feed);

        var result = await RunCommandAsync(
            [
                "find",
                $".{MemberSearchServiceTests.SearchTargetMemberName}",
                "--package", $"{id}@{Version}",
                "--tfm", "net11.0",
                "--source", FirstFeed,
                "--all",
                "--json",
                "--verbose",
                "--tips", "q",
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            MemberSearchServiceTests.SearchTargetMemberName,
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("RangeIgnored", result.Error, StringComparison.Ordinal);
        Assert.Contains("payload Download", result.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// What an abandoned size probe may read of a body that has already
    /// arrived before it finds a read that must wait for the network. On a
    /// socket those bytes were already received; an in-memory feed serves
    /// them on demand, so its served-byte gates allow for them.
    /// </summary>
    private const long AbandonedProbeReadBound = 64 * 1024;

    private static void UseFeed(HttpMessageHandler feed)
    {
        CoreHttpClientFactory.SetAuthenticationDecorator(_ => feed);
        CoreHttpClientFactory.ResetSharedForTesting();
        CoreHttpClientFactory.SetPackageSourceHandlerForTesting(_ => feed);
    }

    /// <summary>
    /// A v3 feed whose flat container honors single <c>Range</c> requests
    /// (suffix and absolute) with <c>206</c>, <c>Content-Range</c>, and an
    /// <c>ETag</c>, counting what it transfers.
    /// </summary>
    private sealed class RangeHonoringFeedHandler(
        string source,
        string id,
        byte[] package,
        bool ignoreRange = false,
        string version = Version) : HttpMessageHandler
    {
        private const string ETag = "\"ranged-fixture\"";
        private long _packageBytesServed;
        private int _rangedResponses;
        private int _fullPackageResponses;

        public long PackageBytesServed => Interlocked.Read(ref _packageBytesServed);

        public int RangedResponses => Volatile.Read(ref _rangedResponses);

        public int FullPackageResponses => Volatile.Read(ref _fullPackageResponses);

        public ConcurrentQueue<string> Requests { get; } = new();

        public ConcurrentQueue<string> Ranges { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requests.Enqueue(url);
            string flat = new Uri(new Uri(source), "flat2/").AbsoluteUri;
            string lower = id.ToLowerInvariant();
            string packageUrl = $"{flat}{lower}/{version}/{lower}.{version}.nupkg";
            if (url == source)
            {
                return Task.FromResult(Respond(
                    request,
                    HttpStatusCode.OK,
                    new StringContent($$"""
                        {"version":"3.0.0","resources":[
                          {"@id":"{{flat}}","@type":"PackageBaseAddress/3.0.0"}
                        ]}
                        """)));
            }
            if (url != packageUrl)
                throw new InvalidOperationException($"Unexpected ranged-feed request: {url}");

            RangeItemHeaderValue? range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (ignoreRange || range is null)
            {
                // Count the bytes actually read: size first abandons a complete
                // response before its body.
                Interlocked.Increment(ref _fullPackageResponses);
                HttpResponseMessage full = Respond(
                    request,
                    HttpStatusCode.OK,
                    new StreamContent(new CountingStream(package, this)));
                full.Content.Headers.ContentLength = package.Length;
                full.Headers.ETag = new EntityTagHeaderValue(ETag);
                return Task.FromResult(full);
            }

            long start;
            long end;
            if (range.From is null)
            {
                long suffix = Math.Min(range.To!.Value, package.Length);
                start = package.Length - suffix;
                end = package.Length - 1;
            }
            else
            {
                start = range.From.Value;
                end = Math.Min(range.To ?? package.Length - 1, package.Length - 1);
            }

            int length = checked((int)(end - start + 1));
            Ranges.Enqueue($"{start}-{end}");
            Interlocked.Increment(ref _rangedResponses);
            Interlocked.Add(ref _packageBytesServed, length);
            var content = new ByteArrayContent(package, (int)start, length);
            content.Headers.ContentRange =
                new ContentRangeHeaderValue(start, end, package.Length);
            HttpResponseMessage partial = Respond(
                request, HttpStatusCode.PartialContent, content);
            partial.Headers.ETag = new EntityTagHeaderValue(ETag);
            partial.Headers.AcceptRanges.Add("bytes");
            return Task.FromResult(partial);
        }

        internal void AddServed(int count) =>
            Interlocked.Add(ref _packageBytesServed, count);

        /// <summary>
        /// A read-only archive body that counts each byte a client reads
        /// exactly once, whichever read overload the client uses.
        /// </summary>
        private sealed class CountingStream(byte[] bytes, RangeHonoringFeedHandler owner)
            : Stream
        {
            private int _position;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => bytes.Length;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                int read = Math.Min(buffer.Length, bytes.Length - _position);
                bytes.AsSpan(_position, read).CopyTo(buffer);
                _position += read;
                owner.AddServed(read);
                return read;
            }

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(Read(buffer.Span));

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                Task.FromResult(Read(buffer.AsSpan(offset, count)));

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) =>
                throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }

        private static HttpResponseMessage Respond(
            HttpRequestMessage request,
            HttpStatusCode status,
            HttpContent content) =>
            new(status)
            {
                Content = content,
                RequestMessage = request,
            };
    }
}
