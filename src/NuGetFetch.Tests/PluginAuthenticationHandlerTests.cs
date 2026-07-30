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
    public async Task ConfiguredCredentialThatIsRejectedDoesNotBurnTheRetryBudget()
    {
        var source = new FakeCredentialSource(new PackageSourceCredential("plugin", "plugin-token"));
        var transport = new ScriptedTransport(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = Client(source, transport);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://feed.example/index.json");
        request.Headers.Authorization = Basic("config", "expired-token");

        using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Configured credentials win, so an acquired one could never be applied to this request.
        // Retrying would resend the identical failing request and re-invoke the plugin with
        // isRetry each time — expensive, possibly interactive, and guaranteed to be discarded.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(transport.Requests);
        Assert.Equal(0, source.Calls);
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

    [Fact]
    public async Task TwoFeedsSharingAHostRecoverFromTheAuthorityKeyedCacheRatherThanFailing()
    {
        // Every Azure DevOps organization lives on pkgs.dev.azure.com and differs only by path,
        // so two orgs collide in a cache keyed by scheme/host/port. This pins what that collision
        // actually costs: the stale token is offered, refused, and replaced, so the request still
        // succeeds. It is wasted work, not a failure, and it never approaches the retry budget.
        var orgA = new Uri("https://pkgs.dev.azure.com/org-a/_packaging/feed/nuget/v3/index.json");
        var orgB = new Uri("https://pkgs.dev.azure.com/org-b/_packaging/feed/nuget/v3/index.json");

        var source = new PerOrgCredentialSource();
        var transport = new PerOrgTransport();
        using HttpClient client = Client(source, transport);

        var attemptsPerRequest = new List<int>();
        foreach (Uri uri in new[] { orgA, orgB, orgA, orgB })
        {
            int before = transport.Attempts;
            using HttpResponseMessage response = await client.GetAsync(uri, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            attemptsPerRequest.Add(transport.Attempts - before);
        }

        // The first request pays one 401 to learn its token. Each later request pays one more,
        // because the previous org overwrote the shared slot: 4 requests, 4 challenges answered.
        Assert.Equal(4, source.Calls);
        Assert.Equal(8, transport.Attempts);

        // Crucially, every request settles in exactly two attempts -- one refusal, one success --
        // so no request approaches MaxAuthRetries and none of them fails.
        Assert.All(attemptsPerRequest, n => Assert.Equal(2, n));

        // Control: without contention the cache does its job, so a repeat of the org that just
        // ran costs one attempt and no acquisition. This is what makes the counts above evidence
        // of a *collision* rather than of a handler that simply never caches -- one that
        // reacquired unconditionally would also spend two attempts per request, but it would
        // spend two here as well.
        int beforeRepeat = transport.Attempts;
        int callsBeforeRepeat = source.Calls;

        using (HttpResponseMessage repeat = await client.GetAsync(orgB, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        }

        Assert.Equal(1, transport.Attempts - beforeRepeat);
        Assert.Equal(callsBeforeRepeat, source.Calls);
    }

    /// <summary>Hands back a distinct credential per Azure DevOps organization, keyed on the URL path.</summary>
    private sealed class PerOrgCredentialSource : ICredentialSource
    {
        private int _calls;

        public bool HasCredentialSources => true;

        public int Calls => _calls;

        public Task<PackageSourceCredential?> GetCredentialsAsync(Uri uri, bool isRetry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            string org = uri.Segments[1].Trim('/');
            return Task.FromResult<PackageSourceCredential?>(new PackageSourceCredential("user", $"token-{org}"));
        }
    }

    /// <summary>Accepts a request only when its credential matches the organization it addresses.</summary>
    private sealed class PerOrgTransport : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => _attempts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);

            string org = request.RequestUri!.Segments[1].Trim('/');
            string expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"user:token-{org}"));
            bool ok = string.Equals(request.Headers.Authorization?.Parameter, expected, StringComparison.Ordinal);

            return Task.FromResult(new HttpResponseMessage(ok ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));
        }
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
