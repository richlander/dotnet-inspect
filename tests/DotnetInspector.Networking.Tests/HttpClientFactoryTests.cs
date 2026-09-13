using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DotnetInspector.Networking.Tests;

[CollectionDefinition("HttpClientFactoryGuard", DisableParallelization = true)]
public sealed class HttpClientFactoryGuardCollection;

[Collection("HttpClientFactoryGuard")]
public sealed class HttpClientFactoryTests : IDisposable
{
    public HttpClientFactoryTests()
    {
        HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        HttpClientFactory.ResetSharedForTesting();
    }

    public void Dispose()
    {
        HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        HttpClientFactory.SetAuthenticationDecorator(null);
        HttpClientFactory.ResetSharedForTesting();
    }

    [Fact]
    public void Shared_ReusesConfiguredClient()
    {
        HttpClient first = HttpClientFactory.Shared;
        HttpClient second = HttpClientFactory.Shared;

        Assert.Same(first, second);
        Assert.Contains("User-Agent", first.DefaultRequestHeaders.Select(header => header.Key));
    }

    [Fact]
    public void CreateClient_UsesConfiguredTimeoutAndReturnsFreshClients()
    {
        HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(5),
        });

        using HttpClient first = HttpClientFactory.CreateClient();
        using HttpClient second = HttpClientFactory.CreateClient();

        Assert.Equal(TimeSpan.FromSeconds(5), first.Timeout);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void CreateCredentialFreeClient_DoesNotAdoptAuthentication()
    {
        bool authenticationAdopted = false;
        HttpClientFactory.SetAuthenticationDecorator(
            handler =>
            {
                authenticationAdopted = true;
                return handler;
            });

        using HttpClient client = HttpClientFactory.CreateCredentialFreeClient();

        Assert.False(authenticationAdopted);
    }

    [Fact]
    public void CredentialFreeDesktopHandlerDisablesAmbientStateAndRedirects()
    {
        using HttpMessageHandler handler =
            HttpClientFactory.CreateCredentialFreeHandler();
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
    public void CredentialFreeBrowserTransportAvoidsDesktopConfiguration()
    {
        using HttpClientHandler transport = HttpClientFactory
            .CreateTransportHandler(
                isBrowser: true,
                includeAuthentication: false);

        Assert.Equal(DecompressionMethods.None, transport.AutomaticDecompression);
        Assert.True(transport.UseCookies);
        Assert.True(transport.AllowAutoRedirect);
    }

    [Fact]
    public void UntrustedFetchClientKeepsContainmentTimeoutAndDisablesProxy()
    {
        HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            DefaultTimeout = TimeSpan.FromMinutes(10),
        });

        using HttpClient client = HttpClientFactory.CreateUntrustedFetchClient();
        using SocketsHttpHandler handler =
            HttpClientFactory.CreateUntrustedSocketsHandler();

        Assert.Equal(HttpClientFactoryOptions.BaselineTimeout, client.Timeout);
        Assert.False(handler.UseProxy);
    }

    [Fact]
    public void InitializeWithDefaultsClearsConfiguredTimeout()
    {
        HttpClientFactory.Initialize(new HttpClientFactoryOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(45),
        });
        HttpClientFactory.Initialize(new HttpClientFactoryOptions());

        using HttpClient client = HttpClientFactory.CreateClient();

        Assert.Equal(HttpClientFactoryOptions.BaselineTimeout, client.Timeout);
    }

    [Fact]
    public void NetworkTelemetryRecordsRedactedActivityAndObservation()
    {
        using var activitySource = new ActivitySource("test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using Activity? activity = activitySource.StartActivity("command");
        var observer = new RecordingObserver();
        using IDisposable subscription = NetworkTelemetry.Subscribe(observer);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://example.test/source.cs?token=secret&ok=1");

        using (NetworkTelemetry.Scope(NetworkTrafficKind.SourceFetch))
        {
            NetworkTelemetry.RecordRequestStarting(
                request,
                NetworkClientKinds.UntrustedFetch);
        }

        NetworkRequestObservation observation = Assert.Single(observer.Observations);
        Assert.Equal("GET", observation.Method);
        Assert.Equal("source-fetch", observation.TrafficKind.ToTelemetryName());
        Assert.Equal(
            "https://example.test/source.cs?REDACTED",
            observation.Url!.Value.ToString());
        Assert.True(observation.IsAllowedByPolicy);

        ActivityEvent activityEvent = Assert.Single(activity!.Events);
        Dictionary<string, object?> tags = activityEvent.Tags.ToDictionary(
            tag => tag.Key,
            tag => tag.Value);
        Assert.Equal(
            "https://example.test/source.cs?REDACTED",
            tags["url.full"]);
    }

    [Fact]
    public async Task NetworkPolicyBlocksUnapprovedTrafficAfterObservation()
    {
        var transport = new StubHttpMessageHandler();
        var observer = new RecordingObserver();
        using IDisposable subscription = NetworkTelemetry.Subscribe(observer);
        using var client = new HttpClient(new NetworkTelemetryHandler(
            transport,
            NetworkClientKinds.Shared));
        using IDisposable traffic =
            NetworkTelemetry.Scope(NetworkTrafficKind.VulnerabilityData);

        await Assert.ThrowsAsync<NetworkPolicyException>(
            () => client.GetAsync(
                "https://api.nuget.org/v3/vulnerabilities/index.json",
                TestContext.Current.CancellationToken));

        NetworkRequestObservation observation = Assert.Single(observer.Observations);
        Assert.False(observation.IsAllowedByPolicy);
        Assert.Equal(0, transport.RequestCount);
    }

    [Fact]
    public async Task NetworkPolicyAllowsAuthorizedTraffic()
    {
        var transport = new StubHttpMessageHandler();
        using var client = new HttpClient(new NetworkTelemetryHandler(
            transport,
            NetworkClientKinds.Shared));
        using IDisposable allowance =
            NetworkTelemetry.Allow(NetworkTrafficKind.VulnerabilityData);
        using IDisposable traffic =
            NetworkTelemetry.Scope(NetworkTrafficKind.VulnerabilityData);

        using HttpResponseMessage response = await client.GetAsync(
            "https://api.nuget.org/v3/vulnerabilities/index.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, transport.RequestCount);
    }

    private sealed class RecordingObserver :
        IObserver<NetworkRequestObservation>
    {
        public List<NetworkRequestObservation> Observations { get; } = [];

        public void OnNext(NetworkRequestObservation value) =>
            Observations.Add(value);

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
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
