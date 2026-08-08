using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NuGetFetch;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Live coverage against a real private Azure DevOps Artifacts feed.
/// </summary>
/// <remarks>
/// <para>
/// These are the only tests that can prove the private-feed story end to end: Azure DevOps
/// authenticates its V3 service index, which no public feed and no in-memory fake exercises
/// by default. The hermetic equivalents live in <see cref="ServiceIndexAuthenticationTests"/>
/// and <see cref="CredentialMechanismTests"/>; these confirm the fakes match reality.
/// </para>
/// <para>
/// They are excluded from PR CI two ways: by <c>Network=Live</c>, and by skipping unless a feed
/// and a token are supplied. A fork PR has no access to either, and a private feed credential
/// must not be a prerequisite for the gate. To run them:
/// </para>
/// <code>
/// export DOTNET_INSPECT_TEST_AZDO_FEED=https://pkgs.dev.azure.com/ORG/PROJECT/_packaging/FEED/nuget/v3/index.json
/// export DOTNET_INSPECT_TEST_AZDO_TOKEN=&lt;PAT with Packaging read, or an Entra access token&gt;
/// export DOTNET_INSPECT_TEST_AZDO_PACKAGE=Markout          # optional
/// dotnet run --project src/NuGetFetch.Tests -c Release -- -trait "Network=Live"
/// </code>
/// <para>
/// The token is read from the environment and never written to a config file, so no secret
/// lands on disk. Both a PAT and an Entra access token are valid values: the feed only ever
/// sees HTTP Basic with the token as the password, so the two are indistinguishable on the
/// wire. That is also why there is no separate "certificate" case — a certificate authenticates
/// a service principal to Entra ID, which returns a token; the feed never sees the certificate.
/// </para>
/// </remarks>
[Trait("Network", "Live")]
public sealed class AzureDevOpsFeedTests
{
    private static string? Feed => Environment.GetEnvironmentVariable("DOTNET_INSPECT_TEST_AZDO_FEED");

    private static string? Token => Environment.GetEnvironmentVariable("DOTNET_INSPECT_TEST_AZDO_TOKEN");

    private static string PackageId =>
        Environment.GetEnvironmentVariable("DOTNET_INSPECT_TEST_AZDO_PACKAGE") ?? "Markout";

    [Fact]
    public async Task CredentialOnTheHandler_ResolvesVersionsFromThePrivateFeed()
    {
        RequireFeed();

        NuGetClient client = new(new HttpClient(new BasicAuthHandler(Token!)));

        IReadOnlyList<string> versions = await client.GetVersionsAsync(
            PackageId, Feed, credential: null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(versions);
    }

    [Fact]
    public async Task CredentialArgumentAlone_FailsOnTheServiceIndex()
    {
        // The live proof of the gap pinned by
        // ServiceIndexAuthenticationTests.ServiceIndexRequest_DoesNotCarryTheCredential.
        // Azure DevOps authenticates its service index, so a caller that supplies a perfectly
        // good credential as the argument still cannot read the feed: discovery happens first
        // and goes out anonymously.
        RequireFeed();

        NuGetClient client = new(new HttpClient());
        PackageSourceCredential credential = new("pat", Token!);

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetVersionsAsync(PackageId, Feed, credential, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task NoCredential_IsRejectedWithUnauthorizedNotNotFound()
    {
        // Azure DevOps answers an unauthenticated request with 401, never 404. Nothing about
        // this response justifies reporting the package as missing.
        RequireFeed();

        NuGetClient client = new(new HttpClient());

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetVersionsAsync(PackageId, Feed, credential: null, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task GarbageToken_IsRejectedWithUnauthorized()
    {
        // A wrong token and a missing token must not be distinguishable from a missing package.
        RequireFeed();

        NuGetClient client = new(new HttpClient(new BasicAuthHandler("not-a-real-token")));

        HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetVersionsAsync(PackageId, Feed, credential: null, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    private static void RequireFeed()
    {
        Assert.SkipWhen(
            string.IsNullOrEmpty(Feed) || string.IsNullOrEmpty(Token),
            "Set DOTNET_INSPECT_TEST_AZDO_FEED and DOTNET_INSPECT_TEST_AZDO_TOKEN to run live Azure DevOps feed tests.");
    }

    /// <summary>Attaches HTTP Basic with the token as the password, as Azure DevOps expects.</summary>
    private sealed class BasicAuthHandler(string token) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"pat:{token}")));

            return base.SendAsync(request, cancellationToken);
        }
    }
}
