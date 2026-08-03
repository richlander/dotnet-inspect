using System.Net;
using NuGetFetch;
using NuGetFetch.Plugins;
using Xunit;

namespace NuGetFetch.Tests;

/// <summary>
/// Live coverage of the real Azure Artifacts credential provider against a real private feed.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PluginProtocolTests"/> proves we speak the protocol correctly to a fake that we
/// wrote. Only this suite proves we speak it correctly to the provider Microsoft ships, which is
/// the claim that actually matters: that a private feed can be read with no credential stored in
/// nuget.config at all.
/// </para>
/// <para>
/// Excluded from PR CI by <c>Network=Live</c> and by skipping unless the environment supplies a
/// feed and a token. To run:
/// </para>
/// <code>
/// dotnet tool install --global Microsoft.Artifacts.CredentialProvider.NuGet.Tool
/// export DOTNET_INSPECT_TEST_AZDO_FEED=https://pkgs.dev.azure.com/ORG/PROJECT/_packaging/FEED/nuget/v3/index.json
/// export ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN=&lt;PAT with Packaging read, or an Entra access token&gt;
/// export ARTIFACTS_CREDENTIALPROVIDER_URI_PREFIXES=https://pkgs.dev.azure.com/ORG/
/// dotnet run --project src/NuGetFetch.Tests -c Release -- -trait "Network=Live"
/// </code>
/// <para>
/// The provider is fed its token through the environment rather than a prompt because these run
/// unattended. Note that the v2.0.2 linux-x64 tool package ships no <c>msalruntime.so</c>, so
/// interactive and broker sign-in paths throw <c>DllNotFoundException</c> on Linux; the
/// environment-token path is unaffected.
/// </para>
/// </remarks>
[Trait("Network", "Live")]
public sealed class AzureDevOpsCredentialProviderTests
{
    private static string? Feed => Environment.GetEnvironmentVariable("DOTNET_INSPECT_TEST_AZDO_FEED");

    private static string? ProviderToken =>
        Environment.GetEnvironmentVariable("ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN");

    [Fact]
    public async Task InstalledProviderIsDiscovered()
    {
        RequireProvider();

        await using var provider = new PluginCredentialProvider();

        // Discovery finds nothing in nuget.config, because credential providers are not
        // registered there. A global dotnet tool install is found by the PATH route alone.
        Assert.True(provider.HasPlugins, "No NuGet credential plugin was discovered.");
    }

    [Fact]
    public async Task ProviderSuppliesCredentialsForThePrivateFeed()
    {
        RequireProvider();

        await using var provider = new PluginCredentialProvider();

        PackageSourceCredential? credential = await provider.GetCredentialsAsync(
            new Uri(Feed!), isRetry: false, TestContext.Current.CancellationToken);

        Assert.NotNull(credential);
        Assert.False(string.IsNullOrEmpty(credential.Password));
    }

    [Fact]
    public async Task PrivateFeedIsReadableWithNoCredentialInConfiguration()
    {
        RequireProvider();

        await using var provider = new PluginCredentialProvider();
        using var handler = new PluginAuthenticationHandler(provider, new HttpClientHandler());
        using var client = new HttpClient(handler);

        // No credential is supplied anywhere: the feed challenges, the provider answers, and the
        // request is replayed. This is the whole point of the feature.
        using HttpResponseMessage response = await client.GetAsync(Feed!, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CredentialsAreReusedAcrossRequestsToTheSameFeed()
    {
        RequireProvider();

        await using var provider = new PluginCredentialProvider();
        using var handler = new PluginAuthenticationHandler(provider, new HttpClientHandler());
        using var client = new HttpClient(handler);

        using (HttpResponseMessage first = await client.GetAsync(Feed!, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using HttpResponseMessage second = await client.GetAsync(Feed!, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task WithoutAnyCredentialSource_TheFeedChallengeIsSurfaced()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Feed), "Set DOTNET_INSPECT_TEST_AZDO_FEED to run.");

        // Point discovery at nothing, which is what a machine with no provider installed looks
        // like. The 401 must surface rather than being reported as a missing package.
        await using var provider = new PluginCredentialProvider(null, []);
        using var handler = new PluginAuthenticationHandler(provider, new HttpClientHandler());
        using var client = new HttpClient(handler);

        using HttpResponseMessage response = await client.GetAsync(Feed!, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static void RequireProvider()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Feed), "Set DOTNET_INSPECT_TEST_AZDO_FEED to run.");
        Assert.SkipWhen(
            string.IsNullOrEmpty(ProviderToken),
            "Set ARTIFACTS_CREDENTIALPROVIDER_ACCESSTOKEN so the provider can answer unattended.");
    }
}
