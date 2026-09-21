using System.Collections.Immutable;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// The baseline phases selected for one asset dependency inspection.
/// </summary>
public sealed record DependencyInspectionPlan(
    bool Declarations,
    bool RestoredRelationships,
    bool Traversal,
    bool Pruning,
    InertString? RequestedFramework,
    int? RequestedDepth)
{
    public bool Licenses { get; init; }
}

/// <summary>
/// One acquired explicit root before dependency evidence is joined.
/// </summary>
public sealed record DependencyInspectionRootInput(
    DependencyRootOccurrenceIdentity Identity,
    DependencyInspectionRootKind Kind,
    InertString Input,
    DependencyInspectionRootState State,
    DependencyGraphNodeIdentity? DependencyIdentity,
    DependencyInspectionTraversalCompletion Traversal);

/// <summary>
/// Settled producer values for one asset dependency inspection.
/// </summary>
public sealed record DependencyInspectionOperationRequest
{
    public DependencyInspectionOperationRequest(
        DependencyInspectionPlan plan,
        int requestedRoots,
        bool isPrefixRootSet,
        PackageDependencyEvidenceOutcome packageInputs,
        IEnumerable<DependencyRootOccurrenceIdentity>
            admittedRootOccurrences,
        IEnumerable<DependencyRootOccurrenceIdentity?>
            failedRootOccurrences,
        IEnumerable<DependencyInspectionRootInput> roots,
        DependencyGraphDocument graph,
        IEnumerable<DependencyInspectionFailure>? additionalFailures,
        IEnumerable<DependencyInspectionPruning>? pruning,
        IEnumerable<DependencyInspectionFailure>? pruningFailures,
        DependencyInspectionPruningSummary pruningSummary,
        InspectionPortableProjection? portableProjection = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedRoots);
        ArgumentNullException.ThrowIfNull(packageInputs);
        ArgumentNullException.ThrowIfNull(admittedRootOccurrences);
        ArgumentNullException.ThrowIfNull(failedRootOccurrences);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(pruningSummary);

        Plan = plan;
        RequestedRoots = requestedRoots;
        IsPrefixRootSet = isPrefixRootSet;
        PackageInputs = packageInputs;
        AdmittedRootOccurrences =
            admittedRootOccurrences.ToImmutableArray();
        FailedRootOccurrences = failedRootOccurrences.ToImmutableArray();
        Roots = roots.ToImmutableArray();
        Graph = graph;
        AdditionalFailures =
            (additionalFailures ?? []).ToImmutableArray();
        Pruning = (pruning ?? []).ToImmutableArray();
        PruningFailures = (pruningFailures ?? []).ToImmutableArray();
        PruningSummary = pruningSummary;
        PortableProjection = portableProjection
            ?? new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported);
    }

    public DependencyInspectionPlan Plan { get; }

    public int RequestedRoots { get; }

    public bool IsPrefixRootSet { get; }

    public PackageDependencyEvidenceOutcome PackageInputs { get; }

    public ImmutableArray<DependencyRootOccurrenceIdentity>
        AdmittedRootOccurrences { get; }

    public ImmutableArray<DependencyRootOccurrenceIdentity?>
        FailedRootOccurrences { get; }

    public ImmutableArray<DependencyInspectionRootInput> Roots { get; }

    public DependencyGraphDocument Graph { get; }

    public ImmutableArray<DependencyInspectionFailure> AdditionalFailures
    { get; }

    public ImmutableArray<DependencyInspectionPruning> Pruning { get; }

    public ImmutableArray<DependencyInspectionFailure> PruningFailures
    { get; }

    public DependencyInspectionPruningSummary PruningSummary { get; }

    public ImmutableArray<DependencyInspectionLicense> Licenses { get; init; } =
        [];

    public DependencyInspectionLicenseSummary LicenseSummary { get; init; } =
        DependencyInspectionLicenseSummary.NotRequested;

    public InspectionPortableProjection PortableProjection { get; }
}

/// <summary>
/// Settles one asset dependency inspection into its host-neutral envelope.
/// </summary>
public static class DependencyInspectionOperation
{
    public static InspectionEnvelope<DependencyInspectionContent> Execute(
        DependencyInspectionOperationRequest request) =>
        ExecuteCore(request).Inspection;

