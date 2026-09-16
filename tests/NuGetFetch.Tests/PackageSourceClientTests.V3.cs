using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed partial class PackageSourceClientTests
{
    [Fact]
    public async Task CanonicalV3VersionAndPackageDiscoverDeclaredBaseAddress()
    {
        const string declaredBaseAddress =
            "https://packages.example/flat/";
        const string declaredVersions =
            "https://packages.example/flat/contoso/index.json";
        const string declaredPackage =
            "https://packages.example/flat/contoso/1.0.0/contoso.1.0.0.nupkg";
        var handler = new RecordingHandler
        {
            [NuGetClient.NuGetOrgServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{declaredBaseAddress}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [declaredVersions] = """{"versions":["1.0.0"]}""",
            [declaredPackage] = "package bytes",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                PackageSource.NuGetOrg,
                client);

        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));
        PackageCandidateObservation candidate =
            Assert.Single(versions.Candidates);

        Assert.Equal(
            PackageListingState.Unknown,
            candidate.ListingState);
        Assert.False(versions.HasAuthoritativeListingState);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;
        Assert.Equal("package bytes".Length, payload.AdvertisedLength);
        Assert.Equal(
            [
                NuGetClient.NuGetOrgServiceIndex,
                declaredVersions,
                declaredPackage,
            ],
            handler.Requested);
        Assert.DoesNotContain(NuGetOrgVersions, handler.Requested);
    }

    [Fact]
    public async Task LegacyNuGetClientRetainsCanonicalFlatContainerShortcut()
    {
        var handler = new RecordingHandler
        {
            [NuGetOrgVersions] = """{"versions":["1.0.0"]}""",
        };
        using var http = new HttpClient(handler);
        var client = new NuGetClient(http);

        IReadOnlyList<string> versions = await client.GetVersionsAsync(
            "contoso",
            cancellationToken:
                TestContext.Current.CancellationToken);

        Assert.Equal(["1.0.0"], versions);
        Assert.Equal([NuGetOrgVersions], handler.Requested);
    }

    [Fact]
    public async Task V3InvalidVersionMetadataIsTypedFailure()
    {
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{FlatContainer}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [Versions] = """{"versions":["../1.0.0"]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
        Assert.Same(runtime.Source, failure.Source);
    }

    [Fact]
    public async Task V3ServiceIndexNotFoundIsInvalidResponse()
    {
        var handler = new RecordingHandler();
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
        Assert.Null(failure.Coordinate);
        Assert.Equal([ServiceIndex], handler.Requested);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("file:///tmp/feed/")]
    [InlineData("https://user:secret@flat.example/")]
    [InlineData("https://flat.example/#fragment")]
    [InlineData("https://feed.example/v3/flat%/")]
    [InlineData("https://feed.example/v3/fl^at/")]
    [InlineData("https://feed.example/v3/flat/?sig=a%")]
    [InlineData(" https://feed.example/v3/flat/")]
    [InlineData("https://bücher.example/flat%/")]
    [InlineData("https://\u200D.example/flat/")]
    public async Task V3UnusablePackageBaseAddressIsInvalidResponse(
        string baseAddress)
    {
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{baseAddress}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
        Assert.Equal([ServiceIndex], handler.Requested);
    }

    [Fact]
    public async Task V3PostHeaderIoFailureIsTransportFailure()
    {
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{FlatContainer}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
        };
        handler.SetResponse(
            Versions,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ImmediateReadFailureStream(
                        new IOException("The response body ended."))),
                RequestMessage = request,
            });
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.Transport,
            failure.Kind);
        Assert.Equal(
            [ServiceIndex, Versions],
            handler.Requested);
    }

    [Fact]
    public async Task V3MissingPackageIsTypedAbsence()
    {
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{FlatContainer}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
        };
        handler.SetStatus(Package, HttpStatusCode.NotFound);
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.NotFound, failure.Kind);
        Assert.Equal(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            failure.Coordinate);
        Assert.Equal([ServiceIndex, Package], handler.Requested);
    }

    [Theory]
    [InlineData(
        "https://feed.example/v3/flat/?sig=secret",
        "?sig=secret")]
    [InlineData(
        "https://feed.example/v3/flat?sig=secret",
        "?sig=secret")]
    [InlineData(
        "https://feed.example/v3/flat/?s%69g=\u2713",
        "?s%69g=%E2%9C%93")]
    public async Task V3SignedPackageBaseAddressPreservesQuery(
        string baseAddress,
        string expectedQuery)
    {
        string signedVersions =
            "https://feed.example/v3/flat/contoso/index.json"
            + expectedQuery;
        string signedPackage =
            "https://feed.example/v3/flat/contoso/1.0.0/contoso.1.0.0.nupkg"
            + expectedQuery;
        string signedManifest =
            "https://feed.example/v3/flat/contoso/1.0.0/contoso.nuspec"
            + expectedQuery;
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{baseAddress}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [signedVersions] = """{"versions":["1.0.0"]}""",
            [signedManifest] = "<package />",
            [signedPackage] = "package bytes",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("signed-resource", ServiceIndex),
                client);

        Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken))
                .Candidates);
        Succeeded(
            await runtime.GetManifestAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await payload.Content.DisposeAsync();

        Assert.Equal(
            [
                ServiceIndex,
                signedVersions,
                ServiceIndex,
                signedManifest,
                ServiceIndex,
                signedPackage,
            ],
            handler.Requested);
    }

    [Fact]
    public async Task V3VersionManifestAndPackageDoNotSendCredentialCrossOrigin()
    {
        const string crossOriginBase =
            "https://packages.example/flat/";
        const string crossOriginVersions =
            "https://packages.example/flat/contoso/index.json";
        const string crossOriginPackage =
            "https://packages.example/flat/contoso/1.0.0/contoso.1.0.0.nupkg";
        const string crossOriginManifest =
            "https://packages.example/flat/contoso/1.0.0/contoso.nuspec";
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{crossOriginBase}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [crossOriginVersions] = """{"versions":["1.0.0"]}""",
            [crossOriginManifest] = "<package />",
            [crossOriginPackage] = "package bytes",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "credentialed",
                    ServiceIndex,
                    new PackageSourceCredential("user", "token")),
                client);

        Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken))
                .Candidates);
        Succeeded(
            await runtime.GetManifestAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await payload.Content.DisposeAsync();

        Assert.Equal(
            [
                "user:token",
                null,
                "user:token",
                null,
                "user:token",
                null,
            ],
            handler.Authentication.Select(DecodeBasic));
    }

    [Fact]
    public async Task V3EscapesUnicodePackageIdsAsPathSegments()
    {
        const string unicodeVersions =
            "https://feed.example/v3/flat/caf%C3%A9/index.json";
        const string unicodePackage =
            "https://feed.example/v3/flat/caf%C3%A9/1.0.0/caf%C3%A9.1.0.0.nupkg";
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "{{FlatContainer}}",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [unicodeVersions] = """{"versions":["1.0.0"]}""",
            [unicodePackage] = "package bytes",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("unicode", ServiceIndex),
                client);

        Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "Caf\u00E9",
                    TestContext.Current.CancellationToken))
                .Candidates);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "Caf\u00E9",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await payload.Content.DisposeAsync();

        Assert.Equal(
            [ServiceIndex, unicodeVersions, unicodePackage],
            handler.Requested);
    }

    [Fact]
    public async Task V3NormalizesIdnPackageBaseAddress()
    {
        const string idnVersions =
            "https://xn--bcher-kva.example/flat/contoso/index.json";
        var handler = new RecordingHandler
        {
            [ServiceIndex] = """
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "https://bücher.example/flat/",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [idnVersions] = """{"versions":["1.0.0"]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("idn", ServiceIndex),
                client);

        Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken))
                .Candidates);
        Assert.Equal(
            [ServiceIndex, idnVersions],
            handler.Requested);
    }

    [Fact]
    public async Task V3PreservesIpv6BracketsWhenEscapingBasePath()
    {
        const string ipv6Versions =
            "https://[::1]/caf%C3%A9/contoso/index.json";
        var handler = new RecordingHandler
        {
            [ServiceIndex] = """
                {
                  "version": "3.0.0",
                  "resources": [
                    {
                      "@id": "https://[::1]/café/",
                      "@type": "PackageBaseAddress/3.0.0"
                    }
                  ]
                }
                """,
            [ipv6Versions] = """{"versions":["1.0.0"]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("ipv6", ServiceIndex),
                client);

        Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken))
                .Candidates);
        Assert.Equal(
            [ServiceIndex, ipv6Versions],
            handler.Requested);
    }

    [Fact]
    public async Task GallerySearchNotFoundIsInvalidResponse()
    {
        var handler = new RecordingHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
        Assert.Null(failure.Coordinate);
    }

    [Theory]
    [InlineData("../admin")]
    [InlineData("contoso/package")]
    [InlineData(" contoso")]
    [InlineData("")]
    public async Task InvalidPackageIdFailsBeforeNetworkAccess(
        string packageId)
    {
        var handler = new RecordingHandler();
        HttpMessageHandler client = handler;
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        await Assert.ThrowsAsync<ArgumentException>(
            () => runtime.GetVersionsAsync(
                packageId,
                TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requested);
    }

    [Theory]
    [InlineData("../1.0.0")]
    [InlineData("1.0.0/extra")]
    [InlineData(" 1.0.0")]
    [InlineData("")]
    public async Task InvalidPackageVersionFailsBeforeNetworkAccess(
        string version)
    {
        var handler = new RecordingHandler();
        HttpMessageHandler client = handler;
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        await Assert.ThrowsAsync<ArgumentException>(
            () => runtime.GetPackageAsync(
                "contoso",
                version,
                TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task UnsupportedCapabilityFailsBeforeNetworkAccess()
    {
        var handler = new RecordingHandler();
        HttpMessageHandler client = handler;
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure error = Failed(
            await runtime.TryGetSymbolsAsync(
                "contoso",
                "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceCapabilities.SymbolPayload,
            error.Capability);
        Assert.Equal(
            PackageSourceFailureKind.Unsupported,
            error.Kind);
        Assert.Same(runtime.Source, error.Source);
        Assert.Equal(
            PackageSourceKind.NuGetV3,
            error.Source.TransportKind);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task CanonicalNuGetOrgV3DiscoversSearchWithoutShortcut()
    {
        const string declaredSearch =
            "https://search.example/query";
        const string request =
            declaredSearch
            + "?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        var handler = new RecordingHandler
        {
            [NuGetClient.NuGetOrgServiceIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{declaredSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [request] = """
                {
                  "data": [
                    {
                      "id": "Contoso",
                      "version": "1.0.0"
                    }
                  ]
                }
                """,
        };
        HttpMessageHandler client = handler;
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "nuget.org",
                    PackageSourceIdentity.NuGetOrg.Value,
                    new PackageSourceCredential("user", "token")),
                client);

        Assert.True(
            runtime.Capabilities.HasFlag(PackageSourceCapabilities.Search));
        PackageSearchMatch match = Assert.Single(
            Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken))
                .Matches);

        Assert.Equal("Contoso", match.Metadata.Id);
        Assert.Equal(
            [NuGetClient.NuGetOrgServiceIndex, request],
            handler.Requested);
        Assert.Equal(
            ["user:token", null],
            handler.Authentication.Select(DecodeBasic));
    }

    [Fact]
    public async Task V3SearchUsesHighestCompatibleResourcesAndFailsOver()
    {
        const string olderSearch =
            "https://feed.example/v3/query-old";
        const string firstSearch =
            "https://feed.example/v3/query-a?sig=%73ecret";
        const string secondSearch =
            "https://feed.example/v3/query-b";
        const string firstRequest =
            firstSearch
            + "&q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        const string secondRequest =
            secondSearch
            + "?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{olderSearch}}",
                      "@type": "SearchQueryService/3.0.0"
                    },
                    {
                      "@id": "{{firstSearch}}",
                      "@type": [
                        "SearchQueryService/3.5.0",
                        "SearchAutocompleteService/3.5.0"
                      ]
                    },
                    {
                      "@id": "{{secondSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [firstRequest] = "<html>sign in</html>",
            [secondRequest] = """
                {
                  "data": [
                    {
                      "id": "Contoso",
                      "version": "1.0.0"
                    }
                  ]
                }
                """,
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "corporate",
                    ServiceIndex,
                    new PackageSourceCredential("user", "token")),
                client);

        PackageSearchMatch match = Assert.Single(
            Succeeded(
                await runtime.SearchAsync(
                    "contoso",
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Matches);

        Assert.Equal("Contoso", match.Metadata.Id);
        Assert.Same(runtime.Source, match.Candidate.Source);
        Assert.Equal(PackageListingState.Listed, match.Candidate.ListingState);
        Assert.Equal(
            [ServiceIndex, firstRequest, secondRequest],
            handler.Requested);
        Assert.DoesNotContain(
            handler.Requested,
            request => request.StartsWith(
                olderSearch,
                StringComparison.Ordinal));
        Assert.Equal(
            ["user:token", "user:token", "user:token"],
            handler.Authentication.Select(DecodeBasic));
    }

    [Fact]
    public async Task V3SearchWithoutAdvertisedResourceIsTypedUnsupported()
    {
        var handler = new RecordingHandler
        {
            [ServiceIndex] = """{"resources":[]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Unsupported, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Search, failure.Capability);
        Assert.Equal([ServiceIndex], handler.Requested);
    }

    [Fact]
    public async Task V3MalformedAdvertisedSearchIsTypedInvalidResponse()
    {
        var handler = new RecordingHandler
        {
            [ServiceIndex] = """
                {
                  "resources": [
                    {
                      "@id": "not a URI",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Search, failure.Capability);
        Assert.Equal([ServiceIndex], handler.Requested);
    }

    [Fact]
    public async Task V3SearchPreservesDeclaredQueryBytes()
    {
        const string signedIndex =
            ServiceIndex + "?s%69g=%73ervice";
        const string signedSearch =
            SearchEndpoint + "?s%69g=%73earch";
        const string request =
            signedSearch
            + "&q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        var handler = new RecordingHandler
        {
            [signedIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{signedSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [request] = """{"data":[]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("signed", signedIndex),
                client);

        PackageSearchResult result = Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Empty(result.Matches);
        Assert.Equal([signedIndex, request], handler.Requested);
    }

    [Fact]
    public async Task V3SearchUsesLibraryDeadline()
    {
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                new StallingHandler(),
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(20),
                    OperationTimeout = TimeSpan.FromMilliseconds(100),
                });

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Search, failure.Capability);
    }

    [Fact]
    public async Task SharedContext_RequestTimeoutCanContinueWithAnotherSource()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(100),
            OperationTimeout = TimeSpan.FromSeconds(3),
        };
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient stalled =
            PackageSourceClientFactory.Create(
                new PackageSource("stalled", ServiceIndex),
                new StallingHandler(),
                options);
        var successfulHandler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{SearchEndpoint}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [SearchRequest] = """{"data":[]}""",
        };
        HttpMessageHandler successfulTransport = successfulHandler;
        using IPackageSourceClient successful =
            PackageSourceClientFactory.Create(
                new PackageSource("successful", ServiceIndex),
                successfulTransport,
                options);

        PackageSourceFailure failure = Failed(
            await stalled.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));
        PackageSearchResult result = Succeeded(
            await successful.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Empty(result.Matches);
        Assert.Equal(
            [ServiceIndex, SearchRequest],
            successfulHandler.Requested);
    }

    [Fact]
    public async Task SharedContext_MetadataBodyTimeoutUsesEffectiveRequestDeadline()
    {
        var sourceOptions = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            MetadataBodyTimeout = TimeSpan.FromMilliseconds(100),
            OperationTimeout = TimeSpan.FromSeconds(2),
        };
        var contextOptions = sourceOptions with
        {
            RequestTimeout = TimeSpan.FromMilliseconds(300),
        };
        using var operation = new NuGetOperationContext(
            contextOptions.RequestTimeout,
            contextOptions.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                new StallingMetadataBodyHandler(),
                sourceOptions);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
    }

    [Fact]
    public async Task SharedContext_ExpiredCeilingPreventsAnotherSource()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromMilliseconds(20),
        };
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        var handler = new RecordingHandler();
        HttpMessageHandler transport = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                transport,
                options);
        await Task.Delay(
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task SharedContext_ExpiredUnsupportedCapabilityIsTypedTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromMilliseconds(20),
        };
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient source =
            PackageSourceClientFactory.Create(
                new PackageSource("v3", ServiceIndex),
                new RecordingHandler(),
                options);
        await Task.Delay(
            TimeSpan.FromMilliseconds(40),
            TestContext.Current.CancellationToken);

        PackageSourceFailure failure = Failed(
            await source.SearchByPrefixAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
    }

    [Fact]
    public async Task V3SearchCallerCancellationRemainsCancellation()
    {
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                new StallingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.SearchAsync(
                "contoso",
                cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task SharedContext_CallerCancellationRetainsOriginalToken()
    {
        using var cancellation = new CancellationTokenSource();
        using var operation = new NuGetOperationContext(
            cancellation.Token);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                new StallingHandler());
        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runtime.SearchAsync(
                    "contoso",
                    cancellationToken: cancellation.Token,
                    operationContext: operation));

        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task SharedContext_RejectsDifferentInvocationToken()
    {
        using var caller = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        using var operation = new NuGetOperationContext(caller.Token);
        var handler = new RecordingHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                handler);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => runtime.SearchAsync(
                "contoso",
                cancellationToken: other.Token,
                operationContext: operation));

        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task SharedContext_DisposalIsTypedOperationTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(5),
            OperationTimeout = TimeSpan.FromSeconds(10),
        };
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        var handler = new StallingHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                handler,
                options);

        Task<PackageSourceOperationResult<PackageSearchResult>> search =
            runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation);
        await handler.RequestStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        operation.Dispose();
        PackageSourceFailure failure = Failed(await search);

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
    }

    [Fact]
    public async Task V3ServiceIndexTransportCancellationIsTypedTransport()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            ServiceIndex,
            _ => throw new OperationCanceledException(
                "transport cancellation"));
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Transport, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Search, failure.Capability);
        Assert.Equal([ServiceIndex], handler.Requested);
    }

    [Fact]
    public async Task V3SearchTransportTimeoutRemainsTypedTimeout()
    {
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{SearchEndpoint}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
        };
        handler.SetResponse(
            SearchRequest,
            _ => throw new TimeoutException("transport timeout"));
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Search, failure.Capability);
    }

    [Fact]
    public async Task V3SearchCanceledTransportTimeoutRemainsTypedTimeout()
    {
        var handler = new CanceledSearchTransportTimeoutHandler();
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(PackageSourceCapabilities.Search, failure.Capability);
    }

    [Fact]
    public async Task V3SearchNormalizesIdnServiceIndex()
    {
        const string unicodeIndex =
            "https://b\u00FCcher.example/v3/index.json";
        const string normalizedIndex =
            "https://xn--bcher-kva.example/v3/index.json";
        const string normalizedSearch =
            "https://xn--bcher-kva.example/v3/query";
        const string request =
            normalizedSearch
            + "?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        var handler = new RecordingHandler
        {
            [normalizedIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{normalizedSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [request] = """{"data":[]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("idn", unicodeIndex),
                client);

        PackageSearchResult result = Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Empty(result.Matches);
        Assert.Equal([normalizedIndex, request], handler.Requested);
    }

    [Fact]
    public async Task V3SearchPreservesSignedBytesWhileNormalizingIdn()
    {
        const string unicodeIndex =
            "https://b\u00FCcher.example/v3/\u00FCber/%69ndex.json?s%69g=\u2713";
        const string normalizedIndex =
            "https://xn--bcher-kva.example/v3/%C3%BCber/%69ndex.json?s%69g=%E2%9C%93";
        const string signedSearch =
            "https://xn--bcher-kva.example/v3/query?s%69g=%73earch";
        const string request =
            signedSearch
            + "&q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        var handler = new RecordingHandler
        {
            [normalizedIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{signedSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [request] = """{"data":[]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("signed-idn", unicodeIndex),
                client);

        PackageSearchResult result = Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Empty(result.Matches);
        Assert.Equal([normalizedIndex, request], handler.Requested);
    }

    [Fact]
    public async Task V3SearchNormalizesAdvertisedUnicodeEndpoint()
    {
        const string unicodeSearch =
            "https://b\u00FCcher.example/v3/\u00FCber/query?s%69g=\u2713";
        const string normalizedSearch =
            "https://xn--bcher-kva.example/v3/%C3%BCber/query?s%69g=%E2%9C%93";
        const string request =
            normalizedSearch
            + "&q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{unicodeSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [request] = """{"data":[]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("unicode-resource", ServiceIndex),
                client);

        PackageSearchResult result = Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Empty(result.Matches);
        Assert.Equal([ServiceIndex, request], handler.Requested);
    }

    [Fact]
    public async Task V3SearchPathlessServiceIndexPreservesSignedQuery()
    {
        const string pathlessIndex =
            "https://feed.example?s%69g=%73ource";
        const string normalizedIndex =
            "https://feed.example/?s%69g=%73ource";
        var handler = new RecordingHandler
        {
            [normalizedIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{SearchEndpoint}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [SearchRequest] = """{"data":[]}""",
        };
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("pathless-signed", pathlessIndex),
                client);

        PackageSearchResult result = Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Empty(result.Matches);
        Assert.Equal([normalizedIndex, SearchRequest], handler.Requested);
    }

    [Fact]
    public async Task V3SearchInvalidRawServiceIndexIsTypedInvalidResponse()
    {
        const string malformedIndex =
            ServiceIndex + "?sig=%zz";
        var handler = new RecordingHandler();
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("malformed", malformedIndex),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.InvalidResponse, failure.Kind);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task V3SearchDoesNotFailOverAuthenticationRejection()
    {
        const string firstSearch =
            "https://feed.example/v3/query-a";
        const string secondSearch =
            "https://feed.example/v3/query-b";
        const string firstRequest =
            firstSearch
            + "?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        const string secondRequest =
            secondSearch
            + "?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
        var handler = new RecordingHandler
        {
            [ServiceIndex] = $$"""
                {
                  "resources": [
                    {
                      "@id": "{{firstSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    },
                    {
                      "@id": "{{secondSearch}}",
                      "@type": "SearchQueryService/3.5.0"
                    }
                  ]
                }
                """,
            [secondRequest] = """{"data":[]}""",
        };
        handler.SetStatus(firstRequest, HttpStatusCode.Unauthorized);
        HttpMessageHandler client = handler;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "corporate",
                    ServiceIndex,
                    new PackageSourceCredential("user", "token")),
                client);

        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.AuthenticationRequired,
            failure.Kind);
        Assert.Equal([ServiceIndex, firstRequest], handler.Requested);
        Assert.DoesNotContain(secondRequest, handler.Requested);
    }
}
