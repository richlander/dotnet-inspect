using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class LibraryIntegrationQueryTests
{
    const string AspirePredicate = "ecosystem=ecosystem.aspire";

    public LibraryIntegrationQueryTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData("ecosystem=aspire", "canonical ecosystem ID")]
    [InlineData("ecosystem=ecosystem.Aspire", "canonical ecosystem ID")]
    [InlineData("ecosystem=ecosystem.missing", "Unknown ecosystem")]
    [InlineData("ecosystem=ecosystem.microsoft-extensions", "no CLI Integration query binding")]
    [InlineData("ecosystem!=ecosystem.aspire", "supports only =")]
    [InlineData("ecosystem>=ecosystem.aspire", "supports only =")]
    public async Task InvalidPredicateFailsBeforeSourceResolution(string predicate, string message)
    {
        var result = await RunAsync(
            "library", "--platform", "DoesNotExist", "--where", predicate, "--offline");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("--where", AspirePredicate, "exactly one")]
    [InlineData("--where", "Kind=ObjectCreationExpression", "cannot be combined")]
    [InlineData("--where", "RootReach>=1", "cannot be combined")]
    [InlineData("--order-by", "RootReach desc", "cannot be combined")]
    [InlineData("--top", "1", "cannot be combined")]
    public async Task UnsupportedCompositionFails(string option, string value, string message)
    {
        var result = await RunAsync("library", "--platform", "DoesNotExist",
            "--where", AspirePredicate, option, value, "--offline");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("--schema")]
    [InlineData("--effective")]
    public async Task TopWithCountCannotBypassCompositionValidation(string? discoveryMode)
    {
        List<string> args =
            ["library", "--platform", "DoesNotExist", "--where", AspirePredicate,
             "--top", "1", "--count", "--offline"];
        if (discoveryMode is not null)
            args.AddRange(["-D", "Integration: Aspire", discoveryMode]);

        var result = await RunAsync([.. args]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot be combined", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task NarrowedJsonIsTheOrdinaryAspireProjection()
    {
        await WithFixtureAsync(true, async path =>
        {
            var full = await RunAsync("library", path, "-S", "Integrations", "--json");
            var narrowed = await RunAsync("library", path, "-S", "Integrations",
                "--where", AspirePredicate, "--json");
            Assert.Equal(0, full.ExitCode);
            Assert.True(narrowed.ExitCode == 0, narrowed.Error);
            Assert.Contains("AddPublicThing", full.Output);
            Assert.Contains("AddSample", full.Output);
            Assert.DoesNotContain("AddPublicThing", narrowed.Output);
            Assert.Contains("AddSample", narrowed.Output);
            Assert.DoesNotContain("integration_scan", narrowed.Output);
            using var fullJson = JsonDocument.Parse(full.Output);
            using var queryJson = JsonDocument.Parse(narrowed.Output);
            Assert.Equal(
                fullJson.RootElement.GetProperty("aspire").GetRawText(),
                queryJson.RootElement.GetProperty("aspire").GetRawText());
        });
    }

    [Fact]
    public async Task PredicateWithoutSelectionRequestsTheIntegrationFamily()
    {
        await WithFixtureAsync(true, async path =>
        {
            var result = await RunAsync("library", path, "--where", AspirePredicate, "--json");
            var selected = await RunAsync("library", path, "-S", "Integrations",
                "--where", AspirePredicate, "--json");
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal(selected.Output, result.Output);
        });
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public async Task ConcreteSectionKeepsItsOrdinaryFormat(string format)
    {
        await WithFixtureAsync(true, async path =>
        {
            var full = await RunAsync("library", path, "-S", "Integration: Aspire", format);
            var narrowed = await RunAsync("library", path, "-S", "Integration: Aspire",
                "--where", AspirePredicate, format);
            Assert.True(narrowed.ExitCode == 0, narrowed.Error);
            Assert.Equal(full.Output, narrowed.Output);
        });
    }

    [Theory]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    public async Task RowWindowAndCountAgreeWithoutDocumentContext(string format)
    {
        await WithFixtureAsync(true, async path =>
        {
            var result = await RunAsync("library", path, "-S", "Integration: Aspire",
                "--where", AspirePredicate, format, "--rows", "1");
            Assert.True(result.ExitCode == 0, result.Error);
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (format == "--jsonl")
            {
                using var row = JsonDocument.Parse(Assert.Single(lines));
                Assert.Contains("Sample", row.RootElement.GetProperty("api").GetString());
            }
            else
            {
                Assert.Equal(2, lines.Length);
                Assert.Equal(lines[0].Split('\t').Length, lines[1].Split('\t').Length);
            }
            var count = await RunAsync("library", path, "-S", "Integration: Aspire",
                "--where", AspirePredicate, "--rows", "1", "--count");
            Assert.Equal(0, count.ExitCode);
            Assert.Equal("1", count.Output.Trim());
        });
    }

    [Fact]
    public async Task UnmatchedSelectedConceptIsEmpty()
    {
        await WithFixtureAsync(true, async path =>
        {
            var result = await RunAsync("library", path, "-S", "Integration: Dependency Injection",
                "--where", AspirePredicate, "--count");
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("0", result.Output.Trim());
        });
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    [InlineData("--markdown")]
    public async Task UnmatchedSelectedConceptKeepsEmptyRowsAndTypedJsonContract(string format)
    {
        await WithFixtureAsync(true, async path =>
        {
            var result = await RunAsync("library", path, "-S", "Integration: Dependency Injection",
                "--where", AspirePredicate, format);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.DoesNotContain("AddPublicThing", result.Output);
            if (format == "--json")
            {
                using var json = JsonDocument.Parse(result.Output);
                Assert.False(json.RootElement.TryGetProperty("dependency_injection", out _));
                Assert.Equal(2, json.RootElement.GetProperty("aspire").GetArrayLength());
            }
            else
            {
                Assert.DoesNotContain("AddSample", result.Output);
            }
            if (format == "--jsonl")
                Assert.Empty(result.Output);
        });
    }

    [Fact]
    public async Task OpportunitiesAreNarrowedBeforeJsonRowsAndCount()
    {
        await WithImageAsync(EcosystemIntegrationScannerTests.BuildCloudClientAssembly(), async path =>
        {
            var full = await RunAsync("library", path, "-S", "Integration: Opportunities", "--json");
            var narrowed = await RunAsync("library", path, "-S", "Integration: Opportunities",
                "--where", AspirePredicate, "--json");
            Assert.True(full.ExitCode == 0, full.Error);
            Assert.True(narrowed.ExitCode == 0, narrowed.Error);
            using var fullJson = JsonDocument.Parse(full.Output);
            using var queryJson = JsonDocument.Parse(narrowed.Output);
            var fullRows = fullJson.RootElement.GetProperty("integration_opportunities").EnumerateArray().ToArray();
            var queryRows = queryJson.RootElement.GetProperty("integration_opportunities").EnumerateArray().ToArray();
            Assert.Contains(fullRows, row => row.GetProperty("integration").GetString() != "Aspire");
            Assert.NotEmpty(queryRows);
            Assert.Equal(
                fullRows.Where(row => row.GetProperty("integration").GetString() == "Aspire")
                    .Select(row => row.GetRawText()),
                queryRows.Select(row => row.GetRawText()));
            var rowResult = await RunAsync("library", path, "-S", "Integration: Opportunities",
                "--where", AspirePredicate, "--jsonl", "--rows", "1", "--columns", "Integration;API");
            Assert.True(rowResult.ExitCode == 0, rowResult.Error);
            using var rowJson = JsonDocument.Parse(rowResult.Output);
            Assert.Equal("Aspire", rowJson.RootElement.GetProperty("integration").GetString());
            Assert.Equal(2, rowJson.RootElement.EnumerateObject().Count());
            var count = await RunAsync("library", path, "-S", "Integration: Opportunities",
                "--where", AspirePredicate, "--count");
            Assert.Equal(0, count.ExitCode);
            Assert.Equal(queryRows.Length.ToString(), count.Output.Trim());
        });
    }

    [Fact]
    public async Task FullEvidenceAndPresenceRemainAuthoritative()
    {
        await WithFixtureAsync(true, async path =>
        {
            HashSet<InspectionQueryDefinition> queries = [AssemblyContextIntegrationsQuery.Definition];
            var batch = Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries, LibrarySections.CreateGroupQueryRegistry(),
                    [new(path, AssemblyResolutionProvenance.Local("ecosystem query test"))]));
            var entry = Assert.IsType<AssemblyIntegrationsEntry.Available>(batch.EntryFor(path));
            using var httpClient = new HttpClient();
            var inspection = Assert.IsType<LibraryInspection>(
                await LibraryMetadataService.InspectAsync(
                    path, new LibraryOptions { IntegrationQuery = AspireQuery() },
                    new VerboseLogger(enabled: false), null, null, httpClient,
                    assemblyReference: batch.AssemblyForInspection(path), integrationsEntry: entry));

            Assert.Same(entry, inspection.AssemblyIntegrationsEntry);
            Assert.Contains(entry.EcosystemSignals,
                signal => signal.GetConcept() == IntegrationConceptCatalog.DependencyInjection);
            Assert.True(inspection.HasDependencyInjectionSupport);
            Assert.True(inspection.HasAspireSupport);
            Assert.Equal(2, inspection.IntegrationCount);
            Assert.Null(inspection.DependencyInjection);
            Assert.Equal(2, inspection.Aspire!.Count);
        });
    }

    [Fact]
    public async Task AdmittedParticipantFailureCannotBecomeAFilteredEmptySuccess()
    {
        await WithFixtureAsync(true, path =>
        {
            HashSet<InspectionQueryDefinition> queries = [AssemblyContextIntegrationsQuery.Definition];
            var batch = Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries, LibrarySections.CreateGroupQueryRegistry(),
                    [new(path, AssemblyResolutionProvenance.Local("ecosystem budget test"))],
                    groupOptions: new AssemblyContextGroupOptions { MaxRetainedImageBytes = 1 }));
            var rejected = Assert.IsType<AssemblyIntegrationsEntry.Rejected>(batch.EntryFor(path));
            var inspection = new LibraryInspection { IntegrationQuery = AspireQuery() };
            LibraryMetadataService.ApplyAssemblyIntegrationsEntry(
                path, inspection, new VerboseLogger(enabled: false), rejected);
            var options = new LibraryOptions
            {
                IntegrationQuery = AspireQuery(),
                IncludeSections = ["Integration: Aspire"],
                Count = true,
            };
            Assert.Same(rejected, inspection.AssemblyIntegrationsEntry);
            Assert.NotEmpty(inspection.InspectionFailures!);
            Assert.Equal(1, LibraryCommand.SelectedInspectionFailureExitCode(
                options, LibrarySections.CreateCatalog().Pipeline, inspection));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MissingAspireDoesNotEraseOtherIntegrationPresence()
    {
        await WithFixtureAsync(false, async path =>
        {
            var full = await RunAsync("library", path, "-S", "Integrations", "--json");
            Assert.Equal(0, full.ExitCode);
            Assert.Contains("AddPublicThing", full.Output);
            var narrowed = await RunAsync("library", path, "-S", "Integration: Aspire",
                "--where", AspirePredicate, "--count");
            Assert.True(narrowed.ExitCode == 0, narrowed.Error);
            Assert.Equal("0", narrowed.Output.Trim());
        });
    }

    [Fact]
    public async Task NonIntegrationSelectionCannotSilentlyDiscardPredicate()
    {
        var result = await RunAsync("library", "/missing/query.dll", "-S", "References",
            "--where", AspirePredicate, "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("targets Integrations", result.Error);
        Assert.Empty(result.Output);
    }

    [Theory]
    [InlineData("--il-offsets", null)]
    [InlineData("--il-offsets", "--schema")]
    [InlineData("--il-offsets", "--effective")]
    [InlineData("--extract-resources", null)]
    [InlineData("--extract-resources", "--schema")]
    [InlineData("--extract-resources", "--effective")]
    public async Task IncompatibleOperationFailsBeforeDiscovery(string operation, string? discoveryMode)
    {
        List<string> args =
            ["library", "/missing/query.dll", "--where", AspirePredicate,
             operation, "/missing/query-operation", "--json"];
        if (discoveryMode is not null)
            args.AddRange(["-D", "Integration: Aspire", discoveryMode]);
        var result = await RunAsync([.. args]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not coordinate or extraction operations", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task StructuralDiscoveryDoesNotRequireScannerOptInOrAcquireTarget()
    {
        var result = await RunAsync("library", "/missing/query.dll",
            "-D", "Integration: Aspire", "--where", AspirePredicate, "--schema", "--json");
        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Contains(json.RootElement.EnumerateArray(),
            field => field.GetProperty("name").GetString() == "API");
        Assert.DoesNotContain("Integration Scan", result.Output);
    }

    [Fact]
    public async Task UnreadableInputDoesNotBecomeASuccessfulZeroCount()
    {
        await WithFixtureAsync(false, async path =>
        {
            await File.WriteAllBytesAsync(path, [0]);
            var result = await RunAsync("library", path, "-S", "Integration: Aspire",
                "--where", AspirePredicate, "--count");
            Assert.Equal(1, result.ExitCode);
            Assert.NotEmpty(result.Error);
            Assert.Empty(result.Output);
        });
    }

    [Fact]
    public async Task AllTfmPackageQueryPreservesTheExistingFormatBoundary()
    {
        await WithFixtureAsync(true, async path =>
        {
            string package = Path.ChangeExtension(path, ".nupkg");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(path, "lib/net8.0/QueryFixture.dll");
                archive.CreateEntryFromFile(path, "lib/net9.0/QueryFixture.dll");
                using var writer = new StreamWriter(archive.CreateEntry("QueryFixture.nuspec").Open());
                writer.Write("""
                    <?xml version="1.0"?>
                    <package><metadata><id>QueryFixture</id><version>1.0.0</version>
                    <authors>Test</authors><description>Query fixture</description></metadata></package>
                    """);
            }
            var result = await RunAsync("library", "--package", package, "--tfm", "all",
                "-S", "Integration: Aspire", "--where", AspirePredicate, "--json", "--offline");
            Assert.True(result.ExitCode == 0, result.Error);
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal(2, json.RootElement.GetArrayLength());
            Assert.All(json.RootElement.EnumerateArray(), library =>
                Assert.Equal(2, library.GetProperty("aspire").GetArrayLength()));
            var jsonl = await RunAsync("library", "--package", package, "--tfm", "all",
                "-S", "Integration: Aspire", "--where", AspirePredicate, "--jsonl", "--offline");
            Assert.Equal(1, jsonl.ExitCode);
            Assert.Contains("requires exactly one table shape", jsonl.Error);
            Assert.Empty(jsonl.Output);
        });
    }

    static Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
        => ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(root.Parse(args), args);
        });

    static IntegrationQueryOptions AspireQuery()
    {
        Assert.True(IntegrationQueryOptions.TryExtract(
            [AspirePredicate], out var query, out _, out var error), error.ToString());
        return query;
    }

    static Task WithFixtureAsync(bool includeAspire, Func<string, Task> action)
        => WithImageAsync(EcosystemIntegrationScannerTests.BuildDependencyInjectionExtensionAssembly(
            includeAspire: includeAspire), action);

    static async Task WithImageAsync(MemoryStream image, Func<string, Task> action)
    {
        using var ownedImage = image;
        var directory = Directory.CreateTempSubdirectory("cli-integration-query-");
        try
        {
            string path = Path.Combine(directory.FullName, "QueryFixture.dll");
            await File.WriteAllBytesAsync(path, image.ToArray());
            await action(path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
