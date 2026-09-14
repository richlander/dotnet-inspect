using System.CommandLine;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class LibraryApiDiffCommandTests
{
    public LibraryApiDiffCommandTests() => NuGetCache.Initialize("dotnet-inspect");

    [Fact]
    public async Task Default_RetainsUnclassifiedTypeChangesAlongsideCompatibility()
    {
        var (exitCode, output, error) = await Run();

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("## Breaking Changes", output);
        Assert.Contains("## Additive Changes", output);
        Assert.Contains("## Other API Changes", output);
        Assert.Contains("TypeDefinitionOnly", output);
        Assert.DoesNotContain("No API changes detected", output);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    public async Task DetailedFormats_RetainUnclassifiedTypeEvidence(string format)
    {
        var (exitCode, output, error) = await Run(
            "-S", "Changes", "--type", "TypeDefinitionOnly", format);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("TypeDefinitionChanged", output);
        Assert.Contains("unclassified", output);
        if (format == "--json")
        {
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement row = Assert.Single(
                document.RootElement.GetProperty("changes").EnumerateArray());
            Assert.Equal(
                "TypeDefinitionChanged", row.GetProperty("kind").GetString());
            Assert.Equal("unclassified", row.GetProperty("classification").GetString());
        }
    }

    [Fact]
    public async Task BreakingFilter_DoesNotClassifyUnassessedChangesAsBreaking()
    {
        var (exitCode, output, error) = await Run(
            "--type", "TypeDefinitionOnly", "--breaking", "--json");

        Assert.Equal(0, exitCode);
        Assert.Contains("classification filter removed all changes", error);
        Assert.DoesNotContain("TypeDefinitionChanged", output);
    }

    [Fact]
    public async Task SameLibrary_IsSuccessfulEmptyRatherThanUnavailable()
    {
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(() =>
            DiffCommand.ExecuteAsync(new()
            {
                LibraryVersionRange = $"{path}..{path}",
            }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("No API changes detected.", output);
        Assert.DoesNotContain("not compared", output);
    }

    [Fact]
    public async Task DifferentLogicalLibraries_AreRejectedWithoutLegacyComparison()
    {
        string before = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string after = FixtureCatalog.DiffV1.AssemblyPath();
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(() =>
            DiffCommand.ExecuteAsync(new()
            {
                LibraryVersionRange = $"{before}..{after}",
                JsonOutput = true,
            }));

        Assert.Equal(1, exitCode);
        Assert.Empty(error);
        Assert.Contains("LogicalLibraryMismatch", output);
        Assert.DoesNotContain("No API changes", output);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.TryGetProperty("changes", out _));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SystemTextJson_PackageVersions_ExposeRealApiAddition()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(() =>
            DiffCommand.ExecuteAsync(new()
            {
                PackageVersionRange = "System.Text.Json@9.0.0..10.0.0",
                Tfm = "net8.0",
                TypeFilter = ["System.Text.Json.JsonSerializerOptions"],
                JsonOutput = true,
            }));

        Assert.True(exitCode == 0, $"{error}\n{output}");
        Assert.Empty(error);
        Assert.Contains("AllowDuplicateProperties", output);
        Assert.Contains("MemberAdded", output);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("changes").ValueKind);
    }

    static Task<(int ExitCode, string Output, string Error)> Run(params string[] options)
    {
        string before = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string after = FixtureCatalog.LibraryApiDiffV2.AssemblyPath();
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", $"{before}..{after}", .. options]);
        return ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());
    }
}
