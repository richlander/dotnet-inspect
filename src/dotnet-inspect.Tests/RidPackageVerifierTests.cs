using System.Net;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class RidPackageVerifierTests
{
    public RidPackageVerifierTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
    }

    [Fact]
    public async Task VerifyAsync_UsesConfiguredV3Source()
    {
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        handler.Add(
            "content.example.test/flat/testpackage.linux-x64/1.0.0/testpackage.linux-x64.nuspec",
            """<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>TestPackage.linux-x64</id><version>1.0.0</version></metadata></package>""");
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

    [Fact]
    public async Task VerifyAsync_UnmappedRidPackagePropagatesMappingFailure()
    {
        string configPath = Path.Combine(
            Path.GetTempPath(),
            $"rid-mapping-{Guid.NewGuid():N}.config");
        File.WriteAllText(configPath, """
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="https://private.example/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="Parent.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = "runtime.linux-x64.Unmapped"
                }
            ]
        };

        try
        {
            PackageSourceMappingException exception =
                await Assert.ThrowsAsync<PackageSourceMappingException>(
                    () => RidPackageVerifier.VerifyAsync(
                        new HttpClient(),
                        result,
                        "1.0.0",
                        localDir: null,
                        logger: new VerboseLogger(enabled: false),
                        sourceOptions: new NuGetSourceOptions { ConfigFile = configPath }));

            Assert.Equal(PackageSourceMappingFailure.NoPattern, exception.Failure);
            Assert.Null(Assert.Single(result.RuntimeIdentifierPackages).Exists);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task VerifyAsync_NotFoundSetsAvailabilityFalse()
    {
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        using var client = new HttpClient(handler);
        var result = CreateResult();

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://feed.example.test/v3/index.json"]
            });

        Assert.False(Assert.Single(result.RuntimeIdentifierPackages!).Exists);
    }

    [Fact]
    public async Task VerifyAsync_OfflineLeavesAvailabilityUnknown()
    {
        using var client = new HttpClient(new OfflineHandler());
        var result = CreateResult();

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://api.nuget.org/v3/index.json"]
            });

        Assert.Null(Assert.Single(result.RuntimeIdentifierPackages!).Exists);
    }

    [Theory]
    [InlineData("<html>sign in</html>")]
    [InlineData("<not-package><metadata><id>TestPackage.linux-x64</id><version>1.0.0</version></metadata></not-package>")]
    [InlineData("<package xmlns=\"urn:a\"><metadata xmlns=\"urn:b\"><id>TestPackage.linux-x64</id><version>1.0.0</version></metadata></package>")]
    [InlineData("<package><metadata><id>Other.Package</id><version>1.0.0</version></metadata></package>")]
    [InlineData("<package><metadata><id>TestPackage.linux-x64</id><version>2.0.0</version></metadata></package>")]
    public async Task VerifyAsync_InvalidNuspecLeavesAvailabilityUnknown(
        string nuspec)
    {
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        handler.Add(
            "content.example.test/flat/testpackage.linux-x64/1.0.0/testpackage.linux-x64.nuspec",
            nuspec);
        using var client = new HttpClient(handler);
        var result = CreateResult();

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://feed.example.test/v3/index.json"]
            });

        Assert.Null(Assert.Single(result.RuntimeIdentifierPackages!).Exists);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Weird:Id")]
    [InlineData("../escape")]
    public async Task VerifyAsync_InvalidPackageIdLeavesAvailabilityUnknown(
        string packageId)
    {
        var handler = new StubHandler();
        using var client = new HttpClient(handler);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = packageId,
                }
            ]
        };

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://feed.example.test/v3/index.json"]
            });
        Assert.Null(Assert.Single(result.RuntimeIdentifierPackages).Exists);
        Assert.Empty(handler.Requests);

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: Path.GetTempPath(),
            logger: new VerboseLogger(enabled: false));
        Assert.Null(Assert.Single(result.RuntimeIdentifierPackages).Exists);
    }

    [Fact]
    public async Task VerifyAsync_BomPrefixedNuspecSetsAvailabilityTrue()
    {
        const string nuspec =
            "\uFEFF<package><metadata><id>TestPackage.linux-x64</id>"
            + "<version>1.0.0</version></metadata></package>";
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        handler.Add(
            "content.example.test/flat/testpackage.linux-x64/1.0.0/testpackage.linux-x64.nuspec",
            nuspec);
        using var client = new HttpClient(handler);
        var result = CreateResult();

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://feed.example.test/v3/index.json"]
            });

        Assert.True(Assert.Single(result.RuntimeIdentifierPackages!).Exists);
        Assert.StartsWith(
            "\uFEFF",
            await PackageExtractor.TryGetNuspecXmlAsync(
                client,
                "TestPackage.linux-x64",
                "1.0.0",
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = ["https://feed.example.test/v3/index.json"]
                }));
    }

    private static InspectionResult CreateResult() => new()
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

    sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new OfflineException("Network access is disabled for this test.");
    }
}
