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

    [Theory]
    [InlineData("find", "System.String", "library")]
    [InlineData("find", "System.String", "platform-library")]
    [InlineData("find", "System.String", "project")]
    [InlineData("find", "System.String", "binary-directory")]
    [InlineData("implements", "IDisposable", "library")]
    [InlineData("implements", "IDisposable", "platform-library")]
    [InlineData("implements", "IDisposable", "project")]
    [InlineData("extensions", "IEnumerable<T>", "library")]
    [InlineData("extensions", "IEnumerable<T>", "platform-library")]
    [InlineData("extensions", "IEnumerable<T>", "project")]
    [InlineData("depends", "System.String", "library")]
    [InlineData("depends", "System.String", "platform-library")]
    [InlineData("depends", "System.String", "project")]
    public async Task EachCommandDirectSource_DoesNotFallBackToPlatform(
        string command,
        string target,
        string sourceKind)
    {
        var (defaultExit, defaultOutput, defaultError) = await RunAppAsync(
            command,
            target,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.True(
            int.Parse(defaultOutput.Trim()) > 0,
            $"The default scope produced no witness for {command} {target}.");

        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-search-scope-{Guid.NewGuid():N}");
        (string Option, string Value) source = sourceKind switch
        {
            "library" => ("--library", $"{missingPath}.dll"),
            "platform-library" => ("--platform", $"Missing.Scope.{Guid.NewGuid():N}"),
            "project" => ("--project", $"{missingPath}.csproj"),
            "binary-directory" => ("--bin", missingPath),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unknown direct source kind.")
        };

        var (_, output, error) = await RunAppAsync(
            command,
            target,
            source.Option,
            source.Value,
            "--count",
            "--tips",
            "q");

        Assert.True(
            string.IsNullOrWhiteSpace(output) || output.Trim() == "0",
            $"Unexpected fallback output: {output}");
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task DependsExplicitSourceMiss_DoesNotFallBackToLibraryMode()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-search-scope-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "depends",
            "System.Console",
            "--library",
            missingLibrary,
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Type 'System.Console' not found in the specified scope.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependsImplicitScope_RetainsBareLibraryFallback()
    {
        var (exit, output, error) = await RunAppAsync(
            "depends",
            "System.Runtime",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.Parse(output.Trim()) > 0);
    }

    [Theory]
    [InlineData("find", "System.String")]
    [InlineData("implements", "IDisposable")]
    [InlineData("extensions", "System.String")]
    public void PackagePrefixGuidance_DisclosesExpansionLimit(
        string command,
        string target)
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(
            [command, target]);
        var option = result.CommandResult.Command.Options.Single(
            candidate => candidate.Name == "--package-prefix");

        Assert.Contains(
            $"up to {ScopeConstants.PackagePrefixExpansionLimit}",
            option.Description,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "docs/workflows/core/type-queries.md",
        "across all Azure AI packages")]
    [InlineData(
        "skills/relationships/SKILL.md",
        "search every package under")]
    public void PackagePrefixCurrentGuidance_DisclosesExpansionLimit(
        string relativePath,
        string exhaustiveClaim)
    {
        string content = File.ReadAllText(Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            relativePath));

        Assert.Contains(
            $"up to {ScopeConstants.PackagePrefixExpansionLimit}",
            content,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            exhaustiveClaim,
            content,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PackagePrefixLimitReached_IsVisible()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(() =>
        {
            CommandLineHelpers.WarnIfPackagePrefixLimitReached(
                ScopeConstants.PackagePrefixExpansionLimit,
                "Contoso.");
            return Task.FromResult(0);
        });

        Assert.Equal(0, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"{ScopeConstants.PackagePrefixExpansionLimit}-package search limit",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "additional matches may be omitted",
            error,
            StringComparison.Ordinal);
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
