using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MsdlProxy.Tests;

public sealed class PackageChangeProxyClientTests
{
    [Fact]
    public async Task AdvisoryRequest_UsesFixedHeadersAndPreservesLink()
    {
        const string link =
            "<https://api.github.com/advisories?ecosystem=nuget"
            + "&type=reviewed&is_withdrawn=false&per_page=100"
            + "&affects=Example&after=cursor>; rel=\"next\"";
        var handler = new RecordingHandler(request =>
        {
            var response = Json("[]");
            response.Headers.TryAddWithoutValidation("Link", link);
            return response;
        });
        using var client = new HttpClient(handler);
        var context = new DefaultHttpContext();

        IActionResult result =
            await PackageChangeProxyClient.GetAdvisoryJsonAsync(
                client,
                new Uri(
                    "https://api.github.com/advisories"
                    + "?ecosystem=nuget&type=reviewed"
                    + "&is_withdrawn=false&per_page=100&affects=Example"),
                context.Response,
                TestContext.Current.CancellationToken);

        var content = Assert.IsType<FileContentResult>(result);
        Assert.Equal("[]", Encoding.UTF8.GetString(content.FileContents));
        Assert.Equal(link, context.Response.Headers.Link.ToString());
        Assert.Equal("api.github.com", handler.LastRequest?.RequestUri?.Host);
        Assert.Equal(
            "dotnet-inspect",
            handler.LastRequest?.Headers.UserAgent.ToString());
        Assert.Contains(
            handler.LastRequest!.Headers.Accept,
            value => value.MediaType == "application/vnd.github+json");
        Assert.Equal(
            "2022-11-28",
            Assert.Single(
                handler.LastRequest.Headers.GetValues(
                    "X-GitHub-Api-Version")));
        Assert.Null(handler.LastRequest.Headers.Authorization);
        Assert.False(handler.LastRequest.Headers.Contains("Cookie"));
    }

