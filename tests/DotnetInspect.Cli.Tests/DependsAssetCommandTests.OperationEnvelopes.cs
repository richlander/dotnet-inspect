using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

public partial class DependsAssetCommandTests
{
    [Fact]
    public async Task TypeEnvelopeRejectsRenderedLineSelectionBeforeAcquisition()
    {
        var result = await RunCapturedAsync(
        [
            "depends",
            "No.Such.Type",
            "--platform",
            "System.Private.CoreLib",
            "--envelope",
            "-n",
            "1",
            "--lines",
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            result.Error);
        Assert.DoesNotContain(
            "not found",
            result.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssetProjectionPreservesTheOperationEnvelope()
    {
        var summary = new DependencyInspectionSummary(
            DependencyInspectionRootSetCompletion.Complete,
            RequestedRoots: 1,
            AdmittedRoots: 1,
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
            PackagePrefix: null);
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Empty;
        var contentRoot = new DependencyInspectionRoot(
            new DependencyRootOccurrenceIdentity(1),
            DependencyInspectionRootKind.Library,
            new InertString(TextPolicy.Field, "example.dll"),
            DependencyInspectionRootState.Admitted,
            DependencyIdentity: null,
            DependencyInspectionTraversalCompletion.NotRequested,
            DependencyInspectionEvidenceAvailability.NotRequested,
            DependencyInspectionEvidencePhaseCompletion.NotRequested,
            DependencyInspectionSelectionStatus.NotRequested,
            DependencyInspectionEvidenceAvailability.NotRequested,
            DependencyInspectionEvidencePhaseCompletion.NotRequested);
        var content = new DependencyInspectionContent(
            summary,
            hierarchy,
            [contentRoot],
            [],
            [],
            []);
        var inspection = new InspectionEnvelope<DependencyInspectionContent>(
            new ResourcePath("asset-dependencies"),
            InspectionContentKind.Document,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
        var root = new DependsRootRow(
            contentRoot,
            source: "Path",
            identityKind: null,
            identity: null);

        var projection = new DependsAssetProjection(
            inspection,
            content.Summary,
            hierarchy.BackingGraph,
            hierarchy,
            HierarchyRows: [],
            [root],
            content.Dependencies,
            content.Pruning,
            RestoredEdges: [],
            content.Failures,
            DependencyGroups: [],
            RestoredPackages: [],
            Enriched: null);

        Assert.Same(inspection, projection.Inspection);
        Assert.Same(content, projection.Content);
        Assert.Same(summary, projection.Summary);
        Assert.Same(hierarchy, projection.Hierarchy);
        Assert.Same(hierarchy.BackingGraph, projection.Graph);
        Assert.Same(contentRoot, Assert.Single(projection.Content.Roots));
        Assert.Equal(
            new DependencyRootOccurrenceIdentity(1),
            Assert.Single(projection.Content.Roots).Identity);
        Assert.Null(projection.Evidence);
        Assert.Null(projection.Enriched);
        Assert.Empty(projection.Content.Dependencies);
        Assert.Empty(projection.Content.Pruning);
        Assert.Empty(projection.Content.Failures);
    }

    [Fact]
    public void AssetProjectionExcludesLivePackageAuthorityFromContent()
    {
        var runtimeFailure = new PackageAuthorityFailure(
            new InertString(TextPolicy.Field, "private"),
            PackageAuthorityFailureKind.Transport,
            "The source failed.");
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
                        "Example.Package@1.0.0")),
            ],
            [],
            [
                new DependencyGraphPackageProjection(
                    0,
                    0,
                    PackageDependencyTraversalProjectionKind
                        .CandidateAcquired,
                    PackageDependencyTraversalProjectionExpansion.Expanded,
                    Evidence: null,
                    Candidate: null,
                    RootOccurrence: null,
                    [
                        DependencyInspectionPackageAuthorityFailure.Create(
                            runtimeFailure),
                    ])
                {
                    RuntimeDiagnostics = [runtimeFailure],
                },
            ],
            []);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([], []));
        var request = new DependencyInspectionOperationRequest(
            new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: true,
                Pruning: false,
                RequestedFramework: null,
                RequestedDepth: null),
            requestedRoots: 0,
            isPrefixRootSet: false,
            outcome,
            admittedRootOccurrences: [],
            failedRootOccurrences: [],
            roots: [],
            graph,
            additionalFailures: null,
            pruning: null,
            pruningFailures: null,
            DependencyInspectionPruningSummary.NotRequested);
        DependencyInspectionContent content =
            DependencyInspectionOperation.Execute(request).Content;

        Assert.Single(graph.PackageProjections[0].RuntimeDiagnostics);
        Assert.Empty(
            content.Hierarchy.BackingGraph.PackageProjections[0]
                .RuntimeDiagnostics);
        Assert.Single(
            content.Hierarchy.BackingGraph.PackageProjections[0].Diagnostics);
    }

    [Fact]
    public void DependencyOperationDetachesLivePackageSourcesFromContent()
    {
        using IPackageSourceClient source =
            PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create());
        PackageDependencyEvidenceRootFailure.PackageProfile failure =
            PackageDependencyEvidenceQuery.CreatePackageProfileFailure(
                new PackageProfileFailure(
                    "Example.Bad",
                    "1.0.0",
                    source.Source,
                    PackageProfileFailureKind.SearchContract,
                    "Search failed"));
        var completion = new PackageDependencyEvidencePackagePrefixCompletion(
            new InertString(TextPolicy.Field, "Example."),
            failure.Source,
            candidates: 1,
            matches: 0,
            failures: 1,
            PackageSearchTruncationReason.None);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [],
                    [failure],
                    packagePrefixCompletion: completion));
        PackageDependencyEvidenceSourceIdentity liveSource =
            outcome.RootSet.PackagePrefixCompletion!.Source;
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("Example.Root", "1.0.0");
        var rootIdentity =
            new PackageDependencyEvidenceRootIdentity.Package(coordinate);
        var evidenceRoot = new PackageDependencyEvidenceRoot(
            rootIdentity,
            new PackageDependencyEvidenceRootProvenance.Package(
                PackageDependencyEvidenceAcquisitionForm.PackageSourceManifest,
                PackageManifestIdentityProvenance.ExpectedCoordinate,
                new InertString(TextPolicy.Field, "Example.Root"),
                liveSource),
            new InertString(TextPolicy.Field, "Example.Root@1.0.0"),
            new PackageDependencyEvidenceDeclarationResult.NotApplicable(),
            new PackageDependencyEvidenceSelection(
                PackageDependencyEvidenceSelectionStatus.NoDependencyGroups,
                SelectedGroup: null,
                SelectedSourceOccurrence: null,
                RequestedFramework: null,
                SelectedFramework: null),
            RestoredTarget: null,
            new PackageDependencyEvidenceRelationshipResult.NotApplicable(),
            new PackageDependencyEvidenceProcessingResult.NotApplicable());
        var groupIdentity = new PackageDependencyEvidenceGroupIdentity.Package(
            rootIdentity,
            IsImplicitManifestGroup: true,
            FirstSourceOccurrence: 0);
        var declarationIdentity =
            new PackageDependencyEvidenceDeclarationIdentity(
                groupIdentity,
                "example.dependency");
        var declaration = new PackageDependencyEvidenceDeclaration(
            declarationIdentity,
            "example.dependency",
            "[1.0.0]",
            new InertString(TextPolicy.Field, "Example.Dependency"),
            new InertString(TextPolicy.Field, "[1.0.0]"),
            SourceOccurrenceCount: 1,
            PackageDependencyEvidenceAuthorship.LibraryDeclared);
        var applicability = new PackageHouseDependencyPruningApplicability(
            evidenceRoot,
            declaration,
            PackageHouseDependencyPruningApplicabilityState.CandidateRequired,
            Processing: null,
            TargetUnavailableReason: null);
        var graph = new DependencyGraphDocument(
            [],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "Example.Root",
                        "1.0.0"),
                    new InertString(
                        TextPolicy.Field,
                        "Example.Root@1.0.0")),
            ],
            [],
            [
                new DependencyGraphPackageProjection(
                    0,
                    0,
                    PackageDependencyTraversalProjectionKind.RootSupplied,
                    PackageDependencyTraversalProjectionExpansion.Expanded,
                    evidenceRoot,
                    Candidate: null,
                    RootOccurrence: null,
                    Diagnostics: []),
            ],
            []);
        var pruning = new DependencyInspectionPruning(
            RootOccurrence: 1,
            rootIdentity,
            evidenceRoot.Display,
            declarationIdentity,
            new InertString(TextPolicy.Field, "net8.0"),
            new InertString(TextPolicy.Field, "net8.0"),
            "example.dependency",
            new InertString(TextPolicy.Field, "Example.Dependency"),
            "[1.0.0]",
            new InertString(TextPolicy.Field, "[1.0.0]"),
            CandidateVersion: null,
            PlatformFamily: null,
            PlatformTargetFramework: null,
            PlatformVersion: null,
            PlatformProvidedVersion: null,
            DependencyInspectionPruningDisposition.NotEvaluated,
            "Candidate required.",
            applicability,
            CandidateOutcome: null,
            Result: null);
        var request = new DependencyInspectionOperationRequest(
            new DependencyInspectionPlan(
                Declarations: false,
                RestoredRelationships: false,
                Traversal: true,
                Pruning: true,
                RequestedFramework: null,
                RequestedDepth: null),
            requestedRoots: 1,
            isPrefixRootSet: true,
            outcome,
            admittedRootOccurrences: [],
            failedRootOccurrences: [null],
            roots: [],
            graph,
            additionalFailures: null,
            pruning: [pruning],
            pruningFailures: null,
            new DependencyInspectionPruningSummary(
                DependencyInspectionPruningCompletion.Complete,
                Roots: 1,
                Declarations: 1,
                Evaluated: 0,
                Delegated: 0,
                Retained: 0,
                NotEvaluated: 1,
                SourceBounded: 0,
                Failed: 0));
        EvidenceInspectionEnvelope<
            DependencyInspectionContent,
            DependencyInspectionEvidenceDocument> enriched =
            DependencyInspectionOperation.ExecuteWithEvidence(request);
        DependencyInspectionContent content = enriched.Inspection.Content;

        Assert.True(
            enriched.Evidence.PackageInputs.RootSet
                .PackagePrefixCompletion!.Source
                .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(
            content.Summary.PackagePrefix!.Source
                .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(GraphContentSource(content.Hierarchy.BackingGraph)
            .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(
            Assert.IsType<DependencyInspectionFailure.Evidence>(
                    Assert.Single(content.Failures))
                .Value.Source!
                .MatchesRuntimeAssociation(source.Source.Association));
        Assert.False(
            RootContentSource(Assert.Single(content.Pruning)
                    .Applicability.Root)
                .MatchesRuntimeAssociation(source.Source.Association));

        static PackageDependencyEvidenceSourceIdentity GraphContentSource(
            DependencyGraphDocument graph) =>
            RootContentSource(
                Assert.Single(graph.PackageProjections).Evidence!);

        static PackageDependencyEvidenceSourceIdentity RootContentSource(
            PackageDependencyEvidenceRoot root) =>
            Assert.IsType<PackageDependencyEvidenceRootProvenance.Package>(
                    root.Provenance)
                .Source!;
    }

}
