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
    private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
        new("WebAssemblyEnableStreamingResponse");

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
        Assert.True(
            handler.LastRequest.Options.TryGetValue(
                BrowserStreamingResponse,
                out bool streaming)
            && streaming);
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

    [Fact]
    public async Task SearchAsync_AdvertisedOversizeBody_Throws()
    {
        var handler = new ResponseHandler(static request =>
        {
            var content = new StringContent("""{"data":[]}""");
            content.Headers.ContentLength = NuGetApi.MaxMetadataResponseBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content,
                RequestMessage = request,
            };
        });
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(
            () => service.SearchAsync(
                "q",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("16777216 byte limit", error.Message);
    }

    [Fact]
    public async Task SearchAsync_UnadvertisedOversizeBody_Throws()
    {
        var handler = new ResponseHandler(static request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new OversizeSearchDocumentStream()),
                RequestMessage = request,
            });
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.SearchAsync(
                "q",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_StalledBody_ThrowsTimeout()
    {
        var handler = new ResponseHandler(static request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StalledStream()),
                RequestMessage = request,
            });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        var service = new SearchService(client, SearchUrl);

        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(
            () => service.SearchAsync(
                "q",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("body timed out", error.Message);
    }

    [Fact]
    public async Task SearchAsync_CallerCancellationRemainsCancellation()
    {
        var handler = new ResponseHandler(static request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new StalledStream()),
                RequestMessage = request,
            });
        using var client = new HttpClient(handler);
        var service = new SearchService(client, SearchUrl);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SearchAsync(
                "q",
                cancellationToken: cancellation.Token));
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

        Assert.Contains("exceeded 32 pages", error.Message);
        Assert.Equal(32, handler.RequestCount);
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

    private sealed class ResponseHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class OversizeSearchDocumentStream : Stream
    {
        private static readonly byte[] Prefix =
            System.Text.Encoding.UTF8.GetBytes("{\"data\":[{\"id\":\"");
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read = Fill(buffer);
            _position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = Fill(buffer.Span);
            _position += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        private int Fill(Span<byte> buffer)
        {
            long length = NuGetApi.MaxMetadataResponseBytes + 1;
            if (_position >= length)
                return 0;

            int count = (int)Math.Min(buffer.Length, length - _position);
            for (int index = 0; index < count; index++)
            {
                long absolute = _position + index;
                buffer[index] = absolute < Prefix.Length
                    ? Prefix[(int)absolute]
                    : (byte)'a';
            }

            return count;
        }
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
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