    public static EvidenceInspectionEnvelope<
        DependencyInspectionContent,
        DependencyInspectionEvidenceDocument> ExecuteWithEvidence(
            DependencyInspectionOperationRequest request)
    {
        (
            InspectionEnvelope<DependencyInspectionContent> inspection,
            DependencyInspectionEvidenceDocument evidence) =
            ExecuteCore(request);
        return new(inspection, evidence);
    }

    private static (
        InspectionEnvelope<DependencyInspectionContent> Inspection,
        DependencyInspectionEvidenceDocument Evidence) ExecuteCore(
            DependencyInspectionOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var evidenceDocument = new DependencyInspectionEvidenceDocument(
            request.PackageInputs,
            request.AdmittedRootOccurrences,
            request.FailedRootOccurrences);
        ValidateAssociations(request, evidenceDocument);

        DependencyEvidenceProjection evidence =
            DependencyEvidenceProjection.Create(evidenceDocument);
        ImmutableArray<DependencyInspectionRoot> roots =
            ProjectRoots(request, evidence, evidenceDocument);
        DependencyGraphDocument graph = request.Plan.Traversal
            ? DetachGraph(request.Graph)
            : new DependencyGraphDocument([], [], [], [], []);
        DependencyHierarchyDocument hierarchy = request.Plan.Traversal
            ? DependencyHierarchyDocument.Create(graph)
            : DependencyHierarchyDocument.Empty;
        if (request.Plan.Traversal)
            ValidateHierarchyRoots(roots, hierarchy);
        ImmutableArray<DependencyInspectionFailure> failures =
        [
            .. ProjectFailures(request, evidence)
                .Select(DetachFailure),
        ];
        var content = new DependencyInspectionContent(
            DetachSummary(ProjectSummary(
                request,
                evidence,
                roots,
                hierarchy)),
            hierarchy,
            roots,
            request.Plan.Declarations
                ? ProjectDependencies(evidence)
                : [],
            request.Plan.Pruning
                ? [.. request.Pruning.Select(DetachPruning)]
                : [],
            failures)
        {
            Licenses = request.Plan.Licenses
                ? request.Licenses
                : [],
        };
        var inspection = new InspectionEnvelope<DependencyInspectionContent>(
            new ResourcePath("asset-dependencies"),
            InspectionContentKind.Document,
            content,
            request.PortableProjection);
        return (inspection, evidenceDocument);
    }

    private static void ValidateHierarchyRoots(
        ImmutableArray<DependencyInspectionRoot> roots,
        DependencyHierarchyDocument hierarchy)
    {
        DependencyInspectionRoot[] expected =
        [
            .. roots
                .Where(static root =>
                    root.State == DependencyInspectionRootState.Admitted),
        ];
        if (expected.Length != hierarchy.Roots.Length)
        {
            throw new InvalidOperationException(
                "Dependency hierarchy roots do not match the admitted explicit roots.");
        }

        for (int index = 0; index < expected.Length; index++)
        {
            DependencyInspectionRoot root = expected[index];
            DependencyHierarchyRootOccurrence hierarchyRoot =
                hierarchy.Roots[index];
            DependencyGraphNodeIdentity graphIdentity =
                hierarchy.BackingGraph.Nodes[hierarchyRoot.NodeId].Identity;
            if (root.Identity != hierarchyRoot.RootOccurrence
                || root.DependencyIdentity != graphIdentity)
            {
                throw new InvalidOperationException(
                    "Dependency hierarchy roots do not match the admitted explicit roots.");
            }
        }
    }

    private static DependencyInspectionSummary DetachSummary(
        DependencyInspectionSummary summary) =>
        summary.PackagePrefix is { } prefix
            ? summary with
            {
                PackagePrefix =
                    new PackageDependencyEvidencePackagePrefixCompletion(
                        prefix.Prefix,
                        prefix.Source.WithoutRuntimeAssociation(),
                        prefix.Candidates,
                        prefix.Matches,
                        prefix.Failures,
                        prefix.TruncationReason),
            }
            : summary;

