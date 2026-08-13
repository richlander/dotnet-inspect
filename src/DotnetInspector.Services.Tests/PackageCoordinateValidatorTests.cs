using DotnetInspector.Packages;
using NuGetFetch;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// A package coordinate is both an address and an identity: it becomes flat-container path
/// segments and it keys a content cache. These tests pin both halves — what the grammar accepts,
/// and that a coordinate cannot leave its path segment.
/// </summary>
public class PackageCoordinateValidatorTests
{
    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("newtonsoft.json")]
    [InlineData("System.Text.Json")]
    [InlineData("xunit.v3")]
    [InlineData("Microsoft.Extensions.DependencyInjection.Abstractions")]
    [InlineData("A")]
    [InlineData("a1")]
    [InlineData("my_package")]
    [InlineData("my-package")]
    [InlineData("a.b-c_d.e")]
    public void RealPackageIds_AreAccepted(string packageId) =>
        Assert.True(PackageCoordinateValidator.IsValidPackageId(packageId));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    [InlineData("a%2fb")]
    [InlineData("a b")]
    [InlineData(".leading")]
    [InlineData("trailing.")]
    [InlineData("double..dot")]
    [InlineData("a@b")]
    [InlineData("a:b")]
    public void SegmentBreakingOrMalformedIds_AreRejected(string packageId) =>
        Assert.False(PackageCoordinateValidator.IsValidPackageId(packageId));

    [Fact]
    public void OverlongIds_AreRejectedAtNuGetsOwnLimit()
    {
        Assert.True(PackageCoordinateValidator.IsValidPackageId(
            new string('a', PackageCoordinateValidator.MaxPackageIdLength)));
        Assert.False(PackageCoordinateValidator.IsValidPackageId(
            new string('a', PackageCoordinateValidator.MaxPackageIdLength + 1)));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0")]
    [InlineData("13.0.3")]
    [InlineData("9.0.0-preview.1.24080.9")]
    [InlineData("1.0.0+build.5")]
    [InlineData("1.0.0.4")]
    public void RealVersions_AreAccepted(string version) =>
        Assert.True(PackageCoordinateValidator.IsValidPackageVersion(version));

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("1.0.0/../evil")]
    [InlineData("1.0.0?x=1")]
    [InlineData("1.0.0#frag")]
    [InlineData("1.0.0%2f..%2fevil")]
    // NuGetVersion.TryParse trims surrounding whitespace, so a padded spelling parses but is not
    // the caller's exact text and must not become a path segment or a cache key.
    [InlineData(" 1.0.0")]
    [InlineData("1.0.0 ")]
    [InlineData("latest")]
    [InlineData("v1.0.0")]
    public void SegmentBreakingOrUnparsableVersions_AreRejected(string version) =>
        Assert.False(PackageCoordinateValidator.IsValidPackageVersion(version));

    [Fact]
    public void OverlongVersions_AreRejectedBeforeParsing()
    {
        string overlong = "1.0.0-"
            + new string('a', PackageCoordinateValidator.MaxPackageVersionLength);

        Assert.False(PackageCoordinateValidator.IsValidPackageVersion(overlong));
    }

    [Fact]
    public void Rejections_CarryTheirTypedReason()
    {
        Assert.False(PackageCoordinateValidator.TryValidatePackageId(
            "a/b",
            out PackageCoordinateRejectionKind? idRejection));
        Assert.Equal(PackageCoordinateRejectionKind.InvalidCharacter, idRejection);

        Assert.False(PackageCoordinateValidator.TryValidatePackageId(
            "trailing.",
            out PackageCoordinateRejectionKind? shapeRejection));
        Assert.Equal(PackageCoordinateRejectionKind.InvalidShape, shapeRejection);

        Assert.False(PackageCoordinateValidator.TryValidatePackageVersion(
            "1.0.0 ",
            out PackageCoordinateRejectionKind? versionRejection));
        Assert.Equal(PackageCoordinateRejectionKind.InvalidCharacter, versionRejection);

        Assert.False(PackageCoordinateValidator.TryValidatePackageVersion(
            "notaversion",
            out PackageCoordinateRejectionKind? unparsable));
        Assert.Equal(PackageCoordinateRejectionKind.UnparsableVersion, unparsable);

        Assert.True(PackageCoordinateValidator.TryValidatePackageId(
            "Newtonsoft.Json",
            out PackageCoordinateRejectionKind? accepted));
        Assert.Null(accepted);
    }

    [Fact]
    public async Task OrdinaryCoordinates_ProduceTheUnchangedFlatContainerAddress()
    {
        using var client = new HttpClient(new FailingHandler());

        string? url = await PackageExtractor.GetPackageDownloadUrlAsync(
            client,
            PackageSource.NuGetOrg,
            "newtonsoft.json",
            "13.0.3",
            log: null);

        Assert.Equal(
            $"{PackageSource.NuGetOrg.GetFlatContainerUrl()}"
            + "/newtonsoft.json/13.0.3/newtonsoft.json.13.0.3.nupkg",
            url);
    }

    [Fact]
    public async Task SegmentBreakingCoordinates_CannotLeaveTheirPathSegment()
    {
        using var client = new HttpClient(new FailingHandler());
        string flatContainer = PackageSource.NuGetOrg.GetFlatContainerUrl()!;

        string? url = await PackageExtractor.GetPackageDownloadUrlAsync(
            client,
            PackageSource.NuGetOrg,
            "evil/../../other",
            "1.0.0/../9.9.9?x=1#f",
            log: null);

        Assert.NotNull(url);
        Assert.StartsWith(flatContainer + "/", url, StringComparison.Ordinal);
        Assert.Equal(
            3,
            url[flatContainer.Length..].Count(character => character == '/'));
        Assert.DoesNotContain('?', url[flatContainer.Length..]);
        Assert.DoesNotContain('#', url[flatContainer.Length..]);
        Assert.DoesNotContain("/../", url, StringComparison.Ordinal);
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(
                "No request may be made while building a download address.");
    }
}
