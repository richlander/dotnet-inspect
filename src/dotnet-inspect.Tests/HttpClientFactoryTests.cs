using System.Net;
using System.Net.Sockets;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

// HttpClientFactory configuration and shared clients are process-wide. Isolate these
// invariants from every external test that can initialize or reset that state (#4246).
[CollectionDefinition("HttpClientFactoryGuard", DisableParallelization = true)]
public sealed class HttpClientFactoryGuardCollection;

/// <summary>
/// Tests for HttpClientFactory shared instance behavior.
/// </summary>
[Collection("HttpClientFactoryGuard")]
public class HttpClientFactoryTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-http-client-tests-{Guid.NewGuid():N}");

    public HttpClientFactoryTests()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize("dotnet-inspect-test", _cacheDir, skipNuGetCache: true);
    }

    public void Dispose()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    [Fact]
    public void Shared_ReturnsSameInstance()
    {
        var client1 = HttpClientFactory.Shared;
        var client2 = HttpClientFactory.Shared;

        Assert.Same(client1, client2);
    }

    [Fact]
    public void Shared_IsNotNull()
    {
        var client = HttpClientFactory.Shared;

        Assert.NotNull(client);
    }

    [Fact]
    public void Shared_HasUserAgentHeader()
    {
        var client = HttpClientFactory.Shared;

        Assert.True(client.DefaultRequestHeaders.Contains("User-Agent"));
    }

    [Fact]
    public void CreateClient_ReturnsDifferentInstances()
    {
        var client1 = DotnetInspector.Core.HttpClientFactory.CreateClient();
        var client2 = DotnetInspector.Core.HttpClientFactory.CreateClient();

        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void CreateClient_UsesConfiguredDefaultTimeout()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(5),
        });

        var client = DotnetInspector.Core.HttpClientFactory.CreateClient();

        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
    }

    [Fact]
    public void CreateCredentialFreeClient_HonorsTimeoutWithoutAuthentication()
    {
        bool authenticationAdopted = false;
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions
            {
                DefaultTimeout = TimeSpan.FromSeconds(7),
            });
        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(
            handler =>
            {
                authenticationAdopted = true;
                return handler;
            });
        try
        {
            using HttpClient client = DotnetInspector.Core.HttpClientFactory
                .CreateCredentialFreeClient();

            Assert.Equal(TimeSpan.FromSeconds(7), client.Timeout);
            Assert.False(authenticationAdopted);
        }
        finally
        {
            DotnetInspector.Core.HttpClientFactory
                .SetAuthenticationDecorator(null);
        }
    }

    [Fact]
    public void CreateCredentialFreeHandler_DisablesDesktopAmbientStateAndRedirects()
    {
        using HttpMessageHandler handler = DotnetInspector.Core.HttpClientFactory
            .CreateCredentialFreeHandler();
        HttpMessageHandler current = handler;
        while (current is DelegatingHandler delegating)
            current = delegating.InnerHandler!;

        HttpClientHandler transport = Assert.IsType<HttpClientHandler>(current);
        Assert.False(transport.UseCookies);
        Assert.False(transport.UseDefaultCredentials);
        Assert.False(transport.PreAuthenticate);
        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public void CreateCredentialFreePackageSourceHandler_DisablesAmbientStateAndRedirects()
    {
        using HttpMessageHandler handler = DotnetInspector.Core.HttpClientFactory
            .CreateCredentialFreePackageSourceHandler(
                "https://private.example/v3/index.json");
        HttpMessageHandler current = handler;
        while (current is DelegatingHandler delegating)
            current = delegating.InnerHandler!;

        SocketsHttpHandler transport =
            Assert.IsType<SocketsHttpHandler>(current);
        Assert.False(transport.UseCookies);
        Assert.Null(transport.Credentials);
        Assert.False(transport.PreAuthenticate);
        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task CreateCredentialFreePackageSourceHandler_AdmitsConfiguredPrivateOriginAndAddsUserAgent()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int sourcePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        string sourceUrl = $"http://127.0.0.1:{sourcePort}/index.json";
        string? requestText = null;
        Task server = Task.Run(
            async () =>
            {
                using TcpClient connection = await listener.AcceptTcpClientAsync(
                    TestContext.Current.CancellationToken);
                await using NetworkStream stream = connection.GetStream();
                var request = new byte[2048];
                int length = await stream.ReadAsync(
                    request,
                    TestContext.Current.CancellationToken);
                requestText = Encoding.ASCII.GetString(request, 0, length);
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"),
                    TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken);

        using var client = new HttpClient(
            DotnetInspector.Core.HttpClientFactory
                .CreateCredentialFreePackageSourceHandler(sourceUrl));
        string response = await client.GetStringAsync(
            sourceUrl,
            TestContext.Current.CancellationToken);
        await server;

        Assert.Equal("{}", response);
        Assert.Contains(
            "User-Agent: dotnet-inspect",
            requestText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateCredentialFreePackageSourceHandler_HonorsOfflinePolicy()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions { Offline = true });
        using var client = new HttpClient(
            DotnetInspector.Core.HttpClientFactory
                .CreateCredentialFreePackageSourceHandler(
                    "https://example.test/v3/index.json"));

        Exception? failure = await Record.ExceptionAsync(
            () => client.GetAsync(
                "https://example.test/v3/index.json",
                TestContext.Current.CancellationToken));

        Assert.NotNull(failure);
        Assert.NotNull(FindOffline(failure));
    }

    [Fact]
    public void CredentialFreeBrowserTransportAvoidsUnsupportedHandlerConfiguration()
    {
        using HttpClientHandler transport = DotnetInspector.Core.HttpClientFactory
            .CreateTransportHandler(
                isBrowser: true,
                includeAuthentication: false);

        Assert.Equal(DecompressionMethods.None, transport.AutomaticDecompression);
        Assert.True(transport.UseCookies);
        Assert.True(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task CreateCredentialFreeClient_HonorsOfflinePolicy()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(
            new HttpClientFactoryOptions
            {
                Offline = true,
            });
        using HttpClient client = DotnetInspector.Core.HttpClientFactory
            .CreateCredentialFreeClient();

        Exception? failure = await Record.ExceptionAsync(
            () => client.GetAsync(
                "https://example.test/",
                TestContext.Current.CancellationToken));

        Assert.NotNull(failure);
        Assert.NotNull(FindOffline(failure));
    }

    [Fact(Timeout = 60_000)]
    public async Task CreateClient_CapturesOneOptionsSnapshot()
    {
        const string ClosedPort = "http://127.0.0.1:1/";
        var observeCreation = new AsyncLocal<bool>();
        using var decoratorEntered = new ManualResetEventSlim();
        using var continueCreation = new ManualResetEventSlim();

        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            Offline = false,
            DefaultTimeout = TimeSpan.FromSeconds(45),
        });
        DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(handler =>
        {
            if (!observeCreation.Value)
                return handler;

            decoratorEntered.Set();
            continueCreation.Wait(TestContext.Current.CancellationToken);
            return handler;
        });

        using HttpClient unrelated = await Task.Run(
            DotnetInspector.Core.HttpClientFactory.CreateClient,
            TestContext.Current.CancellationToken);
        Assert.False(decoratorEntered.IsSet);

        Task<HttpClient> creation = Task.Factory.StartNew(
            () =>
            {
                observeCreation.Value = true;
                return DotnetInspector.Core.HttpClientFactory.CreateClient();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            // Keep the release path off the saturated thread pool: the fact timeout bounds a
            // genuine deadlock, while this wait resumes directly when the dedicated creator enters.
            decoratorEntered.Wait(TestContext.Current.CancellationToken);

            DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions
            {
                Offline = true,
                DefaultTimeout = TimeSpan.FromSeconds(9),
            });
            continueCreation.Set();

            using HttpClient client = await creation;
            Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
            Exception? failure = await Record.ExceptionAsync(
                () => client.GetAsync(ClosedPort, TestContext.Current.CancellationToken));
            Assert.NotNull(failure);
            Assert.Null(FindOffline(failure));
        }
        finally
        {
            continueCreation.Set();
            DotnetInspector.Core.HttpClientFactory.SetAuthenticationDecorator(null);
        }
    }

    /// <summary>
    /// The SSRF-hardened client visits URLs that come from untrusted artifacts, so its timeout
    /// is containment rather than a feed-performance knob and must not follow the configured
    /// default.
    /// </summary>
    [Fact]
    public void CreateUntrustedFetchClient_DoesNotFollowTheConfiguredDefaultTimeout()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(600),
        });

        var untrusted = DotnetInspector.Core.HttpClientFactory.CreateUntrustedFetchClient();
        var standard = DotnetInspector.Core.HttpClientFactory.CreateClient();

        Assert.Equal(HttpClientFactoryOptions.BaselineTimeout, untrusted.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(600), standard.Timeout);
    }

    [Fact]
    public void CreateUntrustedFetchClient_DoesNotUseAnAmbientProxy()
    {
        using SocketsHttpHandler handler =
            DotnetInspector.Core.HttpClientFactory.CreateUntrustedSocketsHandler();

        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void GetPackageSourceClient_ReusesOneClientPerOrigin()
    {
        HttpClient first =
            DotnetInspector.Core.HttpClientFactory.GetPackageSourceClient(
                "https://private.example/v3/index.json");
        HttpClient sameOrigin =
            DotnetInspector.Core.HttpClientFactory.GetPackageSourceClient(
                "https://PRIVATE.example/query");
        HttpClient differentPort =
            DotnetInspector.Core.HttpClientFactory.GetPackageSourceClient(
                "https://private.example:8443/v3/index.json");

        Assert.Same(first, sameOrigin);
        Assert.NotSame(first, differentPort);
    }

    [Fact]
    public async Task PackageSourceClient_AllowsConfiguredPrivateOriginButBlocksPrivateRedirect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int sourcePort =
            ((IPEndPoint)listener.LocalEndpoint).Port;
        int redirectPort = sourcePort == ushort.MaxValue
            ? sourcePort - 1
            : sourcePort + 1;
        string sourceUrl = $"http://127.0.0.1:{sourcePort}/index.json";
        string response =
            "HTTP/1.1 302 Found\r\n"
            + $"Location: http://127.0.0.1:{redirectPort}/secret\r\n"
            + "Content-Length: 0\r\n"
            + "Connection: close\r\n\r\n";

        Task server = Task.Run(
            async () =>
            {
                using TcpClient connection = await listener.AcceptTcpClientAsync(
                    TestContext.Current.CancellationToken);
                await using NetworkStream stream = connection.GetStream();
                var request = new byte[1024];
                _ = await stream.ReadAsync(
                    request,
                    TestContext.Current.CancellationToken);
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes(response),
                    TestContext.Current.CancellationToken);
            },
            TestContext.Current.CancellationToken);

        using HttpClient client =
            DotnetInspector.Core.HttpClientFactory.CreatePackageSourceClient(sourceUrl);
        HttpRequestException exception =
            await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAsync(
                    sourceUrl,
                    TestContext.Current.CancellationToken));
        await server;

        Assert.Contains("Blocked request to non-public address", exception.ToString());
    }

    [Fact]
    public async Task PackageSourceClient_AllowsConfiguredPrivateIpv6Origin()
    {
        Assert.True(Socket.OSSupportsIPv6);
        using var listener =
            new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        int sourcePort =
            ((IPEndPoint)listener.LocalEndpoint).Port;
        string sourceUrl =
            $"http://[::1]:{sourcePort}/index.json";
        Task server = ServeHttpResponseAsync(
            listener,
            """{"resources":[]}""",
            TestContext.Current.CancellationToken);

        using HttpClient client =
            DotnetInspector.Core.HttpClientFactory
                .CreatePackageSourceClient(sourceUrl);
        string response = await client.GetStringAsync(
            sourceUrl,
            TestContext.Current.CancellationToken);
        await server;

        Assert.Equal("""{"resources":[]}""", response);
    }

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("192.0.0.9", false)]
    [InlineData("192.0.0.10", false)]
    [InlineData("0.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("192.0.0.8", true)]
    [InlineData("192.0.2.1", true)]
    [InlineData("192.88.99.1", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("198.18.0.1", true)]
    [InlineData("198.19.255.254", true)]
    [InlineData("198.51.100.1", true)]
    [InlineData("203.0.113.1", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("240.0.0.1", true)]
    [InlineData("2606:4700:4700::1111", false)]
    [InlineData("2606:4700:4700:0:0:5efe:808:808", false)]
    [InlineData("2606:4700:4700:0:0:5efe:a00:1", true)]
    [InlineData("2606:4700:4700:0:200:5efe:7f00:1", true)]
    [InlineData("64:ff9b::808:808", false)]
    [InlineData("2001:1::1", false)]
    [InlineData("2001:1::2", false)]
    [InlineData("2001:1::3", false)]
    [InlineData("2001:3::1", false)]
    [InlineData("2001:4:112::1", false)]
    [InlineData("2001:20::1", false)]
    [InlineData("2001:20::5efe:808:808", false)]
    [InlineData("2001:20::5efe:a00:1", true)]
    [InlineData("2001:30::1", false)]
    [InlineData("2002:808:808::", false)]
    [InlineData("2002:808:808:0:0:5efe:808:808", false)]
    [InlineData("2002:808:808:0:0:5efe:7f00:1", true)]
    [InlineData("2002:808:808:0:200:5efe:a00:1", true)]
    [InlineData("2620:4f:8000::1", false)]
    [InlineData("::", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("64:ff9b::7f00:1", true)]
    [InlineData("64:ff9b:1::1", true)]
    [InlineData("100::1", true)]
    [InlineData("100:0:0:1::1", true)]
    [InlineData("2001::1", true)]
    [InlineData("2001:1::4", true)]
    [InlineData("2001:2::1", true)]
    [InlineData("2001:10::1", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("2002:7f00:1::", true)]
    [InlineData("3fff::1", true)]
    [InlineData("4000::5efe:808:808", true)]
    [InlineData("4000::1", true)]
    [InlineData("4000::200:5efe:808:808", true)]
    [InlineData("5f00::1", true)]
    [InlineData("7fff::1", true)]
    [InlineData("8000::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("ff00::1", true)]
    public void UntrustedFetchAddressClassification_MatchesNonPublicContract(
        string address,
        bool expected)
    {
        var classify = typeof(DotnetInspector.Core.HttpClientFactory).GetMethod(
            "IsNonPublic",
            System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static);
        Assert.NotNull(classify);
        Assert.Equal(
            expected,
            classify.Invoke(null, [IPAddress.Parse(address)]));
    }

    [Fact]
    public async Task PackageSearch_BlocksPrivateCrossOriginDiscoveredEndpoint()
    {
        using var sourceListener = new TcpListener(IPAddress.Loopback, 0);
        using var targetListener = new TcpListener(IPAddress.Loopback, 0);
        sourceListener.Start();
        targetListener.Start();
        string sourceUrl =
            $"http://127.0.0.1:{((IPEndPoint)sourceListener.LocalEndpoint).Port}/index.json";
        string targetUrl =
            $"http://127.0.0.1:{((IPEndPoint)targetListener.LocalEndpoint).Port}/private";
        string serviceIndex = $$"""
            {"resources":[{"@id":"{{targetUrl}}","@type":"SearchQueryService/3.5.0"}]}
            """;

        Task sourceServer = ServeHttpResponseAsync(
            sourceListener,
            serviceIndex,
            TestContext.Current.CancellationToken);
        using var targetCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        Task<bool> targetServer = Task.Run(
            async () =>
            {
                try
                {
                    await ServeHttpResponseAsync(
                        targetListener,
                        """{"data":[{"id":"Private.Package","version":"1.0.0"}]}""",
                        targetCancellation.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            },
            CancellationToken.None);

        Exception? error = await Record.ExceptionAsync(
            () => NuGetSearchService.SearchAsync(
                HttpClientFactory.Shared,
                "Private",
                sourceOptions: new NuGetSourceOptions { Sources = [sourceUrl] }));

        targetCancellation.Cancel();
        await sourceServer;
        bool targetReached = await targetServer;

        Assert.IsType<InvalidOperationException>(error);
        Assert.False(targetReached);
    }

    private static async Task ServeHttpResponseAsync(
        TcpListener listener,
        string body,
        CancellationToken cancellationToken)
    {
        using TcpClient connection =
            await listener.AcceptTcpClientAsync(cancellationToken);
        await using NetworkStream stream = connection.GetStream();
        var request = new byte[4096];
        _ = await stream.ReadAsync(request, cancellationToken);
        byte[] content = Encoding.UTF8.GetBytes(body);
        byte[] headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "Content-Type: application/json\r\n"
            + $"Content-Length: {content.Length}\r\n"
            + "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
    }

    /// <summary>
    /// Initializing with default options has to clear a previously configured timeout. The field is
    /// static, so a leak here would make one test's flag change another test's client.
    /// </summary>
    [Fact]
    public void Initialize_WithDefaultOptions_ClearsAPreviouslyConfiguredTimeout()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(45),
        });
        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions());

        var client = DotnetInspector.Core.HttpClientFactory.CreateClient();

        Assert.Equal(HttpClientFactoryOptions.BaselineTimeout, client.Timeout);
    }

    /// <summary>
    /// Gates the precondition stated on <c>Initialize</c>. Both option properties are consumed
    /// in <c>CreateClient</c>, so a call made once <c>Shared</c> exists governs the next client
    /// built and leaves the cached one alone. Documented rather than fixed: resetting the cache
    /// on every call would discard a client that may be mid-request and disturb the
    /// authentication decorator wiring, and the CLI never hits it because <c>Program.cs</c>
    /// initializes before any command runs.
    /// </summary>
    /// <remarks>
    /// Both settings are asserted, not just the timeout. An earlier version of this test
    /// passed <c>offline: false</c> throughout, so making the offline flag a per-request check
    /// left it green while breaking the very claim it was named as the gate for. The requests
    /// go to a closed loopback port, so an online client is refused at the socket and an
    /// offline one is short-circuited before it gets there.
    /// </remarks>
    [Fact]
    public async Task Initialize_OnceSharedExists_GovernsOnlyLaterClients()
    {
        const string ClosedPort = "http://127.0.0.1:1/";

        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(45),
        });
        var shared = DotnetInspector.Core.HttpClientFactory.Shared;
        Assert.Equal(TimeSpan.FromSeconds(45), shared.Timeout);

        DotnetInspector.Core.HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            Offline = true,
            DefaultTimeout = TimeSpan.FromSeconds(9),
        });

        // The cached client keeps the instance, the timeout, and the handler chain it was
        // built with. Reaching the socket and being refused is what proves it is still online.
        // The failure is asserted to exist as well as to not be offline, since an assertion
        // only that it is not offline would also pass if no request had been made at all.
        Assert.Same(shared, DotnetInspector.Core.HttpClientFactory.Shared);
        Assert.Equal(TimeSpan.FromSeconds(45), DotnetInspector.Core.HttpClientFactory.Shared.Timeout);
        var cachedFailure = await Record.ExceptionAsync(() => shared.GetAsync(ClosedPort, TestContext.Current.CancellationToken));
        Assert.NotNull(cachedFailure);
        Assert.Null(FindOffline(cachedFailure));

        // The same call does govern the next client built.
        var later = DotnetInspector.Core.HttpClientFactory.CreateClient();
        Assert.Equal(TimeSpan.FromSeconds(9), later.Timeout);
        Assert.NotNull(FindOffline(await Record.ExceptionAsync(() => later.GetAsync(ClosedPort, TestContext.Current.CancellationToken))));
    }

    /// <summary>
    /// Walks the inner-exception chain, because <c>HttpClient</c> is free to wrap whatever the
    /// handler pipeline throws and asserting on the outermost type would be brittle.
    /// </summary>
    private static OfflineException? FindOffline(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OfflineException offline)
            {
                return offline;
            }
        }

        return null;
    }

    /// <summary>
    /// <see cref="Uri.Host"/> preserves format characters: an IDN host carrying
    /// U+2066 or U+200B survives normalization intact and was emitted raw as
    /// <c>server.address</c>, next to a URL the same record had already
    /// contained.
    /// </summary>
    [Theory]
    [InlineData("\u2066")]
    [InlineData("\u200b")]
    public void NetworkTelemetry_ContainsAHostileHost(string hostile)
    {
        using var activitySource = new System.Diagnostics.ActivitySource("test");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "test",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("command");
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://ex{hostile}ample.test/source.cs");

        // The request itself keeps the host exactly as written.
        Assert.Contains(hostile, request.RequestUri!.Host, StringComparison.Ordinal);

        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageDownload))
        {
            NetworkTelemetry.RecordRequestStarting(
                request,
                NetworkClientKinds.Shared);
        }

        var evt = Assert.Single(activity!.Events);
        var tags = evt.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        var host = Assert.IsType<string>(tags["server.address"]);
        Assert.DoesNotContain(hostile, host, StringComparison.Ordinal);
        Assert.Contains("ample.test", host, StringComparison.Ordinal);
    }

    [Fact]
    public void NetworkTelemetry_AddsActivityEvent()
    {
        using var activitySource = new System.Diagnostics.ActivitySource("test");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "test",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("command");
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://user:password@example.test/source.cs?access_token=secret&ok=1");

        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageDownload))
        {
            NetworkTelemetry.RecordRequestStarting(
                request,
                NetworkClientKinds.Shared);
        }

        var evt = Assert.Single(activity!.Events);
        Assert.Equal(NetworkTelemetry.RequestStartingEventName, evt.Name);
        var tags = evt.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        Assert.Equal("GET", tags["http.request.method"]);
        Assert.Equal("shared", tags["dotnet_inspect.http.client_kind"]);
        Assert.Equal("package-download", tags["dotnet_inspect.network.kind"]);
        Assert.Equal(true, tags["dotnet_inspect.network.policy.allowed"]);
        var url = Assert.IsType<string>(tags["url.full"]);
        // The whole query goes, not the parameters a name list recognizes: a
        // feed picks the names too, so "ok=1" is no safer than "access_token".
        Assert.Equal("https://example.test/source.cs?REDACTED", url);
        Assert.DoesNotContain("secret", url);
        Assert.DoesNotContain("ok=1", url);
        Assert.DoesNotContain("user:password", url);
    }

    [Fact]
    public async Task EnableNetworkTrafficLogging_PrintsTrafficKindWithoutBlocking()
    {
        using var error = new StringWriter();
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(CSharpText.CSharpIdentifier.ContainRenderedText, error))
        {
            using var client = new HttpClient(new NetworkTelemetryHandler(
                new StubHttpMessageHandler(),
                NetworkClientKinds.UntrustedFetch));

            using var scope = NetworkTelemetry.Scope(NetworkTrafficKind.SourceFetch);
            using var response = await client.GetAsync(
                "https://user:password@example.test/source.cs?token=secret&ok=1",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var stderr = error.ToString();
        Assert.Contains(
            "Network traffic [source-fetch]: GET https://example.test/source.cs?REDACTED",
            stderr);
        Assert.DoesNotContain("ok=1", stderr);
        Assert.DoesNotContain("Network policy error", stderr);
        Assert.DoesNotContain("secret", stderr);
        Assert.DoesNotContain("user:password", stderr);
    }

    [Fact]
    public async Task NetworkPolicy_BlocksUnallowedVulnerabilityTrafficAfterRecordingIt()
    {
        using var error = new StringWriter();
        var transport = new StubHttpMessageHandler();
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(CSharpText.CSharpIdentifier.ContainRenderedText, error))
        using (var client = new HttpClient(new NetworkTelemetryHandler(
            transport,
            NetworkClientKinds.Shared)))
        using (NetworkTelemetry.Scope(NetworkTrafficKind.VulnerabilityData))
        {
            var exception = await Assert.ThrowsAsync<NetworkPolicyException>(() => client.GetAsync(
                "https://api.nuget.org/v3/vulnerabilities/index.json",
                TestContext.Current.CancellationToken));
            Assert.Contains("requires explicit capability authorization", exception.Message);
        }

        var stderr = error.ToString();
        Assert.Contains(
            "Network traffic [vulnerability-data]: GET https://api.nuget.org/v3/vulnerabilities/index.json",
            stderr);
        Assert.Contains(
            "Network policy error [vulnerability-data]: NuGet vulnerability service was accessed outside detailed view or an explicit network-using section",
            stderr);
        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task EnableNetworkTrafficLogging_DoesNotPrintPolicyErrorForAllowedVulnerabilityTraffic()
    {
        var stderr = await CaptureTrafficLogAsync(
            NetworkTrafficKind.VulnerabilityData,
            allowTrafficKind: true);

        Assert.Contains(
            "Network traffic [vulnerability-data]: GET https://api.nuget.org/v3/vulnerabilities/index.json",
            stderr);
        Assert.DoesNotContain("Network policy error", stderr);
    }

    [Fact]
    public async Task NetworkPolicy_VulnerabilityCapabilityAllowsAdvisoryTraffic()
    {
        using var error = new StringWriter();
        var transport = new StubHttpMessageHandler();
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(CSharpText.CSharpIdentifier.ContainRenderedText, error))
        using (var client = new HttpClient(new NetworkTelemetryHandler(
            transport,
            NetworkClientKinds.Shared)))
        using (NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData))
        using (NetworkTelemetry.Scope(NetworkTrafficKind.AdvisoryData))
        using (var response = await client.GetAsync(
            "https://api.github.com/advisories/GHSA-test",
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.DoesNotContain("Network policy error", error.ToString());
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task NetworkPolicy_BlocksAdvisoryTrafficWithoutVulnerabilityCapability()
    {
        var transport = new StubHttpMessageHandler();
        using var client = new HttpClient(new NetworkTelemetryHandler(
            transport,
            NetworkClientKinds.Shared));
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.AdvisoryData);

        await Assert.ThrowsAsync<NetworkPolicyException>(() => client.GetAsync(
            "https://api.github.com/advisories/GHSA-test",
            TestContext.Current.CancellationToken));
        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public void RequestMermaidDiagram_RendersObservedTrafficSequence()
    {
        using var diagram = RequestMermaidDiagram.Start();

        using (RequestTelemetry.Scope("package Markout", "package versions"))
        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageVersionList))
        {
            CacheTelemetry.Record("versions", "markout", CacheAccessResult.Miss);
        }

        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageSearch))
        {
            NetworkTelemetry.RecordRequestStarting(
                new HttpRequestMessage(HttpMethod.Get, "https://azuresearch-usnc.nuget.org/query?q=packageid:markout"),
                NetworkClientKinds.Shared);
        }

        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageDownload))
        {
            NetworkTelemetry.RecordRequestStarting(
                new HttpRequestMessage(HttpMethod.Get, "https://api.nuget.org/v3-flatcontainer/markout/1.0.0/markout.1.0.0.nupkg?token=secret"),
                NetworkClientKinds.Shared);
        }

        var mermaid = diagram.ToMermaid(CSharpText.CSharpIdentifier.ContainRenderedText);

        Assert.Contains("flowchart TD", mermaid);
        Assert.Contains("n0[\"dotnet-inspect\"]", mermaid);
        Assert.Contains("cache miss<br/>versions markout", mermaid);
        Assert.Contains("package-search<br/>GET https://azuresearch-usnc.nuget.org/query?REDACTED", mermaid);
        Assert.Contains("package-download<br/>GET https://api.nuget.org/v3-flatcontainer/markout/1.0.0/markout.1.0.0.nupkg?REDACTED", mermaid);
        Assert.DoesNotContain("token=", mermaid);
        Assert.Contains("n0 --> n1", mermaid);
        Assert.Contains("n1 --> n2", mermaid);
        Assert.Contains("n2 --> n3", mermaid);
        Assert.DoesNotContain("secret", mermaid);
    }

    [Fact]
    public void CacheTelemetry_AddsActivityEventWithRequestCurrency()
    {
        using var activitySource = new System.Diagnostics.ActivitySource("test");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "test",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("command");
        using (RequestTelemetry.Scope("package Markout", "package versions"))
        using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageVersionList))
        {
            CacheTelemetry.Record("versions", "https://api.nuget.org/v3/index.json?token=secret", CacheAccessResult.Hit);
        }

        var evt = Assert.Single(activity!.Events);
        Assert.Equal(CacheTelemetry.CacheAccessEventName, evt.Name);
        var tags = evt.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        Assert.Equal("versions", tags["dotnet_inspect.cache.category"]);
        Assert.Equal("hit", tags["dotnet_inspect.cache.result"]);
        Assert.Equal("package-version-list", tags["dotnet_inspect.network.kind"]);
        Assert.Equal("package Markout", tags["dotnet_inspect.request.what"]);
        Assert.Equal("package versions", tags["dotnet_inspect.request.why"]);
        var key = Assert.IsType<string>(tags["dotnet_inspect.cache.key"]);
        Assert.Equal("https://api.nuget.org/v3/index.json?REDACTED", key);
        Assert.DoesNotContain("secret", key);
        Assert.DoesNotContain("token=", key);
    }

    [Fact]
    public void CacheTelemetry_SymbolMissesIncludeExtensionInCategory()
    {
        using var diagram = RequestMermaidDiagram.Start();
        var key = $"https://example.test/symbols/{Guid.NewGuid():N}.pdb";

        DotnetInspector.Core.CoreCache.Set("symbol-misses", key, "403", extension: "forbidden");
        _ = DotnetInspector.Core.CoreCache.TryGet("symbol-misses", key, extension: "forbidden");
        _ = DotnetInspector.Core.CoreCache.TryGet("symbol-misses", key, extension: "miss");

        var mermaid = diagram.ToMermaid(CSharpText.CSharpIdentifier.ContainRenderedText);

        Assert.Contains("cache store<br/>symbol-misses/forbidden", mermaid);
        Assert.Contains("cache miss<br/>symbol-misses/miss", mermaid);
    }

    [Fact]
    public void RequestMermaidDiagram_CoalescesRepeatedCacheHitsButKeepsMisses()
    {
        using var diagram = RequestMermaidDiagram.Start();
        using var scope = NetworkTelemetry.Scope(NetworkTrafficKind.PlatformResolution);
        var category = $"platform-frameworks-{Guid.NewGuid():N}";
        var key = $"installed-frameworks-{Guid.NewGuid():N}";

        CacheTelemetry.Record(category, key, CacheAccessResult.Hit);
        CacheTelemetry.Record(category, key, CacheAccessResult.Hit);
        CacheTelemetry.Record(category, key, CacheAccessResult.Miss);
        CacheTelemetry.Record(category, key, CacheAccessResult.Miss);
        CacheTelemetry.Record(category, key, CacheAccessResult.Store);
        CacheTelemetry.Record(category, key, CacheAccessResult.Hit);

        var mermaid = diagram.ToMermaid(CSharpText.CSharpIdentifier.ContainRenderedText);
        var label = $"{category} {key}";

        Assert.Equal(1, CountOccurrences(mermaid, $"cache hit<br/>{label}"));
        Assert.Equal(2, CountOccurrences(mermaid, $"cache miss<br/>{label}"));
        Assert.Equal(1, CountOccurrences(mermaid, $"cache store<br/>{label}"));
    }

    private static async Task<string> CaptureTrafficLogAsync(
        NetworkTrafficKind trafficKind,
        bool allowTrafficKind)
    {
        using var error = new StringWriter();
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(CSharpText.CSharpIdentifier.ContainRenderedText, error))
        {
            using var client = new HttpClient(new NetworkTelemetryHandler(
                new StubHttpMessageHandler(),
                NetworkClientKinds.Shared));

            using var trafficScope = NetworkTelemetry.Scope(trafficKind);
            using var allowScope = allowTrafficKind ? NetworkTelemetry.Allow(trafficKind) : null;
            using var response = await client.GetAsync(
                "https://api.nuget.org/v3/vulnerabilities/index.json",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return error.ToString();
    }

    private static int CountOccurrences(string value, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
