using ILInspector.MetadataPrimitives;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// A refused package coordinate has to be distinguishable from an ordinary cache miss.
/// </summary>
/// <remarks>
/// A dependency id is read from another package's nuspec, so it is untrusted.
/// <see cref="NuGetCache.TryGetCachedPackage"/> answers null for an unsafe one rather than
/// throwing, which is its contract -- but that made the refusal look exactly like "not cached",
/// and <c>DependencyResolutionService</c> then dropped the dependency from the resolved tree
/// silently. Null is still the right answer for the caller; being quiet about why is not.
/// </remarks>
public class PackageExtractorCoordinateRefusalTests
{
    [Theory]
    [InlineData("CON", "1.0.0")]
    [InlineData("NUL", "1.0.0")]
    [InlineData("Newtonsoft.Json", "1.0.0 ")]
    public async Task TryGetNuspecXmlAsync_UnsafeCoordinate_SaysItRefused(string id, string version)
    {
        NuGetCache.Initialize("dotnet-inspect-test");
        var log = new List<string>();

        // No HttpClient request should be attempted, so a client with no base address is fine:
        // reaching the network would itself be the failure this guards.
        string? nuspec = null;
        var stderr = await StderrCapture.RunAsync(() =>
            nuspec = PackageExtractor.TryGetNuspecXmlAsync(
                new HttpClient(), id, version, log.Add).GetAwaiter().GetResult());

        Assert.Null(nuspec);

        // Asserted on stderr, not on `log`. This test used to supply its own Action<string> and
        // assert the message arrived there, which proved only that the product wrote to a channel
        // the test itself opened: no caller in the product passes a logger here, so the refusal
        // reached nobody. It now goes to stderr unconditionally, which is what a user sees.
        Assert.Contains("refusing unsafe package coordinate", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The positive control: an ordinary coordinate must not be reported as refused, or the
    /// assertion above would pass for a guard that rejects everything.
    /// </summary>
    [Fact]
    public async Task TryGetNuspecXmlAsync_OrdinaryCoordinate_IsNotReportedAsRefused()
    {
        NuGetCache.Initialize("dotnet-inspect-test");
        var log = new List<string>();

        var stderr = await StderrCapture.RunAsync(() =>
            PackageExtractor.TryGetNuspecXmlAsync(
                new HttpClient(), "Newtonsoft.Json", "13.0.3", log.Add).GetAwaiter().GetResult());

        Assert.DoesNotContain("refusing unsafe package coordinate", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The latest-version cache lookup is a ninth path sink: it combined a caller-supplied package
    /// name into a cache root with no validation, and six call sites reach it. The guard lives in
    /// the method rather than at those call sites, because an earlier fix in this series guarded one
    /// route while the identical hole survived on a sibling route with a green suite.
    /// </summary>
    [Theory]
    [InlineData("../foo")]
    [InlineData("..\\foo")]
    [InlineData("CON")]
    [InlineData("Newtonsoft.Json ")]
    public void TryGetLatestCachedVersion_UnsafeName_ReturnsNullWithoutProbingTheCache(string name)
    {
        NuGetCache.Initialize("dotnet-inspect-test");

        Assert.Null(NuGetCache.TryGetLatestCachedVersion(name));
    }

    /// <summary>
    /// The positive control for the guard above. Without it, a guard that refused every name would
    /// satisfy the refusal theory just as well as a correct one. This asserts only that a
    /// well-formed name is not refused by the path rule -- it may legitimately be absent from the
    /// cache, so a null result here is only a failure if the name was rejected as unsafe.
    /// </summary>
    [Fact]
    public void TryGetLatestCachedVersion_LegitimateName_IsNotRefusedByThePathRule()
    {
        Assert.True(HardenedPath.IsSafePathComponent("Newtonsoft.Json"));
        Assert.True(HardenedPath.IsSafePathComponent("System.Text.Json"));
        Assert.True(HardenedPath.IsSafePathComponent("Valid..Dependency"));
    }

    /// <summary>
    /// The latest-version lookup is the route <c>package &lt;name&gt;</c> takes when no version is
    /// pinned, and it was unguarded: <c>package CON --version</c> refused cleanly because
    /// <c>PackageCommand</c>'s version branch validated the name, while <c>package CON</c> reached
    /// the version cache and then nuget.org before reporting a miss.
    /// </summary>
    /// <remarks>
    /// This is the gate for "the refusal is a property of the name, not of which branch the caller
    /// took". The guard is in the method, so both routes now refuse identically.
    /// <para>
    /// The client's handler throws on any request. Asserting only that the result is null would
    /// pass for the wrong reason: nuget.org has no package called <c>con</c> or <c>../foo</c>
    /// either, so two of these three cases returned null with the guard deleted. Refusing without
    /// asking is the property; not finding anything is not.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("CON")]
    [InlineData("../foo")]
    [InlineData("Newtonsoft.Json ")]
    public async Task GetLatestVersionAsync_UnsafeName_ReturnsNullWithoutReachingTheNetwork(string name)
    {
        NuGetCache.Initialize("dotnet-inspect-test");

        var version = await PackageExtractor.GetLatestVersionAsync(
            new HttpClient(new ThrowingHandler()),
            name,
            [new NuGetFetch.PackageSource("nuget.org", "https://api.nuget.org/v3/index.json")],
            log: null,
            skipCache: true);

        Assert.Null(version);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"The guard should have refused before any request was made; got {request.RequestUri}.");
    }

    /// <summary>
    /// The positive control: the guard must not be refusing every name. Asserting on the path rule
    /// rather than on a live lookup keeps this test off the network.
    /// </summary>
    [Fact]
    public void GetLatestVersionAsync_LegitimateName_IsNotRefusedByThePathRule()
    {
        Assert.True(HardenedPath.IsSafePathComponent("newtonsoft.json"));
        Assert.False(HardenedPath.IsSafePathComponent("con"));
    }
}
