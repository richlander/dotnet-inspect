using System.Net;
using System.Text;
using NuGetFetch;

namespace NuGetFetch.Tests;

public sealed class PackageSourceClientTests
{
    private const string ServiceIndex =
        "https://feed.example/v3/index.json";
    private const string FlatContainer =
        "https://feed.example/v3/flat/";
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
        using var client = new HttpClient(handler);
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
        Assert.Equal(["1.0.0"], await runtime.GetVersionsAsync(
            "contoso",
            TestContext.Current.CancellationToken));
        await using Stream package = await runtime.GetPackageAsync(
            "contoso",
            "1.0.0",
            TestContext.Current.CancellationToken);
        using var reader = new StreamReader(package);
        Assert.Equal(
            "package bytes",
            await reader.ReadToEndAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            ["user:token", "user:token", "user:token"],
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
        using var client = new HttpClient(handler);
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "signed",
                    signedServiceIndex,
                    new PackageSourceCredential("user", "token")),
                client);

        Assert.Equal(
            ["1.0.0"],
            await runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken));
        Assert.Equal(
            [signedServiceIndex, Versions],
            handler.Requested);
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
        using var client = new HttpClient(handler);
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
        using var client = new HttpClient(handler);
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
        using var client = new HttpClient(handler);
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource("corporate", ServiceIndex),
                client);

        PackageSourceCapabilityException error =
            await Assert.ThrowsAsync<PackageSourceCapabilityException>(
                () => runtime.SearchAsync(
                    "contoso",
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(PackageSourceKind.NuGetV3, error.Kind);
        Assert.Equal(
            PackageSourceCapabilities.Search,
            error.Capability);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task CanonicalNuGetOrgV3DoesNotReintroduceSearchShortcut()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                new PackageSource(
                    "nuget.org",
                    PackageSourceIdentity.NuGetOrg.Value,
                    new PackageSourceCredential("user", "token")),
                client);

        Assert.False(
            runtime.Capabilities.HasFlag(PackageSourceCapabilities.Search));
        await Assert.ThrowsAsync<PackageSourceCapabilityException>(
            () => runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requested);
        Assert.Empty(handler.Authentication);
    }

    [Fact]
    public void FactoryRejectsKindsWithoutAnImplementation()
    {
        using var client = new HttpClient(new RecordingHandler());

        PackageSourceClientUnavailableException error =
            Assert.Throws<PackageSourceClientUnavailableException>(
                () => PackageSourceClientFactory.Create(
                    PackageSourceDescriptor.NuGetGallery,
                    client));

        Assert.Equal(PackageSourceKind.NuGetGallery, error.Kind);
    }

    [Fact]
    public void LegacyLocalSourceRemainsAnExplicitUnsupportedKind()
    {
        using var client = new HttpClient(new RecordingHandler());
        var source = new PackageSource(
            "local",
            Path.GetFullPath("packages"));

        PackageSourceClientUnavailableException error =
            Assert.Throws<PackageSourceClientUnavailableException>(
                () => PackageSourceClientFactory.Create(source, client));

        Assert.Equal(PackageSourceKind.LocalFolder, error.Kind);
    }

    private static string? DecodeBasic(string? parameter) =>
        parameter is null
            ? null
            : Encoding.UTF8.GetString(
                Convert.FromBase64String(parameter));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes =
            new(StringComparer.OrdinalIgnoreCase);

        public string this[string url] { set => _routes[url] = value; }

        public List<string> Requested { get; } = [];
        public List<string?> Authentication { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            Authentication.Add(
                request.Headers.Authorization?.Parameter);
            bool hasResponse = _routes.ContainsKey(url);
            HttpStatusCode status = hasResponse
                ? HttpStatusCode.OK
                : HttpStatusCode.NotFound;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    hasResponse
                        ? _routes.GetValueOrDefault(url)!
                        : ""),
                RequestMessage = request,
            });
        }
    }
}
