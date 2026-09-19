using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
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
                HierarchyOccurrences: 0,
                CanonicalNodes: 0,
                Relationships: 0,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionPruningSummary.NotRequested,
                IsPrefixRootSet: false,
                PackagePrefix: null),
            DependencyHierarchyDocument.Empty,
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
        Assert.Empty(
            roundTripped.Inspection.Content.Hierarchy.BackingGraph.Nodes);
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
                HierarchyOccurrences: 1,
                CanonicalNodes: 1,
                Relationships: 1,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionPruningSummary.NotRequested,
                IsPrefixRootSet: false,
                PackagePrefix: null)
            {
                Licenses = new DependencyInspectionLicenseSummary(
                    DependencyInspectionLicenseCompletion.Partial,
                    Packages: 2,
                    Available: 1,
                    Unavailable: 1),
            },
            DependencyHierarchyDocument.Create(graph),
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
                        new InertString(
                            TextPolicy.Prose,
                            "Example failure\r\nwith\tdetail"),
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
                            "Inventory unavailable\r\nTry again.\tLater."),
                        AffectedRootOccurrences: [1],
                        AffectedDeclarations: 1)),
            ])
        {
            Licenses =
            [
                DependencyInspectionLicense.Create(
                    PackageLicenseInventoryItem.Available(
                        PackageSourceCoordinate.Create(
                            "Example.Dependency",
                            "2.0.0"),
                        new PackageLicenseDeclaration(
                            PackageLicenseDeclarationKind.Expression,
                            "MIT"))),
                DependencyInspectionLicense.Create(
                    PackageLicenseInventoryItem.Unavailable(
                        PackageSourceCoordinate.Create(
                            "Example.Unavailable",
                            "3.0.0"),
                        new PackageLicenseInventoryFailure(
                            PackageLicenseInventoryFailureReason
                                .ManifestAcquisitionFailed,
                            "The exact package manifest could not be acquired."))),
            ],
        };
        JsonTypeInfo<DependencyInspectionContent> typeInfo =
            TypeInfo<DependencyInspectionContent>();

        string json = JsonSerializer.Serialize(content, typeInfo);
        DependencyInspectionContent? roundTripped =
            JsonSerializer.Deserialize(json, typeInfo);

        Assert.NotNull(roundTripped);
        Assert.IsType<DependencyGraphNodeIdentity.Package>(
            Assert.Single(roundTripped.Hierarchy.BackingGraph.Nodes).Identity);
        Assert.Equal(
            "Example.Package",
            Assert.Single(
                roundTripped.Hierarchy.BackingGraph.Nodes).Label.ToString());
        var edgeEvidence = Assert.IsType<
            DependencyGraphEvidenceIdentity.PackageVersionConstraint>(
            Assert.Single(
                roundTripped.Hierarchy.BackingGraph.Edges).EvidenceIdentity);
        Assert.Equal("[1.0.0]", edgeEvidence.Value.ToString());
        Assert.Equal(
            "Example.Package@1.0.0",
            Assert.Single(roundTripped.Roots).Input.ToString());
        Assert.IsType<DependencyInspectionFailure.Evidence>(
            roundTripped.Failures[0]);
        Assert.Equal(
            "Example failure\r\nwith\tdetail",
            ((DependencyInspectionFailure.Evidence)
                roundTripped.Failures[0]).Value.Message.ToString());
        Assert.IsType<DependencyInspectionFailure.Traversal>(
            roundTripped.Failures[1]);
        Assert.IsType<DependencyInspectionFailure.Pruning>(
            roundTripped.Failures[2]);
        var inventory = Assert.IsType<
            DependencyInspectionPruningFailure.Inventory>(
            ((DependencyInspectionFailure.Pruning)
                roundTripped.Failures[2]).Value);
        Assert.Equal(
            "Inventory unavailable\r\nTry again.\tLater.",
            inventory.Message.ToString());
        Assert.Equal(
            DependencyInspectionLicenseCompletion.Partial,
            roundTripped.Summary.Licenses.Completion);
        Assert.Equal("MIT", roundTripped.Licenses[0].License.ToString());
        Assert.Equal(
            PackageLicenseInventoryFailureReason.ManifestAcquisitionFailed,
            roundTripped.Licenses[1].FailureReason);
        Assert.Equal(
            "The exact package manifest could not be acquired.",
            roundTripped.Licenses[1].FailureMessage?.ToString());
    }

    [Fact]
    public void FieldValueRejectsMultilineEncodedText()
    {
        const string json =
            """
            {
              "kind": "package-version-constraint",
              "value": "line\nbreak"
            }
            """;
        JsonTypeInfo<DependencyGraphEvidenceIdentity> typeInfo =
            TypeInfo<DependencyGraphEvidenceIdentity>();

        Assert.Throws<FormatException>(
            () => JsonSerializer.Deserialize(json, typeInfo));
    }

    [Fact]
    public void ContentRoundTripsLibraryGraphIdentityVariants()
    {
        var assembly = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "Example.Assembly",
                new Version(1, 2, 3, 4),
                "neutral",
                "0011223344556677"));
        var module = new ManagedMetadataIdentity.Module(
            "Example.Module.netmodule",
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));
        var graph = new DependencyGraphDocument(
            [],
            [
                new DependencyGraphNode(
                    Id: 0,
                    new DependencyGraphNodeIdentity.Library(assembly),
                    new InertString(TextPolicy.Field, assembly.Name)),
                new DependencyGraphNode(
                    Id: 1,
                    new DependencyGraphNodeIdentity.Library(module),
                    new InertString(TextPolicy.Field, module.Name)),
            ],
            [],
            [],
            []);

        DependencyGraphDocument roundTripped = RoundTrip(graph);

        var roundTrippedAssembly = Assert.IsType<
            ManagedMetadataIdentity.Assembly>(
                Assert.IsType<DependencyGraphNodeIdentity.Library>(
                    roundTripped.Nodes[0].Identity).Identity);
        Assert.Equal(assembly.Identity, roundTrippedAssembly.Identity);
        var roundTrippedModule = Assert.IsType<ManagedMetadataIdentity.Module>(
            Assert.IsType<DependencyGraphNodeIdentity.Library>(
                roundTripped.Nodes[1].Identity).Identity);
        Assert.Equal(module.ModuleName, roundTrippedModule.ModuleName);
        Assert.Equal(module.ModuleVersionId, roundTrippedModule.ModuleVersionId);
    }

    [Fact]
    public void DefaultGeneratedArraysSerializeAsEmpty()
    {
        DependencyGraphDocument graph = RoundTrip(
            new DependencyGraphDocument(
                default,
                default,
                default,
                default,
                default));

        Assert.Empty(graph.Roots);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Empty(graph.PackageProjections);
        Assert.Empty(graph.DepthBoundaries);

        DependencyGraphDocument nestedGraph = RoundTrip(
            new DependencyGraphDocument(
                [],
                [],
                [
                    new DependencyGraphEdge(
                        Id: 0,
                        SourceNodeId: 0,
                        TargetNodeId: 0,
                        Relationship: "dependency",
                        RootOccurrences: default,
                        MinimumDepth: 0,
                        DependencyGraphResolutionState.Declared,
                        EvidenceIdentity: null),
                ],
                [
                    new DependencyGraphPackageProjection(
                        Id: 0,
                        NodeId: 0,
                        PackageDependencyTraversalProjectionKind.RootSupplied,
                        PackageDependencyTraversalProjectionExpansion.Expanded,
                        Evidence: null,
                        Candidate: null,
                        RootOccurrence: null,
                        Diagnostics: default),
                ],
                [
                    new DependencyGraphDepthBoundary(
                        NodeId: 0,
                        PackageProjectionId: 0,
                        MaximumDepth: 1,
                        RootOccurrences: default,
                        DependencyGraphDepthBoundaryProducerKind.Package),
                ]));

        Assert.Empty(Assert.Single(nestedGraph.Edges).RootOccurrences);
        Assert.Empty(Assert.Single(nestedGraph.Edges).PackageDiagnostics);
        Assert.Empty(
            Assert.Single(nestedGraph.PackageProjections).Diagnostics);
        Assert.Empty(
            Assert.Single(nestedGraph.DepthBoundaries).RootOccurrences);

        DependencyInspectionContent content = RoundTrip(
            new DependencyInspectionContent(
                new DependencyInspectionSummary(
                    DependencyInspectionRootSetCompletion.Complete,
                    RequestedRoots: 0,
                    AdmittedRoots: 0,
                    FailedRoots: 0,
                    DependencyInspectionTraversalCompletion.NotRequested,
                    RequestedDepth: null,
                    HierarchyOccurrences: 0,
                    CanonicalNodes: 0,
                    Relationships: 0,
                    DependencyInspectionEvidencePhaseCompletion.NotRequested,
                    DependencyInspectionEvidencePhaseCompletion.NotRequested,
                    DependencyInspectionPruningSummary.NotRequested,
                    IsPrefixRootSet: false,
                    PackagePrefix: null),
                DependencyHierarchyDocument.Empty,
                default,
                default,
                default,
                default));

        Assert.Empty(content.Roots);
        Assert.Empty(content.Dependencies);
        Assert.Empty(content.Pruning);
        Assert.Empty(content.Failures);

        DependencyInspectionContent nestedContent = RoundTrip(
            content with
            {
                Failures =
                [
                    new DependencyInspectionFailure.Traversal(
                        new DependencyInspectionTraversalFailure(
                            "boundary",
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
                            AffectedRootOccurrences: default)),
                    new DependencyInspectionFailure.Pruning(
                        new DependencyInspectionPruningFailure.Inventory(
                            "runtime",
                            "net11.0",
                            new InertString(
                                TextPolicy.Prose,
                                "Inventory unavailable"),
                            AffectedRootOccurrences: default,
                            AffectedDeclarations: 0)),
                ],
            });

        Assert.Empty(
            ((DependencyInspectionFailure.Traversal)
                nestedContent.Failures[0]).Value.AffectedRootOccurrences);
        Assert.Empty(
            ((DependencyInspectionPruningFailure.Inventory)
                ((DependencyInspectionFailure.Pruning)
                    nestedContent.Failures[1]).Value).AffectedRootOccurrences);

        PackageDependencyEvidenceOutcome produced =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            PackageFacts("Example.Package") with
                            {
                                DependencyGroups =
                                [
                                    new DeclaredPackageDependencyGroup(
                                        TargetFramework: "",
                                        Dependencies: [],
                                        IsImplicitManifestGroup: true),
                                ],
                            },
                            PackageDependencyEvidenceAcquisitionForm.DirectNuspec),
                    ]));
        var available = Assert.IsType<
            PackageDependencyEvidenceDeclarationResult.Available>(
                Assert.Single(produced.Roots).Declaration);
        PackageDependencyEvidenceGroup producedGroup =
            Assert.Single(available.Groups);
        PackageDependencyEvidenceGroup group = RoundTrip(
            new PackageDependencyEvidenceGroup(
                producedGroup.Identity,
                producedGroup.FrameworkScope,
                SourceOccurrences: default,
                producedGroup.OrderKey,
                Declarations: default));

        Assert.Empty(group.SourceOccurrences);
        Assert.Empty(group.Declarations);

        PackageDependencyEvidenceOutcome outcome = RoundTrip(
            new PackageDependencyEvidenceOutcome(
                default,
                default,
                new PackageDependencyEvidenceRootSetSummary(
                    PackageDependencyEvidenceRootSetCompletion.Complete,
                    AdmittedRootCount: 0,
                    RejectedRootCount: 0,
                    FailedRootCount: 0,
                    IsTruncated: false,
                    PackagePrefixCompletion: null),
                new PackageDependencyEvidencePhaseSummary(
                    new PackageDependencyEvidencePhaseCounts(0, 0, 0, 0, 0),
                    new PackageDependencyEvidencePhaseCounts(0, 0, 0, 0, 0),
                    new PackageDependencyEvidencePhaseCounts(0, 0, 0, 0, 0))));

        Assert.Empty(outcome.Roots);
        Assert.Empty(outcome.FailedRoots);
    }

    [Fact]
    public void PackageCandidateSerializationCarriesNoSourceAuthority()
    {
        var source = new PackageSource(
            "private",
            "https://packages.example.test/v3/index.json",
            new PackageSourceCredential("alice", "correct-horse"));
        PackageSourceAuthorization authorization =
            PackageSourceAuthorization.Authorize([source]);
        PackageAcquisitionCandidate candidate = Assert.IsType<
            PackageAcquisitionCandidate>(
                new PackageAcquisitionCandidateIssuer()
                    .ResolvePinnedCandidate(
                        authorization,
                        PackageSourceCoordinate.Create(
                            "Example.Package",
                            "1.2.3"))
                    .Candidate);
        var projection = new DependencyGraphPackageProjection(
            Id: 0,
            NodeId: 0,
            PackageDependencyTraversalProjectionKind.CandidateAcquired,
            PackageDependencyTraversalProjectionExpansion.Expanded,
            Evidence: null,
            DependencyInspectionPackageCandidate.Create(candidate),
            RootOccurrence: null,
            Diagnostics: [])
        {
            RuntimeCandidate = candidate,
        };
        var graph = new DependencyGraphDocument(
            [],
            [],
            [],
            [projection],
            []);

        string json = JsonSerializer.Serialize(
            graph,
            TypeInfo<DependencyGraphDocument>());

        Assert.DoesNotContain("alice", json, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "correct-horse",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "packages.example.test",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"authorities\"", json, StringComparison.Ordinal);
        DependencyGraphDocument roundTripped = JsonSerializer.Deserialize(
            json,
            TypeInfo<DependencyGraphDocument>())!;
        DependencyInspectionPackageCandidate roundTrippedCandidate =
            Assert.IsType<DependencyInspectionPackageCandidate>(
                Assert.Single(roundTripped.PackageProjections).Candidate);
        Assert.Equal("example.package", roundTrippedCandidate.Coordinate.PackageId);
        Assert.Equal("1.2.3", roundTrippedCandidate.Coordinate.Version);
        Assert.Null(
            Assert.Single(roundTripped.PackageProjections).RuntimeCandidate);
    }

    [Fact]
    public void PortableContentOutcomesRoundTripEveryReachableFamily()
    {
        var authorityFailure =
            new DependencyInspectionPackageAuthorityFailure(
                new InertString(TextPolicy.Field, "private"),
                PackageAuthorityFailureKind.Transport,
                "The source failed.",
                SourceFailure: null,
                ResultSource: null,
                new PackageSourceTimeout(
                    PackageSourceTimeoutKind.Request,
                    TimeSpan.FromSeconds(5)),
                IsRequiredProducerUnavailable: false);
        var candidate = new DependencyInspectionPackageCandidate(
            PackageSourceCoordinate.Create("Example.Package", "1.2.3"),
            PackageAcquisitionCandidateKind.CallerPinned,
            DiscoveryContract: null);
        DependencyInspectionPackageCandidateOutcome[] candidateOutcomes =
        [
            new DependencyInspectionPackageCandidateOutcome.Resolved(
                candidate,
                [authorityFailure]),
            new DependencyInspectionPackageCandidateOutcome.Resolved(
                candidate,
                default),
            new DependencyInspectionPackageCandidateOutcome.Failed(
                new DependencyInspectionPackageCandidateFailure
                    .AuthorizationDenied([authorityFailure])),
            new DependencyInspectionPackageCandidateOutcome.Failed(
                new DependencyInspectionPackageCandidateFailure
                    .AuthorizationDenied(default)),
            new DependencyInspectionPackageCandidateOutcome.Failed(
                new DependencyInspectionPackageCandidateFailure
                    .NoMatchingVersion()),
            new DependencyInspectionPackageCandidateOutcome.Incomplete(
                new DependencyInspectionPackageCandidateIncomplete
                    .PinnedAuthorization([authorityFailure])),
            new DependencyInspectionPackageCandidateOutcome.Incomplete(
                new DependencyInspectionPackageCandidateIncomplete
                    .PinnedAuthorization(default)),
            new DependencyInspectionPackageCandidateOutcome.Incomplete(
                new DependencyInspectionPackageCandidateIncomplete
                    .VersionDiscovery(
                        PackageVersionDiscoveryState.Partial,
                        new DependencyInspectionPackageVersionDiscoveryContract(
                            ContractVersion: 1,
                            IncludePrerelease: true,
                            IncludeUnlisted: false,
                            Limit: null),
                        CandidateObservationCount: 1,
                        [authorityFailure])),
            new DependencyInspectionPackageCandidateOutcome.Incomplete(
                new DependencyInspectionPackageCandidateIncomplete
                    .VersionDiscovery(
                        PackageVersionDiscoveryState.Partial,
                        new DependencyInspectionPackageVersionDiscoveryContract(
                            ContractVersion: 1,
                            IncludePrerelease: true,
                            IncludeUnlisted: false,
                            Limit: null),
                        CandidateObservationCount: 0,
                        default)),
        ];
        var packageRoot =
            new PackageDependencyEvidenceRootIdentity.Package(
                PackageSourceCoordinate.Create("Example.Package", "1.2.3"));
        var group = new PackageDependencyEvidenceGroupIdentity.Package(
            packageRoot,
            IsImplicitManifestGroup: true,
            FirstSourceOccurrence: 1);
        DependencyInspectionPackageManifestFailure[] manifestFailures =
        [
            new DependencyInspectionPackageManifestFailure.Acquisition(
                [authorityFailure]),
            new DependencyInspectionPackageManifestFailure.Acquisition(
                default),
            new DependencyInspectionPackageManifestFailure
                .IncompleteAcquisition([authorityFailure]),
            new DependencyInspectionPackageManifestFailure
                .IncompleteAcquisition(default),
            new DependencyInspectionPackageManifestFailure.Identity(
                new PackageManifestFailure(
                    PackageManifestFailureReason.MalformedXml)),
            new DependencyInspectionPackageManifestFailure.Declaration(
                new PackageDependencyEvidenceDeclarationFailure
                    .InvalidPackageDeclaration(
                        group,
                        SourceOccurrenceCount: 1)),
            new DependencyInspectionPackageManifestFailure
                .ManifestProjectionBudgetExhausted(17),
        ];
        DependencyInspectionRestoredTraversalOutcomeFailure[]
            restoredFailures =
            [
                new DependencyInspectionRestoredTraversalOutcomeFailure.Document(
                    new RestoredProjectDependencyFailure(
                        RestoredProjectDependencyFailureReason
                            .MalformedOrDuplicateBearingJson)),
                new DependencyInspectionRestoredTraversalOutcomeFailure.Graph(
                    new RestoredProjectGraphFailure(
                        RestoredProjectGraphFailureReason.UnresolvedDependency)),
            ];
        DependencyInspectionPruningResult[] pruningResults =
        [
            new DependencyInspectionPruningResult.Evaluated(
                PackageSourceCoordinate.Create("Example.Package", "1.2.3"),
                DotnetInspector.Platforms.PlatformFamily.DotNetRuntime,
                "net11.0",
                "11.0.0",
                "11.0.0",
                PlatformSubsumption.Subsumed,
                DelegatesToPlatform: true),
            new DependencyInspectionPruningResult
                .ApplicationAuthoredExemption(),
            new DependencyInspectionPruningResult.UnattributedAuthorship(),
            new DependencyInspectionPruningResult.TargetUnavailable(
                PackageHouseDependencyPruningTargetUnavailableReason
                    .InventoryUnavailable),
        ];

        foreach (DependencyInspectionPackageCandidateOutcome value in
                 candidateOutcomes)
        {
            Assert.Equal(value.GetType(), RoundTrip(value).GetType());
        }
        foreach (DependencyInspectionPackageManifestFailure value in
                 manifestFailures)
        {
            Assert.Equal(value.GetType(), RoundTrip(value).GetType());
        }
        foreach (DependencyInspectionRestoredTraversalOutcomeFailure value in
                 restoredFailures)
        {
            Assert.Equal(value.GetType(), RoundTrip(value).GetType());
        }
        foreach (DependencyInspectionPruningResult value in pruningResults)
            Assert.Equal(value.GetType(), RoundTrip(value).GetType());

        var declarationIdentity =
            new PackageDependencyEvidenceDeclarationIdentity(
                group,
                "example.package");
        var content = new DependencyInspectionContent(
            new DependencyInspectionSummary(
                DependencyInspectionRootSetCompletion.Partial,
                RequestedRoots: 1,
                AdmittedRoots: 1,
                FailedRoots: 0,
                DependencyInspectionTraversalCompletion.Partial,
                RequestedDepth: null,
                HierarchyOccurrences: 0,
                CanonicalNodes: 0,
                Relationships: 0,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionEvidencePhaseCompletion.NotRequested,
                DependencyInspectionPruningSummary.NotRequested,
                IsPrefixRootSet: false,
                PackagePrefix: null),
            DependencyHierarchyDocument.Empty,
            [],
            [],
            [],
            [
                new DependencyInspectionFailure.Traversal(
                    new DependencyInspectionTraversalFailure(
                        "candidate",
                        SourceProjectionIndex: 0,
                        NodeIndex: null,
                        ProjectionIndex: null,
                        declarationIdentity,
                        "example.package",
                        "[1.0.0]",
                        candidateOutcomes[2],
                        ManifestFailure: null,
                        BudgetKind: null,
                        BudgetLimit: null,
                        RestoredFailure: null,
                        AffectedRootOccurrences: [1])),
                new DependencyInspectionFailure.Traversal(
                    new DependencyInspectionTraversalFailure(
                        "manifest",
                        SourceProjectionIndex: 0,
                        NodeIndex: 0,
                        ProjectionIndex: 0,
                        DeclarationIdentity: null,
                        "example.package",
                        "1.2.3",
                        CandidateOutcome: null,
                        manifestFailures[6],
                        BudgetKind: null,
                        BudgetLimit: 17,
                        RestoredFailure: null,
                        AffectedRootOccurrences: [1])),
                new DependencyInspectionFailure.Traversal(
                    new DependencyInspectionTraversalFailure(
                        "restored",
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
                        new DependencyInspectionRestoredTraversalFailure.Outcome(
                            restoredFailures[0]),
                        AffectedRootOccurrences: [1])),
                new DependencyInspectionFailure.Pruning(
                    new DependencyInspectionPruningFailure.Candidate(
                        RootOccurrence: 1,
                        packageRoot,
                        declarationIdentity,
                        "example.package",
                        "[1.0.0]",
                        candidateOutcomes[2])),
            ]);

        DependencyInspectionContent roundTrippedContent = RoundTrip(content);
        Assert.IsType<DependencyInspectionPackageCandidateOutcome.Failed>(
            ((DependencyInspectionFailure.Traversal)
                roundTrippedContent.Failures[0]).Value.CandidateOutcome);
        Assert.IsType<
            DependencyInspectionPackageManifestFailure
                .ManifestProjectionBudgetExhausted>(
            ((DependencyInspectionFailure.Traversal)
                roundTrippedContent.Failures[1]).Value.ManifestFailure);
        Assert.IsType<
            DependencyInspectionRestoredTraversalOutcomeFailure.Document>(
            ((DependencyInspectionRestoredTraversalFailure.Outcome)
                ((DependencyInspectionFailure.Traversal)
                    roundTrippedContent.Failures[2]).Value.RestoredFailure!)
                .Value);
        Assert.IsType<DependencyInspectionPackageCandidateOutcome.Failed>(
            ((DependencyInspectionPruningFailure.Candidate)
                ((DependencyInspectionFailure.Pruning)
                    roundTrippedContent.Failures[3]).Value).Outcome);
    }

    [Fact]
    public void PortableContentSourceIdentityRejectsMalformedWireValues()
    {
        const string ValidPortableKey =
            "nfp-1.0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        AssertRejected(
            $$"""
            {
              "producer_key": "",
              "portable_producer_key": "{{ValidPortableKey}}",
              "transport_kind": "NuGetV3",
              "producer_display": ""
            }
            """);
        AssertRejected(
            """
            {
              "producer_key": "producer",
              "portable_producer_key": "not-a-portable-key",
              "transport_kind": "NuGetV3",
              "producer_display": ""
            }
            """);
        AssertRejected(
            """
            {
              "producer_key": "producer",
              "portable_producer_key": "nfp-1.0000000000000000000000000000000000000000000000000000000000000000",
              "transport_kind": "NuGetV3",
              "producer_display": ""
            }
            """);
        AssertRejected(
            $$"""
            {
              "producer_key": "producer",
              "portable_producer_key": "{{ValidPortableKey}}",
              "transport_kind": 99,
              "producer_display": ""
            }
            """);

        static void AssertRejected(string json)
        {
            Exception? exception = Record.Exception(() =>
                JsonSerializer.Deserialize(
                    json,
                    DependencyInspectionJsonContext.Default
                        .DependencyInspectionPackageSourceIdentity));
            Assert.True(
                exception is JsonException or ArgumentException,
                $"Expected malformed source identity rejection; received {exception?.GetType().Name ?? "no exception"}.");
        }
    }

    private static T RoundTrip<T>(T value)
    {
        JsonTypeInfo<T> typeInfo = TypeInfo<T>();
        string json = JsonSerializer.Serialize(value, typeInfo);
        return JsonSerializer.Deserialize(json, typeInfo)!;
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
