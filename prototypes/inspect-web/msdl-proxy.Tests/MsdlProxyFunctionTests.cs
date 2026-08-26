using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Http;

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
    }

    [Fact]
    public void Health_ReturnsPlainTextSuccess()
    {
        var function =
            new MsdlProxyFunction(
                new StubHttpClientFactory(new HttpClient()));

        var result =
            Assert.IsType<ContentResult>(
                function.Health(new DefaultHttpContext().Request));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal("ok", result.Content);
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

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