    private static DependencyGraphDocument DetachGraph(
        DependencyGraphDocument graph)
    {
        if (!graph.PackageProjections.Any(static projection =>
                projection.RuntimeCandidate is not null
                || !projection.RuntimeDiagnostics.IsEmpty
                || projection.Evidence is
                {
                    Provenance:
                        PackageDependencyEvidenceRootProvenance.Package
                        {
                            Source: not null,
                        },
                })
            && !graph.Edges.Any(static edge =>
                !edge.RuntimePackageDiagnostics.IsEmpty))
        {
            return graph;
        }

        return graph with
        {
            PackageProjections =
            [
                .. graph.PackageProjections.Select(static projection =>
                    projection with
                    {
                        Evidence = projection.Evidence is { } evidence
                            ? DetachRoot(evidence)
                            : null,
                        RuntimeCandidate = null,
                        RuntimeDiagnostics = [],
                    }),
            ],
            Edges =
            [
                .. graph.Edges.Select(static edge =>
                    edge with { RuntimePackageDiagnostics = [] }),
            ],
        };
    }

    private static DependencyInspectionPruning DetachPruning(
        DependencyInspectionPruning pruning) =>
        pruning with
        {
            Applicability = pruning.Applicability with
            {
                Root = DetachRoot(pruning.Applicability.Root),
            },
            RuntimeCandidateOutcome = null,
            RuntimeResult = null,
        };

    private static DependencyInspectionFailure DetachFailure(
        DependencyInspectionFailure failure) =>
        failure switch
        {
            DependencyInspectionFailure.Evidence evidence =>
                new DependencyInspectionFailure.Evidence(
                    evidence.Value with
                    {
                        Source = evidence.Value.Source?
                            .WithoutRuntimeAssociation(),
                    }),
            DependencyInspectionFailure.Traversal traversal =>
                new DependencyInspectionFailure.Traversal(
                    traversal.Value with
                    {
                        RuntimeCandidateOutcome = null,
                        RuntimeManifestFailure = null,
                    }),
            DependencyInspectionFailure.Pruning
            {
                Value: DependencyInspectionPruningFailure.Candidate candidate,
            } => new DependencyInspectionFailure.Pruning(
                candidate with { RuntimeOutcome = null }),
            _ => failure,
        };

    private static PackageDependencyEvidenceRoot DetachRoot(
        PackageDependencyEvidenceRoot root)
    {
        if (root.Provenance is not
            PackageDependencyEvidenceRootProvenance.Package
            {
                Source: { } source,
            } package)
        {
            return root;
        }

        return new PackageDependencyEvidenceRoot(
            root.Identity,
            package with
            {
                Source = source.WithoutRuntimeAssociation(),
            },
            root.Display,
            root.Declaration,
            root.Selection,
            root.RestoredTarget,
            root.Relationships,
            root.Processing,
            root.RuntimeTarget);
    }

    private static void ValidateAssociations(
        DependencyInspectionOperationRequest request,
        DependencyInspectionEvidenceDocument evidence)
    {
        bool hasPackagePrefix =
            request.PackageInputs.RootSet.PackagePrefixCompletion is not null;
        if (request.IsPrefixRootSet != hasPackagePrefix)
        {
            throw new ArgumentException(
                "Package-prefix root-set identity must agree with package evidence completion.",
                nameof(request));
        }

        var rootsByOccurrence =
            new Dictionary<
                DependencyRootOccurrenceIdentity,
                DependencyInspectionRootInput>();
        foreach (DependencyInspectionRootInput root in request.Roots)
        {
            if (!rootsByOccurrence.TryAdd(root.Identity, root))
            {
                throw new ArgumentException(
                    "Dependency root occurrences must be unique.",
                    nameof(request));
            }
        }

        var admittedAssociations =
            new HashSet<DependencyRootOccurrenceIdentity>();
        foreach (DependencyRootOccurrenceIdentity occurrence in
                 evidence.AdmittedRootOccurrences)
        {
            if (!admittedAssociations.Add(occurrence)
                || !rootsByOccurrence.TryGetValue(
                    occurrence,
                    out DependencyInspectionRootInput? root)
                || root.State != DependencyInspectionRootState.Admitted
                || root.Kind == DependencyInspectionRootKind.Library)
            {
                throw new ArgumentException(
                    "Each admitted package input must identify one admitted non-library root.",
                    nameof(request));
            }
        }

        var failedAssociations =
            new HashSet<DependencyRootOccurrenceIdentity>();
        foreach (DependencyRootOccurrenceIdentity? occurrence in
                 evidence.FailedRootOccurrences)
        {
            if (occurrence is not { } value)
            {
                if (!request.IsPrefixRootSet)
                {
                    throw new ArgumentException(
                        "Only package-prefix producer failures may omit a root occurrence.",
                        nameof(request));
                }
                continue;
            }
            if (!failedAssociations.Add(value)
                || !rootsByOccurrence.TryGetValue(
                    value,
                    out DependencyInspectionRootInput? root)
                || root.State != DependencyInspectionRootState.Failed
                || root.Kind == DependencyInspectionRootKind.Library)
            {
                throw new ArgumentException(
                    "Each explicit failed package input must identify one failed non-library root.",
                    nameof(request));
            }
        }

        foreach (DependencyInspectionRootInput root in request.Roots)
        {
            if (root.Kind == DependencyInspectionRootKind.Library)
                continue;
            bool associated = root.State switch
            {
                DependencyInspectionRootState.Admitted =>
                    admittedAssociations.Contains(root.Identity),
                DependencyInspectionRootState.Failed =>
                    failedAssociations.Contains(root.Identity),
                _ => false,
            };
            if (!associated)
            {
                throw new ArgumentException(
                    "Each explicit non-library root requires one package-input association.",
                    nameof(request));
            }
        }
    }

