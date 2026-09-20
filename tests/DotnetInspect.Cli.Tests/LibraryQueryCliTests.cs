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
    public async Task ProjectedJson_PreservesDuplicateOccurrenceIdentity()
    {
        string path = typeof(LibraryQueryCliTests).Assembly.Location;

        var result = await RunAsync(
            "library",
            "query",
            path,
            path,
            "--where",
            "references=System.Runtime",
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement[] libraries =
            [.. json.RootElement.GetProperty("libraries").EnumerateArray()];

        Assert.Equal(2, libraries.Length);
        Assert.Equal("0", libraries[0].GetProperty("occurrence").GetString());
        Assert.Equal("1", libraries[1].GetProperty("occurrence").GetString());
    }

    [Fact]
    public async Task ProjectedJson_EmptyOccurrenceProjectionReturnsEmptyArray()
    {
        string path = typeof(LibraryQueryCliTests).Assembly.Location;

        var result = await RunAsync(
            "library",
            "query",
            path,
            "--where",
            "references=No.Such.Reference",
            "--json",
            "--columns",
            "Occurrence");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Empty(
            json.RootElement.GetProperty("libraries").EnumerateArray());
    }

    [Fact]
    public async Task Jsonl_PreservesDuplicateOccurrenceIdentity()
    {
        string path = typeof(LibraryQueryCliTests).Assembly.Location;

        var result = await RunAsync(
            "library",
            "query",
            path,
            path,
            "--where",
            "references=System.Runtime",
            "--jsonl");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        string[] lines = result.Output.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);
        Assert.Equal(
            "0",
            first.RootElement.GetProperty("occurrence").GetString());
        Assert.Equal(
            "1",
            second.RootElement.GetProperty("occurrence").GetString());
    }

    [Fact]
    public async Task Jsonl_EmptyOccurrenceProjectionReturnsNoRows()
    {
        string path = typeof(LibraryQueryCliTests).Assembly.Location;

        var result = await RunAsync(
            "library",
            "query",
            path,
            "--where",
            "references=No.Such.Reference",
            "--jsonl",
            "--columns",
            "Occurrence");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Empty(result.Output);
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

    [Fact]
    public async Task Directory_CandidateLimitReportsFullPopulation()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"library-query-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string fileName in
                new[] { "A.First.dll", "B.Second.dll", "C.Third.dll" })
            {
                File.Copy(
                    typeof(LibraryQueryCliTests).Assembly.Location,
                    Path.Combine(directory, fileName));
            }

            var result = await RunAsync(
                "library",
                "query",
                directory,
                "--where",
                "references=System.Runtime",
                "--take",
                "1");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("CandidateLimitReached", result.Error);
            Assert.Contains("1/3 occurrences evaluated", result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Directory_AdmitsUppercaseDllExtension()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"library-query-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(
                typeof(LibraryQueryCliTests).Assembly.Location,
                Path.Combine(directory, "UPPER.DLL"));

            var result = await RunAsync(
                "library",
                "query",
                directory,
                "--where",
                "references=System.Runtime");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Contains("DotnetInspect.Cli.Tests", result.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitNonLibraryFile_IsRejectedBeforeAcquisition()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"library-query-{Guid.NewGuid():N}.txt");
        File.Copy(typeof(LibraryQueryCliTests).Assembly.Location, path);
        try
        {
            var result = await RunAsync(
                "library",
                "query",
                path,
                "--where",
                "references=System.Runtime");

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(
                "must be a .dll or .exe file or a directory",
                result.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidIntentRejectsBeforeSourceAdmission()
    {
        var result = await RunAsync(
            "library",
            "query",
            "/missing/not-a-library.txt",
            "--where",
            "unknown=value");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "does not define term 'unknown'",
            result.Error);
        Assert.DoesNotContain(
            "must be a .dll or .exe file or a directory",
            result.Error);
    }

    [Fact]
    public async Task CandidateLimitCannotBecomeExactCountThroughHeadSelection()
    {
        string path = typeof(LibraryQueryCliTests).Assembly.Location;

        var result = await RunAsync(
            "library",
            "query",
            path,
            path,
            "--where",
            "references=System.Runtime",
            "--take",
            "1",
            "-n",
            "1",
            "--head",
            "--count");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "cannot produce an exact count",
            result.Error);
        Assert.Contains("CandidateLimitReached", result.Error);
    }

    [Fact]
    public async Task EvaluationFailureCannotBecomeExactCountThroughHeadSelection()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"library-query-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "A.Invalid.dll"),
                "not a managed assembly",
                TestContext.Current.CancellationToken);
            File.Copy(
                typeof(LibraryQueryCliTests).Assembly.Location,
                Path.Combine(directory, "B.Valid.dll"));

            var result = await RunAsync(
                "library",
                "query",
                directory,
                "--where",
                "references=System.Runtime",
                "-n",
                "1",
                "--head",
                "--count");

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(
                "cannot produce an exact count",
                result.Error);
            Assert.Contains("EvaluationFailures", result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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
