using System.CommandLine;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Core;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class DependsAssetCommandTests
{
    private static string NuspecFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
            "manifest.nuspec");

    private static string AssetsFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
            "project.assets.json");

    private static string ProjectDirectoryFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.ProjectDirectory();

    [Fact]
    public void ModeValidation_PreservesTypeScopesAndRejectsCrossModeGestures()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();

        Assert.Empty(root.Parse(
        [
            "depends",
            "Example.Type",
            "--package",
            "Example.Package",
            "--library",
            "Example.dll",
            "--project",
            "Example.csproj",
        ]).Errors);

        Assert.Contains(
            root.Parse(["depends", "Example.Type", "--nuspec", "x.nuspec"])
                .Errors,
            error => error.Message.Contains(
                "only without a positional type",
                StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["depends", "--package", "Example", "--platform"])
                .Errors,
            error => error.Message.Contains(
                "only with a positional type",
                StringComparison.Ordinal));
        Assert.Contains(
            root.Parse(["depends", "--project", "Example", "--depth", "0"])
                .Errors,
            error => error.Message.Contains(
                "positive integer",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task TypeDepthBoundMatchesUnboundedShortestPathEdges()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string[] common =
        [
            "depends",
            "System.Int128",
            "--platform",
            "-S",
            "Dependency Graph",
            "--jsonl",
        ];
        (int unboundedExit, string unboundedOutput, string unboundedError) =
            await RunCapturedAsync(common);
        (int boundedExit, string boundedOutput, string boundedError) =
            await RunCapturedAsync([.. common, "--depth", "4"]);

        Assert.True(
            unboundedExit == 0,
            $"unbounded stderr: {unboundedError}");
        Assert.True(
            boundedExit == 0,
            $"bounded stderr: {boundedError}");
        Assert.Empty(unboundedError);
        Assert.Empty(boundedError);

        string[] expected = ParseGraphLines(unboundedOutput)
            .Where(static edge => edge.Depth <= 4)
            .Select(static edge => edge.Identity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actual = ParseGraphLines(boundedOutput)
            .Select(static edge => edge.Identity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TypeDepthBoundariesAreTypedEndpointContext()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string[] graph =
        [
            "depends",
            "System.Int128",
            "--platform",
            "-S",
            "Dependency Graph",
            "--depth",
            "1",
        ];
        (int jsonExit, string jsonOutput, string jsonError) =
            await RunCapturedAsync([.. graph, "--json", "--compact"]);
        (int treeExit, string treeOutput, string treeError) =
            await RunCapturedAsync([.. graph, "--tree"]);
        (int countExit, string countOutput, string countError) =
            await RunCapturedAsync([.. graph, "--count"]);

        Assert.Equal(0, jsonExit);
        Assert.Equal(0, treeExit);
        Assert.Equal(0, countExit);
        Assert.Empty(jsonError);
        Assert.Empty(treeError);
        Assert.Empty(countError);

        using JsonDocument document = JsonDocument.Parse(jsonOutput);
        JsonElement[] boundaries =
        [
            .. document.RootElement.GetProperty("depth_boundaries")
                .EnumerateArray(),
        ];
        Assert.NotEmpty(boundaries);
        Assert.All(
            boundaries,
            boundary =>
            {
                Assert.Equal(
                    "Type",
                    boundary.GetProperty("producer").GetString());
                Assert.Equal(
                    1,
                    boundary.GetProperty("maximum_depth").GetInt32());
                Assert.Equal(
                    [1],
                    boundary.GetProperty("root_occurrences")
                        .EnumerateArray()
                        .Select(static occurrence =>
                            occurrence.GetInt32()));
                JsonElement identity =
                    boundary.GetProperty("node_identity");
                Assert.Equal(
                    "type",
                    identity.GetProperty("kind").GetString());
                Assert.False(
                    string.IsNullOrEmpty(
                        identity.GetProperty("type").GetString()));
            });
        Assert.Contains(
            "(bounded at depth 1)",
            treeOutput,
            StringComparison.Ordinal);
        Assert.Equal(
            document.RootElement.GetProperty("edges")
                .GetArrayLength(),
            int.Parse(
                countOutput.Trim(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TypeMode_RejectsUnusedColumnProjection()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "System.Int128",
            "--platform",
            "-S",
            "Dependency Graph",
            "--jsonl",
            "--columns",
            "Target",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "not supported in positional type mode",
            error,
            StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public async Task ExplicitRoots_PreserveHeterogeneousOccurrenceOrder()
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            "/missing/first.nuspec",
            "--project",
            "/missing/project",
            "--nuspec",
            "/missing/last.nuspec",
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] roots =
        [
            .. document.RootElement.GetProperty("roots").EnumerateArray(),
        ];
        Assert.Equal(
            ["/missing/first.nuspec", "/missing/project", "/missing/last.nuspec"],
            roots.Select(root => root.GetProperty("input").GetString()));
        Assert.Equal(
            [1, 2, 3],
            roots.Select(root => root.GetProperty("root").GetInt32()));
    }

    [Fact]
    public async Task ProjectLocatorsShareIdentityAndRetainLocatorProvenance()
    {
        string csproj = Path.Combine(
            ProjectDirectoryFixture,
            "DotnetInspector.RestoredProjectFixtures.csproj");

        JsonElement directory = await RootAsync(ProjectDirectoryFixture);
        JsonElement project = await RootAsync(csproj);
        JsonElement assets = await RootAsync(AssetsFixture);

        string directoryDigest = RestoredDigest(directory);
        Assert.Equal(directoryDigest, RestoredDigest(project));
        Assert.Equal(directoryDigest, RestoredDigest(assets));
        Assert.Equal("ProjectLocator", directory.GetProperty("source").GetString());
        Assert.Equal("ProjectLocator", project.GetProperty("source").GetString());
        Assert.Equal("ProjectAssets", assets.GetProperty("source").GetString());
    }

    [Fact]
    public async Task DirectoryNamedProjectAssetsJsonRetainsLocatorProvenance()
    {
        string parent = CreateTemporaryDirectory();
        string directory = Directory.CreateDirectory(
            Path.Combine(parent, "project.assets.json")).FullName;
        string obj = Directory.CreateDirectory(
            Path.Combine(directory, "obj")).FullName;
        File.Copy(
            AssetsFixture,
            Path.Combine(obj, "project.assets.json"));

        try
        {
            JsonElement root = await RootAsync(directory);

            Assert.Equal(
                "ProjectLocator",
                root.GetProperty("source").GetString());
            Assert.Equal(
                RestoredDigest(await RootAsync(AssetsFixture)),
                RestoredDigest(root));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
#endif

    [Fact]
    public async Task RestoredTraversal_DepthIsRootRelative()
    {
        int depthOne = await GraphCountAsync(
            ["--project", AssetsFixture, "--depth", "1"]);
        int depthTwo = await GraphCountAsync(
            ["--project", AssetsFixture, "--depth", "2"]);
        int unbounded = await GraphCountAsync(
            ["--project", AssetsFixture]);

        Assert.True(depthOne > 0);
        Assert.True(depthTwo > depthOne);
        Assert.True(unbounded >= depthTwo);

        using JsonDocument bounded = await GraphJsonAsync(
            ["--project", AssetsFixture, "--depth", "1"]);
        JsonElement summary = bounded.RootElement.GetProperty("summary");
        Assert.Equal(
            "DepthBounded",
            summary.GetProperty("traversal_completion")
                .GetString());
        JsonElement boundedGraph =
            bounded.RootElement.GetProperty("dependency_graph");
        JsonElement[] boundaries =
        [
            .. boundedGraph.GetProperty("depth_boundaries")
                .EnumerateArray(),
        ];
        Assert.Equal(2, boundaries.Length);
        Assert.All(
            boundaries,
            boundary =>
            {
                Assert.Equal(
                    "Restored",
                    boundary.GetProperty("producer").GetString());
                Assert.Equal(
                    1,
                    boundary.GetProperty("maximum_depth").GetInt32());
                Assert.Equal(
                    [1],
                    boundary.GetProperty("root_occurrences")
                        .EnumerateArray()
                        .Select(static root => root.GetInt32()));
                Assert.False(
                    boundary.TryGetProperty(
                        "package_projection",
                        out _));
            });
        Assert.Equal(
            depthOne,
            boundedGraph.GetProperty("edges").GetArrayLength());
        Assert.Equal(depthOne, summary.GetProperty("graph_edges").GetInt32());

        using JsonDocument expanded = await GraphJsonAsync(
            ["--project", AssetsFixture, "--depth", "2"]);
        JsonElement[] expandedEdges =
        [
            .. expanded.RootElement.GetProperty("dependency_graph")
                .GetProperty("edges")
                .EnumerateArray(),
        ];
        Assert.All(
            boundaries,
            boundary => Assert.Contains(
                expandedEdges,
                edge => edge.GetProperty("source_identity").GetRawText()
                    == boundary.GetProperty("node_identity").GetRawText()));

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Graph",
            ]);
        (int treeExit, string tree, string treeError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Graph",
                "--tree",
            ]);
        Assert.Equal(0, markdownExit);
        Assert.Equal(0, treeExit);
        Assert.Empty(markdownError);
        Assert.Empty(treeError);
        Assert.Equal(
            2,
            markdown.Split(
                "(bounded at depth 1)",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            2,
            tree.Split(
                "(bounded at depth 1)",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task FailedRestoredTraversalReportsFailedAndTypedDetail()
    {
        var frameworks = new JsonObject
        {
            ["net11.0"] = new JsonObject
            {
                ["dependencies"] = new JsonObject(),
            },
        };
        var assets = new JsonObject
        {
            ["version"] = 4,
            ["targets"] = new JsonObject
            {
                ["net11.0"] = new JsonObject(),
                ["NET11.0"] = new JsonObject(),
            },
            ["projectFileDependencyGroups"] = new JsonObject
            {
                ["net11.0"] = new JsonArray(),
            },
            ["project"] = new JsonObject
            {
                ["frameworks"] = frameworks,
            },
        };
        string path = WriteTemporaryFile(
            "project.assets.json",
            Encoding.UTF8.GetBytes(assets.ToJsonString()));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "-S",
            "Dependency Graph,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Failed",
            document.RootElement.GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        JsonElement traversal = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray(),
            failure => failure.GetProperty("phase").GetString()
                == "Traversal").GetProperty("traversal");
        Assert.Equal(
            "AmbiguousTargetIdentity",
            traversal.GetProperty("restored")
                .GetProperty("graph_reason")
                .GetString());
    }

    [Fact]
    public async Task AvailableRestoredTraversalReportsTypedGraphFailureWithPartialRows()
    {
        string path = WriteTemporaryFile(
            "project.assets.json",
            DenseProjectMeshDocument(projects: 130));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "-S",
            "Dependency Graph,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Partial",
            document.RootElement.GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        JsonElement edges = document.RootElement
            .GetProperty("dependency_graph")
            .GetProperty("edges");
        Assert.True(edges.GetArrayLength() > 0);
        Assert.True(
            edges.GetArrayLength()
                <= RestoredProjectDependencyTraversalQuery
                    .MaxProjectRelationships);

        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray(),
            row => row.GetProperty("phase").GetString() == "Traversal");
        Assert.Equal(
            "ConfiguredLimitExceeded",
            failure.GetProperty("reason").GetString());
        JsonElement traversal = failure.GetProperty("traversal");
        Assert.Equal(
            [1],
            traversal.GetProperty("affected_roots")
                .EnumerateArray()
                .Select(static root => root.GetInt32()));
        JsonElement restored = traversal.GetProperty("restored");
        Assert.Equal("Graph", restored.GetProperty("kind").GetString());
        Assert.Equal(
            "ConfiguredLimitExceeded",
            restored.GetProperty("graph_reason").GetString());
        Assert.Equal(1, restored.GetProperty("occurrences").GetInt32());
    }

    [Fact]
    public async Task DirectNuspec_ProducesASourceBoundedBoundaryGraph()
    {
        using JsonDocument document = await GraphJsonAsync(
        [
            "--nuspec",
            NuspecFixture,
            "--depth",
            "3",
        ]);

        Assert.Equal(
            "SourceBounded",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        JsonElement edges = document.RootElement
            .GetProperty("dependency_graph")
            .GetProperty("edges");
        Assert.True(edges.GetArrayLength() > 0);
        Assert.All(
            edges.EnumerateArray(),
            edge => Assert.Equal(
                "package-boundary",
                edge.GetProperty("target_identity")
                    .GetProperty("kind")
                    .GetString()));
    }

    [Fact]
    public async Task DirectNuspec_InvalidTraversalTfmFailsBeforeProjection()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "--tfm",
            "not a tfm",
            "-S",
            "Dependency Graph",
            "--json",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "is not a valid NuGet framework",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedDirectNuspecRootsRetainDistinctBoundaryEdges()
    {
        int single = await GraphCountAsync(
            ["--nuspec", NuspecFixture]);
        int repeated = await GraphCountAsync(
        [
            "--nuspec",
            NuspecFixture,
            "--nuspec",
            NuspecFixture,
        ]);

        Assert.True(single > 0);
        Assert.Equal(single * 2, repeated);
    }

    [Fact]
    public async Task EvidenceOnly_DoesNotRequestTraversal()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            "Dependencies",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "NotRequested",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        Assert.False(
            document.RootElement.TryGetProperty(
                "dependency_graph",
                out _));
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
    }

    [Fact]
    public async Task BareSelect_DoesNotRequestExpandablePackageTraversal()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "/missing/second-audit.nupkg",
            "-S",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "NotRequested",
            document.RootElement
                .GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
        Assert.False(
            document.RootElement.TryGetProperty(
                "dependency_graph",
                out _));
    }

#if DEBUG
    [Fact]
    public async Task RootsOnly_DoesNotPublishUnrequestedDeclarationFailure()
    {
        string path = WriteTemporaryFile(
            "conflicting.nuspec",
            Manifest(
                "Contoso.Conflicting",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Dependency" version="[1.0.0]" />
                  <dependency id="Contoso.Dependency" version="[2.0.0]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(
            "NotRequested",
            summary.GetProperty("declaration_completion").GetString());
        Assert.Equal(
            "NotRequested",
            summary.GetProperty("restored_relationship_completion")
                .GetString());
        JsonElement root = document.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "NotRequested",
            root.GetProperty("declaration").GetString());
        Assert.Equal(
            "NotRequested",
            root.GetProperty("restored_relationships").GetString());
        Assert.Equal(
            "NotRequested",
            root.GetProperty("selection").GetString());
        Assert.Equal(0, root.GetProperty("declaration_groups").GetInt32());
        Assert.Equal(0, root.GetProperty("declarations").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("failures", out _));
    }

    [Fact]
    public async Task RootsEffectiveDiscovery_DoesNotRunDeclarationPhase()
    {
        string path = WriteTemporaryFile(
            "conflicting-effective.nuspec",
            Manifest(
                "Contoso.Conflicting",
                "1.0.0",
                """
                <group targetFramework="net8.0">
                  <dependency id="Contoso.Dependency" version="[1.0.0]" />
                  <dependency id="Contoso.Dependency" version="[2.0.0]" />
                </group>
                """));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "-D",
            "Roots",
            "--effective",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("| Root | column |", output, StringComparison.Ordinal);
    }
#endif

    [Fact]
    public async Task RestoredDependencies_ExposeResolvedVersionInEveryTableShape()
    {
        string[] arguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            "Dependencies",
        ];

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(arguments);
        (int tableExit, string table, string tableError) =
            await RunCapturedAsync([.. arguments, "--table"]);
        (int jsonExit, string json, string jsonError) =
            await RunCapturedAsync([.. arguments, "--json", "--compact"]);

        Assert.Equal(0, markdownExit);
        Assert.Equal(0, tableExit);
        Assert.Equal(0, jsonExit);
        Assert.Empty(markdownError);
        Assert.Empty(tableError);
        Assert.Empty(jsonError);
        Assert.Contains("| Resolved |", markdown, StringComparison.Ordinal);
        Assert.Contains("resolved", table, StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement resolved = document.RootElement
            .GetProperty("dependencies")
            .EnumerateArray()
            .First(dependency =>
                dependency.TryGetProperty("resolved", out JsonElement value)
                && !string.IsNullOrEmpty(value.GetString()));
        Assert.True(
            resolved.TryGetProperty(
                "resolved_package_identity",
                out _));
        Assert.True(
            resolved.TryGetProperty(
                "resolved_relationship_identity",
                out _));
    }

    [Fact]
    public async Task RepeatedRestoredRootsRetainDistinctDependencyRows()
    {
        int single = await DependencyCountAsync(
            ["--project", AssetsFixture]);
        int repeated = await DependencyCountAsync(
        [
            "--project",
            AssetsFixture,
            "--project",
            AssetsFixture,
        ]);

        Assert.True(single > 0);
        Assert.Equal(single * 2, repeated);
    }

#if DEBUG
    [Fact]
    public async Task RootsJson_RetainsOwnerIssuedRestoredProvenance()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            "Roots,Dependencies,Restored Edges",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement.GetProperty("roots")[0];

        Assert.False(
            string.IsNullOrEmpty(
                root.GetProperty("content_digest").GetString()));
        Assert.Equal(
            "Available",
            root.GetProperty("declaration").GetString());
        Assert.Equal(
            "Complete",
            root.GetProperty("declaration_completion").GetString());
        Assert.True(root.TryGetProperty("restored_selection", out _));
        Assert.True(
            root.TryGetProperty("target_framework_identity", out _));
        Assert.True(root.TryGetProperty("target_selection", out _));

        (exitCode, output, error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "-S",
            "Roots,Dependencies",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument manifest = JsonDocument.Parse(output);
        JsonElement manifestRoot =
            manifest.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "Selected",
            manifestRoot.GetProperty("selection").GetString());
        Assert.True(manifestRoot.TryGetProperty("selected_group", out _));
        Assert.True(
            manifestRoot.TryGetProperty(
                "selected_source_occurrence",
                out _));

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "-S",
            "Roots,Dependencies",
        ]);
        Assert.Equal(0, markdownExit);
        Assert.Empty(markdownError);
        Assert.Contains("package:1", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageGraphJson_RetainsProjectionProvenance()
    {
        string missing = CreateTemporaryDirectory();
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Root",
            "1.0.0",
            Dependency("Contoso.Child", "[1.0.0]"));
        WriteLocalSourcePackage(
            source,
            "Contoso.Child",
            "1.0.0",
            "");
        File.WriteAllText(
            Path.Combine(
                missing,
                "Contoso.Child.1.0.0.nupkg"),
            "not a package archive");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            missing,
            "--source",
            source,
            "-S",
            "Dependency Graph,Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] projections =
        [
            .. document.RootElement.GetProperty("dependency_graph")
                .GetProperty("package_projections")
                .EnumerateArray(),
        ];
        JsonElement acquired = Assert.Single(
            projections,
            projection =>
                projection.GetProperty("kind").GetString()
                    == "CandidateAcquired");
        JsonElement supplied = Assert.Single(
            projections,
            projection =>
                projection.GetProperty("kind").GetString()
                    == "RootSupplied");
        Assert.Equal(
            "Expanded",
            acquired.GetProperty("expansion").GetString());
        Assert.True(
            acquired.GetProperty("candidate")
                .GetProperty("correspondence")
                .GetInt32() > 0);
        Assert.NotEmpty(
            acquired.GetProperty("candidate")
                .GetProperty("authorities")
                .EnumerateArray());
        Assert.True(
            acquired.GetProperty("evidence")
                .GetProperty("source")
                .GetProperty("association")
                .GetInt32() > 0);
        Assert.Equal(
            "NoDependencyGroups",
            acquired.GetProperty("evidence")
                .GetProperty("selection")
                .GetString());
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("selected_group", out _));
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("selected_source_occurrence", out _));
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("selected_framework", out _));
        Assert.Equal(
            "Selected",
            supplied.GetProperty("evidence")
                .GetProperty("selection")
                .GetString());
        Assert.True(
            supplied.GetProperty("evidence")
                .TryGetProperty("selected_group", out _));
        Assert.True(
            supplied.GetProperty("evidence")
                .TryGetProperty("selected_source_occurrence", out _));
        Assert.True(
            supplied.GetProperty("evidence")
                .TryGetProperty("selected_framework", out _));
        Assert.False(
            acquired.GetProperty("evidence")
                .TryGetProperty("declaration", out _));
        JsonElement root = document.RootElement.GetProperty("roots")[0];
        Assert.True(
            root.GetProperty("package_source")
                .GetProperty("association")
                .GetInt32() > 0);
        Assert.Equal(
            "ExpectedCoordinate",
            root.GetProperty("identity_provenance").GetString());
    }
