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

    [Fact]
    public async Task SearchAsync_ErrorStatus_Throws()
    {
        var handler = new CapturingHandler("""{"data":[]}""", HttpStatusCode.Unauthorized);
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await service.SearchAsync("q", cancellationToken: TestContext.Current.CancellationToken));
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
}