    private static ImmutableArray<DependencyInspectionRoot> ProjectRoots(
        DependencyInspectionOperationRequest request,
        DependencyEvidenceProjection evidence,
        DependencyInspectionEvidenceDocument evidenceDocument)
    {
        Dictionary<int, DependencyEvidenceRootRow> evidenceRows =
            evidence.Roots.ToDictionary(static root => root.RootIndex);
        Dictionary<DependencyRootOccurrenceIdentity,
            PackageDependencyEvidenceRoot> packageRoots =
            evidenceDocument.AdmittedRootOccurrences
                .Select((occurrence, index) => (
                    occurrence,
                    root: evidenceDocument.PackageInputs.Roots[index]))
                .ToDictionary(static pair => pair.occurrence, static pair =>
                    pair.root);
        var roots =
            ImmutableArray.CreateBuilder<DependencyInspectionRoot>(
                request.Roots.Length);
        bool selectionRequested =
            request.Plan.Declarations || request.Plan.Traversal;

        foreach (DependencyInspectionRootInput input in
                 request.Roots.OrderBy(static root => root.Identity.Value))
        {
            if (input.Kind == DependencyInspectionRootKind.Library)
            {
                roots.Add(
                    new DependencyInspectionRoot(
                        input.Identity,
                        input.Kind,
                        input.Input,
                        input.State,
                        request.Plan.Traversal
                            ? input.DependencyIdentity
                            : null,
                        request.Plan.Traversal
                            ? input.Traversal
                            : DependencyInspectionTraversalCompletion
                                .NotRequested,
                        request.Plan.Declarations
                            ? DependencyInspectionEvidenceAvailability
                                .NotApplicable
                            : DependencyInspectionEvidenceAvailability
                                .NotRequested,
                        request.Plan.Declarations
                            ? DependencyInspectionEvidencePhaseCompletion
                                .NotApplicable
                            : DependencyInspectionEvidencePhaseCompletion
                                .NotRequested,
                        selectionRequested
                            ? DependencyInspectionSelectionStatus.NotApplicable
                            : DependencyInspectionSelectionStatus.NotRequested,
                        request.Plan.RestoredRelationships
                            ? DependencyInspectionEvidenceAvailability
                                .NotApplicable
                            : DependencyInspectionEvidenceAvailability
                                .NotRequested,
                        request.Plan.RestoredRelationships
                            ? DependencyInspectionEvidencePhaseCompletion
                                .NotApplicable
                            : DependencyInspectionEvidencePhaseCompletion
                                .NotRequested));
                continue;
            }

            if (input.State == DependencyInspectionRootState.Failed)
            {
                roots.Add(
                    new DependencyInspectionRoot(
                        input.Identity,
                        input.Kind,
                        input.Input,
                        input.State,
                        DependencyIdentity: null,
                        request.Plan.Traversal
                            ? input.Traversal
                            : DependencyInspectionTraversalCompletion
                                .NotRequested,
                        request.Plan.Declarations
                            ? DependencyInspectionEvidenceAvailability.Failed
                            : DependencyInspectionEvidenceAvailability
                                .NotRequested,
                        request.Plan.Declarations
                            ? DependencyInspectionEvidencePhaseCompletion.Failed
                            : DependencyInspectionEvidencePhaseCompletion
                                .NotRequested,
                        selectionRequested
                            ? DependencyInspectionSelectionStatus.Unavailable
                            : DependencyInspectionSelectionStatus.NotRequested,
                        request.Plan.RestoredRelationships
                            ? DependencyInspectionEvidenceAvailability.Failed
                            : DependencyInspectionEvidenceAvailability
                                .NotRequested,
                        request.Plan.RestoredRelationships
                            ? DependencyInspectionEvidencePhaseCompletion.Failed
                            : DependencyInspectionEvidencePhaseCompletion
                                .NotRequested));
                continue;
            }

            if (!evidenceRows.TryGetValue(
                    input.Identity.Value,
                    out DependencyEvidenceRootRow? evidenceRoot)
                || !packageRoots.TryGetValue(
                    input.Identity,
                    out PackageDependencyEvidenceRoot? packageRoot))
            {
                throw new InvalidOperationException(
                    "An admitted dependency root did not retain its package evidence.");
            }

            (
                DependencyInspectionSelectionStatus status,
                InertString? requestedFramework,
                InertString? selectedFramework) =
                ProjectSelection(
                    evidenceRoot,
                    evidence,
                    request.Plan.RequestedFramework,
                    selectionRequested);
            roots.Add(
                new DependencyInspectionRoot(
                    input.Identity,
                    input.Kind,
                    input.Input,
                    input.State,
                    request.Plan.Traversal
                        ? input.DependencyIdentity
                            ?? DependencyIdentity(packageRoot)
                        : null,
                    request.Plan.Traversal
                        ? input.Traversal
                        : DependencyInspectionTraversalCompletion.NotRequested,
                    request.Plan.Declarations
                        ? DeclarationState(evidenceRoot)
                        : DependencyInspectionEvidenceAvailability.NotRequested,
                    request.Plan.Declarations
                        ? DeclarationCompletion(evidenceRoot)
                        : DependencyInspectionEvidencePhaseCompletion
                            .NotRequested,
                    status,
                    request.Plan.RestoredRelationships
                        ? RelationshipState(evidenceRoot)
                        : DependencyInspectionEvidenceAvailability.NotRequested,
                    request.Plan.RestoredRelationships
                        ? RelationshipCompletion(evidenceRoot)
                        : DependencyInspectionEvidencePhaseCompletion
                            .NotRequested)
                {
                    RequestedFramework = requestedFramework,
                    SelectedFramework = selectedFramework,
                });
        }

        return roots.ToImmutable();
    }

