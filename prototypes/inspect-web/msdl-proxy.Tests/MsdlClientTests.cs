using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MsdlProxy.Tests;

public sealed class MsdlClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static async Task<(int StatusCode, byte[] Body)> ExecuteAsync(
        IActionResult result)
    {
        if (result is FileStreamResult streamResult)
        {
            await using Stream content = streamResult.FileStream;
            using var body = new MemoryStream();
            await content.CopyToAsync(body);
            return (StatusCodes.Status200OK, body.ToArray());
        }

        int statusCode =
            Assert.IsAssignableFrom<IStatusCodeActionResult>(result)
                .StatusCode
            ?? StatusCodes.Status200OK;
        return (statusCode, []);
    }

    [Fact]
    public async Task ProxySymbolAsync_BuildsExactMsdlUrlShape()
    {
        // Must match the URL SymbolPackageDownloader.SymbolServers.cs already
        // builds client-side: /download/symbols/{pdbFileName}/{symbolKey}/{pdbFileName}.
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        _ = await MsdlClient.ProxySymbolAsync(client, "foo.pdb", "abc123", CancellationToken.None);

        Assert.Equal(
            "https://msdl.microsoft.com/download/symbols/foo.pdb/abc123/foo.pdb",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ProxySymbolAsync_EncodesPdbNameAsOnePathSegment()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        _ = await MsdlClient.ProxySymbolAsync(
            client,
            "foo#.pdb",
            "abc123",
            CancellationToken.None);

        Assert.Equal(
            "https://msdl.microsoft.com/download/symbols/"
            + "foo%23.pdb/abc123/foo%23.pdb",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task ProxySymbolAsync_PassesThroughUpstream404()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        var result = await MsdlClient.ProxySymbolAsync(client, "foo.pdb", "abc123", CancellationToken.None);
        var (statusCode, _) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
    }

    [Fact]
    public async Task ProxySymbolAsync_ReturnsBadGatewayOnUpstreamServerError()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);

        var result = await MsdlClient.ProxySymbolAsync(client, "foo.pdb", "abc123", CancellationToken.None);
        var (statusCode, _) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status502BadGateway, statusCode);
    }

    [Fact]
    public async Task ProxySymbolAsync_StreamsUpstreamBodyThroughOnSuccess()
    {
        var expectedBytes = Encoding.UTF8.GetBytes("fake pdb bytes");
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBytes),
            };
            return response;
        });
        using var client = new HttpClient(handler);

        var result = await MsdlClient.ProxySymbolAsync(client, "foo.pdb", "abc123", CancellationToken.None);
        var (statusCode, body) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Equal(expectedBytes, body);
    }

    [Fact]
    public async Task ProxySymbolAsync_RejectsResponseWithOversizedDeclaredContentLength()
    {
        var oversized = new byte[1];
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(oversized),
            };
            // Lie about the length being far larger than the configured cap
            // so the early Content-Length check has to fire before any body
            // is ever streamed.
            response.Content.Headers.ContentLength = 9 * 1024 * 1024;
            return response;
        });
        using var client = new HttpClient(handler);

        var result = await MsdlClient.ProxySymbolAsync(client, "foo.pdb", "abc123", CancellationToken.None);
        var (statusCode, _) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, statusCode);
    }

    [Fact]
    public async Task ProxySymbolAsync_RejectsUndeclaredBodyAboveBrowserLimit()
    {
        byte[] oversized = new byte[(8 * 1024 * 1024) + 1];
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(oversized),
            });
        using var client = new HttpClient(handler);

        IActionResult result =
            await MsdlClient.ProxySymbolAsync(
                client,
                "foo.pdb",
                "abc123",
                CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => _ = await ExecuteAsync(result));
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
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
}