#endif

    [Fact]
    public async Task PackageDepthBoundary_RetainsAllAffectedRootOccurrences()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.RootA",
            "1.0.0",
            Dependency("Contoso.Shared", "[1.0.0]"));
        WriteLocalSourcePackage(
            source,
            "Contoso.RootB",
            "1.0.0",
            Dependency("Contoso.Shared", "[1.0.0]"));
        WriteLocalSourcePackage(
            source,
            "Contoso.Shared",
            "1.0.0",
            "");

        using JsonDocument document = await GraphJsonAsync(
        [
            "--package",
            "Contoso.RootA@1.0.0",
            "--package",
            "Contoso.RootB@1.0.0",
            "--source",
            source,
            "--depth",
            "1",
        ]);

        JsonElement graph =
            document.RootElement.GetProperty("dependency_graph");
        JsonElement boundary = Assert.Single(
            graph.GetProperty("depth_boundaries").EnumerateArray());
        Assert.Equal(
            "Package",
            boundary.GetProperty("producer").GetString());
        Assert.True(
            boundary.GetProperty("package_projection").GetInt32() >= 0);
        Assert.Equal(
            [1, 2],
            boundary.GetProperty("root_occurrences")
                .EnumerateArray()
                .Select(static root => root.GetInt32()));
        Assert.Equal(
            2,
            graph.GetProperty("edges").GetArrayLength());
        Assert.Equal(
            2,
            document.RootElement.GetProperty("summary")
                .GetProperty("graph_edges")
                .GetInt32());
    }