    private static ImmutableArray<DependencyInspectionFailure> ProjectFailures(
        DependencyInspectionOperationRequest request,
        DependencyEvidenceProjection evidence) =>
    [
        .. evidence.Failures
            .Where(failure =>
                IsSelectedFailurePhase(failure.Phase, request.Plan))
            .Select(static failure =>
                new DependencyInspectionFailure.Evidence(failure)),
        .. request.AdditionalFailures
            .Concat(request.PruningFailures)
            .Where(failure => IsSelectedFailure(failure, request.Plan)),
    ];

    private static bool IsSelectedFailure(
        DependencyInspectionFailure failure,
        DependencyInspectionPlan plan) =>
        failure switch
        {
            DependencyInspectionFailure.Evidence evidence =>
                IsSelectedFailurePhase(evidence.Value.Phase, plan),
            DependencyInspectionFailure.Traversal =>
                plan.Traversal || plan.Licenses,
            DependencyInspectionFailure.Pruning => plan.Pruning,
            _ => false,
        };

    private static bool IsSelectedFailurePhase(
        DependencyEvidenceFailurePhase phase,
        DependencyInspectionPlan plan) =>
        phase switch
        {
            DependencyEvidenceFailurePhase.Root
                or DependencyEvidenceFailurePhase.PackageProfile
                or DependencyEvidenceFailurePhase.Library => true,
            DependencyEvidenceFailurePhase.Declaration => plan.Declarations,
            DependencyEvidenceFailurePhase.Graph =>
                plan.RestoredRelationships,
            DependencyEvidenceFailurePhase.Traversal =>
                plan.Traversal || plan.Licenses,
            DependencyEvidenceFailurePhase.License => plan.Licenses,
            DependencyEvidenceFailurePhase.Pruning => plan.Pruning,
            _ => false,
        };

