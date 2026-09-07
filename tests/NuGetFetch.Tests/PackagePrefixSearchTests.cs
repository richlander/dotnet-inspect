using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed class PackagePrefixSearchTests
{
    private const string Prefix = "Contoso.";
    private const string GallerySearch = "https://azuresearch-usnc.nuget.org/query";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gallery_RequestsEachPageOnlyWhenAdvanced(bool prerelease)
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            JsonPage("Contoso.Second"),
            JsonPage());
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);

        IAsyncEnumerable<PackageSourceOperationResult<PackageSearchResult>> pages =
            source.SearchByPrefixPagesAsync(
                Prefix,
                prerelease: prerelease,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(handler.Requests);
        await using var enumerator = pages.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.Empty(handler.Requests);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.First");
        await Task.Yield();
        AssertRequests(handler, [0], prerelease);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.Second");
        await Task.Yield();
        AssertRequests(handler, [0, 1], prerelease);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None);
        AssertRequests(handler, [0, 1, 2], prerelease);
        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler, [0, 1, 2], prerelease);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gallery_DisposalDoesNotRequestAnotherPage(bool advanceFirst)
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);

        await using (var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            if (advanceFirst)
            {
                Assert.True(await enumerator.MoveNextAsync());
                AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
                    "Contoso.First");
            }
        }

        await Task.Yield();
        AssertRequests(handler, advanceFirst ? [0] : []);
    }

    [Fact]
    public async Task Gallery_DeduplicatesAcrossPagesInFirstObservedRelevanceOrder()
    {
        var handler = new PagingHandler(
            JsonPage("Other.Package", "Contoso.Zulu", "contoso.Alpha", "CONTOSO.ZULU"),
            new Page("""
                {"data":[
                  {"id":"CONTOSO.ALPHA","version":"2.0.0"},
                  {"id":"contoso.Middle","version":"1.0.0"},
                  {"id":"Contoso.Zulu","version":"3.0.0"}
                ]}
                """),
            JsonPage());
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        PackageSearchResult first = AssertPage(
            source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.Zulu", "contoso.Alpha");
        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "contoso.Middle");
        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None);
        Assert.False(await enumerator.MoveNextAsync());

        Assert.Equal(["Contoso.Zulu", "contoso.Alpha"],
            first.Matches.Select(match => match.Metadata.Id));
        Assert.All(first.Matches, match => Assert.Equal("1.0.0", match.Metadata.Version));
        AssertRequests(handler, [0, 4, 7]);
    }

    [Fact]
    public async Task Gallery_EmptyFilteredPageDoesNotProveExhaustion()
    {
        var handler = new PagingHandler(
            JsonPage("Other.Package", "Other.Contoso.Package"),
            JsonPage("contoso.Match"),
            JsonPage());
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None);
        AssertRequests(handler, [0]);
        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "contoso.Match");
        AssertRequests(handler, [0, 2]);
        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None);
        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler, [0, 2, 3]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Gallery_RequestedLimitCountsUniqueMatchesWithoutAnotherRequest(int take)
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First", "Other.Package"),
            JsonPage("CONTOSO.FIRST", "Contoso.Second", "Contoso.Third", "Contoso.Excess"),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            take: take,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current,
            take == 1
                ? PackageSearchTruncationReason.RequestedLimit
                : PackageSearchTruncationReason.None,
            "Contoso.First");
        if (take == 3)
        {
            Assert.True(await enumerator.MoveNextAsync());
            AssertPage(source, enumerator.Current, PackageSearchTruncationReason.RequestedLimit,
                "Contoso.Second", "Contoso.Third");
        }

        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler, take == 1 ? [0] : [0, 2]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{}", PackageSourceFailureKind.AuthenticationRequired)]
    [InlineData(HttpStatusCode.OK, "not-json", PackageSourceFailureKind.InvalidResponse)]
    public async Task Gallery_LateTypedFailurePreservesEarlierPageAndEndsEnumeration(
        HttpStatusCode status,
        string body,
        PackageSourceFailureKind failureKind)
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            new Page(body, status),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        PackageSearchResult first = AssertPage(
            source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.First");
        AssertRequests(handler, [0]);
        Assert.True(await enumerator.MoveNextAsync());
        AssertFailure(source, enumerator.Current, failureKind);
        Assert.False(await enumerator.MoveNextAsync());

        Assert.Equal("Contoso.First", Assert.Single(first.Matches).Metadata.Id);
        AssertRequests(handler, [0, 1]);
    }

    [Fact]
    public async Task Gallery_RepeatedPageIsATerminalTypedFailure()
    {
        string[] ids = Enumerable.Range(0, 100)
            .Select(index => $"Other.Package.{index}").ToArray();
        var handler = new PagingHandler(
            JsonPage(ids),
            JsonPage(ids.Select(id => id.ToUpperInvariant()).ToArray()),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None);
        Assert.True(await enumerator.MoveNextAsync());
        AssertFailure(source, enumerator.Current, PackageSourceFailureKind.InvalidResponse);
        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler, [0, 100]);
    }

    [Theory]
    [InlineData(100, 31, PackageSearchTruncationReason.SourcePageLimit)]
    [InlineData(1, 100, PackageSearchTruncationReason.ClientPageLimit)]
    public async Task Gallery_PageCeilingsPreserveLastPageAndDoNotFetchPastBoundary(
        int rawPageSize,
        int pageCount,
        PackageSearchTruncationReason truncationReason)
    {
        Page[] pages = Enumerable.Range(0, pageCount)
            .Select(page => JsonPage(
                Enumerable.Range(page * rawPageSize, rawPageSize - 1)
                    .Select(index => $"Other.Package.{index}")
                    .Append($"Contoso.Page.{page}").ToArray()))
            .ToArray();
        var handler = new PagingHandler(pages);
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            take: pageCount + 1,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        for (int page = 0; page < pageCount; page++)
        {
            Assert.True(await enumerator.MoveNextAsync());
            AssertPage(source, enumerator.Current,
                page == pageCount - 1 ? truncationReason : PackageSearchTruncationReason.None,
                $"Contoso.Page.{page}");
            Assert.Equal(page + 1, handler.Requests.Count);
        }

        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler,
            Enumerable.Range(0, pageCount).Select(page => page * rawPageSize).ToArray());
    }

    [Fact]
    public async Task Gallery_MetadataByteLimitStillAppliesToLaterPages()
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            JsonPage(Enumerable.Range(0, 40).Select(index => $"Contoso.Package.{index}").ToArray()),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler,
            new NuGetFetchOptions { MaxMetadataResponseBytes = 256 });
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.First");
        Assert.True(await enumerator.MoveNextAsync());
        AssertFailure(source, enumerator.Current, PackageSourceFailureKind.ResponseRejected);
        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler, [0, 1]);
    }

    [Fact]
    public async Task Gallery_ConsumerHoldDoesNotSpendActiveOperationBudget()
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            JsonPage("Contoso.Second"),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                OperationTimeout = TimeSpan.FromSeconds(2),
            });
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            take: 2,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.First");
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        AssertRequests(handler, [0]);
        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.RequestedLimit,
            "Contoso.Second");
        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler, [0, 1]);
    }

    [Fact]
    public async Task Gallery_ActiveSourceWorkSharesOneBudgetAcrossPages()
    {
        // Either page fits a fresh budget; both together exceed the operation ceiling.
        var handler = new PagingHandler(
            JsonPage("Contoso.First") with { Delay = TimeSpan.FromSeconds(4) },
            JsonPage("Contoso.Second") with { Delay = TimeSpan.FromSeconds(4) },
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                OperationTimeout = TimeSpan.FromSeconds(6),
            });
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            take: 2,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.First");
        Assert.True(await enumerator.MoveNextAsync());
        AssertFailure(source, enumerator.Current, PackageSourceFailureKind.Timeout);
        Assert.False(await enumerator.MoveNextAsync());
        AssertRequests(handler, [0, 1]);
    }

    [Fact]
    public async Task Gallery_SuppliedContextKeepsItsWallClockCeilingDuringConsumerHold()
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler,
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(30),
                OperationTimeout = TimeSpan.FromSeconds(30),
            });
        using var operation = new NuGetOperationContext(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken,
            operationContext: operation)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.First");
        operation.ThrowIfExpired();
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        AssertFailure(source, enumerator.Current, PackageSourceFailureKind.Timeout);
        Assert.False(await enumerator.MoveNextAsync());
        Assert.Throws<NuGetOperationTimeoutException>(operation.ThrowIfExpired);
        AssertRequests(handler, [0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Gallery_CancellationDuringHoldRetainsOriginalTokenWithoutAnotherRequest(
        bool useEnumeratorToken)
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: useEnumeratorToken ? default : cancellation.Token)
            .GetAsyncEnumerator(useEnumeratorToken ? cancellation.Token : default);

        Assert.True(await enumerator.MoveNextAsync());
        AssertPage(source, enumerator.Current, PackageSearchTruncationReason.None,
            "Contoso.First");
        cancellation.Cancel();
        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await enumerator.MoveNextAsync());

        Assert.Equal(cancellation.Token, error.CancellationToken);
        AssertRequests(handler, [0]);
    }

    [Fact]
    public async Task V3_DefaultPageImplementationYieldsOneUnsupportedResult()
    {
        var handler = new PagingHandler();
        using IPackageSourceClient source = PackageSourceClientFactory.Create(
            PackageSourceDescriptor.NuGetV3(
                "offline", "Offline feed", new Uri("https://feed.example/v3/index.json")),
            PackageSourceAssociation.Create(),
            handler);
        await using var enumerator = source.SearchByPrefixPagesAsync(
            Prefix,
            cancellationToken: TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        AssertFailure(source, enumerator.Current, PackageSourceFailureKind.Unsupported);
        Assert.False(await enumerator.MoveNextAsync());
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(3, PackageSearchTruncationReason.RequestedLimit)]
    [InlineData(10, PackageSearchTruncationReason.None)]
    public async Task Gallery_MaterializedSearchRetainsTheSameOrderedAggregate(
        int take,
        PackageSearchTruncationReason truncationReason)
    {
        Page[] responses =
        [
            JsonPage("Contoso.Zulu", "Other.Package"),
            JsonPage("CONTOSO.ZULU", "contoso.Alpha", "Contoso.Middle"),
            JsonPage(),
        ];
        var streamingHandler = new PagingHandler(responses);
        var materializedHandler = new PagingHandler(responses);
        using IPackageSourceClient streamingSource = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), streamingHandler);
        using IPackageSourceClient materializedSource = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), materializedHandler);
        List<string> streamedIds = [];
        PackageSearchResult? last = null;
        await foreach (PackageSourceOperationResult<PackageSearchResult> page in
            streamingSource.SearchByPrefixPagesAsync(
                Prefix,
                take: take,
                cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Null(page.Failure);
            last = Assert.IsType<PackageSearchResult>(page.Value);
            streamedIds.AddRange(last.Matches.Select(match => match.Metadata.Id));
        }

        PackageSearchResult aggregate = AssertPage(
            materializedSource,
            await materializedSource.SearchByPrefixAsync(
                Prefix,
                take: take,
                cancellationToken: TestContext.Current.CancellationToken),
            truncationReason,
            "Contoso.Zulu", "contoso.Alpha", "Contoso.Middle");
        Assert.NotNull(last);
        Assert.Equal(truncationReason, last.TruncationReason);
        Assert.Equal(streamedIds, aggregate.Matches.Select(match => match.Metadata.Id));
        int[] skips = take == 3 ? [0, 2] : [0, 2, 5];
        AssertRequests(streamingHandler, skips);
        AssertRequests(materializedHandler, skips);
    }

    [Fact]
    public async Task Gallery_MaterializedSearchDoesNotReturnPartialSuccessAfterLateFailure()
    {
        var handler = new PagingHandler(
            JsonPage("Contoso.First"),
            new Page("not-json"),
            JsonPage("Contoso.Unrequested"));
        using IPackageSourceClient source = PackageSourceClientFactory.CreateGallery(
            PackageSourceAssociation.Create(), handler);

        AssertFailure(source,
            await source.SearchByPrefixAsync(
                Prefix,
                cancellationToken: TestContext.Current.CancellationToken),
            PackageSourceFailureKind.InvalidResponse);
        AssertRequests(handler, [0, 1]);
    }

    private static PackageSearchResult AssertPage(
        IPackageSourceClient source,
        PackageSourceOperationResult<PackageSearchResult> outcome,
        PackageSearchTruncationReason truncationReason,
        params string[] ids)
    {
        Assert.Null(outcome.Failure);
        PackageSearchResult page = Assert.IsType<PackageSearchResult>(outcome.Value);
        Assert.Same(source.Source, page.Source);
        Assert.Equal(truncationReason, page.TruncationReason);
        Assert.Equal(truncationReason != PackageSearchTruncationReason.None, page.Truncated);
        Assert.Equal(ids, page.Matches.Select(match => match.Metadata.Id));
        Assert.All(page.Matches, match =>
        {
            Assert.Same(source.Source, match.Candidate.Source);
            Assert.Equal(match.Metadata.Id.ToLowerInvariant(), match.Candidate.Coordinate.PackageId);
            Assert.Equal(match.Metadata.Version, match.Candidate.Coordinate.Version);
            Assert.Equal(PackageDiscoveryContract.KeywordSearch, match.Candidate.DiscoveryContract);
            Assert.Equal(PackageListingState.Listed, match.Candidate.ListingState);
        });
        return page;
    }

    private static void AssertFailure(
        IPackageSourceClient source,
        PackageSourceOperationResult<PackageSearchResult> outcome,
        PackageSourceFailureKind kind)
    {
        Assert.Null(outcome.Value);
        PackageSourceFailure failure = Assert.IsType<PackageSourceFailure>(outcome.Failure);
        Assert.Same(source.Source, failure.Source);
        Assert.Equal(PackageSourceCapabilities.Search, failure.Capability);
        Assert.Equal(kind, failure.Kind);
    }

    private static void AssertRequests(
        PagingHandler handler,
        int[] skips,
        bool prerelease = false)
    {
        Assert.Equal(
            skips.Select(skip =>
                $"{GallerySearch}?q={Uri.EscapeDataString(Prefix)}"
                + $"&skip={skip.ToString(CultureInfo.InvariantCulture)}"
                + $"&take=100&prerelease={(prerelease ? "true" : "false")}&semVerLevel=2.0.0"),
            handler.Requests.Select(uri => uri.AbsoluteUri));
    }

    private static Page JsonPage(params string[] ids) =>
        new(JsonSerializer.Serialize(new
        {
            totalHits = 0,
            data = ids.Select(id => new { id, version = "1.0.0" }).ToArray(),
        }));

    private sealed record Page(
        string Body,
        HttpStatusCode Status = HttpStatusCode.OK,
        TimeSpan Delay = default);

    private sealed class PagingHandler(params Page[] pages) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Uri uri = Assert.IsType<Uri>(request.RequestUri);
            int index = Requests.Count;
            Requests.Add(uri);
            Assert.True(index < pages.Length, $"Unexpected page request: {uri.AbsoluteUri}");
            Page page = pages[index];
            if (page.Delay > TimeSpan.Zero)
                await Task.Delay(page.Delay, cancellationToken);

            return new HttpResponseMessage(page.Status)
            {
                Content = new StringContent(page.Body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
    }
}
