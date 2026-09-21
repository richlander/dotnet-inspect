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
    public async Task DiscoveryReflectsCompiledSectionCatalog()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "-D"]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        foreach (string section in new[]
        {
            DependsAssetSections.DependencyHierarchy,
            DependsAssetSections.Dependencies,
            DependsAssetSections.Pruning,
            DependsAssetSections.Failures,
        })
            Assert.Contains(section, output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            DependsTypeSections.DependencyGraph,
            output,
            StringComparison.Ordinal);
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

    [Fact]
    public async Task AssetModeRejectsObsoleteDependencyGraphSection()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-S",
                DependsTypeSections.DependencyGraph,
            ]);

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Contains(
            DependsTypeSections.DependencyGraph,
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            DependsAssetSections.DependencyHierarchy,
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareEffectiveDiscoveryDoesNotRunUnboundedPruning()
    {
        (int exitCode, string output, string error) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                NuspecFixture,
                "-D",
                "--effective",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain(
            DependsAssetSections.Pruning,
            output,
            StringComparison.Ordinal);
    }

#if !DEBUG
    [Fact]
    public async Task RetailCatalogSchemaAndCategoryOmitDiagnosticSections()
    {
        Assert.Equal(
            [
                DependsAssetSections.DependencyHierarchy,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            DependsAssetSections.SectionOrder);
        var expectedSections = new HashSet<string>(
            [
                DependsAssetSections.DependencyHierarchy,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Pruning,
                DependsAssetSections.Failures,
            ],
            StringComparer.OrdinalIgnoreCase);
        Assert.True(
            expectedSections.SetEquals(
                DependsAssetSections.Catalog.SelectableSectionNames));
        Assert.Equal(
            [
                DependsAssetSections.DependencyHierarchy,
                DependsAssetSections.Dependencies,
                DependsAssetSections.Failures,
            ],
            DependsAssetSections.Catalog.SelectionCategoryMap[
                SectionCategoryNames.Dependencies]);

        (int exitCode, string output, string error) =
            await RunCapturedAsync(["depends", "-D", "--schema"]);
        (int hierarchyExit, string hierarchy, string hierarchyError) =
            await RunCapturedAsync(
            [
                "depends",
                "-D",
                DependsAssetSections.DependencyHierarchy,
                "--schema",
            ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal(0, hierarchyExit);
        Assert.Empty(hierarchyError);
        Assert.Contains("Edge ID", hierarchy, StringComparison.Ordinal);
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
            "--format=json",
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
                "--format=json",
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

}