    private static ImmutableArray<DependencyInspectionDependency>
        ProjectDependencies(DependencyEvidenceProjection evidence)
    {
        var restoredByDeclaration = evidence.RestoredEdges
            .Where(static edge => edge.DeclarationAssociation is not null)
            .ToDictionary(
                static edge => (
                    edge.RootIndex,
                    edge.RootIdentity,
                    edge.DeclarationAssociation!.Value),
                static edge => edge);
        return
        [
            .. evidence.Dependencies.Select(declaration =>
            {
                DependencyEvidenceRestoredEdgeRow? restored =
                    restoredByDeclaration.TryGetValue(
                        (
                            declaration.RootIndex,
                            declaration.RootIdentity,
                            declaration.DeclarationIdentity),
                        out DependencyEvidenceRestoredEdgeRow? associated)
                    && associated.Dependency.Coordinate.PackageId.Equals(
                        declaration.PackageId,
                        StringComparison.Ordinal)
                        ? associated
                        : null;
                return new DependencyInspectionDependency(
                    declaration,
                    restored?.PackageVersion,
                    restored?.Dependency,
                    restored?.Identity);
            }),
        ];
    }

    private static DependencyInspectionSummary ProjectSummary(
        DependencyInspectionOperationRequest request,
        DependencyEvidenceProjection evidence,
        ImmutableArray<DependencyInspectionRoot> roots,
        DependencyHierarchyDocument hierarchy)
    {
        int admitted = roots.Count(
            static root => root.State == DependencyInspectionRootState.Admitted);
        int failed = request.IsPrefixRootSet
            ? evidence.Summary.FailedRootCount
                + evidence.Summary.RejectedRootCount
            : roots.Length - admitted;
        DependencyInspectionRootSetCompletion rootSet =
            failed == 0
            && IsCompleteRootSet(
                evidence.Summary.RootSetCompletion,
                evidence.Summary.RejectedRootCount,
                evidence.Summary.FailedRootCount,
                evidence.Summary.IsTruncated,
                evidence.Summary.PackagePrefix?.TruncationReason)
                ? DependencyInspectionRootSetCompletion.Complete
                : admitted == 0
                    ? DependencyInspectionRootSetCompletion.Failed
                    : DependencyInspectionRootSetCompletion.Partial;
        return new DependencyInspectionSummary(
            rootSet,
            request.RequestedRoots,
            admitted,
            failed,
            request.Plan.Traversal
                ? AggregateTraversal(roots)
                : DependencyInspectionTraversalCompletion.NotRequested,
            request.Plan.Traversal
                ? request.Plan.RequestedDepth
                : null,
            hierarchy.Occurrences.Length,
            hierarchy.BackingGraph.Nodes.Length,
            hierarchy.BackingGraph.Edges.Length,
            AggregateEvidencePhase(
                roots,
                static root => root.DeclarationCompletion,
                request.Plan.Declarations),
            AggregateEvidencePhase(
                roots,
                static root => root.RestoredRelationshipCompletion,
                request.Plan.RestoredRelationships),
            request.Plan.Pruning
                ? request.PruningSummary
                : DependencyInspectionPruningSummary.NotRequested,
            request.IsPrefixRootSet,
            evidence.Summary.PackagePrefix)
        {
            Licenses = request.Plan.Licenses
                ? request.LicenseSummary
                : DependencyInspectionLicenseSummary.NotRequested,
        };
    }

    private static bool IsCompleteRootSet(
        PackageDependencyEvidenceRootSetCompletion completion,
        int rejectedRootCount,
        int failedRootCount,
        bool isTruncated,
        PackageSearchTruncationReason? truncationReason) =>
        completion == PackageDependencyEvidenceRootSetCompletion.Complete
        || rejectedRootCount == 0
        && failedRootCount == 0
        && isTruncated
        && truncationReason == PackageSearchTruncationReason.RequestedLimit;

    private static DependencyInspectionEvidenceAvailability DeclarationState(
        DependencyEvidenceRootRow root) =>
        root.DeclarationState switch
        {
            DependencyEvidenceDeclarationState.NotApplicable =>
                DependencyInspectionEvidenceAvailability.NotApplicable,
            DependencyEvidenceDeclarationState.Available =>
                DependencyInspectionEvidenceAvailability.Available,
            DependencyEvidenceDeclarationState.Unavailable =>
                DependencyInspectionEvidenceAvailability.Unavailable,
            DependencyEvidenceDeclarationState.Failed =>
                DependencyInspectionEvidenceAvailability.Failed,
            _ => throw new InvalidOperationException(
                "Unknown declaration state."),
        };

