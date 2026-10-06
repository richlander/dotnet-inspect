using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Http;

namespace MsdlProxy.Tests;

public sealed class PackageChangeProxyFunctionTests
{
    [Theory]
    [InlineData(
        nameof(PackageChangeProxyFunction.GetNuGetAsync),
        "NuGetPackageChangeProxy",
        "package-changes/nuget")]
    [InlineData(
        nameof(PackageChangeProxyFunction.GetAdvisoriesAsync),
        "GitHubPackageChangeAdvisoryProxy",
        "package-changes/advisories")]
    public void Endpoints_ExposeAnonymousGetRoutes(
        string methodName,
        string functionName,
        string route)
    {
        MethodInfo method =
            typeof(PackageChangeProxyFunction).GetMethod(methodName)
            ?? throw new InvalidOperationException(
                $"Function method '{methodName}' is absent.");
        Assert.Equal(
            functionName,
            method.GetCustomAttribute<FunctionAttribute>()?.Name);

        HttpTriggerAttribute trigger =
            method.GetParameters()[0]
                .GetCustomAttribute<HttpTriggerAttribute>()
            ?? throw new InvalidOperationException(
                $"HTTP trigger for '{methodName}' is absent.");
        Assert.Equal(AuthorizationLevel.Anonymous, trigger.AuthLevel);
        Assert.Equal(route, trigger.Route);
        Assert.NotNull(trigger.Methods);
        Assert.Equal(["get"], trigger.Methods);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidRequest_IsRejectedBeforeClientCreation(
        bool nuget)
    {
        var function =
            new PackageChangeProxyFunction(
                new RejectingHttpClientFactory());
        var context = new DefaultHttpContext();
        context.Request.QueryString = nuget
            ? new QueryString("?path=https%3A%2F%2Fexample.com")
            : new QueryString(
                "?ecosystem=npm&type=reviewed&is_withdrawn=false"
                + "&per_page=100&affects=Example");

        IActionResult result = nuget
            ? await function.GetNuGetAsync(
                context.Request,
                TestContext.Current.CancellationToken)
            : await function.GetAdvisoriesAsync(
                context.Request,
                TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        AssertResponseHeaders(context.Response);
    }

    [Fact]
    public async Task ValidAdvisoryRequest_UsesNamedClientAndReturnsJson()
    {
        var handler = new StaticResponseHandler();
        using var client = new HttpClient(handler);
        var factory = new RecordingHttpClientFactory(client);
        var function = new PackageChangeProxyFunction(factory);
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(
            "?ecosystem=nuget&type=reviewed&is_withdrawn=false"
            + "&per_page=100&affects=Microsoft.Extensions.AI");

        IActionResult result =
            await function.GetAdvisoriesAsync(
                context.Request,
                TestContext.Current.CancellationToken);

        var content = Assert.IsType<FileContentResult>(result);
        Assert.Equal("[]", Encoding.UTF8.GetString(content.FileContents));
        Assert.Equal(
            PackageChangeProxyClient.AdvisoryClientName,
            factory.RequestedName);
        Assert.Equal("api.github.com", handler.RequestUri?.Host);
        AssertResponseHeaders(context.Response);
    }

    private static void AssertResponseHeaders(HttpResponse response)
    {
        IHeaderDictionary headers = response.Headers;
        Assert.Equal("no-store", headers.CacheControl.ToString());
        Assert.Equal(
            "nosniff",
            headers["X-Content-Type-Options"].ToString());
        Assert.Equal(
            "no-referrer",
            headers["Referrer-Policy"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal(
            "max-age=63072000; includeSubDomains",
            headers["Strict-Transport-Security"].ToString());
    }

    private sealed class RejectingHttpClientFactory
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException(
                "Invalid input reached outbound client creation.");
    }

    private sealed class RecordingHttpClientFactory(HttpClient client)
        : IHttpClientFactory
    {
        internal string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;
            return client;
        }
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        internal Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8),
            };
            response.Content.Headers.ContentType =
                new("application/json");
            return Task.FromResult(response);
        }
    }
}
