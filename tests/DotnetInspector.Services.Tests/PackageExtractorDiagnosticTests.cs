using System.Net;
using System.Text;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed class PackageExtractorDiagnosticTests
{
    [Fact]
    public async Task ServiceIndexDiagnosticsContainSourceAndResourceText()
    {
        const string Secret = "signed-secret";
        const char Escape = '\u001b';
        string sourceUrl = $"https://feed.test/v3/index.json?x={Secret}";
        var source = new PackageSource(sourceUrl, sourceUrl);
        string json =
            $$"""
              {
                "resources": [
                  {
                    "@id": "not-a-url",
                    "@type": "PackageBaseAddress/3.0.0{{Escape}}[31m"
                  }
                ]
              }
              """;
        using var client = new HttpClient(
            new StaticJsonHandler(sourceUrl, json));
        List<string> logs = [];

        await DotnetInspector.Packages.PackageExtractor
            .GetServiceIndexResourcesAsync(
            client,
            source,
            logs.Add,
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(logs);
        Assert.All(
            logs,
            line =>
            {
                Assert.DoesNotContain(Secret, line, StringComparison.Ordinal);
                Assert.DoesNotContain(Escape, line);
            });
    }

    sealed class StaticJsonHandler(string expectedUrl, string json)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(expectedUrl, request.RequestUri?.ToString());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"),
                });
        }
    }
}
