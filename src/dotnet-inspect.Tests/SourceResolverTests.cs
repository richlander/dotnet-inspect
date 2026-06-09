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

    [Theory]
    [InlineData("string", "System.String")]
    [InlineData("int", "System.Int32")]
    [InlineData("bool", "System.Boolean")]
    [InlineData("object", "System.Object")]
    [InlineData("nint", "System.IntPtr")]
    [InlineData("nuint", "System.UIntPtr")]
    [InlineData("String", "System.String")]
    [InlineData("DateTime", "System.DateTime")]
    [InlineData("Guid", "System.Guid")]
    [InlineData("Math", "System.Math")]
    [InlineData("System.String", "System.String")]
    [InlineData("List<T>", "System.Collections.Generic.List`1")]
    [InlineData("List`1", "System.Collections.Generic.List`1")]
    [InlineData("Dictionary<TKey,TValue>", "System.Collections.Generic.Dictionary`2")]
    [InlineData("Dictionary`2", "System.Collections.Generic.Dictionary`2")]
    [InlineData("Dictionary", "System.Collections.Generic.Dictionary`2")]
    [InlineData("Action", "System.Action")]
    [InlineData("Action`1", "System.Action`1")]
    [InlineData("Tuple", "System.Tuple")]
    [InlineData("Tuple`2", "System.Tuple`2")]
    [InlineData("ValueTuple", "System.ValueTuple")]
    [InlineData("ValueTuple`2", "System.ValueTuple`2")]
    [InlineData("Func`2", "System.Func`2")]
    public async Task ResolveAsync_BareCoreLibType_ResolvesAsPlatformType(string input, string expectedTypeName)
    {
        SkipIfCoreLibUnavailable();

        var source = await SourceResolver.ResolveAsync(
            [input], explicitPackage: null, explicitAssembly: null, explicitPlatform: null, verbose: false);

        Assert.Equal("System.Private.CoreLib", source.PlatformAssembly);
        Assert.Equal(expectedTypeName, source.TypeName);
        Assert.Null(source.PackagePath);
        Assert.Null(source.AssemblyPath);
    }

    [Fact]
    public async Task ResolveAsync_BareCoreLibGenericFamilyWithoutArity_KeepsPackageFallback()
    {
        SkipIfCoreLibUnavailable();

        var source = await SourceResolver.ResolveAsync(
            ["Func"], explicitPackage: null, explicitAssembly: null, explicitPlatform: null, verbose: false);

        Assert.Equal("Func", source.PackagePath);
        Assert.Null(source.PlatformAssembly);
        Assert.Null(source.TypeName);
    }

    [Fact]
    public async Task ResolveAsync_UnknownBareName_KeepsPackageFallback()
    {
        SkipIfCoreLibUnavailable();

        var source = await SourceResolver.ResolveAsync(
            ["Definitely.NotACoreLibType"], explicitPackage: null, explicitAssembly: null, explicitPlatform: null, verbose: false);

        Assert.Equal("Definitely.NotACoreLibType", source.PackagePath);
        Assert.Null(source.PlatformAssembly);
        Assert.Null(source.TypeName);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitSource_DoesNotUseBareCoreLibLookup()
    {
        SkipIfCoreLibUnavailable();

        var source = await SourceResolver.ResolveAsync(
            ["string"], explicitPackage: "Example.Package", explicitAssembly: null, explicitPlatform: null, verbose: false);

        Assert.Equal("Example.Package", source.PackagePath);
        Assert.Equal("string", source.TypeName);
        Assert.Null(source.PlatformAssembly);
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

    private static void SkipIfCoreLibUnavailable()
    {
        var (coreLibPath, _, _, coreLibError) = PlatformResolver.ResolveAssembly("System.Private.CoreLib");
        if (coreLibPath == null || coreLibError != null)
            Assert.Skip($"System.Private.CoreLib not available: {coreLibError}");
    }
}
