using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
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
        PersistentCache.Initialize("dotnet-inspect-test");
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
        PersistentCache.Initialize("dotnet-inspect-test");
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
            .. document.RootElement.GetProperty("queryResult").GetProperty("dependency")
                .GetProperty("depthBoundaries")
                .EnumerateArray(),
        ];
        Assert.NotEmpty(boundaries);
        Assert.All(
            boundaries,
            boundary =>
            {
                Assert.Equal(
                    1,
                    boundary.GetProperty("maximumDepth").GetInt32());
                Assert.False(
                    string.IsNullOrEmpty(
                        boundary.GetProperty("typeName").GetString()));
            });
        Assert.Contains(
            "(bounded at depth 1)",
            treeOutput,
            StringComparison.Ordinal);
        Assert.Equal(
            document.RootElement.GetProperty("rowSelection").GetProperty("relationships")
                .GetArrayLength(),
            int.Parse(
                countOutput.Trim(),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TypeMode_RejectsUnusedColumnProjection()
    {
        PersistentCache.Initialize("dotnet-inspect-test");
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
        int depthOne = await HierarchyCountAsync(
            ["--project", AssetsFixture, "--depth", "1"]);
        int depthTwo = await HierarchyCountAsync(
            ["--project", AssetsFixture, "--depth", "2"]);
        int unbounded = await HierarchyCountAsync(
            ["--project", AssetsFixture]);

        Assert.True(depthOne > 0);
        Assert.True(depthTwo > depthOne);
        Assert.True(unbounded >= depthTwo);

        using JsonDocument bounded = await HierarchyJsonAsync(
            ["--project", AssetsFixture, "--depth", "1"]);
        JsonElement summary = bounded.RootElement.GetProperty("summary");
        Assert.Equal(
            "DepthBounded",
            summary.GetProperty("traversal_completion")
                .GetString());
        JsonElement boundedHierarchy =
            bounded.RootElement.GetProperty("dependency_hierarchy");
        JsonElement[] boundaries =
        [
            .. boundedHierarchy.GetProperty("depth_boundaries")
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
                    1,
                    boundary.GetProperty("root_occurrence").GetInt32());
                Assert.False(
                    boundary.TryGetProperty(
                        "package_projection",
                        out _));
            });
        Assert.Equal(
            depthOne,
            boundedHierarchy.GetProperty("occurrences").GetArrayLength());
        Assert.Equal(
            depthOne,
            summary.GetProperty("hierarchy_occurrences").GetInt32());

        using JsonDocument expanded = await HierarchyJsonAsync(
            ["--project", AssetsFixture, "--depth", "2"]);
        JsonElement[] expandedEdges =
        [
            .. expanded.RootElement.GetProperty("dependency_hierarchy")
                .GetProperty("occurrences")
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
                "Dependency Hierarchy",
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
                "Dependency Hierarchy",
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
            "Dependency Hierarchy,Failures",
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
            "Dependency Hierarchy,Failures",
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
            .GetProperty("dependency_hierarchy")
            .GetProperty("occurrences");
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
    public async Task DirectNuspec_ProducesASourceBoundedBoundaryHierarchy()
    {
        using JsonDocument document = await HierarchyJsonAsync(
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
            .GetProperty("dependency_hierarchy")
            .GetProperty("occurrences");
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
            "Dependency Hierarchy",
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
    public async Task RepeatedDirectNuspecRootsRetainDistinctBoundaryOccurrences()
    {
        int single = await HierarchyCountAsync(
            ["--nuspec", NuspecFixture]);
        int repeated = await HierarchyCountAsync(
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
                "dependency_hierarchy",
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
                "dependency_hierarchy",
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

    [Fact]
    public async Task EffectiveDiscoveryRetainsItsProjectionSchema()
    {
        var result = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "-D",
            "--effective",
            "--json",
            "--columns",
            "Name",
        ]);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using JsonDocument document = JsonDocument.Parse(result.Output);
        Assert.NotEmpty(document.RootElement.EnumerateArray());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row => Assert.True(row.TryGetProperty("name", out _)));
    }
#endif

}
