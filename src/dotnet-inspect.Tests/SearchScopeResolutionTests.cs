using DotnetInspector.CommandLine;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class SearchScopeResolutionTests
{
    public SearchScopeResolutionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Fact]
    public void NoExplicitSource_UsesOnlyPlatformFrameworks()
    {
        var scope = ScopeResolver.Resolve(new(), [], []);

        Assert.Equal(["runtime", "aspnetcore", "netstandard"], scope.Frameworks);
        Assert.Empty(scope.Packages);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("library")]
    [InlineData("platform-library")]
    [InlineData("project-or-directory")]
    [InlineData("package-prefix")]
    public void EachExplicitSourceKind_SuppressesTheDefault(string sourceKind)
    {
        string[] packages = sourceKind == "package" ? ["Example.Package"] : [];
        string[] libraries = sourceKind == "library" ? ["Example.dll"] : [];
        string? packagePrefix = sourceKind == "package-prefix" ? "Example." : null;
        bool hasOtherSource = sourceKind is "platform-library" or "project-or-directory";

        var scope = ScopeResolver.Resolve(
            new(),
            packages,
            libraries,
            packagePrefix,
            hasOtherSource);

        Assert.Empty(scope.Frameworks);
        Assert.Equal(packages, scope.Packages);
    }

    [Fact]
    public void ExplicitGroups_ComposeInOrderWithoutDuplicatePackages()
    {
        string duplicate = ScopeConstants.ExtensionsPackages[0].ToUpperInvariant();
        string[] explicitPackages = ["Example.Package", duplicate, "example.package"];

        var scope = ScopeResolver.Resolve(
            new(Platform: true, Extensions: true, AspNetCore: true),
            explicitPackages,
            []);

        string[] expectedPackages =
        [
            .. explicitPackages
                .Concat(ScopeConstants.ExtensionsPackages)
                .Concat(ScopeConstants.AspNetCorePackages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        Assert.Equal(ScopeConstants.PlatformFrameworks, scope.Frameworks);
        Assert.Equal(expectedPackages, scope.Packages);
        Assert.Equal("Example.Package", scope.Packages[0]);
        Assert.Equal(duplicate, scope.Packages[1]);
    }

    [Fact]
    public async Task ExplicitMissingDirectory_DoesNotFallBackToPlatform()
    {
        string missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"missing-search-scope-{Guid.NewGuid():N}");

        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.String",
            "--bin",
            missingDirectory,
            "-t",
            "1",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(output);
        Assert.Contains("Directory not found", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("find", "System.String")]
    [InlineData("implements", "IDisposable")]
    [InlineData("extensions", "System.String")]
    [InlineData("depends", "System.String")]
    public void CuratedCompatibilityInput_IsNotRegistered(string command, string target)
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [command, target, "--curated"]);

        Assert.NotEmpty(result.Errors);
        Assert.Contains(
            result.Errors,
            error => error.Message.Contains(
                "Unrecognized command or argument '--curated'",
                StringComparison.Ordinal));
    }

    private static Task<(int Exit, string Output, string Error)> RunAppAsync(
        params string[] args)
    {
        return ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(root.Parse(args), args);
        });
    }
}
