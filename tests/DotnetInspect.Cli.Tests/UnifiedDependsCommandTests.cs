using System.CommandLine;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class UnifiedDependsCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory,
        "unified-depends-tests", Guid.NewGuid().ToString("N"));
    private static string Nuspec => FixtureCatalog.RestoredProjectDependencyFacts.AssetPath("manifest.nuspec");
    private static string Assets => FixtureCatalog.RestoredProjectDependencyFacts.AssetPath("project.assets.json");
    private static string Project => Path.Combine(
        FixtureCatalog.RestoredProjectDependencyFacts.ProjectDirectory(),
        "DotnetInspector.RestoredProjectFixtures.csproj");

    public UnifiedDependsCommandTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
        Directory.CreateDirectory(_directory);
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("Type", "--nuspec", "x")]
    [InlineData("Type", "--package-prefix", "Contoso.")]
    [InlineData("Type", "--max-packages", "1")]
    [InlineData("Type", "--preview")]
    [InlineData("--project", "x", "--platform")]
    [InlineData("--nuspec", "x", "--platform-library", "System.Runtime")]
    [InlineData("--library", "x", "--extensions")]
    [InlineData("--package", "x", "--aspnetcore")]
    [InlineData("--package", "x", "--package-prefix", "Contoso.")]
    [InlineData("--library", "x", "--depth", "0")]
    [InlineData("--library", "x", "--depth", "-1")]
    [InlineData("--library", "x", "--depth", "1.5")]
    public void ParserRejectsModeInapplicableGestures(params string[] args)
    {
        var result = CommandLineBuilder.CreateRootCommand().Parse(["depends", .. args]);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("--tsv", "-v:n")]
    [InlineData("--jsonl", "-v:d")]
    [InlineData("--table", "-v:q")]
    [InlineData("--tree", "-v:n")]
    [InlineData("--mermaid", "-v:d")]
    [InlineData("--depth", "1", "-S", "Dependencies")]
    public async Task ShapeAndDepthRejectBeforeAcquisition(params string[] args)
    {
        var result = await Run(["--package", "Not.A.Real.Package@1.0.0", .. args]);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("require", result.Error);
    }

    [Fact]
    public async Task DiscoveryIsStructuralAndIncludesTheDependencyCategory()
    {
        var result = await Run("--package", "Not.A.Real.Package@1.0.0", "-D", "@Dependencies", "--json");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Dependency Graph", result.Output);
        Assert.Contains("Dependencies", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public async Task PositionalMissRemainsATypeAttempt()
    {
        var result = await Run("System.Text",
            "--library", FixtureCatalog.RestoredProjectDependencyFacts.AssemblyPath(),
            "-S", "Roots", "-S", "Failures", "--json");
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement root = Assert.Single(json.RootElement.GetProperty("roots").EnumerateArray());
        Assert.Equal("Type", root.GetProperty("kind").GetString());
        Assert.Equal("Failed", root.GetProperty("admission").GetString());
        Assert.Contains("TypeNotFound", result.Output);
        Assert.DoesNotContain("LibraryUnavailable", result.Output);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ProjectLocatorsRetainTheExactAssetsGraph(int depth)
    {
        var fromProject = await Run("--project", Project, "--depth", depth.ToString(), "-v:d", "--json");
        var fromAssets = await Run("--project", Assets, "--depth", depth.ToString(), "-v:d", "--json");
        Assert.Equal(0, fromProject.ExitCode);
        Assert.Equal(0, fromAssets.ExitCode);
        using var project = JsonDocument.Parse(fromProject.Output);
        using var assets = JsonDocument.Parse(fromAssets.Output);
        Assert.Equal(
            project.RootElement.GetProperty("dependency_graph").GetProperty("edges").GetRawText(),
            assets.RootElement.GetProperty("dependency_graph").GetProperty("edges").GetRawText());
        Assert.Equal(
            project.RootElement.GetProperty("roots")[0].GetProperty("identity").GetRawText(),
            assets.RootElement.GetProperty("roots")[0].GetProperty("identity").GetRawText());
        Assert.Equal("ProjectLocator",
            project.RootElement.GetProperty("roots")[0].GetProperty("evidence").GetProperty("source_kind").GetString());
        Assert.Equal("ProjectAssets",
            assets.RootElement.GetProperty("roots")[0].GetProperty("evidence").GetProperty("source_kind").GetString());
        Assert.All(project.RootElement.GetProperty("dependency_graph").GetProperty("edges").EnumerateArray(),
            edge => Assert.InRange(edge.GetProperty("minimum_depth").GetInt32(), 1, depth));
    }

    [Fact]
    public async Task ProjectTraversalAndEvidenceUseTheSameReturnedFacts()
    {
        DependencyDocument document = await DependsCommand.BuildDocumentAsync(
            Options(new(DependencyRootKind.Project, Assets)) with { Depth = 1 }, true,
            TestContext.Current.CancellationToken);
        var traversal = Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
            Assert.Single(document.RestoredTraversals).Value).Value;
        var root = Assert.Single(document.EvidenceOutcome.Roots);
        var provenance = Assert.IsType<PackageDependencyEvidenceRootProvenance.RestoredProject>(root.Provenance);
        Assert.Equal(traversal.Facts.ContentProvenance, provenance.ContentProvenance);
        Assert.Equal(DependencyCompletion.DepthBounded, document.Traversal);
        Assert.All(document.Graph.Edges, edge => Assert.Equal(1, edge.MinimumDepth));
        Assert.Null(document.PackageTraversal);
    }

    [Fact]
    public async Task EvidenceUniverseDoesNotChangeWithGraphOrDepth()
    {
        var first = await Run("--project", Assets, "-S", "Dependencies", "--json");
        var second = await Run("--project", Assets, "-S", "Dependencies",
            "-S", "Dependency Graph", "--depth", "1", "--json");
        using var evidence = JsonDocument.Parse(first.Output);
        using var graph = JsonDocument.Parse(second.Output);
        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal("NotRequested", evidence.RootElement.GetProperty("summary").GetProperty("traversal").GetString());
        Assert.Equal(evidence.RootElement.GetProperty("dependencies").GetRawText(),
            graph.RootElement.GetProperty("dependencies").GetRawText());
    }

    [Fact]
    public async Task EvidenceOnlyTypeSelectionDoesNotTraverseTheHierarchy()
    {
        DependencyDocument document = await DependsCommand.BuildDocumentAsync(
            Options(new(DependencyRootKind.Type, "System.Int128")) with
            {
                TargetType = "System.Int128",
                PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames,
            },
            traverse: false,
            TestContext.Current.CancellationToken);

        Assert.Equal(DependencyCompletion.NotRequested, document.Traversal);
        Assert.Single(document.Graph.Nodes);
        Assert.Empty(document.Graph.Edges);
        Assert.Empty(document.Graph.Boundaries);
    }

    [Fact]
    public async Task RepeatedHeterogeneousRootsRetainOrderAndFailedSiblings()
    {
        var result = await Run("--nuspec", Nuspec, "--package", "",
            "--project", Assets, "--nuspec", Nuspec, "-v:d", "--json");
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var roots = json.RootElement.GetProperty("roots").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Nuspec", "Package", "Project", "Nuspec" },
            roots.Select(root => root.GetProperty("kind").GetString()));
        Assert.Equal(new[] { 0, 1, 2, 3 }, roots.Select(root => root.GetProperty("occurrence").GetInt32()));
        Assert.False(roots[1].TryGetProperty("identity", out _));
        Assert.Equal(roots[0].GetProperty("identity").GetRawText(), roots[3].GetProperty("identity").GetRawText());
        Assert.Equal("Partial", json.RootElement.GetProperty("summary").GetProperty("traversal").GetString());
        Assert.NotEmpty(json.RootElement.GetProperty("dependency_graph").GetProperty("edges").EnumerateArray());
        int[] edgeRoots = [.. json.RootElement.GetProperty("dependency_graph").GetProperty("edges").EnumerateArray()
            .Select(edge => edge.GetProperty("root_occurrences").EnumerateArray().Min(root => root.GetInt32()))];
        Assert.Equal(edgeRoots.Order(), edgeRoots);
        Assert.Contains(json.RootElement.GetProperty("failures").EnumerateArray(),
            failure => failure.GetProperty("roots").EnumerateArray().Any(root => root.GetInt32() == 1));
    }

    [Fact]
    public async Task DepthOneDoesNotAcquireAChildManifest()
    {
        string archive = Archive("RestoredProjectFixture", "1.0.0", File.ReadAllBytes(Nuspec));
        foreach ((string id, string version) in new[]
        {
            ("Microsoft.CodeAnalysis.BannedApiAnalyzers", "5.6.0"),
            ("MarkdownTable.Formatting", "0.3.4"), ("NuGet.Packaging", "7.0.3"),
        })
            Archive(id, version, Manifest(id, version));
        var options = Options(new(DependencyRootKind.Package, archive)) with
        {
            Tfm = "net11.0", Depth = 1,
            SourceOptions = new NuGetSourceOptions { Sources = [_directory] },
        };
        await using var composition = new DesktopPackageSourceComposition(TimeSpan.FromSeconds(30));
        var candidates = new CountingCandidates(new PackageDependencyTraversalCandidateAdapter(
            new DesktopPackageDependencyCandidateSource(composition, options.SourceOptions)));
        var manifests = new CountingManifests(new DesktopPackageDependencyTraversalManifestSource(composition));

        DependencyDocument bounded = await DependsCommand.BuildDocumentAsync(options, true,
            TestContext.Current.CancellationToken, candidates, manifests);
        Assert.True(bounded.IsSuccessful);
        Assert.Equal(3, candidates.Calls);
        Assert.Equal(0, manifests.Calls);
        Assert.Equal(3, bounded.Graph.Edges.Length);
        Assert.Equal(DependencyCompletion.DepthBounded, bounded.Traversal);
        Assert.All(bounded.PackageTraversal!.Projections.Skip(1),
            projection => Assert.NotNull(projection.Candidate));

        candidates.Calls = 0;
        DependencyDocument evidence = await DependsCommand.BuildDocumentAsync(options with { Depth = null },
            false, TestContext.Current.CancellationToken, candidates, manifests);
        Assert.True(evidence.IsSuccessful);
        Assert.Equal(0, candidates.Calls);
        Assert.Equal(0, manifests.Calls);
        Assert.Null(evidence.PackageTraversal);
        Assert.Equal(DependencyCompletion.NotRequested, evidence.Traversal);
        Assert.Equal(bounded.Evidence.Dependencies, evidence.Evidence.Dependencies);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task DirectNuspecIsSourceBoundedWithoutAnySourceOperations(int depth)
    {
        var candidates = new CountingCandidates(null);
        var manifests = new CountingManifests(null);
        DependencyDocument result = await DependsCommand.BuildDocumentAsync(
            Options(new(DependencyRootKind.Nuspec, Nuspec)) with { Depth = depth, Tfm = "net11.0" },
            true, TestContext.Current.CancellationToken, candidates, manifests);
        Assert.True(result.IsSuccessful);
        Assert.Equal(DependencyCompletion.SourceBounded, result.Traversal);
        Assert.Equal(3, result.Graph.Edges.Length);
        Assert.Equal(0, candidates.Calls);
        Assert.Equal(0, manifests.Calls);
        Assert.All(result.PackageTraversal!.Roots,
            root => Assert.Equal(PackageDependencyTraversalExpansionAuthority.DirectDeclarationsOnly, root.Occurrence.Authority));
    }

    [Theory]
    [InlineData("--nuspec")]
    [InlineData("--project")]
    public async Task GraphRowsMatchAcrossSinksAndWindows(string rootOption)
    {
        string[] common = [rootOption, rootOption == "--nuspec" ? Nuspec : Assets, "--rows", "1..2"];
        foreach (string format in new[] { "--table", "--tsv", "--jsonl", "--mermaid", "--json", "--count", "--tree" })
        {
            var result = await Run([.. common, format]);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            if (format is "--table" or "--tsv")
                Assert.Equal(3, Lines(result.Output).Length);
            else if (format == "--jsonl")
                Assert.Equal(2, Lines(result.Output).Length);
            else if (format == "--mermaid")
                Assert.Equal(2, Lines(result.Output).Count(line => line.Contains("-->")));
            else if (format == "--json")
            {
                using var json = JsonDocument.Parse(result.Output);
                Assert.Equal(2, json.RootElement.GetProperty("dependency_graph").GetProperty("edges").GetArrayLength());
            }
            else if (format == "--count")
                Assert.Equal("2", result.Output.Trim());
            else
                Assert.Equal(2, Lines(result.Output).Count(line => line.Contains('├') || line.Contains('└')));
        }
    }

    [Fact]
    public async Task RootsCountIncludesFailedAttempts()
    {
        var result = await Run("--nuspec", Nuspec, "--project", "", "-S", "Roots", "--count");
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("2", result.Output.Trim());
    }

    [Fact]
    public async Task GraphColumnsDoNotRemoveDocumentCompletion()
    {
        var result = await Run("--nuspec", Nuspec, "--columns", "Source;Target", "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.TryGetProperty("summary", out _));
        Assert.True(json.RootElement.TryGetProperty("dependency_graph", out _));
        Assert.DoesNotContain("minimum_depth", result.Output);
    }

    [Fact]
    public async Task TypeDepthUsesTheOwnerBoundAndEmptyRootRemainsInJson()
    {
        var bounded = await Run("System.Int128", "--depth", "1", "--json");
        Assert.Equal(0, bounded.ExitCode);
        using var json = JsonDocument.Parse(bounded.Output);
        Assert.Equal("DepthBounded", json.RootElement.GetProperty("summary").GetProperty("traversal").GetString());
        Assert.All(json.RootElement.GetProperty("dependency_graph").GetProperty("edges").EnumerateArray(),
            edge => Assert.Equal(1, edge.GetProperty("minimum_depth").GetInt32()));
        var leaf = await Run("System.IDisposable", "--depth", "1", "--json");
        Assert.Equal(0, leaf.ExitCode);
        using var empty = JsonDocument.Parse(leaf.Output);
        Assert.Equal("Complete", empty.RootElement.GetProperty("summary").GetProperty("traversal").GetString());
        Assert.Single(empty.RootElement.GetProperty("dependency_graph").GetProperty("nodes").EnumerateArray());
        Assert.Empty(empty.RootElement.GetProperty("dependency_graph").GetProperty("edges").EnumerateArray());
    }

    [Fact]
    public async Task LibraryDepthOneStopsBeforeResolvingReferences()
    {
        var result = await Run("--library", FixtureCatalog.RestoredProjectDependencyFacts.AssemblyPath(),
            "--depth", "1", "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.All(json.RootElement.GetProperty("dependency_graph").GetProperty("edges").EnumerateArray(), edge =>
        {
            Assert.Equal(1, edge.GetProperty("minimum_depth").GetInt32());
            Assert.Equal("declared", edge.GetProperty("resolution").GetString());
        });
        Assert.NotEmpty(json.RootElement.GetProperty("dependency_graph").GetProperty("boundaries").EnumerateArray());
    }

    [Fact]
    public async Task SharedLibraryRelationshipsMergeRootReachability()
    {
        foreach (string[] roots in new[]
        {
            new[] { "System.Linq", "System.Collections" },
            new[] { "System.Collections", "System.Linq" },
        })
        {
            var result = await Run(
                "--library", roots[0],
                "--library", roots[1],
                "--depth", "2",
                "--json");

            Assert.Equal(0, result.ExitCode);
            using var json = JsonDocument.Parse(result.Output);
            JsonElement shared = Assert.Single(
                json.RootElement.GetProperty("dependency_graph")
                    .GetProperty("edges")
                    .EnumerateArray(),
                edge =>
                    edge.GetProperty("source_identity")
                        .GetProperty("library")
                        .GetProperty("name").GetString()
                        == "System.Collections"
                    && edge.GetProperty("target_identity")
                        .GetProperty("library")
                        .GetProperty("name").GetString()
                        == "System.Private.CoreLib");
            Assert.Equal(
                "resolved",
                shared.GetProperty("resolution").GetString());
            Assert.Equal(2, shared.GetProperty("root_distances")
                .EnumerateObject().Count());
        }
    }

    [Fact]
    public async Task PackageGraphDisclosesFrameworkSelectionBoundaries()
    {
        var jsonResult = await Run(
            "--nuspec", Nuspec,
            "--tfm", "net8.0",
            "--json");

        Assert.Equal(0, jsonResult.ExitCode);
        using var json = JsonDocument.Parse(jsonResult.Output);
        JsonElement projection = Assert.Single(
            json.RootElement.GetProperty("dependency_graph")
                .GetProperty("package_projections")
                .EnumerateArray());
        Assert.Equal(
            "NoMatchingTargetFramework",
            projection.GetProperty("selection_status").GetString());
        Assert.Equal(
            "net8.0",
            projection.GetProperty("requested_framework").GetString());
        Assert.Contains(
            json.RootElement.GetProperty("dependency_graph")
                .GetProperty("boundaries")
                .EnumerateArray(),
            boundary => boundary.GetProperty("kind").GetString()
                == "NoMatchingTargetFramework");

        var markdown = await Run(
            "--nuspec", Nuspec,
            "--tfm", "net8.0");
        Assert.Equal(0, markdown.ExitCode);
        Assert.Contains(
            "no matching target framework",
            markdown.Output,
            StringComparison.OrdinalIgnoreCase);

        var mermaid = await Run(
            "--nuspec", Nuspec,
            "--tfm", "net8.0",
            "--mermaid");
        Assert.Equal(0, mermaid.ExitCode);
        Assert.Contains(
            "no matching target framework",
            mermaid.Output,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalSourceTraversalPreservesCandidateAndDocumentCorrespondence()
    {
        string archive = Archive("RestoredProjectFixture", "1.0.0", File.ReadAllBytes(Nuspec));
        foreach ((string id, string version) in new[]
        {
            ("Microsoft.CodeAnalysis.BannedApiAnalyzers", "5.6.0"),
            ("MarkdownTable.Formatting", "0.3.4"), ("NuGet.Packaging", "7.0.3"),
        })
            Archive(id, version, Manifest(id, version));
        var result = await Run("--project", Assets, "--package", archive,
            "--source", _directory, "--depth", "2", "--json");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        JsonElement graph = json.RootElement.GetProperty("dependency_graph");
        JsonElement root = Assert.Single(graph.GetProperty("package_roots").EnumerateArray());
        Assert.Equal(0, root.GetProperty("occurrence_index").GetInt32());
        Assert.Equal(1, root.GetProperty("document_occurrence_index").GetInt32());
        var projections = graph.GetProperty("package_projections").EnumerateArray().ToArray();
        Assert.Equal(4, projections.Length);
        Assert.All(projections.Skip(1), projection =>
        {
            Assert.True(projection.TryGetProperty("candidate_correspondence", out _));
            Assert.Equal("Expanded", projection.GetProperty("expansion").GetString());
        });
        Assert.All(graph.GetProperty("edges").EnumerateArray().Where(edge =>
            edge.GetProperty("root_occurrences").EnumerateArray().Any(root => root.GetInt32() == 1)),
            edge => Assert.True(edge.GetProperty("evidence_identity").TryGetProperty("traversal_edge_index", out _)));
        Assert.Single(graph.GetProperty("restored_traversals").EnumerateArray());
        var windowed = await Run("--project", Assets, "--package", archive,
            "--source", _directory, "--depth", "2", "--rows", "1", "--json");
        Assert.Equal(0, windowed.ExitCode);
        using var window = JsonDocument.Parse(windowed.Output);
        JsonElement selected = window.RootElement.GetProperty("dependency_graph");
        HashSet<int> visibleNodes = [.. selected.GetProperty("nodes").EnumerateArray().Select(node => node.GetProperty("id").GetInt32())];
        Assert.All(selected.GetProperty("package_projections").EnumerateArray(),
            projection => Assert.Contains(projection.GetProperty("document_node_id").GetInt32(), visibleNodes));
        Assert.All(selected.GetProperty("boundaries").EnumerateArray(),
            boundary => Assert.Contains(boundary.GetProperty("node_id").GetInt32(), visibleNodes));
    }

    [Fact]
    public async Task FailedResolutionRemainsAnEdgeAndATypedFailureBesideHealthyRoots()
    {
        string archive = Archive("RestoredProjectFixture", "1.0.0", File.ReadAllBytes(Nuspec));
        var result = await Run("--package", archive, "--nuspec", Nuspec, "--tfm", "net11.0",
            "--source", _directory, "--depth", "1", "-S", "@Dependencies", "--json");
        Assert.Equal(1, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("Partial", json.RootElement.GetProperty("summary").GetProperty("traversal").GetString());
        Assert.Equal(6, json.RootElement.GetProperty("dependency_graph").GetProperty("edges").GetArrayLength());
        Assert.Contains(json.RootElement.GetProperty("failures").EnumerateArray(),
            failure => failure.TryGetProperty("candidate_reason", out var reason) && reason.GetString() == "NoMatchingVersion");
        Assert.Contains(json.RootElement.GetProperty("summary").GetProperty("roots").EnumerateArray(),
            root => root.GetProperty("traversal").GetString() == "SourceBounded");
    }

    [Theory]
    [InlineData("--nuspec")]
    [InlineData("--project")]
    [InlineData("--library")]
    public async Task UnusedSourceOverridesFailBeforeOpeningRoots(string option)
    {
        var result = await Run(option, "missing", "--source", _directory);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("remote --package", result.Error);
    }

    [Fact]
    public async Task ExplicitVerbosityAndOneSelectedTableCompose()
    {
        var result = await Run("--nuspec", Nuspec, "-v:n", "-S", "Roots", "--tsv");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, Lines(result.Output).Length);
    }

    [Fact]
    public async Task UnprojectedGraphJsonlRetainsNumericRowFields()
    {
        var result = await Run("--nuspec", Nuspec, "--rows", "1", "--jsonl");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("minimum_depth").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("root_occurrences").ValueKind);
    }

    [Fact]
    public async Task NormalMarkdownOmitsAnEmptyFailuresSection()
    {
        var result = await Run("--nuspec", Nuspec, "-v:n");
        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("## Failures", result.Output);
        Assert.Contains("## Dependency Graph", result.Output);
        Assert.Contains("## Dependencies", result.Output);
    }

    [Fact]
    public async Task EmptyPositionalSubjectCannotSelectPackageSharing()
    {
        var result = await Run("", "--package", "Newtonsoft.Json@13.0.4", "--share");
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--share requires exactly one --package", result.Error);
    }

    private static DependsOptions Options(DependencyRootRequest root) => new()
    {
        Roots = [root],
        Packages = root.Kind == DependencyRootKind.Package ? [root.Value] : [],
        Nuspecs = root.Kind == DependencyRootKind.Nuspec ? [root.Value] : [],
        Projects = root.Kind == DependencyRootKind.Project ? [root.Value] : [],
    };

    private static Task<(int ExitCode, string Output, string Error)> Run(params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            RootCommand root = CommandLineBuilder.CreateRootCommand();
            string[] processed = CommandLineBuilder.PreprocessArgs(["depends", .. args, "--tips", "q"], root);
            return CommandLineBuilder.InvokeAsync(root.Parse(processed), processed);
        });

    private string Archive(string id, string version, byte[] manifest)
    {
        string path = Path.Combine(_directory, $"{id}.{version}.nupkg");
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        using Stream entry = archive.CreateEntry($"{id}.nuspec").Open();
        entry.Write(manifest);
        return path;
    }

    private static byte[] Manifest(string id, string version) =>
        Encoding.UTF8.GetBytes($"""
        <package><metadata><id>{id}</id><version>{version}</version><authors>Fixture</authors><description>Leaf</description></metadata></package>
        """);
    private static string[] Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private sealed class CountingCandidates(IPackageDependencyTraversalCandidateResolver? inner)
        : IPackageDependencyTraversalCandidateResolver
    {
        public int Calls { get; set; }
        public ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
            PackageDependencyEvidenceDeclaration declaration, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            Calls++;
            return (inner ?? throw new InvalidOperationException("Unexpected candidate resolution."))
                .ResolveAsync(declaration, cancellationToken, operationContext);
        }
    }

    private sealed class CountingManifests(IPackageDependencyTraversalManifestAcquirer? inner)
        : IPackageDependencyTraversalManifestAcquirer
    {
        public int Calls { get; private set; }
        public Task<PackageDependencyTraversalManifestResult> AcquireAsync(
            PackageAcquisitionCandidate candidate, CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
        {
            Calls++;
            return (inner ?? throw new InvalidOperationException("Unexpected child manifest acquisition."))
                .AcquireAsync(candidate, cancellationToken, operationContext);
        }
    }
}