#if DEBUG
    [Fact]
    public async Task GraphOnly_RetainsFrameworkSelectionState()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--tfm",
            "net99.0",
            "-S",
            "Dependency Graph,Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "Dependency traversal completed as Partial",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(
            "Partial",
            summary.GetProperty("traversal_completion").GetString());
        Assert.Equal(
            0,
            document.RootElement.GetProperty("dependency_graph")
                .GetProperty("edges")
                .GetArrayLength());
        JsonElement root = document.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "NoMatchingTargetFramework",
            root.GetProperty("selection").GetString());
        Assert.Equal(
            "net99.0",
            root.GetProperty("requested_framework").GetString());
        Assert.NotEqual(
            "NotRequested",
            root.GetProperty("traversal").GetString());
        Assert.False(root.TryGetProperty("selected_framework", out _));

        (exitCode, output, error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--tfm",
            "net11.0",
            "-S",
            "Dependency Graph,Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument selectedDocument = JsonDocument.Parse(output);
        JsonElement selectedRoot =
            selectedDocument.RootElement.GetProperty("roots")[0];
        Assert.Equal(
            "Selected",
            selectedRoot.GetProperty("selection").GetString());
        Assert.True(selectedRoot.TryGetProperty("selected_group", out _));
        Assert.True(
            selectedRoot.TryGetProperty(
                "selected_source_occurrence",
                out _));
        Assert.Equal(
            "net11.0",
            selectedRoot.GetProperty("requested_framework").GetString());
        Assert.Equal(
            "net11.0",
            selectedRoot.GetProperty("selected_framework").GetString());
        Assert.Equal(
            "Requested",
            selectedRoot.GetProperty("target_selection").GetString());
    }
