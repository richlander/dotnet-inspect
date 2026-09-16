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

        Assert.Equal(
            PackageSourceKind.NuGetV3,
            runtime.Source.TransportKind);
        Assert.Equal(
            PackageSourceCapabilities.Search
                | PackageSourceCapabilities.VersionEnumeration
                | PackageSourceCapabilities.Manifest
                | PackageSourceCapabilities.PackagePayload
                | PackageSourceCapabilities.Catalog,
            runtime.Capabilities);
        PackageVersionResult versions = Succeeded(
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));
        PackageCandidateObservation candidate =
            Assert.Single(versions.Candidates);
        Assert.Equal("contoso", candidate.Coordinate.PackageId);
        Assert.Equal("1.0.0", candidate.Coordinate.Version);
        Assert.Same(runtime.Source, candidate.Source);
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
        Assert.Same(runtime.Source, manifest.Source);
        Assert.Equal(
            PackageSourceKind.NuGetV3,
            manifest.Source.TransportKind);
        Assert.Equal(
            "<package />",
            Encoding.UTF8.GetString(manifest.Content.ToArray()));
        PackageSourcePayload packagePayload = Succeeded(
            await runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        await using Stream package = packagePayload.Content;
        Assert.Equal(candidate.Coordinate, packagePayload.Coordinate);
        Assert.Same(runtime.Source, packagePayload.Source);
        Assert.Equal(
            PackageSourcePayloadKind.Package,
            packagePayload.Kind);
        Assert.Equal(
            PackageSourceKind.NuGetV3,
            packagePayload.Source.TransportKind);
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
    public void CustomClientCannotAdvertiseAnUnforwardedCatalogCapability()
    {
        var descriptor = PackageSourceDescriptor.NuGetV3(
            "custom",
            "Custom",
            new Uri(ServiceIndex));

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => NuGetFetch.PackageSourceClientFactory.CreateCustom(
                    descriptor,
                    PackageSourceAssociation.Create(),
                    factory => new FactoryOnlyPackageSourceClient(
                        factory.Source,
                        PackageSourceCapabilities.Catalog)));

        Assert.Contains(
            "cannot advertise the Catalog capability",
            error.Message);
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
        Assert.Same(runtime.Source, candidate.Source);
        Assert.DoesNotContain(
            "secret",
            runtime.Source.Producer.Key,
            StringComparison.OrdinalIgnoreCase);
        using IPackageSourceClient rotated =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "rotated",
                    ServiceIndex + "?sig=other"),
                new RecordingHandler());
        Assert.Equal(
            runtime.Source.Producer,
            rotated.Source.Producer);
        Assert.NotSame(
            runtime.Source.Association,
            rotated.Source.Association);
        Assert.Equal(
            [signedServiceIndex, Versions],
            handler.Requested);
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
}
