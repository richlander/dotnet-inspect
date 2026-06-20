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
        var originalError = Console.Error;
        using var error = new DisposeTolerantWriter();
        Console.SetError(error);

        try
        {
            DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging();
            using var client = new HttpClient(new NetworkTelemetryHandler(
                new StubHttpMessageHandler(),
                NetworkClientKinds.UntrustedFetch));

            using var scope = NetworkTelemetry.Scope(NetworkTrafficKind.SourceFetch);
            using var response = await client.GetAsync(
                "https://user:password@example.test/source.cs?token=secret&ok=1",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Console.SetError(originalError);
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
    public async Task EnableNetworkTrafficLogging_PrintsPolicyErrorForUnallowedVulnerabilityTraffic()
    {
        var stderr = await CaptureTrafficLogAsync(
            NetworkTrafficKind.VulnerabilityData,
            allowTrafficKind: false);

        Assert.Contains(
            "Network traffic [vulnerability-data]: GET https://api.nuget.org/v3/vulnerabilities/index.json",
            stderr);
        Assert.Contains(
            "Network policy error [vulnerability-data]: NuGet vulnerability service was accessed outside detailed view or an explicit network-using section",
            stderr);
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

        var mermaid = diagram.ToMermaid();

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

        var mermaid = diagram.ToMermaid();

        Assert.Contains("cache store<br/>symbol-misses/forbidden", mermaid);
        Assert.Contains("cache miss<br/>symbol-misses/miss", mermaid);
    }

    [Fact]
    public void RequestMermaidDiagram_CoalescesRepeatedCacheHitsButKeepsMisses()
    {
        using var diagram = RequestMermaidDiagram.Start();
        using var scope = NetworkTelemetry.Scope(NetworkTrafficKind.PlatformResolution);

        CacheTelemetry.Record("platform-frameworks", "installed-frameworks", CacheAccessResult.Hit);
        CacheTelemetry.Record("platform-frameworks", "installed-frameworks", CacheAccessResult.Hit);
        CacheTelemetry.Record("platform-frameworks", "installed-frameworks", CacheAccessResult.Miss);
        CacheTelemetry.Record("platform-frameworks", "installed-frameworks", CacheAccessResult.Miss);
        CacheTelemetry.Record("platform-frameworks", "installed-frameworks", CacheAccessResult.Store);
        CacheTelemetry.Record("platform-frameworks", "installed-frameworks", CacheAccessResult.Hit);

        var mermaid = diagram.ToMermaid();

        Assert.Equal(1, CountOccurrences(mermaid, "cache hit<br/>platform-frameworks installed-frameworks"));
        Assert.Equal(2, CountOccurrences(mermaid, "cache miss<br/>platform-frameworks installed-frameworks"));
        Assert.Equal(1, CountOccurrences(mermaid, "cache store<br/>platform-frameworks installed-frameworks"));
    }

    private static async Task<string> CaptureTrafficLogAsync(
        NetworkTrafficKind trafficKind,
        bool allowTrafficKind)
    {
        var originalError = Console.Error;
        using var error = new DisposeTolerantWriter();
        Console.SetError(error);

        try
        {
            DotnetInspector.Core.HttpClientFactory.EnableNetworkTrafficLogging();
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
        finally
        {
            Console.SetError(originalError);
        }

        return error.ToString();
    }

    /// <summary>
    /// A Console capture sink that tolerates writes after disposal. Network-traffic
    /// telemetry publishes on the async HTTP pipeline, so a continuation can write to
    /// the captured <see cref="Console.Error"/> after the capture scope ends — writing
    /// to a disposed <see cref="StringWriter"/> throws <see cref="ObjectDisposedException"/>
    /// (issue #705). This sink's <see cref="Dispose(bool)"/> is a no-op and its buffer is
    /// synchronized, so a late or racing write is harmlessly recorded, never thrown.
    /// </summary>
    private sealed class DisposeTolerantWriter : TextWriter
    {
        private readonly System.Text.StringBuilder _buffer = new();
        private readonly object _gate = new();

        public override System.Text.Encoding Encoding => System.Text.Encoding.Unicode;

        public override void Write(char value)
        {
            lock (_gate) { _buffer.Append(value); }
        }

        public override void Write(string? value)
        {
            lock (_gate) { _buffer.Append(value); }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            lock (_gate) { _buffer.Append(buffer, index, count); }
        }

        public override string ToString()
        {
            lock (_gate) { return _buffer.ToString(); }
        }

        protected override void Dispose(bool disposing)
        {
            // No-op: tolerate late writes from async telemetry continuations (issue #705).
        }
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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
