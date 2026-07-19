using System.Net;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

public class RidPackageVerifierTests
{
    [Fact]
    public async Task VerifyAsync_UsesConfiguredV3Source()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        handler.Add(
            "content.example.test/flat/testpackage.linux-x64/1.0.0/testpackage.linux-x64.nuspec",
            """<?xml version="1.0"?><package><metadata><id>TestPackage.linux-x64</id><version>1.0.0</version></metadata></package>""");
        using var client = new HttpClient(handler);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = "TestPackage.linux-x64"
                }
            ]
        };

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions { Sources = ["https://feed.example.test/v3/index.json"] });

        Assert.True(
            Assert.Single(result.RuntimeIdentifierPackages).Exists,
            string.Join(Environment.NewLine, handler.Requests));
        Assert.DoesNotContain(handler.Requests, request =>
            request.Host.Equals("api.nuget.org", StringComparison.OrdinalIgnoreCase));
    }

    sealed class StubHandler : HttpMessageHandler
    {
        readonly List<(string Match, string Body)> _routes = [];

        public List<Uri> Requests { get; } = [];

        public void Add(string match, string body) => _routes.Add((match, body));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var route = _routes.FirstOrDefault(candidate =>
                request.RequestUri!.ToString().Contains(candidate.Match, StringComparison.Ordinal));
            return Task.FromResult(route == default
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(route.Body, Encoding.UTF8, "application/xml")
                });
        }
    }
}
