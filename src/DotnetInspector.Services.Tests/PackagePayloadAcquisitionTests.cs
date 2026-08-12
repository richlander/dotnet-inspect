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

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected network request: {request.RequestUri}");
    }
}
