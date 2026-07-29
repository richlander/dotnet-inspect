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
}
