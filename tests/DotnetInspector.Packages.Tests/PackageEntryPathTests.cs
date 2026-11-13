using DotnetInspector.Packages;

namespace DotnetInspector.Packages.Tests;

public sealed class PackageEntryPathTests
{
    [Theory]
    [InlineData("lib/net10.0/Example.Component.dll", "Example.Component.dll")]
    [InlineData(@"tools\net10.0\any\Example.Component.dll", "Example.Component.dll")]
    [InlineData("Example.Component.dll", "Example.Component.dll")]
    [InlineData("lib/net10.0/", "")]
    public void GetLeafName_UsesArchiveSeparators(
        string path,
        string expected)
    {
        Assert.Equal(expected, PackageEntryPath.GetLeafName(path));
    }

    [Theory]
    [InlineData("lib/net10.0/Example.Component.dll", "Example.Component")]
    [InlineData(@"tools\net10.0\any\Example.Component", "Example")]
    [InlineData("Example", "Example")]
    [InlineData(".dll", "")]
    public void GetStemName_RemovesOnlyTheLeafFinalExtension(
        string path,
        string expected)
    {
        Assert.Equal(expected, PackageEntryPath.GetStemName(path));
    }

    [Theory]
    [InlineData("content/readme.md", ".md")]
    [InlineData(@"content\readme.component.txt", ".txt")]
    [InlineData("readme", "")]
    [InlineData("readme.", "")]
    public void GetExtension_UsesOnlyTheLeafFinalExtension(
        string path,
        string expected)
    {
        Assert.Equal(expected, PackageEntryPath.GetExtension(path));
    }

    [Fact]
    public void CompileAsset_ProvidesOwnerIssuedAssemblyNames()
    {
        var asset = new PackageCompileAsset(
            "Example.Component",
            "ref/net10.0/Example.Component.dll",
            "Example.Component.dll",
            "net10.0",
            PackageCompileAssetKind.Reference);

        Assert.Equal("Example.Component.dll", asset.AssemblyName);
        Assert.Equal("Example.Component", asset.AssemblySimpleName);
    }
}
