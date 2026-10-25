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
            "package-dependencies.manifest-rejected",
            Assert.Single(inspection.Diagnostics).Code);
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

    [Fact]
    public async Task ExecuteAsync_RoundTripsTransportContract()
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
                new(input, "net8.0"),
                TestContext.Current.CancellationToken);
        string json = JsonSerializer.Serialize(
            inspection,
            PackageDependencyInspectionJsonContext.Default
                .PackageDependencyInspectionEnvelope);
        InspectionEnvelope<PackageDependencyInspectionOutcome> roundTripped =
            JsonSerializer.Deserialize(
                json,
                PackageDependencyInspectionJsonContext.Default
                    .PackageDependencyInspectionEnvelope)
            ?? throw new InvalidOperationException(
                "The Package dependency inspection envelope did not deserialize.");

        Assert.Contains("\"kind\":\"available\"", json, StringComparison.Ordinal);
        var available =
            Assert.IsType<PackageDependencyInspectionOutcome.Available>(
                roundTripped.Content);
        Assert.Equal("example.package", available.Coordinate.PackageId);
        Assert.Equal("net8.0", available.Dependencies.SelectedTargetFramework);
        Assert.Equal(
            "Example.Dependency",
            Assert.Single(
                Assert.Single(available.Dependencies.Groups).Dependencies).Id);
        Assert.Equal(
            "package-dependencies/share",
            Assert.IsType<InspectionShare.NonProjectable>(
                roundTripped.Share).Path);
        Assert.Empty(roundTripped.Diagnostics);
    }

    private static PackageInspectionInput Input(string dependencies)
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

        return PackageInspectionInput.CreateLocal(
            new InMemoryPackageContent(
                package.ToArray(),
                fromCache: false,
                producerKey: "package-dependency-inspection-tests"),
            "Example.Package",
            "1.0.0");
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