#endif

    [Fact]
    public async Task TraversalFailureJson_RetainsAllAffectedRootsAndTypedSourceDetail()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Root",
            "1.0.0",
            Dependency("Contoso.Missing", "[1.0.0]"));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "Contoso.Root@1.0.0",
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            source,
            "-S",
            "Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument noTraversal = JsonDocument.Parse(output);
        Assert.False(
            noTraversal.RootElement.TryGetProperty(
                "dependency_graph",
                out _));

        (exitCode, output, error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "Contoso.Root@1.0.0",
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            source,
            "-S",
            "Dependency Graph,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray(),
            row => row.GetProperty("phase").GetString() == "Traversal");
        JsonElement traversal = failure.GetProperty("traversal");
        Assert.Equal(
            [1, 2],
            traversal.GetProperty("affected_roots")
                .EnumerateArray()
                .Select(static root => root.GetInt32()));
        Assert.Equal(
            "Acquisition",
            traversal.GetProperty("manifest").GetProperty("kind").GetString());
        Assert.NotEmpty(
            traversal.GetProperty("manifest")
                .GetProperty("source_failures")
                .EnumerateArray());

        (int jsonlExit, string jsonl, string jsonlError) =
            await RunCapturedAsync(
            [
                "depends",
                "--package",
                "Contoso.Root@1.0.0",
                "--package",
                "Contoso.Root@1.0.0",
                "--source",
                source,
                "-S",
                "Dependency Graph,Failures",
                "--jsonl",
            ]);
        Assert.Equal(1, jsonlExit);
        Assert.Contains("typed failure", jsonlError, StringComparison.Ordinal);
        JsonElement[] jsonlRows =
        [
            .. jsonl.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement.Clone();
                }),
        ];
        Assert.Contains(
            jsonlRows,
            row => row.GetProperty("kind").GetString()
                == "dependency-graph");
        JsonElement jsonlFailure = Assert.Single(
            jsonlRows,
            row => row.GetProperty("kind").GetString() == "failure");
        Assert.Equal(
            [1, 2],
            jsonlFailure.GetProperty("failure")
                .GetProperty("traversal")
                .GetProperty("affected_roots")
                .EnumerateArray()
                .Select(static root => root.GetInt32()));
        Assert.NotEmpty(
            jsonlFailure.GetProperty("failure")
                .GetProperty("traversal")
                .GetProperty("manifest")
                .GetProperty("source_failures")
                .EnumerateArray());

        (int projectedExit, _, string projectedError) =
            await RunCapturedAsync(
            [
                "depends",
                "--package",
                "Contoso.Root@1.0.0",
                "--package",
                "Contoso.Root@1.0.0",
                "--source",
                source,
                "-S",
                "Dependency Graph,Failures",
                "--json",
                "--columns",
                "Reason",
            ]);
        Assert.Equal(1, projectedExit);
        Assert.Contains(
            "cannot represent typed traversal failure detail",
            projectedError,
            StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public async Task FailedExplicitRootStillHasAnExactRootCount()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            "/missing/root-count.nuspec",
            "-S",
            "Roots",
            "--count",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Equal("1", output.Trim());
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
    }
