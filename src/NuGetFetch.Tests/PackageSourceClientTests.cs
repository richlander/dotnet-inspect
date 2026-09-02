using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed class PackageSourceClientTests
{
    private const string GallerySearch =
        "https://azuresearch-usnc.nuget.org/query";
    private const string GalleryVersions =
        "https://globalcdn.nuget.org/v3-flatcontainer/contoso/index.json";
    private const string GalleryManifest =
        "https://globalcdn.nuget.org/v3-flatcontainer/contoso/1.0.0/contoso.nuspec";
    private const string GalleryRegistration =
        "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/index.json";
    private const string GalleryRegistrationPage =
        "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/2.0.0.json";
    private const string GalleryPackage =
        "https://globalcdn.nuget.org/packages/contoso.1.0.0.nupkg";
    private const string GallerySymbols =
        "https://globalcdn.nuget.org/symbol-packages/contoso.1.0.0.snupkg";
    private const string ServiceIndex =
        "https://feed.example/v3/index.json";
    private const string SearchEndpoint =
        "https://feed.example/v3/query";
    private const string SearchRequest =
        SearchEndpoint
        + "?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0";
    private const string FlatContainer =
        "https://feed.example/v3/flat/";
    private const string NuGetOrgVersions =
        "https://api.nuget.org/v3-flatcontainer/contoso/index.json";
    private const string Versions =
        "https://feed.example/v3/flat/contoso/index.json";
    private const string Package =
        "https://feed.example/v3/flat/contoso/1.0.0/contoso.1.0.0.nupkg";
    private const string Manifest =
        "https://feed.example/v3/flat/contoso/1.0.0/contoso.nuspec";

    [Fact]
    public void GalleryAndCanonicalV3ShareProducerIdentity()
    {
        PackageSourceDescriptor v3 = PackageSourceDescriptor.NuGetV3(
            "nuget-v3",
            "NuGet.org v3",
            new Uri("HTTPS://API.NUGET.ORG:443/v3/index.json/"));

        Assert.Equal(
            PackageSourceDescriptor.NuGetGallery.Identity,
            v3.Identity);
        Assert.NotEqual(
            PackageSourceDescriptor.NuGetGallery.Kind,
            v3.Kind);
        Assert.Null(PackageSourceDescriptor.NuGetGallery.Endpoint);
    }

    [Fact]
    public void HttpProducerIdentityPreservesEndpointDistinctions()
    {
        PackageSourceIdentity upperPath =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri("https://feed.example/V3/index.json"));
        PackageSourceIdentity lowerPath =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri("https://FEED.EXAMPLE:443/v3/index.json/"));
        PackageSourceIdentity query =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri("https://feed.example/V3/index.json?tenant=a"));

        Assert.NotEqual(upperPath, lowerPath);
        Assert.NotEqual(upperPath, query);
        Assert.Equal(
            "https://feed.example:443/v3/index.json",
            lowerPath.Value);
    }

    [Fact]
    public void HttpProducerIdentityFoldsIdnAndPercentEscapeSpelling()
    {
        PackageSourceIdentity unicode =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri("https://bücher.example/feed/%2f?q=%2f"));
        PackageSourceIdentity ascii =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri("https://xn--bcher-kva.example:443/feed/%2F?q=%2F"));

        Assert.Equal(unicode, ascii);
        Assert.Contains(
            "xn--bcher-kva.example:443",
            unicode.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HttpProducerIdentityPreservesIpv6Brackets()
    {
        PackageSourceIdentity identity =
            PackageSourceIdentity.ForHttpEndpoint(
                new Uri("https://[::1]/v3/index.json"));

        Assert.Equal(
            "https://[::1]:443/v3/index.json",
            identity.Value);
    }

    [Fact]
    public void DescriptorRejectsCredentialsEmbeddedInEndpoint()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PackageSourceDescriptor.NuGetV3(
                "credentialed",
                "Credentialed",
                new Uri("https://user:token@feed.example/v3/index.json")));

        Assert.Contains(
            "cannot contain user information",
            error.Message);
    }

    [Theory]
    [InlineData("https://feed.example/v3/index.json?sig=secret")]
    [InlineData("https://feed.example/v3/index.json#metadata")]
    public void PortableDescriptorRejectsQueryAndFragment(string endpoint)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PackageSourceDescriptor.NuGetV3(
                "nonportable",
                "Nonportable",
                new Uri(endpoint)));

        Assert.Contains(
            "cannot contain a query or fragment",
            error.Message);
    }

    [Theory]
    [InlineData("https://feed.example/v3/index.json?sig=secret")]
    [InlineData("https://feed.example/v3/index.json#sig=secret")]
    public void PortableDescriptorRejectsRawQueryAndFragment(string endpoint)
    {
        var rawEndpoint = new Uri(
            endpoint,
            new UriCreationOptions
            {
                DangerousDisablePathAndQueryCanonicalization = true,
            });

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PackageSourceDescriptor.NuGetV3(
                "raw",
                "Raw",
                rawEndpoint));

        Assert.Contains(
            "cannot contain a query or fragment",
            error.Message);
    }

    [Fact]
    public void PortableDescriptorRejectsRelativeEndpoint()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PackageSourceDescriptor.NuGetV3(
                "relative",
                "Relative",
                new Uri("v3/index.json", UriKind.Relative)));

        Assert.Contains("must be an absolute", error.Message);
    }

    [Fact]
    public void DescriptorIsCredentialFreeConfiguration()
    {
        PackageSourceDescriptor descriptor = PackageSourceDescriptor.NuGetV3(
            "corporate",
            "Corporate feed",
            new Uri(ServiceIndex),
            enabled: false);

        Assert.Equal("corporate", descriptor.Id);
        Assert.Equal("Corporate feed", descriptor.DisplayName);
        Assert.Equal(PackageSourceKind.NuGetV3, descriptor.Kind);
        Assert.False(descriptor.Enabled);
        Assert.Null(
            typeof(PackageSourceDescriptor).GetProperty(
                nameof(PackageSource.Credential)));
    }

    [Fact]
    public async Task LegacyPackageSourceCreatesV3Client()
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
            [Versions] = """{"versions":["1.0.0"]}""",
            [Manifest] = "<package />",
            [Package] = "package bytes",
        };
        HttpMessageHandler client = handler;
        var source = new PackageSource(
            "corporate",
            ServiceIndex,
            new PackageSourceCredential("user", "token"));

        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(source, client);

        Assert.Equal(PackageSourceKind.NuGetV3, runtime.Kind);
        Assert.Equal(
            PackageSourceCapabilities.Search
                | PackageSourceCapabilities.VersionEnumeration
                | PackageSourceCapabilities.Manifest
                | PackageSourceCapabilities.PackagePayload,
            runtime.Capabilities);
        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));
        PackageCandidateObservation candidate =
            Assert.Single(versions.Candidates);
        Assert.Equal("contoso", candidate.Coordinate.PackageId);
        Assert.Equal("1.0.0", candidate.Coordinate.Version);
        Assert.Equal(runtime.Identity, candidate.Producer);
        Assert.Equal(
            PackageDiscoveryContract.CompleteVersionEnumeration,
            candidate.DiscoveryContract);
        Assert.Equal(
            PackageListingState.Unknown,
            candidate.ListingState);
        Assert.False(versions.HasAuthoritativeListingState);
        PackageSourceManifest manifest = Succeeded(
            await runtime.GetManifestAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(candidate.Coordinate, manifest.Coordinate);
        Assert.Equal(runtime.Identity, manifest.Producer);
        Assert.Equal(PackageSourceKind.NuGetV3, manifest.TransportKind);
        Assert.Equal(
            "<package />",
            Encoding.UTF8.GetString(manifest.Content.Span));
        PackageSourcePayload packagePayload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream package = packagePayload.Content;
        Assert.Equal(candidate.Coordinate, packagePayload.Coordinate);
        Assert.Equal(runtime.Identity, packagePayload.Producer);
        Assert.Equal(
            PackageSourcePayloadKind.Package,
            packagePayload.Kind);
        Assert.Equal(PackageSourceKind.NuGetV3, packagePayload.TransportKind);
        using var reader = new StreamReader(package);
        Assert.Equal(
            "package bytes",
            await reader.ReadToEndAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            [
                "user:token",
                "user:token",
                "user:token",
                "user:token",
                "user:token",
                "user:token",
            ],
            handler.Authentication.Select(DecodeBasic));
    }

    [Fact]
    public async Task LegacySignedSourceRemainsRuntimeOnlyConfiguration()
    {
        const string signedServiceIndex =
            ServiceIndex + "?sig=secret";
        var handler = new RecordingHandler
        {
            [signedServiceIndex] = $$"""
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
            [Versions] = """{"versions":["1.0.0"]}""",
        };
        HttpMessageHandler client = handler;
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "signed",
                    signedServiceIndex,
                    new PackageSourceCredential("user", "token")),
                client);

        PackageCandidateObservation candidate = Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken))
                .Candidates);
        Assert.Equal("1.0.0", candidate.Coordinate.Version);
        Assert.Equal(runtime.Identity, candidate.Producer);
        Assert.DoesNotContain(
            "secret",
            runtime.Identity.Value,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            runtime.Identity,
            PackageSourceIdentity.ForProducerEndpoint(
                new Uri(ServiceIndex + "?sig=other")));
        Assert.Equal(
            [signedServiceIndex, Versions],
            handler.Requested);
    }

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
        Assert.Equal(runtime.Identity, failure.Producer);
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
        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(PackageSourceKind.NuGetV3, error.TransportKind);
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
        Assert.Equal(runtime.Identity, match.Candidate.Producer);
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
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                TimeSpan.FromMilliseconds(100)),
            failure.Timeout);
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

        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            failure.Timeout);
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
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.MetadataBody,
                sourceOptions.MetadataBodyTimeout),
            failure.Timeout);
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

        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                options.OperationTimeout),
            failure.Timeout);
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
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                options.OperationTimeout),
            failure.Timeout);
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
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                options.OperationTimeout),
            failure.Timeout);
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
        Assert.Null(failure.Timeout);
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
        Assert.Null(failure.Timeout);
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

    [Fact]
    public async Task GalleryClientUsesKnownEndpointsWithoutServiceIndex()
    {
        var handler = new RecordingHandler
        {
            [GallerySearch] = """
                {
                  "data": [
                    {
                      "id": "Contoso",
                      "version": "1.0.0",
                      "authors": ["Contoso"],
                      "owners": ["Contoso", "Partner"]
                    }
                  ]
                }
                """,
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryManifest] = "<package />",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json#identity",
                      "items": [
                        {
                          "catalogEntry": {
                            "version": "1.0.0"
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
            [GalleryPackage] = "package bytes",
            [GallerySymbols] = "symbol bytes",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        Assert.Equal(
            PackageSourceCapabilities.Search
                | PackageSourceCapabilities.VersionEnumeration
                | PackageSourceCapabilities.Manifest
                | PackageSourceCapabilities.PackagePayload
                | PackageSourceCapabilities.SymbolPayload,
            runtime.Capabilities);
        PackageSearchMatch match = Assert.Single(
            Succeeded(
                await runtime.SearchAsync(
                    "contoso",
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Matches);
        Assert.Equal("Contoso", match.Metadata.Id);
        Assert.Equal("contoso", match.Candidate.Coordinate.PackageId);
        Assert.Equal("1.0.0", match.Candidate.Coordinate.Version);
        Assert.Equal(runtime.Identity, match.Candidate.Producer);
        Assert.Equal(
            PackageDiscoveryContract.KeywordSearch,
            match.Candidate.DiscoveryContract);
        Assert.Equal(
            PackageListingState.Listed,
            match.Candidate.ListingState);
        Assert.Equal(["Contoso", "Partner"], match.Metadata.Owners);
        PackageSearchMatch prefixMatch = Assert.Single(
            Succeeded(
                await runtime.SearchByPrefixAsync(
                    "Contoso",
                    take: 1,
                    cancellationToken:
                        TestContext.Current.CancellationToken))
                .Matches);
        Assert.Equal(match.Candidate, prefixMatch.Candidate);
        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "Contoso",
                TestContext.Current.CancellationToken));
        PackageCandidateObservation version =
            Assert.Single(versions.Candidates);
        Assert.Equal("1.0.0", version.Coordinate.Version);
        Assert.Equal(
            PackageListingState.Listed,
            version.ListingState);
        Assert.True(versions.HasAuthoritativeListingState);
        PackageSourceManifest manifest = Succeeded(
            await runtime.GetManifestAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        Assert.Equal("<package />", Encoding.UTF8.GetString(manifest.Content.Span));
        Assert.Equal(match.Candidate.Coordinate, manifest.Coordinate);
        Assert.Equal(runtime.Identity, manifest.Producer);
        Assert.Equal(PackageSourceKind.NuGetGallery, manifest.TransportKind);
        PackageSourcePayload packagePayload = Succeeded(
            await runtime.GetPackageAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        PackageSourcePayload symbolPayload = Succeeded(
            await runtime.TryGetSymbolsAsync(
                "Contoso",
                "1.0",
                TestContext.Current.CancellationToken));
        await using Stream package = packagePayload.Content;
        await using Stream symbols = symbolPayload.Content;

        Assert.Equal("package bytes", await ReadAsync(package));
        Assert.Equal("symbol bytes", await ReadAsync(symbols));
        Assert.Equal(
            PackageSourcePayloadKind.Package,
            packagePayload.Kind);
        Assert.Equal(
            PackageSourcePayloadKind.Symbols,
            symbolPayload.Kind);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            packagePayload.TransportKind);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            symbolPayload.TransportKind);
        Assert.Equal("package bytes".Length, packagePayload.AdvertisedLength);
        Assert.Equal("symbol bytes".Length, symbolPayload.AdvertisedLength);
        Assert.Equal(packagePayload.Coordinate, symbolPayload.Coordinate);
        Assert.Equal(runtime.Identity, packagePayload.Producer);
        Assert.Equal(runtime.Identity, symbolPayload.Producer);
        Assert.DoesNotContain(
            handler.Requested,
            url => url.Contains(
                "api.nuget.org/v3/index.json",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handler.Requested,
            url => url.StartsWith(
                $"{GallerySearch}?",
                StringComparison.Ordinal));
        string[] searchRequests =
        [
            .. handler.Requested.Where(
                url => url.StartsWith(
                    GallerySearch,
                    StringComparison.Ordinal)),
        ];
        Assert.Equal(2, searchRequests.Length);
        Assert.All(
            searchRequests,
            searchRequest =>
            {
                Assert.Contains("q=Contoso", searchRequest, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("prerelease=false", searchRequest);
                Assert.Contains("semVerLevel=2.0.0", searchRequest);
            });
        Assert.Equal(
            [
                GalleryVersions,
                GalleryRegistration,
                GalleryManifest,
                GalleryPackage,
                GallerySymbols,
            ],
            handler.Requested.Where(
                url => !url.StartsWith(
                    GallerySearch,
                    StringComparison.Ordinal)));
        Assert.All(handler.Authentication, Assert.Null);
        Assert.All(
            handler.Headers,
            headers =>
            {
                Assert.DoesNotContain("Authorization", headers.Keys);
                Assert.DoesNotContain("Cookie", headers.Keys);
                Assert.DoesNotContain("X-NuGet-ApiKey", headers.Keys);
            });
    }

    [Fact]
    public async Task GalleryEnumerationJoinsAuthoritativeListingState()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] =
                """{"versions":["1.0.0","1.1.0","2.0.0-beta.1"]}""",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "items": [
                        {
                          "catalogEntry": {
                            "version": "1.0",
                            "listed": true
                          }
                        },
                        {
                          "catalogEntry": {
                            "version": "1.1.0",
                            "listed": false
                          }
                        },
                        {
                          "catalogEntry": {
                            "version": "2.0.0-beta.1"
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(
            [
                ("1.0.0", PackageListingState.Listed),
                ("1.1.0", PackageListingState.Unlisted),
                ("2.0.0-beta.1", PackageListingState.Listed),
            ],
            result.Candidates.Select(candidate => (
                candidate.Coordinate.Version,
                candidate.ListingState)));
        Assert.All(
            result.Candidates,
            candidate => Assert.Equal(
                PackageDiscoveryContract.CompleteVersionEnumeration,
                candidate.DiscoveryContract));
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Fact]
    public async Task GalleryExternalRegistrationPageIsValidatedAndRebased()
    {
        const string externalPage =
            "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/2.0.0.json";
        var handler = new RecordingHandler
        {
            [GalleryVersions] =
                """{"versions":["1.0.0","2.0.0"]}""",
            [GalleryRegistration] = $$"""
                {
                  "items": [
                    {
                      "@id": "{{externalPage}}"
                    }
                  ]
                }
                """,
            [GalleryRegistrationPage] = """
                {
                  "items": [
                    {
                      "catalogEntry": {
                        "version": "1.0.0",
                        "listed": false
                      }
                    },
                    {
                      "catalogEntry": {
                        "version": "2.0.0",
                        "listed": true
                      }
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(
            [
                PackageListingState.Unlisted,
                PackageListingState.Listed,
            ],
            result.Candidates.Select(candidate =>
                candidate.ListingState));
        Assert.Equal(
            [
                GalleryVersions,
                GalleryRegistration,
                GalleryRegistrationPage,
            ],
            handler.Requested);
        Assert.DoesNotContain(externalPage, handler.Requested);
    }

    [Theory]
    [InlineData("http://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json")]
    [InlineData("https://user@api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json?secret=x")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json#fragment")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/other/page/1.0.0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/%63ontoso/page/1.0.0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1%2E0%2E0/1.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0%2F2.0.0/2.0.0.json")]
    [InlineData("https://api.nuget.org/v3/registration5-gz-semver2/contoso/not-page/1.0.0/1.0.0.json")]
    public async Task GalleryRejectsIneligibleExternalRegistrationPage(
        string externalPage)
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = $$"""
                {
                  "items": [
                    {
                      "@id": "{{externalPage}}"
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Fact]
    public async Task GalleryIncompleteRegistrationIsTypedPartialEnumeration()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] =
                """{"versions":["1.0.0","2.0.0"]}""",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "items": [
                        {
                          "catalogEntry": {
                            "version": "1.0.0",
                            "listed": false
                          }
                        }
                      ]
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.All(
            result.Candidates,
            candidate => Assert.Equal(
                PackageListingState.Unknown,
                candidate.ListingState));
    }

    [Theory]
    [InlineData("""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": "false"
                  }
                }
              ]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": true
                  }
                },
                {
                  "catalogEntry": {
                    "version": "1.0",
                    "listed": false
                  }
                }
              ]
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "items": [
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": false,
                    "listed": true
                  }
                }
              ]
            }
          ]
        }
        """)]
    public async Task GalleryMalformedRegistrationIsTypedPartialEnumeration(
        string registration)
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task GalleryCorruptEncodedVersionMetadataIsInvalidResponse()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryVersions,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ImmediateReadFailureStream(
                        new InvalidDataException(
                            "The encoded response body is corrupt."))),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
    }

    [Fact]
    public async Task GalleryCorruptEncodedRegistrationIsTypedPartialEnumeration()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
        };
        handler.SetResponse(
            GalleryRegistration,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ImmediateReadFailureStream(
                        new InvalidDataException(
                            "The encoded response body is corrupt."))),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task GalleryMalformedExternalPageIsTypedPartialEnumeration()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = """
                {
                  "items": [
                    {
                      "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
                    }
                  ]
                }
                """,
            [GalleryRegistrationPage] = """{"items":{}}""",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task GalleryRegistrationParserRetainsOnlyFlatCandidates()
    {
        const string page = """
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "listed": false
                  }
                },
                {
                  "catalogEntry": {
                    "version": "2.0.0",
                    "listed": true
                  }
                }
              ]
            }
            """;
        using var json = new MemoryStream(Encoding.UTF8.GetBytes(page));
        var candidates = new HashSet<string>(
            ["1.0.0"],
            StringComparer.OrdinalIgnoreCase);
        var budget =
            new NuGetGalleryRegistrationBudget(
                candidates.Count,
                NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        using var operation = CreateRegistrationParserOperation(
            TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, PackageListingState> listings =
            await NuGetGalleryRegistration.DeserializePageAsync(
                json,
                candidates,
                budget,
                operation,
                TestContext.Current.CancellationToken);

        KeyValuePair<string, PackageListingState> listing =
            Assert.Single(listings);
        Assert.Equal("1.0.0", listing.Key);
        Assert.Equal(PackageListingState.Unlisted, listing.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryRegistrationTraversalHonorsCallerCancellation(
        bool inline)
    {
        const int itemCount = 512;
        string items = RegistrationItems(itemCount);
        using var cancellation = new CancellationTokenSource();
        var candidates = new InterruptingReadOnlySet(
            itemCount,
            cancellation.Cancel);
        var budget =
            new NuGetGalleryRegistrationBudget(
                candidates.Count,
                NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        using var operation =
            CreateRegistrationParserOperation(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DeserializeRegistrationItemsAsync(
                items,
                inline,
                candidates,
                budget,
                operation,
                cancellation.Token));

        Assert.Equal(128, candidates.ContainsCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryRegistrationTraversalUsesMonotonicDeadline(
        bool inline)
    {
        const int itemCount = 512;
        string items = RegistrationItems(itemCount);
        var candidates = new InterruptingReadOnlySet(
            itemCount,
            () => Thread.Sleep(TimeSpan.FromMilliseconds(250)));
        var budget =
            new NuGetGalleryRegistrationBudget(
                candidates.Count,
                NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        using var operation = new NuGetOperationDeadline(
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromMilliseconds(100),
            },
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            await Assert.ThrowsAsync<NuGetOperationTimeoutException>(
                () => DeserializeRegistrationItemsAsync(
                    items,
                    inline,
                    candidates,
                    budget,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(100), error.Timeout);
        Assert.Equal(128, candidates.ContainsCalls);
    }

    [Fact]
    public async Task GalleryRegistrationLeafLimitIsTypedPartialEnumeration()
    {
        string extraItems = string.Join(
            ",",
            Enumerable.Range(
                2,
                NuGetGalleryRegistrationBudget.MinimumLeafCount)
                .Select(version =>
                    $$"""
                      {
                        "catalogEntry": {
                          "version": "{{version}}.0.0"
                        }
                      }
                      """));
        string registration = $$"""
            {
              "items": [
                {
                  "items": [
                    {
                      "catalogEntry": {
                        "version": "1.0.0"
                      }
                    },
                    {{extraItems}}
                  ]
                }
              ]
            }
            """;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Fact]
    public async Task GalleryRegistrationAggregateByteLimitIsTypedPartialEnumeration()
    {
        const int maximumBytes = 512;
        const string firstPage =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json";
        const string secondPage =
            "https://globalcdn.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.1/1.0.1.json";
        string padding = new('a', 220);
        string registration = $$"""
            {
              "items": [
                {
                  "@id": "{{firstPage}}"
                },
                {
                  "@id": "{{secondPage}}"
                }
              ]
            }
            """;
        string page = $$"""
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "padding": "{{padding}}"
                  }
                }
              ]
            }
            """;
        Assert.InRange(
            Encoding.UTF8.GetByteCount(registration),
            1,
            maximumBytes);
        Assert.InRange(
            Encoding.UTF8.GetByteCount(page),
            1,
            maximumBytes);
        Assert.True(
            Encoding.UTF8.GetByteCount(registration)
            + (2 * Encoding.UTF8.GetByteCount(page))
            > maximumBytes);
        Assert.True(
            2 * Encoding.UTF8.GetByteCount(page)
            < 1_024);
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
            [firstPage] = page,
            [secondPage] = page,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = 1_024,
                    MaxRegistrationMetadataBytes = maximumBytes,
                });

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [
                GalleryVersions,
                GalleryRegistration,
                firstPage,
                secondPage,
            ],
            handler.Requested);
    }

    [Fact]
    public async Task
        GalleryRegistrationDefaultAggregateCoversMeasuredMassTransitCanary()
    {
        const int pageCount = 25;
        const int measuredMassTransitBytes = 18_163_736;
        string padding = new('a', 740_000);
        string page = $$"""
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "padding": "{{padding}}"
                  }
                }
              ]
            }
            """;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
        };
        var indexPages = new string[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            string version = $"1.0.{i}";
            string path =
                "/v3/registration5-gz-semver2/contoso/page/"
                + $"{version}/{version}.json";
            indexPages[i] =
                $$"""{"@id":"https://api.nuget.org{{path}}"}""";
            handler[$"https://globalcdn.nuget.org{path}"] = page;
        }

        string registration =
            $$"""{"items":[{{string.Join(",", indexPages)}}]}""";
        long registrationBytes =
            Encoding.UTF8.GetByteCount(registration)
            + ((long)pageCount * Encoding.UTF8.GetByteCount(page));
        Assert.True(
            registrationBytes
            > NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        Assert.True(registrationBytes >= measuredMassTransitBytes);
        Assert.True(
            registrationBytes
            < NuGetFetchOptions.DefaultMaxRegistrationMetadataBytes);
        handler[GalleryRegistration] = registration;
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Listed,
            Assert.Single(result.Candidates).ListingState);
    }

    [Fact]
    public async Task
        GalleryRegistrationDefaultBatchExceedsPerResponseLimit()
    {
        const int pageCount = 8;
        string padding = new('a', 2_100_000);
        string page = $$"""
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0",
                    "padding": "{{padding}}"
                  }
                }
              ]
            }
            """;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
        };
        var indexPages = new string[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            string version = $"1.0.{i}";
            string path =
                "/v3/registration5-gz-semver2/contoso/page/"
                + $"{version}/{version}.json";
            indexPages[i] =
                $$"""{"@id":"https://api.nuget.org{{path}}"}""";
            handler[$"https://globalcdn.nuget.org{path}"] = page;
        }

        int pageBytes = Encoding.UTF8.GetByteCount(page);
        long batchBytes = (long)pageCount * pageBytes;
        Assert.True(
            pageBytes
            < NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        Assert.True(
            batchBytes
            > NuGetFetchOptions.DefaultMaxMetadataResponseBytes);
        Assert.True(
            batchBytes
            < NuGetFetchOptions.DefaultMaxRegistrationPageBatchBytes);
        handler[GalleryRegistration] =
            $$"""{"items":[{{string.Join(",", indexPages)}}]}""";
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
    }

    [Fact]
    public async Task GalleryRegistrationReservationWaitsForReturnedCapacity()
    {
        var budget = new NuGetGalleryRegistrationBudget(
            candidateCount: 1,
            maximumBytes: 2);
        byte[] buffer = new byte[1];
        using Stream first = budget.LimitBytes(
            new MemoryStream([(byte)'a']));
        Assert.Equal(
            1,
            await first.ReadAsync(
                buffer,
                TestContext.Current.CancellationToken));
        var blockedEof = new BlockingEofStream();
        using Stream eof = budget.LimitBytes(blockedEof);
        Task<int> eofRead = eof.ReadAsync(
            buffer,
            TestContext.Current.CancellationToken).AsTask();
        await blockedEof.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        using Stream final = budget.LimitBytes(
            new MemoryStream([(byte)'b']));
        Task<int> finalRead = final.ReadAsync(
            buffer,
            TestContext.Current.CancellationToken).AsTask();
        Assert.False(finalRead.IsCompleted);

        blockedEof.Release.TrySetResult();

        Assert.Equal(0, await eofRead);
        Assert.Equal(1, await finalRead);
    }

    [Fact]
    public async Task
        GalleryRegistrationMaterializationBudgetReturnsFailedAttemptCapacity()
    {
        var budget = new NuGetGalleryRegistrationByteBudget(
            maximumBytes: 2);

        await Assert.ThrowsAsync<IOException>(
            () => budget.MaterializeAsync(
                new ReadThenFailureStream([(byte)'a', (byte)'b']),
                TestContext.Current.CancellationToken));

        using NuGetGalleryRegistrationByteBudget.Materialization
            materialization = await budget.MaterializeAsync(
            new MemoryStream([(byte)'c', (byte)'d']),
            TestContext.Current.CancellationToken);
        using MemoryStream destination = materialization.Commit();

        Assert.Equal("cd", Encoding.UTF8.GetString(destination.ToArray()));

        await Assert.ThrowsAsync<
            NuGetRegistrationResourceLimitExceededException>(
            () => budget.MaterializeAsync(
                new MemoryStream([(byte)'e']),
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        GalleryLatePageDeadlineReturnsMaterializationCapacity(
            bool metadataBodyDeadline)
    {
        const string page = """
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0"
                  }
                }
              ]
            }
            """;
        string registration = $$"""
            {
              "items": [
                {
                  "@id": "{{GalleryRegistrationPage}}"
                }
              ]
            }
            """;
        byte[] pageBytes = Encoding.UTF8.GetBytes(page);
        int pageRequests = 0;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        handler.SetResponse(
            GalleryRegistrationPage,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Interlocked.Increment(ref pageRequests) == 1
                    ? new StreamContent(
                        new LateEofStream(
                            pageBytes,
                            TimeSpan.FromMilliseconds(100)))
                    : new ByteArrayContent(pageBytes),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = Math.Max(
                        pageBytes.Length,
                        Encoding.UTF8.GetByteCount(registration)),
                    MaxRegistrationPageBatchBytes = pageBytes.Length,
                    MaxRegistrationMetadataBytes =
                        Encoding.UTF8.GetByteCount(registration)
                        + (2L * pageBytes.Length),
                    RequestTimeout = metadataBodyDeadline
                        ? TimeSpan.FromSeconds(1)
                        : TimeSpan.FromMilliseconds(40),
                    OperationTimeout = TimeSpan.FromSeconds(3),
                    MetadataBodyTimeout = metadataBodyDeadline
                        ? TimeSpan.FromMilliseconds(40)
                        : Timeout.InfiniteTimeSpan,
                });

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(2, pageRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        GalleryCleanupFailureReturnsMaterializationCapacity(
            bool responseCleanup)
    {
        const string page = """
            {
              "items": [
                {
                  "catalogEntry": {
                    "version": "1.0.0"
                  }
                }
              ]
            }
            """;
        string registration = $$"""
            {
              "items": [
                {
                  "@id": "{{GalleryRegistrationPage}}"
                }
              ]
            }
            """;
        byte[] pageBytes = Encoding.UTF8.GetBytes(page);
        int pageRequests = 0;
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = registration,
        };
        handler.SetResponse(
            GalleryRegistrationPage,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Interlocked.Increment(ref pageRequests) == 1
                    ? new CleanupFailureContent(
                        pageBytes,
                        responseCleanup)
                    : new ByteArrayContent(pageBytes),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = Math.Max(
                        pageBytes.Length,
                        Encoding.UTF8.GetByteCount(registration)),
                    MaxRegistrationPageBatchBytes = pageBytes.Length,
                    MaxRegistrationMetadataBytes =
                        Encoding.UTF8.GetByteCount(registration)
                        + (2L * pageBytes.Length),
                    RequestTimeout = TimeSpan.FromSeconds(1),
                    OperationTimeout = TimeSpan.FromSeconds(3),
                });

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(2, pageRequests);
    }

    [Fact]
    public async Task GalleryRegistrationAggregateCountsFailedAttemptBytes()
    {
        var budget = new NuGetGalleryRegistrationByteBudget(
            maximumBytes: 2);

        using (Stream failedAttempt = budget.LimitBytes(
            new ReadThenFailureStream([(byte)'a'])))
        {
            await Assert.ThrowsAsync<IOException>(
                () => failedAttempt.CopyToAsync(
                    new MemoryStream(),
                    TestContext.Current.CancellationToken));
        }

        using Stream retry = budget.LimitBytes(
            new MemoryStream([(byte)'b', (byte)'c']));
        await Assert.ThrowsAsync<
            NuGetRegistrationResourceLimitExceededException>(
            () => retry.CopyToAsync(
                new MemoryStream(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GalleryRegistrationPageLimitIsTypedPartialEnumeration()
    {
        const string externalPage =
            """
            {
              "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
            }
            """;
        string pages = string.Join(
            ",",
            Enumerable.Repeat(
                externalPage,
                NuGetGalleryRegistrationBudget.MaximumPageCount + 1));
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryRegistration] = $$"""{"items":[{{pages}}]}""",
            [GalleryRegistrationPage] = """
                {
                  "items": [
                    {
                      "catalogEntry": {
                        "version": "1.0.0"
                      }
                    }
                  ]
                }
                """,
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.False(result.HasAuthoritativeListingState);
        Assert.Equal(
            PackageListingState.Unknown,
            Assert.Single(result.Candidates).ListingState);
        Assert.Equal(
            [GalleryVersions, GalleryRegistration],
            handler.Requested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrationResourceLimitsMapToResponseRejected(
        bool pageLimit)
    {
        PackageSourceDescriptor descriptor =
            PackageSourceDescriptor.NuGetGallery;
        var budget = new NuGetGalleryRegistrationBudget(
            candidateCount: 1,
            maximumBytes: 1);

        PackageSourceFailure failure = Failed(
            await PackageSourceOperation.CaptureAsync(
                descriptor.Identity,
                descriptor.Kind,
                PackageSourceCapabilities.VersionEnumeration,
                () =>
                {
                    if (pageLimit)
                    {
                        budget.EnsurePageCount(
                            NuGetGalleryRegistrationBudget.MaximumPageCount
                            + 1);
                    }
                    else
                    {
                        for (int i = 0;
                             i <= NuGetGalleryRegistrationBudget
                                 .MinimumLeafCount;
                             i++)
                        {
                            budget.ObserveLeaf();
                        }
                    }

                    return Task.FromResult(0);
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            failure.Kind);
    }

    [Fact]
    public async Task GalleryExternalPagesUseBoundedConcurrency()
    {
        var handler = new ConcurrentRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult result = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.True(result.HasAuthoritativeListingState);
        Assert.Equal(9, result.Candidates.Count);
        Assert.Equal(9, handler.PageRequests);
        Assert.Equal(8, handler.MaxActivePageRequests);
    }

    [Fact]
    public async Task GalleryCallerCancellationDuringRegistrationRemainsCancellation()
    {
        var handler = new CancelableRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        using var cancellation = new CancellationTokenSource();
        Task<PackageSourceOperationResult<PackageVersionResult>> operation =
            runtime.GetVersionsAsync(
                "contoso",
                cancellation.Token);
        await handler.RegistrationStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
    }

    [Fact]
    public async Task GallerySharedContextCallerCancellationDuringRegistrationRemainsCancellation()
    {
        var handler = new CancelableRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        using var cancellation = new CancellationTokenSource();
        using var context = new NuGetOperationContext(cancellation.Token);
#pragma warning disable xUnit1051 // The default invocation token is the contract under test.
        Task<PackageSourceOperationResult<PackageVersionResult>> operation =
            runtime.GetVersionsAsync(
                "contoso",
                operationContext: context);
#pragma warning restore xUnit1051
        await handler.RegistrationStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => operation);
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public async Task GalleryCallerCancellationOutranksConcurrentRegistrationFault()
    {
        var handler = new FaultAndCancelRegistrationHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        using var cancellation = new CancellationTokenSource();
        Task<PackageSourceOperationResult<PackageVersionResult>> operation =
            runtime.GetVersionsAsync(
                "contoso",
                cancellation.Token);
        await handler.BothPagesStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();
        handler.ReleaseFault.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryConcurrentTransportFaultCannotHideTimeout(
        bool operationExpires)
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(60),
            OperationTimeout = operationExpires
                ? TimeSpan.FromMilliseconds(800)
                : TimeSpan.FromSeconds(5),
        };
        var handler = new FaultAndTimeoutRegistrationHandler();
        using var context = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: context));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                operationExpires
                    ? PackageSourceTimeoutKind.Operation
                    : PackageSourceTimeoutKind.Request,
                operationExpires
                    ? options.OperationTimeout
                    : options.RequestTimeout),
            failure.Timeout);
        Assert.True(handler.FastTransportRequests > 0);
        Assert.True(handler.StallingRequests > 0);
    }

    [Fact]
    public async Task GalleryConcurrentTransportFaultCannotHideTransportTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromSeconds(5),
        };
        var handler = new FaultAndTimeoutRegistrationHandler(
            transportTimeout: true);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Null(failure.Timeout);
        Assert.True(handler.FastTransportRequests > 0);
        Assert.True(handler.StallingRequests > 0);
    }

    [Fact]
    public async Task GalleryConcurrentTransportFaultCannotHideCanceledTransportTimeout()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromSeconds(5),
        };
        var handler = new FaultAndTimeoutRegistrationHandler(
            canceledTransportTimeout: true);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Null(failure.Timeout);
        Assert.True(handler.FastTransportRequests > 0);
        Assert.True(handler.StallingRequests > 0);
    }

    [Fact]
    public async Task GalleryLateProtocolFailureCannotBecomePartial()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateMalformedRegistrationHandler(),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                options.OperationTimeout),
            failure.Timeout);
    }

    [Fact]
    public async Task GalleryLateMetadataProtocolFailurePreservesBodyDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromSeconds(2),
            MetadataBodyTimeout = TimeSpan.FromMilliseconds(20),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateMalformedRegistrationHandler(),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.MetadataBody,
                options.MetadataBodyTimeout),
            failure.Timeout);
    }

    [Fact]
    public async Task GalleryLateInvalidDataPreservesRequestDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(5),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateInvalidDataRegistrationHandler(),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            failure.Timeout);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GalleryLateStreamingTimeoutPreservesDeadline(
        bool operationExpires,
        bool canceledTransportTimeout)
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = operationExpires
                ? TimeSpan.FromSeconds(2)
                : TimeSpan.FromMilliseconds(40),
            OperationTimeout = operationExpires
                ? TimeSpan.FromMilliseconds(40)
                : TimeSpan.FromSeconds(2),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new LateStreamingTimeoutHandler(
                    canceledTransportTimeout),
                options);

        PackageSourceFailure failure = Failed(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                operationExpires
                    ? PackageSourceTimeoutKind.Operation
                    : PackageSourceTimeoutKind.Request,
                operationExpires
                    ? options.OperationTimeout
                    : options.RequestTimeout),
            failure.Timeout);
    }

    [Fact]
    public void GalleryFinalListingProjectionPreservesOperationTimeout()
    {
        var candidate = new PackageCandidateObservation(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            PackageSourceIdentity.NuGetOrg,
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Unknown);
        var partial = new PackageVersionResult(
            new DelayedList<PackageCandidateObservation>(candidate),
            hasAuthoritativeListingState: false);
        var listings =
            new Dictionary<string, PackageListingState>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["1.0.0"] = PackageListingState.Listed,
            };
        using var operation = new NuGetOperationDeadline(
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(1),
                OperationTimeout = TimeSpan.FromMilliseconds(20),
            },
            Timeout.InfiniteTimeSpan,
            TestContext.Current.CancellationToken);

        NuGetOperationTimeoutException error =
            Assert.Throws<NuGetOperationTimeoutException>(
                () => NuGetGalleryPackageSourceClient
                    .ApplyRegistrationListingsOrPartial(
                        partial,
                        listings,
                        operation,
                        TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMilliseconds(20), error.Timeout);
    }

    [Fact]
    public async Task GalleryMissingSymbolsAreTypedAbsence()
    {
        var handler = new RecordingHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.TryGetSymbolsAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal(PackageSourceFailureKind.NotFound, failure.Kind);
        Assert.Equal(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            failure.Coordinate);
        Assert.Equal(
            PackageSourceKind.NuGetGallery,
            failure.TransportKind);
        Assert.Equal(
            PackageSourceCapabilities.SymbolPayload,
            failure.Capability);
        Assert.Equal([GallerySymbols], handler.Requested);
    }

    [Fact]
    public async Task GalleryMissingPackageIsTypedAbsence()
    {
        var handler = new RecordingHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.NotFound, failure.Kind);
        Assert.Equal(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            failure.Coordinate);
        Assert.Equal(
            PackageSourceCapabilities.PackagePayload,
            failure.Capability);
        Assert.Equal([GalleryPackage], handler.Requested);
    }

    [Fact]
    public async Task GalleryMissingManifestIsTypedAbsence()
    {
        var handler = new RecordingHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.GetManifestAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.NotFound, failure.Kind);
        Assert.Equal(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            failure.Coordinate);
        Assert.Equal(
            PackageSourceCapabilities.Manifest,
            failure.Capability);
        Assert.Equal([GalleryManifest], handler.Requested);
    }

    [Fact]
    public async Task GalleryManifestHonorsMetadataBound()
    {
        var handler = new RecordingHandler
        {
            [GalleryManifest] = "<package />",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxManifestResponseBytes = 8,
                });

        PackageSourceFailure failure = Failed(
            await runtime.GetManifestAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            failure.Kind);
        Assert.Equal([GalleryManifest], handler.Requested);
    }

    [Fact]
    public async Task GalleryRejectsInvalidVersionMetadata()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["../1.0.0"]}""",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
        Assert.Equal([GalleryVersions], handler.Requested);
    }

    [Fact]
    public async Task GalleryRejectsNullVersionDocument()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = "null",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
        Assert.Equal([GalleryVersions], handler.Requested);
    }

    [Fact]
    public async Task GalleryClassifiesBoundedMetadataRejection()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = 8,
                });

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            failure.Kind);
        Assert.Equal([GalleryVersions], handler.Requested);
    }

    [Fact]
    public async Task GalleryLateMetadataRejectionIsNotRetriedAsTimeout()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryVersions,
            request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new LateOversizeStream(
                        """{"versions":["1.0.0"]}"""u8.ToArray())),
                RequestMessage = request,
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    MaxMetadataResponseBytes = 8,
                    RequestTimeout = TimeSpan.FromSeconds(1),
                    OperationTimeout = TimeSpan.FromSeconds(2),
                    MetadataBodyTimeout = TimeSpan.FromMilliseconds(40),
                });

        PackageSourceFailure failure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            failure.Kind);
        Assert.Equal([GalleryVersions], handler.Requested);
    }

    [Fact]
    public async Task GalleryMissingPackageHasNoVersions()
    {
        var handler = new RecordingHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));

        Assert.Empty(versions.Candidates);
        Assert.True(versions.HasAuthoritativeListingState);
        Assert.Equal([GalleryVersions], handler.Requested);
    }

    [Fact]
    public void GalleryRejectsCredentials()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetGallery,
                credential:
                    new PackageSourceCredential("user", "token")));

        Assert.Contains("does not accept credentials", error.Message);
    }

    [Fact]
    public void GalleryDescriptorRequiresGalleryFactory()
    {
        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => PackageSourceClientFactory.Create(
                    PackageSourceDescriptor.NuGetGallery));

        Assert.Contains("isolated transport", error.Message);
    }

    [Fact]
    public void RuntimeFactoriesDoNotAcceptSharedHttpClient()
    {
        Assert.DoesNotContain(
            typeof(PackageSourceClientFactory).GetMethods(),
            method => method.IsPublic
                && method.GetParameters().Any(
                    parameter => parameter.ParameterType
                        == typeof(HttpClient)));
    }

    [Fact]
    public void DefaultV3TransportHasNoAmbientCredentialMechanisms()
    {
        using HttpMessageHandler transport =
            PackageSourceClientFactory
                .CreateV3TransportHandler(
                    new Uri(ServiceIndex),
                    isBrowser: false);
        SocketsHttpHandler handler =
            Assert.IsType<SocketsHttpHandler>(transport);

        Assert.False(handler.UseCookies);
        Assert.False(handler.PreAuthenticate);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.Credentials);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public void BrowserV3TransportAvoidsUnsupportedHandlerConfiguration()
    {
        using HttpClientHandler handler =
            PackageSourceClientFactory
                .CreateCredentialFreeTransportHandler(
                    isBrowser: true);

        Assert.True(handler.UseCookies);
        Assert.True(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task DefaultV3TransportBlocksPrivateCrossOriginSearchEndpoint()
    {
        using var sourceListener =
            new TcpListener(IPAddress.Loopback, 0);
        using var targetListener =
            new TcpListener(IPAddress.Loopback, 0);
        sourceListener.Start();
        targetListener.Start();
        int sourcePort =
            ((IPEndPoint)sourceListener.LocalEndpoint).Port;
        int targetPort =
            ((IPEndPoint)targetListener.LocalEndpoint).Port;
        string sourceUrl =
            $"http://127.0.0.1:{sourcePort}/index.json";
        string targetUrl =
            $"http://127.0.0.1:{targetPort}/private";
        string serviceIndex = $$"""
            {
              "resources": [
                {
                  "@id": "{{targetUrl}}",
                  "@type": "SearchQueryService/3.5.0"
                }
              ]
            }
            """;

        Task sourceServer = ServeHttpResponseAsync(
            sourceListener,
            serviceIndex,
            TestContext.Current.CancellationToken);
        using var targetCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        Task<bool> targetServer = Task.Run(
            async () =>
            {
                try
                {
                    await ServeHttpResponseAsync(
                        targetListener,
                        """{"data":[]}""",
                        targetCancellation.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            },
            CancellationToken.None);

        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("private", sourceUrl));
        PackageSourceOperationResult<PackageSearchResult> result =
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        targetCancellation.Cancel();
        await sourceServer;
        bool targetReached = await targetServer;
        var failure = Assert.IsType<
            PackageSourceOperationResult<PackageSearchResult>.Failed>(
                result);
        Assert.Equal(
            PackageSourceFailureKind.Transport,
            failure.Failure.Kind);
        Assert.False(targetReached);
    }

    [Fact]
    public async Task DefaultV3TransportBlocksPrivateCrossOriginVersionAndPackageResources()
    {
        using var sourceListener =
            new TcpListener(IPAddress.Loopback, 0);
        using var targetListener =
            new TcpListener(IPAddress.Loopback, 0);
        sourceListener.Start();
        targetListener.Start();
        int sourcePort =
            ((IPEndPoint)sourceListener.LocalEndpoint).Port;
        int targetPort =
            ((IPEndPoint)targetListener.LocalEndpoint).Port;
        string sourceUrl =
            $"http://127.0.0.1:{sourcePort}/index.json";
        string targetUrl =
            $"http://127.0.0.1:{targetPort}/flat/";
        string serviceIndex = $$"""
            {
              "version": "3.0.0",
              "resources": [
                {
                  "@id": "{{targetUrl}}",
                  "@type": "PackageBaseAddress/3.0.0"
                }
              ]
            }
            """;

        Task sourceServer = ServeHttpResponseAsync(
            sourceListener,
            serviceIndex,
            TestContext.Current.CancellationToken);
        using var targetCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        Task<bool> targetServer = Task.Run(
            async () =>
            {
                try
                {
                    await ServeHttpResponseAsync(
                        targetListener,
                        """{"versions":["1.0.0"]}""",
                        targetCancellation.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            },
            CancellationToken.None);

        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("private", sourceUrl));
        PackageSourceFailure versionFailure = Failed(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));
        PackageSourceFailure packageFailure = Failed(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        targetCancellation.Cancel();
        await sourceServer;
        bool targetReached = await targetServer;
        Assert.Equal(
            PackageSourceFailureKind.Transport,
            versionFailure.Kind);
        Assert.Equal(
            PackageSourceFailureKind.Transport,
            packageFailure.Kind);
        Assert.False(targetReached);
    }

    [Fact]
    public async Task DefaultV3TransportAllowsConfiguredPrivateIpv6Source()
    {
        Assert.True(Socket.OSSupportsIPv6);
        using var sourceListener =
            new TcpListener(IPAddress.IPv6Loopback, 0);
        sourceListener.Start();
        int sourcePort =
            ((IPEndPoint)sourceListener.LocalEndpoint).Port;
        string sourceUrl =
            $"http://[::1]:{sourcePort}/index.json";
        string searchUrl =
            $"http://[::1]:{sourcePort}/query";
        string serviceIndex = $$"""
            {
              "resources": [
                {
                  "@id": "{{searchUrl}}",
                  "@type": "SearchQueryService/3.5.0"
                }
              ]
            }
            """;
        Task sourceServer = ServeHttpResponsesAsync(
            sourceListener,
            [serviceIndex, """{"data":[]}"""],
            TestContext.Current.CancellationToken);

        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("ipv6-private", sourceUrl));
        PackageSearchResult result = Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        await sourceServer;
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task DefaultV3TransportNormalizesPathlessServiceIndexRoot()
    {
        using var sourceListener =
            new TcpListener(IPAddress.Loopback, 0);
        sourceListener.Start();
        int sourcePort =
            ((IPEndPoint)sourceListener.LocalEndpoint).Port;
        string sourceUrl =
            $"http://127.0.0.1:{sourcePort}";
        string searchUrl =
            $"http://127.0.0.1:{sourcePort}/query";
        string serviceIndex = $$"""
            {
              "resources": [
                {
                  "@id": "{{searchUrl}}",
                  "@type": "SearchQueryService/3.5.0"
                }
              ]
            }
            """;
        Task<IReadOnlyList<string>> sourceServer =
            ServeHttpResponsesAsync(
                sourceListener,
                [serviceIndex, """{"data":[]}"""],
                TestContext.Current.CancellationToken);

        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("pathless", sourceUrl));
        PackageSearchResult result = Succeeded(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        IReadOnlyList<string> requestLines = await sourceServer;
        Assert.Empty(result.Matches);
        Assert.Equal(
            [
                "GET / HTTP/1.1",
                "GET /query?q=contoso&skip=0&take=20&prerelease=false&semVerLevel=2.0.0 HTTP/1.1",
            ],
            requestLines);
    }

    [Fact]
    public async Task DefaultV3VersionAndPackagePreserveSignedServiceIndexBytes()
    {
        using var sourceListener =
            new TcpListener(IPAddress.Loopback, 0);
        sourceListener.Start();
        int sourcePort =
            ((IPEndPoint)sourceListener.LocalEndpoint).Port;
        string sourceUrl =
            $"http://127.0.0.1:{sourcePort}/%69ndex.json?s%69g=%73ervice";
        string flatContainer =
            $"http://127.0.0.1:{sourcePort}/flat/";
        string serviceIndex = $$"""
            {
              "version": "3.0.0",
              "resources": [
                {
                  "@id": "{{flatContainer}}",
                  "@type": "PackageBaseAddress/3.0.0"
                }
              ]
            }
            """;
        Task<IReadOnlyList<string>> sourceServer =
            ServeHttpResponsesAsync(
                sourceListener,
                [
                    serviceIndex,
                    """{"versions":["1.0.0"]}""",
                    serviceIndex,
                    "package bytes",
                ],
                TestContext.Current.CancellationToken);

        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("signed-index", sourceUrl));
        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;
        using var reader = new StreamReader(content);

        Assert.Single(versions.Candidates);
        Assert.Equal(
            "package bytes",
            await reader.ReadToEndAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            [
                "GET /%69ndex.json?s%69g=%73ervice HTTP/1.1",
                "GET /flat/contoso/index.json HTTP/1.1",
                "GET /%69ndex.json?s%69g=%73ervice HTTP/1.1",
                "GET /flat/contoso/1.0.0/contoso.1.0.0.nupkg HTTP/1.1",
            ],
            await sourceServer);
    }

    [Fact]
    public void BrowserNuGetRequestsOmitAmbientCredentials()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ServiceIndex);
        NuGetHttpRequest.ConfigureBrowserRequest(
            request,
            isBrowser: true);
        var fetchOptionsKey = new HttpRequestOptionsKey<
            IDictionary<string, object>>("WebAssemblyFetchOptions");

        Assert.True(
            request.Options.TryGetValue(
                fetchOptionsKey,
                out IDictionary<string, object>? options));
        Assert.Equal("omit", options["credentials"]);
        Assert.Equal("error", options["redirect"]);
    }

    [Fact]
    public void BrowserV3ResourcesRequireSameOrigin()
    {
        Assert.Null(
            NuGetSourceRequest.CredentialForEndpoint(
                ServiceIndex,
                SearchEndpoint,
                credential: null,
                isBrowser: true));

        NuGetSourceResponseException error =
            Assert.Throws<NuGetSourceResponseException>(
                () => NuGetSourceRequest.CredentialForEndpoint(
                    ServiceIndex,
                    "https://cdn.example/query",
                    credential: null,
                    isBrowser: true));
        Assert.Contains("cross-origin resource", error.Message);
    }

    [Theory]
    [InlineData(
        HttpStatusCode.MultipleChoices,
        "https://feed.example/redirected",
        "user:token")]
    [InlineData(
        HttpStatusCode.Found,
        "https://feed.example/redirected",
        "user:token")]
    [InlineData(
        HttpStatusCode.Found,
        "https://cdn.example/redirected",
        null)]
    public async Task DesktopRedirectsScopeAuthorizationToOriginalOrigin(
        HttpStatusCode redirectStatus,
        string redirectTarget,
        string? expectedRedirectAuthorization)
    {
        var transport = new RedirectRecordingHandler(
            redirectStatus,
            redirectTarget);
        using var client = new HttpClient(
            new NuGetCredentialRedirectHandler(transport));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ServiceIndex);
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes("user:token")));

        using HttpResponseMessage response =
            await client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["user:token", expectedRedirectAuthorization],
            transport.Authorization);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public async Task DesktopRedirectLimitAllowsFiveAndRejectsSix(
        int redirects,
        bool rejected)
    {
        using var client = new HttpClient(
            new NuGetCredentialRedirectHandler(
                new RedirectChainHandler(redirects)));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ServiceIndex);

        if (!rejected)
        {
            using HttpResponseMessage response =
                await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return;
        }

        await Assert.ThrowsAsync<NuGetRedirectLimitExceededException>(
            () => client.SendAsync(
                request,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RedirectLimitIsResponseRejected()
    {
        using var client = new HttpClient(
            new NuGetCredentialRedirectHandler(
                new RedirectChainHandler(redirects: 6)));

        PackageSourceFailure failure = Failed(
            await PackageSourceOperation.CaptureAsync(
                PackageSourceIdentity.NuGetOrg,
                PackageSourceKind.NuGetV3,
                PackageSourceCapabilities.VersionEnumeration,
                async () =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        ServiceIndex);
                    using HttpResponseMessage response =
                        await client.SendAsync(
                            request,
                            TestContext.Current.CancellationToken);
                    return response.StatusCode;
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.ResponseRejected,
            failure.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://feed.example/path%")]
    [InlineData("https://\u200D.example/next")]
    [InlineData("https://user:secret@feed.example/next")]
    public async Task MalformedRedirectTargetIsInvalidResponse(
        string redirectTarget)
    {
        var transport = new RawRedirectHandler(
            redirectTarget);
        using var client = new HttpClient(
            new NuGetCredentialRedirectHandler(transport));

        PackageSourceFailure failure = Failed(
            await PackageSourceOperation.CaptureAsync(
                PackageSourceIdentity.NuGetOrg,
                PackageSourceKind.NuGetV3,
                PackageSourceCapabilities.VersionEnumeration,
                async () =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        ServiceIndex);
                    using HttpResponseMessage response =
                        await client.SendAsync(
                            request,
                            TestContext.Current.CancellationToken);
                    return response.StatusCode;
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceFailureKind.InvalidResponse,
            failure.Kind);
        Assert.Equal(1, transport.Requests);
    }

    [Fact]
    public void V3OwnedTransportIsDisposedWithClient()
    {
        var handler = new RecordingHandler();
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                handler);

        runtime.Dispose();

        Assert.True(handler.Disposed);
    }

    [Fact]
    public void V3OwnedTransportLeavesLibraryDeadlinesAuthoritative()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMinutes(5),
            OperationTimeout = TimeSpan.FromMinutes(10),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                new RecordingHandler(),
                options);
        NuGetV3PackageSourceClient v3 =
            Assert.IsType<NuGetV3PackageSourceClient>(runtime);

        Assert.Equal(Timeout.InfiniteTimeSpan, v3.TransportTimeout);
        Assert.Equal(
            options.RequestTimeout,
            NuGetFetchOptions.RequestTimeoutForClient(
                options,
                v3.TransportTimeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateProjectionRemainsInsideOperationDeadline(
        bool search)
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromMilliseconds(20),
        };
        using var operation = new NuGetOperationDeadline(
            options,
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);

        Assert.Throws<NuGetOperationTimeoutException>(
            () =>
            {
                if (search)
                {
                    PackageSourceProjection.ProjectSearch(
                        new DelayedList<SearchResult>(
                            new SearchResult("contoso", "1.0.0")),
                        PackageSourceIdentity.NuGetOrg,
                        operation);
                }
                else
                {
                    PackageSourceProjection.ProjectVersions(
                        "contoso",
                        new DelayedList<string>("1.0.0"),
                        PackageSourceIdentity.NuGetOrg,
                        PackageDiscoveryContract.CompleteVersionEnumeration,
                        PackageListingState.Unknown,
                        hasAuthoritativeListingState: false,
                        operation);
                }
            });
    }

    [Fact]
    public void GalleryOwnedTransportIsDisposedWithClient()
    {
        var handler = new RecordingHandler();
        IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        runtime.Dispose();

        Assert.True(handler.Disposed);
    }

    [Fact]
    public void GalleryOwnedTransportLeavesLibraryDeadlinesAuthoritative()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMinutes(5),
            OperationTimeout = TimeSpan.FromMinutes(10),
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new RecordingHandler(),
                options);
        NuGetGalleryPackageSourceClient gallery =
            Assert.IsType<NuGetGalleryPackageSourceClient>(runtime);

        Assert.Equal(Timeout.InfiniteTimeSpan, gallery.TransportTimeout);
        Assert.Equal(
            options.RequestTimeout,
            NuGetFetchOptions.RequestTimeoutForClient(
                options,
                gallery.TransportTimeout));
    }

    [Fact]
    public void GalleryDesktopTransportDecompressesSemVer2Registration()
    {
        using HttpClientHandler handler =
            PackageSourceClientFactory.CreateGalleryTransportHandler(
                isBrowser: false);

        Assert.Equal(
            DecompressionMethods.All,
            handler.AutomaticDecompression);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseDefaultCredentials);
        Assert.False(handler.PreAuthenticate);
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void GalleryBrowserTransportAvoidsUnsupportedHandlerConfiguration()
    {
        using HttpClientHandler handler =
            PackageSourceClientFactory.CreateGalleryTransportHandler(
                isBrowser: true);

        Assert.Equal(
            DecompressionMethods.None,
            handler.AutomaticDecompression);
        Assert.True(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task GalleryDesktopTransportFollowsSourceOwnedRedirects()
    {
        var handler = new GalleryRedirectHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await payload.Content.DisposeAsync();

        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task GalleryEscapesUnicodePackageIdsAsOneSegment()
    {
        const string versions =
            "https://globalcdn.nuget.org/v3-flatcontainer/caf%C3%A9/index.json";
        var handler = new RecordingHandler
        {
            [versions] = """{"versions":["1.0.0"]}""",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageCandidateObservation candidate = Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "Caf\u00E9",
                    TestContext.Current.CancellationToken))
                .Candidates);
        Assert.Equal("caf\u00E9", candidate.Coordinate.PackageId);
        Assert.Equal("1.0.0", candidate.Coordinate.Version);
        Assert.Equal(
            [
                versions,
                "https://globalcdn.nuget.org/v3/registration5-gz-semver2/caf%C3%A9/index.json",
            ],
            handler.Requested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryRequestsUseLibraryDeadlines(bool payload)
    {
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new StallingHandler(),
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(50),
                    // If both bounds elapse, Operation correctly wins.
                    OperationTimeout = TimeSpan.FromSeconds(30),
                });

        PackageSourceFailure failure = payload
            ? Failed(
                await runtime.GetPackageAsync(
                    "contoso",
                    "1.0.0",
                    TestContext.Current.CancellationToken))
            : Failed(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(
            payload
                ? PackageSourceCapabilities.PackagePayload
                : PackageSourceCapabilities.VersionEnumeration,
            failure.Capability);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                TimeSpan.FromMilliseconds(50)),
            failure.Timeout);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("versions")]
    [InlineData("manifest")]
    [InlineData("package")]
    public async Task GalleryRetriesTransientFailuresWithinOneOperation(
        string operation)
    {
        var handler = new TransientGalleryHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        switch (operation)
        {
            case "search":
                Assert.Single(
                    Succeeded(
                        await runtime.SearchAsync(
                            "contoso",
                            cancellationToken:
                                TestContext.Current.CancellationToken))
                    .Matches);
                break;
            case "versions":
                Assert.Single(
                    Succeeded(
                        await runtime.GetVersionsAsync(
                            "contoso",
                            TestContext.Current.CancellationToken))
                    .Candidates);
                break;
            case "manifest":
                Succeeded(
                    await runtime.GetManifestAsync(
                        "contoso",
                        "1.0.0",
                        TestContext.Current.CancellationToken));
                break;
            case "package":
                PackageSourcePayload payload = Succeeded(
                    await runtime.GetPackageAsync(
                        "contoso",
                        "1.0.0",
                        TestContext.Current.CancellationToken));
                await payload.Content.DisposeAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(operation));
        }

        Assert.Equal(2, handler.PrimaryRequests);
    }

    [Fact]
    public async Task GalleryRetriesBrowserStatuslessTransportFailure()
    {
        var handler = new TransientGalleryHandler(statuslessFailure: true);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken))
            .Candidates);

        Assert.Equal(2, handler.PrimaryRequests);
    }

    [Fact]
    public async Task GalleryRetryBackoffUsesOperationNotRequestTimeout()
    {
        var handler = new TransientGalleryHandler();
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                handler,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(50),
                    OperationTimeout = TimeSpan.FromSeconds(1),
                });

        Assert.Single(
            Succeeded(
                await runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken))
            .Candidates);
        Assert.Equal(2, handler.PrimaryRequests);
    }

    [Fact]
    public async Task GalleryCallerCancellationRemainsCancellation()
    {
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(
                new StallingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.GetVersionsAsync(
                "contoso",
                cancellation.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, PackageSourceFailureKind.AuthenticationRequired)]
    [InlineData(HttpStatusCode.Forbidden, PackageSourceFailureKind.AuthenticationRequired)]
    [InlineData(HttpStatusCode.BadGateway, PackageSourceFailureKind.Transport)]
    public async Task GalleryClassifiesHttpFailures(
        HttpStatusCode statusCode,
        PackageSourceFailureKind expected)
    {
        var handler = new RecordingHandler();
        handler.SetStatus(GalleryPackage, statusCode);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        PackageSourceFailure failure = Failed(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        Assert.Equal(expected, failure.Kind);
        Assert.Equal(runtime.Identity, failure.Producer);
        Assert.DoesNotContain(
            GalleryPackage,
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadTransportFailureRetainsSafeSourceIdentity()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadTransportFailureOutranksRacingReadCancellation()
    {
        using var readCancellation = new CancellationTokenSource();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingPayloadStream(readCancellation.Cancel)),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    readCancellation.Token).AsTask());

        Assert.True(readCancellation.IsCancellationRequested);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
    }

    [Fact]
    public async Task PayloadCallerCancellationDoesNotRetainTransportFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingPayloadStream(cancellation.Cancel)),
            });
        using var operation = new NuGetOperationContext(cancellation.Token);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                cancellationToken: cancellation.Token,
                operationContext: operation));
        await using Stream content = payload.Content;

        OperationCanceledException error =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => content.ReadAsync(
                    new byte[1],
                    cancellation.Token).AsTask());

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadCanceledTransportTimeoutRetainsSafeSourceIdentity(
        bool readAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new CanceledTimeoutPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error;
        if (readAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                () => content.ReadByte());
        }

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadCanceledTransportTimeoutDuringDisposalRetainsSafeSourceIdentity(
        bool disposeAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new CanceledTimeoutDisposePayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        PackageSourceStreamException error;
        if (disposeAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => payload.Content.DisposeAsync().AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                payload.Content.Dispose);
        }

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadDisposalFailureRetainsSafeSourceIdentity()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingDisposePayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        PackageSourceStreamException error =
            Assert.Throws<PackageSourceStreamException>(
                payload.Content.Dispose);

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadAsyncDisposalFailureRetainsSafeSourceIdentity()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingDisposePayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => payload.Content.DisposeAsync().AsTask());

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.Timeout);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadConcurrentDisposalTranslatesOutstandingRead(
        bool disposeAsync)
    {
        var inner = new DisposalUnblocksPayloadStream();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(inner),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;
        Task<int> read = content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken)
            .AsTask();
        await inner.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        if (disposeAsync)
            await content.DisposeAsync();
        else
            content.Dispose();

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);
        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadConcurrentDisposalEofTranslatesOutstandingRead(
        bool disposeAsync)
    {
        var inner = new DisposalReturnsEofPayloadStream();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(inner),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;
        Task<int> read = content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken)
            .AsTask();
        await inner.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        if (disposeAsync)
            await content.DisposeAsync();
        else
            content.Dispose();

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);
        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadConcurrentDisposalTranslatesSynchronousEof(
        bool disposeAsync)
    {
        var inner = new DisposalReturnsEofPayloadStream();
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(inner),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;
        Task<int> read = Task.Run(
            () => content.Read(new byte[1], 0, 1),
            TestContext.Current.CancellationToken);
        await inner.ReadStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        if (disposeAsync)
            await content.DisposeAsync();
        else
            content.Dispose();

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);
        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadObjectDisposedFailureRetainsSafeSourceIdentity(
        bool readAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ObjectDisposedPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error;
        if (readAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                () => content.ReadByte());
        }

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadObjectDisposedFailurePreservesRequestDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new LateObjectDisposedPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayloadInvalidDataFailureRetainsSafeSourceIdentity(
        bool readAsync)
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new InvalidDataPayloadStream(delay: false)),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error;
        if (readAsync)
        {
            error = await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());
        }
        else
        {
            error = Assert.Throws<PackageSourceStreamException>(
                () => content.ReadByte());
        }

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Transport, error.Kind);
        Assert.Null(error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadInvalidDataFailurePreservesRequestDeadline()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new InvalidDataPayloadStream(delay: true)),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PayloadReadAfterDisposalRemainsObjectDisposed()
    {
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1]),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        payload.Content.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => payload.Content.ReadByte());
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => payload.Content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task PayloadTimeoutRetainsSourceAndConfiguredDuration()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(40),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new StallingPayloadStream()),
            });
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));
        await using Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(runtime.Kind, error.TransportKind);
        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.False(error.CleanupFailed);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task PayloadTimeoutRetainsCleanupFailureWithoutInnerException()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(40),
            OperationTimeout = TimeSpan.FromSeconds(1),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new ThrowingDisposeStallingPayloadStream()),
            });
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Stream content = payload.Content;

        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => content.ReadAsync(
                    new byte[1],
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Request,
                options.RequestTimeout),
            error.Timeout);
        Assert.True(error.CleanupFailed);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(
            "secret.example",
            error.Message,
            StringComparison.Ordinal);
        _ = await Assert.ThrowsAsync<PackageSourceStreamException>(
            () => content.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task DisposingSharedContextCancelsOutstandingPayloadRead()
    {
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(5),
            OperationTimeout = TimeSpan.FromSeconds(10),
        };
        var handler = new RecordingHandler();
        handler.SetResponse(
            GalleryPackage,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new StallingPayloadStream()),
            });
        using var operation = new NuGetOperationContext(
            options.RequestTimeout,
            options.OperationTimeout,
            TestContext.Current.CancellationToken);
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler, options);
        PackageSourcePayload payload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                cancellationToken:
                    TestContext.Current.CancellationToken,
                operationContext: operation));
        await using Stream content = payload.Content;

        Task<int> read = content.ReadAsync(
                new byte[1],
                TestContext.Current.CancellationToken)
            .AsTask();
        operation.Dispose();
        PackageSourceStreamException error =
            await Assert.ThrowsAsync<PackageSourceStreamException>(
                () => read);

        Assert.Equal(PackageSourceFailureKind.Timeout, error.Kind);
        Assert.Equal(
            new PackageSourceTimeout(
                PackageSourceTimeoutKind.Operation,
                options.OperationTimeout),
            error.Timeout);
    }

    [Fact]
    public void LegacyLocalSourceRemainsAnExplicitUnsupportedKind()
    {
        var source = new PackageSource(
            "local",
            Path.GetFullPath("packages"));

        PackageSourceClientUnavailableException error =
            Assert.Throws<PackageSourceClientUnavailableException>(
                () => PackageSourceClientFactory.Create(source));

        Assert.Equal(PackageSourceKind.LocalFolder, error.Kind);
    }

    private static string? DecodeBasic(string? parameter) =>
        parameter is null
            ? null
            : Encoding.UTF8.GetString(
                Convert.FromBase64String(parameter));

    private static async Task<string> ReadAsync(Stream stream)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        return await reader.ReadToEndAsync(
            TestContext.Current.CancellationToken);
    }

    private static T Succeeded<T>(
        PackageSourceOperationResult<T> result) =>
        Assert.IsType<PackageSourceOperationResult<T>.Succeeded>(result)
            .Value;

    private static PackageSourceFailure Failed<T>(
        PackageSourceOperationResult<T> result) =>
        Assert.IsType<PackageSourceOperationResult<T>.Failed>(result)
            .Failure;

    private static TaskCanceledException CreateCanceledTransportTimeout() =>
        new(
            "Simulated canceled transport timeout from "
            + "https://secret.example/package.",
            new TimeoutException("Simulated transport timeout."),
            CancellationToken.None);

    private sealed class DelayedList<T>(T value) : IReadOnlyList<T>
    {
        public int Count => 1;

        public T this[int index]
        {
            get
            {
                Assert.Equal(0, index);
                Thread.Sleep(100);
                return value;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            yield return this[0];
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static NuGetOperationDeadline CreateRegistrationParserOperation(
        CancellationToken cancellationToken) =>
        new(
            new NuGetFetchOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5),
                OperationTimeout = TimeSpan.FromSeconds(5),
            },
            Timeout.InfiniteTimeSpan,
            cancellationToken);

    private static string RegistrationItems(int count) =>
        string.Join(
            ",",
            Enumerable.Range(1, count)
                .Select(version =>
                    $$"""
                      {
                        "catalogEntry": {
                          "version": "{{version}}.0.0"
                        }
                      }
                      """));

    private static async Task DeserializeRegistrationItemsAsync(
        string items,
        bool inline,
        IReadOnlySet<string> candidates,
        NuGetGalleryRegistrationBudget budget,
        NuGetOperationDeadline operation,
        CancellationToken cancellationToken)
    {
        string json = inline
            ? $$"""{"items":[{"items":[{{items}}]}]}"""
            : $$"""{"items":[{{items}}]}""";
        using var stream =
            new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (inline)
        {
            await NuGetGalleryRegistration.DeserializeIndexAsync(
                stream,
                candidates,
                budget,
                operation,
                cancellationToken);
        }
        else
        {
            await NuGetGalleryRegistration.DeserializePageAsync(
                stream,
                candidates,
                budget,
                operation,
                cancellationToken);
        }
    }

    private sealed class InterruptingReadOnlySet
        : HashSet<string>, IReadOnlySet<string>
    {
        private readonly Action _interrupt;
        private int _containsCalls;

        public InterruptingReadOnlySet(int count, Action interrupt)
            : base(
                Enumerable.Range(1, count)
                    .Select(version => $"{version}.0.0"),
                StringComparer.OrdinalIgnoreCase)
        {
            _interrupt = interrupt;
        }

        public int ContainsCalls => _containsCalls;

        bool IReadOnlySet<string>.Contains(string item)
        {
            if (Interlocked.Increment(ref _containsCalls) == 1)
                _interrupt();
            return Contains(item);
        }
    }

    private sealed class RedirectRecordingHandler(
        HttpStatusCode redirectStatus,
        string redirectTarget)
        : HttpMessageHandler
    {
        public List<string?> Authorization { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Authorization.Add(
                DecodeBasic(
                    request.Headers.Authorization?.Parameter));
            if (Authorization.Count == 1)
            {
                var redirect = new HttpResponseMessage(
                    redirectStatus);
                redirect.Headers.Location =
                    new Uri(redirectTarget);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class RedirectChainHandler(int redirects)
        : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_requests++ < redirects)
            {
                var redirect = new HttpResponseMessage(
                    HttpStatusCode.Found);
                redirect.Headers.Location =
                    new Uri($"/redirect-{_requests}", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class GalleryRedirectHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            if (Requests == 1)
            {
                var redirect =
                    new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location =
                    new Uri("/redirected", UriKind.Relative);
                return Task.FromResult(redirect);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3]),
                });
        }
    }

    private sealed class RawRedirectHandler(string location)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            var redirect = new HttpResponseMessage(
                HttpStatusCode.Found);
            redirect.Headers.TryAddWithoutValidation(
                "Location",
                location);
            return Task.FromResult(redirect);
        }
    }

    private static async Task<string> ServeHttpResponseAsync(
        TcpListener listener,
        string body,
        CancellationToken cancellationToken)
    {
        using TcpClient connection =
            await listener.AcceptTcpClientAsync(cancellationToken);
        await using NetworkStream stream = connection.GetStream();
        var request = new byte[4096];
        int requestLength = 0;
        while (requestLength < request.Length)
        {
            int read = await stream.ReadAsync(
                request.AsMemory(requestLength),
                cancellationToken);
            if (read == 0)
                break;

            requestLength += read;
            if (request.AsSpan(0, requestLength).IndexOf(
                    "\r\n\r\n"u8) >= 0)
            {
                break;
            }
        }

        string requestText =
            Encoding.ASCII.GetString(request, 0, requestLength);
        string requestLine = requestText.Split(
            "\r\n",
            StringSplitOptions.None)[0];
        byte[] content = Encoding.UTF8.GetBytes(body);
        byte[] headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Content-Type: application/json\r\n"
            + $"Content-Length: {content.Length}\r\n"
            + "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
        return requestLine;
    }

    private static async Task<IReadOnlyList<string>> ServeHttpResponsesAsync(
        TcpListener listener,
        IReadOnlyList<string> bodies,
        CancellationToken cancellationToken)
    {
        var requestLines = new List<string>(bodies.Count);
        foreach (string body in bodies)
        {
            requestLines.Add(
                await ServeHttpResponseAsync(
                    listener,
                    body,
                    cancellationToken));
        }

        return requestLines;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HttpStatusCode> _statuses =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            Func<HttpRequestMessage, HttpResponseMessage>> _responses =
            new(StringComparer.OrdinalIgnoreCase);

        public string this[string url] { set => _routes[url] = value; }

        public void SetStatus(string url, HttpStatusCode statusCode) =>
            _statuses[url] = statusCode;

        public void SetResponse(
            string url,
            Func<HttpRequestMessage, HttpResponseMessage> response) =>
            _responses[url] = response;

        public List<string> Requested { get; } = [];
        public List<string?> Authentication { get; } = [];
        public List<IReadOnlyDictionary<string, string[]>> Headers { get; } =
            [];
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            Authentication.Add(
                request.Headers.Authorization?.Parameter);
            Headers.Add(
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase));
            if (_responses.TryGetValue(
                    url,
                    out Func<HttpRequestMessage, HttpResponseMessage>? response))
            {
                return Task.FromResult(response(request));
            }

            string? route = _routes.Keys.FirstOrDefault(
                candidate => url.Equals(
                        candidate,
                        StringComparison.OrdinalIgnoreCase)
                    || candidate == GallerySearch
                        && url.StartsWith(
                            GallerySearch + "?",
                            StringComparison.OrdinalIgnoreCase));
            bool hasResponse = route is not null;
            HttpStatusCode status = _statuses.GetValueOrDefault(
                url,
                hasResponse
                    ? HttpStatusCode.OK
                    : HttpStatusCode.NotFound);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    hasResponse
                        ? _routes.GetValueOrDefault(route!)!
                        : ""),
                RequestMessage = request,
            });
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StallingHandler : HttpMessageHandler
    {
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class CanceledSearchTransportTimeoutHandler
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == ServiceIndex)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""
                        {
                          "resources": [
                            {
                              "@id": "{{SearchEndpoint}}",
                              "@type": "SearchQueryService/3.5.0"
                            }
                          ]
                        }
                        """),
                };
            }

            await Task.Yield();
            throw CreateCanceledTransportTimeout();
        }
    }

    private sealed class StallingMetadataBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(
                        new StallingMetadataBodyStream()),
                });
    }

    private sealed class StallingMetadataBodyStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingPayloadStream(Action? beforeThrow = null)
        : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            beforeThrow?.Invoke();
            throw new IOException(
                "Transport failed at https://secret.example/package.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            beforeThrow?.Invoke();
            return ValueTask.FromException<int>(
                new IOException(
                    "Transport failed at https://secret.example/package."));
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class CanceledTimeoutPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw CreateCanceledTransportTimeout();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                CreateCanceledTransportTimeout());

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class CanceledTimeoutDisposePayloadStream
        : MemoryStream
    {
        public CanceledTimeoutDisposePayloadStream()
            : base([1], writable: false)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw CreateCanceledTransportTimeout();
        }
    }

    private sealed class LateObjectDisposedPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw new ObjectDisposedException(
                "https://secret.example/package");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class InvalidDataPayloadStream(bool delay) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw CreateFailure();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (delay)
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw CreateFailure();
        }

        private static InvalidDataException CreateFailure() =>
            new(
                "Simulated invalid payload from "
                + "https://secret.example/package.");

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class StallingPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingDisposePayloadStream
        : MemoryStream
    {
        public ThrowingDisposePayloadStream()
            : base([1], writable: false)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw new IOException(
                "Cleanup failed for https://secret.example/package.");
        }
    }

    private sealed class ThrowingDisposeStallingPayloadStream
        : Stream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await _disposed.Task;
            throw new ObjectDisposedException(
                nameof(ThrowingDisposeStallingPayloadStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.TrySetResult();
            base.Dispose(disposing);
            throw new IOException(
                "Cleanup failed for https://secret.example/package.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class DisposalUnblocksPayloadStream : Stream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _disposed.Task;
            throw new ObjectDisposedException(
                nameof(DisposalUnblocksPayloadStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class DisposalReturnsEofPayloadStream : Stream
    {
        private readonly TaskCompletionSource _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            ReadStarted.TrySetResult();
            _disposed.Task.GetAwaiter().GetResult();
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _disposed.Task;
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ObjectDisposedPayloadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new ObjectDisposedException(
                "https://secret.example/package");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new ObjectDisposedException(
                    "https://secret.example/package"));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingEofStream : Stream
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return 0;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ConcurrentRegistrationHandler : HttpMessageHandler
    {
        private const int ExpectedBatchSize = 8;
        private readonly TaskCompletionSource _pageRequestsMayComplete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activePageRequests;
        private int _maxActivePageRequests;
        private int _pageRequests;

        public int MaxActivePageRequests => _maxActivePageRequests;
        public int PageRequests => _pageRequests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == GalleryVersions)
            {
                return Response(
                    """{"versions":["1.0.0","2.0.0","3.0.0","4.0.0","5.0.0","6.0.0","7.0.0","8.0.0","9.0.0"]}""");
            }

            if (url == GalleryRegistration)
            {
                string pages = string.Join(
                    ",",
                    Enumerable.Range(1, 9).Select(version =>
                        $$"""
                          {
                            "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/{{version}}.0.0/{{version}}.0.0.json"
                          }
                          """));
                return Response($$"""{"items":[{{pages}}]}""");
            }

            int active = Interlocked.Increment(
                ref _activePageRequests);
            UpdateMaximum(active);
            Interlocked.Increment(ref _pageRequests);
            if (active == 1)
            {
                _ = ReleasePageRequestsAsync(
                    TimeSpan.FromMilliseconds(200));
            }

            if (active == ExpectedBatchSize)
            {
                _ = ReleasePageRequestsAsync(
                    TimeSpan.FromMilliseconds(50));
            }

            try
            {
                await _pageRequestsMayComplete.Task.WaitAsync(
                    cancellationToken);
                string version =
                    request.RequestUri.Segments[^2].TrimEnd('/');
                return Response(
                    $$"""
                      {
                        "items": [
                          {
                            "catalogEntry": {
                              "version": "{{version}}"
                            }
                          }
                        ]
                      }
                      """);
            }
            finally
            {
                Interlocked.Decrement(ref _activePageRequests);
            }
        }

        private async Task ReleasePageRequestsAsync(TimeSpan delay)
        {
            await Task.Delay(delay);
            _pageRequestsMayComplete.TrySetResult();
        }

        private void UpdateMaximum(int active)
        {
            int observed;
            do
            {
                observed = _maxActivePageRequests;
                if (observed >= active)
                    return;
            }
            while (Interlocked.CompareExchange(
                ref _maxActivePageRequests,
                active,
                observed) != observed);
        }

        private static HttpResponseMessage Response(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
    }

    private sealed class LateMalformedRegistrationHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent content =
                request.RequestUri!.AbsoluteUri == GalleryVersions
                    ? new StringContent(
                        """{"versions":["1.0.0"]}""")
                    : new StreamContent(
                        new LateMalformedRegistrationStream());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
        }
    }

    private sealed class LateMalformedRegistrationStream : Stream
    {
        private bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_sent)
                return 0;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            buffer.Span[0] = (byte)'{';
            _sent = true;
            return 1;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class LateInvalidDataRegistrationHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent content =
                request.RequestUri!.AbsoluteUri == GalleryVersions
                    ? new StringContent(
                        """{"versions":["1.0.0"]}""")
                    : new StreamContent(
                        new LateInvalidDataRegistrationStream());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
        }
    }

    private sealed class LateInvalidDataRegistrationStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            throw new InvalidDataException(
                "Simulated late invalid registration data.");
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class LateStreamingTimeoutHandler(
        bool canceledTransportTimeout) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            if (canceledTransportTimeout)
                throw CreateCanceledTransportTimeout();

            throw new TimeoutException(
                "Simulated late streaming transport timeout.");
        }
    }

    private sealed class CancelableRegistrationHandler : HttpMessageHandler
    {
        public TaskCompletionSource RegistrationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == GalleryVersions)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content =
                        new StringContent("""{"versions":["1.0.0"]}"""),
                };
            }

            RegistrationStarted.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The registration stall completed without cancellation.");
        }
    }

    private sealed class FaultAndCancelRegistrationHandler
        : HttpMessageHandler
    {
        private int _pageRequests;

        public TaskCompletionSource BothPagesStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFault { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == GalleryVersions)
            {
                return Response(
                    """{"versions":["1.0.0","2.0.0"]}""");
            }

            if (url == GalleryRegistration)
            {
                return Response(
                    """
                    {
                      "items": [
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
                        },
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/2.0.0/2.0.0.json"
                        }
                      ]
                    }
                    """);
            }

            int page = Interlocked.Increment(ref _pageRequests);
            if (page == 2)
                BothPagesStarted.TrySetResult();
            if (page == 1)
            {
                await ReleaseFault.Task;
                throw new JsonException(
                    "Simulated registration page failure.");
            }

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The registration stall completed without cancellation.");
        }

        private static HttpResponseMessage Response(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
    }

    private sealed class FaultAndTimeoutRegistrationHandler(
        bool transportTimeout = false,
        bool canceledTransportTimeout = false)
        : HttpMessageHandler
    {
        public int FastTransportRequests;
        public int StallingRequests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            if (url == GalleryVersions)
            {
                return Response(
                    """{"versions":["1.0.0","2.0.0"]}""");
            }

            if (url == GalleryRegistration)
            {
                return Response(
                    """
                    {
                      "items": [
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/1.0.0/1.0.0.json"
                        },
                        {
                          "@id": "https://api.nuget.org/v3/registration5-gz-semver2/contoso/page/2.0.0/2.0.0.json"
                        }
                      ]
                    }
                    """);
            }

            if (url.EndsWith(
                    "/page/1.0.0/1.0.0.json",
                    StringComparison.Ordinal))
            {
                Interlocked.Increment(ref FastTransportRequests);
                throw new HttpRequestException(
                    "Simulated registration transport failure.");
            }

            Interlocked.Increment(ref StallingRequests);
            if (canceledTransportTimeout)
                throw CreateCanceledTransportTimeout();

            if (transportTimeout)
            {
                throw new TimeoutException(
                    "Simulated registration transport timeout.");
            }

            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "The registration stall completed without cancellation.");
        }

        private static HttpResponseMessage Response(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
    }

    private sealed class TransientGalleryHandler(
        bool statuslessFailure = false) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public int PrimaryRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            string url = request.RequestUri!.AbsoluteUri;
            if (url.StartsWith(
                    "https://globalcdn.nuget.org/v3/registration5-gz-semver2/",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (PrimaryRequests++ == 0)
            {
                if (statuslessFailure)
                {
                    throw new HttpRequestException(
                        "Browser fetch failed.",
                        new InvalidOperationException(
                            "JavaScript transport failure."));
                }

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.BadGateway));
            }

            HttpContent content = url.StartsWith(
                    GallerySearch,
                    StringComparison.Ordinal)
                ? new StringContent(
                    """{"data":[{"id":"Contoso","version":"1.0.0"}]}""")
                : url.Equals(GalleryVersions, StringComparison.Ordinal)
                    ? new StringContent(
                        """{"versions":["1.0.0"]}""")
                    : url.Equals(GalleryManifest, StringComparison.Ordinal)
                        ? new StringContent("<package />")
                        : new ByteArrayContent("package bytes"u8.ToArray());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
        }
    }

    private sealed class ImmediateReadFailureStream(Exception failure)
        : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw failure;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(failure);

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class ReadThenFailureStream(byte[] content) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < content.Length)
            {
                int copied = Math.Min(count, content.Length - _position);
                content.AsSpan(_position, copied).CopyTo(
                    buffer.AsSpan(offset, copied));
                _position += copied;
                return copied;
            }

            throw new IOException("The response body ended.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return ValueTask.FromResult(
                    Read(buffer.Span));
            }
            catch (Exception exception)
            {
                return ValueTask.FromException<int>(exception);
            }
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position < content.Length)
            {
                int copied = Math.Min(
                    buffer.Length,
                    content.Length - _position);
                content.AsSpan(_position, copied).CopyTo(buffer);
                _position += copied;
                return copied;
            }

            throw new IOException("The response body ended.");
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class LateEofStream(
        byte[] content,
        TimeSpan eofDelay) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position < content.Length)
            {
                int copied = Math.Min(
                    buffer.Length,
                    content.Length - _position);
                content.AsMemory(_position, copied).CopyTo(buffer);
                _position += copied;
                return copied;
            }

            await Task.Delay(eofDelay).ConfigureAwait(false);
            return 0;
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }

    private sealed class CleanupFailureContent(
        byte[] content,
        bool responseCleanup) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await stream.WriteAsync(content);
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(
                responseCleanup
                    ? new MemoryStream(content, writable: false)
                    : new AsyncDisposeFailureStream(content));

        protected override bool TryComputeLength(out long length)
        {
            length = content.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && responseCleanup)
            {
                throw new IOException(
                    "The response cleanup failed.");
            }
        }
    }

    private sealed class AsyncDisposeFailureStream(byte[] content)
        : MemoryStream(content, writable: false)
    {
        public override ValueTask DisposeAsync() =>
            ValueTask.FromException(
                new IOException("The body cleanup failed."));
    }

    private sealed class LateOversizeStream(byte[] content) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_position == content.Length)
                return 0;
            if (_position == 0)
            {
                while (!cancellationToken.IsCancellationRequested)
                    await Task.Yield();
            }

            int count = Math.Min(
                content.Length - _position,
                buffer.Length);
            content.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            return count;
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
    }
}
