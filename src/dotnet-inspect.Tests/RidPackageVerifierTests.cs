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
    [Fact]
    public async Task VerifyAsync_UsesConfiguredV3Source()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
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

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("2.0.0", false)]
    public async Task VerifyAsync_FallsBackToAuthoritativeVersionIndex(
        string version,
        bool expected)
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string packageId =
            $"FallbackPackage.{Guid.NewGuid():N}.linux-x64";
        string normalizedPackageId = packageId.ToLowerInvariant();
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        handler.Add(
            $"content.example.test/flat/{normalizedPackageId}/index.json",
            """{"versions":["1.0.0"]}""");
        using var client = new HttpClient(handler);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = packageId,
                },
            ],
        };

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            version,
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://feed.example.test/v3/index.json"],
            });

        Assert.Equal(
            expected,
            Assert.Single(result.RuntimeIdentifierPackages).Exists);
        Assert.Contains(
            handler.Requests,
            request => request.AbsolutePath.EndsWith(
                $"/{normalizedPackageId}/index.json",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_MissingVersionIndexIsAbsent()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string packageId =
            $"MissingPackage.{Guid.NewGuid():N}.linux-x64";
        string normalizedPackageId = packageId.ToLowerInvariant();
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        using var client = new HttpClient(handler);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = packageId,
                },
            ],
        };

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://feed.example.test/v3/index.json"],
            });

        Assert.False(
            Assert.Single(result.RuntimeIdentifierPackages).Exists);
        Assert.Contains(
            handler.Requests,
            request => request.AbsolutePath.EndsWith(
                $"/{normalizedPackageId}/index.json",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyAsync_MissingHttpServiceIndexIsUnknown()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        var handler = new StubHandler();
        using var client = new HttpClient(handler);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId =
                        $"UnavailableFeed.{Guid.NewGuid():N}.linux-x64",
                },
            ],
        };

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources =
                [
                    "https://missing-feed.example/v3/index.json",
                ],
            });

        Assert.Null(
            Assert.Single(result.RuntimeIdentifierPackages).Exists);
        Assert.Contains(
            handler.Requests,
            request => request.Host == "missing-feed.example");
    }

    [Fact]
    public async Task VerifyAsync_VersionIndexFailureIsUnknown()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string packageId =
            $"BrokenPackage.{Guid.NewGuid():N}.linux-x64";
        string normalizedPackageId = packageId.ToLowerInvariant();
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        handler.Add(
            $"content.example.test/flat/{normalizedPackageId}/index.json",
            """{"notVersions":[]}""");
        using var client = new HttpClient(handler);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = packageId,
                },
            ],
        };

        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            logger: new VerboseLogger(enabled: false),
            sourceOptions: new NuGetSourceOptions
            {
                Sources = ["https://feed.example.test/v3/index.json"],
            });

        Assert.Null(
            Assert.Single(result.RuntimeIdentifierPackages).Exists);
    }

    [Fact]
    public async Task VerifyAsync_OfflineVersionCheckIsUnknown()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string packageId =
            $"OfflinePackage.{Guid.NewGuid():N}.linux-x64";
        var handler = new StubHandler();
        handler.Add(
            "feed.example.test/v3/index.json",
            """{"version":"3.0.0","resources":[{"@type":"PackageBaseAddress/3.0.0","@id":"https://content.example.test/flat/"}]}""");
        using var client = new HttpClient(handler);
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = packageId,
                },
            ],
        };

        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions { Offline = true });
        try
        {
            await RidPackageVerifier.VerifyAsync(
                client,
                result,
                "1.0.0",
                localDir: null,
                logger: new VerboseLogger(enabled: false),
                sourceOptions: new NuGetSourceOptions
                {
                    Sources =
                    [
                        "https://feed.example.test/v3/index.json",
                    ],
                });

            Assert.Null(
                Assert.Single(result.RuntimeIdentifierPackages).Exists);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory.Initialize(
                new HttpClientFactoryOptions());
        }
    }

    [Fact]
    public async Task VerifyAsync_NonHttpSourceDoesNotMakeAbsenceUnknown()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string localSource = Directory.CreateTempSubdirectory(
            "dotnet-inspect-rid-source-").FullName;
        string packageId =
            $"LocalPackage.{Guid.NewGuid():N}.linux-x64";
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = packageId,
                },
            ],
        };

        try
        {
            await RidPackageVerifier.VerifyAsync(
                new HttpClient(),
                result,
                "1.0.0",
                localDir: null,
                logger: new VerboseLogger(enabled: false),
                sourceOptions: new NuGetSourceOptions
                {
                    Sources = [localSource],
                });

            Assert.False(
                Assert.Single(result.RuntimeIdentifierPackages).Exists);
        }
        finally
        {
            Directory.Delete(localSource);
        }
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
