using DotnetInspector.Packages;

namespace DotnetInspector.SourceSelection.Tests;

public sealed class PackagePrefixRequestTests
{
    [Theory]
    [InlineData("Aspire.")]
    [InlineData("CommunityToolkit.Aspire.")]
    [InlineData("Contoso-")]
    [InlineData("a")]
    [InlineData("_")]
    [InlineData("Contoso_Core")]
    public void PublicConsumerRetainsExactBoundedRequest(string prefix)
    {
        var request = new PackagePrefixRequest(prefix, 500, includePrerelease: true);
        Assert.Equal(prefix, request.Prefix);
        Assert.Equal(500, request.MaxPackages);
        Assert.True(request.IncludePrerelease);
        Assert.False(new PackagePrefixRequest(prefix, 1).IncludePrerelease);
        Assert.Equal(int.MaxValue, new PackagePrefixRequest(prefix, int.MaxValue).MaxPackages);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" Contoso.")]
    [InlineData("Contoso. ")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData(".Contoso")]
    [InlineData("Contoso..")]
    [InlineData("Contoso.-")]
    [InlineData("Contoso--Core")]
    [InlineData("Contoso.*")]
    [InlineData("Contoso?")]
    [InlineData("Contoso/Core")]
    [InlineData("Contoso\\Core")]
    [InlineData("Contoso:Core")]
    [InlineData("Contoso\u00e9")]
    [InlineData("Contoso\0")]
    [InlineData("Contoso\n")]
    public void MalformedPrefixesAreRejected(string prefix) =>
        Assert.Throws<ArgumentException>(() => new PackagePrefixRequest(prefix, 500));

    [Fact]
    public void PrefixLengthMustAllowACompletePackageId()
    {
        int maximum = PackageCoordinateResolver.MaxPackageIdLength;
        string fullId = new('a', maximum);
        string extendable = new string('a', maximum - 2) + ".";
        Assert.Equal(fullId, new PackagePrefixRequest(fullId, 1).Prefix);
        Assert.Equal(extendable, new PackagePrefixRequest(extendable, 1).Prefix);
        Assert.Throws<ArgumentException>(() => new PackagePrefixRequest(fullId + "a", 1));
        Assert.Throws<ArgumentException>(() =>
            new PackagePrefixRequest(new string('a', maximum - 1) + ".", 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NonpositiveBoundsAreRejected(int maximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackagePrefixRequest("Contoso.", maximum));

    [Fact]
    public void NullPrefixIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new PackagePrefixRequest(null!, 1));
}
