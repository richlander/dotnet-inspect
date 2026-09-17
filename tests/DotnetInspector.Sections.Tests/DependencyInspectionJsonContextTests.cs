using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class DependencyInspectionJsonContextTests
{
    [Fact]
    public void EmptyEnrichedEnvelopeRoundTripsThroughGeneratedMetadata()
    {
        var content = new DependencyInspectionContent(
            new DependencyInspectionSummary(
                DependencyInspectionRootSetCompletion.Complete,
                RequestedRoots: 0,
                AdmittedRoots: 0,
                FailedRoots: 0,
                DependencyInspectionTraversalCompletion.NotRequested,
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
            []);
        PackageDependencyEvidenceOutcome packageInputs =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([]));
        var evidence = new DependencyInspectionEvidenceDocument(
            packageInputs,
            [],
            []);
        var envelope =
            new EvidenceInspectionEnvelope<
                DependencyInspectionContent,
                DependencyInspectionEvidenceDocument>(
                new InspectionEnvelope<DependencyInspectionContent>(
                    content,
                    new InspectionShare.NonProjectable(
                        "depends",
                        "No portable share.")),
                evidence);
        JsonTypeInfo<
            EvidenceInspectionEnvelope<
                DependencyInspectionContent,
                DependencyInspectionEvidenceDocument>> typeInfo =
            TypeInfo<
                EvidenceInspectionEnvelope<
                    DependencyInspectionContent,
                    DependencyInspectionEvidenceDocument>>();

        string json = JsonSerializer.Serialize(envelope, typeInfo);
        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument>? roundTripped =
                JsonSerializer.Deserialize(json, typeInfo);

        Assert.NotNull(roundTripped);
        Assert.Empty(roundTripped.Inspection.Content.Roots);
        Assert.Empty(roundTripped.Inspection.Content.Graph.Nodes);
        Assert.Empty(roundTripped.Evidence.PackageInputs.Roots);
        Assert.Empty(roundTripped.Evidence.AdmittedRootOccurrences);
        Assert.IsType<InspectionShare.NonProjectable>(
            roundTripped.Inspection.Share);
    }

    [Fact]
    public void PackageEvidenceRoundTripsClosedRootAndFailureVariants()
    {
        PackageDependencyEvidenceOutcome packageInputs =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            PackageFacts("Example.Package"),
                            PackageDependencyEvidenceAcquisitionForm
                                .DirectNuspec),
                    ],
                    [
                        new PackageDependencyEvidenceRootFailure.Acquisition(
                            PackageDependencyEvidenceAcquisitionForm
                                .PackageArchive,
                            PackageDependencyEvidenceAcquisitionFailureReason
                                .AcquisitionFailed),
                    ]));
        var document = new DependencyInspectionEvidenceDocument(
            packageInputs,
            [new DependencyRootOccurrenceIdentity(1)],
            [new DependencyRootOccurrenceIdentity(2)]);
        JsonTypeInfo<DependencyInspectionEvidenceDocument> typeInfo =
            TypeInfo<DependencyInspectionEvidenceDocument>();

        string json = JsonSerializer.Serialize(document, typeInfo);
        DependencyInspectionEvidenceDocument? roundTripped =
            JsonSerializer.Deserialize(json, typeInfo);

        Assert.NotNull(roundTripped);
        PackageDependencyEvidenceRoot root =
            Assert.Single(roundTripped.PackageInputs.Roots);
        Assert.IsType<PackageDependencyEvidenceRootIdentity.Package>(
            root.Identity);
        Assert.IsType<PackageDependencyEvidenceRootProvenance.Package>(
            root.Provenance);
        Assert.IsType<PackageDependencyEvidenceDeclarationResult.Available>(
            root.Declaration);
        Assert.IsType<
            PackageDependencyEvidenceRelationshipResult.NotApplicable>(
            root.Relationships);
        Assert.IsType<PackageDependencyEvidenceProcessingResult.NotApplicable>(
            root.Processing);
        Assert.IsType<PackageDependencyEvidenceRootFailure.Acquisition>(
            Assert.Single(roundTripped.PackageInputs.FailedRoots));
        Assert.Equal(
            new DependencyRootOccurrenceIdentity(1),
            Assert.Single(roundTripped.AdmittedRootOccurrences));
        Assert.Equal(
            new DependencyRootOccurrenceIdentity(2),
            Assert.Single(roundTripped.FailedRootOccurrences));
    }

    [Fact]
    public void ContentRoundTripsClosedGraphAndFailureVariants()
    {
        var identity =
            new DependencyGraphNodeIdentity.Package("Example.Package", "1.0.0");
        var graph = new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new DependencyGraphNode(
                    0,
                    identity,
                    new InertString(TextPolicy.Field, "Example.Package")),
            ],
            [
                new DependencyGraphEdge(
                    0,
                    0,
                    0,
                    "dependency",
                    [1],
                    MinimumDepth: 0,
                    DependencyGraphResolutionState.Resolved,
                    new DependencyGraphEvidenceIdentity
                        .PackageVersionConstraint(
                            new InertString(TextPolicy.Field, "[1.0.0]"))),
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
                GraphNodes: 1,
                GraphEdges: 1,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionPruningSummary.NotRequested,
                IsPrefixRootSet: false,
                PackagePrefix: null),
            graph,
            [
                new DependencyInspectionRoot(
                    new DependencyRootOccurrenceIdentity(1),
                    DependencyInspectionRootKind.Package,
                    new InertString(TextPolicy.Field, "Example.Package@1.0.0"),
                    DependencyInspectionRootState.Admitted,
                    identity,
                    DependencyInspectionTraversalCompletion.Complete,
                    DependencyInspectionEvidenceAvailability.NotRequested,
                    DependencyInspectionEvidencePhaseCompletion.NotRequested,
                    DependencyInspectionSelectionStatus.NotRequested,
                    DependencyInspectionEvidenceAvailability.NotRequested,
                    DependencyInspectionEvidencePhaseCompletion.NotRequested),
            ],
            [],
            [],
            [
                new DependencyInspectionFailure.Evidence(
                    new DependencyEvidenceFailureRow(
                        DependencyEvidenceFailurePhase.Root,
                        "example",
                        SourceKind: null,
                        RootIndex: 1,
                        RootIdentity: null,
                        Group: null,
                        GroupIndex: null,
                        Source: null,
                        Subject: null,
                        PackageId: null,
                        PackageVersion: null,
                        SourceLabel: null,
                        new InertString(TextPolicy.Prose, "Example failure"),
                        Occurrences: 1)),
                new DependencyInspectionFailure.Traversal(
                    new DependencyInspectionTraversalFailure(
                        "example",
                        SourceProjectionIndex: null,
                        NodeIndex: null,
                        ProjectionIndex: null,
                        DeclarationIdentity: null,
                        PackageId: null,
                        VersionConstraint: null,
                        CandidateOutcome: null,
                        ManifestFailure: null,
                        BudgetKind: null,
                        BudgetLimit: null,
                        RestoredFailure: null,
                        AffectedRootOccurrences: [1])),
                new DependencyInspectionFailure.Pruning(
                    new DependencyInspectionPruningFailure.Inventory(
                        "runtime",
                        "net11.0",
                        new InertString(
                            TextPolicy.Prose,
                            "Inventory unavailable"),
                        AffectedRootOccurrences: [1],
                        AffectedDeclarations: 1)),
            ]);
        JsonTypeInfo<DependencyInspectionContent> typeInfo =
            TypeInfo<DependencyInspectionContent>();

        string json = JsonSerializer.Serialize(content, typeInfo);
        DependencyInspectionContent? roundTripped =
            JsonSerializer.Deserialize(json, typeInfo);

        Assert.NotNull(roundTripped);
        Assert.IsType<DependencyGraphNodeIdentity.Package>(
            Assert.Single(roundTripped.Graph.Nodes).Identity);
        Assert.IsType<
            DependencyGraphEvidenceIdentity.PackageVersionConstraint>(
            Assert.Single(roundTripped.Graph.Edges).EvidenceIdentity);
        Assert.IsType<DependencyInspectionFailure.Evidence>(
            roundTripped.Failures[0]);
        Assert.IsType<DependencyInspectionFailure.Traversal>(
            roundTripped.Failures[1]);
        Assert.IsType<DependencyInspectionFailure.Pruning>(
            roundTripped.Failures[2]);
        Assert.IsType<DependencyInspectionPruningFailure.Inventory>(
            ((DependencyInspectionFailure.Pruning)
                roundTripped.Failures[2]).Value);
    }

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        (JsonTypeInfo<T>)DependencyInspectionJsonContext.Default.GetTypeInfo(
            typeof(T))!;

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
}
