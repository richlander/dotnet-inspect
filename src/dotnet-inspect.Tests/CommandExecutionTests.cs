using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

/// <summary>
/// Integration tests that verify actual command execution produces correct output.
/// Uses platform libraries and the test assembly itself as data sources — no network required.
/// </summary>
[Collection("Console")]
public class CommandExecutionTests
{
    private static readonly string TestAssemblyPath =
        typeof(CommandExecutionTests).Assembly.Location;

    public CommandExecutionTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    // ── api command ──────────────────────────────────────────────────

    [Fact]
    public async Task Api_PlatformLibrary_ListsTypes()
    {
        var options = new ApiOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Api_PlatformLibrary_WithTypeFilter_ShowsMembers()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Serialize", output);
        Assert.Contains("Deserialize", output);
    }

    [Fact]
    public async Task Api_PlatformLibrary_JsonOutput()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            JsonOutput = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Should be valid JSON
        var doc = JsonDocument.Parse(output);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task Api_PlatformLibrary_OneLine()
    {
        var options = new ApiOptions
        {
            PlatformAssembly = "System.Text.Json",
            OneLine = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);

        // OneLine format produces tab-separated or columnar output
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1, "Expected multiple lines of type output");
    }

    [Fact]
    public async Task Api_NonexistentPackage_ShowsError()
    {
        var options = new ApiOptions { PackagePath = "NonexistentPackage123456" };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Api_LocalAssembly_ListsTypes()
    {
        var options = new ApiOptions { AssemblyPath = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => ApiCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("CommandExecutionTests", output);
    }

    // ── find command ─────────────────────────────────────────────────

    [Fact]
    public async Task Find_PlatformLibrary_FindsType()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_NoPattern_ShowsError()
    {
        var options = new FindOptions
        {
            Pattern = "",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No pattern", error);
    }

    // ── assembly command ─────────────────────────────────────────────

    [Fact]
    public async Task Assembly_PlatformLibrary_ShowsInfo()
    {
        var options = new AssemblyOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task Assembly_LocalAssembly_ShowsInfo()
    {
        var options = new AssemblyOptions { AssemblyName = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => AssemblyCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("dotnet-inspect.Tests", output);
    }

    // ── package command ──────────────────────────────────────────────

    [Fact]
    public async Task Package_NonexistentPackage_ShowsError()
    {
        var options = new InspectionOptions
        {
            PackageArgs = ["NonexistentPackage123456"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }
}
