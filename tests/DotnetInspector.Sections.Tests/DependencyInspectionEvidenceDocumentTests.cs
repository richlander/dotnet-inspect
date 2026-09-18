using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class DependencyInspectionEvidenceDocumentTests
{
    [Fact]
    public void OperationPreservesSettledShareAcrossOrdinaryAndEnriched()
    {
        DependencyInspectionEvidenceDocument evidence = EmptyDocument();
        var share = new InspectionShare.NonProjectable(
            "asset-dependencies/share",
            "No exact package root was requested.");
        var request = new DependencyInspectionOperationRequest(
            new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: false,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            requestedRoots: 0,
            isPrefixRootSet: false,
            evidence.PackageInputs,
            evidence.AdmittedRootOccurrences,
            evidence.FailedRootOccurrences,
            roots: [],
            new DependencyGraphDocument([], [], [], [], []),
            additionalFailures: [],
            pruning: [],
            pruningFailures: [],
            DependencyInspectionPruningSummary.NotRequested,
            share);
        InspectionEnvelope<DependencyInspectionContent> ordinary =
            DependencyInspectionOperation.Execute(request);
        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument> enriched =
                DependencyInspectionOperation.ExecuteWithEvidence(request);

        Assert.Equal(ordinary, enriched.Inspection);
        Assert.Same(share, ordinary.Share);
        Assert.Same(share, enriched.Inspection.Share);
        Assert.Same(evidence.PackageInputs, enriched.Evidence.PackageInputs);
    }

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
                    source.Source,
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

    [Fact]
    public void GeneratedJsonRetainsConcretePackageEvidence()
    {
        var document = new DependencyInspectionEvidenceDocument(
            Outcome(admittedRoots: 1),
            [new DependencyRootOccurrenceIdentity(1)],
            []);

        JsonElement root = JsonSerializer.SerializeToElement(
            document,
            DependencyInspectionJsonContext.Default
                .DependencyInspectionEvidenceDocument);
        JsonElement packageRoot =
            root.GetProperty("packageInputs")
                .GetProperty("roots")[0];

        Assert.Equal(
            "package",
            packageRoot.GetProperty("identity")
                .GetProperty("case")
                .GetString());
        Assert.Equal(
            "example.package.0",
            packageRoot.GetProperty("identity")
                .GetProperty("coordinate")
                .GetProperty("packageId")
                .GetString());
        Assert.Equal(
            "package",
            packageRoot.GetProperty("provenance")
                .GetProperty("case")
                .GetString());
        Assert.Equal(
            "DirectNuspec",
            packageRoot.GetProperty("provenance")
                .GetProperty("acquisitionForm")
                .GetString());

    }

    [Fact]
    public void GeneratedContentJsonRetainsConcreteGraphEvidenceIdentity()
    {
        var graph = new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Type("Root"),
                    new InertString(TextPolicy.Field, "root")),
                new DependencyGraphNode(
                    1,
                    new DependencyGraphNodeIdentity.Type("Dependency"),
                    new InertString(TextPolicy.Field, "dependency")),
            ],
            [
                new DependencyGraphEdge(
                    0,
                    0,
                    1,
                    "depends",
                    [1],
                    1,
                    DependencyGraphResolutionState.Resolved,
                    new DependencyGraphEvidenceIdentity
                        .PackageVersionConstraint(
                            new InertString(TextPolicy.Field, "[1.0.0,)"))),
            ],
            [],
            []);
        var content = new DependencyInspectionContent(
            new DependencyInspectionSummary(
                DependencyInspectionRootSetCompletion.Complete,
                RequestedRoots: 1,
                AdmittedRoots: 1,
                FailedRoots: 0,
                DependencyInspectionTraversalCompletion.Complete,
                RequestedDepth: null,
                GraphNodes: 2,
                GraphEdges: 1,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionPruningSummary.NotRequested,
                IsPrefixRootSet: false,
                PackagePrefix: null),
            graph,
            [],
            [],
            [],
            []);

        JsonElement root = JsonSerializer.SerializeToElement(
            content,
            DependencyInspectionJsonContext.Default
                .DependencyInspectionContent);
        JsonElement evidenceIdentity =
            root.GetProperty("graph")
                .GetProperty("edges")[0]
                .GetProperty("evidenceIdentity");

        Assert.Equal(
            "packageVersionConstraint",
            evidenceIdentity.GetProperty("case").GetString());
        Assert.Equal(
            "[1.0.0,)",
            evidenceIdentity.GetProperty("value").GetString());

        DependencyInspectionContent? roundTrip = JsonSerializer.Deserialize(
            root.GetRawText(),
            DependencyInspectionJsonContext.Default
                .DependencyInspectionContent);
        Assert.NotNull(roundTrip);
        Assert.IsType<
            DependencyGraphEvidenceIdentity.PackageVersionConstraint>(
                Assert.Single(roundTrip.Graph.Edges).EvidenceIdentity);
    }

    [Fact]
    public void GeneratedContentJsonRetainsNestedFailureCases()
    {
        var rootIdentity = new PackageDependencyEvidenceRootIdentity.Package(
            PackageSourceCoordinate.Create("Example.Package", "1.0.0"));
        var groupIdentity = new PackageDependencyEvidenceGroupIdentity.Package(
            rootIdentity,
            IsImplicitManifestGroup: false,
            FirstSourceOccurrence: 0);
        var declarationIdentity =
            new PackageDependencyEvidenceDeclarationIdentity(
                groupIdentity,
                "example.dependency");
        var declaration = new PackageDependencyEvidenceDeclaration(
            declarationIdentity,
            "example.dependency",
            "[1.0.0,)",
            new InertString(TextPolicy.Field, "Example.Dependency"),
            new InertString(TextPolicy.Field, "[1.0.0,)"),
            SourceOccurrenceCount: 1,
            PackageDependencyEvidenceAuthorship.LibraryDeclared);
        var content = new DependencyInspectionContent(
            new DependencyInspectionSummary(
                DependencyInspectionRootSetCompletion.Partial,
                RequestedRoots: 1,
                AdmittedRoots: 1,
                FailedRoots: 0,
                DependencyInspectionTraversalCompletion.Failed,
                RequestedDepth: null,
                GraphNodes: 0,
                GraphEdges: 0,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionPruningSummary.NotRequested,
                IsPrefixRootSet: false,
                PackagePrefix: null),
            new DependencyGraphDocument([], [], [], [], []),
            [],
            [],
            [],
            [
                new DependencyInspectionFailure.Traversal(
                    new DependencyInspectionTraversalFailure(
                        "NoMatchingVersion",
                        SourceProjectionIndex: 0,
                        NodeIndex: null,
                        ProjectionIndex: null,
                        DeclarationIdentity: declarationIdentity,
                        PackageId: "example.dependency",
                        VersionConstraint: "[1.0.0,)",
                        CandidateOutcome:
                            new PackageDependencyTraversalCandidateResult.Failed(
                                new PackageDependencyTraversalCandidateFailure
                                    .NoMatchingVersion()),
                        ManifestFailure: null,
                        BudgetKind: null,
                        BudgetLimit: null,
                        RestoredFailure: null,
                        AffectedRootOccurrences: [1])),
                new DependencyInspectionFailure.Pruning(
                    new DependencyInspectionPruningFailure.Candidate(
                        RootOccurrence: 1,
                        RootIdentity: rootIdentity,
                        DeclarationIdentity: declarationIdentity,
                        PackageId: "example.dependency",
                        VersionConstraint: "[1.0.0,)",
                        Outcome: new PackageDependencyCandidateResult.Failed(
                            declaration,
                            new PackageDependencyCandidateFailure
                                .NoMatchingVersion()))),
            ]);

        JsonElement root = JsonSerializer.SerializeToElement(
            content,
            DependencyInspectionJsonContext.Default
                .DependencyInspectionContent);
        JsonElement traversalCandidate =
            root.GetProperty("failures")[0]
                .GetProperty("value")
                .GetProperty("candidateOutcome");
        JsonElement pruningCandidate =
            root.GetProperty("failures")[1]
                .GetProperty("value")
                .GetProperty("outcome");

        Assert.Equal(
            "failed",
            traversalCandidate.GetProperty("case").GetString());
        Assert.Equal(
            "noMatchingVersion",
            traversalCandidate.GetProperty("failure")
                .GetProperty("case")
                .GetString());
        Assert.Equal(
            "failed",
            pruningCandidate.GetProperty("case").GetString());
        Assert.Equal(
            "noMatchingVersion",
            pruningCandidate.GetProperty("failure")
                .GetProperty("case")
                .GetString());
    }

    [Fact]
    public void GeneratedContentJsonRetainsPruningEvaluation()
    {
        PackageDependencyEvidenceRoot evidenceRoot =
            Assert.Single(Outcome(admittedRoots: 1).Roots);
        var groupIdentity = new PackageDependencyEvidenceGroupIdentity.Package(
            (PackageDependencyEvidenceRootIdentity.Package)
                evidenceRoot.Identity,
            IsImplicitManifestGroup: false,
            FirstSourceOccurrence: 0);
        var declaration = new PackageDependencyEvidenceDeclaration(
            new PackageDependencyEvidenceDeclarationIdentity(
                groupIdentity,
                "example.dependency"),
            "example.dependency",
            "[1.0.0,)",
            new InertString(TextPolicy.Field, "Example.Dependency"),
            new InertString(TextPolicy.Field, "[1.0.0,)"),
            SourceOccurrenceCount: 1,
            PackageDependencyEvidenceAuthorship.LibraryDeclared);
        var applicability = new PackageHouseDependencyPruningApplicability(
            evidenceRoot,
            declaration,
            PackageHouseDependencyPruningApplicabilityState
                .CandidateRequired,
            Processing: null,
            TargetUnavailableReason: null);
        var pruning = new DependencyInspectionPruning(
            RootOccurrence: 1,
            evidenceRoot.Identity,
            evidenceRoot.Display,
            declaration.Identity,
            RequestedFramework:
                new InertString(TextPolicy.Field, "net11.0"),
            SelectedFramework:
                new InertString(TextPolicy.Field, "net11.0"),
            declaration.CanonicalPackageId,
            declaration.SourcePackageIdSpelling,
            declaration.CanonicalVersionConstraint,
            declaration.SourceVersionConstraintSpelling,
            CandidateVersion: "1.0.0",
            PlatformFamily: "DotNetRuntime",
            PlatformTargetFramework: "net11.0",
            PlatformVersion: "11.0.0",
            PlatformProvidedVersion: "2.0.0",
            DependencyInspectionPruningDisposition.PlatformDelegation,
            Reason: PlatformSubsumption.Subsumed.ToString(),
            applicability,
            CandidateOutcome: null,
            new DependencyInspectionPruningEvaluation(
                PlatformSubsumption.Subsumed,
                DelegatesToPlatform: true));
        var content = new DependencyInspectionContent(
            new DependencyInspectionSummary(
                DependencyInspectionRootSetCompletion.Complete,
                RequestedRoots: 1,
                AdmittedRoots: 1,
                FailedRoots: 0,
                DependencyInspectionTraversalCompletion.NotRequested,
                RequestedDepth: null,
                GraphNodes: 0,
                GraphEdges: 0,
                DependencyInspectionEvidencePhaseCompletion.Complete,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                new DependencyInspectionPruningSummary(
                    DependencyInspectionPruningCompletion.Complete,
                    Roots: 1,
                    Declarations: 1,
                    Evaluated: 1,
                    Delegated: 1,
                    Retained: 0,
                    NotEvaluated: 0,
                    SourceBounded: 0,
                    Failed: 0),
                IsPrefixRootSet: false,
                PackagePrefix: null),
            new DependencyGraphDocument([], [], [], [], []),
            [],
            [],
            [pruning],
            []);

        JsonElement root = JsonSerializer.SerializeToElement(
            content,
            DependencyInspectionJsonContext.Default
                .DependencyInspectionContent);
        JsonElement evaluation =
            root.GetProperty("pruning")[0]
                .GetProperty("evaluation");

        Assert.Equal(
            "Subsumed",
            evaluation.GetProperty("subsumption").GetString());
        Assert.True(
            evaluation.GetProperty("delegatesToPlatform").GetBoolean());
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

    private static DependencyInspectionEvidenceDocument EmptyDocument() =>
        new(
            Outcome(),
            [],
            []);

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
