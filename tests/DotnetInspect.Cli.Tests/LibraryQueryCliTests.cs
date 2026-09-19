using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class LibraryQueryCliTests
{
    [Fact]
    public void DiscoveryValues_DeriveFromTheProductRoute()
    {
        LibraryQueryRegisteredTerm registered =
            Assert.Single(LibraryQuery.RegisteredTerms);
        SectionQueryKey key = Assert.Single(
            LibraryQueryOptions.QueryKeys);

        Assert.Equal(registered.Descriptor.Key, key.Name);
        Assert.Equal(["="], key.Comparisons);
        Assert.Equal("assembly simple name", key.ValueKind);
    }

    [Fact]
    public async Task QueryHelp_DescribesReferencesWithoutAcquiringPopulation()
    {
        var result = await RunAsync(
            "library",
            "query",
            "/missing/LibraryQuery.dll",
            "-Q",
            "Libraries",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement section = Assert.Single(
            json.RootElement.GetProperty("sections").EnumerateArray());
        JsonElement facet = Assert.Single(
            section.GetProperty("facets").EnumerateArray());
        Assert.Equal("references", facet.GetProperty("name").GetString());
        Assert.Equal(
            "--where \"references=System.Text.Json\"",
            facet.GetProperty("example").GetString());
    }

    [Fact]
    public async Task ExplicitLibrary_MatchesDirectReference()
    {
        string path = typeof(LibraryQueryCliTests).Assembly.Location;

        var result = await RunAsync(
            "library",
            "query",
            path,
            "--where",
            "references=System.Runtime");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("DotnetInspect.Cli.Tests", result.Output);
        Assert.Contains("System.Runtime", result.Output);
        Assert.DoesNotContain("Evidence", result.Output);
        Assert.DoesNotContain("Occurrence", result.Output);
    }

    [Fact]
    public async Task Directory_PreservesMatchBesideInvalidCandidate()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"library-query-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string valid = Path.Combine(directory, "A.Valid.dll");
            string invalid = Path.Combine(directory, "B.Invalid.dll");
            File.Copy(
                typeof(LibraryQueryCliTests).Assembly.Location,
                valid);
            await File.WriteAllTextAsync(
                invalid,
                "not a managed assembly",
                TestContext.Current.CancellationToken);

            var result = await RunAsync(
                "library",
                "query",
                directory,
                "--where",
                "references=System.Runtime");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("DotnetInspect.Cli.Tests", result.Output);
            Assert.Contains("B.Invalid.dll", result.Error);
            Assert.Contains("EvaluationFailures", result.Error);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "A.Valid.dll"));
            File.Delete(Path.Combine(directory, "B.Invalid.dll"));
            Directory.Delete(directory);
        }
    }

    private static Task<(int ExitCode, string Output, string Error)> RunAsync(
        params string[] arguments) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(arguments, root);
            return CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });
}
