using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotnetInspector.PackageQueries;
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
