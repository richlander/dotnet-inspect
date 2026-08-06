using System.Net;
using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for HttpClientFactory shared instance behavior.
/// </summary>
[Collection("Console")]
public class HttpClientFactoryTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-http-client-tests-{Guid.NewGuid():N}");

    public HttpClientFactoryTests()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false);
        DotnetInspector.Core.HttpClientFactory.ResetSharedForTesting();
        NuGetCache.Initialize("dotnet-inspect-test", _cacheDir, skipNuGetCache: true);
    }

    public void Dispose()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false);
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
    public void CreateNew_ReturnsDifferentInstances()
    {
        var client1 = DotnetInspector.Core.HttpClientFactory.CreateNew();
        var client2 = DotnetInspector.Core.HttpClientFactory.CreateNew();

        Assert.NotSame(client1, client2);
    }

    [Fact]
    public void CreateNew_RespectsTimeout()
    {
        var timeout = TimeSpan.FromSeconds(5);
        var client = DotnetInspector.Core.HttpClientFactory.CreateNew(timeout);

        Assert.Equal(timeout, client.Timeout);
    }

    /// <summary>
    /// A package search against a large authenticated feed can exceed the 30 second default,
    /// and before this variable existed the cap was unreachable from outside the process.
    /// </summary>
    [Theory]
    [InlineData(null, 30)]          // unset: the documented default
    [InlineData("", 30)]            // set but empty
    [InlineData("120", 120)]        // the case the variable exists for
    [InlineData("1", 1)]            // lower bound, accepted
    [InlineData("3600", 3600)]      // upper bound, accepted
    [InlineData("0", 30)]           // below the lower bound
    [InlineData("-5", 30)]          // negative
    [InlineData("3601", 30)]        // above the upper bound
    [InlineData("abc", 30)]         // not a number
    [InlineData("12.5", 30)]        // not whole seconds
    [InlineData("99999999", 30)]    // would exceed HttpClient.Timeout's own ceiling
    public void CreateNew_ReadsDefaultTimeoutFromEnvironment(string? configured, int expectedSeconds)
    {
        const string variable = "DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS";
        string? original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, configured);

            // Through CreateNew, not the parsing helper: the "99999999" case is only
            // meaningful if the resolved value actually reaches HttpClient.Timeout, which
            // throws above int.MaxValue milliseconds. Parsing it correctly and then
            // crashing on assignment would still be a bug.
            var client = DotnetInspector.Core.HttpClientFactory.CreateNew();

            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), client.Timeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    /// <summary>
    /// The SSRF-hardened client visits URLs that come from untrusted artifacts, so its timeout
    /// is containment rather than a feed-performance knob and must not follow the variable.
    /// </summary>
    [Fact]
    public void CreateUntrustedFetchClient_IgnoresTheConfiguredTimeout()
    {
        const string variable = "DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS";
        string? original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "600");

            var untrusted = DotnetInspector.Core.HttpClientFactory.CreateUntrustedFetchClient();
            var shared = DotnetInspector.Core.HttpClientFactory.CreateNew();

            Assert.Equal(TimeSpan.FromSeconds(30), untrusted.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(600), shared.Timeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }

    /// <summary>
    /// The parsed <c>--http-timeout</c> value outranks the variable, so a flag on the command
    /// line is not silently overridden by a stale export in a shell profile.
    /// </summary>
    [Fact]
    public void CreateNew_PrefersTheInitializedTimeoutOverTheEnvironment()
    {
        const string variable = "DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS";
        string? original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "600");
            DotnetInspector.Core.HttpClientFactory.Initialize(offline: false, defaultTimeout: TimeSpan.FromSeconds(45));

            var client = DotnetInspector.Core.HttpClientFactory.CreateNew();

            Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
            DotnetInspector.Core.HttpClientFactory.Initialize(offline: false);
        }
    }

    /// <summary>
    /// Initializing without a timeout has to clear a previously configured one. The field is
    /// static, so a leak here would make one test's flag change another test's client.
    /// </summary>
    [Fact]
    public void Initialize_WithoutATimeout_ClearsAPreviouslyConfiguredOne()
    {
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false, defaultTimeout: TimeSpan.FromSeconds(45));
        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false);

        var client = DotnetInspector.Core.HttpClientFactory.CreateNew();

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
    }

    /// <summary>
    /// Gates the precondition stated on <c>Initialize</c>. Both of its parameters are consumed
    /// in <c>CreateNew</c>, so a call made once <c>Shared</c> exists governs the next client
    /// built and leaves the cached one alone. Documented rather than fixed: resetting the cache
    /// on every call would discard a client that may be mid-request and disturb the
    /// authentication decorator wiring, and the CLI never hits it because <c>Program.cs</c>
    /// initializes before any command runs.
    /// </summary>
    /// <remarks>
    /// Both parameters are asserted, not just the timeout. An earlier version of this test
    /// passed <c>offline: false</c> throughout, so making the offline flag a per-request check
    /// left it green while breaking the very claim it was named as the gate for. The requests
    /// go to a closed loopback port, so an online client is refused at the socket and an
    /// offline one is short-circuited before it gets there.
    /// </remarks>
    [Fact]
    public async Task Initialize_OnceSharedExists_GovernsOnlyLaterClients()
    {
        const string ClosedPort = "http://127.0.0.1:1/";

        DotnetInspector.Core.HttpClientFactory.Initialize(offline: false, defaultTimeout: TimeSpan.FromSeconds(45));
        var shared = DotnetInspector.Core.HttpClientFactory.Shared;
        Assert.Equal(TimeSpan.FromSeconds(45), shared.Timeout);

        DotnetInspector.Core.HttpClientFactory.Initialize(offline: true, defaultTimeout: TimeSpan.FromSeconds(9));

        // The cached client keeps the instance, the timeout, and the handler chain it was
        // built with. Reaching the socket and being refused is what proves it is still online.
        Assert.Same(shared, DotnetInspector.Core.HttpClientFactory.Shared);
        Assert.Equal(TimeSpan.FromSeconds(45), DotnetInspector.Core.HttpClientFactory.Shared.Timeout);
        Assert.Null(FindOffline(await Record.ExceptionAsync(() => shared.GetAsync(ClosedPort, TestContext.Current.CancellationToken))));

        // The same call does govern the next client built.
        var later = DotnetInspector.Core.HttpClientFactory.CreateNew();
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
    /// Both spellings of the setting share one validator, so they cannot drift apart on what
    /// they accept. The flag reports a rejection and stops; the variable falls back.
    /// </summary>
    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("120", true, 120)]
    [InlineData("3600", true, 3600)]
    [InlineData(null, false, 0)]
    [InlineData("", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("-5", false, 0)]
    [InlineData("3601", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("12.5", false, 0)]
    [InlineData("99999999", false, 0)]
    [InlineData(" 120 ", true, 120)]    // NumberStyles.Integer tolerates surrounding whitespace
    public void TryParseTimeoutSeconds_AcceptsWholeSecondsInRange(string? value, bool expected, int expectedSeconds)
    {
        bool accepted = DotnetInspector.Core.HttpClientFactory.TryParseTimeoutSeconds(value, out TimeSpan timeout);

        Assert.Equal(expected, accepted);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), timeout);
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
        Assert.Equal("https://example.test/source.cs?access_token=REDACTED&ok=1", url);
        Assert.DoesNotContain("secret", url);
        Assert.DoesNotContain("user:password", url);
    }

    [Fact]
    public async Task EnableNetworkTrafficLogging_PrintsTrafficKindWithoutBlocking()
    {
        using var error = new StringWriter();
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(ILInspector.CSharp.CSharpIdentifier.ContainRenderedText, error))
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
            "Network traffic [source-fetch]: GET https://example.test/source.cs?token=REDACTED&ok=1",
            stderr);
        Assert.DoesNotContain("Network policy error", stderr);
        Assert.DoesNotContain("secret", stderr);
        Assert.DoesNotContain("user:password", stderr);
    }

    [Fact]
    public async Task NetworkPolicy_BlocksUnallowedVulnerabilityTrafficAfterRecordingIt()
    {
        using var error = new StringWriter();
        var transport = new StubHttpMessageHandler();
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(ILInspector.CSharp.CSharpIdentifier.ContainRenderedText, error))
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
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(ILInspector.CSharp.CSharpIdentifier.ContainRenderedText, error))
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

        var mermaid = diagram.ToMermaid(ILInspector.CSharp.CSharpIdentifier.ContainRenderedText);

        Assert.Contains("flowchart TD", mermaid);
        Assert.Contains("n0[\"dotnet-inspect\"]", mermaid);
        Assert.Contains("cache miss<br/>versions markout", mermaid);
        Assert.Contains("package-search<br/>GET https://azuresearch-usnc.nuget.org/query?q=packageid:markout", mermaid);
        Assert.Contains("package-download<br/>GET https://api.nuget.org/v3-flatcontainer/markout/1.0.0/markout.1.0.0.nupkg?token=REDACTED", mermaid);
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
        Assert.Contains("token=REDACTED", key);
        Assert.DoesNotContain("secret", key);
    }

    [Fact]
    public void CacheTelemetry_SymbolMissesIncludeExtensionInCategory()
    {
        using var diagram = RequestMermaidDiagram.Start();
        var key = $"https://example.test/symbols/{Guid.NewGuid():N}.pdb";

        DotnetInspector.Core.CoreCache.Set("symbol-misses", key, "403", extension: "forbidden");
        _ = DotnetInspector.Core.CoreCache.TryGet("symbol-misses", key, extension: "forbidden");
        _ = DotnetInspector.Core.CoreCache.TryGet("symbol-misses", key, extension: "miss");

        var mermaid = diagram.ToMermaid(ILInspector.CSharp.CSharpIdentifier.ContainRenderedText);

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

        var mermaid = diagram.ToMermaid(ILInspector.CSharp.CSharpIdentifier.ContainRenderedText);
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
        using (DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging(ILInspector.CSharp.CSharpIdentifier.ContainRenderedText, error))
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
