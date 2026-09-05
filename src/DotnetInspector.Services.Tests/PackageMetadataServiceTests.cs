using System.Text.Json;
using System.Net.Http.Headers;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

[Collection(CoreCacheCollection.Name)]
public class PackageMetadataServiceTests : IDisposable
{
    public PackageMetadataServiceTests()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        CoreCache.Clear("metadata");
    }

    public void Dispose()
    {
        CoreCache.Clear("metadata");
    }

    [Fact]
    public void ParseDeprecation_WithAllFields()
    {
        var json = """
        {
            "reasons": ["Legacy", "CriticalBugs"],
            "message": "Use the new package instead",
            "alternatePackage": { "id": "NewPackage" }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var deprecation = PackageMetadataService.ParseDeprecation(doc.RootElement);

        Assert.Equal(2, deprecation.Reasons!.Count);
        Assert.Contains("Legacy", deprecation.Reasons);
        Assert.Contains("CriticalBugs", deprecation.Reasons);
        Assert.Equal("Use the new package instead", deprecation.Message);
        Assert.Equal("NewPackage", deprecation.AlternatePackageId);
    }

    [Fact]
    public void ParseDeprecation_ReasonsOnly()
    {
        var json = """{ "reasons": ["Other"] }""";

        using var doc = JsonDocument.Parse(json);
        var deprecation = PackageMetadataService.ParseDeprecation(doc.RootElement);

        Assert.Single(deprecation.Reasons!);
        Assert.Equal("Other", deprecation.Reasons![0]);
        Assert.Null(deprecation.Message);
        Assert.Null(deprecation.AlternatePackageId);
    }

    [Fact]
    public void ParseDeprecation_EmptyObject()
    {
        var json = """{}""";

        using var doc = JsonDocument.Parse(json);
        var deprecation = PackageMetadataService.ParseDeprecation(doc.RootElement);

        Assert.Null(deprecation.Reasons);
        Assert.Null(deprecation.Message);
        Assert.Equal("Deprecated", deprecation.Summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_WithReasons()
    {
        var dep = new PackageDeprecation
        {
            Reasons = ["Legacy"],
            AlternatePackageId = "NewPkg"
        };
        Assert.Equal("Legacy - use NewPkg", dep.Summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_WithMessage()
    {
        var dep = new PackageDeprecation
        {
            Reasons = ["CriticalBugs"],
            Message = "Security issue"
        };
        Assert.Equal("CriticalBugs - Security issue", dep.Summary);
    }

    [Fact]
    public void PackageDeprecation_Summary_Empty()
    {
        var dep = new PackageDeprecation();
        Assert.Equal("Deprecated", dep.Summary);
    }

    [Theory]
    [InlineData("1.0.0", "[1.0.0, 2.0.0)", true)]
    [InlineData("2.0.0", "[1.0.0, 2.0.0)", false)]
    [InlineData("1.5.0", "[1.0.0, 2.0.0)", true)]
    [InlineData("0.9.0", "[1.0.0, 2.0.0)", false)]
    [InlineData("1.0.0", "1.0.0", true)]
    public void IsVersionInRange_VariousRanges(string version, string range, bool expected)
    {
        var nugetVersion = NuGet.Versioning.NuGetVersion.Parse(version);
        Assert.Equal(expected, PackageMetadataService.IsVersionInRange(nugetVersion, range));
    }

    [Theory]
    [InlineData(0, "Low")]
    [InlineData(1, "Moderate")]
    [InlineData(2, "High")]
    [InlineData(3, "Critical")]
    [InlineData(99, "Unknown")]
    public void SeverityToString_MapsCorrectly(int severity, string expected)
    {
        Assert.Equal(expected, PackageMetadataService.SeverityToString(severity));
    }

    [Fact]
    public void PackageMetadata_DefaultValues()
    {
        var metadata = new PackageMetadata();

        Assert.Null(metadata.Published);
        Assert.Null(metadata.TotalDownloads);
        Assert.Null(metadata.VersionDownloads);
        Assert.Null(metadata.VersionCount);
        Assert.Null(metadata.PackageSize);
        Assert.Null(metadata.IsVerified);
        Assert.Null(metadata.Listed);
        Assert.Null(metadata.Owners);
        Assert.Null(metadata.Deprecation);
        Assert.Null(metadata.Vulnerabilities);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_UsesConfiguredServiceIndexResources()
    {
        const string source = "https://private.example/v3/index.json";
        using var config = new TempNuGetConfig(
            [("private", source)],
            credentialedSource: "private");
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v3/index.json" => Json("""
                {
                  "version": "3.0.0",
                  "resources": [
                    { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                    { "@id": "https://private.example/query?s%69g=%73ecret&semVerLevel=1.0.0", "@type": "SearchQueryService/3.5.0" },
                    { "@id": "https://private.example/flat/", "@type": "PackageBaseAddress/3.0.0" },
                    { "@id": "https://private.example/vulnerabilities/index.json", "@type": "VulnerabilityInfo/6.7.0" }
                  ]
                }
                """),
            "/registration/private.package/index.json" => SingleVersionRegistration(
                "Private.Package",
                "1.0.0",
                """
                "published": "2024-01-02T03:04:05Z",
                "listed": false,
                "deprecation": {
                  "reasons": ["Legacy"],
                  "message": "Use Private.Package.Next"
                }
                """),
            "/query" => Json("""
                {
                  "data": [
                    { "id": "Not.The.Package", "totalDownloads": 999 },
                    {
                      "id": "Private.Package",
                      "totalDownloads": "42",
                      "verified": true,
                      "owners": "private-owner, second-owner",
                      "versions": [
                        { "version": "1.0.0", "downloads": "7" },
                        { "version": "2.0.0", "downloads": 3 }
                      ]
                    }
                  ]
                }
                """),
            "/vulnerabilities/index.json" => Json(
                """[{ "@id": "/vulnerabilities/page.json" }]"""),
            "/vulnerabilities/page.json" => Json("""
                {
                  "private.package": [
                    {
                      "url": "https://advisories.example/CVE-2024-1",
                      "severity": 2,
                      "versions": "[1.0.0, 2.0.0)"
                    }
                  ]
                }
                """),
            "/flat/private.package/1.0.0/private.package.1.0.0.nupkg"
                when request.Method == HttpMethod.Get => Package(length: 1234),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
        });
        var log = new List<string>();

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log.Add,
            sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Equal(
            new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            result.Published);
        Assert.Equal(42, result.TotalDownloads);
        Assert.Equal(7, result.VersionDownloads);
        Assert.Equal(2, result.VersionCount);
        Assert.Equal(1234, result.PackageSize);
        Assert.True(result.IsVerified);
        Assert.False(result.Listed);
        Assert.Equal(["private-owner", "second-owner"], result.Owners);
        Assert.Equal("Legacy - Use Private.Package.Next", result.Deprecation!.Summary);
        Assert.Equal("High", Assert.Single(result.Vulnerabilities!).Severity);
        Assert.Equal(
            "/registration/private.package/index.json",
            Assert.Single(
                handler.Requests,
                request => request.Uri.AbsolutePath.StartsWith(
                    "/registration/",
                    StringComparison.Ordinal)).Uri.AbsolutePath);
        Assert.Equal(
            "?s%69g=%73ecret&q=private.package&skip=0&take=20&prerelease=true&semVerLevel=2.0.0",
            Assert.Single(
                handler.Requests,
                request => request.Uri.AbsolutePath == "/query").Uri.Query);
        Assert.Equal(
            "bytes=0-0",
            Assert.Single(
                handler.Requests,
                request => request.Uri.AbsolutePath.EndsWith(
                    ".nupkg",
                    StringComparison.Ordinal)).Range);
        Assert.DoesNotContain(
            log,
            message => message.Contains("secret", StringComparison.Ordinal));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("private.example", request.Uri.Host);
            Assert.NotNull(request.Authorization);
        });
    }

    [Fact]
    public async Task FetchAllMetadataAsync_SearchFailureRedactsDeclaredQuery()
    {
        const string source = "https://private.example/v3/index.json";
        const string packageId = "Signed.Endpoint.Failure";
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v3/index.json" => Json("""
                {
                  "version": "3.0.0",
                  "resources": [
                    { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                    { "@id": "https://private.example/query?s%69g=SUPERSECRETSIG", "@type": "SearchQueryService/3.5.0" }
                  ]
                }
                """),
            "/registration/signed.endpoint.failure/index.json" =>
                SingleVersionRegistration(
                    "Signed.Endpoint.Failure",
                    "1.0.0",
                    """
                    "published": "2024-01-02T03:04:05Z"
                    """),
            "/query" => Json("<html>SUPERSECRETEXCEPTION</html>"),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
        });
        var log = new List<string>();

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            packageId,
            "1.0.0",
            log.Add,
            forceLatest: true,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.NotNull(result.Published);
        Assert.Contains(
            log,
            message => message.Contains(
                "Error fetching search metadata",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            log,
            message => message.Contains("SUPERSECRET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_AuthoritativeAbsenceFallsBackInSourceOrder()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        var handler = new RoutingHandler(request =>
        {
            string host = request.RequestUri!.Host;
            string path = request.RequestUri.AbsolutePath;
            if (path == "/v3/index.json")
            {
                return Json($$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://{{host}}/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://{{host}}/flat/", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """);
            }

            if (host == "b.example"
                && path == "/registration/contoso.package/index.json")
            {
                return SingleVersionRegistration(
                    "Contoso.Package",
                    "1.0.0",
                    """
                    "published": "2025-02-03T00:00:00Z"
                    """);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [sourceA, sourceB] };
        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Contoso.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Contoso.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Equal(
            new DateTimeOffset(2025, 2, 3, 0, 0, 0, TimeSpan.Zero),
            result.Published);
        Assert.Equal(result.Published, warm.Published);
        Assert.Equal(coldRequests, handler.Requests.Count);
        Assert.Contains(handler.Requests, request => request.Uri.Host == "a.example");
        Assert.Contains(handler.Requests, request => request.Uri.Host == "b.example");
    }

    [Fact]
    public async Task FetchAllMetadataAsync_UnreadableHigherSourceDoesNotBorrowLowerMetadata()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.Host == "a.example"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
                : Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://b.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """));

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Contoso.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [sourceA, sourceB] });

        Assert.Null(result.Published);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Uri.Host == "b.example");
    }

    [Fact]
    public async Task FetchAllMetadataAsync_PagesUntilSearchReturnsTheExactPackage()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v3/index.json")
            {
                return Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """);
            }
            if (request.RequestUri.AbsolutePath
                == "/registration/private.package/index.json")
            {
                return SingleVersionRegistration("Private.Package", "1.0.0");
            }
            if (request.RequestUri.AbsolutePath == "/query"
                && request.RequestUri.Query.Contains("skip=0", StringComparison.Ordinal))
            {
                string inexactResults = string.Join(
                    ',',
                    Enumerable.Range(0, 10).Select(index =>
                        $$"""{ "id": "Private.Package.Similar{{index}}" }"""));
                return Json(
                    $$"""{ "totalHits": 11, "data": [{{inexactResults}}] }""");
            }
            if (request.RequestUri.AbsolutePath == "/query"
                && request.RequestUri.Query.Contains("skip=10", StringComparison.Ordinal))
            {
                return Json("""
                    {
                      "data": [
                        {
                          "id": "Private.Package",
                          "totalDownloads": 42,
                          "versions": [
                            { "version": "1.0.0", "downloads": 7 }
                          ]
                        }
                      ]
                    }
                    """);
            }

            throw new InvalidOperationException(
                $"Unexpected metadata request: {request.RequestUri}");
        });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(42, result.TotalDownloads);
        Assert.Equal(7, result.VersionDownloads);
        Assert.Equal(
            2,
            handler.Requests.Count(request =>
                request.Uri.AbsolutePath == "/query"));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_SearchDeprecationMustMatchRequestedVersion()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/query" => Json("""
                    {
                      "data": [
                        {
                          "id": "Private.Package",
                          "version": "2.0.0",
                          "deprecation": {
                            "reasons": ["Legacy"],
                            "alternatePackage": {
                              "id": "\u0405ystem.LatestOnly"
                            }
                          },
                          "versions": [
                            { "version": "1.0.0", "downloads": 1 },
                            { "version": "2.0.0", "downloads": 2 }
                          ]
                        }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result =
            await PackageMetadataService.FetchAllMetadataAsync(
                new HttpClient(handler),
                "Private.Package",
                "1.0.0",
                log: null,
                forceLatest: true,
                sourceOptions:
                    new NuGetSourceOptions { Sources = [source] });

        Assert.True(result.DeprecationMetadataAvailable);
        Assert.Null(result.Deprecation);
        Assert.Equal(1, result.VersionDownloads);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_RegistrationDeprecationSurvivesNewerCleanSearchVersion()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(
                        RegistrationPage(
                            "1.0.0",
                            "2.0.0",
                            RegistrationLeaf(
                                "Private.Package",
                                "1.0.0",
                                """
                                "deprecation": {
                                  "reasons": ["Legacy"],
                                  "message": "Move to 2.0.0"
                                }
                                """),
                            RegistrationLeaf("Private.Package", "2.0.0")))),
                "/query" => Json("""
                    {
                      "data": [
                        {
                          "id": "Private.Package",
                          "version": "2.0.0",
                          "versions": [
                            { "version": "1.0.0", "downloads": 11 },
                            { "version": "2.0.0", "downloads": 22 }
                          ]
                        }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.Equal("Legacy - Move to 2.0.0", cold.Deprecation!.Summary);
        Assert.Equal("Legacy - Move to 2.0.0", warm.Deprecation!.Summary);
        Assert.True(cold.DeprecationMetadataAvailable);
        Assert.Equal(11, cold.VersionDownloads);
        Assert.Equal(coldRequests, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotCacheMismatchedSearchVersion()
    {
        const string source = "https://private.example/v3/index.json";
        int searchRequests = 0;
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/flat/", "@type": "PackageBaseAddress/3.0.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/flat/private.package/1.0.0/private.package.1.0.0.nupkg" =>
                    Package(length: 42),
                "/query" => Search(),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.False(first.DeprecationMetadataAvailable);
        Assert.Null(first.Deprecation);
        Assert.True(second.DeprecationMetadataAvailable);
        Assert.Equal(
            "\u0405ystem.Fixed",
            second.Deprecation!.AlternatePackageId);
        Assert.Equal(2, searchRequests);

        HttpResponseMessage Search()
        {
            searchRequests++;
            string version =
                searchRequests == 1 ? "2.0.0" : "1.0.0";
            return Json($$"""
                {
                  "data": [
                    {
                      "id": "Private.Package",
                      "version": "{{version}}",
                      "deprecation": {
                        "alternatePackage": {
                          "id": "\u0405ystem.Fixed"
                        }
                      }
                    }
                  ]
                }
                """);
        }
    }

    [Fact]
    public async Task FetchAllMetadataAsync_CachesMatchingSearchVersionWithoutDeprecation()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/flat/", "@type": "PackageBaseAddress/3.0.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/flat/private.package/1.0.0/private.package.1.0.0.nupkg" =>
                    Package(length: 42),
                "/query" => Json("""
                    {
                      "data": [
                        {
                          "id": "Private.Package",
                          "version": "1.0.0"
                        }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.True(cold.DeprecationMetadataAvailable);
        Assert.True(warm.DeprecationMetadataAvailable);
        Assert.Null(cold.Deprecation);
        Assert.Null(warm.Deprecation);
        Assert.Equal(coldRequests, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_CachesRegistrationAuthorityDespiteSearchVersionMismatch()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/query" => Json("""
                    {
                      "data": [
                        {
                          "id": "Private.Package",
                          "version": "2.0.0"
                        }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.True(cold.DeprecationMetadataAvailable);
        Assert.True(warm.DeprecationMetadataAvailable);
        Assert.Equal(coldRequests, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotAdoptOrCacheMismatchedPageIdentity()
    {
        const string source = "https://private.example/v3/index.json";
        int registrationRequests = 0;
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    Registration(),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.False(first.DeprecationMetadataAvailable);
        Assert.Null(first.Deprecation);
        Assert.True(second.DeprecationMetadataAvailable);
        Assert.Equal(
            "\u0405ystem.Fixed",
            second.Deprecation!.AlternatePackageId);
        Assert.Equal(2, registrationRequests);

        HttpResponseMessage Registration()
        {
            registrationRequests++;
            string id = registrationRequests == 1
                ? "Different.Package"
                : "Private.Package";
            return SingleVersionRegistration(
                id,
                "1.0.0",
                """
                "deprecation": {
                  "alternatePackage": { "id": "\u0405ystem.Fixed" }
                }
                """);
        }
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotAdoptOrCacheMismatchedLinkedPageIdentity()
    {
        const string source = "https://private.example/v3/index.json";
        int pageRequests = 0;
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(LinkedRegistrationPage(
                        "../pages/private.package-1.json",
                        "1.0.0",
                        "1.0.0"))),
                "/registration/pages/private.package-1.json" => Page(),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.False(first.DeprecationMetadataAvailable);
        Assert.Null(first.Deprecation);
        Assert.True(second.DeprecationMetadataAvailable);
        Assert.Equal(
            "\u0405ystem.Fixed",
            second.Deprecation!.AlternatePackageId);
        Assert.Equal(2, pageRequests);

        HttpResponseMessage Page()
        {
            pageRequests++;
            string id = pageRequests == 1
                ? "Different.Package"
                : "Private.Package";
            return Json(RegistrationPageDocument(RegistrationLeaf(
                id,
                "1.0.0",
                """
                "deprecation": {
                  "alternatePackage": { "id": "\u0405ystem.Fixed" }
                }
                """)));
        }
    }

    [Fact]
    public async Task FetchAllMetadataAsync_TriesEquivalentSearchEndpointsInOrder()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/query-a", "@type": "SearchQueryService/3.5.0" },
                        { "@id": "https://private.example/query-a", "@type": "SearchQueryService/3.5.0" },
                        { "@id": "https://private.example/query-b", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/query-a" => Json("{"),
                "/query-b" => Json("""
                    {
                      "data": [
                        { "id": "Private.Package", "totalDownloads": 42 }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(42, result.TotalDownloads);
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/query-a");
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/query-b");
        Assert.Equal(
            1,
            handler.Requests.Count(
                request => request.Uri.AbsolutePath == "/query-a"));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_EquivalentSearchFailoverIsBounded()
    {
        const string source = "https://private.example/v3/index.json";
        string resources = string.Join(
            ",",
            Enumerable.Range(0, 6).Select(index =>
                $$"""{"@id":"https://private.example/query-{{index}}","@type":"SearchQueryService/3.5.0"}"""));
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json(
                    $$"""{"resources":[{"@id":"https://private.example/registration/","@type":"RegistrationsBaseUrl/3.6.0"},{{resources}}]}"""),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                _ when request.RequestUri.AbsolutePath.StartsWith(
                    "/query-",
                    StringComparison.Ordinal) => Json("{"),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        _ = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            forceLatest: true,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(
            ["/query-0", "/query-1", "/query-2", "/query-3"],
            handler.Requests
                .Where(request => request.Uri.AbsolutePath.StartsWith(
                    "/query-",
                    StringComparison.Ordinal))
                .Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_MalformedRegistrationUsesEquivalentEndpoint()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration-a/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/registration-b/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration-a/private.package/index.json" => Json("{"),
                "/registration-b/private.package/index.json" =>
                    SingleVersionRegistration(
                        "Private.Package",
                        "1.0.0",
                        """
                        "published": "2025-01-02T00:00:00Z"
                        """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(2025, result.Published!.Value.Year);
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath.StartsWith(
                "/registration-b/",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_TriesEquivalentVulnerabilityEndpoints()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/vulnerabilities-a.json", "@type": "VulnerabilityInfo/6.7.0" },
                        { "@id": "https://private.example/vulnerabilities-b.json", "@type": "VulnerabilityInfo/6.7.0" },
                        { "@id": "https://private.example/vulnerabilities-c.json", "@type": "VulnerabilityInfo/6.7.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/vulnerabilities-a.json" =>
                    Json("""[{ "@id": "/malformed-vulnerability-page.json" }]"""),
                "/malformed-vulnerability-page.json" => Json("""
                    {
                      "private.package": [
                        {
                          "url": "https://advisories.example/CVE-2025-1",
                          "severity": "high",
                          "versions": "[1.0.0]"
                        }
                      ]
                    }
                    """),
                "/vulnerabilities-b.json" =>
                    Json("""[{ "@id": "/vulnerability-page.json" }]"""),
                "/vulnerabilities-c.json" =>
                    Json("""[{ "@id": "/vulnerability-page.json" }]"""),
                "/vulnerability-page.json" => Json("""
                    {
                      "private.package": [
                        {
                          "url": "https://advisories.example/CVE-2025-1",
                          "severity": 2,
                          "versions": "[1.0.0]"
                        }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(
            "High",
            Assert.Single(result.Vulnerabilities!).Severity);
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/vulnerabilities-b.json");
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/vulnerabilities-c.json");
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotCacheFailedVulnerabilityFetch()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/vulnerabilities.json", "@type": "VulnerabilityInfo/6.7.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/vulnerabilities.json" => new HttpResponseMessage(
                    System.Net.HttpStatusCode.BadRequest),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        _ = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int firstRequestCount = handler.Requests.Count;
        _ = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.True(handler.Requests.Count > firstRequestCount);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotCacheFailedRegistrationPageFetch()
    {
        const string source = "https://private.example/v3/index.json";
        int indexRequests = 0;
        int pageRequests = 0;
        HttpResponseMessage Index()
        {
            indexRequests++;
            return Json(RegistrationIndex(LinkedRegistrationPage(
                "/registration/pages/private.package.json",
                "1.0.0",
                "1.0.0")));
        }
        HttpResponseMessage Page()
        {
            pageRequests++;
            return indexRequests == 1
                ? new HttpResponseMessage(
                    System.Net.HttpStatusCode.BadGateway)
                : Json(RegistrationPageDocument(RegistrationLeaf(
                    "Private.Package",
                    "1.0.0",
                    """
                    "deprecation": {
                      "reasons": ["Legacy"],
                      "message": "Use a replacement."
                    }
                    """)));
        }
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Index(),
                "/registration/pages/private.package.json" => Page(),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.Null(first.Deprecation);
        Assert.False(first.DeprecationMetadataAvailable);
        Assert.Equal("Use a replacement.", second.Deprecation!.Message);
        Assert.True(second.DeprecationMetadataAvailable);
        Assert.True(pageRequests > 1);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotCacheIndeterminateRegistration()
    {
        const string source = "https://private.example/v3/index.json";
        int registrationRequests = 0;
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/flat/", "@type": "PackageBaseAddress/3.0.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    RegistrationFailure(),
                "/flat/private.package/1.0.0/private.package.1.0.0.nupkg" =>
                    Package(length: 42),
                "/query" => Json("""{ "data": [] }"""),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        int firstRegistrationRequests = registrationRequests;
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.False(first.DeprecationMetadataAvailable);
        Assert.False(second.DeprecationMetadataAvailable);
        Assert.True(registrationRequests > firstRegistrationRequests);

        HttpResponseMessage RegistrationFailure()
        {
            registrationRequests++;
            return new HttpResponseMessage(
                System.Net.HttpStatusCode.BadGateway);
        }
    }

    [Fact]
    public async Task FetchAllMetadataAsync_FlatContainerOnlyCompletesOptionalMetadata()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/flat/", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """),
                "/flat/private.package/1.0.0/private.package.1.0.0.nupkg" =>
                    Package(length: 42),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);

        PackageMetadata cold =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions:
                    new NuGetSourceOptions { Sources = [source] });
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions:
                    new NuGetSourceOptions { Sources = [source] });

        Assert.True(cold.DeprecationMetadataAvailable);
        Assert.True(warm.DeprecationMetadataAvailable);
        Assert.False(cold.DeprecationMetadataSupported);
        Assert.False(warm.DeprecationMetadataSupported);
        Assert.Null(cold.Deprecation);
        Assert.Equal(coldRequests, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotCacheFailedSearchFetch()
    {
        const string source = "https://private.example/v3/index.json";
        int registrationRequests = 0;
        int searchRequests = 0;
        HttpResponseMessage Registration()
        {
            registrationRequests++;
            return SingleVersionRegistration("Private.Package", "1.0.0");
        }
        HttpResponseMessage Search()
        {
            searchRequests++;
            return registrationRequests == 1
                ? new HttpResponseMessage(
                    System.Net.HttpStatusCode.BadGateway)
                : Json("""
                    {
                      "data": [
                        {
                          "id": "Private.Package",
                          "totalDownloads": 42
                        }
                      ]
                    }
                    """);
        }
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/query", "@type": "SearchQueryService/3.5.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    Registration(),
                "/query" => Search(),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.Null(first.TotalDownloads);
        Assert.Equal(42, second.TotalDownloads);
        Assert.True(searchRequests > 1);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotCacheMalformedVulnerabilityIndexAsClean()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/vulnerabilities.json", "@type": "VulnerabilityInfo/6.7.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/vulnerabilities.json" => Json("""[{ "@name": "base" }]"""),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        int firstRequestCount = handler.Requests.Count;
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.Null(first.Vulnerabilities);
        Assert.Null(second.Vulnerabilities);
        Assert.True(handler.Requests.Count > firstRequestCount);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_PreservesPartialVulnerabilitiesFromMalformedPage()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/vulnerabilities.json", "@type": "VulnerabilityInfo/6.7.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/vulnerabilities.json" => Json("""
                    [
                      { "@id": "/vulnerability-page.json" },
                      { "@id": "/malformed-page.json" }
                    ]
                    """),
                "/vulnerability-page.json" => Json("""
                    {
                      "private.package": [
                        {
                          "url": "https://advisories.example/CVE-2025-1",
                          "severity": 2,
                          "versions": "[1.0.0]"
                        }
                      ]
                    }
                    """),
                "/malformed-page.json" => Json("{"),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        int firstRequestCount = handler.Requests.Count;
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.Equal(
            "High",
            Assert.Single(first.Vulnerabilities!).Severity);
        Assert.Equal(
            "High",
            Assert.Single(second.Vulnerabilities!).Severity);
        Assert.True(handler.Requests.Count > firstRequestCount);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_PreservesPartialVulnerabilitiesWithoutCaching()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/vulnerabilities-a.json", "@type": "VulnerabilityInfo/6.7.0" },
                        { "@id": "https://private.example/vulnerabilities-b.json", "@type": "VulnerabilityInfo/6.7.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/vulnerabilities-a.json" or "/vulnerabilities-b.json" => Json("""
                    [
                      { "@id": "/vulnerability-page.json" },
                      { "@id": "/failed-page.json" }
                    ]
                    """),
                "/vulnerability-page.json" => Json("""
                    {
                      "private.package": [
                        {
                          "url": "https://advisories.example/CVE-2025-1",
                          "severity": 2,
                          "versions": "[1.0.0]"
                        }
                      ]
                    }
                    """),
                "/failed-page.json" => new HttpResponseMessage(
                    System.Net.HttpStatusCode.BadRequest),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);
        int firstRequestCount = handler.Requests.Count;
        PackageMetadata second =
            await PackageMetadataService.FetchAllMetadataAsync(
                client,
                "Private.Package",
                "1.0.0",
                log: null,
                sourceOptions: options);

        Assert.Equal(
            "High",
            Assert.Single(first.Vulnerabilities!).Severity);
        Assert.Equal(
            "High",
            Assert.Single(second.Vulnerabilities!).Severity);
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/vulnerabilities-a.json");
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath == "/vulnerabilities-b.json");
        Assert.True(handler.Requests.Count > firstRequestCount);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_HtmlPackageProbeIsIndeterminate()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/flat/", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json("{"),
                "/flat/private.package/1.0.0/private.package.1.0.0.nupkg" =>
                    Html("<html>Sign in</html>"),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Null(result.PackageSize);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotUsePartialBodyLengthAsPackageSize()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://private.example/flat/", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration("Private.Package", "1.0.0"),
                "/flat/private.package/1.0.0/private.package.1.0.0.nupkg" =>
                    Package(length: null),
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Null(result.PackageSize);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_CacheIsScopedByProducer()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        var handler = new RoutingHandler(request =>
        {
            string host = request.RequestUri!.Host;
            return request.RequestUri.AbsolutePath == "/v3/index.json"
                ? Json($$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://{{host}}/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration(
                    "Same.Package",
                    "1.0.0",
                    host == "a.example"
                        ? """
                          "published": "2024-01-01T00:00:00Z"
                          """
                        : """
                          "published": "2025-01-01T00:00:00Z"
                          """);
        });
        using var client = new HttpClient(handler);

        PackageMetadata fromA = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Same.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [sourceA] });
        PackageMetadata fromB = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Same.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [sourceB] });
        int requestsAfterColdRuns = handler.Requests.Count;
        PackageMetadata warmA = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Same.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [sourceA] });

        Assert.Equal(2024, fromA.Published!.Value.Year);
        Assert.Equal(2025, fromB.Published!.Value.Year);
        Assert.Equal(2024, warmA.Published!.Value.Year);
        Assert.Equal(requestsAfterColdRuns, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_WarmLowerSourceDoesNotOverrideHigherSource()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        var handler = new RoutingHandler(request =>
        {
            string host = request.RequestUri!.Host;
            return request.RequestUri.AbsolutePath == "/v3/index.json"
                ? Json($$"""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://{{host}}/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration(
                    "Ordered.Package",
                    "1.0.0",
                    host == "a.example"
                        ? """
                          "published": "2024-01-01T00:00:00Z"
                          """
                        : """
                          "published": "2025-01-01T00:00:00Z"
                          """);
        });
        using var client = new HttpClient(handler);

        _ = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Ordered.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [sourceB] });
        int requestsAfterPrimingB = handler.Requests.Count;
        PackageMetadata ordered = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Ordered.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [sourceA, sourceB] });

        Assert.Equal(2024, ordered.Published!.Value.Year);
        Assert.True(handler.Requests.Count > requestsAfterPrimingB);
        Assert.Equal(
            "a.example",
            handler.Requests[requestsAfterPrimingB].Uri.Host);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_IgnoresLegacySourceBlindCacheEntry()
    {
        const string source = "https://private.example/v3/index.json";
        MetadataFieldCache.Set(
            "legacy.package@1.0.0",
            new PackageMetadata { Published = DateTimeOffset.UnixEpoch });
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration(
                    "Legacy.Package",
                    "1.0.0",
                    """
                    "published": "2024-01-01T00:00:00Z"
                    """));

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Legacy.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(2024, result.Published!.Value.Year);
        Assert.NotEmpty(handler.Requests);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_IgnoresLegacyGuessedLeafAbsenceEntry()
    {
        const string source = "https://private.example/v3/index.json";
        string legacyKey =
            $"v6-full-{NuGetCache.GetSourceKey(source)}-stale.package@1.0.0";
        MetadataFieldCache.SetAbsent(legacyKey);
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration(
                    "Stale.Package",
                    "1.0.0",
                    """
                    "published": "2024-01-01T00:00:00Z"
                    """));
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Stale.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Stale.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Equal(2024, cold.Published!.Value.Year);
        Assert.Equal(cold.Published, warm.Published);
        Assert.Contains(
            handler.Requests,
            request => request.Uri.AbsolutePath
                == "/registration/stale.package/index.json");
        Assert.Equal(coldRequests, handler.Requests.Count);
        Assert.True(
            Assert.IsType<MetadataFieldCache.Entry>(
                MetadataFieldCache.TryGetEntry(legacyKey)).IsAbsent);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_HonorsAcquisitionProducerRestriction()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        NuGetSourceOptions? options = NuGetSourceResolver.RestrictToSourceKeys(
            new NuGetSourceOptions { Sources = [sourceA, sourceB] },
            [NuGetCache.GetSourceKey(sourceB)]);
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://b.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration(
                    "Restricted.Package",
                    "1.0.0",
                    """
                    "published": "2025-01-01T00:00:00Z"
                    """));

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Restricted.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Equal(2025, result.Published!.Value.Year);
        Assert.All(
            handler.Requests,
            request => Assert.Equal("b.example", request.Uri.Host));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_MinimalRegistrationEntryIsCached()
    {
        const string source = "https://empty.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://empty.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration("Empty.Package", "1.0.0"));
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold =
            await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Empty.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm =
            await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Empty.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Equal(coldRequests, handler.Requests.Count);
        Assert.True(cold.DeprecationMetadataAvailable);
        Assert.True(warm.DeprecationMetadataAvailable);
        Assert.Null(cold.Published);
        Assert.Null(cold.Deprecation);
        Assert.Null(warm.Deprecation);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_CachedFeedTextCannotInjectAbsenceMarker()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration(
                    "Injected.Package",
                    "1.0.0",
                    """
                    "published": "2024-01-02T03:04:05Z",
                    "deprecation": {
                      "reasons": ["Legacy"],
                      "message": "text\nabsent: true"
                    }
                    """));
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Injected.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Injected.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Equal(cold.Published, warm.Published);
        Assert.Equal(coldRequests, handler.Requests.Count);
    }

    [Fact]
    public void MetadataCache_EncodesEveryFeedControlledField()
    {
        string key = $"injection-{Guid.NewGuid():N}";
        var metadata = new PackageMetadata
        {
            DeprecationMetadataAvailable = true,
            Listed = false,
            Owners = ["owner\nvulnerabilities:"],
            Deprecation = new PackageDeprecation
            {
                Reasons = ["Legacy\nowners:"],
                Message = "text\nvulnerabilities:",
                AlternatePackageId = "replacement\nabsent: true",
            },
            Vulnerabilities =
            [
                new PackageVulnerability
                {
                    Severity = "High|Critical",
                    CveId = "CVE-2025-1\nowners:",
                    GhsaId = "GHSA-test",
                    Summary = "summary\nabsent: true",
                    AdvisoryUrl = "https://advisory.example/a?x=1\nowners:",
                },
            ],
        };

        MetadataFieldCache.Set(key, metadata);
        MetadataFieldCache.Entry cached =
            Assert.IsType<MetadataFieldCache.Entry>(
                MetadataFieldCache.TryGetEntry(key));

        Assert.False(cached.IsAbsent);
        Assert.True(cached.Metadata.DeprecationMetadataAvailable);
        Assert.False(cached.Metadata.Listed);
        Assert.Equal(metadata.Owners, cached.Metadata.Owners);
        Assert.Equal(
            metadata.Deprecation.Message,
            cached.Metadata.Deprecation!.Message);
        Assert.Equal(
            metadata.Deprecation.Reasons,
            cached.Metadata.Deprecation.Reasons);
        Assert.Equal(
            metadata.Deprecation.AlternatePackageId,
            cached.Metadata.Deprecation.AlternatePackageId);
        PackageVulnerability vulnerability =
            Assert.Single(cached.Metadata.Vulnerabilities!);
        Assert.Equal("High|Critical", vulnerability.Severity);
        Assert.Equal("CVE-2025-1\nowners:", vulnerability.CveId);
        Assert.Equal("summary\nabsent: true", vulnerability.Summary);
        Assert.Equal(
            "https://advisory.example/a?x=1\nowners:",
            vulnerability.AdvisoryUrl);
    }

    [Fact]
    public void MetadataCache_PreservesCheckedCleanVulnerabilityState()
    {
        string key = $"checked-clean-{Guid.NewGuid():N}";
        var metadata = new PackageMetadata
        {
            Vulnerabilities = [],
        };

        MetadataFieldCache.Set(key, metadata);
        MetadataFieldCache.Entry cached =
            Assert.IsType<MetadataFieldCache.Entry>(
                MetadataFieldCache.TryGetEntry(key));

        Assert.False(cached.IsAbsent);
        Assert.Empty(Assert.IsType<List<PackageVulnerability>>(
            cached.Metadata.Vulnerabilities));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_WithholdsStaticCredentialFromCrossOriginRegistrationPage()
    {
        const string source = "https://private.example/v3/index.json";
        using var config = new TempNuGetConfig(
            [("private", source)],
            credentialedSource: "private");
        var sourceHandler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" =>
                    Json("""
                        {
                          "version": "3.0.0",
                          "resources": [
                            { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                          ]
                        }
                        """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(LinkedRegistrationPage(
                        "https://pages.example/private.package/page-1.json",
                        "1.0.0",
                        "1.0.0"))),
                _ => throw new InvalidOperationException(
                    "Cross-origin metadata used the configured-feed client."),
            });
        var untrustedHandler = new RoutingHandler(request =>
            request.RequestUri!.Host == "pages.example"
                ? Json(RegistrationPageDocument(RegistrationLeaf(
                    "Private.Package",
                    "1.0.0",
                    """
                    "published": "2024-01-01T00:00:00Z",
                    "deprecation": { "reasons": ["Legacy"] }
                    """)))
                : throw new InvalidOperationException(
                    "Unexpected untrusted metadata request."));
        using var client = new HttpClient(sourceHandler);
        using var untrustedClient = new HttpClient(untrustedHandler);

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path },
            untrustedClient: untrustedClient);

        Assert.Equal("Legacy", result.Deprecation!.Summary);
        Assert.Equal(
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            result.Published);
        Assert.All(
            sourceHandler.Requests,
            request => Assert.NotNull(request.Authorization));
        Assert.Null(Assert.Single(untrustedHandler.Requests).Authorization);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_PackageMappingSelectsTheMetadataProducer()
    {
        const string privateSource = "https://private.example/v3/index.json";
        const string publicSource = "https://public.example/v3/index.json";
        using var config = new TempNuGetConfig(
            [("private", privateSource), ("public", publicSource)],
            mappings: [("private", "Private.*"), ("public", "Public.*")]);
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : SingleVersionRegistration(
                    "Private.Package",
                    "1.0.0",
                    """
                    "published": "2024-01-01T00:00:00Z"
                    """));

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { ConfigFile = config.Path });

        Assert.Equal(2024, result.Published!.Value.Year);
        Assert.All(
            handler.Requests,
            request => Assert.Equal("private.example", request.Uri.Host));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_IgnoresMalformedAdvertisedResourceUrls()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath == "/v3/index.json"
                ? Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "[", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """)
                : throw new InvalidOperationException(
                    "A malformed advertised URL must not be requested."));

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Null(result.Published);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_ArrayValuedResourceTypeDoesNotInvalidateFeed()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "urn:example", "@type": ["Other/1.0.0", "Other"] }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration(
                        "Private.Package",
                        "1.0.0",
                        """
                        "published": "2024-01-02T03:04:05Z"
                        """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(2024, result.Published!.Value.Year);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_UsesOnlyBestRegistrationCapabilityVersion()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        var handler = new RoutingHandler(request =>
        {
            string host = request.RequestUri!.Host;
            return request.RequestUri.AbsolutePath switch
            {
                "/v3/index.json" when host == "a.example" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://a.example/registration-36/", "@type": "RegistrationsBaseUrl/3.6.0" },
                        { "@id": "https://a.example/registration-34/", "@type": "RegistrationsBaseUrl/3.4.0" }
                      ]
                    }
                    """),
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://b.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration-36/private.package/index.json" =>
                    new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
                "/registration-34/private.package/index.json" =>
                    new HttpResponseMessage(
                        System.Net.HttpStatusCode.InternalServerError),
                "/registration/private.package/index.json" =>
                    SingleVersionRegistration(
                        "Private.Package",
                        "1.0.0",
                        """
                        "published": "2025-01-02T00:00:00Z"
                        """),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            };
        });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [sourceA, sourceB] });

        Assert.Equal(2025, result.Published!.Value.Year);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Uri.AbsolutePath.StartsWith(
                "/registration-34/",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_IgnoresMalformedRegistrationPageLink()
    {
        const string source = "https://private.example/v3/index.json";
        int registrationRequests = 0;
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" =>
                    Registration(),
                _ => throw new InvalidOperationException(
                    "A malformed registration page link must not be requested."),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata first = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        PackageMetadata second = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Null(first.Published);
        Assert.Null(first.Deprecation);
        Assert.False(first.DeprecationMetadataAvailable);
        Assert.True(second.DeprecationMetadataAvailable);
        Assert.Equal(
            "\u0405ystem.Fixed",
            second.Deprecation!.AlternatePackageId);
        Assert.True(registrationRequests > 1);

        HttpResponseMessage Registration()
        {
            registrationRequests++;
            return registrationRequests == 1
                ? Json(RegistrationIndex(LinkedRegistrationPage(
                    "http://[::1",
                    "1.0.0",
                    "1.0.0")))
                : SingleVersionRegistration(
                    "Private.Package",
                    "1.0.0",
                    """
                    "published": "2024-01-02T03:04:05Z",
                    "deprecation": {
                      "reasons": ["Legacy"],
                      "alternatePackage": { "id": "\u0405ystem.Fixed" }
                    }
                    """);
        }
    }

    [Fact]
    public async Task FetchAllMetadataAsync_InlineRegistrationPageNeedsNoAdditionalRequest()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(
                        LinkedRegistrationPage(
                            "/registration/pages/older.json",
                            "0.1.0",
                            "0.9.0"),
                        InlineRegistrationPage(
                            "/registration/pages/current.json",
                            "1.0.0",
                            "2.0.0",
                            RegistrationLeaf(
                                "Private.Package",
                                "1.0.0",
                                """
                                "published": "2024-03-04T05:06:07Z",
                                "listed": true,
                                "deprecation": { "reasons": ["Legacy"] }
                                """),
                            RegistrationLeaf("Private.Package", "2.0.0")),
                        LinkedRegistrationPage(
                            "/registration/pages/newer.json",
                            "3.0.0",
                            "4.0.0"))),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(
            new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero),
            result.Published);
        Assert.True(result.Listed);
        Assert.Equal("Legacy", result.Deprecation!.Summary);
        Assert.Equal(
            ["/v3/index.json", "/registration/private.package/index.json"],
            handler.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_FollowsNonPatternRelativePageLink()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(LinkedRegistrationPage(
                        "../pages/p-42.json?token=abc",
                        "1.0.0",
                        "1.0.0"))),
                "/registration/pages/p-42.json" => Json(
                    RegistrationPageDocument(RegistrationLeaf(
                        "Private.Package",
                        "1.0.0",
                        """
                        "published": "2024-05-06T07:08:09Z"
                        """))),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(
            new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero),
            result.Published);
        Assert.Equal(
            "?token=abc",
            Assert.Single(
                handler.Requests,
                request => request.Uri.AbsolutePath
                    == "/registration/pages/p-42.json").Uri.Query);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_FetchesOnlyThePageCoveringTheRequestedVersion()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(
                        LinkedRegistrationPage(
                            "/registration/pages/one.json",
                            "0.1.0",
                            "1.0.0"),
                        LinkedRegistrationPage(
                            "/registration/pages/two.json",
                            "1.2.3-alpha",
                            "1.2.3-beta.5"),
                        LinkedRegistrationPage(
                            "/registration/pages/three.json",
                            "2.0.0",
                            "3.0.0"))),
                "/registration/pages/two.json" => Json(
                    RegistrationPageDocument(
                        RegistrationLeaf("Private.Package", "1.2.3-alpha"),
                        RegistrationLeaf(
                            "Private.Package",
                            "1.2.3-Beta.1+build.7",
                            """
                            "published": "2024-07-08T09:10:11Z",
                            "listed": false
                            """))),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.2.3-Beta.1+build.7",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(
            new DateTimeOffset(2024, 7, 8, 9, 10, 11, TimeSpan.Zero),
            result.Published);
        Assert.False(result.Listed);
        Assert.Equal(
            [
                "/v3/index.json",
                "/registration/private.package/index.json",
                "/registration/pages/two.json",
            ],
            handler.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_NormalizesRequestedVersionForPageSelection()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(
                        LinkedRegistrationPage(
                            "/registration/pages/older.json",
                            "0.1.0",
                            "0.9.0"),
                        RegistrationPage(
                            "1.0.0",
                            "1.0.0",
                            RegistrationLeaf(
                                "PRIVATE.package",
                                "1.0.0.0",
                                """
                                "published": "2024-11-12T13:14:15Z"
                                """)))),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Equal(
            new DateTimeOffset(2024, 11, 12, 13, 14, 15, TimeSpan.Zero),
            result.Published);
        Assert.Equal(
            ["/v3/index.json", "/registration/private.package/index.json"],
            handler.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_ValidRegistrationWithoutTheRequestedVersionIsAbsence()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(LinkedRegistrationPage(
                        "/registration/pages/newer.json",
                        "2.0.0",
                        "3.0.0"))),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Null(cold.Published);
        Assert.Null(warm.Published);
        Assert.Equal(coldRequests, handler.Requests.Count);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_MissingAdvertisedPageIsNotAbsence()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(LinkedRegistrationPage(
                        "/registration/pages/current.json",
                        "1.0.0",
                        "1.0.0"))),
                "/registration/pages/current.json" =>
                    new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Null(cold.Published);
        Assert.Null(warm.Published);
        Assert.True(handler.Requests.Count > coldRequests);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_MalformedSelectedPageIsNotAbsence()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(LinkedRegistrationPage(
                        "/registration/pages/current.json",
                        "1.0.0",
                        "1.0.0"))),
                "/registration/pages/current.json" =>
                    Json("""{ "items": { "catalogEntry": {} } }"""),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata cold = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        int coldRequests = handler.Requests.Count;
        PackageMetadata warm = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Null(cold.Published);
        Assert.False(cold.DeprecationMetadataAvailable);
        Assert.Null(warm.Published);
        Assert.True(handler.Requests.Count > coldRequests);
    }

    [Fact]
    public async Task FetchAllMetadataAsync_DoesNotTraverseLinkedPagesRecursively()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json(
                    RegistrationIndex(LinkedRegistrationPage(
                        "/registration/pages/current.json",
                        "1.0.0",
                        "1.0.0"))),
                "/registration/pages/current.json" => Json(
                    RegistrationPageDocument(LinkedRegistrationPage(
                        "/registration/pages/nested.json",
                        "1.0.0",
                        "1.0.0"))),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            new HttpClient(handler),
            "Private.Package",
            "1.0.0",
            log: null,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.Null(result.Published);
        Assert.Equal(
            [
                "/v3/index.json",
                "/registration/private.package/index.json",
                "/registration/pages/current.json",
            ],
            handler.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_AdmitsBoundedPageCountAndRejectsMore()
    {
        const string source = "https://private.example/v3/index.json";
        var handler = new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://private.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/bounded.package/index.json" =>
                    Json(PagedRegistrationIndex("Bounded.Package", pageCount: 128)),
                "/registration/oversized.package/index.json" =>
                    Json(PagedRegistrationIndex("Oversized.Package", pageCount: 129)),
                _ => throw new InvalidOperationException(
                    $"Unexpected metadata request: {request.RequestUri}"),
            });
        using var client = new HttpClient(handler);
        var options = new NuGetSourceOptions { Sources = [source] };

        PackageMetadata bounded = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Bounded.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);
        PackageMetadata oversized = await PackageMetadataService.FetchAllMetadataAsync(
            client,
            "Oversized.Package",
            "1.0.0",
            log: null,
            sourceOptions: options);

        Assert.Equal(
            new DateTimeOffset(2024, 9, 10, 11, 12, 13, TimeSpan.Zero),
            bounded.Published);
        Assert.Null(oversized.Published);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Uri.AbsolutePath.StartsWith(
                "/registration/pages/",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchAllMetadataAsync_LocalSourceDoesNotFallThroughToNuGetOrg()
    {
        List<string> log = [];

        PackageMetadata result = await PackageMetadataService.FetchAllMetadataAsync(
            DotnetInspector.Core.HttpClientFactory.Shared,
            "Private.Package",
            "1.0.0",
            log.Add,
            sourceOptions: new NuGetSourceOptions
            {
                Sources = [Path.Combine(
                    Path.GetTempPath(),
                    $"feed-{Guid.NewGuid():N}")],
            });

        Assert.Null(result.Published);
        Assert.Contains(
            log,
            message => message.StartsWith(
                "Skipping non-HTTP NuGet metadata source",
                StringComparison.Ordinal));
    }

    private static string RegistrationIndex(params string[] pages) =>
        $$"""{ "items": [ {{string.Join(",", pages)}} ] }""";

    private static string RegistrationPage(
        string lower,
        string upper,
        params string[] leaves) =>
        $$"""
        {
          "lower": "{{lower}}",
          "upper": "{{upper}}",
          "items": [ {{string.Join(",", leaves)}} ]
        }
        """;

    private static string LinkedRegistrationPage(
        string pageUrl,
        string lower,
        string upper) =>
        $$"""{ "@id": "{{pageUrl}}", "lower": "{{lower}}", "upper": "{{upper}}" }""";

    private static string InlineRegistrationPage(
        string pageUrl,
        string lower,
        string upper,
        params string[] leaves) =>
        $$"""
        {
          "@id": "{{pageUrl}}",
          "lower": "{{lower}}",
          "upper": "{{upper}}",
          "items": [ {{string.Join(",", leaves)}} ]
        }
        """;

    private static string PagedRegistrationIndex(string packageId, int pageCount)
    {
        string[] pages =
        [
            .. Enumerable.Range(1, pageCount - 1).Select(index =>
                LinkedRegistrationPage(
                    $"/registration/pages/{index}.json",
                    $"0.0.{index}",
                    $"0.0.{index}")),
            RegistrationPage(
                "1.0.0",
                "1.0.0",
                RegistrationLeaf(
                    packageId,
                    "1.0.0",
                    """
                    "published": "2024-09-10T11:12:13Z"
                    """)),
        ];
        return RegistrationIndex(pages);
    }

    private static string RegistrationPageDocument(params string[] leaves) =>
        $$"""{ "items": [ {{string.Join(",", leaves)}} ] }""";

    private static string RegistrationLeaf(
        string id,
        string version,
        string? catalogFields = null)
    {
        string extra = catalogFields is null ? "" : $", {catalogFields}";
        return $$"""
            { "catalogEntry": { "id": "{{id}}", "version": "{{version}}"{{extra}} } }
            """;
    }

    private static HttpResponseMessage SingleVersionRegistration(
        string id,
        string version,
        string? catalogFields = null) =>
        Json(RegistrationIndex(
            RegistrationPage(
                version,
                version,
                RegistrationLeaf(id, version, catalogFields))));

    private static HttpResponseMessage Json(string content) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage Html(string content) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "text/html"),
        };

    private static HttpResponseMessage Package(long? length)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent([0]),
        };
        response.Content.Headers.ContentRange = length is null
            ? new ContentRangeHeaderValue(0, 0)
            : new ContentRangeHeaderValue(0, 0, length.Value);
        return response;
    }

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                request.Headers.Range?.ToString()));
            return Task.FromResult(route(request));
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri Uri,
        AuthenticationHeaderValue? Authorization,
        string? Range);

    private sealed class TempNuGetConfig : IDisposable
    {
        public TempNuGetConfig(
            IReadOnlyList<(string Name, string Url)> sources,
            string? credentialedSource = null,
            IReadOnlyList<(string Source, string Pattern)>? mappings = null)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"metadata-sources-{Guid.NewGuid():N}.config");
            string sourceEntries = string.Join(
                Environment.NewLine,
                sources.Select(source =>
                    $"    <add key=\"{source.Name}\" value=\"{source.Url}\" />"));
            string credentials = credentialedSource is null
                ? ""
                : $$"""
                    <packageSourceCredentials>
                      <{{credentialedSource}}>
                        <add key="Username" value="user" />
                        <add key="ClearTextPassword" value="token" />
                      </{{credentialedSource}}>
                    </packageSourceCredentials>
                    """;
            string mapping = mappings is not { Count: > 0 }
                ? ""
                : $"""
                    <packageSourceMapping>
                    {string.Join(
                        Environment.NewLine,
                        mappings.Select(item =>
                            $"  <packageSource key=\"{item.Source}\"><package pattern=\"{item.Pattern}\" /></packageSource>"))}
                    </packageSourceMapping>
                    """;
            File.WriteAllText(Path, $$"""
                <configuration>
                  <packageSources>
                    <clear />
                {{sourceEntries}}
                  </packageSources>
                {{credentials}}
                {{mapping}}
                </configuration>
                """);
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}