#endif

    [Fact]
    public async Task EffectiveDiscovery_UsesActualDirectNuspecApplicability()
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        Assert.Empty(
            root.Parse(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-D",
                "--effective",
            ]).Errors);

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "-D",
            "--effective",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Dependency Graph", output, StringComparison.Ordinal);
        Assert.Contains("Dependencies", output, StringComparison.Ordinal);
#if DEBUG
        Assert.Contains("Roots", output, StringComparison.Ordinal);
        Assert.Contains(
            "Dependency Groups",
            output,
            StringComparison.Ordinal);
#else
        Assert.DoesNotContain("Roots", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Dependency Groups",
            output,
            StringComparison.Ordinal);
#endif
        Assert.DoesNotContain("Restored Edges", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Restored Packages",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EffectiveDiscovery_RetainsRootFailureExitStatus()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            "/missing/effective-root.nuspec",
            "-D",
            "--effective",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Failures", output, StringComparison.Ordinal);
#if DEBUG
        Assert.Contains("Roots", output, StringComparison.Ordinal);
#else
        Assert.DoesNotContain("Roots", output, StringComparison.Ordinal);
#endif
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PackageSearchTruncationReason.None, false)]
    [InlineData(PackageSearchTruncationReason.RequestedLimit, false)]
    [InlineData(PackageSearchTruncationReason.SourcePageLimit, true)]
    [InlineData(PackageSearchTruncationReason.ClientPageLimit, true)]
    public void PackagePrefixTruncation_OnlyProducerLimitsFail(
        PackageSearchTruncationReason reason,
        bool expected) =>
        Assert.Equal(
            expected,
            DependsCommand.IsFailedPrefixTruncation(reason));

    [Theory]
    [InlineData(
        PackageDependencyEvidenceRootSetCompletion.Incomplete,
        0,
        0,
        true,
        PackageSearchTruncationReason.RequestedLimit,
        true)]
    [InlineData(
        PackageDependencyEvidenceRootSetCompletion.Incomplete,
        1,
        0,
        true,
        PackageSearchTruncationReason.RequestedLimit,
        false)]
    [InlineData(
        PackageDependencyEvidenceRootSetCompletion.Incomplete,
        0,
        1,
        true,
        PackageSearchTruncationReason.RequestedLimit,
        false)]
    [InlineData(
        PackageDependencyEvidenceRootSetCompletion.Incomplete,
        0,
        0,
        true,
        PackageSearchTruncationReason.SourcePageLimit,
        false)]
    [InlineData(
        PackageDependencyEvidenceRootSetCompletion.Incomplete,
        0,
        0,
        true,
        PackageSearchTruncationReason.ClientPageLimit,
        false)]
    public void PackagePrefixRootSetCompletion_OnlyRequestedLimitIsSuccessful(
        PackageDependencyEvidenceRootSetCompletion completion,
        int rejectedRootCount,
        int failedRootCount,
        bool isTruncated,
        PackageSearchTruncationReason truncationReason,
        bool expected) =>
        Assert.Equal(
            expected,
            DependsCommand.IsCompleteCommandRootSet(
                completion,
                rejectedRootCount,
                failedRootCount,
                isTruncated,
                truncationReason));

    [Fact]
    public async Task FailedSibling_RetainsUsableRootAndReturnsNonzero()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--nuspec",
            "/missing/sibling.nuspec",
            "-S",
            "Dependencies,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("admitted_roots").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed_roots").GetInt32());
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
        Assert.Equal(
            1,
            document.RootElement.GetProperty("failures").GetArrayLength());
    }

    [Fact]
    public async Task MalformedLibraryPackage_RetainsValidSiblingRoot()
    {
        string malformed = WriteTemporaryFile(
            "malformed-library.nupkg",
            Encoding.UTF8.GetBytes("not a package archive"));
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            malformed,
            "--project",
            AssetsFixture,
            "-S",
            "Dependencies,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
        Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());
    }

    [Fact]
    public async Task UnsupportedPlatformLibraryTfm_RetainsValidSiblingRoot()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            NuspecFixture,
            "--library",
            "System.Runtime",
            "--tfm",
            "net48",
            "-S",
            "Dependencies,Failures",
            "--json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.True(
            document.RootElement.GetProperty("dependencies")
                .GetArrayLength() > 0);
        Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());
    }

    [Fact]
    public async Task GraphAndFailuresJsonl_UsesOneDiscriminatedSchema()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--nuspec",
            "/missing/jsonl-sibling.nuspec",
            "-S",
            "Dependency Graph,Failures",
            "--jsonl",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains("typed failure", error, StringComparison.Ordinal);
        JsonElement[] rows =
        [
            .. output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument row = JsonDocument.Parse(line);
                    return row.RootElement.Clone();
                }),
        ];
        Assert.Contains(
            rows,
            row => row.GetProperty("kind").GetString()
                == "dependency-graph");
        JsonElement failure = Assert.Single(
            rows,
            row => row.GetProperty("kind").GetString() == "failure");
        Assert.True(
            failure.GetProperty("failure")
                .TryGetProperty("evidence", out _));
    }

    [Fact]
    public async Task DiscoveryReflectsCompiledSectionCatalog()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "-D"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        foreach (string section in new[]
        {
            DependsAssetSections.DependencyGraph,
            DependsAssetSections.Dependencies,
            DependsAssetSections.Failures,
        })
            Assert.Contains(section, output, StringComparison.Ordinal);
        foreach (string section in new[]
        {
            DependsAssetSections.Roots,
            DependsAssetSections.RestoredEdges,
            DependsAssetSections.DependencyGroups,
            DependsAssetSections.RestoredPackages,
        })
