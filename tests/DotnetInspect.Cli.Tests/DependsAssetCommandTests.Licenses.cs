using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
    [Fact]
    public void LicenseProjectionKeepsPairsSeparateFromDeclarationEvidence()
    {
        DependencyInspectionLicense license = DependencyInspectionLicense.Create(
            PackageLicenseInventoryItem.Available(
                PackageSourceCoordinate.Create("Example.Package", "1.2.3"),
                new PackageLicenseDeclaration(
                    PackageLicenseDeclarationKind.Expression,
                    "MIT")));
        DependencyInspectionSummary summary = new(
            DependencyInspectionRootSetCompletion.Complete,
            RequestedRoots: 1,
            AdmittedRoots: 1,
            FailedRoots: 0,
            DependencyInspectionTraversalCompletion.NotRequested,
            RequestedDepth: null,
            HierarchyOccurrences: 0,
            CanonicalNodes: 0,
            Relationships: 0,
            DependencyInspectionEvidencePhaseCompletion.Complete,
            DependencyInspectionEvidencePhaseCompletion.NotApplicable,
            DependencyInspectionPruningSummary.NotRequested,
            IsPrefixRootSet: false,
            PackagePrefix: null)
        {
            Licenses = new DependencyInspectionLicenseSummary(
                DependencyInspectionLicenseCompletion.Complete,
                Packages: 1,
                Available: 1,
                Unavailable: 0),
        };
        var content = new DependencyInspectionContent(
            summary,
            DependencyHierarchyDocument.Empty,
            [],
            [],
            [],
            [])
        {
            Licenses = [license],
        };
        var projection = new DependsAssetProjection(
            new InspectionEnvelope<DependencyInspectionContent>(
                content,
                new InspectionShare.NonProjectable(
                    "dependencies",
                    "Test inspection.")),
            summary,
            DependencyHierarchyDocument.Empty.BackingGraph,
            DependencyHierarchyDocument.Empty,
            HierarchyRows: [],
            Roots: [],
            Dependencies: [],
            Pruning: [],
            RestoredEdges: [],
            Failures: [],
            DependencyGroups: [],
            RestoredPackages: [],
            Enriched: null);

        DependsLicenseView view = DependsLicenseView.From(license);
        Assert.Equal("example.package", view.Package);
        Assert.Equal("1.2.3", view.Version);
        Assert.Equal("MIT", view.License);

        DependsAssetDocument document = DependsAssetDocument.Create(
            projection,
            new HashSet<string>([DependsAssetSections.Licenses]),
            rows: null);
        DependsLicenseJson json = Assert.Single(document.Licenses!);
        Assert.Equal("MIT", json.License.ToString());
        Assert.Equal(
            DependencyInspectionLicenseDeclarationKind.Expression,
            json.DeclarationKind);
        Assert.Equal("MIT", json.DeclarationValue?.ToString());
    }

    [Fact]
    public async Task DirectNuspecLicenses_ResolvesTheDependencyClosure()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Licensed",
            "1.0.0",
            Dependency("Contoso.Transitive", "[2.0.0]"),
            """<license type="expression">MIT</license>""");
        WriteLocalSourcePackage(
            source,
            "Contoso.Transitive",
            "2.0.0",
            "",
            """<license type="file">OSMFEULA.rtf</license>""");
        string root = WriteTemporaryFile(
            "root.nuspec",
            Manifest(
                "Contoso.Root",
                "1.0.0",
                Dependency("Contoso.Licensed", "[1.0.0]")));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            root,
            "--source",
            source,
            "-S",
            "Licenses",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "Complete",
            document.RootElement.GetProperty("summary")
                .GetProperty("licenses")
                .GetProperty("completion")
                .GetString());
        Dictionary<string, JsonElement> licenses = document.RootElement
            .GetProperty("licenses")
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("package").GetString()!,
                row => row.Clone(),
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, licenses.Count);
        Assert.Equal(
            "MIT",
            licenses["contoso.licensed"].GetProperty("license").GetString());
        Assert.Equal(
            "Expression",
            licenses["contoso.licensed"]
                .GetProperty("declaration_kind")
                .GetString());
        Assert.Equal(
            "OSMF",
            licenses["contoso.transitive"].GetProperty("license").GetString());
        Assert.Equal(
            "OSMFEULA.rtf",
            licenses["contoso.transitive"]
                .GetProperty("declaration_value")
                .GetString());
        Assert.DoesNotContain(
            licenses.Keys,
            packageId => string.Equals(
                packageId,
                "contoso.root",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DirectNuspecLicenses_DepthBoundedDiscoveryIsComplete()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Licensed",
            "1.0.0",
            Dependency("Contoso.Transitive", "[2.0.0]"),
            """<license type="expression">MIT</license>""");
        WriteLocalSourcePackage(
            source,
            "Contoso.Transitive",
            "2.0.0",
            "",
            """<license type="expression">Apache-2.0</license>""");
        string root = WriteTemporaryFile(
            "root.nuspec",
            Manifest(
                "Contoso.Root",
                "1.0.0",
                Dependency("Contoso.Licensed", "[1.0.0]")));

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            root,
            "--source",
            source,
            "--depth",
            "1",
            "-S",
            "Dependency Hierarchy,Licenses",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(
            "DepthBounded",
            summary.GetProperty("traversal_completion").GetString());
        Assert.Equal(
            "Complete",
            summary.GetProperty("licenses")
                .GetProperty("completion")
                .GetString());
        JsonElement license = Assert.Single(
            document.RootElement.GetProperty("licenses").EnumerateArray());
        Assert.Equal(
            "contoso.licensed",
            license.GetProperty("package").GetString());
        Assert.Equal("MIT", license.GetProperty("license").GetString());
    }

    [Fact]
    public async Task DirectNuspecLicenses_FailedSiblingMakesCountInexact()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Licensed",
            "1.0.0",
            "",
            """<license type="expression">MIT</license>""");
        string root = WriteTemporaryFile(
            "root.nuspec",
            Manifest(
                "Contoso.Root",
                "1.0.0",
                Dependency("Contoso.Licensed", "[1.0.0]")));
        string missing = Path.Combine(
            CreateTemporaryDirectory(),
            "missing.nuspec");

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--nuspec",
            root,
            "--nuspec",
            missing,
            "--source",
            source,
            "-S",
            "Licenses",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "license inventory completed as Partial",
            error,
            StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary");
        Assert.Equal(
            "Partial",
            summary.GetProperty("root_set_completion").GetString());
        Assert.Equal(
            "Partial",
            summary.GetProperty("licenses")
                .GetProperty("completion")
                .GetString());
        Assert.Single(
            document.RootElement.GetProperty("licenses").EnumerateArray());

        (int countExit, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--nuspec",
                root,
                "--nuspec",
                missing,
                "--source",
                source,
                "-S",
                "Licenses",
                "--count",
            ]);
        Assert.Equal(1, countExit);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Licenses' count",
            countError,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoredProjectLicenses_UsesExactRestoredPackages()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Direct",
            "1.0.0",
            Dependency("Contoso.Transitive", "[2.0.0]"),
            """<license type="expression">Apache-2.0</license>""");
        WriteLocalSourcePackage(
            source,
            "Contoso.Transitive",
            "2.0.0",
            "");
        string assets = WriteTemporaryFile(
            "project.assets.json",
            ProjectAssetsDocument());

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            assets,
            "--source",
            source,
            "-S",
            "Licenses",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Dictionary<string, string> licenses = document.RootElement
            .GetProperty("licenses")
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("package").GetString()!,
                row => row.GetProperty("license").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["contoso.direct"] = "Apache-2.0",
                ["contoso.transitive"] = "none",
            },
            licenses);
    }

    [Fact]
    public async Task RestoredProjectLicenses_RetainsUnavailableRows()
    {
        string source = CreateTemporaryDirectory();
        WriteLocalSourcePackage(
            source,
            "Contoso.Direct",
            "1.0.0",
            Dependency("Contoso.Transitive", "[2.0.0]"),
            """<license type="expression">Apache-2.0</license>""");
        string assets = WriteTemporaryFile(
            "project.assets.json",
            ProjectAssetsDocument());

        (int exitCode, string output, string error) = await RunCapturedAsync(
        [
            "depends",
            "--project",
            assets,
            "--source",
            source,
            "-S",
            "Licenses,Failures",
            "--format=json",
            "--compact",
        ]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            "license inventory completed as Partial",
            error,
            StringComparison.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement summary = document.RootElement.GetProperty("summary")
            .GetProperty("licenses");
        Assert.Equal("Partial", summary.GetProperty("completion").GetString());
        Assert.Equal(2, summary.GetProperty("packages").GetInt32());
        Assert.Equal(1, summary.GetProperty("available").GetInt32());
        Assert.Equal(1, summary.GetProperty("unavailable").GetInt32());
        JsonElement unavailable = Assert.Single(
            document.RootElement.GetProperty("licenses").EnumerateArray(),
            row => row.GetProperty("state").GetString() == "Unavailable");
        Assert.Equal(
            "unavailable",
            unavailable.GetProperty("license").GetString());
        Assert.True(unavailable.TryGetProperty("failure_reason", out _));
        JsonElement failure = Assert.Single(
            document.RootElement.GetProperty("failures").EnumerateArray());
        Assert.Equal("License", failure.GetProperty("phase").GetString());
        Assert.Equal(
            "ManifestAcquisitionFailed",
            failure.GetProperty("reason").GetString());
        JsonElement evidence = failure.GetProperty("evidence");
        Assert.Equal(
            "contoso.transitive",
            evidence.GetProperty("package_id").GetString());
        Assert.Equal(
            "2.0.0",
            evidence.GetProperty("package_version").GetString());

        (int countExit, string countOutput, string countError) =
            await RunCapturedAsync(
            [
                "depends",
                "--project",
                assets,
                "--source",
                source,
                "-S",
                "Licenses",
                "--count",
            ]);
        Assert.Equal(1, countExit);
        Assert.Empty(countOutput);
        Assert.Contains(
            "--count cannot report an exact 'Licenses' count",
            countError,
            StringComparison.Ordinal);
    }

    private static byte[] ProjectAssetsDocument() =>
        Encoding.UTF8.GetBytes(
            new JsonObject
            {
                ["version"] = 4,
                ["targets"] = new JsonObject
                {
                    ["net11.0"] = new JsonObject
                    {
                        ["Contoso.Direct/1.0.0"] = new JsonObject
                        {
                            ["type"] = "package",
                            ["dependencies"] = new JsonObject
                            {
                                ["Contoso.Transitive"] = "2.0.0",
                            },
                        },
                        ["Contoso.Transitive/2.0.0"] = new JsonObject
                        {
                            ["type"] = "package",
                        },
                    },
                },
                ["projectFileDependencyGroups"] = new JsonObject
                {
                    ["net11.0"] =
                        new JsonArray("Contoso.Direct >= 1.0.0"),
                },
                ["project"] = new JsonObject
                {
                    ["frameworks"] = new JsonObject
                    {
                        ["net11.0"] = new JsonObject
                        {
                            ["dependencies"] = new JsonObject
                            {
                                ["Contoso.Direct"] = new JsonObject
                                {
                                    ["target"] = "Package",
                                    ["version"] = "[1.0.0, )",
                                },
                            },
                        },
                    },
                },
            }.ToJsonString());
}
