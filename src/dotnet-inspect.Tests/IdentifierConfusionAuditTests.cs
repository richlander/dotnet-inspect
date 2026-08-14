using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class IdentifierConfusionAuditTests
{
    [Fact]
    public void PackageAudit_InspectsPackageAndDependencyIdentifierLocations()
    {
        var model = new InspectionResult
        {
            PackageName = "Contoso.Utilities",
            Deprecation = new PackageDeprecation { AlternatePackageId = "Δelta.Tools" },
            DependencyGroups =
            [
                new DependencyGroup
                {
                    TargetFramework = "net11.0",
                    Dependencies = [new PackageDependency { Id = "Ѕystem.Text.Json" }],
                },
            ],
            RuntimeDependencies = [new PackageDependency { Id = "Micrοsoft.Runtime" }],
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = "Αzure.Native",
                },
            ],
        };

        IReadOnlyList<IdentifierConfusionCase> cases =
            IdentifierConfusionAudit.InspectPackage(model);

        Assert.Equal(4, cases.Count);
        Assert.Collection(
            cases,
            value => AssertCase(
                value,
                "Deprecation.AlternatePackageId",
                IdentifierConcern.NonAscii,
                null),
            value => AssertCase(
                value,
                "DependencyGroups[0].Dependencies[0].Id",
                IdentifierConcern.NonAscii | IdentifierConcern.ReservedPrefixHomoglyph,
                "System"),
            value => AssertCase(
                value,
                "RuntimeDependencies[0].Id",
                IdentifierConcern.NonAscii | IdentifierConcern.ReservedPrefixHomoglyph,
                "Microsoft"),
            value => AssertCase(
                value,
                "RuntimeIdentifierPackages[0].PackageId",
                IdentifierConcern.NonAscii | IdentifierConcern.ReservedPrefixHomoglyph,
                "Azure"));
    }

    [Fact]
    public void LibraryAudit_InspectsAssemblyAndReferenceNames()
    {
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Ѕystem.Facade",
                References =
                [
                    new AssemblyReference("Contoso.Δelta", "1.0.0.0", null, null),
                ],
            },
            IdentifierConfusionReferenceClosure =
            [
                new AssemblyReferenceNode { Name = "Micrοsoft.Transitive", Depth = 1 },
            ],
        };

        IReadOnlyList<IdentifierConfusionCase> cases =
            IdentifierConfusionAudit.InspectLibrary(model);

        Assert.Collection(
            cases,
            value => AssertCase(
                value,
                "AssemblyInfo.AssemblyName",
                IdentifierConcern.NonAscii | IdentifierConcern.ReservedPrefixHomoglyph,
                "System"),
            value => AssertCase(
                value,
                "AssemblyInfo.References[0].Name",
                IdentifierConcern.NonAscii,
                null),
            value => AssertCase(
                value,
                "IdentifierConfusionReferenceClosure[0].Name",
                IdentifierConcern.NonAscii | IdentifierConcern.ReservedPrefixHomoglyph,
                "Microsoft"));
    }

    [Fact]
    public void LibraryAudit_PreservesCaseDistinctResolvedNames()
    {
        const string directName = "Micr\u039fsoft.Shared";
        const string transitiveName = "micr\u03bfsoft.shared";
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Root",
                References =
                [
                    new AssemblyReference(
                        directName,
                        "1.0.0.0",
                        "en-US",
                        null),
                ],
            },
            IdentifierConfusionReferenceClosure =
            [
                new AssemblyReferenceNode
                {
                    Name = directName,
                    Version = "1.0.0.0",
                    Depth = 0,
                },
                new AssemblyReferenceNode
                {
                    Name = transitiveName,
                    Version = "1.0.0.0",
                    Depth = 1,
                },
            ],
        };

        IReadOnlyList<IdentifierConfusionCase> cases =
            IdentifierConfusionAudit.InspectLibrary(model);

        Assert.Collection(
            cases,
            value =>
            {
                Assert.Equal(
                    "AssemblyInfo.References[0].Name",
                    value.Location);
                Assert.Equal(
                    [0x039F],
                    value.Confusion.NonAsciiCodePoints);
            },
            value =>
            {
                Assert.Equal(
                    "IdentifierConfusionReferenceClosure[1].Name",
                    value.Location);
                Assert.Equal(
                    [0x03BF],
                    value.Confusion.NonAsciiCodePoints);
            });
    }

    [Fact]
    public async Task PackageSignals_SeparatesIdentifierConfusionFromTextContainment()
    {
        var model = new InspectionResult
        {
            PackageName = "Ѕystem.Text.Json",
            Version = "1.0.0",
        };

        await AuditSignalBuilder.PopulatePackageAuditAsync(
            model,
            new HttpClient(),
            new VerboseLogger(false));

        AuditSignal identifier = Assert.Single(
            model.AuditSignals!,
            value => value.Signal == "Identifier confusion");
        Assert.Equal("Detected", identifier.Value);
        Assert.Equal(
            "1 non-ASCII identifier; 1 reserved-prefix homoglyph (System)",
            identifier.Evidence);

        AuditSignal containment = Assert.Single(
            model.AuditSignals!,
            value => value.Signal == "Artifact text containment");
        Assert.Equal("None", containment.Value);
    }

    [Fact]
    public void LibrarySignals_ReportIdentifierConfusionWithoutContent()
    {
        const string secret = "SECRET";
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = $"Micrοsoft.{secret}",
            },
        };

        AuditSignalBuilder.RefreshLibraryAuditSignals(model);

        AuditSignal signal = Assert.Single(
            model.AuditSignals!,
            value => value.Signal == "Identifier confusion");
        Assert.Equal("Detected", signal.Value);
        Assert.Equal(
            "1 non-ASCII identifier; 1 reserved-prefix homoglyph (Microsoft)",
            signal.Evidence);
        Assert.DoesNotContain(secret, signal.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void LibrarySignals_KeepTransitiveReferenceTraversalExplicit()
    {
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Contoso.Root",
                References =
                [
                    new AssemblyReference("System.Runtime", "1.0.0.0", null, null),
                ],
            },
            IdentifierConfusionReferenceClosure =
            [
                new AssemblyReferenceNode { Name = "Micrοsoft.Transitive", Depth = 1 },
            ],
        };

        AuditSignalBuilder.RefreshLibraryAuditSignals(model);

        AuditSignal signal = Assert.Single(
            model.AuditSignals!,
            value => value.Signal == "Identifier confusion");
        Assert.Equal("None", signal.Value);
        Assert.Equal("all inspected assembly names use ASCII characters", signal.Evidence);
    }

    private static void AssertCase(
        IdentifierConfusionCase value,
        string location,
        IdentifierConcern concerns,
        string? reservedPrefix)
    {
        Assert.Equal(location, value.Location);
        Assert.Equal(concerns, value.Confusion.Concerns);
        Assert.Equal(reservedPrefix, value.Confusion.ReservedPrefixMatch?.ReservedPrefix);
    }
}
