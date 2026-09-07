using System.CommandLine;
using System.Net;
using System.Net.Http.Headers;
using DotnetInspector.CommandLine;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;
using NuGetSource = NuGetFetch.PackageSource;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for searching configured NuGet feeds: SearchQueryService discovery from a V3 service
/// index, the origin scope that keeps feed credentials from following a feed-supplied URL, and
/// the refusal to render an unsearchable feed set as "no packages found". Issue #3417.
/// </summary>
public class NuGetSearchSourcesTests
{
    private const string IndexUrl = "https://feed.example/v3/index.json";
    private const string NuGetOrgIndexUrl = "https://api.nuget.org/v3/index.json";
    private const string SearchUrl = "https://feed.example/v3/query";

    private static string ServiceIndex(string searchId) => $$"""
        {
          "version": "3.0.0",
          "resources": [
            { "@id": "https://feed.example/v3/flat2/", "@type": "PackageBaseAddress/3.0.0" },
            { "@id": "{{searchId}}", "@type": "SearchQueryService/3.5.0" }
          ]
        }
        """;

    [Fact]
    public void SearchTimeoutOptions_DeriveFourRequestDeadlines()
    {
        NuGetFetchOptions options =
            NuGetFetchOptions.FromRequestTimeout(
                TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), options.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(240), options.OperationTimeout);
    }

    [Fact]
    public async Task GetSearchQueryServiceAsync_DiscoversVersionedSearchResource()
    {
        var handler = new RouteHandler { [IndexUrl] = ServiceIndex(SearchUrl) };
        using var client = new HttpClient(handler);

        string? discovered = await PackageExtractor.GetSearchQueryServiceAsync(
            client,
            new NuGetSource("contoso", IndexUrl),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SearchUrl, discovered);
    }

    [Fact]
    public async Task GetSearchQueryServiceAsync_PreservesDeclaredQueryBytes()
    {
        const string declared =
            "https://feed.example/v3/query?s%69g=%73ecret&opaque=%7E%41";
        var handler = new RouteHandler { [IndexUrl] = ServiceIndex(declared) };
        using var client = new HttpClient(handler);

        string? discovered = await PackageExtractor.GetSearchQueryServiceAsync(
            client,
            new NuGetSource("contoso", IndexUrl),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(declared, discovered);
    }

    [Fact]
    public async Task GetSearchQueryServiceAsync_ServiceIndexRequiresBrowserStreamingResponse()
    {
        var handler = new RouteHandler { [IndexUrl] = ServiceIndex(SearchUrl) };
        using var client = new HttpClient(handler);

        await PackageExtractor.GetSearchQueryServiceAsync(
            client,
            new NuGetSource("contoso", IndexUrl),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(handler.BrowserStreamingRequested);
    }

    [Fact]
    public async Task SearchAsync_UsesHighestSearchCapabilityVersion()
    {
        const string olderSearch = "https://feed.example/v3/query-old";
        const string currentSearch = "https://feed.example/v3/query-current";
        var handler = new RouteHandler
        {
            [IndexUrl] = $$"""
                {"resources":[
                  {"@id":"{{olderSearch}}","@type":"SearchQueryService/3.0.0"},
                  {"@id":"{{currentSearch}}","@type":"SearchQueryService/3.5.0"}
                ]}
                """,
            [olderSearch] = """{"data":[{"id":"Wrong.Package","version":"1.0.0"}]}""",
            [currentSearch] = """{"data":[{"id":"Contoso.Package","version":"1.0.0"}]}""",
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "Contoso",
            sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] });

        Assert.Equal("Contoso.Package", Assert.Single(outcome.Results).PackageId);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.StartsWith(olderSearch, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_UnsupportedFutureCapability_UsesHighestSupportedVersion()
    {
        const string futureSearch = "https://feed.example/v4/query";
        const string currentSearch = "https://feed.example/v3/query-current";
        var handler = new RouteHandler
        {
            [IndexUrl] = $$"""
                {"resources":[
                  {"@id":"{{futureSearch}}","@type":"SearchQueryService/4.0.0"},
                  {"@id":"{{currentSearch}}","@type":"SearchQueryService/3.5.0"}
                ]}
                """,
            [futureSearch] = "<html>unsupported protocol</html>",
            [currentSearch] = """{"data":[{"id":"Contoso.Package","version":"1.0.0"}]}""",
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "Contoso",
            sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] });

        Assert.Equal("Contoso.Package", Assert.Single(outcome.Results).PackageId);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.StartsWith(futureSearch, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_MalformedEquivalentEndpoint_TriesNextInIndexOrder()
    {
        const string firstSearch = "https://feed.example/v3/query-a";
        const string secondSearch = "https://feed.example/v3/query-b";
        var handler = new RouteHandler
        {
            [IndexUrl] = $$"""
                {"resources":[
                  {"@id":"{{firstSearch}}","@type":"SearchQueryService/3.5.0"},
                  {"@id":"{{secondSearch}}","@type":"SearchQueryService/3.5.0"}
                ]}
                """,
            [firstSearch] = "<html>sign in</html>",
            [secondSearch] = """{"data":[{"id":"Contoso.Package","version":"1.0.0"}]}""",
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "Contoso",
            sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] });

        Assert.Empty(outcome.Failures);
        Assert.Equal("Contoso.Package", Assert.Single(outcome.Results).PackageId);
        Assert.Collection(
            handler.Requested,
            request => Assert.Equal(IndexUrl, request),
            request => Assert.StartsWith(firstSearch + "?", request, StringComparison.Ordinal),
            request => Assert.StartsWith(secondSearch + "?", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_OperationCanceledEndpoint_TriesNextEquivalentEndpoint()
    {
        const string firstSearch = "https://feed.example/v3/query-cancelled";
        const string secondSearch = "https://feed.example/v3/query-success";
        var handler = new RouteHandler
        {
            [IndexUrl] = $$"""
                {"resources":[
                  {"@id":"{{firstSearch}}","@type":"SearchQueryService/3.5.0"},
                  {"@id":"{{secondSearch}}","@type":"SearchQueryService/3.5.0"}
                ]}
                """,
            [secondSearch] = """{"data":[{"id":"Contoso.Package","version":"1.0.0"}]}""",
        };
        handler.Throw(firstSearch, new OperationCanceledException());
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "Contoso",
            sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] });

        Assert.Equal("Contoso.Package", Assert.Single(outcome.Results).PackageId);
        Assert.Collection(
            handler.Requested,
            request => Assert.Equal(IndexUrl, request),
            request => Assert.StartsWith(firstSearch + "?", request, StringComparison.Ordinal),
            request => Assert.StartsWith(secondSearch + "?", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_OperationCanceledServiceIndex_TriesNextSource()
    {
        const string secondIndex = "https://second.example/v3/index.json";
        const string secondSearch = "https://second.example/v3/query";
        var handler = new RouteHandler
        {
            [secondIndex] = ServiceIndex(secondSearch),
            [secondSearch] = """{"data":[{"id":"Contoso.Package","version":"1.0.0"}]}""",
        };
        handler.Throw(IndexUrl, new OperationCanceledException());
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "Contoso",
            sourceOptions: new NuGetSourceOptions
            {
                Sources = [IndexUrl, secondIndex],
            });

        Assert.Equal("Contoso.Package", Assert.Single(outcome.Results).PackageId);
        Assert.Single(outcome.Failures);
        Assert.Collection(
            handler.Requested,
            request => Assert.Equal(IndexUrl, request),
            request => Assert.Equal(secondIndex, request),
            request => Assert.StartsWith(secondSearch + "?", request, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_EquivalentEndpointFailover_IsBounded()
    {
        string[] searchUrls =
        [
            "https://feed.example/v3/query-0",
            "https://feed.example/v3/query-1",
            "https://feed.example/v3/query-2",
            "https://feed.example/v3/query-3",
            "https://feed.example/v3/query-4",
            "https://feed.example/v3/query-5",
        ];
        string resources = string.Join(
            ",",
            searchUrls.Select(
                url => $$"""{"@id":"{{url}}","@type":"SearchQueryService/3.5.0"}"""));
        var handler = new RouteHandler
        {
            [IndexUrl] = $$"""{"resources":[{{resources}}]}""",
            [searchUrls[0]] = "<html>failure 0</html>",
            [searchUrls[1]] = "<html>failure 1</html>",
            [searchUrls[2]] = "<html>failure 2</html>",
            [searchUrls[3]] = "<html>failure 3</html>",
            [searchUrls[4]] = """{"data":[{"id":"Too.Late","version":"1.0.0"}]}""",
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuGetSearchService.SearchAsync(
                client,
                "Contoso",
                sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] }));

        Assert.Collection(
            handler.Requested,
            request => Assert.Equal(IndexUrl, request),
            request => Assert.StartsWith(searchUrls[0] + "?", request, StringComparison.Ordinal),
            request => Assert.StartsWith(searchUrls[1] + "?", request, StringComparison.Ordinal),
            request => Assert.StartsWith(searchUrls[2] + "?", request, StringComparison.Ordinal),
            request => Assert.StartsWith(searchUrls[3] + "?", request, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SearchAsync_EquivalentEndpointFailover_SharesOperationCeiling()
    {
        const string firstSearch = "https://feed.example/v3/query-fast-failure";
        const string secondSearch = "https://feed.example/v3/query-stalled";
        const string thirdSearch = "https://feed.example/v3/query-too-late";
        var handler = new SearchBudgetHandler(
            IndexUrl,
            firstSearch,
            secondSearch,
            thirdSearch);
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuGetSearchService.SearchAsync(
                client,
                "Contoso",
                sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] },
                fetchOptions: new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(10),
                    OperationTimeout = TimeSpan.FromSeconds(2),
                }));

        Assert.Equal(
            [IndexUrl, firstSearch, secondSearch],
            handler.Requested.Select(RouteHandler.WithoutQuery));
        Assert.Contains(
            nameof(NuGetOperationTimeoutException),
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_OperationTimeoutDescribesEveryRemainingSource()
    {
        const string firstIndex = "https://first.example/v3/index.json";
        const string secondIndex = "https://second.example/v3/index.json";
        const string thirdIndex = "https://third.example/v3/index.json";
        var handler = new RouteHandler();
        handler.RespondWithContent(
            firstIndex,
            static () => new StallingBodyContent());
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                NuGetSearchService.SearchAsync(
                    client,
                    "Contoso",
                    sourceOptions: new NuGetSourceOptions
                    {
                        Sources = [firstIndex, secondIndex, thirdIndex],
                    },
                    fetchOptions: new NuGetFetchOptions
                    {
                        RequestTimeout = TimeSpan.FromSeconds(5),
                        OperationTimeout = TimeSpan.FromMilliseconds(100),
                    }));

        Assert.Contains("first.example", error.Message, StringComparison.Ordinal);
        Assert.Contains("second.example", error.Message, StringComparison.Ordinal);
        Assert.Contains("third.example", error.Message, StringComparison.Ordinal);
        Assert.Equal(
            2,
            error.Message.Split(
                "search not attempted",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task GetSearchQueryServiceAsync_FeedWithoutSearchResource_ReturnsNull()
    {
        const string flatContainerOnly = """
            { "version": "3.0.0", "resources": [
              { "@id": "https://feed.example/v3/flat2/", "@type": "PackageBaseAddress/3.0.0" } ] }
            """;
        var handler = new RouteHandler { [IndexUrl] = flatContainerOnly };
        using var client = new HttpClient(handler);

        string? discovered = await PackageExtractor.GetSearchQueryServiceAsync(
            client,
            new NuGetSource("contoso", IndexUrl),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(discovered);
    }

    [Fact]
    public async Task GetSearchQueryServiceAsync_LocalFolderSource_ReturnsNull()
    {
        using var client = new HttpClient(new RouteHandler());

        string? discovered = await PackageExtractor.GetSearchQueryServiceAsync(
            client,
            new NuGetSource("local", @"D:\packages"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(discovered);
    }

    [Fact]
    public async Task SearchAsync_ExplicitSource_SearchesDiscoveredEndpoint()
    {
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = """{"data":[{"id":"Contoso.Internal","version":"1.2.3"}],"totalHits":"0"}"""
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client, "Contoso", sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] });

        Assert.Empty(outcome.Failures);
        NuGetSearchResult result = Assert.Single(outcome.Results);
        Assert.Equal("Contoso.Internal", result.PackageId);
        Assert.Contains(handler.Requested, u => u.StartsWith(SearchUrl + "?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_ConfiguredDeadlineBoundsServiceIndexBody()
    {
        var handler = new RouteHandler();
        handler.RespondWithContent(IndexUrl, static () => new StallingBodyContent());
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client,
                "Contoso",
                sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] },
                fetchOptions: new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(40),
                    OperationTimeout = TimeSpan.FromSeconds(30),
                }));

        Assert.Contains(
            "NuGetRequestTimeoutException",
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_UnsearchableSource_ThrowsRatherThanReportingNoResults()
    {
        // Every configured source failed. An empty list here would render as
        // "No packages found", which is the opposite of what happened.
        var handler = new RouteHandler { [IndexUrl] = """{"version":"3.0.0","resources":[]}""" };
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client, "Contoso", sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] }));

        Assert.Contains("No configured NuGet source could be searched", ex.Message);
        Assert.Contains("no searchable endpoint", ex.Message);
        Assert.DoesNotContain("requires credentials", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "requires credentials")]
    [InlineData(HttpStatusCode.Forbidden, "denied access")]
    [InlineData(HttpStatusCode.BadRequest, "service index unavailable")]
    public async Task SearchAsync_RefusedServiceIndex_ReportsTypedFailure(
        HttpStatusCode status,
        string expectedReason)
    {
        var handler = new RouteHandler();
        handler.RespondWith(IndexUrl, status);
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client, "Contoso", sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] }));

        Assert.Contains(expectedReason, ex.Message);
        Assert.Contains($"HTTP {(int)status} {status}", ex.Message);
        Assert.Contains("reading the service index", ex.Message);
        Assert.DoesNotContain("no searchable endpoint", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_ServiceIndexFailures_AreAttributedPerSource()
    {
        const string refusedIndex = "https://refused.example/v3/index.json";
        const string unsearchableIndex = "https://unsearchable.example/v3/index.json";

        var handler = new RouteHandler
        {
            [unsearchableIndex] = """{"version":"3.0.0","resources":[]}"""
        };
        handler.RespondWith(refusedIndex, HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig(
            [("refused", refusedIndex), ("unsearchable", unsearchableIndex)]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client, "Contoso", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path }));

        Assert.Contains("refused: source requires credentials", ex.Message);
        Assert.Contains(
            $"unsearchable: no searchable endpoint for '{unsearchableIndex}'",
            ex.Message);
    }

    [Fact]
    public async Task SearchAsync_MalformedSearchBody_SurfacesAsFailureNotEmptyResults()
    {
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = "<html>sign in</html>"
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client, "Contoso", sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] }));
    }

    [Theory]
    [InlineData(NuGetOrgIndexUrl)]
    [InlineData(NuGetOrgIndexUrl + "/")]
    public async Task SearchAsync_NuGetOrgMalformedBody_UsesStandardDiscoveryAndFailure(
        string indexUrl)
    {
        var handler = new RouteHandler
        {
            [indexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = "<html>sign in</html>"
        };
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig([("nuget.org", indexUrl)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client,
                "Contoso",
                sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path }));

        Assert.Contains("No configured NuGet source could be searched", exception.Message);
        Assert.Contains("nuget.org: search failed", exception.Message);
        Assert.Contains(nameof(System.Text.Json.JsonException), exception.Message);
        Assert.DoesNotContain("<html>", exception.Message);
        Assert.Collection(
            handler.Requested,
            requested => Assert.Equal(indexUrl, requested),
            requested => Assert.StartsWith(SearchUrl + "?", requested, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        """{"data":[{"id":null,"version":"1.0.0"}]}""",
        nameof(System.Text.Json.JsonException))]
    [InlineData(
        """{"data":[{"id":"Contoso.Package","version":"not-a-version"}]}""",
        nameof(InvalidOperationException))]
    public async Task SearchAsync_InvalidResultIdentity_ReportsSourceFailure(
        string body,
        string failureType)
    {
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = body,
        };
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig([("contoso", IndexUrl)]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NuGetSearchService.SearchAsync(
                client,
                "Contoso",
                sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path }));

        Assert.Contains("contoso: search failed", exception.Message);
        Assert.Contains(failureType, exception.Message);
        Assert.DoesNotContain("Value cannot be null", exception.Message);
    }

    [Fact]
    public async Task SearchAsync_OneSourceFails_ReportsFailureAlongsideResults()
    {
        const string goodIndex = "https://good.example/v3/index.json";
        const string goodSearch = "https://good.example/v3/query";
        const string badIndex = "https://bad.example/v3/index.json";

        var handler = new RouteHandler
        {
            [goodIndex] = $$"""
                { "version": "3.0.0", "resources": [
                  { "@id": "{{goodSearch}}", "@type": "SearchQueryService/3.5.0" } ] }
                """,
            [goodSearch] = """{"data":[{"id":"Good.Package","version":"1.0.0"}]}""",
            [badIndex] = """{"version":"3.0.0","resources":[]}"""
        };
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig([("good", goodIndex), ("bad", badIndex)]);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Single(outcome.Results);
        string failure = Assert.Single(outcome.Failures);
        Assert.Contains("bad.example", failure);
    }

    [Fact]
    public async Task SearchAsync_BodyFailures_AreAttributedPerSource()
    {
        const string goodIndex = "https://good.example/v3/index.json";
        const string goodSearch = "https://good.example/v3/query";
        const string oversizeIndex = "https://oversize.example/v3/index.json";
        const string oversizeSearch = "https://oversize.example/v3/query";
        const string timeoutIndex = "https://timeout.example/v3/index.json";
        const string timeoutSearch = "https://timeout.example/v3/query";
        const string resetIndex = "https://reset.example/v3/index.json";
        const string resetSearch = "https://reset.example/v3/query";

        var handler = new RouteHandler
        {
            [goodIndex] = ServiceIndex(goodSearch),
            [goodSearch] = """{"data":[{"id":"Good.Package","version":"1.0.0"}]}""",
            [oversizeIndex] = ServiceIndex(oversizeSearch),
            [timeoutIndex] = ServiceIndex(timeoutSearch),
            [resetIndex] = ServiceIndex(resetSearch),
        };
        handler.RespondWithContent(
            oversizeSearch,
            () => new AdvertisedLengthContent(
                NuGetFetchOptions.DefaultMaxMetadataResponseBytes + 1));
        handler.Throw(
            timeoutSearch,
            new TimeoutException("test transport timeout"));
        handler.RespondWithContent(
            resetSearch,
            () => new FailingBodyContent());
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig(
            [
                ("good", goodIndex),
                ("oversize", oversizeIndex),
                ("timeout", timeoutIndex),
                ("reset", resetIndex),
            ]);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "q",
            sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Equal("Good.Package", Assert.Single(outcome.Results).PackageId);
        Assert.Equal(3, outcome.Failures.Count);
        Assert.Contains(
            outcome.Failures,
            failure => failure.Contains(
                nameof(NuGetMetadataResponseTooLargeException),
                StringComparison.Ordinal));
        Assert.Contains(
            outcome.Failures,
            failure => failure.Contains(
                nameof(TimeoutException),
                StringComparison.Ordinal));
        Assert.Contains(
            outcome.Failures,
            failure => failure.Contains(
                nameof(IOException),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_SemanticallyEquivalentVersionsAcrossSources_ReturnedOnce()
    {
        const string indexA = "https://a.example/v3/index.json";
        const string indexB = "https://b.example/v3/index.json";
        const string searchA = "https://a.example/v3/query";
        const string searchB = "https://b.example/v3/query";

        var handler = new RouteHandler
        {
            [indexA] = $$"""{"resources":[{"@id":"{{searchA}}","@type":"SearchQueryService"}]}""",
            [indexB] = $$"""{"resources":[{"@id":"{{searchB}}","@type":"SearchQueryService"}]}""",
            [searchA] = """{"data":[{"id":"Shared.Package","version":"1.0.0"}]}""",
            [searchB] = """
                {"data":[
                  {"id":"shared.package","version":"1.0.0.0"},
                  {"id":"Unique.Package","version":"2.0.0"}
                ]}
                """
        };
        using var client = new HttpClient(handler);
        using var config = new TempNuGetConfig([("a", indexA), ("b", indexB)]);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "Package",
            take: 2,
            sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Empty(outcome.Failures);
        Assert.Equal(
            ["Shared.Package", "Unique.Package"],
            outcome.Results.Select(result => result.PackageId));
    }

    [Fact]
    public async Task SearchAsync_CredentialedFeed_SendsCredentialsToSameOriginEndpoint()
    {
        using var config = new TempNuGetConfig(IndexUrl);
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(SearchUrl),
            [SearchUrl] = """{"data":[]}"""
        };
        using var client = new HttpClient(handler);

        await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        AuthenticationHeaderValue? sent = handler.AuthFor(SearchUrl);
        Assert.NotNull(sent);
        Assert.Equal("Basic", sent.Scheme);
    }

    [Fact]
    public async Task SearchAsync_CredentialedFeed_WithholdsCredentialsFromForeignEndpoint()
    {
        // The service index is the only thing naming the search endpoint, so a hostile or
        // compromised feed can point it anywhere. Credentials must not follow.
        const string foreignSearch = "https://attacker.example/collect";
        using var config = new TempNuGetConfig(IndexUrl);
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(foreignSearch),
            [foreignSearch] = """{"data":[]}"""
        };
        using var client = new HttpClient(handler);

        await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.NotNull(handler.AuthFor(IndexUrl));
        Assert.Null(handler.AuthFor(foreignSearch));
    }

    [Theory]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example/v3/query", true)]
    [InlineData("https://feed.example/v3/index.json", "https://FEED.EXAMPLE/other", true)]
    [InlineData("https://feed.example/v3/index.json", "https://attacker.example/query", false)]
    [InlineData("https://feed.example/v3/index.json", "http://feed.example/query", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example:8443/query", false)]
    [InlineData("https://feed.example/v3/index.json", "https://sub.feed.example/query", false)]
    [InlineData("https://xn--bcher-kva.example/i.json", "https://b\u00fccher.example/query", true)]
    [InlineData("https://b\u00fccher.example/i.json", "https://xn--bcher-kva.example/query", true)]
    [InlineData("https://[::1]/i.json", "https://[0:0:0:0:0:0:0:1]/query", true)]
    [InlineData("https://feed.example/v3/index.json", null, false)]
    [InlineData("https://feed.example/v3/index.json", "/relative", false)]
    [InlineData(@"D:\packages", "https://feed.example/query", false)]
    public void IsSameOrigin_ComparesSchemeHostAndPort(string? sourceUrl, string? endpointUrl, bool expected)
    {
        Assert.Equal(expected, NuGetCredentialScope.IsSameOrigin(sourceUrl, endpointUrl));
    }

    [Fact]
    public void AuthFor_SourceWithoutCredentials_ReturnsNullWithoutReporting()
    {
        List<string> log = [];
        var source = new NuGetSource("contoso", IndexUrl);

        Assert.Null(NuGetCredentialScope.AuthFor(source, "https://attacker.example/x", log.Add));
        Assert.Empty(log);
    }

    [Fact]
    public void AuthFor_ForeignEndpoint_WithholdsAndReports()
    {
        List<string> log = [];
        var source = new NuGetSource("contoso", IndexUrl, new PackageSourceCredential("user", "pass"));

        Assert.Null(NuGetCredentialScope.AuthFor(source, "https://attacker.example/x", log.Add));
        Assert.Contains(log, m => m.Contains("Withholding credentials", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveSources_MissingExplicitConfig_ThrowsRatherThanDefaultingToNuGetOrg()
    {
        // Silently ignoring a config the user named and searching nuget.org instead reports
        // someone else's packages as the answer, with exit code 0.
        string missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.config");

        var ex = Assert.Throws<FileNotFoundException>(() =>
            NuGetSourceResolver.ResolveSources(new NuGetSourceOptions { ConfigFile = missing }));

        Assert.Contains(missing, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSources_MalformedExplicitConfig_ThrowsRatherThanDefaultingToNuGetOrg()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broken-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, "<configuration><packageSources>");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                NuGetSourceResolver.ResolveSources(new NuGetSourceOptions { ConfigFile = path }));

            Assert.Contains("not valid XML", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("package", "Newtonsoft.Json")]
    [InlineData("package", "search", "Newtonsoft.Json")]
    [InlineData("type", "JsonSerializer", "--package", "System.Text.Json")]
    public void UnusableNuGetConfig_IsRejectedAtParseTime(params string[] leadingArgs)
    {
        // ResolveSources throws for an unusable explicit config, but only `package search` wraps
        // its invocation in a try/catch. Without a parse-time validator on the shared option, every
        // other source-aware command reports the same mistake as an unhandled stack trace. This
        // test is the gate on that wiring: it fails if the validator is removed from SharedOptions.
        string missing = Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.config");

        var result = CommandLineBuilder.CreateRootCommand()
            .Parse([.. leadingArgs, "--nugetconfig", missing]);

        Assert.Contains(result.Errors, e => e.Message.Contains(missing, StringComparison.Ordinal));
    }

    [Theory]
    // Same endpoint under canonicalization the URL grammar allows.
    [InlineData("https://feed.example/v3/index.json", "https://FEED.example/v3/index.json", true)]
    [InlineData("https://feed.example/v3/", "https://feed.example/v3", true)]
    [InlineData("https://feed.example:443/v3/index.json", "https://feed.example/v3/index.json", true)]
    [InlineData("https://bücher.example/v3/index.json", "https://xn--bcher-kva.example/v3/index.json", true)]
    // Percent-escape hex digits are case-insensitive per RFC 3986, and Uri preserves whichever
    // spelling the caller wrote, so comparing raw would withhold credentials from the user's feed.
    [InlineData("https://feed.example/a%2fb/index.json", "https://feed.example/a%2Fb/index.json", true)]
    [InlineData("https://feed.example/v3/index.json?k=a%2fb", "https://feed.example/v3/index.json?k=a%2Fb", true)]
    // The escape still denotes a different character than the literal it encodes.
    [InlineData("https://feed.example/a%2fb/index.json", "https://feed.example/a/b/index.json", false)]
    // Different endpoints. Paths and queries are case-sensitive over HTTP, so folding them would
    // hand one feed's credentials to another feed on the same host.
    [InlineData("https://feed.example/FeedA/index.json", "https://feed.example/feeda/index.json", false)]
    [InlineData("https://feed.example/v3/index.json?id=A", "https://feed.example/v3/index.json?id=a", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example/v4/index.json", false)]
    [InlineData("https://feed.example/v3/index.json", "http://feed.example/v3/index.json", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example:8443/v3/index.json", false)]
    [InlineData("https://feed.example/v3/index.json", "https://feed.example.attacker.com/v3/index.json", false)]
    public void IsSameEndpoint_DistinguishesFeedsThatDifferOutsideTheOrigin(
        string a, string b, bool expected)
    {
        Assert.Equal(expected, NuGetCredentialScope.IsSameEndpoint(a, b));
    }

    [Fact]
    public void ResolveSources_ExplicitSourceMatchingConfig_AdoptsCredentialsAndKeepsTheGivenUrl()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        // Same endpoint, spelled with a host case the user chose.
        string asTyped = IndexUrl.Replace("feed.example", "FEED.example", StringComparison.Ordinal);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions { ConfigFile = config.Path, Sources = [asTyped] });

        NuGetSource source = Assert.Single(sources);
        Assert.NotNull(source.Credential);
        // The request must go where the user pointed it, not to the config's spelling.
        Assert.Equal(asTyped, source.Url);
    }

    [Fact]
    public void ResolveSources_ExplicitSourceOnAnotherPath_DoesNotAdoptCredentials()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions
            {
                ConfigFile = config.Path,
                Sources = ["https://feed.example/PRIVATE/index.json"],
            });

        Assert.Null(Assert.Single(sources).Credential);
    }

    [Fact]
    public void ResolveSources_WellFormedConfigDeclaringNoSources_ThrowsRatherThanDefaultingToNuGetOrg()
    {
        // Well-formed XML is not enough: any XML file parses, including a .csproj passed by
        // mistake. Explicit configuration starts empty, so such a file must fail rather than
        // answer from an unrelated feed.
        string path = Path.Combine(Path.GetTempPath(), $"notaconfig-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                NuGetSourceResolver.ResolveSources(new NuGetSourceOptions { ConfigFile = path }));

            Assert.Contains("no package sources", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveConfiguredSources_NoConfiguredSources_DoesNotSubstituteNuGetOrg()
    {
        string path = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, "<configuration><packageSources><clear /></packageSources></configuration>");
        try
        {
            Assert.Empty(NuGetFetch.SourceResolver.ResolveConfiguredSources(path));
            Assert.Empty(NuGetFetch.SourceResolver.ResolveSources(configPath: path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("https://user:t0ken@private.example/v3/index.json", "t0ken")]
    [InlineData("https://:t0ken@private.example/v3/index.json", "t0ken")]
    [InlineData("https://alice@private.example/v3/index.json", "alice")]
    public void IsSupportedSource_CredentialsInUrl_ReportsWithoutEchoingTheCredential(
        string url, string secret)
    {
        // NuGet never sends URL userinfo, so this authenticates against nothing and would
        // otherwise surface as a bare 401 that reads like a wrong credential rather than an
        // unused one.
        Assert.False(SourceResolver.IsSupportedSource(url, out InertString? problem));

        Assert.NotNull(problem);
        string text = problem.Value.ToString();
        Assert.Contains("<user>:<password>", text, StringComparison.Ordinal);

        // The problem text is printed, so it must not carry the credential it is rejecting.
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);

        // It still has to say which source was rejected.
        Assert.Contains("private.example", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSources_CredentialsInConfiguredSource_IsRejected()
    {
        // The option validators only see --source and --add-source. A source declared in a
        // nuget.config reaches a feed without passing either, so rejecting it has to happen
        // where every source is resolved, not where two of them are parsed.
        string path = Path.Combine(Path.GetTempPath(), $"nuget-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="https://alice:t0ken@private.example/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        try
        {
            var ex = Assert.Throws<UnsupportedSourceException>(
                () => SourceResolver.ResolveSources(configPath: path));

            Assert.Contains("<user>:<password>", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("t0ken", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsSupportedSource_RejectionMessage_CarriesNoControlCharacters()
    {
        // The message goes to stderr, and the URL in it can come from a nuget.config in a
        // repository the user only cloned, so it is untrusted text on a terminal path. The
        // rebuild through UriBuilder percent-encodes control characters, which is what keeps an
        // escape sequence in a source URL from reaching the terminal. That is a property of Uri
        // normalization rather than of this code, so it is pinned here: losing it would put a
        // live ESC on stderr with nothing else to catch it.
        string esc = "\u001b[31mPWNED\u001b[0m";

        Assert.False(SourceResolver.IsSupportedSource(
            $"https://alice:t0ken@evil.example/{esc}/index.json?x={esc}", out InertString? problem));

        Assert.NotNull(problem);
        string text = problem.Value.ToString();
        Assert.DoesNotContain(text, c => char.IsControl(c));
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);

        // Still identifies the source, and still without the credential.
        Assert.Contains("evil.example", text, StringComparison.Ordinal);
        Assert.DoesNotContain("t0ken", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSupportedSource_IsTheNonThrowingHalfOfThrowIfUnsupported()
    {
        // The pair has to agree, or a caller that asks first still gets thrown at.
        const string bad = "https://alice:t0ken@private.example/v3/index.json";
        const string good = "https://private.example/v3/index.json";

        Assert.False(SourceResolver.IsSupportedSource(bad));
        Assert.Throws<UnsupportedSourceException>(
            () => UnsupportedSourceException.ThrowIfUnsupported(bad));

        Assert.True(SourceResolver.IsSupportedSource(good));
        UnsupportedSourceException.ThrowIfUnsupported(good);
    }

    [Theory]
    [InlineData("https://api.nuget.org/v3/index.json")]
    [InlineData("https://pkgs.dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json")]
    [InlineData("https://private.example/v3/index.json?access_token=t0ken")]
    [InlineData("/tmp/local/folder")]
    public void IsSupportedSource_SourceWithoutEmbeddedCredentials_IsAccepted(string url)
    {
        // A token in the query is a shape some feeds really use, so it must not be rejected
        // here; only userinfo is unsupported.
        Assert.True(SourceResolver.IsSupportedSource(url, out InertString? problem));
        Assert.Null(problem);
    }

    [Fact]
    public void DescribeConfigProblem_UnreadableConfig_ReportsRatherThanThrows()
    {
        // The caller is a parse-time validator, which runs outside every handler in Program.cs.
        // A throw here is not a reportable error, it is a process crash with a raw stack trace.
        string path = Path.Combine(Path.GetTempPath(), $"locked-{Guid.NewGuid():N}.config");
        File.WriteAllText(path, $"""
            <configuration><packageSources><add key="a" value="{IndexUrl}" /></packageSources></configuration>
            """);
        try
        {
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                string? problem = NuGetSourceResolver.DescribeConfigProblem(path);

                Assert.NotNull(problem);
                Assert.Contains("could not be read", problem, StringComparison.Ordinal);
            }

            // Same file, once the lock is gone, is usable.
            Assert.Null(NuGetSourceResolver.DescribeConfigProblem(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveSources_ValidExplicitConfig_ReturnsItsSources()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions { ConfigFile = config.Path });

        NuGetSource source = Assert.Single(sources);
        Assert.Equal(IndexUrl, source.Url);
    }

    [Fact]
    public void ResolveSources_DisabledMalformedAliasDoesNotInvalidateExplicitConfig()
    {
        using var config = new TempNuGetConfig(
            [("current", IndexUrl), ("legacy", "file:relative")],
            disabledSources: ["legacy"]);

        Assert.Null(
            NuGetSourceResolver.DescribeConfigProblem(
                config.Path));

        PackageSource ordinary = Assert.Single(
            NuGetSourceResolver.ResolveSources(
                new NuGetSourceOptions { ConfigFile = config.Path }));
        Assert.Equal("current", ordinary.Name);

        PackageSource explicitlySelected = Assert.Single(
            NuGetSourceResolver.ResolveSources(
                new NuGetSourceOptions
                {
                    ConfigFile = config.Path,
                    Sources = [IndexUrl],
                }));
        Assert.Equal("current", explicitlySelected.Name);
    }

    [Fact]
    public void ResolveSources_MultipleExplicitSources_ReplacesDefaults()
    {
        // --source documents itself as "replaces defaults", but more than one value used to be
        // forwarded as *additional* sources, which re-entered config resolution and searched
        // feeds the user never named. Asserting the exact set proves nothing was merged in:
        // config resolution falls back to nuget.org even on a machine with no nuget.config.
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = ["https://a.example/v3/index.json", "https://b.example/v3/index.json"]
        });

        Assert.Equal(
            ["https://a.example/v3/index.json", "https://b.example/v3/index.json"],
            sources.Select(s => s.Url));
    }

    [Fact]
    public void ResolveSources_ExplicitSourceWithAddSource_KeepsBoth()
    {
        // SourceResolver's explicit-source fast path returned early, dropping additional sources.
        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = ["https://a.example/v3/index.json"],
            AdditionalSources = ["https://b.example/v3/index.json"]
        });

        Assert.Equal(
            ["https://a.example/v3/index.json", "https://b.example/v3/index.json"],
            sources.Select(s => s.Url));
    }

    [Fact]
    public void ResolveSources_ExplicitSource_AdoptsConfiguredCredentials()
    {
        // Naming an authenticated feed with --source must still pick up the credentials the
        // user declared for that same URL, or the feature cannot reach an authenticated feed.
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = [IndexUrl],
            ConfigFile = config.Path
        });

        NuGetSource source = Assert.Single(sources);
        Assert.NotNull(source.GetAuthHeader());
        Assert.Equal("contoso", source.Name);
    }

    [Fact]
    public void ResolveSources_ExplicitSourceNotInConfig_HasNoCredentials()
    {
        using var config = new TempNuGetConfig(IndexUrl);

        List<NuGetSource> sources = NuGetSourceResolver.ResolveSources(new NuGetSourceOptions
        {
            Sources = ["https://unrelated.example/v3/index.json"],
            ConfigFile = config.Path
        });

        NuGetSource source = Assert.Single(sources);
        Assert.Null(source.GetAuthHeader());
    }

    [Fact]
    public async Task SearchAsync_MultipleExplicitSources_SearchesOnlyThose()
    {
        const string indexA = "https://a.example/v3/index.json";
        const string indexB = "https://b.example/v3/index.json";
        const string searchA = "https://a.example/v3/query";
        const string searchB = "https://b.example/v3/query";

        var handler = new RouteHandler
        {
            [indexA] = $$"""{"resources":[{"@id":"{{searchA}}","@type":"SearchQueryService"}]}""",
            [indexB] = $$"""{"resources":[{"@id":"{{searchB}}","@type":"SearchQueryService"}]}""",
            [searchA] = """{"data":[{"id":"A.Package","version":"1.0.0"}]}""",
            [searchB] = """{"data":[{"id":"B.Package","version":"1.0.0"}]}"""
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client, "q", sourceOptions: new NuGetSourceOptions { Sources = [indexA, indexB] });

        Assert.Empty(outcome.Failures);
        Assert.Equal(2, outcome.Results.Count);

        // Nothing beyond the two named feeds was contacted — in particular not nuget.org.
        Assert.All(handler.Requested, url =>
            Assert.True(
                url.StartsWith("https://a.example/", StringComparison.Ordinal)
                || url.StartsWith("https://b.example/", StringComparison.Ordinal),
                $"unexpected request to {url}"));
    }

    [Fact]
    public async Task SearchAsync_PackageSourceMappingFiltersEachResultByReportingAlias()
    {
        const string indexA = "https://a.example/v3/index.json";
        const string indexB = "https://b.example/v3/index.json";
        const string searchA = "https://a.example/v3/query";
        const string searchB = "https://b.example/v3/query";
        const string results = """
            {"data":[
                {"id":"A.Package","version":"1.0.0"},
                {"id":"B.Package","version":"1.0.0"},
                {"id":"Unmapped.Package","version":"1.0.0"}
            ]}
            """;
        using var config = new TempNuGetConfig(
            [("a", indexA), ("b", indexB)],
            mappings: [("a", "A.*"), ("b", "B.*")]);
        var handler = new RouteHandler
        {
            [indexA] = $$"""{"resources":[{"@id":"{{searchA}}","@type":"SearchQueryService"}]}""",
            [indexB] = $$"""{"resources":[{"@id":"{{searchB}}","@type":"SearchQueryService"}]}""",
            [searchA] = results,
            [searchB] = results,
        };
        using var client = new HttpClient(handler);

        NuGetSearchOutcome outcome = await NuGetSearchService.SearchAsync(
            client,
            "Package",
            sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Empty(outcome.Failures);
        Assert.Equal(
            ["A.Package", "B.Package"],
            outcome.Results.Select(result => result.PackageId));
    }

    [Fact]
    public async Task SearchAsync_ConflictingEligibleAliasCredentialsFail()
    {
        const string index = "https://feed.example/v3/index.json";
        const string search = "https://feed.example/v3/query";
        using var config = new TempNuGetConfig(
            [("anonymous", index), ("authenticated", index)],
            credentialedSource: "authenticated",
            mappings: [("anonymous", "Contoso.*"), ("authenticated", "Contoso.*")]);
        var handler = new RouteHandler
        {
            [index] = $$"""{"resources":[{"@id":"{{search}}","@type":"SearchQueryService"}]}""",
            [search] = """{"data":[{"id":"Contoso.Package","version":"1.0.0"}]}""",
        };
        using var client = new HttpClient(handler);

        PackageSourceMappingException exception =
            await Assert.ThrowsAsync<PackageSourceMappingException>(
                () => NuGetSearchService.SearchAsync(
                    client,
                    "Contoso",
                    sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path }));

        Assert.Equal(
            PackageSourceMappingFailure.ConflictingCredentials,
            exception.Failure);
    }

    [Fact]
    public async Task SearchByPrefixAsync_UsesSelectedSourcesAndFiltersTheirResults()
    {
        const string index = "https://private.example/v3/index.json";
        const string search = "https://private.example/v3/query";

        var handler = new RouteHandler
        {
            [index] = $$"""{"resources":[{"@id":"{{search}}","@type":"SearchQueryService"}]}""",
            [search] = """
                {"data":[
                    {"id":"Contoso.Tools","version":"1.0.0"},
                    {"id":"Other.Contoso","version":"1.0.0"}
                ]}
                """
        };
        using var client = new HttpClient(handler);

        List<NuGetSearchResult> results = await NuGetSearchService.SearchByPrefixAsync(
            client,
            "Contoso.",
            sourceOptions: new NuGetSourceOptions { Sources = [index] });

        NuGetSearchResult result = Assert.Single(results);
        Assert.Equal("Contoso.Tools", result.PackageId);
        Assert.All(handler.Requested, url =>
            Assert.StartsWith("https://private.example/", url, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchByPrefixAsync_FiltersBeforeAggregateLimit()
    {
        const string index = "https://a.example/v3/index.json";
        const string search = "https://a.example/v3/query";
        var handler = new PrefixPagingHandler(index, search);
        using var client = new HttpClient(handler);

        List<NuGetSearchResult> results = await NuGetSearchService.SearchByPrefixAsync(
            client,
            "Contoso.",
            take: 1,
            sourceOptions: new NuGetSourceOptions { Sources = [index] });

        NuGetSearchResult result = Assert.Single(results);
        Assert.Equal("Contoso.Tools", result.PackageId);
    }

    [Fact]
    public async Task SearchByPrefixAsync_DeduplicatesPackageIdsAcrossSources()
    {
        const string indexA = "https://a.example/v3/index.json";
        const string indexB = "https://b.example/v3/index.json";
        const string searchA = "https://a.example/v3/query";
        const string searchB = "https://b.example/v3/query";
        var handler = new RouteHandler
        {
            [indexA] = $$"""{"resources":[{"@id":"{{searchA}}","@type":"SearchQueryService"}]}""",
            [indexB] = $$"""{"resources":[{"@id":"{{searchB}}","@type":"SearchQueryService"}]}""",
            [searchA] = """{"data":[{"id":"Contoso.Tools","version":"1.0.0"}]}""",
            [searchB] = """{"data":[{"id":"contoso.tools","version":"2.0.0"}]}"""
        };
        using var client = new HttpClient(handler);

        List<NuGetSearchResult> results = await NuGetSearchService.SearchByPrefixAsync(
            client,
            "Contoso.",
            sourceOptions: new NuGetSourceOptions { Sources = [indexA, indexB] });

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchByPrefixAsync_FailsClosedWhenSelectedSourceIsUnsearchable()
    {
        const string index = "https://private.example/v3/index.json";
        const string search = "https://private.example/v3/query";
        var handler = new RouteHandler
        {
            [index] = $$"""{"resources":[{"@id":"{{search}}","@type":"SearchQueryService"}]}""",
            [search] = """{"data":[{"id":"Contoso.Tools","version":"1.0.0"}]}"""
        };
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NuGetSearchService.SearchByPrefixAsync(
                client,
                "Contoso.",
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [index, "/local/packages"],
                }));

        Assert.Contains("Could not search every configured NuGet source", error.Message);
    }

    /// <summary>
    /// Regression: source resolution ran only when a source option was passed, so a NuGet.Config
    /// discovered from the working directory was ignored and search silently went to nuget.org —
    /// the exact symptom issue #3417 reported for an authenticated feed configured that way.
    /// </summary>
    [Fact]
    public void ResolveSources_NoOptions_HonorsDiscoveredConfig()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"discover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "NuGet.Config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="contoso" value="https://contoso.example/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            List<PackageSource> sources = NuGetSourceResolver.ResolveSources(null, dir);

            PackageSource only = Assert.Single(sources);
            Assert.Equal("https://contoso.example/v3/index.json", only.Url);
            Assert.False(only.IsNuGetOrg);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Two configured entries differing only by a trailing slash are aliases for one producer,
    /// but package source mapping names those aliases independently. Both must survive source
    /// selection until a package id chooses between them.
    /// </summary>
    [Fact]
    public void ResolveSources_TrailingSlashAliases_AreAllRetained()
    {
        const string bare = "https://feed.example/v3/index.json";
        const string slashed = "https://feed.example/v3/index.json/";

        using var config = new TempNuGetConfig(
            [("bare", bare), ("slashed", slashed)], credentialedSource: "bare");

        List<PackageSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions { Sources = [slashed], ConfigFile = config.Path });

        Assert.Equal(["bare", "slashed"], sources.Select(source => source.Name));
        Assert.All(sources, source => Assert.Equal(slashed, source.Url));
        Assert.NotNull(sources[0].Credential);
        Assert.Null(sources[1].Credential);
    }

    [Fact]
    public void ResolveSources_CommandRelativePathMatchesConfiguredLocalAlias()
    {
        string workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"local-command-{Guid.NewGuid():N}");
        string feed = Path.Combine(workingDirectory, "feed");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            using var config = new TempNuGetConfig(
                [("configured-local", feed)]);

            PackageSource source = Assert.Single(
                NuGetSourceResolver.ResolveSources(
                    new NuGetSourceOptions
                    {
                        ConfigFile = config.Path,
                        Sources = ["feed"],
                    },
                    workingDirectory));

            Assert.Equal("configured-local", source.Name);
            Assert.Equal(feed, source.Url);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Repeated trailing slashes are a different producer identity. An unmatched explicit URL
    /// therefore retains its literal spelling as the alias mapping must name.
    /// </summary>
    [Fact]
    public void ResolveSources_RepeatedTrailingSlash_UsesLiteralAlias()
    {
        using var config = new TempNuGetConfig(
            [("bare", "https://feed.example/v3/index.json"),
             ("slashed", "https://feed.example/v3/index.json/")],
            credentialedSource: "bare");

        List<PackageSource> sources = NuGetSourceResolver.ResolveSources(
            new NuGetSourceOptions
            {
                Sources = ["https://feed.example/v3/index.json//"],
                ConfigFile = config.Path
            });

        PackageSource only = Assert.Single(sources);
        Assert.Equal("https://feed.example/v3/index.json//", only.Name);
        Assert.Null(only.Credential);
        Assert.Null(only.GetAuthHeader());
    }

    [Fact]
    public void ResolveSourcesForPackage_MappingAbsent_AllowsEveryProducer()
    {
        using var config = new TempNuGetConfig(
            [("a", "https://a.example/v3/index.json"),
             ("b", "https://b.example/v3/index.json")]);

        List<PackageSource> sources = NuGetSourceResolver.ResolveSourcesForPackage(
            new NuGetSourceOptions { ConfigFile = config.Path },
            "Contoso.Package");

        Assert.Equal(["a", "b"], sources.Select(source => source.Name));
    }

    [Fact]
    public void ConfiguredAuthority_QueryDistinctSameProducerSourcesRemainDistinct()
    {
        using var config = new TempNuGetConfig(
            [("first", "https://feed.example/v3/index.json?tenant=first"),
             ("second", "https://feed.example/v3/index.json?tenant=second")],
            mappings: [("first", "*"), ("second", "*")]);

        List<PackageSource> sources =
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package");

        Assert.Equal(["first", "second"], sources.Select(source => source.Name));
    }

    [Fact]
    public void ConfiguredAuthority_CredentialPathRotationsRemainDistinctWithoutDiagnosticDisclosure()
    {
        const string FirstSecret = "credential-slot-first";
        const string SecondSecret = "credential-slot-second";
        ConfiguredPackageAuthorityKey first =
            ConfiguredPackageAuthorityKey.Create(
                new NuGetSource(
                    "first",
                    $"https://feed.example/{FirstSecret}/index.json"));
        ConfiguredPackageAuthorityKey second =
            ConfiguredPackageAuthorityKey.Create(
                new NuGetSource(
                    "second",
                    $"https://feed.example/{SecondSecret}/index.json"));

        Assert.NotEqual(first, second);
        Assert.DoesNotContain(FirstSecret, first.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SecondSecret, second.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "https://feed.example/%7E/private/index.json",
        "https://feed.example/~/private/index.json")]
    [InlineData(
        "https://feed.example/other/../v3/index.json",
        "https://feed.example/v3/index.json")]
    public void ConfiguredAuthority_RawPathDistinctionsRemainSeparate(
        string firstUrl,
        string secondUrl)
    {
        ConfiguredPackageAuthorityKey first =
            ConfiguredPackageAuthorityKey.Create(
                new PackageSource("first", firstUrl));
        ConfiguredPackageAuthorityKey second =
            ConfiguredPackageAuthorityKey.Create(
                new PackageSource("second", secondUrl));

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("https://api.nuget.org/v3/%69ndex.json")]
    [InlineData("https://api.nuget.org/other/../v3/index.json")]
    public void ConfiguredAuthority_NoncanonicalNuGetOrgPathIsNotGallery(
        string sourceUrl)
    {
        var source = new PackageSource("custom", sourceUrl);
        ConfiguredPackageAuthorityKey authority =
            ConfiguredPackageAuthorityKey.Create(source);

        Assert.False(authority.IsNuGetOrg);
        Assert.False(source.IsNuGetOrg);
    }

    [Theory]
    [InlineData("https://api.nuget.org/v3/index.json")]
    [InlineData("HTTPS://API.NUGET.ORG:443/v3/index.json/")]
    public void ConfiguredAuthority_CanonicalNuGetOrgPathIsGallery(
        string sourceUrl)
    {
        var source = new PackageSource("nuget.org", sourceUrl);
        ConfiguredPackageAuthorityKey authority =
            ConfiguredPackageAuthorityKey.Create(source);

        Assert.True(authority.IsNuGetOrg);
        Assert.True(source.IsNuGetOrg);
    }

    [Fact]
    public void ResolveSources_EncodedPathDoesNotAdoptLiteralPathCredentials()
    {
        const string Encoded =
            "https://feed.example/%7E/private/index.json";
        const string Literal =
            "https://feed.example/~/private/index.json";
        using var config = new TempNuGetConfig(
            [("encoded", Encoded)],
            credentialedSource: "encoded");

        PackageSource source = Assert.Single(
            NuGetSourceResolver.ResolveSources(
                new NuGetSourceOptions
                {
                    ConfigFile = config.Path,
                    Sources = [Literal],
                }));

        Assert.Equal(Literal, source.Name);
        Assert.Equal(Literal, source.Url);
        Assert.Null(source.Credential);
    }

    [Fact]
    public void ResolveSourcesForPackageWithFailures_RetainsValidPeer()
    {
        using var config = new TempNuGetConfig(
            [("valid", IndexUrl),
             ("unsupported", "ftp://legacy.example/v3/index.json")]);

        PackageSourceResolution resolution =
            NuGetSourceResolver.ResolveSourcesForPackageWithFailures(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package");

        Assert.Equal(
            ["valid"],
            resolution.Sources.Select(source => source.Name));
        PackageSourceResolutionFailure failure =
            Assert.Single(resolution.Failures);
        Assert.Equal("unsupported", failure.Name);
        Assert.Equal("unsupported", failure.Authority.ToString());
        Assert.DoesNotContain(
            "ftp://legacy.example",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSourcesForPackageWithFailures_MappedUnsupportedAliasIsNotInactive()
    {
        using var config = new TempNuGetConfig(
            [("legacy", "ftp://legacy.example/v3/index.json")],
            mappings: [("legacy", "*")]);

        PackageSourceResolution resolution =
            NuGetSourceResolver.ResolveSourcesForPackageWithFailures(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package");

        Assert.Empty(resolution.Sources);
        PackageSourceResolutionFailure failure =
            Assert.Single(resolution.Failures);
        Assert.Equal("legacy", failure.Name);
        Assert.DoesNotContain(
            "not active",
            failure.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveSourcesForPackageWithFailures_ExplicitUnsupportedSourceRetainsMappedAlias()
    {
        const string Unsupported =
            "ftp://legacy.example/v3/index.json";
        using var config = new TempNuGetConfig(
            [("valid", IndexUrl), ("legacy", Unsupported)],
            mappings: [("valid", "*"), ("legacy", "*")]);

        PackageSourceResolution resolution =
            NuGetSourceResolver.ResolveSourcesForPackageWithFailures(
                new NuGetSourceOptions
                {
                    ConfigFile = config.Path,
                    Sources = [IndexUrl, Unsupported],
                },
                "Contoso.Package");

        Assert.Equal(
            ["valid"],
            resolution.Sources.Select(source => source.Name));
        PackageSourceResolutionFailure failure =
            Assert.Single(resolution.Failures);
        Assert.Equal("legacy", failure.Name);
        Assert.Equal("legacy", failure.Authority.ToString());
    }

    [Fact]
    public void ResolveSourcesForPackageWithFailures_ExplicitMalformedSourceRetainsMappedAlias()
    {
        const string Malformed =
            "https://example.invalid/%zz/index.json";
        using var config = new TempNuGetConfig(
            [("valid", IndexUrl), ("malformed", Malformed)],
            mappings: [("valid", "*"), ("malformed", "*")]);

        PackageSourceResolution resolution =
            NuGetSourceResolver.ResolveSourcesForPackageWithFailures(
                new NuGetSourceOptions
                {
                    ConfigFile = config.Path,
                    Sources = [IndexUrl, Malformed],
                },
                "Contoso.Package");

        Assert.Equal(
            ["valid"],
            resolution.Sources.Select(source => source.Name));
        PackageSourceResolutionFailure failure =
            Assert.Single(resolution.Failures);
        Assert.Equal("malformed", failure.Name);
        Assert.Equal("malformed", failure.Authority.ToString());
    }

    [Fact]
    public void ResolveSourcesForPackage_LegacyCallerRetainsMalformedPeer()
    {
        const string Malformed =
            "https://example.invalid/%zz/index.json";
        using var config = new TempNuGetConfig(
            [("valid", IndexUrl), ("malformed", Malformed)]);

        List<PackageSource> sources =
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package");

        Assert.Equal(
            ["valid", "malformed"],
            sources.Select(source => source.Name));
    }

    [Fact]
    public void ResolveSourcesForPackage_OneTrailingSlashAliasesCollapse()
    {
        using var config = new TempNuGetConfig(
            [("bare", "https://feed.example/v3/index.json"),
             ("slashed", "https://feed.example/v3/index.json/")],
            mappings: [("bare", "*"), ("slashed", "*")]);

        PackageSource source = Assert.Single(
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package"));

        Assert.Equal("bare", source.Name);
    }

    [Fact]
    public void ResolveSourcesForPackage_RepeatedTrailingSlashAuthorityRemainsDistinct()
    {
        using var config = new TempNuGetConfig(
            [("slashed", "https://feed.example/v3/index.json/"),
             ("repeated", "https://feed.example/v3/index.json//")],
            mappings: [("slashed", "*"), ("repeated", "*")]);

        List<PackageSource> sources =
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package");

        Assert.Equal(
            ["slashed", "repeated"],
            sources.Select(source => source.Name));
    }

    [Fact]
    public void ResolveSourcesForPackage_MappingCollapsesEquivalentLocalAliases()
    {
        string feed = Path.Combine(
            Path.GetTempPath(),
            $"local-feed-{Guid.NewGuid():N}");
        using var config = new TempNuGetConfig(
            [("path", feed), ("uri", new Uri(feed).AbsoluteUri)],
            mappings: [("path", "*"), ("uri", "*")]);

        PackageSource source = Assert.Single(
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package"));

        Assert.Equal(feed, source.Url);
    }

    [Fact]
    public void PackageSourceMapping_SelectsAliasesBeforeAuthorityCollapse()
    {
        string feed = Path.Combine(
            Path.GetTempPath(),
            $"mapped-local-feed-{Guid.NewGuid():N}");
        using var config = new TempNuGetConfig(
            [("path", feed), ("uri", new Uri(feed).AbsoluteUri)],
            mappings:
            [
                ("path", "Path.*"),
                ("uri", "Uri.*"),
            ]);
        var authorization = new SourcePolicyPackageSourceAuthorization(
            new NuGetSourceOptions { ConfigFile = config.Path });

        ConfiguredPackageAuthority path = Assert.Single(
            authorization.AuthorizeSourcesFor("path.package").Authorities);
        ConfiguredPackageAuthority uri = Assert.Single(
            authorization.AuthorizeSourcesFor("uri.package").Authorities);

        Assert.Equal("path", path.Source.Name);
        Assert.Equal("uri", uri.Source.Name);
        Assert.Equal(path.LocalIdentity, uri.LocalIdentity);
    }

    [Fact]
    public void PackageSourceAuthorization_QueryDistinctAuthoritiesHaveExactAssociations()
    {
        using var config = new TempNuGetConfig(
            [
                ("tenant-a", "https://feed.example/v3/index.json?tenant=a"),
                ("tenant-b", "https://feed.example/v3/index.json?tenant=b"),
            ],
            mappings: [("tenant-a", "*"), ("tenant-b", "*")]);
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                NuGetSourceResolver.ResolveSourcesForPackage(
                    new NuGetSourceOptions { ConfigFile = config.Path },
                    "contoso.package"));
        Assert.Collection(
            authorization.Authorities,
            first =>
            {
                Assert.Null(first.PersistentCacheKey);
                Assert.True(
                    authorization.TryGetAuthority(
                        first.Association,
                        out ConfiguredPackageAuthority? recovered));
                Assert.Same(first, recovered);
            },
            second => Assert.Null(second.PersistentCacheKey));
        ConfiguredPackageAuthority first = authorization.Authorities[0];
        ConfiguredPackageAuthority second = authorization.Authorities[1];
        using IPackageSourceClient firstClient =
            PackageSourceClientFactory.Create(
                first.Source,
                first.Association);
        using IPackageSourceClient secondClient =
            PackageSourceClientFactory.Create(
                second.Source,
                second.Association);

        Assert.Equal(
            firstClient.Source.Producer,
            secondClient.Source.Producer);
        Assert.NotSame(first.Association, second.Association);
        Assert.False(
            authorization.TryGetAuthority(
                PackageSourceAssociation.Create(),
                out _));
    }

    [Fact]
    public void PackageSourceAuthorization_CredentialPathAuthoritiesHaveNoPersistentKey()
    {
        const string firstSecret = "first-secret";
        const string secondSecret = "second-secret";
        using var config = new TempNuGetConfig(
            [
                ("first", $"https://feed.example/F/auth/{firstSecret}/api"),
                ("second", $"https://feed.example/F/auth/{secondSecret}/api"),
            ],
            mappings: [("first", "*"), ("second", "*")]);
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                NuGetSourceResolver.ResolveSourcesForPackage(
                    new NuGetSourceOptions { ConfigFile = config.Path },
                    "contoso.package"));
        ConfiguredPackageAuthority first = authorization.Authorities[0];
        ConfiguredPackageAuthority second = authorization.Authorities[1];
        using IPackageSourceClient firstClient =
            PackageSourceClientFactory.Create(
                first.Source,
                first.Association);
        using IPackageSourceClient secondClient =
            PackageSourceClientFactory.Create(
                second.Source,
                second.Association);

        Assert.Equal(
            firstClient.Source.Producer,
            secondClient.Source.Producer);
        Assert.NotSame(first, second);
        Assert.Null(first.PersistentCacheKey);
        Assert.Null(second.PersistentCacheKey);
    }

    [Fact]
    public void PackageSourceAuthorization_HttpAuthorityWithoutStableIdHasNoPersistentKey()
    {
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [new PackageSource("online", IndexUrl)]);

        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.Authorities);
        Assert.Equal(ConfiguredPackageAuthorityKind.Http, authority.Kind);
        Assert.NotNull(authority.HttpEndpoint);
        Assert.Null(authority.LocalIdentity);
        Assert.Null(authority.PersistentCacheKey);
    }

    [Fact]
    public void SourceClassification_PlainDirectoryNeverConstructsHttpTransport()
    {
        string feed = Path.Combine(
            Path.GetTempPath(),
            $"plain-local-feed-{Guid.NewGuid():N}");
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [new PackageSource("local", feed)]);

        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.Authorities);
        Assert.Equal(
            ConfiguredPackageAuthorityKind.LocalFolder,
            authority.Kind);
        Assert.Equal(feed, authority.LocalIdentity!.CanonicalPath);
        Assert.Null(authority.HttpEndpoint);
        Assert.NotNull(authority.PersistentCacheKey);
    }

    [Fact]
    public void SourceClassification_FileUriNeverConstructsHttpTransport()
    {
        string feed = Path.Combine(
            Path.GetTempPath(),
            $"uri-local-feed-{Guid.NewGuid():N}");
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize(
                [new PackageSource("local", new Uri(feed).AbsoluteUri)]);

        ConfiguredPackageAuthority authority =
            Assert.Single(authorization.Authorities);
        Assert.Equal(
            ConfiguredPackageAuthorityKind.LocalFolder,
            authority.Kind);
        Assert.Equal(feed, authority.LocalIdentity!.CanonicalPath);
        Assert.Null(authority.HttpEndpoint);
    }

    [Fact]
    public void SourceClassification_UnsupportedSchemeCreatesNoAuthorityOrRequest()
    {
        using var config = new TempNuGetConfig(
            [("legacy", "ftp://feed.example/v3/index.json")]);
        var policy = new SourcePolicyPackageSourceAuthorization(
            new NuGetSourceOptions { ConfigFile = config.Path });

        PackageSourceAuthorization authorization =
            policy.AuthorizeSourcesFor("contoso.package");

        Assert.Empty(authorization.Authorities);
        Assert.Empty(authorization.Sources);
        Assert.Contains(
            "HTTP(S)",
            authorization.DenialReason!,
            StringComparison.Ordinal);
        Assert.Contains(
            "legacy",
            authorization.DenialReason!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackageSourceMapping_ConflictingAliasPoliciesFailBeforeClientCreation()
    {
        const string endpoint = "https://feed.example/v3/index.json";
        using var config = new TempNuGetConfig(
            [("anonymous", endpoint), ("authenticated", endpoint)],
            credentialedSource: "authenticated",
            mappings: [("anonymous", "*"), ("authenticated", "*")]);
        var policy = new SourcePolicyPackageSourceAuthorization(
            new NuGetSourceOptions { ConfigFile = config.Path });

        PackageSourceAuthorization authorization =
            policy.AuthorizeSourcesFor("contoso.package");

        Assert.Empty(authorization.Authorities);
        Assert.Contains(
            "conflicting credentials",
            authorization.DenialReason!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveSourcesForPackage_MappingSelectsConfiguredName()
    {
        using var config = new TempNuGetConfig(
            [("a", "https://a.example/v3/index.json"),
             ("b", "https://b.example/v3/index.json")],
            mappings: [("a", "A.*"), ("B", "B.*")]);

        PackageSource source = Assert.Single(
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "B.Package"));

        Assert.Equal("b", source.Name);
    }

    [Fact]
    public void ResolveSourcesForPackage_MappingClassifiesOnlySelectedAliases()
    {
        using var config = new TempNuGetConfig(
            [("current", IndexUrl), ("legacy", "file:relative")],
            mappings:
            [
                ("current", "Contoso.*"),
                ("legacy", "Legacy.*"),
            ]);

        PackageSource current = Assert.Single(
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Contoso.Package"));

        Assert.Equal("current", current.Name);
        Assert.Throws<UnsupportedSourceException>(
            () => NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Legacy.Package"));
    }

    [Fact]
    public void ResolveSourcesForPackage_UnmatchedPackageFails()
    {
        using var config = new TempNuGetConfig(
            [("a", "https://a.example/v3/index.json")],
            mappings: [("a", "A.*")]);

        PackageSourceMappingException exception = Assert.Throws<PackageSourceMappingException>(
            () => NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "B.Package"));

        Assert.Equal(PackageSourceMappingFailure.NoPattern, exception.Failure);
        Assert.Contains("no pattern", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSourcesForPackage_InactiveMappedSourceFails()
    {
        using var config = new TempNuGetConfig(
            [("active", "https://active.example/v3/index.json")],
            mappings: [("inactive", "*")]);

        PackageSourceMappingException exception = Assert.Throws<PackageSourceMappingException>(
            () => NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions { ConfigFile = config.Path },
                "Any.Package"));

        Assert.Equal(PackageSourceMappingFailure.InactiveSource, exception.Failure);
        Assert.Contains("inactive", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not active", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSourcesForPackage_ExplicitUrlRetainsAliasesUntilMappingSelectsOne()
    {
        const string bare = "https://feed.example/v3/index.json";
        const string slashed = "https://feed.example/v3/index.json/";
        using var config = new TempNuGetConfig(
            [("bare", bare), ("slashed", slashed)],
            credentialedSource: "bare",
            mappings: [("bare", "Contoso.*"), ("slashed", "Other.*")]);

        PackageSource source = Assert.Single(
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions
                {
                    ConfigFile = config.Path,
                    Sources = [slashed],
                },
                "Contoso.Package"));

        Assert.Equal("bare", source.Name);
        Assert.Equal(slashed, source.Url);
        Assert.NotNull(source.Credential);
    }

    [Fact]
    public void ResolveSourcesForPackage_EligibleAliasesWithConflictingCredentialsFail()
    {
        const string bare = "https://feed.example/v3/index.json";
        const string slashed = "https://feed.example/v3/index.json/";
        using var config = new TempNuGetConfig(
            [("bare", bare), ("slashed", slashed)],
            credentialedSource: "bare",
            mappings: [("bare", "*"), ("slashed", "*")]);

        PackageSourceMappingException exception = Assert.Throws<PackageSourceMappingException>(
            () => NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions
                {
                    ConfigFile = config.Path,
                    Sources = [slashed],
                },
                "Contoso.Package"));

        Assert.Equal(PackageSourceMappingFailure.ConflictingCredentials, exception.Failure);
        Assert.Contains("conflicting credentials", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSourcesForPackage_ExplicitUrlRetainsDisabledConfiguredAlias()
    {
        using var config = new TempNuGetConfig(
            [("private", IndexUrl)],
            credentialedSource: "private",
            mappings: [("private", "Contoso.*")],
            disabledSources: ["private"]);

        PackageSource source = Assert.Single(
            NuGetSourceResolver.ResolveSourcesForPackage(
                new NuGetSourceOptions
                {
                    ConfigFile = config.Path,
                    Sources = [IndexUrl],
                },
                "Contoso.Package"));

        Assert.Equal("private", source.Name);
        Assert.NotNull(source.Credential);
    }

    [Fact]
    public void ReporterRestriction_IsAppliedAfterPackageSourceMapping()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        using var config = new TempNuGetConfig(
            [("a", sourceA), ("b", sourceB)],
            mappings: [("a", "Contoso.*"), ("b", "Other.*")]);
        NuGetSourceOptions? options = NuGetSourceResolver.RestrictToSources(
            new NuGetSourceOptions { ConfigFile = config.Path },
            [sourceB]);

        List<PackageSource> mapped = NuGetSourceResolver.ResolveSourcesForPackage(
            options,
            "Contoso.Package");
        IReadOnlyList<PackageSource> authorized =
            NuGetSourceResolver.ResolveAuthorizedSources(options, mapped);

        Assert.Equal(["a"], mapped.Select(source => source.Name));
        Assert.Empty(authorized);
    }

    /// <summary>
    /// The desktop authorization adapter answers per package id: it composes
    /// the same mapping and credential policy the CLI uses, rather than handing
    /// one union of every configured source to a caller that would then let any
    /// of them serve any package. A mapping failure keeps its own message
    /// instead of degrading to an unexplained empty set.
    /// </summary>
    [Fact]
    public void SourcePolicyAuthorization_AnswersOneProducerSetPerPackageId()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        using var config = new TempNuGetConfig(
            [("a", sourceA), ("b", sourceB)],
            mappings: [("a", "Contoso.*"), ("b", "Other.*")]);
        var authorization = new SourcePolicyPackageSourceAuthorization(
            new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Equal(
            ["a"],
            authorization.AuthorizeSourcesFor("contoso.package")
                .Sources.Select(source => source.Name));
        Assert.Equal(
            ["b"],
            authorization.AuthorizeSourcesFor("other.package")
                .Sources.Select(source => source.Name));

        PackageSourceAuthorization denied =
            authorization.AuthorizeSourcesFor("unmapped.package");
        Assert.Empty(denied.Sources);
        Assert.Contains(
            "no pattern",
            denied.DenialReason!,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A malformed <c>packageSourceMapping</c> reaches the adapter as an
    /// exception from the configuration reader. The seam's contract is a typed
    /// answer, so it becomes a denial rather than escaping into a caller — and
    /// the reader's message quotes the offending config text and path, so the
    /// denial states the rule instead of reproducing it.
    /// </summary>
    [Theory]
    [InlineData("""    <packageSource key="feed" />""")]
    [InlineData("""    <packageSource><package pattern="Contoso.*" /></packageSource>""")]
    [InlineData("""    <packageSource key="feed"><package /></packageSource>""")]
    [InlineData("""    <packageSource key="feed"><package pattern="Con*oso" /></packageSource>""")]
    public void SourcePolicyAuthorization_WithMalformedMapping_DeniesTyped(
        string mappingBody)
    {
        using var config = new TempNuGetConfig(
            [("feed", IndexUrl)],
            rawMapping: mappingBody);
        var authorization = new SourcePolicyPackageSourceAuthorization(
            new NuGetSourceOptions { ConfigFile = config.Path });

        PackageSourceAuthorization denied =
            authorization.AuthorizeSourcesFor("contoso.package");

        Assert.Empty(denied.Sources);
        Assert.NotNull(denied.DenialReason);
        Assert.Contains(
            "malformed",
            denied.DenialReason,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            config.Path,
            denied.DenialReason,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Contoso",
            denied.DenialReason,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same malformed configuration reaches the coordinate resolver as a
    /// typed unavailable rather than an unhandled exception.
    /// </summary>
    [Fact]
    public async Task SourcePolicyResolution_WithMalformedMapping_IsUnavailable()
    {
        using var config = new TempNuGetConfig(
            [("feed", IndexUrl)],
            rawMapping: """    <packageSource key="feed" />""");
        using var client = new HttpClient(new ThrowingHandler());

        PackageCoordinateResolution resolution =
            await PackageCoordinateResolver.ResolveUsingSourcePolicyAsync(
                client,
                new PackageCoordinate("contoso.package", "1.0.0"),
                new NuGetSourceOptions { ConfigFile = config.Path },
                cancellationToken: TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<PackageCoordinateResolution.Unavailable>(resolution);
        Assert.Contains(
            "malformed",
            unavailable.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Malformed configuration reached the network: {request.RequestUri}");
    }

    /// <summary>
    /// A search endpoint is feed-declared metadata that can carry a signature,
    /// and the exception raised for a malformed response embedded it. Both the
    /// endpoint and the remote's own message stay out of the failure a caller
    /// prints — and the request that carried the signature is structurally
    /// correct, which retaining the secret alone does not show.
    /// </summary>
    [Theory]
    [InlineData("https://feed.example/v3/query")]
    [InlineData("https://feed.example/v3/query?sig=SECRETVALUE")]
    [InlineData("https://feed.example/v3/query?sig=SECRETVALUE&api-version=2")]
    [InlineData("https://feed.example/v3/query?")]
    [InlineData("https://feed.example/v3/query#anchor")]
    public async Task SearchAsync_SignedEndpointWithInvalidDocument_ComposesTheQueryAndHidesTheEndpoint(
        string declaredSearchUrl)
    {
        const string secret = "SECRETVALUE";
        var handler = new RouteHandler
        {
            [IndexUrl] = ServiceIndex(declaredSearchUrl),
            // A syntactically valid document of the wrong shape: no "data".
            ["https://feed.example/v3/query"] = """{"totalHits":0}""",
        };
        using var client = new HttpClient(handler);
        List<string> logs = [];

        // Every configured source failed, so the search surfaces the failure
        // list rather than an empty result. That message is what a user sees.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await NuGetSearchService.SearchAsync(
                client,
                "Contoso",
                log: logs.Add,
                sourceOptions: new NuGetSourceOptions { Sources = [IndexUrl] }));

        string requested = Assert.Single(
            handler.Requested,
            url => url.Contains("/v3/query", StringComparison.Ordinal));
        var outbound = new Uri(requested, UriKind.Absolute);

        // The path is untouched, the query has one boundary, and the fragment
        // is gone: the parameters joined the query rather than extending an
        // existing value.
        Assert.Equal("/v3/query", outbound.AbsolutePath);
        Assert.Equal(1, requested.Count(character => character == '?'));
        Assert.Equal(string.Empty, outbound.Fragment);

        Dictionary<string, string> parameters = QueryParameters(outbound);
        Assert.Equal("Contoso", parameters["q"]);
        Assert.Equal("0", parameters["skip"]);
        Assert.Equal("20", parameters["take"]);
        Assert.Equal("false", parameters["prerelease"]);

        // A signature the endpoint declared survives as its own parameter,
        // with its own value.
        bool signed = declaredSearchUrl.Contains("sig=", StringComparison.Ordinal);
        Assert.Equal(signed, parameters.ContainsKey("sig"));
        if (signed)
            Assert.Equal(secret, parameters["sig"]);
        Assert.Equal(
            declaredSearchUrl.Contains("api-version=", StringComparison.Ordinal),
            parameters.ContainsKey("api-version"));

        // And nothing that prints carries it.
        Assert.DoesNotContain(secret, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("search failed", thrown.Message, StringComparison.Ordinal);
        Assert.All(
            logs,
            line => Assert.DoesNotContain(secret, line, StringComparison.Ordinal));
    }

    static Dictionary<string, string> QueryParameters(Uri uri)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);
            string name = separator < 0 ? pair : pair[..separator];
            string value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            parameters[Uri.UnescapeDataString(name)] =
                Uri.UnescapeDataString(value);
        }

        return parameters;
    }

    /// <summary>
    /// A nuget.org URL that is not the canonical service index is still the exact source the user
    /// named. Standard discovery must consult that source rather than substituting another
    /// nuget.org endpoint.
    /// </summary>
    [Theory]
    [InlineData("https://api.nuget.org/definitely-not-a-service-index")]
    [InlineData("https://api.nuget.org/v3/index.json//")]
    [InlineData("https://api.nuget.org/v3/index.json#custom")]
    public async Task SearchAsync_NoncanonicalNuGetOrgSource_UsesNamedServiceIndex(
        string odd)
    {
        var handler = new RouteHandler();
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => NuGetSearchService.SearchAsync(
            client, "Newtonsoft.Json", sourceOptions: new NuGetSourceOptions { Sources = [odd] }));

        // The named source was consulted through ordinary service-index discovery.
        Assert.NotEmpty(handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            url => url.Contains("api.nuget.org/v3/query", StringComparison.Ordinal));
    }

    /// <summary>Writes a nuget.config naming the given sources, and deletes it on dispose.</summary>
    private sealed class TempNuGetConfig : IDisposable
    {
        public string Path { get; }

        /// <summary>Declares one credentialed source named "contoso".</summary>
        public TempNuGetConfig(string sourceUrl)
            : this([("contoso", sourceUrl)], credentialedSource: "contoso")
        {
        }

        public TempNuGetConfig(
            IReadOnlyList<(string Name, string Url)> sources,
            string? credentialedSource = null,
            IReadOnlyList<(string Source, string Pattern)>? mappings = null,
            IReadOnlyList<string>? disabledSources = null,
            string? rawMapping = null)
        {
            string adds = string.Join(
                Environment.NewLine,
                sources.Select(s => $"""    <add key="{s.Name}" value="{s.Url}" />"""));

            string credentials = credentialedSource is null ? "" : $"""
                  <packageSourceCredentials>
                    <{credentialedSource}>
                      <add key="Username" value="user" />
                      <add key="ClearTextPassword" value="pass" />
                    </{credentialedSource}>
                  </packageSourceCredentials>
                """;
            string mapping = rawMapping is not null ? $"""
                  <packageSourceMapping>
                {rawMapping}
                  </packageSourceMapping>
                """
                : mappings is null ? "" : $"""
                  <packageSourceMapping>
                {string.Join(
                    Environment.NewLine,
                    mappings
                        .GroupBy(item => item.Source)
                        .Select(group => $"""
                    <packageSource key="{group.Key}">
                {string.Join(
                    Environment.NewLine,
                    group.Select(item => $"""      <package pattern="{item.Pattern}" />"""))}
                    </packageSource>
                """))}
                  </packageSourceMapping>
                """;
            string disabled = disabledSources is null ? "" : $"""
                  <disabledPackageSources>
                {string.Join(
                    Environment.NewLine,
                    disabledSources.Select(
                        source => $"""    <add key="{source}" value="true" />"""))}
                  </disabledPackageSources>
                """;

            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nuget-{Guid.NewGuid():N}.config");
            File.WriteAllText(Path, $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                {adds}
                  </packageSources>
                {credentials}
                {mapping}
                {disabled}
                </configuration>
                """);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp file.
            }
        }
    }

    /// <summary>
    /// Serves canned bodies by URL (ignoring the query string) and records what was asked for,
    /// including the Authorization header each request carried. Unknown URLs return 404.
    /// </summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
            new("WebAssemblyEnableStreamingResponse");
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Func<HttpContent>> _contentRoutes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Exception> _exceptions =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Url, AuthenticationHeaderValue? Auth)> _requests = [];

        public string this[string url] { set => _routes[url] = (HttpStatusCode.OK, value); }

        public IReadOnlyList<string> Requested => _requests.Select(r => r.Url).ToList();
        public bool BrowserStreamingRequested { get; private set; }

        public void RespondWith(string url, HttpStatusCode status, string body = "") =>
            _routes[url] = (status, body);

        public void RespondWithContent(string url, Func<HttpContent> content) =>
            _contentRoutes[url] = content;

        public void Throw(string url, Exception exception) =>
            _exceptions[url] = exception;

        public AuthenticationHeaderValue? AuthFor(string url) =>
            _requests.FirstOrDefault(r => WithoutQuery(r.Url).Equals(url, StringComparison.OrdinalIgnoreCase)).Auth;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            _requests.Add((url, request.Headers.Authorization));
            BrowserStreamingRequested |= request.Options.TryGetValue(
                BrowserStreamingResponse,
                out bool enabled)
                && enabled;
            string routeUrl = WithoutQuery(url);

            if (_exceptions.TryGetValue(routeUrl, out Exception? exception))
                return Task.FromException<HttpResponseMessage>(exception);

            bool laterSearchPage = request.RequestUri.Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Any(parameter =>
                    parameter.StartsWith("skip=", StringComparison.OrdinalIgnoreCase)
                    && parameter is not "skip=0");
            HttpResponseMessage response = laterSearchPage
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[]}""")
                }
                : _contentRoutes.TryGetValue(
                    routeUrl,
                    out Func<HttpContent>? content)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content()
                }
                : _routes.TryGetValue(
                    routeUrl,
                    out (HttpStatusCode Status, string Body) route)
                ? new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };

            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public static string WithoutQuery(string url)
        {
            int q = url.IndexOf('?', StringComparison.Ordinal);
            return q < 0 ? url : url[..q];
        }
    }

    private sealed class SearchBudgetHandler(
        string indexUrl,
        string firstSearch,
        string secondSearch,
        string thirdSearch) : HttpMessageHandler
    {
        private readonly List<string> _requested = [];

        public IReadOnlyList<string> Requested => _requested;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            _requested.Add(url);

            string body;
            if (url.Equals(indexUrl, StringComparison.Ordinal))
            {
                body = $$"""
                    {"resources":[
                      {"@id":"{{firstSearch}}","@type":"SearchQueryService/3.5.0"},
                      {"@id":"{{secondSearch}}","@type":"SearchQueryService/3.5.0"},
                      {"@id":"{{thirdSearch}}","@type":"SearchQueryService/3.5.0"}
                    ]}
                    """;
            }
            else if (url.StartsWith(firstSearch, StringComparison.Ordinal))
            {
                body = "<html>failure</html>";
            }
            else if (url.StartsWith(secondSearch, StringComparison.Ordinal))
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable after infinite delay.");
            }
            else
            {
                body = """{"data":[{"id":"Too.Late","version":"1.0.0"}]}""";
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
                RequestMessage = request,
            };
        }
    }

    private sealed class AdvertisedLengthContent : HttpContent
    {
        public AdvertisedLengthContent(long length)
        {
            Headers.ContentLength = length;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.FromException(
                new InvalidOperationException("Oversized content must not be read."));

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength!.Value;
            return true;
        }
    }

    private sealed class FailingBodyContent : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new PrefixThenFailStream());

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.FromException(
                new InvalidOperationException(
                    "Headers-first metadata must read the response stream."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StallingBodyContent : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new StallingBodyStream());

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.FromException(
                new InvalidOperationException(
                    "Headers-first metadata must read the response stream."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StallingBodyStream : Stream
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

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class PrefixThenFailStream : Stream
    {
        private static readonly byte[] Prefix =
            """{"data":"""u8.ToArray();
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;

            if (_offset == Prefix.Length)
                throw new IOException("Simulated response reset.");

            int count = Math.Min(buffer.Length, Prefix.Length - _offset);
            Prefix.AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return ValueTask.FromResult(Read(buffer.Span));
            }
            catch (IOException ex)
            {
                return ValueTask.FromException<int>(ex);
            }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class PrefixPagingHandler(
        string indexUrl,
        string searchUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            bool takeOne = request.RequestUri.Query
                .TrimStart('?')
                .Split('&')
                .Contains("take=1", StringComparer.Ordinal);
            string body = url.StartsWith(indexUrl, StringComparison.Ordinal)
                ? $$"""{"resources":[{"@id":"{{searchUrl}}","@type":"SearchQueryService"}]}"""
                : takeOne
                    ? """{"data":[{"id":"Other.Package","version":"1.0.0"}]}"""
                    : """
                        {"data":[
                            {"id":"Other.Package","version":"1.0.0"},
                            {"id":"Contoso.Tools","version":"1.0.0"}
                        ]}
                        """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
                RequestMessage = request
            });
        }
    }
}