    private static DependencyInspectionEvidencePhaseCompletion
        DeclarationCompletion(DependencyEvidenceRootRow root) =>
        root.DeclarationState switch
        {
            DependencyEvidenceDeclarationState.NotApplicable =>
                DependencyInspectionEvidencePhaseCompletion.NotApplicable,
            DependencyEvidenceDeclarationState.Available
                when root.DeclarationCompletion
                    == PackageDependencyEvidencePhaseCompletion.Complete =>
                        DependencyInspectionEvidencePhaseCompletion.Complete,
            DependencyEvidenceDeclarationState.Available =>
                DependencyInspectionEvidencePhaseCompletion.Partial,
            DependencyEvidenceDeclarationState.Unavailable =>
                DependencyInspectionEvidencePhaseCompletion.Unavailable,
            DependencyEvidenceDeclarationState.Failed =>
                DependencyInspectionEvidencePhaseCompletion.Failed,
            _ => throw new InvalidOperationException(
                "Unknown declaration completion."),
        };

    private static DependencyInspectionEvidenceAvailability RelationshipState(
        DependencyEvidenceRootRow root) =>
        root.GraphState switch
        {
            DependencyEvidenceGraphState.NotApplicable =>
                DependencyInspectionEvidenceAvailability.NotApplicable,
            DependencyEvidenceGraphState.Available =>
                DependencyInspectionEvidenceAvailability.Available,
            DependencyEvidenceGraphState.Unavailable =>
                DependencyInspectionEvidenceAvailability.Unavailable,
            DependencyEvidenceGraphState.Failed =>
                DependencyInspectionEvidenceAvailability.Failed,
            _ => throw new InvalidOperationException(
                "Unknown restored-relationship state."),
        };

    private static DependencyInspectionEvidencePhaseCompletion
        RelationshipCompletion(DependencyEvidenceRootRow root) =>
        root.GraphState switch
        {
            DependencyEvidenceGraphState.NotApplicable =>
                DependencyInspectionEvidencePhaseCompletion.NotApplicable,
            DependencyEvidenceGraphState.Available
                when root.GraphCompletion
                    == PackageDependencyEvidencePhaseCompletion.Complete =>
                        DependencyInspectionEvidencePhaseCompletion.Complete,
            DependencyEvidenceGraphState.Available =>
                DependencyInspectionEvidencePhaseCompletion.Partial,
            DependencyEvidenceGraphState.Unavailable =>
                DependencyInspectionEvidencePhaseCompletion.Unavailable,
            DependencyEvidenceGraphState.Failed =>
                DependencyInspectionEvidencePhaseCompletion.Failed,
            _ => throw new InvalidOperationException(
                "Unknown restored-relationship completion."),
        };

    private static (
        DependencyInspectionSelectionStatus Status,
        InertString? RequestedFramework,
        InertString? SelectedFramework) ProjectSelection(
            DependencyEvidenceRootRow root,
            DependencyEvidenceProjection evidence,
            InertString? requestedFramework,
            bool requested)
    {
        if (!requested)
        {
            return (
                DependencyInspectionSelectionStatus.NotRequested,
                null,
                null);
        }

        if (root.Owner != PackageDependencyEvidenceInputKind.RestoredProject)
        {
            return (
                PackageSelectionStatus(root),
                root.RequestedFramework,
                root.SelectedFramework);
        }

        if (root.RestoredTargetFrameworkIdentity is not { } selectedFramework)
        {
            return (
                requestedFramework is null
                    ? DependencyInspectionSelectionStatus.Unavailable
                    : DependencyInspectionSelectionStatus
                        .NoMatchingTargetFramework,
                requestedFramework,
                null);
        }

        InertString? selectedFrameworkSpelling = null;
        if (root.RestoredSelection is { } selection)
        {
            foreach (DependencyEvidenceGroupRow group in
                     evidence.DependencyGroups.Where(group =>
                         group.RootIndex == root.RootIndex))
            {
                PackageDependencyEvidenceGroupOccurrence.RestoredProject?
                    occurrence = group.SourceOccurrences
                        .OfType<PackageDependencyEvidenceGroupOccurrence
                            .RestoredProject>()
                        .FirstOrDefault(occurrence =>
                            occurrence.Identity.Selection == selection
                            && string.Equals(
                                occurrence.Identity.PivotIdentity,
                                selectedFramework,
                                StringComparison.Ordinal));
                if (occurrence is not null)
                {
                    selectedFrameworkSpelling =
                        root.RestoredTargetFrameworkSpelling;
                    break;
                }
            }
        }
        return (
            DependencyInspectionSelectionStatus.Selected,
            requestedFramework,
            selectedFrameworkSpelling
                ?? root.RestoredTargetFrameworkSpelling);
    }

