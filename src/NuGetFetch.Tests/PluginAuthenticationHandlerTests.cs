using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NuGetFetch;
using NuGetFetch.Plugins;

namespace NuGetFetch.Tests;

/// <summary>
/// Pins the 401-driven credential loop.
/// </summary>
/// <remarks>
/// <para>
/// The shape under test is copied from NuGet's own <c>HttpSourceAuthenticationHandler</c>
/// (NuGet/NuGet.Client, src/NuGet.Core/NuGet.Protocol/HttpSource/). Doing this in a
/// <see cref="DelegatingHandler"/> rather than at each call site is what makes it impossible for
/// an individual request to be accidentally anonymous, and it means a public feed never triggers
/// a credential lookup.
/// </para>
/// <para>
/// These tests use a fake credential source, so nothing here launches a process or touches a
/// network. The wire protocol itself is covered by <see cref="PluginProtocolTests"/>.
/// </para>
/// </remarks>
public sealed class PluginAuthenticationHandlerTests
{
    [Fact]
    public async Task SourceThatNeverChallenges_IsNeverAskedForCredentials()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(source, transport);

        using var response = await client.GetAsync("https://public.example/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The point of being 401-driven: a public feed costs nothing, so no plugin process is
        // launched for sources that do not want credentials.
        Assert.Equal(0, source.Calls);
        Assert.Null(transport.Requests[0].Authorization);
    }

    [Fact]
    public async Task Challenge_AcquiresCredentialsAndReplaysTheRequest()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(request =>
            request.Authorization is null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(source, transport);

        using var response = await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(Basic("user", "token"), transport.Requests[1].Authorization);
    }

    [Fact]
    public async Task FirstAcquisitionIsNotARetry_AndTheNextOneIs()
    {
        // Always rejecting forces repeated acquisition, which is what exposes the IsRetry flag.
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "stale"));
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = Client(source, transport);

        using var response = await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The Azure Artifacts provider warns that without IsRetry it "MAY" return invalid
        // credentials from its cache, so the second ask must be marked as a retry or a stale
        // token can never be replaced.
        Assert.Equal([false, true, true], source.RetryFlags);
    }

    [Fact]
    public async Task AcquiredCredentialsAreReusedForTheSameSource()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(request =>
            request.Authorization is null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(source, transport);

        using (await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken)) { }
        using (await client.GetAsync("https://feed.example/flat2/markout/index.json", TestContext.Current.CancellationToken)) { }

        // Three requests, not four: the second URL was authenticated on its first attempt.
        // Without this, every request to a private feed pays an extra round trip.
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal(1, source.Calls);
        Assert.Equal(Basic("user", "token"), transport.Requests[2].Authorization);
    }

    [Fact]
    public async Task CredentialsAreNotSharedBetweenSources()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(request =>
            request.Authorization is null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(source, transport);

        using (await client.GetAsync("https://one.example/index.json", TestContext.Current.CancellationToken)) { }
        using (await client.GetAsync("https://two.example/index.json", TestContext.Current.CancellationToken)) { }

        // A token minted for one feed must never be offered to another host. Each source is
        // challenged and asked for separately.
        Assert.Equal(2, source.Calls);
        Assert.Null(transport.Requests[2].Authorization);
        Assert.Equal(["https://one.example", "https://two.example"], source.Uris.Select(u => u.GetLeftPart(UriPartial.Authority)));
    }

    [Fact]
    public async Task PortAndSchemeArePartOfSourceIdentity()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(request =>
            request.Authorization is null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(source, transport);

        using (await client.GetAsync("https://feed.example:8443/index.json", TestContext.Current.CancellationToken)) { }
        using (await client.GetAsync("https://feed.example:9443/index.json", TestContext.Current.CancellationToken)) { }

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task WhenNoCredentialsAreAvailable_TheChallengeIsSurfacedUnchanged()
    {
        var source = new FakeCredentialSource(null);
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = Client(source, transport);

        using var response = await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken);

        // Returning the 401 rather than swallowing it is what lets a caller report an
        // authentication failure instead of a missing package.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, source.Calls);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task RepeatedlyRejectedCredentials_AreBounded()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "wrong"));
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = Client(source, transport);

        using var response = await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Matches AmbientAuthenticationState.MaxAuthRetries in NuGet. A provider that keeps
        // handing back credentials the feed keeps refusing must not loop forever.
        Assert.Equal(PluginAuthenticationHandler.MaxAuthRetries, transport.Requests.Count);
    }

    [Fact]
    public async Task WithNoCredentialSourcesInstalled_TheHandlerStaysOutOfTheWay()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token")) { Available = false };
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = Client(source, transport);

        using var response = await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(transport.Requests);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task ForbiddenDoesNotTriggerAcquisitionByDefault()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = Client(source, transport);

        using var response = await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken);

        // 403 normally means "authenticated, but not permitted", where new credentials cannot
        // help. NuGet gates this behind the same opt-in.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task ForbiddenTriggersAcquisitionWhenEnabled()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(request =>
            request.Authorization is null
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var handler = new PluginAuthenticationHandler(source, transport) { PromptOn403 = true };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://feed.example/index.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ConfiguredCredentialsTakePrecedenceOverAcquiredOnes()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("plugin", "plugin-token"));
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(source, transport);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://feed.example/index.json");
        request.Headers.Authorization = Basic("config", "config-token");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // A credential already on the request came from nuget.config or an explicit caller.
        // Overwriting it would silently change which identity is used.
        Assert.Equal(Basic("config", "config-token"), transport.Requests[0].Authorization);
    }

    [Fact]
    public async Task ConcurrentRequestsToOneSourceAcquireCredentialsOnce()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("user", "token"));
        var transport = new ScriptedTransport(request =>
            request.Authorization is null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Client(source, transport);

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(i =>
                client.GetAsync($"https://feed.example/package/{i}", TestContext.Current.CancellationToken)));

        try
        {
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        // Eight parallel requests to one feed should mean one credential acquisition, not eight
        // concurrent interactive sign-in prompts.
        Assert.Equal(1, source.Calls);
    }

    private static HttpClient Client(ICredentialSource source, HttpMessageHandler transport) =>
        new(new PluginAuthenticationHandler(source, transport));

    private static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    private sealed class FakeCredentialSource(PackageSourceCredential? credential) : ICredentialSource
    {
        private int _calls;

        public bool Available { get; init; } = true;

        public bool HasCredentialSources => Available;

        public int Calls => _calls;

        public List<bool> RetryFlags { get; } = [];

        public List<Uri> Uris { get; } = [];

        public Task<PackageSourceCredential?> GetCredentialsAsync(Uri uri, bool isRetry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            lock (RetryFlags)
            {
                RetryFlags.Add(isRetry);
                Uris.Add(uri);
            }

            return Task.FromResult(credential);
        }
    }

    /// <summary>A transport whose reply is a pure function of the request's Authorization header.</summary>
    private sealed class ScriptedTransport(Func<HttpRequestHeaders, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestHeaders> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request.Headers);
            }

            return Task.FromResult(respond(request.Headers));
        }
    }
}