    [Fact]
    public async Task NuGetRequest_ReturnsBoundedJson()
    {
        var handler = new RecordingHandler(_ => Json("""{"version":"3.0.0"}"""));
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        var content = Assert.IsType<FileContentResult>(result);
        Assert.Equal(
            """{"version":"3.0.0"}""",
            Encoding.UTF8.GetString(content.FileContents));
        Assert.Equal(
            "https://api.nuget.org/v3/index.json",
            handler.LastRequest?.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task NuGetCatalogPageWithoutContentType_ReturnsJson()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                    Encoding.UTF8.GetBytes(
                        """{"@id":"https://api.nuget.org/v3/catalog0/page22909.json"}""")),
            });
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri(
                    "https://api.nuget.org/v3/catalog0/page22909.json"),
                TestContext.Current.CancellationToken);

        var content = Assert.IsType<FileContentResult>(result);
        Assert.Equal(
            """{"@id":"https://api.nuget.org/v3/catalog0/page22909.json"}""",
            Encoding.UTF8.GetString(content.FileContents));
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task NuGetServiceIndexWithoutContentType_IsBadGateway()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                    """{"version":"3.0.0"}"""u8.ToArray()),
            });
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            StatusCodes.Status502BadGateway,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Fact]
    public async Task AdvisoryResponseWithoutContentType_IsBadGateway()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("[]"u8.ToArray()),
            });
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetAdvisoryJsonAsync(
                client,
                new Uri(
                    "https://api.github.com/advisories"
                    + "?ecosystem=nuget&type=reviewed"
                    + "&is_withdrawn=false&per_page=100&affects=Example"),
                new DefaultHttpContext().Response,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            StatusCodes.Status502BadGateway,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(
        HttpStatusCode.TooManyRequests,
        StatusCodes.Status429TooManyRequests)]
    [InlineData(
        HttpStatusCode.InternalServerError,
        StatusCodes.Status500InternalServerError)]
    [InlineData(HttpStatusCode.Redirect, StatusCodes.Status302Found)]
    public async Task ProviderStatus_RemainsNonSuccess(
        HttpStatusCode upstream,
        int expected)
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(upstream));
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            expected,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.PartialContent)]
    public async Task UnexpectedSuccessfulStatus_IsBadGateway(
        HttpStatusCode upstream)
    {
        var handler = new RecordingHandler(
            _ => new HttpResponseMessage(upstream));
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            StatusCodes.Status502BadGateway,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Fact]
    public async Task WrongMediaType_IsBadGateway()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html></html>"),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("text/html");
            return response;
        });
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            StatusCodes.Status502BadGateway,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Fact]
    public async Task DeclaredAndObservedOversize_ArePayloadTooLarge()
    {
        var declaredHandler = new RecordingHandler(_ =>
        {
            HttpResponseMessage response = Json("[]");
            response.Content.Headers.ContentLength =
                PackageChangeProxyClient.MaxAdvisoryDocumentBytes + 1;
            return response;
        });
        using var declaredClient = new HttpClient(declaredHandler);
        var context = new DefaultHttpContext();

        IActionResult declared =
            await PackageChangeProxyClient.GetAdvisoryJsonAsync(
                declaredClient,
                new Uri(
                    "https://api.github.com/advisories"
                    + "?ecosystem=nuget&type=reviewed"
                    + "&is_withdrawn=false&per_page=100&affects=Example"),
                context.Response,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(declared)
                .StatusCode);

        byte[] oversized =
            new byte[
                checked((int)
                    PackageChangeProxyClient.MaxAdvisoryDocumentBytes + 1)];
        var observedHandler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(oversized),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");
            return response;
        });
        using var observedClient = new HttpClient(observedHandler);

        IActionResult observed =
            await PackageChangeProxyClient.GetAdvisoryJsonAsync(
                observedClient,
                new Uri(
                    "https://api.github.com/advisories"
                    + "?ecosystem=nuget&type=reviewed"
                    + "&is_withdrawn=false&per_page=100&affects=Example"),
                new DefaultHttpContext().Response,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(observed)
                .StatusCode);
    }

    [Theory]
    [InlineData(false, StatusCodes.Status502BadGateway)]
    [InlineData(true, StatusCodes.Status504GatewayTimeout)]
    public async Task TransportFailure_RemainsVisible(
        bool timeout,
        int expected)
    {
        var handler = new ThrowingHandler(timeout);
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            expected,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Fact]
    public async Task ResponseBodyFailure_IsBadGateway()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingContent(),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");
            return response;
        });
        using var client = new HttpClient(handler);

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            StatusCodes.Status502BadGateway,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Fact]
    public async Task ResponseBodyTimeout_IsGatewayTimeout()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new DelayedReadStream()),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");
            return response;
        });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        IActionResult result =
            await PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            StatusCodes.Status504GatewayTimeout,
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new DelayedReadStream()),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");
            return response;
        });
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(1),
        };
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PackageChangeProxyClient.GetNuGetJsonAsync(
                client,
                new Uri("https://api.nuget.org/v3/index.json"),
                cancellation.Token));
    }

    [Fact]
    public void PrimaryHandler_DisablesRedirectsAndCredentials()
    {
        using var handler = Assert.IsType<HttpClientHandler>(
            PackageChangeProxyClient.CreatePrimaryHandler());

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.PreAuthenticate);
        Assert.Null(handler.Credentials);
        Assert.Equal(
            DecompressionMethods.All,
            handler.AutomaticDecompression);
    }

    private static HttpResponseMessage Json(string value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(value, Encoding.UTF8),
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        internal HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ThrowingHandler(bool timeout)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                timeout
                    ? new TaskCanceledException()
                    : new HttpRequestException());
    }

    private sealed class UnknownLengthContent(byte[] bytes)
        : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            Task.FromException(
                new IOException("Response body failed."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class DelayedReadStream : Stream
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

        public override void Flush()
        {
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            return 0;
        }

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
}
