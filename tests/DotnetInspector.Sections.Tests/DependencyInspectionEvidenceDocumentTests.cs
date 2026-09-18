using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class DependencyInspectionEvidenceDocumentTests
{
    [Fact]
    public void ConstructionPreservesParallelAssociations()
    {
        PackageDependencyEvidenceOutcome outcome = Outcome(
            admittedRoots: 1,
            failedRoots:
            [
                AcquisitionFailure(),
            ]);
        var admitted = new DependencyRootOccurrenceIdentity(2);
        var failed = new DependencyRootOccurrenceIdentity(3);

        var document = new DependencyInspectionEvidenceDocument(
            outcome,
            [admitted],
            [failed]);

        Assert.Same(outcome, document.PackageInputs);
        Assert.Equal(admitted, Assert.Single(document.AdmittedRootOccurrences));
        Assert.Equal(failed, Assert.Single(document.FailedRootOccurrences));
    }

    [Fact]
    public void ProjectionUsesTypedRootOccurrenceAssociations()
    {
        PackageDependencyEvidenceOutcome outcome = Outcome(
            admittedRoots: 1,
            failedRoots:
            [
                AcquisitionFailure(),
            ]);
        var document = new DependencyInspectionEvidenceDocument(
            outcome,
            [new DependencyRootOccurrenceIdentity(2)],
            [new DependencyRootOccurrenceIdentity(3)]);

        DependencyEvidenceProjection projection =
            DependencyEvidenceProjection.Create(document);

        Assert.Equal(2, Assert.Single(projection.Roots).RootIndex);
        Assert.Equal(3, Assert.Single(projection.Failures).RootIndex);
    }

    [Fact]
    public void ConstructionNormalizesDefaultEmptyAssociations()
    {
        var document = new DependencyInspectionEvidenceDocument(
            Outcome(),
            default,
            default);

        Assert.Empty(document.AdmittedRootOccurrences);
        Assert.Empty(document.FailedRootOccurrences);
    }

    [Fact]
    public void ConstructionRejectsAssociationLengthMismatch()
    {
        PackageDependencyEvidenceOutcome admitted = Outcome(admittedRoots: 1);

        ArgumentException admittedFailure = Assert.Throws<ArgumentException>(
            () => new DependencyInspectionEvidenceDocument(
                admitted,
                [],
                []));

        Assert.Equal(
            "admittedRootOccurrences",
            admittedFailure.ParamName);

        PackageDependencyEvidenceOutcome failed = Outcome(
            failedRoots:
            [
                AcquisitionFailure(),
            ]);
        ArgumentException failedFailure = Assert.Throws<ArgumentException>(
            () => new DependencyInspectionEvidenceDocument(
                failed,
                [],
                []));

        Assert.Equal("failedRootOccurrences", failedFailure.ParamName);
    }

    [Fact]
    public void ConstructionRejectsMissingExplicitFailureOccurrence()
    {
        PackageDependencyEvidenceOutcome outcome = Outcome(
            failedRoots:
            [
                AcquisitionFailure(),
            ]);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new DependencyInspectionEvidenceDocument(
                outcome,
                [],
                [null]));

        Assert.Equal("failedRootOccurrences", failure.ParamName);
    }

    [Fact]
    public void ConstructionAllowsUnassociatedPackagePrefixFailure()
    {
        using IPackageSourceClient source = PackageSourceClientFactory.Create(
            new PackageSource(
                "nuget.org",
                "https://api.nuget.org/v3/index.json"),
            PackageSourceAssociation.Create());
        PackageDependencyEvidenceOutcome outcome = Outcome(
            failedRoots:
            [
                new PackageDependencyEvidenceRootFailure.PackageProfile(
                    PackageDependencyEvidenceSourceIdentity.Create(
                        source.Source).WithAssociation(1),
                    PackageProfileFailureKind.ManifestAcquisition,
                    ManifestFailureReason: null,
                    Coordinate: null,
                    PackageId: null,
                    Version: null,
                    new InertString(
                        TextPolicy.Field,
                        "Package profile unavailable",
                        128)),
            ]);

        var document = new DependencyInspectionEvidenceDocument(
            outcome,
            [],
            [null]);

        Assert.Null(Assert.Single(document.FailedRootOccurrences));
    }

    [Fact]
    public void RootOccurrenceIdentityIsOneBased()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DependencyRootOccurrenceIdentity(0));
    }

    [Fact]
    public void ConstructionRejectsDefaultRootOccurrenceIdentity()
    {
        ArgumentException admittedFailure = Assert.Throws<ArgumentException>(
            () => new DependencyInspectionEvidenceDocument(
                Outcome(admittedRoots: 1),
                [default(DependencyRootOccurrenceIdentity)],
                []));

        Assert.Equal(
            "admittedRootOccurrences",
            admittedFailure.ParamName);

        ArgumentException failedFailure = Assert.Throws<ArgumentException>(
            () => new DependencyInspectionEvidenceDocument(
                Outcome(
                    failedRoots:
                    [
                        AcquisitionFailure(),
                    ]),
                [],
                [default(DependencyRootOccurrenceIdentity)]));

        Assert.Equal("failedRootOccurrences", failedFailure.ParamName);
    }

    private static PackageDependencyEvidenceOutcome Outcome(
        int admittedRoots = 0,
        ImmutableArray<PackageDependencyEvidenceRootFailure> failedRoots =
            default)
    {
        failedRoots = failedRoots.IsDefault ? [] : failedRoots;
        return PackageDependencyEvidenceQuery.Execute(
            new PackageDependencyEvidenceRequest(
                [
                    .. Enumerable.Range(0, admittedRoots).Select(index =>
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            PackageFacts($"Example.Package.{index}"),
                            PackageDependencyEvidenceAcquisitionForm
                                .DirectNuspec)),
                ],
                failedRoots));
    }

    private static PackageManifestFacts PackageFacts(string packageId) =>
        new(
            PackageSourceCoordinate.Create(packageId, "1.0.0"),
            "",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            false,
            null,
            [])
        {
            IdentityProvenance =
                PackageManifestIdentityProvenance.SelfAttested,
        };

    private static PackageDependencyEvidenceRootFailure AcquisitionFailure() =>
        new PackageDependencyEvidenceRootFailure.Acquisition(
            PackageDependencyEvidenceAcquisitionForm.PackageArchive,
            PackageDependencyEvidenceAcquisitionFailureReason
                .AcquisitionFailed);
}
