using DotnetInspector.Platforms;

namespace DotnetInspector.SourceSelection.Tests;

public sealed class PlatformLibraryPopulationDeclarationTests
{
    [Theory]
    [InlineData(PlatformFamily.DotNetRuntime)]
    [InlineData(PlatformFamily.AspNetCore)]
    public void PublicConsumerRetainsExactProductFamily(PlatformFamily family)
    {
        var declaration = new PlatformLibraryPopulationDeclaration(family);

        Assert.Equal(family, declaration.Family);
    }

    [Fact]
    public void EqualityDependsOnlyOnExactProductFamily()
    {
        var runtime = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        var equalRuntime = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        var aspNetCore = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.AspNetCore);

        Assert.Equal(runtime, equalRuntime);
        Assert.Equal(runtime.GetHashCode(), equalRuntime.GetHashCode());
        Assert.NotEqual(runtime, aspNetCore);
        Assert.Equal(2, new HashSet<PlatformLibraryPopulationDeclaration>
        {
            runtime,
            equalRuntime,
            aspNetCore,
        }.Count);
    }

    [Fact]
    public void UndefinedProductFamilyIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlatformLibraryPopulationDeclaration((PlatformFamily)2));
    }
}
