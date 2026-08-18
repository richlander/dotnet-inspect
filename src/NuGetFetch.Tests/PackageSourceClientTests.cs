using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed class PackageSourceClientTests
{
    private const string GallerySearch =
        "https://azuresearch-usnc.nuget.org/query";
    private const string GalleryVersions =
        "https://globalcdn.nuget.org/v3-flatcontainer/contoso/index.json";
    private const string GalleryPackage =
        "https://globalcdn.nuget.org/packages/contoso.1.0.0.nupkg";
    private const string GallerySymbols =
        "https://globalcdn.nuget.org/symbol-packages/contoso.1.0.0.snupkg";
    private const string ServiceIndex =
        "https://feed.example/v3/index.json";
    private const string FlatContainer =
        "https://feed.example/v3/flat/";
    private const string NuGetOrgVersions =
        "https://api.nuget.org/v3-flatcontainer/contoso/index.json";
    private const string Versions =
        "https://feed.example/v3/flat/contoso/index.json";
    private const string Package =
        "https://feed.example/v3/flat/contoso/1.0.0/contoso.1.0.0.nupkg";

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
            PackageSourceCapabilities.VersionEnumeration
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
            ["user:token", "user:token", "user:token", "user:token"],
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
    public async Task CanonicalV3EnumerationReportsUnknownListingState()
    {
        var handler = new RecordingHandler
        {
            [NuGetOrgVersions] = """{"versions":["1.0.0"]}""",
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
                Content = new StreamContent(new ImmediateIoFailureStream()),
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

    [Theory]
    [InlineData("https://feed.example/v3/flat/?sig=secret")]
    [InlineData("https://feed.example/v3/flat?sig=secret")]
    public async Task V3SignedPackageBaseAddressPreservesQuery(
        string baseAddress)
    {
        const string signedVersions =
            "https://feed.example/v3/flat/contoso/index.json?sig=secret";
        const string signedPackage =
            "https://feed.example/v3/flat/contoso/1.0.0/contoso.1.0.0.nupkg?sig=secret";
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
                signedPackage,
            ],
            handler.Requested);
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
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PackageSourceCapabilities.Search,
            error.Capability);
        Assert.Equal(
            PackageSourceFailureKind.Unsupported,
            error.Kind);
        Assert.Equal(runtime.Identity, error.Producer);
        Assert.Equal(PackageSourceKind.NuGetV3, error.TransportKind);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task CanonicalNuGetOrgV3DoesNotReintroduceSearchShortcut()
    {
        var handler = new RecordingHandler();
        HttpMessageHandler client = handler;
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "nuget.org",
                    PackageSourceIdentity.NuGetOrg.Value,
                    new PackageSourceCredential("user", "token")),
                client);

        Assert.False(
            runtime.Capabilities.HasFlag(PackageSourceCapabilities.Search));
        PackageSourceFailure failure = Failed(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            PackageSourceFailureKind.Unsupported,
            failure.Kind);
        Assert.Empty(handler.Requested);
        Assert.Empty(handler.Authentication);
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
                      "version": "1.0.0"
                    }
                  ]
                }
                """,
            [GalleryVersions] = """{"versions":["1.0.0"]}""",
            [GalleryPackage] = "package bytes",
            [GallerySymbols] = "symbol bytes",
        };
        using IPackageSourceClient runtime =
            PackageSourceClientFactory.CreateGallery(handler);

        Assert.Equal(
            PackageSourceCapabilities.Search
                | PackageSourceCapabilities.VersionEnumeration
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
        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "Contoso",
                TestContext.Current.CancellationToken));
        PackageCandidateObservation version =
            Assert.Single(versions.Candidates);
        Assert.Equal("1.0.0", version.Coordinate.Version);
        Assert.Equal(
            PackageListingState.Unknown,
            version.ListingState);
        Assert.False(versions.HasAuthoritativeListingState);
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
        string searchRequest = Assert.Single(
            handler.Requested,
            url => url.StartsWith(
                GallerySearch,
                StringComparison.Ordinal));
        Assert.Contains("q=contoso", searchRequest);
        Assert.Contains("prerelease=false", searchRequest);
        Assert.Contains("semVerLevel=2.0.0", searchRequest);
        Assert.Equal(
            [GalleryVersions, GalleryPackage, GallerySymbols],
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
        Assert.False(versions.HasAuthoritativeListingState);
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
        using HttpClientHandler handler =
            PackageSourceClientFactory
                .CreateCredentialFreeTransportHandler(
                    isBrowser: false);

        Assert.False(handler.UseCookies);
        Assert.False(handler.UseDefaultCredentials);
        Assert.False(handler.PreAuthenticate);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Null(handler.Credentials);
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
        Assert.Equal([versions], handler.Requested);
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
                    OperationTimeout = TimeSpan.FromSeconds(1),
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
    }

    [Theory]
    [InlineData("search")]
    [InlineData("versions")]
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

        Assert.Equal(2, handler.Requests);
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
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class TransientGalleryHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Requests++ == 0)
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.BadGateway));
            }

            string url = request.RequestUri!.AbsoluteUri;
            HttpContent content = url.StartsWith(
                    GallerySearch,
                    StringComparison.Ordinal)
                ? new StringContent(
                    """{"data":[{"id":"Contoso","version":"1.0.0"}]}""")
                : url.Equals(GalleryVersions, StringComparison.Ordinal)
                    ? new StringContent(
                        """{"versions":["1.0.0"]}""")
                    : new ByteArrayContent("package bytes"u8.ToArray());
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content,
                });
        }
    }

    private sealed class ImmediateIoFailureStream : Stream
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
            throw new IOException("The response body ended.");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new IOException("The response body ended."));

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
