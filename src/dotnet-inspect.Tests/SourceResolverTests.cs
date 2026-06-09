using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class SourceResolverTests
{
    public SourceResolverTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public async Task ResolveAsync_QualifiedPlatformTypeTypo_PreservesPrefixForSuggestions()
    {
        var source = await SourceResolver.ResolveAsync(
            ["System.Text.Json.JsonSerializizer"],
            explicitPackage: null,
            explicitAssembly: null,
            explicitPlatform: null,
            verbose: false,
            tryQualifiedTypeName: true);

        Assert.Equal("System.Text.Json", source.PlatformAssembly);
        Assert.Equal("JsonSerializizer", source.TypeName);
        Assert.Null(source.PackagePath);
    }

    [Fact]
    public async Task ResolveAsync_PackageTypeSyntax_DoesNotUseRootPlatformFallback()
    {
        var source = await SourceResolver.ResolveAsync(
            ["System.CommandLine", "Command"],
            explicitPackage: null,
            explicitAssembly: null,
            explicitPlatform: null,
            verbose: false,
            tryQualifiedTypeName: true);

        Assert.Equal("System.CommandLine", source.PackagePath);
        Assert.Equal("Command", source.TypeName);
        Assert.Null(source.PlatformAssembly);
    }

    [Fact]
    public async Task ResolveAsync_PackageLikeSingleArg_DoesNotUseRootPlatformFallback()
    {
        var source = await SourceResolver.ResolveAsync(
            ["System.CommandLine"],
            explicitPackage: null,
            explicitAssembly: null,
            explicitPlatform: null,
            verbose: false,
            tryQualifiedTypeName: true);

        Assert.Equal("System.CommandLine", source.PackagePath);
        Assert.Null(source.TypeName);
        Assert.Null(source.PlatformAssembly);
    }
}
