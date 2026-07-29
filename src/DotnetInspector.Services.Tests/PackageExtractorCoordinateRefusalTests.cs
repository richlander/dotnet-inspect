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
        var nuspec = await PackageExtractor.TryGetNuspecXmlAsync(
            new HttpClient(), id, version, log.Add);

        Assert.Null(nuspec);
        Assert.Contains(log, m => m.Contains("Refusing unsafe package coordinate", StringComparison.Ordinal));
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

        await PackageExtractor.TryGetNuspecXmlAsync(
            new HttpClient(), "Newtonsoft.Json", "13.0.3", log.Add);

        Assert.DoesNotContain(log, m => m.Contains("Refusing unsafe package coordinate", StringComparison.Ordinal));
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
}