    private static DependencyInspectionSelectionStatus PackageSelectionStatus(
        DependencyEvidenceRootRow root) =>
        root.SelectionStatus switch
        {
            PackageDependencyEvidenceSelectionStatus.Selected =>
                DependencyInspectionSelectionStatus.Selected,
            PackageDependencyEvidenceSelectionStatus.NoDependencyGroups =>
                DependencyInspectionSelectionStatus.NoDependencyGroups,
            PackageDependencyEvidenceSelectionStatus
                .NoMatchingTargetFramework =>
                    DependencyInspectionSelectionStatus
                        .NoMatchingTargetFramework,
            PackageDependencyEvidenceSelectionStatus.Unavailable =>
                DependencyInspectionSelectionStatus.Unavailable,
            _ => throw new InvalidOperationException(
                "Unknown dependency selection status."),
        };

    private static DependencyGraphNodeIdentity DependencyIdentity(
        PackageDependencyEvidenceRoot root) =>
        root.Identity switch
        {
            PackageDependencyEvidenceRootIdentity.Package package =>
                new DependencyGraphNodeIdentity.Package(
                    package.Coordinate.PackageId,
                    package.Coordinate.Version),
            PackageDependencyEvidenceRootIdentity.RestoredProject restored =>
                new DependencyGraphNodeIdentity.RestoredRoot(
                    restored.Identity),
            _ => throw new InvalidOperationException(
                "Asset dependency inspection supports package and restored-project evidence roots."),
        };

    private static DependencyInspectionEvidencePhaseCompletion
        AggregateEvidencePhase(
            ImmutableArray<DependencyInspectionRoot> roots,
            Func<
                DependencyInspectionRoot,
                DependencyInspectionEvidencePhaseCompletion> select,
            bool requested)
    {
        if (!requested)
            return DependencyInspectionEvidencePhaseCompletion.NotRequested;

        DependencyInspectionEvidencePhaseCompletion[] states =
        [
            .. roots.Select(select),
        ];
        if (states.Length == 0
            || states.All(static state =>
                state == DependencyInspectionEvidencePhaseCompletion.Failed))
        {
            return DependencyInspectionEvidencePhaseCompletion.Failed;
        }
        if (states.Any(static state =>
            state is DependencyInspectionEvidencePhaseCompletion.Partial
                or DependencyInspectionEvidencePhaseCompletion.Failed)
            || states.Any(static state =>
                state
                    == DependencyInspectionEvidencePhaseCompletion.Unavailable)
            && states.Any(static state =>
                state == DependencyInspectionEvidencePhaseCompletion.Complete))
        {
            return DependencyInspectionEvidencePhaseCompletion.Partial;
        }
        if (states.Any(static state =>
            state
                == DependencyInspectionEvidencePhaseCompletion.Unavailable))
        {
            return DependencyInspectionEvidencePhaseCompletion.Unavailable;
        }
        if (states.Any(static state =>
            state == DependencyInspectionEvidencePhaseCompletion.Complete))
        {
            return DependencyInspectionEvidencePhaseCompletion.Complete;
        }
        return DependencyInspectionEvidencePhaseCompletion.NotApplicable;
    }

    private static DependencyInspectionTraversalCompletion AggregateTraversal(
        ImmutableArray<DependencyInspectionRoot> roots)
    {
        if (roots.IsEmpty
            || roots.All(static root =>
                root.Traversal
                    == DependencyInspectionTraversalCompletion.Failed))
        {
            return DependencyInspectionTraversalCompletion.Failed;
        }
        if (roots.Any(static root =>
            root.Traversal
                is DependencyInspectionTraversalCompletion.Partial
                    or DependencyInspectionTraversalCompletion.Failed))
        {
            return DependencyInspectionTraversalCompletion.Partial;
        }
        if (roots.Any(static root =>
            root.Traversal
                == DependencyInspectionTraversalCompletion.DepthBounded))
        {
            return DependencyInspectionTraversalCompletion.DepthBounded;
        }
        if (roots.Any(static root =>
            root.Traversal
                == DependencyInspectionTraversalCompletion.SourceBounded))
        {
            return DependencyInspectionTraversalCompletion.SourceBounded;
        }
        return DependencyInspectionTraversalCompletion.Complete;
    }
}
