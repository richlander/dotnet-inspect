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
            failure.Source.TransportKind);
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
            typeof(NuGetFetch.PackageSourceClientFactory).GetMethods(),
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
        PackageSourceFailure failure = Failed(result);
        Assert.Equal(
            PackageSourceFailureKind.Transport,
            failure.Kind);
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
        PackageSourceResultFactory results = CreateResultFactory(
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri(ServiceIndex)));

        PackageSourceFailure failure = Failed(
            await PackageSourceOperation.CaptureVersionsAsync(
                results,
                async () =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        ServiceIndex);
                    using HttpResponseMessage response =
                        await client.SendAsync(
                            request,
                            TestContext.Current.CancellationToken);
                    return results.Versions(
                        [],
                        hasAuthoritativeListingState: false);
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
        PackageSourceResultFactory results = CreateResultFactory(
            PackageSourceDescriptor.NuGetV3(
                "feed",
                "Feed",
                new Uri(ServiceIndex)));

        PackageSourceFailure failure = Failed(
            await PackageSourceOperation.CaptureVersionsAsync(
                results,
                async () =>
                {
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        ServiceIndex);
                    using HttpResponseMessage response =
                        await client.SendAsync(
                            request,
                            TestContext.Current.CancellationToken);
                    return results.Versions(
                        [],
                        hasAuthoritativeListingState: false);
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
        PackageSourceResultFactory results = CreateResultFactory();

        Assert.Throws<NuGetOperationTimeoutException>(
            () =>
            {
                if (search)
                {
                    PackageSourceProjection.ProjectSearch(
                        results,
                        new DelayedList<SearchResult>(
                            new SearchResult("contoso", "1.0.0")),
                        operation);
                }
                else
                {
                    PackageSourceProjection.ProjectVersions(
                        results,
                        "contoso",
                        new DelayedList<string>("1.0.0"),
                        PackageDiscoveryContract.CompleteVersionEnumeration,
                        PackageListingState.Unknown,
                        hasAuthoritativeListingState: false,
                        operation);
                }
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedSearchSnapshotRemainsInsideOperationDeadline(
        bool versions)
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
        PackageSourceResultFactory results = CreateResultFactory();
        IReadOnlyList<SearchVersion>? nestedVersions = versions
            ? new DelayedList<SearchVersion>(
                new SearchVersion("1.0.0", 1))
            : null;
        IReadOnlyList<string>? nestedOwners = versions
            ? null
            : new DelayedList<string>("contoso");
        var result = new SearchResult(
            "contoso",
            "1.0.0",
            Versions: nestedVersions,
            Owners: nestedOwners);

        Assert.Throws<NuGetOperationTimeoutException>(
            () => PackageSourceProjection.ProjectSearch(
                results,
                [result],
                operation));
    }

    [Fact]
    public void VersionResultSnapshotRemainsInsideOperationDeadline()
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
        PackageSourceResultFactory results = CreateResultFactory();
        PackageCandidateObservation candidate = results.Candidate(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Unknown);

        Assert.Throws<NuGetOperationTimeoutException>(
            () => results.Versions(
                new DelayedList<PackageCandidateObservation>(candidate),
                hasAuthoritativeListingState: false,
                operation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessPublicationRemainsInsideOperationDeadline(
        bool versions)
    {
        PackageSourceResultFactory results = CreateResultFactory();
        PackageSearchResult searchResult =
            results.Search([new SearchResult("contoso", "1.0.0")]);
        PackageCandidateObservation candidate = results.Candidate(
            PackageSourceCoordinate.Create("contoso", "1.0.0"),
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Unknown);
        PackageVersionResult versionResult = results.Versions(
            [candidate],
            hasAuthoritativeListingState: false);
        var options = new NuGetFetchOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(1),
            OperationTimeout = TimeSpan.FromMilliseconds(20),
        };
        using var operation = new NuGetOperationDeadline(
            options,
            Timeout.InfiniteTimeSpan,
            CancellationToken.None);
        await Task.Delay(
            TimeSpan.FromMilliseconds(100),
            TestContext.Current.CancellationToken);

        PackageSourceFailure failure;
        if (versions)
        {
            failure = Failed(
                await PackageSourceOperation.CaptureVersionsAsync(
                    results,
                    () => Task.FromResult(versionResult),
                    TestContext.Current.CancellationToken,
                    operationDeadline: operation));
        }
        else
        {
            failure = Failed(
                await PackageSourceOperation.CaptureSearchAsync(
                    results,
                    () => Task.FromResult(searchResult),
                    TestContext.Current.CancellationToken,
                    operationDeadline: operation));
        }

        Assert.Equal(PackageSourceFailureKind.Timeout, failure.Kind);
        Assert.Equal(
            versions
                ? PackageSourceCapabilities.VersionEnumeration
                : PackageSourceCapabilities.Search,
            failure.Capability);
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
        Assert.Same(runtime.Source, failure.Source);
        Assert.DoesNotContain(
            GalleryPackage,
            failure.Message,
            StringComparison.Ordinal);
    }
}
