using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public class PackageExtractorTests
{
    [Theory]
    [InlineData("Newtonsoft.Json.13.0.3.nupkg", "Newtonsoft.Json", "13.0.3")]
    [InlineData("System.Text.Json.9.0.0.nupkg", "System.Text.Json", "9.0.0")]
    [InlineData("Microsoft.Extensions.DependencyInjection.9.0.0.nupkg", "Microsoft.Extensions.DependencyInjection", "9.0.0")]
    [InlineData("xunit.2.9.3.nupkg", "xunit", "2.9.3")]
    [InlineData("Foo.1.0.0-preview.1.nupkg", "Foo", "1.0.0-preview.1")]
    public void ParsePackageReference_NupkgFile_SplitsNameAndVersion(string fileName, string expectedName, string expectedVersion)
    {
        var (name, version) = PackageExtractor.ParsePackageReference(fileName);

        Assert.Equal(expectedName, name);
        Assert.Equal(expectedVersion, version);
    }

    [Theory]
    [InlineData("NoVersionHere")]
    [InlineData("JustAName")]
    public void ParsePackageReference_NoVersion_ReturnsNameOnly(string packageRef)
    {
        var (name, version) = PackageExtractor.ParsePackageReference(packageRef);

        Assert.Equal(packageRef, name);
        Assert.Null(version);
    }

    [Theory]
    [InlineData("System.Text.Json", "system.text.json", "")]
    [InlineData("System.Text.Json@8.0.5", "system.text.json", "8.0.5")]
    [InlineData("Foo@11.0.0-PREVIEW*", "foo", "11.0.0-preview*")]
    public void ParsePackageTarget_RemoteReference_NormalizesNameAndVersion(
        string packageRef,
        string expectedName,
        string expectedVersion)
    {
        var target = PackageExtractor.ParsePackageTarget(packageRef);

        Assert.False(target.IsLocalFile);
        Assert.Equal(packageRef, target.OriginalArgument);
        Assert.Equal(expectedName, target.PackageName);
        Assert.Equal(expectedVersion, target.Version);
    }

    [Fact]
    public void ParsePackageTarget_ExplicitVersion_OverridesEmbeddedVersion()
    {
        var target = PackageExtractor.ParsePackageTarget("System.Text.Json@8.0.0", explicitVersion: "9.0.0");

        Assert.False(target.IsLocalFile);
        Assert.Equal("system.text.json", target.PackageName);
        Assert.Equal("9.0.0", target.Version);
    }

    [Fact]
    public void ParsePackageTarget_LocalNupkg_UsesCliLocalTargetShape()
    {
        var target = PackageExtractor.ParsePackageTarget("/tmp/System.Text.Json.9.0.0.nupkg");

        Assert.True(target.IsLocalFile);
        Assert.Equal("/tmp/System.Text.Json.9.0.0.nupkg", target.OriginalArgument);
        Assert.Equal("System.Text.Json.9.0.0", target.PackageName);
        Assert.Equal("local", target.Version);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("latest", true)]
    [InlineData("LATEST", true)]
    [InlineData("1.0.0", true)]
    [InlineData("13.0.3-beta1", true)]
    [InlineData("11.0.0-preview*", true)]
    [InlineData("not-a-version", false)]
    public void IsValidPackageReferenceVersion_AllowsNuGetLatestAndWildcardVersions(string? version, bool expected)
    {
        Assert.Equal(expected, PackageExtractor.IsValidPackageReferenceVersion(version));
    }

    [Fact]
    public async Task ExtractPackageAsync_LocalFile_DoesNotExist_ReturnsError()
    {
        var outcome = await PackageExtractor.ExtractPackageAsync(
            new HttpClient(), "/nonexistent/path/package.nupkg");

        Assert.False(outcome.IsSuccess);
        Assert.Contains("File not found", outcome.ErrorMessage);
    }

    [Fact]
    public void PackageExtractionResult_Record_Properties()
    {
        var resolution = new PackageExtractionResult(
            "/extract", "/temp", "MyPackage", "1.0.0", "/path.nupkg", FromCache: true);

        Assert.Equal("/extract", resolution.ExtractPath);
        Assert.Equal("/temp", resolution.TempDir);
        Assert.Equal("MyPackage", resolution.PackageName);
        Assert.Equal("1.0.0", resolution.Version);
        Assert.Equal("/path.nupkg", resolution.NupkgPath);
        Assert.True(resolution.FromCache);
    }

    [Theory]
    [InlineData("/home/.nuget/packages/newtonsoft.json/13.0.3/lib/net6.0/Newtonsoft.Json.dll", "Newtonsoft.Json", "13.0.3")]
    [InlineData("/cache/system.text.json/9.0.0/lib/net8.0/System.Text.Json.dll", "System.Text.Json", "9.0.0")]
    public void ExtractVersionFromPath_FindsVersion(string dllPath, string packageName, string expectedVersion)
    {
        var version = PackageExtractor.ExtractVersionFromPath(dllPath, packageName);

        Assert.Equal(expectedVersion, version);
    }

    [Fact]
    public void ExtractVersionFromPath_NoMatch_ReturnsNull()
    {
        var version = PackageExtractor.ExtractVersionFromPath("/some/random/path.dll", "SomePackage");

        Assert.Null(version);
    }
}
