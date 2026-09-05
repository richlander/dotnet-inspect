using System.Net;
using System.Text;

namespace NuGetFetch.Tests;

public sealed class GalleryDiscoveryClientTests
{
    private const string Row = """
        {"PackageRegistration":{"Id":"Cake.Tool","DownloadCount":9000000,
        "Verified":true,"Owners":["cake-build"]},"Version":"6.2.0",
        "NormalizedVersion":"6.2.0","Description":"Build automation","DownloadCount":12}
        """;

    [Fact]
    public async Task GalleryDiscoveryUsesSearchMetadataOnly()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            $$"""{"totalHits":6000,"data":[{{Row}}]}""")));
        var association = PackageSourceAssociation.Create();
        using INuGetGalleryPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(association, handler);
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery, 2,
            packageType: NuGetGalleryPackageType.DotnetTool);

        var outcome = await source.DiscoverAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(outcome.Failure);
        NuGetGalleryDiscoveryResult result = Assert.IsType<NuGetGalleryDiscoveryResult>(outcome.Value);
        Assert.Same(request, result.Request);
        Assert.Same(source.Source, result.Source);
        Assert.Same(association, result.Source.Association);
        Assert.Equal(PackageProducerIdentity.NuGetOrg, result.Source.Producer);
        Assert.Equal(6000, result.EstimatedTotalHits);
        NuGetGalleryDiscoveryMatch match = Assert.Single(result.Matches);
        Assert.Equal("Cake.Tool", match.PackageId);
        Assert.Equal("6.2.0", match.Version);
        Assert.Equal(9_000_000, match.TotalDownloads);
        Assert.True(match.Verified);
        Assert.Equal(["cake-build"], match.Owners);
        Assert.Equal("Build automation", match.Description);
        Assert.Equal(PackageSourceCoordinate.Create("cake.tool", "6.2.0"), match.Candidate.Coordinate);
        Assert.Same(result.Source, match.Candidate.Source);
        Assert.Equal(PackageListingState.Listed, match.Candidate.ListingState);
        Assert.Equal(
            "https://azuresearch-usnc.nuget.org/search/query?packageType=dotnettool&sortBy=totalDownloads-desc&prerelease=false&semVerLevel=2.0.0&skip=0&take=2",
            Assert.Single(handler.Urls));
    }

    [Theory]
    [InlineData(null, null, "totalDownloads-desc", false)]
    [InlineData("json", null, "relevance", false)]
    [InlineData("owner:someone tags:\"a b\"&take=1", NuGetGalleryDiscoveryOrder.MostDownloaded, "totalDownloads-desc", true)]
    [InlineData(null, NuGetGalleryDiscoveryOrder.Relevance, "relevance", true)]
    public async Task DiscoveryEncodesExactInputWithoutPrefixOrPagination(
        string? text, NuGetGalleryDiscoveryOrder? order, string wireOrder, bool prerelease)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json("""{"data":[]}""")));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery, 1000, text,
            NuGetGalleryPackageType.Template, order, prerelease);

        Assert.NotNull((await source.DiscoverAsync(request, TestContext.Current.CancellationToken)).Value);

        string url = Assert.Single(handler.Urls);
        Assert.Contains("take=1000", url);
        Assert.Contains("skip=0", url);
        Assert.Contains("packageType=template", url);
        Assert.Contains($"sortBy={wireOrder}", url);
        Assert.Contains($"prerelease={prerelease.ToString().ToLowerInvariant()}", url);
        if (text is null)
            Assert.DoesNotContain("q=", url);
        else
            Assert.Contains($"q={Uri.EscapeDataString(text)}", url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"totalHits\":null,")]
    [InlineData("\"totalHits\":-1,")]
    [InlineData("\"totalHits\":\"many\",")]
    [InlineData("\"totalHits\":1.5,")]
    [InlineData("\"totalHits\":9223372036854775808,")]
    [InlineData("\"totalHits\":1,\"totalHits\":2,")]
    public async Task UnusablePopulationEstimateDoesNotInvalidateRows(string estimate)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            $$"""{ {{estimate}} "data":[{{Row}}] }""")));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var result = await source.DiscoverAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Null(result.Failure);
        Assert.Null(result.Value!.EstimatedTotalHits);
        Assert.Single(result.Value.Matches);
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"DownloadCount\":null,\"Verified\":null,\"Owners\":null")]
    [InlineData(",\"DownloadCount\":-1,\"Verified\":\"yes\",\"Owners\":[17]")]
    [InlineData(",\"DownloadCount\":1,\"DownloadCount\":2,\"Verified\":true,\"Verified\":false")]
    public async Task RelevanceRetainsUnavailableMetadataWithoutInventingFacts(string optional)
    {
        string row = $$"""
            {"PackageRegistration":{"Id":"Sample"{{optional}}},
            "Version":"1.0","NormalizedVersion":"1.0.0","Description":"\u202Ehidden"}
            """;
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            $$"""{"data":[{{row}}]}""")));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var result = await source.DiscoverAsync(
            Request(order: NuGetGalleryDiscoveryOrder.Relevance),
            TestContext.Current.CancellationToken);
        NuGetGalleryDiscoveryMatch match = Assert.Single(result.Value!.Matches);
        Assert.Null(match.TotalDownloads);
        Assert.Null(match.Verified);
        Assert.Empty(match.Owners);
        Assert.Null(match.Description);
        Assert.Equal("1.0.0", match.Candidate.Coordinate.Version);
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        yield return ["""{}"""];
        yield return ["""{"data":null}"""];
        yield return ["""{"data":[],"data":[]}"""];
        yield return ["""{"data":[null]}"""];
        yield return ["""{"data":[{}]}"""];
        yield return [$$"""{"data":[{{Row}},{{Row}}]}"""];
        yield return [$$"""{"data":[{{Row}},{{Row.Replace("Cake.Tool", "cake.tool")}}]}"""];
        yield return [$$"""{"data":[{{Row}},null]}"""];
        yield return [$$"""{"data":[{{Row}}]"""];
        yield return [$$"""{"data":[{{Row}}]} trailing"""];
        foreach (string row in new[]
        {
            Row.Replace("\"Id\":\"Cake.Tool\"", "\"Id\":\"../invalid\""),
            Row.Replace("\"Id\":\"Cake.Tool\"", "\"Id\":\"Cake.Tool\",\"Id\":\"Other\""),
            Row.Replace("\"Version\":\"6.2.0\"", "\"Version\":\"6.2.0\",\"Version\":\"6.2.0\""),
            Row.Replace("\"Version\":\"6.2.0\"", "\"Version\":false"),
            Row.Replace("\"NormalizedVersion\":\"6.2.0\"", "\"NormalizedVersion\":\"7.0.0\""),
            Row.Replace("\"NormalizedVersion\":\"6.2.0\"", "\"NormalizedVersion\":\"6.2.0\",\"NormalizedVersion\":\"6.2.0\""),
            Row.Replace("\"NormalizedVersion\":\"6.2.0\",", ""),
            Row.Replace("\"DownloadCount\":9000000", "\"DownloadCount\":-1"),
            Row.Replace("\"DownloadCount\":9000000", "\"DownloadCount\":9000000,\"DownloadCount\":1"),
            Row.Replace("\"DownloadCount\":9000000,", ""),
            Row.Replace("\"PackageRegistration\":", "\"PackageRegistration\":{},\"PackageRegistration\":"),
            Row.Replace("6.2.0", "6.2.0-preview.1"),
        })
            yield return [$$"""{"data":[{{row}}]}"""];
    }

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task InvalidResponseRejectsAtomically(string json)
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json(json)));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var outcome = await source.DiscoverAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Null(outcome.Value);
        Assert.Equal(PackageSourceFailureKind.InvalidResponse, outcome.Failure!.Kind);
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task OverCapacityIsRejectedRatherThanTruncated()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            $$"""{"data":[{{Row}},{{Row.Replace("Cake.Tool", "Other")}}]}""")));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var result = await source.DiscoverAsync(
            new NuGetGalleryDiscoveryRequest(PackageSourceDescriptor.NuGetGallery, 1),
            TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.Equal(PackageSourceFailureKind.InvalidResponse, result.Failure!.Kind);
    }

    [Fact]
    public async Task ProviderOrderAndTiesArePreserved()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            $$"""{"totalHits":0,"data":[{{Row}},{{Row.Replace("Cake.Tool", "Alpha")}}]}""")));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var result = await source.DiscoverAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(["Cake.Tool", "Alpha"], result.Value!.Matches.Select(match => match.PackageId));
        Assert.Equal(0, result.Value.EstimatedTotalHits);
    }

    [Fact]
    public async Task ExplicitPrereleasePolicyAdmitsSemVer2Versions()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            $$"""{"data":[{{Row.Replace("6.2.0", "6.3.0-preview.1")}}]}""")));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var request = new NuGetGalleryDiscoveryRequest(
            PackageSourceDescriptor.NuGetGallery, 10, includePrerelease: true);
        var result = await source.DiscoverAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("6.3.0-preview.1", Assert.Single(result.Value!.Matches).Version);
        Assert.Contains("prerelease=true&semVerLevel=2.0.0", Assert.Single(handler.Urls));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MetadataBoundsRejectWholeResponse(bool advertised)
    {
        using var handler = new RecordingHandler((_, _) =>
        {
            HttpResponseMessage response = Json($$"""{"data":[{{Row}}]}""");
            if (!advertised)
                response.Content = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes(
                    $$"""{"data":[{{Row}}]}""")));
            return Task.FromResult(response);
        });
        using var source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler,
            new NuGetFetchOptions { MaxMetadataResponseBytes = 32 });
        var result = await source.DiscoverAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.Equal(PackageSourceFailureKind.ResponseRejected, result.Failure!.Kind);
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task TransientFailureRetriesTheSameFullRequest()
    {
        int attempts = 0;
        using var handler = new RecordingHandler((_, _) => Task.FromResult(
            ++attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json($$"""{"data":[{{Row}}]}""")));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var result = await source.DiscoverAsync(Request(), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Value);
        Assert.Equal(2, attempts);
        Assert.Equal(handler.Urls[0], handler.Urls[1]);
    }

    [Fact]
    public async Task SharedDeadlineFailureDoesNotPublishRows()
    {
        using var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("""{"data":[]}""");
        });
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        using var context = new NuGetOperationContext(
            TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(80),
            TestContext.Current.CancellationToken);
        var result = await source.DiscoverAsync(
            Request(), TestContext.Current.CancellationToken, context);
        Assert.Null(result.Value);
        Assert.Equal(PackageSourceFailureKind.Timeout, result.Failure!.Kind);
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task CallerCancellationCannotBecomeSuccessfulRows()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var handler = new RecordingHandler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(Json($$"""{"data":[{{Row}}]}"""));
        });
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.DiscoverAsync(Request(), cancellation.Token));
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task IncompleteTransportRejectsEvenApparentlyCompleteJson()
    {
        using var handler = new RecordingHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new TruncatedTransportStream(
                    Encoding.UTF8.GetBytes($$"""{"data":[{{Row}}]}"""))),
            }));
        using var source = PackageSourceClientFactory.CreateGallery(PackageSourceAssociation.Create(), handler);
        var result = await source.DiscoverAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.Equal(PackageSourceFailureKind.Transport, result.Failure!.Kind);
        Assert.Equal(4, handler.Urls.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestAndMetadataBodyTimeoutsStayTypedAndBounded(bool stallBody)
    {
        using var handler = new RecordingHandler(async (_, token) =>
        {
            if (!stallBody)
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StallingStream()),
            };
        });
        using var source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler,
            new NuGetFetchOptions
            {
                OperationTimeout = TimeSpan.FromSeconds(5),
                RequestTimeout = stallBody ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(20),
                MetadataBodyTimeout = TimeSpan.FromMilliseconds(20),
            });
        var result = await source.DiscoverAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Null(result.Value);
        Assert.Equal(PackageSourceFailureKind.Timeout, result.Failure!.Kind);
        Assert.Equal(4, handler.Urls.Count);
    }

    private static NuGetGalleryDiscoveryRequest Request(
        NuGetGalleryDiscoveryOrder? order = null) =>
        new(PackageSourceDescriptor.NuGetGallery, 10, order: order);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return respond(request, cancellationToken);
        }
    }

    private class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class TruncatedTransportStream(byte[] bytes) : NonSeekableStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position == Length)
                throw new IOException("The transport ended before its advertised body completed.");
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class StallingStream() : NonSeekableStream([])
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
