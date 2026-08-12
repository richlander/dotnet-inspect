using System.Net;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Host-neutral payload acquisition: a store answers first, only the
/// coordinate's authorized sources are consulted, and the producer that served
/// the bytes stays with them.
/// </summary>
public sealed class PackagePayloadAcquisitionTests
{
    static readonly PackageSource NuGetOrg = PackageSource.NuGetOrg;
    static readonly PackageSource Primary =
        new("primary", "https://primary.test/v3/index.json");

    const string PackageId = "sample.package";
    const string Version = "1.2.3";
    const string NupkgUrl =
        $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{Version}/{PackageId}.{Version}.nupkg";

    [Fact]
    public async Task CacheMiss_DownloadsAndCommitsWithProducerIdentity()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NuGetOrgPayloadHandler(nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(PackagePayloadOrigin.Download, payload.Origin);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
        Assert.Equal(
            "lib/net10.0/Sample.dll",
            Assert.Single(payload.Content.EnumerateEntries()));
    }

    [Fact]
    public async Task CacheHit_AnswersWithoutNetworkWork()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(PackagePayloadOrigin.Cache, payload.Origin);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
    }

    [Fact]
    public async Task CachedContentOfAnUnauthorizedProducer_IsNotServed()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(Primary.Url),
            new MemoryStream(nupkg),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new NotFoundHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    [Fact]
    public async Task SourcesAreTriedInOrderUntilOneServesThePayload()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        var handler = new NuGetOrgPayloadHandler(nupkg);
        using var client = new HttpClient(handler);

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary, NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);
        Assert.Contains(
            handler.Requests,
            url => url.StartsWith("https://primary.test/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoSourceServingTheCoordinate_IsUnavailable()
    {
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NotFoundHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Contains(PackageId, unavailable.Message, StringComparison.Ordinal);
        Assert.Contains("nuget.org", unavailable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithNoAuthorizedSource_IsUnavailable()
    {
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new FailingHandler());

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    [Fact]
    public async Task Acquisition_ObservesCancellationBeforeStoreOrSourceWork()
    {
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new FailingHandler());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task UnboundedChunkedPayload_IsRejectedWithoutContentLength()
    {
        // The response never advertises a length, so only counting the bytes
        // that actually arrive can bound it.
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new NuGetOrgHandler(() => new StreamContent(new EndlessStream())));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxArchiveBytes = 4096 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task AdvertisedOversizePayload_IsATypedSourceFailure()
    {
        var store = new InMemoryPackageStore();
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        using var client = new HttpClient(
            new NuGetOrgHandler(() => new ByteArrayContent(nupkg)));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxArchiveBytes = 16 },
                cancellationToken: TestContext.Current.CancellationToken);

        // An oversized payload stays an outcome rather than an exception, so
        // the remaining authorized sources are still tried.
        Assert.IsType<PackagePayloadResult.Unavailable>(result);
    }

    [Fact]
    public async Task InvalidArchiveFromOneSource_LetsTheNextSourceServe()
    {
        byte[] nupkg = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(
            new TwoSourceHandler(
                primaryContent: "this is not a zip archive"u8.ToArray(),
                nuGetOrgContent: nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(Primary, NuGetOrg),
                store,
                cancellationToken: TestContext.Current.CancellationToken);

        AcquiredPackagePayload payload = Acquired(result);
        Assert.Equal(
            NuGetCache.GetSourceKey(NuGetOrg.Url),
            payload.ProducerKey);

        // The unusable payload never entered the cache under the source that
        // served it, so a later run is not answered from poisoned bytes.
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(Primary.Url)]));
    }

    [Fact]
    public async Task ArchiveDeclaringTooManyEntries_IsRejected()
    {
        byte[] nupkg = TestPackageArchive.Create(
            "lib/net10.0/One.dll",
            "lib/net10.0/Two.dll",
            "lib/net10.0/Three.dll");
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NuGetOrgPayloadHandler(nupkg));

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxEntryCount = 2 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task ArchiveDeclaringTooMuchExpandedContent_IsRejected()
    {
        // A small archive whose entries expand far beyond it: the bound is on
        // what the archive declares it will become, not on what it weighs.
        byte[] nupkg = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/Bomb.dll", new byte[512 * 1024]));
        var store = new InMemoryPackageStore();
        using var client = new HttpClient(new NuGetOrgPayloadHandler(nupkg));

        Assert.True(nupkg.Length < 64 * 1024);

        PackagePayloadResult result =
            await PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                limits: new PackagePayloadLimits { MaxExpandedBytes = 4096 },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<PackagePayloadResult.Unavailable>(result);
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    [Fact]
    public async Task Acquisition_ObservesCancellationDuringDownload()
    {
        var store = new InMemoryPackageStore();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(
            new NuGetOrgHandler(
                () => new StreamContent(
                    new EndlessStream(onRead: cancellation.Cancel))));

        // Cancellation requested while the body is being copied is a
        // cancellation, not a source failure that would try the next feed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackagePayloadAcquisition.AcquireAsync(
                client,
                Coordinate(NuGetOrg),
                store,
                cancellationToken: cancellation.Token));
        Assert.Null(
            store.TryGetCached(
                PackageId,
                Version,
                [NuGetCache.GetSourceKey(NuGetOrg.Url)]));
    }

    static AcquiredPackagePayload Acquired(PackagePayloadResult result)
        => Assert.IsType<PackagePayloadResult.Acquired>(result).Payload;

    static ResolvedPackageCoordinate Coordinate(
        params PackageSource[] sources)
        => new(
            PackageId,
            Version,
            "net10.0",
            runtimeIdentifier: null,
            sources,
            wasFloating: false);

    sealed class NuGetOrgPayloadHandler(byte[] nupkg) : HttpMessageHandler
    {
        readonly List<string> _requests = [];

        internal IReadOnlyList<string> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (_requests)
                _requests.Add(url);

            if (url.Equals(
                $"https://api.nuget.org/v3-flatcontainer/{PackageId}/{Version}/{PackageId}.{Version}.nupkg",
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nupkg),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Answers the nuget.org flat-container URL for this coordinate with
    /// caller-supplied content, so a test can shape the response body
    /// (advertised length, endlessness, cancellation) rather than only its
    /// bytes.
    /// </summary>
    sealed class NuGetOrgHandler(Func<HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            return Task.FromResult(
                url.Equals(NupkgUrl, StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = content(),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>
    /// A non-seekable stream that never ends, so the response advertises no
    /// length and only a bound on bytes actually read can stop it.
    /// </summary>
    sealed class EndlessStream(Action? onRead = null) : Stream
    {
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
            onRead?.Invoke();
            buffer.AsSpan(offset, count).Clear();
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            buffer.Span.Clear();
            return ValueTask.FromResult(buffer.Length);
        }

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

    /// <summary>
    /// Two feeds with their own flat containers, so a payload one source
    /// cannot serve usably can be answered by the next authorized source.
    /// </summary>
    sealed class TwoSourceHandler(
        byte[] primaryContent,
        byte[] nuGetOrgContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            if (url.Equals(Primary.Url, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {"resources":[{"@id":"https://primary.test/flat/","@type":"PackageBaseAddress/3.0.0"}]}
                            """),
                    });
            }

            if (url.Equals(
                $"https://primary.test/flat/{PackageId}/{Version}/{PackageId}.{Version}.nupkg",
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(primaryContent),
                    });
            }

            return Task.FromResult(
                url.Equals(NupkgUrl, StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(nuGetOrgContent),
                    }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
