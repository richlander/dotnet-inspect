using System.Text;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageHouseDependencyInputAdapterTests
{
    [Fact]
    public void DeclarationInputRetainsRootResolutionTargetAndAssociation()
    {
        PackageDependencyEvidenceRoot root = PackageRoot("[2.0.0]");
        PackageDependencyEvidenceDeclaration declaration =
            Assert.Single(
                Assert.Single(
                    Assert.IsType<
                        PackageDependencyEvidenceDeclarationResult.Available>(
                        root.Declaration).Groups).Declarations);
        PackageAcquisitionCandidate candidate =
            Candidate(
                PackageSourceCoordinate.Create(
                    declaration.CanonicalPackageId,
                    "2.0.0"));
        var resolution = new PackageDependencyCandidateResult.Resolved(
            declaration,
            candidate,
            []);
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact("net11.0");
        PackageHouseOperation operation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle);

        PackageHouseDependencyInput input =
            PackageHouseDependencyInputAdapter.Create(
                root,
                resolution,
                operation,
                target);

        Assert.Same(root, input.Root);
        Assert.Same(root.Processing, input.Processing);
        Assert.Same(target, input.Request.TargetContext);
        Assert.Same(operation, input.Request.Operation);
        Assert.Same(input.Association, input.Request.Association);
        PackageHouseDependencySubject.Declaration subject =
            Assert.IsType<PackageHouseDependencySubject.Declaration>(
                input.Subject);
        Assert.Same(declaration, subject.Evidence);
        Assert.Same(resolution, subject.Resolution);
        Assert.Same(candidate, subject.Candidate);
        Assert.Equal(
            PackageDependencyEvidenceAuthorship.LibraryDeclared,
            subject.Authorship);
        Assert.Same(
            candidate,
            Assert.IsType<PackageHouseDemand.Candidate>(
                input.Request.Demand).Value);
    }

    [Fact]
    public void DeclarationInputRejectsForeignOrUnsatisfiedEvidence()
    {
        PackageDependencyEvidenceRoot root = PackageRoot("[2.0.0]");
        PackageDependencyEvidenceDeclaration declaration =
            Assert.Single(
                Assert.Single(
                    Assert.IsType<
                        PackageDependencyEvidenceDeclarationResult.Available>(
                        root.Declaration).Groups).Declarations);
        PackageAcquisitionCandidate candidate =
            Candidate(
                PackageSourceCoordinate.Create(
                    declaration.CanonicalPackageId,
                    "3.0.0"));
        var unsatisfied = new PackageDependencyCandidateResult.Resolved(
            declaration,
            candidate,
            []);
        PackageHouseOperation operation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle);

        Assert.Throws<ArgumentException>(
            () => PackageHouseDependencyInputAdapter.Create(
                root,
                unsatisfied,
                operation));

        PackageDependencyEvidenceRoot foreignRoot =
            PackageRoot("[2.0.0]", packageId: "Another.Package");
        Assert.Throws<ArgumentException>(
            () => PackageHouseDependencyInputAdapter.Create(
                foreignRoot,
                new PackageDependencyCandidateResult.Resolved(
                    declaration,
                    Candidate(
                        PackageSourceCoordinate.Create(
                            declaration.CanonicalPackageId,
                            "2.0.0")),
                    []),
                operation));
    }

    [Fact]
    public void RelationshipInputRetainsObservedCoordinateWithoutRevalidatingConstraint()
    {
        PackageDependencyEvidenceRoot root = RestoredRoot();
        PackageDependencyEvidenceRelationshipResult.Available relationships =
            Assert.IsType<
                PackageDependencyEvidenceRelationshipResult.Available>(
                root.Relationships);
        PackageDependencyEvidenceRelationship original =
            relationships.Relationships.First(
                relationship =>
                    relationship.CanonicalRequestedConstraint
                        is not null);
        PackageDependencyEvidenceRelationship observed = original with
        {
            CanonicalRequestedConstraint = "[99.0.0]",
            SourceRequestedConstraintSpelling =
                new InertString(
                    TextPolicy.Field,
                    "[99.0.0]"),
        };
        root = root with
        {
            Relationships =
                new PackageDependencyEvidenceRelationshipResult.Available(
                    relationships.Packages,
                    [observed],
                    [],
                    PackageDependencyEvidencePhaseCompletion.Complete),
        };
        PackageAcquisitionCandidate candidate =
            Candidate(observed.ResolvedCoordinate);

        PackageHouseDependencyInput input =
            PackageHouseDependencyInputAdapter.Create(
                root,
                observed,
                candidate,
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Settle));

        PackageHouseDependencySubject.Relationship subject =
            Assert.IsType<PackageHouseDependencySubject.Relationship>(
                input.Subject);
        Assert.Same(observed, subject.Evidence);
        Assert.Same(candidate, subject.Candidate);
        Assert.Equal(observed.Authorship, subject.Authorship);
        Assert.Same(root.Processing, input.Processing);
    }

    [Fact]
    public void RelationshipInputRejectsForeignEvidenceAndCoordinate()
    {
        PackageDependencyEvidenceRoot root = RestoredRoot();
        PackageDependencyEvidenceRelationship relationship =
            Assert.IsType<
                PackageDependencyEvidenceRelationshipResult.Available>(
                root.Relationships).Relationships[0];
        PackageHouseOperation operation =
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Settle);

        Assert.Throws<ArgumentException>(
            () => PackageHouseDependencyInputAdapter.Create(
                root,
                relationship with
                {
                    ResolvedCoordinate =
                        PackageSourceCoordinate.Create(
                            relationship.ResolvedCoordinate.PackageId,
                            "99.0.0"),
                },
                Candidate(relationship.ResolvedCoordinate),
                operation));
        Assert.Throws<ArgumentException>(
            () => PackageHouseDependencyInputAdapter.Create(
                root,
                relationship,
                Candidate(
                    PackageSourceCoordinate.Create(
                        relationship.ResolvedCoordinate.PackageId,
                        "99.0.0")),
                operation));
    }

    [Fact]
    public void ProcessingStatesRemainUninterpreted()
    {
        PackageDependencyEvidenceRoot initial = PackageRoot("[2.0.0]");
        PackageDependencyEvidenceDeclaration declaration =
            Assert.Single(
                Assert.Single(
                    Assert.IsType<
                        PackageDependencyEvidenceDeclarationResult.Available>(
                        initial.Declaration).Groups).Declarations);
        PackageAcquisitionCandidate candidate =
            Candidate(
                PackageSourceCoordinate.Create(
                    declaration.CanonicalPackageId,
                    "2.0.0"));
        var resolution = new PackageDependencyCandidateResult.Resolved(
            declaration,
            candidate,
            []);
        PackageDependencyEvidenceProcessingResult[] states =
        [
            new PackageDependencyEvidenceProcessingResult.NotApplicable(),
            new PackageDependencyEvidenceProcessingResult.Available(
                [
                    PackageDependencyEvidenceProcessingObservation
                        .RestoreResolution,
                ],
                [],
                PackageDependencyEvidencePhaseCompletion.Complete),
            new PackageDependencyEvidenceProcessingResult.Available(
                [
                    PackageDependencyEvidenceProcessingObservation
                        .RestoreResolution,
                ],
                [
                    new PackageDependencyEvidenceProcessingFailure.Association(
                        PackageDependencyEvidenceProcessingObservation
                            .PackagePruningEvaluation),
                ],
                PackageDependencyEvidencePhaseCompletion.Incomplete),
            new PackageDependencyEvidenceProcessingResult.Unavailable(),
            new PackageDependencyEvidenceProcessingResult.Failed(
                new PackageDependencyEvidenceProcessingFailure.Association(
                    PackageDependencyEvidenceProcessingObservation
                        .PackagePruningEvaluation)),
        ];

        foreach (PackageDependencyEvidenceProcessingResult processing in states)
        {
            PackageDependencyEvidenceRoot root = initial with
            {
                Processing = processing,
            };

            PackageHouseDependencyInput input =
                PackageHouseDependencyInputAdapter.Create(
                    root,
                    resolution,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Settle));

            Assert.Same(processing, input.Processing);
        }
    }

    private static PackageDependencyEvidenceRoot PackageRoot(
        string versionConstraint,
        string packageId = "Example.Package")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>1.0.0</version>
                <authors>Example</authors>
                <description>Example</description>
                <dependencies>
                  <group targetFramework="net11.0">
                    <dependency id="Example.Dependency"
                                version="{{versionConstraint}}" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        PackageManifestFacts facts = Assert.IsType<
            PackageManifestFactsResult.Available>(
                PackageManifestFactsQuery.ExecuteSelfAttested(bytes)).Value;
        return Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            facts,
                            PackageDependencyEvidenceAcquisitionForm
                                .DirectNuspec,
                            "net11.0"),
                    ])).Roots);
    }

    private static PackageDependencyEvidenceRoot RestoredRoot()
    {
        RestoredProjectDependencyFacts facts = Assert.IsType<
            RestoredProjectDependencyFactsResult.Available>(
                RestoredProjectDependencyFactsQuery.Execute(
                    File.ReadAllBytes(
                        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
                            "project.assets.json")),
                    new RestoredProjectTargetRequest("net11.0"))).Value;
        return Assert.Single(
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery
                            .CreateRestoredProjectInput(
                                facts,
                                PackageDependencyEvidenceAcquisitionForm
                                    .ProjectAssets),
                    ])).Roots);
    }

    private static PackageAcquisitionCandidate Candidate(
        PackageSourceCoordinate coordinate)
    {
        var authority = new ConfiguredPackageAuthority(
            new PackageSource(
                "test",
                "https://test.example/v3/index.json"));
        return PackageAcquisitionCandidate.CreatePinned(
            new object(),
            coordinate,
            [authority]);
    }
}
