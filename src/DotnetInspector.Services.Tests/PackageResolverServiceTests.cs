namespace DotnetInspector.Services.Tests;

public class PackageResolverServiceTests
{
    [Theory]
    [InlineData("Newtonsoft.Json.13.0.3", "Newtonsoft.Json", "13.0.3")]
    [InlineData("System.Text.Json.9.0.0", "System.Text.Json", "9.0.0")]
    [InlineData("Microsoft.Extensions.DependencyInjection.9.0.0", "Microsoft.Extensions.DependencyInjection", "9.0.0")]
    [InlineData("xunit.2.9.3", "xunit", "2.9.3")]
    [InlineData("Foo.1.0.0-preview.1", "Foo", "1.0.0-preview.1")]
    public void ParsePackageFileName_SplitsNameAndVersion(string fileName, string expectedName, string expectedVersion)
    {
        var (name, version) = PackageResolverService.ParsePackageFileName(fileName);

        Assert.Equal(expectedName, name);
        Assert.Equal(expectedVersion, version);
    }

    [Theory]
    [InlineData("NoVersionHere")]
    [InlineData("JustAName")]
    public void ParsePackageFileName_NoVersion_ReturnsFileNameAsName(string fileName)
    {
        var (name, version) = PackageResolverService.ParsePackageFileName(fileName);

        Assert.Equal(fileName, name);
        Assert.Null(version);
    }

    [Fact]
    public async Task ResolvePackageAsync_LocalFile_DoesNotExist_ReturnsNull()
    {
        var result = await PackageResolverService.ResolvePackageAsync(
            "/nonexistent/path/package.nupkg", null, null, new HttpClient());

        Assert.Null(result);
    }

    [Fact]
    public void PackageResolution_Record_Properties()
    {
        var resolution = new PackageResolverService.PackageResolution(
            "/extract", "/temp", "MyPackage", "1.0.0", "/path.nupkg", FromCache: true);

        Assert.Equal("/extract", resolution.ExtractPath);
        Assert.Equal("/temp", resolution.TempDir);
        Assert.Equal("MyPackage", resolution.PackageName);
        Assert.Equal("1.0.0", resolution.Version);
        Assert.Equal("/path.nupkg", resolution.NupkgPath);
        Assert.True(resolution.FromCache);
    }
}
