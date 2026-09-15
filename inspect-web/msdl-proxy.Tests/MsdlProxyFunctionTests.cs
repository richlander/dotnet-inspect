using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MsdlProxy.Tests;

public sealed class MsdlProxyFunctionTests
{
    [Fact]
    public void GetSymbolAsync_ExposesAnonymousManagedApiRoute()
    {
        MethodInfo method =
            typeof(MsdlProxyFunction).GetMethod(
                nameof(MsdlProxyFunction.GetSymbolAsync))
            ?? throw new InvalidOperationException("MSDL function is absent.");
        Assert.Equal(
            "MsdlProxy",
            method.GetCustomAttribute<FunctionAttribute>()?.Name);

        HttpTriggerAttribute trigger =
            method.GetParameters()[0]
                .GetCustomAttribute<HttpTriggerAttribute>()
            ?? throw new InvalidOperationException("MSDL HTTP trigger is absent.");
        Assert.Equal(AuthorizationLevel.Anonymous, trigger.AuthLevel);
        Assert.Equal("msdl/{pdbFileName}/{symbolKey}", trigger.Route);
        Assert.NotNull(trigger.Methods);
        Assert.Equal(["get"], trigger.Methods);
    }

    [Fact]
    public async Task GetSymbolAsync_RejectsInvalidSegmentsBeforeMsdlRequest()
    {
        var handler = new CountingHandler();
        using var client = new HttpClient(handler);
        var function =
            new MsdlProxyFunction(new StubHttpClientFactory(client));
        var request = new DefaultHttpContext().Request;

        IActionResult result =
            await function.GetSymbolAsync(
                request,
                "../evil.pdb",
                "not-hex",
                TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, handler.RequestCount);
        _ = await ExecuteWithSecurityHeadersAsync(request, result);
        Assert.Equal(StatusCodes.Status400BadRequest, request.HttpContext.Response.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false, StatusCodes.Status200OK)]
    [InlineData(HttpStatusCode.NotFound, false, StatusCodes.Status404NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, false, StatusCodes.Status502BadGateway)]
    [InlineData(HttpStatusCode.OK, true, StatusCodes.Status413PayloadTooLarge)]
    public async Task GetSymbolAsync_EmitsSecurityHeadersForUpstreamOutcomes(
        HttpStatusCode upstreamStatus,
        bool oversized,
        int expectedStatus)
    {
        byte[] symbolBytes = Encoding.UTF8.GetBytes("symbol bytes");
        var handler = new CountingHandler(() =>
        {
            var response = new HttpResponseMessage(upstreamStatus)
            {
                Content = new ByteArrayContent(symbolBytes),
            };
            if (oversized)
                response.Content.Headers.ContentLength = 9 * 1024 * 1024;
            return response;
        });
        using var client = new HttpClient(handler);
        var function = new MsdlProxyFunction(new StubHttpClientFactory(client));
        var request = new DefaultHttpContext().Request;

        IActionResult result = await function.GetSymbolAsync(
            request, "example.pdb", new string('A', 33),
            TestContext.Current.CancellationToken);
        byte[] body = await ExecuteWithSecurityHeadersAsync(request, result);

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(expectedStatus, request.HttpContext.Response.StatusCode);
        if (expectedStatus == StatusCodes.Status200OK)
        {
            Assert.Equal("application/octet-stream", request.HttpContext.Response.ContentType);
            Assert.Equal(symbolBytes, body);
        }
        else
        {
            Assert.Empty(body);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetSymbolAsync_EmitsSecurityHeadersOnTransportFailure(bool timeout)
    {
        var handler = new CountingHandler(() =>
        {
            if (timeout)
                throw new TaskCanceledException();
            throw new HttpRequestException();
        });
        using var client = new HttpClient(handler);
        var function = new MsdlProxyFunction(new StubHttpClientFactory(client));
        var request = new DefaultHttpContext().Request;

        IActionResult result = await function.GetSymbolAsync(
            request, "example.pdb", new string('A', 33),
            TestContext.Current.CancellationToken);
        _ = await ExecuteWithSecurityHeadersAsync(request, result);

        Assert.Equal(StatusCodes.Status502BadGateway, request.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsPlainTextSuccess()
    {
        using var client = new HttpClient();
        var function =
            new MsdlProxyFunction(
                new StubHttpClientFactory(client));
        var request = new DefaultHttpContext().Request;

        var result =
            Assert.IsType<ContentResult>(
                function.Health(request));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal("ok", result.Content);
        byte[] body = await ExecuteWithSecurityHeadersAsync(request, result);
        Assert.Equal(StatusCodes.Status200OK, request.HttpContext.Response.StatusCode);
        Assert.Equal("ok", Encoding.UTF8.GetString(body));
    }

    private static async Task<byte[]> ExecuteWithSecurityHeadersAsync(
        HttpRequest request, IActionResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        using ServiceProvider provider = services.BuildServiceProvider();
        request.HttpContext.RequestServices = provider;
        using var body = new MemoryStream();
        request.HttpContext.Response.Body = body;

        await result.ExecuteResultAsync(
            new ActionContext(request.HttpContext, new RouteData(), new ActionDescriptor()));

        IHeaderDictionary headers = request.HttpContext.Response.Headers;
        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("no-referrer", headers["Referrer-Policy"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal(
            "max-age=63072000; includeSubDomains",
            headers["Strict-Transport-Security"].ToString());
        return body.ToArray();
    }

    private sealed class StubHttpClientFactory(HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(MsdlClient.Name, name);
            return client;
        }
    }

    private sealed class CountingHandler(Func<HttpResponseMessage>? respond = null)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(
                respond?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
