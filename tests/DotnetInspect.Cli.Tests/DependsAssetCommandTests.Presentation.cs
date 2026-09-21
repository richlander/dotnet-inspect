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
    public async Task LibraryDepthBeyondTheHierarchyReportsComplete()
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
            "Dependency Hierarchy",
            "--format=json",
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
                "Dependency Hierarchy,Failures",
                "--format=json",
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
                document.RootElement.GetProperty("dependency_hierarchy")
                    .GetProperty("occurrences")
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
                "Dependency Hierarchy",
                "--count",
            ]);

            Assert.Equal(1, countExit);
            Assert.Empty(countOutput);
            Assert.Contains(
                "--count cannot report an exact 'Dependency Hierarchy' count",
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
            "--format=json",
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
                    DependencyInspectionRootKind.Nuspec,
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
    public async Task HierarchyFormatsUseOneOccurrenceCurrency()
    {
        string[] root =
        [
            "depends",
            "--project",
            AssetsFixture,
            "--depth",
            "1",
            "-S",
            "Dependency Hierarchy",
            "--rows",
            "1..2",
        ];

        (_, string markdown, _) = await RunCapturedAsync(root);
        (_, string table, _) = await RunCapturedAsync([.. root, "--format=table"]);
        (_, string tsv, _) = await RunCapturedAsync([.. root, "--format=tsv"]);
        (_, string jsonl, _) = await RunCapturedAsync([.. root, "--format=jsonl"]);
        (_, string json, _) = await RunCapturedAsync([.. root, "--format=json"]);
        (_, string tree, _) = await RunCapturedAsync([.. root, "--tree"]);
        (_, string mermaid, _) = await RunCapturedAsync(
            [.. root, "--format=mermaid"]);
        (_, string count, _) = await RunCapturedAsync(
            [.. root, "--count"]);

        Assert.Contains("```text", markdown, StringComparison.Ordinal);
        Assert.Equal(3, NonEmptyLines(table));
        Assert.Equal(3, NonEmptyLines(tsv));
        Assert.Equal(2, NonEmptyLines(jsonl));
        using JsonDocument typed = JsonDocument.Parse(json);
        Assert.Equal(
            2,
            typed.RootElement.GetProperty("dependency_hierarchy")
                .GetProperty("occurrences")
                .GetArrayLength());
        Assert.Contains("└", tree, StringComparison.Ordinal);
        Assert.StartsWith("graph TD", mermaid, StringComparison.Ordinal);
        Assert.Equal("2", count.Trim());
    }

    [Fact]
    public async Task SemanticWindowComposesWithRenderedLineLimit()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Hierarchy",
                "--rows",
                "2..2",
                "--count",
                "-n",
                "100",
                "--lines",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1..1")]
    public async Task LegacyRowsRemainCommandOwnedDuringSemanticAdoption(
        string rows)
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                AssetsFixture,
                "--depth",
                "1",
                "-S",
                "Dependency Hierarchy",
                "--rows",
                rows,
                "--count",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task PlainTextColumns_RenderProjectedHierarchyRows()
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
            await RunCapturedAsync([.. arguments, "--format=plaintext"]);
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
        PersistentCache.Initialize("dotnet-inspect-test");
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
    public async Task UnsupportedAssetQueryFailsBeforeRootAcquisition()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"depends-query-{Guid.NewGuid():N}.nuspec");
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                missing,
                "--where",
                "Source=Example.*",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "not queryable by this Dependency route",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedDependencyQueryOperatorFailsWithoutException()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"depends-query-{Guid.NewGuid():N}.dll");
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "Example.Type",
                "--library",
                missing,
                "--where",
                "Source starts-with System",
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            "does not support 'starts-with' predicates",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Exception",
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
            ["depends", "--nuspec", path, "-v:n", "--format=json"]);
        (_, string tsv, _) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            path,
            "-S",
            "Dependencies",
            "--format=tsv",
        ]);

        foreach (string rendered in new[] { markdown, json, tsv })
            HostileOutputAssert.NoRenderingHazard(rendered, "stdout");
        Assert.Contains(@"net8.0\u202Ehostile", markdown, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\\u202Ehostile", json, StringComparison.Ordinal);
        Assert.Contains(@"net8.0\u202Ehostile", tsv, StringComparison.Ordinal);
    }

}
