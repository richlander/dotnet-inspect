using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class DependencyInspectionOperationTests
{
    [Fact]
    public void OrdinaryAndEnrichedExecutionSettleTheSameBaseline()
    {
        PackageDependencyEvidenceOutcome outcome = PackageOutcome(
            failedRoots:
            [
                AcquisitionFailure(),
            ]);
        DependencyInspectionOperationRequest request = Request(
            outcome,
            plan: new DependencyInspectionPlan(
                Declarations: true,
                RestoredRelationships: false,
                Traversal: false,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            roots:
            [
                LibraryRoot(1),
                PackageRoot(2),
                FailedPackageRoot(3),
            ],
            admittedAssociations:
            [
                new DependencyRootOccurrenceIdentity(2),
            ],
            failedAssociations:
            [
                new DependencyRootOccurrenceIdentity(3),
            ]);

        InspectionEnvelope<DependencyInspectionContent> ordinary =
            DependencyInspectionOperation.Execute(request);
        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument> enriched =
                DependencyInspectionOperation.ExecuteWithEvidence(request);

        Assert.Equal(ordinary, enriched.Inspection);
        Assert.Equal(ordinary.GetHashCode(), enriched.Inspection.GetHashCode());
        Assert.Same(outcome, enriched.Evidence.PackageInputs);
        Assert.Equal(
            [1, 2, 3],
            enriched.Inspection.Content.Roots.Select(
                static root => root.Identity.Value));
        Assert.Equal(
            "example.dependency",
            Assert.Single(enriched.Inspection.Content.Dependencies)
                .Declaration.PackageId);
        Assert.Single(enriched.Inspection.Content.Failures);
    }

    [Fact]
    public void BaselinePlanRetainsRootFailuresWithoutAddingEvidenceRows()
    {
        PackageDependencyEvidenceOutcome outcome = PackageOutcome(
            failedRoots:
            [
                AcquisitionFailure(),
            ]);
        DependencyInspectionOperationRequest request = Request(
            outcome,
            plan: new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: false,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            roots:
            [
                PackageRoot(1),
                FailedPackageRoot(2),
            ],
            admittedAssociations:
            [
                new DependencyRootOccurrenceIdentity(1),
            ],
            failedAssociations:
            [
                new DependencyRootOccurrenceIdentity(2),
            ]);

        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument> enriched =
                DependencyInspectionOperation.ExecuteWithEvidence(request);

        Assert.Empty(enriched.Inspection.Content.Dependencies);
        DependencyInspectionFailure.Evidence rootFailure = Assert.IsType<
            DependencyInspectionFailure.Evidence>(
                Assert.Single(enriched.Inspection.Content.Failures));
        Assert.Equal(
            DependencyEvidenceFailurePhase.Root,
            rootFailure.Value.Phase);
        Assert.All(
            enriched.Inspection.Content.Roots,
            static root =>
            {
                Assert.Equal(
                    DependencyInspectionEvidenceAvailability.NotRequested,
                    root.DeclarationState);
                Assert.Equal(
                    DependencyInspectionEvidencePhaseCompletion.NotRequested,
                    root.DeclarationCompletion);
            });
        Assert.Single(enriched.Evidence.PackageInputs.Roots);
        Assert.Single(enriched.Evidence.PackageInputs.FailedRoots);
    }

    [Fact]
    public void TraversalCompletionAggregatesAcrossAllRoots()
    {
        PackageDependencyEvidenceOutcome outcome = PackageOutcome();
        DependencyInspectionOperationRequest request = Request(
            outcome,
            plan: new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: true,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: 3),
            roots:
            [
                LibraryRoot(1) with
                {
                    Traversal =
                        DependencyInspectionTraversalCompletion.Complete,
                },
                PackageRoot(2) with
                {
                    Traversal =
                        DependencyInspectionTraversalCompletion.DepthBounded,
                },
            ],
            admittedAssociations:
            [
                new DependencyRootOccurrenceIdentity(2),
            ]);

        InspectionEnvelope<DependencyInspectionContent> inspection =
            DependencyInspectionOperation.Execute(request);

        Assert.Equal(
            DependencyInspectionTraversalCompletion.DepthBounded,
            inspection.Content.Summary.TraversalCompletion);
        Assert.Equal(3, inspection.Content.Summary.RequestedDepth);
    }

    [Fact]
    public void LibraryRootUsesACompleteEmptyPackageEvidenceValue()
    {
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([], []));
        DependencyInspectionOperationRequest request = Request(
            outcome,
            plan: new DependencyInspectionPlan(
                Declarations: true,
                RestoredRelationships: true,
                Traversal: false,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            roots:
            [
                LibraryRoot(1),
            ]);

        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument> enriched =
                DependencyInspectionOperation.ExecuteWithEvidence(request);

        DependencyInspectionRoot root =
            Assert.Single(enriched.Inspection.Content.Roots);
        Assert.Equal(
            DependencyInspectionEvidenceAvailability.NotApplicable,
            root.DeclarationState);
        Assert.Equal(
            DependencyInspectionEvidenceAvailability.NotApplicable,
            root.RestoredRelationshipState);
        Assert.Empty(enriched.Evidence.PackageInputs.Roots);
        Assert.Empty(enriched.Evidence.AdmittedRootOccurrences);
    }

    [Fact]
    public void FailedLibraryDoesNotDegradePackageEvidenceCompletion()
    {
        DependencyInspectionOperationRequest request = Request(
            PackageOutcome(),
            plan: new DependencyInspectionPlan(
                Declarations: true,
                RestoredRelationships: false,
                Traversal: false,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            roots:
            [
                FailedLibraryRoot(1),
                PackageRoot(2),
            ],
            admittedAssociations:
            [
                new DependencyRootOccurrenceIdentity(2),
            ]);

        DependencyInspectionContent content =
            DependencyInspectionOperation.Execute(request).Content;

        DependencyInspectionRoot failedLibrary = content.Roots[0];
        Assert.Equal(
            DependencyInspectionRootState.Failed,
            failedLibrary.State);
        Assert.Equal(
            DependencyInspectionEvidenceAvailability.NotApplicable,
            failedLibrary.DeclarationState);
        Assert.Equal(
            DependencyInspectionEvidencePhaseCompletion.NotApplicable,
            failedLibrary.DeclarationCompletion);
        Assert.Equal(
            DependencyInspectionSelectionStatus.NotApplicable,
            failedLibrary.Selection);
        Assert.Equal(
            DependencyInspectionEvidencePhaseCompletion.Complete,
            content.Summary.DeclarationCompletion);
        Assert.Single(content.Dependencies);
    }

    [Fact]
    public void AssociationValidationRejectsMissingDuplicateAndWrongStateRoots()
    {
        PackageDependencyEvidenceOutcome onePackage = PackageOutcome();
        ArgumentException missing = Assert.Throws<ArgumentException>(
            () => DependencyInspectionOperation.Execute(
                Request(
                    onePackage,
                    roots:
                    [
                        PackageRoot(1),
                        PackageRoot(2),
                    ],
                    admittedAssociations:
                    [
                        new DependencyRootOccurrenceIdentity(1),
                    ])));
        Assert.Equal("request", missing.ParamName);

        PackageDependencyEvidenceOutcome twoPackages =
            PackageOutcome(packageCount: 2);
        ArgumentException duplicate = Assert.Throws<ArgumentException>(
            () => DependencyInspectionOperation.Execute(
                Request(
                    twoPackages,
                    roots:
                    [
                        PackageRoot(1),
                        PackageRoot(2),
                    ],
                    admittedAssociations:
                    [
                        new DependencyRootOccurrenceIdentity(1),
                        new DependencyRootOccurrenceIdentity(1),
                    ])));
        Assert.Equal("request", duplicate.ParamName);

        ArgumentException wrongState = Assert.Throws<ArgumentException>(
            () => DependencyInspectionOperation.Execute(
                Request(
                    onePackage,
                    roots:
                    [
                        FailedPackageRoot(1),
                    ],
                    admittedAssociations:
                    [
                        new DependencyRootOccurrenceIdentity(1),
                    ])));
        Assert.Equal("request", wrongState.ParamName);

        ArgumentException library = Assert.Throws<ArgumentException>(
            () => DependencyInspectionOperation.Execute(
                Request(
                    onePackage,
                    roots:
                    [
                        LibraryRoot(1),
                    ],
                    admittedAssociations:
                    [
                        new DependencyRootOccurrenceIdentity(1),
                    ])));
        Assert.Equal("request", library.ParamName);
    }

    [Fact]
    public void PackagePrefixFailureMayRemainUnassociated()
    {
        using IPackageSourceClient source = PackageSourceClientFactory.Create(
            new PackageSource(
                "nuget.org",
                "https://api.nuget.org/v3/index.json"),
            PackageSourceAssociation.Create());
        var failure =
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
                    128));
        var prefix = new PackageDependencyEvidencePackagePrefixCompletion(
            new InertString(TextPolicy.Field, "Example"),
            source.Source,
            candidates: 1,
            matches: 0,
            failures: 1,
            PackageSearchTruncationReason.None);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [],
                    [failure],
                    packagePrefixCompletion: prefix));
        DependencyInspectionOperationRequest request = Request(
            outcome,
            plan: new DependencyInspectionPlan(
                Declarations: true,
                RestoredRelationships: false,
                Traversal: false,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            packagePrefix: true,
            roots: [],
            failedAssociations: [null]);

        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument> enriched =
                DependencyInspectionOperation.ExecuteWithEvidence(request);

        Assert.Null(Assert.Single(enriched.Evidence.FailedRootOccurrences));
        DependencyInspectionFailure.Evidence projected = Assert.IsType<
            DependencyInspectionFailure.Evidence>(
                Assert.Single(enriched.Inspection.Content.Failures));
        Assert.Null(projected.Value.RootIndex);
        Assert.Equal(
            DependencyInspectionRootSetCompletion.Failed,
            enriched.Inspection.Content.Summary.RootSetCompletion);
    }

    [Fact]
    public void NonPrefixFailureMustIdentifyAnExplicitRoot()
    {
        using IPackageSourceClient source = PackageSourceClientFactory.Create(
            new PackageSource(
                "nuget.org",
                "https://api.nuget.org/v3/index.json"),
            PackageSourceAssociation.Create());
        var failure =
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
                    128));
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([], [failure]));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => DependencyInspectionOperation.Execute(
                Request(
                    outcome,
                    requestedRoots: 1,
                    roots: [],
                    failedAssociations: [null])));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void PlanExcludesUnselectedTraversalAndPruningValues()
    {
        DependencyInspectionFailure traversalFailure = TraversalFailure();
        DependencyInspectionFailure pruningFailure = PruningFailure();
        var graph = new DependencyGraphDocument(
            [],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "Example.Package",
                        "1.0.0"),
                    new InertString(
                        TextPolicy.Field,
                        "Example.Package")),
            ],
            [],
            [],
            []);
        ImmutableArray<DependencyInspectionRootInput> roots =
        [
            PackageRoot(1) with
            {
                Traversal =
                    DependencyInspectionTraversalCompletion.Complete,
            },
        ];
        ImmutableArray<DependencyRootOccurrenceIdentity> associations =
        [
            new DependencyRootOccurrenceIdentity(1),
        ];
        var selectedPruningSummary = new DependencyInspectionPruningSummary(
            DependencyInspectionPruningCompletion.Failed,
            Roots: 1,
            Declarations: 1,
            Evaluated: 0,
            Delegated: 0,
            Retained: 0,
            NotEvaluated: 1,
            SourceBounded: 0,
            Failed: 1);
        DependencyInspectionOperationRequest request = Request(
            PackageOutcome(),
            roots: roots,
            admittedAssociations: associations,
            graph: graph,
            additionalFailures: [traversalFailure],
            pruningFailures: [pruningFailure],
            pruningSummary: selectedPruningSummary);

        InspectionEnvelope<DependencyInspectionContent> inspection =
            DependencyInspectionOperation.Execute(request);

        Assert.Empty(inspection.Content.Graph.Nodes);
        Assert.Equal(
            DependencyInspectionTraversalCompletion.NotRequested,
            Assert.Single(inspection.Content.Roots).Traversal);
        Assert.Empty(inspection.Content.Failures);
        Assert.Equal(
            DependencyInspectionPruningSummary.NotRequested,
            inspection.Content.Summary.Pruning);

        InspectionEnvelope<DependencyInspectionContent> selected =
            DependencyInspectionOperation.Execute(
                Request(
                    PackageOutcome(),
                    plan: new DependencyInspectionPlan(
                        Declarations: false,
                        RestoredRelationships: false,
                        Traversal: true,
                        Pruning: true,
                        RequestedFramework: null,
                        RequestedDepth: 1),
                    roots: roots,
                    admittedAssociations: associations,
                    graph: graph,
                    additionalFailures: [traversalFailure],
                    pruningFailures: [pruningFailure],
                    pruningSummary: selectedPruningSummary));

        Assert.Single(selected.Content.Graph.Nodes);
        Assert.Equal(
            DependencyInspectionTraversalCompletion.Complete,
            Assert.Single(selected.Content.Roots).Traversal);
        Assert.Equal(2, selected.Content.Failures.Length);
        Assert.Equal(
            selectedPruningSummary,
            selected.Content.Summary.Pruning);
    }

    [Theory]
    [InlineData(
        0,
        0,
        PackageSearchTruncationReason.RequestedLimit,
        true)]
    [InlineData(
        1,
        0,
        PackageSearchTruncationReason.RequestedLimit,
        false)]
    [InlineData(
        0,
        1,
        PackageSearchTruncationReason.RequestedLimit,
        false)]
    [InlineData(
        0,
        0,
        PackageSearchTruncationReason.SourcePageLimit,
        false)]
    [InlineData(
        0,
        0,
        PackageSearchTruncationReason.ClientPageLimit,
        false)]
    public void PackagePrefixRootSetCompletionOnlyAcceptsRequestedLimit(
        int rejectedRootCount,
        int failedRootCount,
        PackageSearchTruncationReason truncationReason,
        bool expected)
    {
        using IPackageSourceClient source = PackageSourceClientFactory.Create(
            new PackageSource(
                "nuget.org",
                "https://api.nuget.org/v3/index.json"),
            PackageSourceAssociation.Create());
        ImmutableArray<PackageDependencyEvidenceRootFailure> failures =
            failedRootCount == 0
                ? []
                :
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
                ];
        var prefix = new PackageDependencyEvidencePackagePrefixCompletion(
            new InertString(TextPolicy.Field, "Example"),
            source.Source,
            candidates: failedRootCount,
            matches: 0,
            failures: failedRootCount,
            truncationReason: truncationReason);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [],
                    failures,
                    rejectedRootCount,
                    isTruncated: true,
                    packagePrefixCompletion: prefix));
        DependencyInspectionOperationRequest request = Request(
            outcome,
            packagePrefix: true,
            roots: [],
            failedAssociations:
                failedRootCount == 0 ? [] : [null]);

        Assert.Equal(
            expected
                ? DependencyInspectionRootSetCompletion.Complete
                : DependencyInspectionRootSetCompletion.Failed,
            DependencyInspectionOperation.Execute(request)
                .Content.Summary.RootSetCompletion);
    }

    private static DependencyInspectionOperationRequest Request(
        PackageDependencyEvidenceOutcome outcome,
        DependencyInspectionPlan? plan = null,
        bool packagePrefix = false,
        int? requestedRoots = null,
        ImmutableArray<DependencyInspectionRootInput> roots = default,
        ImmutableArray<DependencyRootOccurrenceIdentity>
            admittedAssociations = default,
        ImmutableArray<DependencyRootOccurrenceIdentity?>
            failedAssociations = default,
        DependencyGraphDocument? graph = null,
        ImmutableArray<DependencyInspectionFailure>
            additionalFailures = default,
        ImmutableArray<DependencyInspectionFailure>
            pruningFailures = default,
        DependencyInspectionPruningSummary? pruningSummary = null) =>
        new(
            plan ?? new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: false,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            requestedRoots
                ?? (packagePrefix
                    ? outcome.RootSet.AdmittedRootCount
                        + outcome.RootSet.FailedRootCount
                        + outcome.RootSet.RejectedRootCount
                    : roots.IsDefault
                        ? 0
                        : roots.Length),
            packagePrefix,
            outcome,
            admittedAssociations.IsDefault ? [] : admittedAssociations,
            failedAssociations.IsDefault ? [] : failedAssociations,
            roots.IsDefault ? [] : roots,
            graph ?? new DependencyGraphDocument([], [], [], [], []),
            additionalFailures:
                additionalFailures.IsDefault ? [] : additionalFailures,
            pruning: [],
            pruningFailures:
                pruningFailures.IsDefault ? [] : pruningFailures,
            pruningSummary:
                pruningSummary
                ?? DependencyInspectionPruningSummary.NotRequested);

    private static DependencyInspectionFailure TraversalFailure() =>
        new DependencyInspectionFailure.Traversal(
            new DependencyInspectionTraversalFailure(
                "Traversal failed",
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
                AffectedRootOccurrences: [1]));

    private static DependencyInspectionFailure PruningFailure() =>
        new DependencyInspectionFailure.Pruning(
            new DependencyInspectionPruningFailure.Inventory(
                "runtime",
                "net11.0",
                new InertString(
                    TextPolicy.Prose,
                    "Pruning inventory unavailable."),
                [1],
                AffectedDeclarations: 1));

    private static DependencyInspectionRootInput LibraryRoot(int occurrence) =>
        new(
            new DependencyRootOccurrenceIdentity(occurrence),
            DependencyInspectionRootKind.Library,
            new InertString(TextPolicy.Field, "example.dll"),
            DependencyInspectionRootState.Admitted,
            new DependencyGraphNodeIdentity.Library(
                new ManagedMetadataIdentity.Module(
                    "example.dll",
                    Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))),
            DependencyInspectionTraversalCompletion.NotRequested);

    private static DependencyInspectionRootInput FailedLibraryRoot(
        int occurrence) =>
        new(
            new DependencyRootOccurrenceIdentity(occurrence),
            DependencyInspectionRootKind.Library,
            new InertString(TextPolicy.Field, "missing.dll"),
            DependencyInspectionRootState.Failed,
            GraphIdentity: null,
            DependencyInspectionTraversalCompletion.NotRequested);

    private static DependencyInspectionRootInput PackageRoot(int occurrence) =>
        new(
            new DependencyRootOccurrenceIdentity(occurrence),
            DependencyInspectionRootKind.Package,
            new InertString(TextPolicy.Field, "Example.Package"),
            DependencyInspectionRootState.Admitted,
            new DependencyGraphNodeIdentity.Package(
                "Example.Package",
                "1.0.0"),
            DependencyInspectionTraversalCompletion.NotRequested);

    private static DependencyInspectionRootInput FailedPackageRoot(
        int occurrence) =>
        new(
            new DependencyRootOccurrenceIdentity(occurrence),
            DependencyInspectionRootKind.Package,
            new InertString(TextPolicy.Field, "Missing.Package"),
            DependencyInspectionRootState.Failed,
            GraphIdentity: null,
            DependencyInspectionTraversalCompletion.Failed);

    private static PackageDependencyEvidenceOutcome PackageOutcome(
        int packageCount = 1,
        ImmutableArray<PackageDependencyEvidenceRootFailure> failedRoots =
            default) =>
        PackageDependencyEvidenceQuery.Execute(
            new PackageDependencyEvidenceRequest(
                [
                    .. Enumerable.Range(0, packageCount).Select(index =>
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            PackageFacts(
                                packageCount == 1
                                    ? "Example.Package"
                                    : $"Example.Package.{index}"),
                            PackageDependencyEvidenceAcquisitionForm
                                .DirectNuspec)),
                ],
                failedRoots.IsDefault ? [] : failedRoots));

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
            [
                new DeclaredPackageDependencyGroup(
                    "",
                    [
                        new DeclaredPackageDependency(
                            "Example.Dependency",
                            "[2.0.0]"),
                    ],
                    IsImplicitManifestGroup: true),
            ])
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
