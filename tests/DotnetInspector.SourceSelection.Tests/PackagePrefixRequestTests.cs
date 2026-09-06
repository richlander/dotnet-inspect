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
        var declaration = new PackagePrefixDeclaration(prefix);
        Assert.Equal(prefix, declaration.Prefix);

        var request = new PackagePrefixRequest(prefix, 500, includePrerelease: true);
        var composed = PackagePrefixRequest.Create(declaration, 500, includePrerelease: true);
        Assert.Same(declaration, composed.Declaration);
        Assert.Equal(request, composed);
        Assert.Equal(prefix, request.Prefix);
        Assert.Equal(prefix, request.Declaration.Prefix);
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
    public void MalformedPrefixesAreRejected(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new PackagePrefixDeclaration(prefix));
        Assert.Throws<ArgumentException>(() => new PackagePrefixRequest(prefix, 500));
    }

    [Fact]
    public void PrefixLengthMustAllowACompletePackageId()
    {
        int maximum = PackageCoordinateResolver.MaxPackageIdLength;
        string fullId = new('a', maximum);
        string extendable = new string('a', maximum - 2) + ".";
        foreach (string prefix in new[] { fullId, extendable })
        {
            Assert.Equal(prefix, new PackagePrefixDeclaration(prefix).Prefix);
            Assert.Equal(prefix, new PackagePrefixRequest(prefix, 1).Prefix);
        }

        foreach (string prefix in new[] { fullId + "a", new string('a', maximum - 1) + "." })
        {
            Assert.Throws<ArgumentException>(() => new PackagePrefixDeclaration(prefix));
            Assert.Throws<ArgumentException>(() => new PackagePrefixRequest(prefix, 1));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void NonpositiveBoundsAreRejected(int maximum)
    {
        var declaration = new PackagePrefixDeclaration("Contoso.");
        Assert.Throws<ArgumentOutOfRangeException>(() => PackagePrefixRequest.Create(declaration, maximum));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackagePrefixRequest("Contoso.", maximum));
        Assert.Equal("Contoso.", declaration.Prefix);
    }

    [Fact]
    public void OneDeclarationSupportsIndependentConsumerPolicies()
    {
        var declaration = new PackagePrefixDeclaration("Aspire.");
        var small = PackagePrefixRequest.Create(declaration, 5);
        var large = PackagePrefixRequest.Create(declaration, int.MaxValue, includePrerelease: true);

        Assert.Same(declaration, small.Declaration);
        Assert.Same(declaration, large.Declaration);
        Assert.Equal("Aspire.", small.Prefix);
        Assert.Equal("Aspire.", large.Prefix);
        Assert.Equal(5, small.MaxPackages);
        Assert.False(small.IncludePrerelease);
        Assert.Equal(int.MaxValue, large.MaxPackages);
        Assert.True(large.IncludePrerelease);
        Assert.NotEqual(small, large);
        Assert.NotEqual(small, PackagePrefixRequest.Create(declaration, 5, includePrerelease: true));
        Assert.NotEqual(small, PackagePrefixRequest.Create(declaration, 6));
        Assert.NotEqual(small, new PackagePrefixRequest("aspire.", 5));
        Assert.Equal("Aspire.", declaration.Prefix);
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new PackagePrefixDeclaration(null!));
        Assert.Throws<ArgumentNullException>(() => new PackagePrefixRequest(null!, 1));
        Assert.Throws<ArgumentNullException>(() => PackagePrefixRequest.Create(null!, 1));
    }
}
