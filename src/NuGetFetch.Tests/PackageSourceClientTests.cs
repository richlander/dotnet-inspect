using System.Net;
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
        using var client = new HttpClient(handler);
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetGallery,
                client);

        Assert.Equal(
            PackageSourceCapabilities.Search
                | PackageSourceCapabilities.VersionEnumeration
                | PackageSourceCapabilities.PackagePayload
                | PackageSourceCapabilities.SymbolPayload,
            runtime.Capabilities);
        SearchResult result = Assert.Single(
            await runtime.SearchAsync(
                "contoso",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Equal("Contoso", result.Id);
        Assert.Equal(
            ["1.0.0"],
            await runtime.GetVersionsAsync(
                "Contoso",
                TestContext.Current.CancellationToken));
        await using Stream package = await runtime.GetPackageAsync(
            "Contoso",
            "1.0",
            TestContext.Current.CancellationToken);
        await using Stream? symbols = await runtime.TryGetSymbolsAsync(
            "Contoso",
            "1.0",
            TestContext.Current.CancellationToken);

        Assert.Equal("package bytes", await ReadAsync(package));
        Assert.Equal("symbol bytes", await ReadAsync(symbols!));
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
    }

    [Fact]
    public async Task GalleryMissingSymbolsReturnNull()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetGallery,
                client);

        Assert.Null(
            await runtime.TryGetSymbolsAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken));
        Assert.Equal([GallerySymbols], handler.Requested);
    }

    [Fact]
    public async Task GalleryRejectsInvalidVersionMetadata()
    {
        var handler = new RecordingHandler
        {
            [GalleryVersions] = """{"versions":["../1.0.0"]}""",
        };
        using var client = new HttpClient(handler);
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetGallery,
                client);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => runtime.GetVersionsAsync(
                    "contoso",
                    TestContext.Current.CancellationToken));

        Assert.Contains("invalid package version", error.Message);
        Assert.Equal([GalleryVersions], handler.Requested);
    }

    [Fact]
    public void GalleryRejectsCredentials()
    {
        using var client = new HttpClient(new RecordingHandler());

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetGallery,
                client,
                credential:
                    new PackageSourceCredential("user", "token")));

        Assert.Contains("does not accept credentials", error.Message);
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
        using var client = new HttpClient(handler);
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetGallery,
                client);

        Assert.Equal(
            ["1.0.0"],
            await runtime.GetVersionsAsync(
                "Caf\u00E9",
                TestContext.Current.CancellationToken));
        Assert.Equal([versions], handler.Requested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GalleryRequestsUseLibraryDeadlines(bool payload)
    {
        using var client = new HttpClient(new StallingHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        IPackageSourceClient runtime =
            PackageSourceClientFactory.Create(
                PackageSourceDescriptor.NuGetGallery,
                client,
                new NuGetFetchOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(50),
                    OperationTimeout = TimeSpan.FromSeconds(1),
                });

        Task request = payload
            ? runtime.GetPackageAsync(
                "contoso",
                "1.0.0",
                TestContext.Current.CancellationToken)
            : runtime.GetVersionsAsync(
                "contoso",
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<NuGetRequestTimeoutException>(
            async () => await request);
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
            string? route = _routes.Keys.FirstOrDefault(
                candidate => url.Equals(
                        candidate,
                        StringComparison.OrdinalIgnoreCase)
                    || candidate == GallerySearch
                        && url.StartsWith(
                            GallerySearch + "?",
                            StringComparison.OrdinalIgnoreCase));
            bool hasResponse = route is not null;
            HttpStatusCode status = hasResponse
                ? HttpStatusCode.OK
                : HttpStatusCode.NotFound;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(
                    hasResponse
                        ? _routes.GetValueOrDefault(route!)!
                        : ""),
                RequestMessage = request,
            });
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
}
