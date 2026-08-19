using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    private static async Task<(int StatusCode, byte[] Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
                .BuildServiceProvider(),
        };
        var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        return (context.Response.StatusCode, body.ToArray());
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
            response.Content.Headers.ContentLength = 300_000_000;
            return response;
        });
        using var client = new HttpClient(handler);

        var result = await MsdlClient.ProxySymbolAsync(client, "foo.pdb", "abc123", CancellationToken.None);
        var (statusCode, _) = await ExecuteAsync(result);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, statusCode);
    }
}
