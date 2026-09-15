using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Offline tests for <see cref="SearchService"/>: request shaping, credential attachment, and
/// the refusal to report a failed search as an empty one. Live-network coverage lives in
/// <c>SearchServiceIntegrationTests</c>.
/// </summary>
public class SearchServiceTests
{
    private const string SearchUrl = "https://feed.example/query";

    [Fact]
    public async Task SearchAsync_UsesConfiguredSearchUrlAndQueryParameters()
    {
        var handler = new CapturingHandler("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        await service.SearchAsync("json serializer", take: 5, prerelease: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(handler.LastRequest);
        // AbsoluteUri preserves percent-encoding; ToString() unescapes it.
        string url = handler.LastRequest.RequestUri!.AbsoluteUri;
        Assert.StartsWith(SearchUrl + "?", url);
        Assert.Contains("q=json%20serializer", url);
        Assert.Contains("take=5", url);
        Assert.Contains("prerelease=true", url);
        Assert.Contains("semVerLevel=2.0.0", url);
    }

    [Fact]
    public async Task SearchAsync_ReplacesExistingSemVerLevel()
    {
        var handler = new CapturingHandler("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var service = new SearchService(
            client,
            SearchUrl + "?semVerLevel=1.0.0&sig=kept");

        await service.SearchAsync(
            "q",
            cancellationToken: TestContext.Current.CancellationToken);

        string url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("semVerLevel=1.0.0", url);
        Assert.Contains("?sig=kept&", url);
        Assert.Equal(
            1,
            url.Split('&').Count(
                pair => pair.Contains(
                    "semVerLevel=",
                    StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("semVerLevel=2.0.0", url);
    }

    [Fact]
    public async Task SearchAsync_PreservesEncodedSignedQueryBytes()
    {
        const string existing =
            "s%69g=%73ecret&opaque=%7E%41";
        var handler = new CapturingHandler("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var service = new SearchService(
            client,
            $"{SearchUrl}?{existing}");

        await service.SearchAsync(
            "q",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith(
            $"{SearchUrl}?{existing}&",
            handler.LastRequest!.RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_AuthorityOnlyEndpoint_SendsTheRootPath()
    {
        var handler = new CapturingHandler("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var service = new SearchService(client, "https://feed.example");

        await service.SearchAsync(
            "q",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "https://feed.example/?",
            handler.LastRequest!.RequestUri!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_WithAuth_SendsAuthorizationHeader()
    {
        var handler = new CapturingHandler("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);
        var auth = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

        await service.SearchAsync("q", take: 1, prerelease: false, auth: auth,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(auth, handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task SearchAsync_WithoutAuth_SendsNoAuthorizationHeader()
    {
        var handler = new CapturingHandler("""{"data":[]}""");
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        await service.SearchAsync("q", take: 1, prerelease: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task SearchAsync_AzureDevOpsResponse_ReturnsResults()
    {
        // Real Azure DevOps shape: totalHits is a string, and it says "0" even when data
        // is populated. Issue #3417.
        var handler = new CapturingHandler("""
            {"data":[{"id":"Contoso.Internal","version":"1.2.3","versions":[]}],"totalHits":"0"}
            """);
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        IReadOnlyList<SearchResult> results = await service.SearchAsync("Contoso",
            cancellationToken: TestContext.Current.CancellationToken);

        SearchResult result = Assert.Single(results);
        Assert.Equal("Contoso.Internal", result.Id);
        Assert.Equal("1.2.3", result.Version);
    }

    [Fact]
    public async Task SearchAsync_UnicodePackageIds_ReturnResults()
    {
        var handler = new CapturingHandler("""
            {
              "data": [
                {"id":"日本語サンプルデータ","version":"1.2.3","versions":[]},
                {"id":"Contoso.P\u0430ckage","version":"1.2.3","versions":[]},
                {"id":"Pkg\u0301","version":"1.2.3","versions":[]}
              ]
            }
            """);
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        IReadOnlyList<SearchResult> results = await service.SearchAsync(
            "日本語",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["日本語サンプルデータ", "Contoso.P\u0430ckage", "Pkg\u0301"],
            results.Select(result => result.Id));
    }

    [Fact]
    public async Task SearchAsync_MalformedBody_Throws()
    {
        var handler = new CapturingHandler("<html>login required</html>");
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        await Assert.ThrowsAsync<JsonException>(async () =>
            await service.SearchAsync("q", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_NullDocument_Throws()
    {
        var handler = new CapturingHandler("null");
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.SearchAsync("q", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("""{"data":[null]}""", true)]
    [InlineData("""{"data":[{"id":null,"version":"1.0.0"}]}""", true)]
    [InlineData("""{"data":[{"version":"1.0.0"}]}""", true)]
    [InlineData("""{"data":[{"id":"","version":"1.0.0"}]}""", false)]
    [InlineData("""{"data":[{"id":"Contoso.Package","version":null}]}""", true)]
    [InlineData("""{"data":[{"id":"Contoso.Package"}]}""", true)]
    [InlineData("""{"data":[{"id":"Contoso.Package","version":""}]}""", false)]
    [InlineData("""{"data":[{"id":" Contoso.Package","version":"1.0.0"}]}""", false)]
    [InlineData("""{"data":[{"id":"Contoso..Package","version":"1.0.0"}]}""", false)]
    [InlineData("""{"data":[{"id":"Contoso/Package","version":"1.0.0"}]}""", false)]
    [InlineData("{\"data\":[{\"id\":\"Contoso.Package\\u200B\",\"version\":\"1.0.0\"}]}", false)]
    [InlineData("""{"data":[{"id":"Contoso.Package","version":"not-a-version"}]}""", false)]
    [InlineData("""{"data":[{"id":"Contoso.Package","version":" 1.0.0"}]}""", false)]
    [InlineData("{\"data\":[{\"id\":\"Contoso.Package\\n\",\"version\":\"1.0.0\"}]}", false)]
    [InlineData(
        """{"data":[{"id":"Contoso.Package","version":"1.0.0","versions":[{"version":"not-a-version","downloads":1}]}]}""",
        false)]
    public async Task SearchAsync_InvalidResultIdentity_Throws(
        string body,
        bool missingRequiredData)
    {
        var handler = new CapturingHandler(body);
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        if (missingRequiredData)
        {
            await Assert.ThrowsAsync<JsonException>(() =>
                service.SearchAsync(
                    "q",
                    cancellationToken: TestContext.Current.CancellationToken));
        }
        else
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SearchAsync(
                    "q",
                    cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains("result identity", exception.Message);
        }
    }

    [Fact]
    public async Task SearchAsync_ErrorStatus_Throws()
    {
        var handler = new CapturingHandler("""{"data":[]}""", HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await service.SearchAsync("q", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchByPrefixAsync_ContinuesAfterServerCappedPage()
    {
        var handler = new CappedPagingHandler();
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        IReadOnlyList<SearchResult> results = await service.SearchByPrefixAsync(
            "Contoso.",
            take: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Contoso.Match", Assert.Single(results).Id);
        Assert.Equal([0, 50], handler.RequestedSkips);
    }

    [Fact]
    public async Task SearchByPrefixAsync_RejectsRepeatedFullPage()
    {
        var handler = new RepeatingPageHandler();
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchByPrefixAsync(
                "Contoso.",
                take: 1,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("without making progress", error.Message);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task SearchByPrefixAsync_RejectsPageLimit()
    {
        var handler = new EndlessPagingHandler();
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchByPrefixAsync(
                "Contoso.",
                take: 1,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(
            "ended before the requested result count",
            error.Message);
        Assert.Equal(100, handler.RequestCount);
    }

    [Fact]
    public async Task SearchByPrefixWithStateAsync_ReportsSourceSkipLimit()
    {
        var handler = new EndlessPagingHandler();
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        PrefixSearchResult result =
            await service.SearchByPrefixWithStateAsync(
                "Contoso.",
                take: 1,
                maximumSkip: 100,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Matches);
        Assert.Equal(
            PrefixSearchCompletion.SourcePageLimitReached,
            result.Completion);
        Assert.True(result.Truncated);
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class CapturingHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body),
                RequestMessage = request
            });
        }
    }

    private sealed class CappedPagingHandler : HttpMessageHandler
    {
        public List<int> RequestedSkips { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int skip = GetQueryInt(request.RequestUri!, "skip");
            RequestedSkips.Add(skip);
            string body = skip == 0
                ? $$"""{"data":[{{NonmatchingResults(50, 0)}}]}"""
                : """{"data":[{"id":"Contoso.Match","version":"1.0.0"}]}""";
            return Json(body, request);
        }
    }

    private sealed class RepeatingPageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Json(
                $$"""{"data":[{{NonmatchingResults(100, 0)}}]}""",
                request);
        }
    }

    private sealed class EndlessPagingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            int skip = GetQueryInt(request.RequestUri!, "skip");
            return Json(
                $$"""{"data":[{{NonmatchingResults(100, skip)}}]}""",
                request);
        }
    }

    private static int GetQueryInt(Uri uri, string name)
    {
        string prefix = name + "=";
        string value = uri.Query
            .TrimStart('?')
            .Split('&')
            .Single(part => part.StartsWith(prefix, StringComparison.Ordinal));
        return int.Parse(
            value[prefix.Length..],
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string NonmatchingResults(int count, int offset) =>
        string.Join(
            ',',
            Enumerable.Range(offset, count).Select(index =>
                $$"""{"id":"Other.Package.{{index}}","version":"1.0.0"}"""));

    private static Task<HttpResponseMessage> Json(
        string body,
        HttpRequestMessage request) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
            RequestMessage = request
        });
}
