using System.Net;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

public sealed class NuGetClientTests
{
    [Theory]
    [InlineData("service-index")]
    [InlineData("version-index")]
    public async Task LatestVersion_MalformedSourceContinuesToHealthySource(
        string malformedDocument)
    {
        var handler = new RoutedHandler(malformedDocument);
        using var http = new HttpClient(handler);
        var client = new NuGetClient(http);
        PackageSource[] sources =
        [
            new("malformed", "https://malformed.example/index.json"),
            new("healthy", "https://healthy.example/index.json"),
        ];

        string? version = await client.GetLatestVersionAsync(
            "package",
            sources,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("9.9.9", version);
        Assert.Contains(
            "https://healthy.example/flat/package/index.json",
            handler.Requested);
    }

    [Fact]
    public async Task LatestVersion_MissingServiceIndexContinuesToHealthySource()
    {
        var handler = new MissingSourceHandler();
        using var http = new HttpClient(handler);
        var client = new NuGetClient(http);
        PackageSource[] sources =
        [
            new("missing", "https://missing.example/index.json"),
            new("healthy", "https://healthy.example/index.json"),
        ];

        string? version = await client.GetLatestVersionAsync(
            "package",
            sources,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("9.9.9", version);
        Assert.Equal(
            [
                "https://missing.example/index.json",
                "https://healthy.example/index.json",
                "https://healthy.example/flat/package/index.json",
            ],
            handler.Requested);
    }

    private sealed class RoutedHandler(string malformedDocument)
        : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            string body = url switch
            {
                "https://malformed.example/index.json"
                    when malformedDocument == "service-index" => "{broken",
                "https://malformed.example/index.json" => ServiceIndex("malformed"),
                "https://malformed.example/flat/package/index.json" => "{broken",
                "https://healthy.example/index.json" => ServiceIndex("healthy"),
                "https://healthy.example/flat/package/index.json" =>
                    """{"versions":["9.9.9"]}""",
                _ => throw new InvalidOperationException(url),
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
                RequestMessage = request,
            });
        }

        internal static string ServiceIndex(string host) =>
            $$"""
            {"version":"3.0.0","resources":[{"@id":"https://{{host}}.example/flat/",
            "@type":"PackageBaseAddress/3.0.0"}]}
            """;
    }

    private sealed class MissingSourceHandler : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            (HttpStatusCode Status, string Body) response = url switch
            {
                "https://missing.example/index.json" =>
                    (HttpStatusCode.NotFound, ""),
                "https://healthy.example/index.json" =>
                    (HttpStatusCode.OK, RoutedHandler.ServiceIndex("healthy")),
                "https://healthy.example/flat/package/index.json" =>
                    (HttpStatusCode.OK, """{"versions":["9.9.9"]}"""),
                _ => throw new InvalidOperationException(url),
            };

            return Task.FromResult(
                new HttpResponseMessage(response.Status)
                {
                    Content = new StringContent(response.Body),
                    RequestMessage = request,
                });
        }
    }
}
