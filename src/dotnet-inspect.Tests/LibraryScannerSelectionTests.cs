using System.Collections.Immutable;
using System.CommandLine;
using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class LibraryScannerSelectionTests
{
    static int s_invocations;

    public LibraryScannerSelectionTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData("aspire", "canonical ecosystem ID")]
    [InlineData("ecosystem.Aspire", "canonical ecosystem ID")]
    [InlineData("ecosystem.missing", "Unknown ecosystem")]
    [InlineData("ecosystem.microsoft-extensions", "has no scanner")]
    public async Task InvalidSelectionFailsBeforeSourceResolution(string id, string message)
    {
        var result = await RunAsync(
            "library", "--platform", "DoesNotExist", "--scanner", id, "--offline");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public void CanonicalSelectionRetainsTheCatalogBinding()
    {
        Assert.Null(LibraryScannerSelection.Resolve("ecosystem.aspire", out var binding));
        Assert.Same(
            Assert.IsType<EcosystemScannerSelectionResult.Known>(
                EcosystemPackCatalog.SelectScanner(EcosystemPackIds.Aspire)).Binding,
            binding);
    }

    [Fact]
    public async Task ScannerWithoutAnIdDoesNotSelectTheDefaultPath()
    {
        var result = await RunAsync(
            "library", typeof(EcosystemIntegrationScanner).Assembly.Location,
            "--scanner", "--json");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--scanner", result.Error);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task SelectedJsonKeepsScopeAndFullResultsSeparate()
    {
        await WithFixtureAsync(true, async path =>
        {
            var selected = await RunAsync(
                "library", path, "--scanner", "ecosystem.aspire", "--json");
            Assert.True(selected.ExitCode == 0, selected.Error);
            using var json = JsonDocument.Parse(selected.Output);
            var scan = json.RootElement.GetProperty("integration_scan");
            Assert.Equal("ecosystem.aspire", scan.GetProperty("scanner").GetString());
            Assert.Equal("complete", scan.GetProperty("status").GetString());
            Assert.Equal(2, scan.GetProperty("signals").GetArrayLength());
            Assert.All(scan.GetProperty("signals").EnumerateArray(), row =>
                Assert.Equal("Aspire", row.GetProperty("integration").GetString()));

            var full = await RunAsync(
                "library", path, "-S", "Integration: Aspire", "--json");
            Assert.True(full.ExitCode == 0, full.Error);
            using var fullJson = JsonDocument.Parse(full.Output);
            Assert.False(fullJson.RootElement.TryGetProperty("integration_scan", out _));
            Assert.Equal(
                fullJson.RootElement.GetProperty("aspire").EnumerateArray()
                    .Select(row => row.GetProperty("name").GetString()),
                scan.GetProperty("signals").EnumerateArray()
                    .Select(row => row.GetProperty("name").GetString()));
        });
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public async Task SelectedRowsRetainScannerScope(string format)
    {
        await WithFixtureAsync(true, async path =>
        {
            var result = await RunAsync(
                "library", path, "--scanner", "ecosystem.aspire", format);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("ecosystem.aspire", result.Output);
            Assert.Contains("AddSample", result.Output);
            Assert.DoesNotContain("AddExample", result.Output);
        });
    }

    [Fact]
    public async Task EmptySelectionIsSuccessfulAndScoped()
    {
        await WithFixtureAsync(false, async path =>
        {
            var result = await RunAsync(
                "library", path, "--scanner", "ecosystem.aspire", "--json");
            Assert.True(result.ExitCode == 0, result.Error);
            using var json = JsonDocument.Parse(result.Output);
            var scan = json.RootElement.GetProperty("integration_scan");
            Assert.Equal("ecosystem.aspire", scan.GetProperty("scanner").GetString());
            Assert.Equal("complete", scan.GetProperty("status").GetString());
            Assert.Empty(scan.GetProperty("signals").EnumerateArray());

            var markdown = await RunAsync(
                "library", path, "--scanner", "ecosystem.aspire", "--markdown");
            Assert.True(markdown.ExitCode == 0, markdown.Error);
            Assert.Contains("ecosystem.aspire", markdown.Output);
            Assert.Contains("complete", markdown.Output);
            Assert.DoesNotContain("ecosystem.aspire", markdown.Output.Split('\n')[0]);

            var count = await RunAsync(
                "library", path, "--scanner", "ecosystem.aspire", "--count");
            Assert.True(count.ExitCode == 0, count.Error);
            Assert.Equal("0", count.Output.Trim());
        });
    }

    [Fact]
    public async Task StructuralDiscoveryDescribesSelectedModeWithoutExecution()
    {
        var result = await RunAsync(
            "library", "--scanner", "ecosystem.aspire", "-D", "--schema", "--trace");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Integration Scan", result.Output);
        Assert.DoesNotContain("SelectedIntegrationScan", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryDiscoveryDoesNotExposeScannerOnlySections(bool tree)
    {
        string[] args = tree
            ? ["library", "-D", "--schema", "--tree"]
            : ["library", "-D", "--schema"];
        var result = await RunAsync(args);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.DoesNotContain(IntegrationSectionNames.Scan, result.Output);
        Assert.Contains("Integration: Aspire", result.Output);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public async Task SelectedRowsWithControlCharactersStayContained(string format)
    {
        await WithFixtureAsync(true, async path =>
        {
            var result = await RunAsync(
                "library", path, "--scanner", "ecosystem.aspire", format);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("INJECTED", result.Output);
            HostileOutputAssert.NoRenderingHazard(result.Output, "selected-integration-scan");
            HostileOutputAssert.NoLineSplit(result.Output, "INJECTED");
        }, aspireMethodName: "AddSample\u000BINJECTED");
    }

    [Fact]
    public async Task PlatformInputUsesTheSelectedOperation()
    {
        var result = await RunAsync(
            "library", "--platform", "System.Text.Json",
            "--scanner", "ecosystem.aspire", "--count", "--offline");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("0", result.Output.Trim());
    }

    [Fact]
    public async Task SelectedModeRequiresItsSection()
    {
        var result = await RunAsync(
            "library", "./missing.dll", "--scanner", "ecosystem.aspire", "-S", "References");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("requires the Integration Scan section", result.Error);
    }

    [Theory]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    public async Task UnsupportedExtractionDoesNotReturnEmptySuccess(string option)
    {
        var result = await RunAsync(
            "library", "./missing.dll", "--scanner", "ecosystem.aspire", option);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("supports section rows, columns, and counts", result.Error);
    }

    [Fact]
    public async Task UnreadableInputDoesNotBecomeASuccessfulZeroCount()
    {
        await WithFixtureAsync(false, async path =>
        {
            await File.WriteAllBytesAsync(path, [0]);
            var result = await RunAsync(
                "library", path, "--scanner", "ecosystem.aspire", "--count");
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("requires a readable managed assembly", result.Error);
            Assert.Empty(result.Output);
        });
    }

    [Fact]
    public void SelectedPlanRepeatsWithoutUsingFullScanCache()
    {
        s_invocations = 0;
        var binding = EcosystemIntegrationScannerBinding.Create(CountEmpty);
        var catalog = LibrarySections.CreateCatalog(binding);
        string path = typeof(EcosystemIntegrationScanner).Assembly.Location;
        var reference = Assert.IsType<ResolvedAssemblyReference>(
            ResolvedAssemblyReference.CreateFromPathIfManaged(
                path, AssemblyResolutionProvenance.Local("scanner test")));
        using var workspace = new InspectionWorkspace();
        using var group = workspace.CreateAssemblyContextGroup(
        [
            new AssemblyContextParticipant(
                reference,
                new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path))),
        ]);
        var plan = catalog.GroupQueryCatalog.Plan(LibraryScannerSelection.Query);
        Assert.Equal([LibraryScannerSelection.Query], plan.Queries);
        Assert.DoesNotContain(LibraryScannerSelection.Query,
            LibrarySections.GroupQueryCatalog.RegisteredQueries);
        for (int operation = 0; operation < 2; operation++)
        {
            var result = plan.Run(group).Get(LibraryScannerSelection.Query);
            Assert.Same(binding, result.Binding);
            Assert.True(result.IsComplete);
            Assert.Empty(Assert.IsType<AssemblyIntegrationsEntry.Selected>(
                Assert.Single(result.Assemblies)).EcosystemSignals);
        }
        Assert.Equal(2, s_invocations);
    }

    [Fact]
    public async Task SelectedAndFullQueriesShareInputButNotResultScope()
    {
        await WithFixtureAsync(true, path =>
        {
            var catalog = LibrarySections.CreateCatalog(EcosystemIntegrationScanner.AspireBinding);
            HashSet<InspectionQueryDefinition> queries =
                [LibraryScannerSelection.Query, AssemblyContextIntegrationsQuery.Definition];
            var batch = Assert.IsType<AssemblyContextIntegrationsBatch>(
                AssemblyContextIntegrationsRunner.RunIfRequested(
                    queries, catalog.GroupQueryCatalog,
                    [new(path, AssemblyResolutionProvenance.Local("scanner fixture"))]));
            var full = Assert.IsType<AssemblyIntegrationsEntry.Available>(batch.EntryFor(path));
            var selected = Assert.IsType<AssemblyIntegrationsEntry.Selected>(batch.ScanEntryFor(path));
            Assert.Equal(
                full.EcosystemSignals.Where(row => row.Integration == "Aspire"),
                selected.EcosystemSignals);
            Assert.Contains(full.EcosystemSignals, row => row.Integration == "Dependency Injection");
            Assert.NotNull(batch.AssemblyForInspection(path));
            Assert.Empty(queries);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RejectedSelectedParticipantRemainsAFailure()
    {
        string path = typeof(EcosystemIntegrationScanner).Assembly.Location;
        var catalog = LibrarySections.CreateCatalog(EcosystemIntegrationScanner.AspireBinding);
        HashSet<InspectionQueryDefinition> queries = [LibraryScannerSelection.Query];
        var batch = Assert.IsType<AssemblyContextIntegrationsBatch>(
            AssemblyContextIntegrationsRunner.RunIfRequested(
                queries, catalog.GroupQueryCatalog,
                [new(path, AssemblyResolutionProvenance.Local("budget fixture"))],
                groupOptions: new AssemblyContextGroupOptions { MaxRetainedImageBytes = 1 }));
        var entry = Assert.IsType<AssemblyIntegrationsEntry.Rejected>(batch.ScanEntryFor(path));
        using var httpClient = new HttpClient();
        LibraryInspection? model = null;
        await ConsoleCapture.RunAsync(async () =>
        {
            model = await LibraryMetadataService.InspectAsync(
                path,
                new LibraryOptions { Scanner = "ecosystem.aspire" },
                new VerboseLogger(false),
                null, null, httpClient,
                integrationScanEntry: entry);
            return 0;
        });
        Assert.NotNull(model);
        Assert.NotNull(model.IntegrationScan);
        Assert.Equal("rejected", model.IntegrationScan.Status);
        Assert.NotNull(model.IntegrationScan.Error);
        Assert.Equal(IntegrationSectionNames.Scan, Assert.Single(model.InspectionFailures!).Section);
        Assert.Equal(1, LibraryCommand.SelectedInspectionFailureExitCode(
            new LibraryOptions { IncludeSections = [IntegrationSectionNames.Scan] },
            catalog.Pipeline, model));
        var diagnostic = await ConsoleCapture.RunAsync(() =>
        {
            Assert.True(LibraryCommand.RejectEmptyExactSection(
                model,
                new LibraryOptions { IncludeSections = [IntegrationSectionNames.Scan] },
                catalog.Pipeline));
        });
        Assert.Contains("ecosystem.aspire", diagnostic.Error);
    }

    [Fact]
    public async Task AllTfmPackageSelectionScopesEveryResult()
    {
        await WithFixtureAsync(true, async path =>
        {
            string package = Path.ChangeExtension(path, ".nupkg");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(path, "lib/net8.0/ScannerFixture.dll");
                archive.CreateEntryFromFile(path, "lib/net9.0/ScannerFixture.dll");
                using var writer = new StreamWriter(archive.CreateEntry("ScannerFixture.nuspec").Open());
                writer.Write("""
                    <?xml version="1.0"?>
                    <package><metadata><id>ScannerFixture</id><version>1.0.0</version>
                    <authors>Test</authors><description>Scanner fixture</description></metadata></package>
                    """);
            }
            var result = await RunAsync(
                "library", "--package", package, "--tfm", "all",
                "--scanner", "ecosystem.aspire", "--json", "--offline");
            Assert.Equal(0, result.ExitCode);
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal(2, json.RootElement.GetArrayLength());
            Assert.All(json.RootElement.EnumerateArray(), library =>
            {
                var scan = library.GetProperty("integration_scan");
                Assert.Equal("ecosystem.aspire", scan.GetProperty("scanner").GetString());
                Assert.Equal(2, scan.GetProperty("signals").GetArrayLength());
            });
        });
    }

    static ImmutableArray<EcosystemIntegrationClassification> CountEmpty(
        EcosystemIntegrationObservationContext context)
    {
        s_invocations++;
        return [];
    }

    static Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args) =>
        ConsoleCapture.RunAsync(async () =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            args = CommandLineBuilder.PreprocessArgs(args, root);
            return await CommandLineBuilder.InvokeAsync(root.Parse(args), args);
        });

    static async Task WithFixtureAsync(
        bool includeAspire, Func<string, Task> action, string aspireMethodName = "AddSample")
    {
        var directory = Directory.CreateTempSubdirectory("cli-scanner-");
        try
        {
            string path = Path.Combine(directory.FullName, "ScannerFixture.dll");
            using var image = EcosystemIntegrationScannerTests.BuildDependencyInjectionExtensionAssembly(
                includeAspire: includeAspire, aspireMethodName: aspireMethodName);
            await File.WriteAllBytesAsync(path, image.ToArray());
            await action(path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