#if DEBUG
            Assert.Contains(section, output, StringComparison.Ordinal);
#else
            Assert.DoesNotContain(section, output, StringComparison.Ordinal);
#endif
        Assert.Contains("@Dependencies", output, StringComparison.Ordinal);
    }

#if !DEBUG
    [Fact]
    public async Task RetailCatalogSchemaAndCategoryOmitDiagnosticSections()
    {
        Assert.Equal(
            [
                DependsAssetSections.DependencyGraph,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Failures,
            ],
            DependsAssetSections.SectionOrder);
        var expectedSections = new HashSet<string>(
            [
                DependsAssetSections.DependencyGraph,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Failures,
            ],
            StringComparer.OrdinalIgnoreCase);
        Assert.True(
            expectedSections.SetEquals(
                DependsAssetSections.Catalog.SelectableSectionNames));
        Assert.Equal(
            DependsAssetSections.SectionOrder,
            DependsAssetSections.Catalog.SelectionCategoryMap[
                SectionCategoryNames.Dependencies]);

        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "-D", "--schema"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        foreach (string section in new[]
        {
            DependsAssetSections.Roots,
            DependsAssetSections.RestoredEdges,
            DependsAssetSections.DependencyGroups,
            DependsAssetSections.RestoredPackages,
        })
            Assert.DoesNotContain(section, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Roots")]
    [InlineData("Restored Edges")]
    [InlineData("Dependency Groups")]
    [InlineData("Restored Packages")]
    [InlineData("Restored*")]
    public async Task RetailSelectionCannotReachDiagnosticSections(
        string selector)
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-S",
            selector,
        ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.True(
            error.Contains("not found", StringComparison.Ordinal)
            || error.Contains("No sections match", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-v:m")]
    [InlineData("-v:n")]
    [InlineData("-v:d")]
    public async Task RetailVerbosityNeverEmitsDiagnosticSections(
        string verbosity)
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            verbosity,
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        foreach (string property in new[]
        {
            "roots",
            "restored_edges",
            "dependency_groups",
            "restored_packages",
        })
            Assert.False(document.RootElement.TryGetProperty(property, out _));

        (exitCode, output, error) =
            await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            verbosity,
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        foreach (string heading in new[]
        {
            "## Roots",
            "## Restored Edges",
            "## Dependency Groups",
            "## Restored Packages",
        })
            Assert.DoesNotContain(heading, output, StringComparison.Ordinal);
    }
#else
    [Fact]
    public async Task DiagnosticSectionsRemainExactlySelectableInDebugBuild()
    {
        var scenarios =
            new (string Section, string Property, string[] Arguments)[]
            {
                (
                    DependsAssetSections.Roots,
                    "roots",
                    ["--nuspec", NuspecFixture]),
                (
                    DependsAssetSections.RestoredEdges,
                    "restored_edges",
                    ["--project", AssetsFixture]),
                (
                    DependsAssetSections.DependencyGroups,
                    "dependency_groups",
                    ["--nuspec", NuspecFixture]),
                (
                    DependsAssetSections.RestoredPackages,
                    "restored_packages",
                    ["--project", AssetsFixture]),
            };

        foreach ((string section, string property, string[] arguments)
            in scenarios)
        {
            (int exitCode, string output, string error) =
                await RunCapturedAsync(
            [
                "depends",
                .. arguments,
                "-S",
                section,
                "--json",
                "--compact",
            ]);

            Assert.Equal(0, exitCode);
            Assert.Empty(error);
            using JsonDocument document = JsonDocument.Parse(output);
            Assert.True(
                document.RootElement.GetProperty(property)
                    .GetArrayLength() > 0);
        }
    }
#endif

    [Fact]
    public async Task TypeModeDiscoveryExposesOnlyTheGraphSection()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "Int128", "-D"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("Dependency Graph", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"{Environment.NewLine}Roots",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Restored Edges", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryDepthBeyondTheGraphReportsComplete()
    {
        string library = typeof(DependsAssetCommandTests).Assembly.Location;
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            library,
            "--depth",
            "100",
            "-S",
            "Dependency Graph",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Complete",
            document.RootElement.GetProperty("summary")
                .GetProperty("traversal_completion")
                .GetString());
    }

    [Fact]
    public async Task MissingLibraryBinding_IsTypedPartialTraversalFailure()
    {
        string directory = CreateTemporaryDirectory();
        string library = Path.Combine(
            directory,
            "ILInspector.Metadata.TypeDependencyConsumer.dll");
        File.Copy(
            FixtureCatalog.MetadataTypeDependencyConsumer.AssemblyPath(),
            library);

        try
        {
            (int exitCode, string output, string error) =
                await RunCapturedAsync(
            [
                "depends",
                "--library",
                library,
                "-S",
                "Dependency Graph,Failures",
                "--json",
                "--compact",
            ]);

            Assert.Equal(1, exitCode);
            Assert.Contains("typed failure", error, StringComparison.Ordinal);
            Assert.Contains(
                "Dependency traversal completed as Partial",
                error,
                StringComparison.Ordinal);

            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement summary =
                document.RootElement.GetProperty("summary");
            Assert.Equal(
                "Partial",
                summary.GetProperty("traversal_completion").GetString());

            const string missingAssembly =
                "ILInspector.Metadata.TypeDependencyReference";
            JsonElement edge = Assert.Single(
                document.RootElement.GetProperty("dependency_graph")
                    .GetProperty("edges")
                    .EnumerateArray(),
                candidate => candidate.GetProperty("target_identity")
                    .GetProperty("library")
                    .GetProperty("name")
                    .GetString() == missingAssembly);
            Assert.Equal(
                "declared",
                edge.GetProperty("resolution").GetString());
            Assert.Equal(
                missingAssembly,
                edge.GetProperty("evidence_identity")
                    .GetProperty("assembly_reference")
                    .GetProperty("name")
                    .GetString());

            JsonElement failure = Assert.Single(
                document.RootElement.GetProperty("failures")
                    .EnumerateArray(),
                row => row.GetProperty("reason").GetString() == "Missing");
            JsonElement traversal = failure.GetProperty("traversal");
            JsonElement binding =
                traversal.GetProperty("assembly_binding");
            Assert.Equal(
                "Missing",
                binding.GetProperty("kind").GetString());
            Assert.Equal(
                "NoNameOwner",
                binding.GetProperty("disposition").GetString());
            JsonElement requestedAssembly =
                binding.GetProperty("requested_assembly");
            Assert.Equal(
                missingAssembly,
                requestedAssembly
                    .GetProperty("name")
                    .GetString());
            Assert.Equal(
                edge.GetProperty("evidence_identity")
                    .GetProperty("assembly_reference")
                    .GetRawText(),
                requestedAssembly.GetRawText());
            Assert.Equal(
                [1],
                traversal.GetProperty("affected_roots")
                    .EnumerateArray()
                    .Select(static root => root.GetInt32()));

            (int countExit, string countOutput, string countError) =
                await RunCapturedAsync(
            [
                "depends",
                "--library",
                library,
                "-S",
                "Dependency Graph",
                "--count",
            ]);

            Assert.Equal(1, countExit);
            Assert.Empty(countOutput);
            Assert.Contains(
                "--count cannot report an exact 'Dependency Graph' count",
                countError,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InstalledPlatformLibraryRejectsUnusedSourceOverride()
    {
        (int exitCode, _, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            "System.Text.Json",
            "--source",
            "https://example.invalid/v3/index.json",
            "-S",
            "Dependencies",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "require a remote --package root",
            error,
            StringComparison.Ordinal);
    }

#if DEBUG
    [Fact]
    public async Task LibraryRoot_RetainsAssemblySourceKind()
    {
        string library = typeof(DependsAssetCommandTests).Assembly.Location;
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--library",
            library,
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Assembly",
            document.RootElement.GetProperty("roots")[0]
                .GetProperty("source")
                .GetString());
    }
#endif

    [Fact]
    public async Task PositionalLibraryFallbackHonorsDepth()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        string library = typeof(DependsAssetCommandTests).Assembly.Location;
        (int fallbackExit, string fallbackCount, string fallbackError) =
            await RunCapturedAsync(
        [
            "depends",
            library,
            "--depth",
            "1",
            "--count",
        ]);
        (int explicitExit, string explicitCount, string explicitError) =
            await RunCapturedAsync(
        [
            "depends",
            "--library",
            library,
            "--depth",
            "1",
            "-S",
            "Dependency Graph",
            "--count",
        ]);

        Assert.True(
            fallbackExit == 0,
            $"fallback stderr: {fallbackError}");
        Assert.True(
            explicitExit == 0,
            $"explicit stderr: {explicitError}");
        Assert.Equal(explicitCount.Trim(), fallbackCount.Trim());
    }

    [Fact]
    public async Task PreCanceledAssetRequestDoesNotPublishAnOutcome()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = new DependsOptions
        {
            AssetRoots =
            [
                new DependsAssetRoot(
                    1,
                    DependsAssetRootKind.Nuspec,
                    "/missing/cancelled.nuspec"),
            ],
            Select = ["Dependencies"],
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DependsCommand.ExecuteAssetDependsAsync(
                options,
                cancellation.Token));
    }

    [Fact]
    public async Task GraphFormatsUseOneLogicalEdgeCurrency()
    {
        string[] root =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "-S",
            "Dependency Graph",
            "--rows",
            "1..2",
        ];

        (_, string markdown, _) = await RunCapturedAsync(root);
        (_, string table, _) = await RunCapturedAsync([.. root, "--table"]);
        (_, string tsv, _) = await RunCapturedAsync([.. root, "--tsv"]);
        (_, string jsonl, _) = await RunCapturedAsync([.. root, "--jsonl"]);
        (_, string json, _) = await RunCapturedAsync([.. root, "--json"]);
        (_, string tree, _) = await RunCapturedAsync([.. root, "--tree"]);
        (_, string mermaid, _) = await RunCapturedAsync(
            [.. root, "--mermaid"]);
        (_, string count, _) = await RunCapturedAsync(
            [.. root, "--count"]);

        Assert.Contains("```text", markdown, StringComparison.Ordinal);
        Assert.Equal(3, NonEmptyLines(table));
        Assert.Equal(3, NonEmptyLines(tsv));
        Assert.Equal(2, NonEmptyLines(jsonl));
        using JsonDocument typed = JsonDocument.Parse(json);
        Assert.Equal(
            2,
            typed.RootElement.GetProperty("dependency_graph")
                .GetProperty("edges")
                .GetArrayLength());
        Assert.Contains("└", tree, StringComparison.Ordinal);
        Assert.StartsWith("graph TD", mermaid, StringComparison.Ordinal);
        Assert.Equal("2", count.Trim());
    }

    [Fact]
    public async Task PlainTextColumns_RenderProjectedGraphRows()
    {
        string[] arguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "--columns",
            "Target",
        ];

        (int plainExit, string plain, string plainError) =
            await RunCapturedAsync([.. arguments, "--plaintext"]);
        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(arguments);

        Assert.Equal(0, plainExit);
        Assert.Equal(0, markdownExit);
        Assert.Empty(plainError);
        Assert.Empty(markdownError);
        Assert.DoesNotContain("└", plain, StringComparison.Ordinal);
        Assert.Contains("Target", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Source Kind", plain, StringComparison.Ordinal);
        Assert.Contains("Target", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Source Kind",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuietPositionalTypeMode_SucceedsWithoutOutput()
    {
        CoreCache.Initialize("dotnet-inspect-test");
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "System.Int128",
            "--platform",
            "-v:q",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task QuietPositionalTypeDepthFailsBeforeSourceAcquisition()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"depends-quiet-depth-{Guid.NewGuid():N}.dll");
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "Example.Type",
                "--library",
                missing,
                "-v:q",
                "--depth",
                "1",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "--depth requires the Dependency Graph section",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HostileNuspecText_RemainsContained()
    {
        string path = WriteTemporaryFile(
            "hostile.nuspec",
            Encoding.UTF8.GetBytes(
                """
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>Hostile.Package</id>
                    <version>1.0.0</version>
                    <authors>Author</authors>
                    <description>Hostile fixture.</description>
                    <dependencies>
                      <group targetFramework="net8.0&#x202E;hostile">
                        <dependency id="Contoso.Dependency" version="[1.0.0]" />
                      </group>
                    </dependencies>
                  </metadata>
                </package>
                """));

        (_, string markdown, _) = await RunCapturedAsync(
            ["depends", "--nuspec", path, "-v:n"]);
        (_, string json, _) = await RunCapturedAsync(
            ["depends", "--nuspec", path, "-v:n", "--json"]);
        (_, string tsv, _) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "-S",
            "Dependencies",
            "--tsv",
        ]);

        foreach (string rendered in new[] { markdown, json, tsv })
            HostileOutputAssert.NoRenderingHazard(rendered, "stdout");
        Assert.Contains(@"net8.0\u202Ehostile", markdown, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\\u202Ehostile", json, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\u202Ehostile", tsv, StringComparison.Ordinal);
    }

#if DEBUG
    private static async Task<JsonElement> RootAsync(string path)
    {
        (int exitCode, string output, _) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            path,
            "-S",
            "Roots",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("roots")[0].Clone();
    }

    private static string RestoredDigest(JsonElement root) =>
        root.GetProperty("identity")
            .GetProperty("restored_root")
            .GetProperty("facts_digest")
            .GetString()!;
#endif

    private static async Task<int> GraphCountAsync(string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependency Graph",
            "--count",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return int.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> DependencyCountAsync(string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependencies",
            "--count",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return int.Parse(
            output.Trim(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<JsonDocument> GraphJsonAsync(
        string[] arguments)
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            .. arguments,
            "-S",
            "Dependency Graph",
            "--json",
            "--compact",
        ]);
        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return JsonDocument.Parse(output);
    }

    private static int NonEmptyLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    private static IEnumerable<(string Identity, int Depth)> ParseGraphLines(
        string output)
    {
        foreach (string line in output.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            yield return (
                $"{root.GetProperty("source_identity").GetString()}\0"
                    + $"{root.GetProperty("relationship").GetString()}\0"
                    + root.GetProperty("target_identity").GetString(),
                root.GetProperty("minimum_depth").GetInt32());
        }
    }

    private static Task<(int ExitCode, string Output, string Error)>
        RunCapturedAsync(string[] args) =>
        ConsoleCapture.RunAsync(() => RunAsync(args));

    private static Task<int> RunAsync(string[] args)
    {
        RootCommand root = CommandLineBuilder.CreateRootCommand();
        string[] processed = CommandLineBuilder.PreprocessArgs(args, root);
        return CommandLineBuilder.InvokeAsync(
            root.Parse(processed),
            processed);
    }

    private static string WriteTemporaryFile(string name, byte[] content)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-depends-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-depends-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteLocalSourcePackage(
        string folder,
        string packageId,
        string version,
        string dependenciesXml)
    {
        string path = Path.Combine(
            folder,
            $"{packageId}.{version}.nupkg");
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry($"{packageId}.nuspec");
        using Stream entryStream = entry.Open();
        entryStream.Write(
            Manifest(packageId, version, dependenciesXml));
    }

    private static string Dependency(string packageId, string constraint) =>
        $"""
         <group targetFramework="net8.0">
           <dependency id="{packageId}" version="{constraint}" />
         </group>
         """;

    private static byte[] DenseProjectMeshDocument(int projects)
    {
        var targets = new JsonObject();
        for (int index = 0; index < projects; index++)
        {
            var dependencies = new JsonObject();
            for (int other = 0; other < projects; other++)
            {
                if (other != index)
                    dependencies.Add($"Mesh.Project{other}", "1.0.0");
            }

            targets.Add(
                $"Mesh.Project{index}/1.0.0",
                new JsonObject
                {
                    ["type"] = "project",
                    ["dependencies"] = dependencies,
                });
        }

        var document = new JsonObject
        {
            ["version"] = 4,
            ["targets"] = new JsonObject
            {
                ["net11.0"] = targets,
            },
            ["projectFileDependencyGroups"] = new JsonObject
            {
                ["net11.0"] =
                    new JsonArray("Mesh.Project0 >= 1.0.0"),
            },
            ["project"] = new JsonObject
            {
                ["frameworks"] = new JsonObject
                {
                    ["net11.0"] = new JsonObject
                    {
                        ["dependencies"] = new JsonObject(),
                    },
                },
            },
        };
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }

    private static byte[] Manifest(
        string packageId,
        string version,
        string dependencies) =>
        Encoding.UTF8.GetBytes(
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                <metadata>
                  <id>{{packageId}}</id>
                  <version>{{version}}</version>
                  <authors>Depends Tests</authors>
                  <description>Depends test package.</description>
                  <dependencies>
                    {{dependencies}}
                  </dependencies>
                </metadata>
              </package>
              """);
}
