using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Composing package resource URLs from a feed-declared base address. The base
/// arrives in a service index the source controls, so it is untrusted input in
/// a URL-shaped position, and string concatenation onto it is not URL
/// construction.
/// </summary>
public sealed class PackageResourceUrlTests
{
    [Theory]
    [InlineData("https://feed.test/flat/")]
    [InlineData("https://feed.test/flat")]
    public void Combine_TreatsATrailingSlashAsOptional(string baseAddress)
    {
        Assert.Equal(
            "https://feed.test/flat/sample.package/1.2.3/sample.package.1.2.3.nupkg",
            PackageResourceUrl.Combine(
                baseAddress,
                "sample.package",
                "1.2.3",
                "sample.package.1.2.3.nupkg"));
    }

    /// <summary>
    /// A pre-signed base carries its signature in the query. Appending the
    /// package path as text puts that path inside the query value, so the
    /// request asks the container root for nothing and the signature is
    /// corrupted; the path has to be appended to the path.
    /// </summary>
    [Fact]
    public void Combine_AppendsToThePathAndPreservesASignedQuery()
    {
        Assert.Equal(
            "https://feed.test/flat/sample/1.0.0/sample.1.0.0.nupkg?sig=abc",
            PackageResourceUrl.Combine(
                "https://feed.test/flat?sig=abc",
                "sample",
                "1.0.0",
                "sample.1.0.0.nupkg"));
    }

    [Fact]
    public void Combine_PreservesASignedQueryOnASlashTerminatedBase()
    {
        Assert.Equal(
            "https://feed.test/flat/sample/1.0.0/sample.1.0.0.nupkg?sig=abc&v=2",
            PackageResourceUrl.Combine(
                "https://feed.test/flat/?sig=abc&v=2",
                "sample",
                "1.0.0",
                "sample.1.0.0.nupkg"));
    }

    [Fact]
    public void Combine_DropsAFragmentTheRequestWouldNeverSend()
    {
        Assert.Equal(
            "https://feed.test/flat/sample/1.0.0/sample.1.0.0.nupkg",
            PackageResourceUrl.Combine(
                "https://feed.test/flat#anchor",
                "sample",
                "1.0.0",
                "sample.1.0.0.nupkg"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative/flat")]
    [InlineData("flat/")]
    [InlineData("ftp://feed.test/flat")]
    [InlineData("file:///tmp/flat")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:secret@feed.test/flat")]
    public void Combine_RefusesAnUnusableBaseAddress(string? baseAddress)
    {
        Assert.Null(
            PackageResourceUrl.Combine(
                baseAddress,
                "sample",
                "1.0.0",
                "sample.1.0.0.nupkg"));
    }

    [Fact]
    public void Combine_RefusesAnEmptySegmentList()
    {
        Assert.Null(PackageResourceUrl.Combine("https://feed.test/flat"));
        Assert.Null(
            PackageResourceUrl.Combine("https://feed.test/flat", "sample", ""));
    }

    /// <summary>
    /// Segments are escaped as single path components, so a segment that
    /// reached this point without validation still cannot introduce a path,
    /// query, or fragment of its own.
    /// </summary>
    [Theory]
    [InlineData("../../admin", "https://feed.test/flat/..%2F..%2Fadmin")]
    [InlineData("a?b=c", "https://feed.test/flat/a%3Fb%3Dc")]
    [InlineData("a#b", "https://feed.test/flat/a%23b")]
    [InlineData("a/b", "https://feed.test/flat/a%2Fb")]
    public void Combine_EscapesEachSegment(string segment, string expected)
    {
        Assert.Equal(
            expected,
            PackageResourceUrl.Combine("https://feed.test/flat", segment));
    }

    [Fact]
    public void Combine_LeavesRealCoordinateSpellingsUnchanged()
    {
        Assert.Equal(
            "https://api.nuget.org/v3-flatcontainer/microsoft.extensions.dependencyinjection.abstractions/9.0.0-preview.1/microsoft.extensions.dependencyinjection.abstractions.9.0.0-preview.1.nupkg",
            PackageResourceUrl.Combine(
                "https://api.nuget.org/v3-flatcontainer",
                "microsoft.extensions.dependencyinjection.abstractions",
                "9.0.0-preview.1",
                "microsoft.extensions.dependencyinjection.abstractions.9.0.0-preview.1.nupkg"));
    }
}
