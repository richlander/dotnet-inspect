using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections.Tests;

public sealed class PackageDependencyInspectionOperationTests
{
    [Fact]
    public async Task ExecuteAsync_PreservesEmptySelectedGroup()
    {
        PackageInspectionInput input = Input(
            """
            <dependencies>
              <group targetFramework="net8.0" />
            </dependencies>
            """);

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input, "net8.0"),
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<PackageDependencyInspectionOutcome.Available>(
                inspection.Content);
        Assert.Equal(
            "Package dependency inspection fixture.",
            available.Manifest.Description?.ToString());
        Assert.Equal(
            PackageManifestIdentityProvenance.ExpectedCoordinate,
            available.Manifest.IdentityProvenance);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.Selected,
            available.Dependencies.SelectionStatus);
        Assert.Equal(0, available.Dependencies.SelectedGroupIndex);
        Assert.Empty(
            Assert.Single(available.Dependencies.Groups).Dependencies);
        Assert.Empty(Assert.IsType<DeclaredPackageDependencyGroup>(
            available.Dependencies.SelectedGroup).Dependencies);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task ExecuteAsync_DistinguishesSuccessfulZeroDependencies()
    {
        PackageInspectionInput input = Input("");

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input),
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<PackageDependencyInspectionOutcome.Available>(
                inspection.Content);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.NoDependencyGroups,
            available.Dependencies.SelectionStatus);
        Assert.Empty(available.Dependencies.Groups);
        Assert.Null(available.Dependencies.SelectedGroup);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task ExecuteAsync_UsesSelfAttestedLocalManifestIdentity()
    {
        PackageInspectionInput input = Input(
            """
            <dependencies>
              <dependency id="Example.Dependency" version="[1.0.0]" />
            </dependencies>
            """,
            includeExpectedIdentity: false);

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input),
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<PackageDependencyInspectionOutcome.Available>(
                inspection.Content);
        Assert.Equal("Example.Package", available.Manifest.DeclaredPackageId);
        Assert.Equal("1.0.0", available.Manifest.DeclaredPackageVersion);
        Assert.Equal(
            PackageManifestIdentityProvenance.SelfAttested,
            available.Manifest.IdentityProvenance);
        Assert.Equal(
            "Example.Dependency",
            Assert.Single(
                Assert.Single(available.Dependencies.Groups).Dependencies).Id);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task ExecuteAsync_DistinguishesNoMatchingFramework()
    {
        PackageInspectionInput input = Input(
            """
            <dependencies>
              <group targetFramework="net8.0">
                <dependency id="Example.Dependency" version="[1.0.0]" />
              </group>
            </dependencies>
            """);

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input, "net9.0"),
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<PackageDependencyInspectionOutcome.Available>(
                inspection.Content);
        Assert.Equal(
            PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework,
            available.Dependencies.SelectionStatus);
        Assert.Null(available.Dependencies.SelectedGroup);
        Assert.Single(available.Dependencies.Groups);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesDependencyOccurrenceOrderAndJsonShape()
    {
        PackageInspectionInput input = Input(
            """
            <dependencies>
              <group targetFramework="net8.0">
                <dependency id="Example.Dependency" version="[1.0.0]" />
                <dependency id="example.dependency" version="[2.0.0]" />
                <dependency id="Example.Dependency" version="[3.0.0]" />
              </group>
            </dependencies>
            """);

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input, "net8.0"),
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<PackageDependencyInspectionOutcome.Available>(
                inspection.Content);
        Assert.Collection(
            Assert.Single(available.Dependencies.Groups).Dependencies,
            dependency =>
            {
                Assert.Equal("Example.Dependency", dependency.Id);
                Assert.Equal("[1.0.0]", dependency.VersionRange);
            },
            dependency =>
            {
                Assert.Equal("example.dependency", dependency.Id);
                Assert.Equal("[2.0.0]", dependency.VersionRange);
            },
            dependency =>
            {
                Assert.Equal("Example.Dependency", dependency.Id);
                Assert.Equal("[3.0.0]", dependency.VersionRange);
            });

        string json = JsonSerializer.Serialize(
            inspection,
            PackageDependencyInspectionJsonContext.Default
                .InspectionEnvelopePackageDependencyInspectionOutcome);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement content =
            document.RootElement.GetProperty("content");
        Assert.Equal("available", content.GetProperty("kind").GetString());
        JsonElement manifest = content.GetProperty("manifest");
        JsonElement coordinate = manifest.GetProperty("coordinate");
        Assert.Equal(
            "example.package",
            coordinate.GetProperty("packageId").GetString());
        Assert.Equal(
            "1.0.0",
            coordinate.GetProperty("version").GetString());
        Assert.Equal(
            "available",
            manifest.GetProperty("dependencyDeclarations")
                .GetProperty("outcome")
                .GetString());
        Assert.Equal(
            "available",
            manifest.GetProperty("frameworkReferences")
                .GetProperty("outcome")
                .GetString());

        InspectionEnvelope<PackageDependencyInspectionOutcome> roundTrip =
            Assert.IsType<
                InspectionEnvelope<PackageDependencyInspectionOutcome>>(
                JsonSerializer.Deserialize(
                    json,
                    PackageDependencyInspectionJsonContext.Default
                        .InspectionEnvelopePackageDependencyInspectionOutcome));
        var roundTripAvailable =
            Assert.IsType<PackageDependencyInspectionOutcome.Available>(
                roundTrip.Content);
        Assert.Equal(
            ["Example.Dependency", "example.dependency", "Example.Dependency"],
            Assert.Single(roundTripAvailable.Dependencies.Groups)
                .Dependencies
                .Select(dependency => dependency.Id));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTypedNoManifest()
    {
        var content = new InMemoryPackageContent(
            EmptyArchive(),
            fromCache: false,
            producerKey: "package-dependency-inspection-tests");
        PackageInspectionInput input =
            PackageInspectionInput.CreateLocal(
                content,
                "Example.Package",
                "1.0.0");

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input),
                TestContext.Current.CancellationToken);

        var noManifest =
            Assert.IsType<PackageDependencyInspectionOutcome.NoManifest>(
                inspection.Content);
        Assert.Equal("Example.Package", noManifest.PackageId);
        Assert.Equal("1.0.0", noManifest.PackageVersion);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTypedRejectedManifest()
    {
        PackageInspectionInput input = Input(
            """
            <dependencies>
              <dependency id="evil/../other" version="[1.0.0]" />
            </dependencies>
            """);

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input),
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<PackageDependencyInspectionOutcome.Failed>(
                inspection.Content);
        Assert.Equal(
            PackageDependencyInspectionFailureReason.ManifestRejected,
            failed.Reason);
        Assert.Equal(
            "Example.Package",
            Assert.IsType<PackageManifestFacts>(failed.Manifest)
                .DeclaredPackageId);
        Assert.Equal(
            "evil/../other",
            Assert.Single(failed.Manifest.DependencyIdentifierObservations)
                .Id);
        Assert.Equal(
            "package-dependencies.manifest-rejected",
            Assert.Single(inspection.Diagnostics).Code);
    }

    [Fact]
    public async Task ExecuteAsync_RetainsMalformedManifestFailure()
    {
        PackageInspectionInput input = Input("<dependencies>");

        var inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input),
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<PackageDependencyInspectionOutcome.Failed>(
                inspection.Content);
        var failure = Assert.IsType<PackageManifestFailure>(
            failed.ManifestFailure);
        Assert.Equal(PackageManifestFailureReason.MalformedXml, failure.Reason);
        Assert.True(failure.LineNumber > 0);
        Assert.True(failure.LinePosition > 0);
        Assert.Null(failed.Manifest);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTypedUnavailableContent()
    {
        PackageInspectionInput input = PackageInspectionInput.CreateLocal(
            new InMemoryPackageContent(
                [1, 2, 3, 4],
                fromCache: false,
                producerKey: "package-dependency-inspection-tests"),
            "Example.Package",
            "1.0.0");

        InspectionEnvelope<PackageDependencyInspectionOutcome> inspection =
            await PackageDependencyInspectionOperation.ExecuteAsync(
                new(input),
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<PackageDependencyInspectionOutcome.Failed>(
                inspection.Content);
        Assert.Equal(
            PackageDependencyInspectionFailureReason.ContentUnavailable,
            failed.Reason);
        Assert.Equal(
            "package-dependencies.content-unavailable",
            Assert.Single(inspection.Diagnostics).Code);
    }

    private static PackageInspectionInput Input(
        string dependencies,
        bool includeExpectedIdentity = true)
    {
        string manifest =
            $"""
             <package>
               <metadata>
                 <id>Example.Package</id>
                 <version>1.0.0</version>
                 <authors>Tests</authors>
                 <description>Package dependency inspection fixture.</description>
                 {dependencies}
               </metadata>
             </package>
             """;
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(
            package,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream entry =
                archive.CreateEntry("Example.Package.nuspec").Open();
            entry.Write(Encoding.UTF8.GetBytes(manifest));
        }

        var content = new InMemoryPackageContent(
            package.ToArray(),
            fromCache: false,
            producerKey: "package-dependency-inspection-tests");
        return includeExpectedIdentity
            ? PackageInspectionInput.CreateLocal(
                content,
                "Example.Package",
                "1.0.0")
            : PackageInspectionInput.CreateLocal(content);
    }

    private static byte[] EmptyArchive()
    {
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(
            package,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
        }
        return package.ToArray();
    }
}
