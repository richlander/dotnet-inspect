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
    public async Task MarkdownRendersSelectedSectionsWithoutCompletionDocument()
    {
        string[] arguments =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "-S",
            "Dependency Hierarchy,Dependencies",
        ];

        (int markdownExit, string markdown, string markdownError) =
            await RunCapturedAsync(arguments);
        (int projectedExit, string projected, string projectedError) =
            await RunCapturedAsync([.. arguments, "--columns", "Root"]);
        (int jsonExit, string json, string jsonError) =
            await RunCapturedAsync([.. arguments, "--json", "--compact"]);

        Assert.Equal(0, markdownExit);
        Assert.Equal(0, projectedExit);
        Assert.Equal(0, jsonExit);
        Assert.Empty(markdownError);
        Assert.Empty(projectedError);
        Assert.Empty(jsonError);
        Assert.StartsWith(
            "## Dependency Hierarchy",
            markdown.TrimStart(),
            StringComparison.Ordinal);
        Assert.Contains(
            "## Dependencies",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "```text",
            markdown,
            StringComparison.Ordinal);
        Assert.True(
            markdown.IndexOf(
                "## Dependency Hierarchy",
                StringComparison.Ordinal)
            < markdown.IndexOf(
                "## Dependencies",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "| Root Set |",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "# Dependencies",
            markdown.Split(Environment.NewLine),
            StringComparer.Ordinal);
        Assert.StartsWith(
            "## Dependency Hierarchy",
            projected.TrimStart(),
            StringComparison.Ordinal);
        Assert.Contains(
            "## Dependencies",
            projected,
            StringComparison.Ordinal);
        Assert.True(
            projected.IndexOf(
                "## Dependency Hierarchy",
                StringComparison.Ordinal)
            < projected.IndexOf(
                "## Dependencies",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "| Root Set |",
            projected,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "# Dependencies",
            projected.Split(Environment.NewLine),
            StringComparer.Ordinal);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task MarkdownRendersEmbeddedMermaidWithNeighboringSection()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "-S",
            "Dependency Hierarchy,Dependencies",
            "--markdown",
            "--mermaid",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.StartsWith(
            "## Dependency Hierarchy",
            output.TrimStart(),
            StringComparison.Ordinal);
        Assert.Contains("```mermaid", output, StringComparison.Ordinal);
        Assert.Contains("## Dependencies", output, StringComparison.Ordinal);
        Assert.DoesNotContain("| Root Set |", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "# Dependencies",
            output.Split(Environment.NewLine),
            StringComparer.Ordinal);
    }

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
    public async Task PackageHierarchyJson_RetainsProjectionProvenance()
    {
        string rootSource = CreateTemporaryDirectory();
        string dependencySource = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            rootSource,
            "Contoso.Root",
            "1.0.0",
            Dependency("Contoso.Child", "[1.0.0]"));
        WriteLocalSourcePackage(
            dependencySource,
            "Contoso.Child",
            "1.0.0",
            "");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            rootSource,
            "--source",
            dependencySource,
            "-S",
            "Dependency Hierarchy,Roots",
            "--json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] projections =
        [
            .. document.RootElement.GetProperty("dependency_hierarchy")
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
        JsonElement acquiredSource =
            acquired.GetProperty("evidence")
                .GetProperty("source");
        JsonElement suppliedSource =
            supplied.GetProperty("evidence")
                .GetProperty("source");
        string? acquiredProducer =
            acquiredSource.GetProperty("producer_key").GetString();
        int[] authorityAssociations =
        [
            .. acquired.GetProperty("candidate")
                .GetProperty("authorities")
                .EnumerateArray()
                .Select(static authority =>
                    authority.GetProperty("association").GetInt32()),
        ];
        int acquiredAssociation =
            acquiredSource.GetProperty("association").GetInt32();
        int suppliedAssociation =
            suppliedSource.GetProperty("association").GetInt32();

        Assert.NotEqual(suppliedAssociation, acquiredAssociation);
        Assert.Contains(acquiredAssociation, authorityAssociations);
        Assert.False(string.IsNullOrEmpty(acquiredProducer));
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
        Assert.Equal(
            suppliedAssociation,
            root.GetProperty("package_source")
                .GetProperty("association")
                .GetInt32());
        Assert.Equal(
            "ExpectedCoordinate",
            root.GetProperty("identity_provenance").GetString());
    }

    [Fact]
    public void SourceTokens_CorrelateDocumentLocalOrdinalsByRuntimeSource()
    {
        using IPackageSourceClient first =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        using IPackageSourceClient second =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageDependencyEvidenceSourceIdentity firstEvidence =
            EvidenceSource(first.Source);
        PackageDependencyEvidenceSourceIdentity secondEvidence =
            EvidenceSource(second.Source);
        DependencyEvidenceSourceTokens tokens =
            DependencyEvidenceSourceTokens.Create();

        tokens.Reserve(firstEvidence);
        tokens.Reserve(secondEvidence);
        int firstRuntimeToken = tokens.Reserve(first.Source);
        int secondRuntimeToken = tokens.Reserve(second.Source);

        Assert.Equal(1, firstEvidence.Association);
        Assert.Equal(1, secondEvidence.Association);
        Assert.NotEqual(firstRuntimeToken, secondRuntimeToken);
        Assert.Equal(firstRuntimeToken, tokens.Reserve(firstEvidence));
        Assert.Equal(secondRuntimeToken, tokens.Reserve(secondEvidence));

        static PackageDependencyEvidenceSourceIdentity EvidenceSource(
            PackageSourceResultIdentity source)
        {
            PackageDependencyEvidenceRootFailure.PackageProfile failure =
                PackageDependencyEvidenceQuery.CreatePackageProfileFailure(
                    new PackageProfileFailure(
                        "Example.Package",
                        "1.0.0",
                        source,
                        PackageProfileFailureKind.SearchContract,
                        "Search failed"));
            PackageDependencyEvidenceOutcome outcome =
                PackageDependencyEvidenceQuery.Execute(
                    new PackageDependencyEvidenceRequest(
                        [],
                        [failure]));
            return Assert.IsType<
                PackageDependencyEvidenceRootFailure.PackageProfile>(
                    Assert.Single(outcome.FailedRoots)).Source;
        }
    }
#endif

    [Fact]
    public async Task
        PackageHierarchyJson_UsesDefaultTraversalTargetWithoutReselectingRoot()
    {
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
            """
            <group targetFramework="net6.0">
              <dependency id="Contoso.Legacy" version="[1.0.0]" />
            </group>
            <group targetFramework="net8.0" />
            """);

        using JsonDocument document = await HierarchyJsonAsync(
        [
            "--package",
            "Contoso.Root@1.0.0",
            "--source",
            source,
        ]);

        JsonElement[] projections =
        [
            .. document.RootElement.GetProperty("dependency_hierarchy")
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

        JsonElement acquiredEvidence = acquired.GetProperty("evidence");
        Assert.Equal(
            TraversalTargetFrameworkPolicy.ProductDefaultTargetFramework,
            acquiredEvidence.GetProperty("requested_framework").GetString());
        Assert.Equal(
            "net8.0",
            acquiredEvidence.GetProperty("selected_framework").GetString());

        JsonElement suppliedEvidence = supplied.GetProperty("evidence");
        Assert.False(
            suppliedEvidence.TryGetProperty("requested_framework", out _));
        Assert.Equal(
            "net8.0",
            suppliedEvidence.GetProperty("selected_framework").GetString());
    }

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

        using JsonDocument document = await HierarchyJsonAsync(
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

        JsonElement hierarchy =
            document.RootElement.GetProperty("dependency_hierarchy");
        JsonElement[] boundaries =
        [
            .. hierarchy.GetProperty("depth_boundaries").EnumerateArray(),
        ];
        Assert.Equal(
            [1, 2],
            boundaries.Select(boundary =>
                boundary.GetProperty("root_occurrence").GetInt32()));
        Assert.All(
            boundaries,
            boundary =>
            {
                Assert.Equal(
                    "Package",
                    boundary.GetProperty("producer").GetString());
                Assert.True(
                    boundary.GetProperty("package_projection")
                        .GetInt32() >= 0);
            });
        Assert.Equal(
            2,
            hierarchy.GetProperty("occurrences").GetArrayLength());
        Assert.Equal(
            2,
            document.RootElement.GetProperty("summary")
                .GetProperty("hierarchy_occurrences")
                .GetInt32());
    }

#if DEBUG
    [Fact]
    public async Task HierarchyOnly_RetainsFrameworkSelectionState()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--tfm",
            "net99.0",
            "-S",
            "Dependency Hierarchy,Roots",
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
            document.RootElement.GetProperty("dependency_hierarchy")
                .GetProperty("occurrences")
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
            "Dependency Hierarchy,Roots",
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
                "dependency_hierarchy",
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
            "Dependency Hierarchy,Failures",
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
                "Dependency Hierarchy,Failures",
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
                == "dependency-hierarchy");
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
                "Dependency Hierarchy,Failures",
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
        Assert.Contains("Dependency Hierarchy", output, StringComparison.Ordinal);
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
        int expectedCount =
            document.RootElement.GetProperty("dependencies").GetArrayLength();
        Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());

        (int countExit, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--library",
                malformed,
                "--project",
                AssetsFixture,
                "-S",
                "Dependencies",
                "--count",
            ]);

        Assert.Equal(1, countExit);
        Assert.Equal(
            expectedCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            countOutput.Trim());
        Assert.Contains(
            "typed failure",
            countError,
            StringComparison.Ordinal);
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
    public async Task HierarchyAndFailuresJsonl_UsesOneDiscriminatedSchema()
    {
        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            AssetsFixture,
            "--nuspec",
            "/missing/jsonl-sibling.nuspec",
            "-S",
            "Dependency Hierarchy,Failures",
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
                == "dependency-hierarchy");
        JsonElement failure = Assert.Single(
            rows,
            row => row.GetProperty("kind").GetString() == "failure");
        Assert.True(
            failure.GetProperty("failure")
                .TryGetProperty("evidence", out _));
    }

}
