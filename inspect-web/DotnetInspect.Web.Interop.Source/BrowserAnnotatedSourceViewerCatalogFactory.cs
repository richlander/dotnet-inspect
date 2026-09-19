using System.Collections.Frozen;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Research;

namespace DotnetInspect.Web.Interop.Source;

internal static class BrowserAnnotatedSourceViewerCatalogFactory
{
    internal static readonly IReadOnlySet<AnnotationCategory> DefaultFindingCategories =
        new[]
        {
            AnnotationCategory.Allocation,
            AnnotationCategory.Unsafety,
            AnnotationCategory.Cost,
            AnnotationCategory.Semantics,
            AnnotationCategory.Lifetime,
        }.ToFrozenSet();

    private static readonly BrowserAnnotatedSourceCapabilityAvailability NotProjected =
        new(
            Available: false,
            UnavailableReason:
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected);

    // Call-shaped syntax wins hit testing independently of whether a destination is available.
    private static readonly string[] InvocationLikeNodeKinds =
    [
        "InvocationExpression",
        "IndirectInvocationExpression",
        "ObjectCreationExpression",
        "DelegateCreationExpression",
    ];

    private static readonly IReadOnlySet<string> AllocationDescriptorIds =
        new[]
        {
            "alloc.box",
            "alloc.array",
            "alloc.new",
            "alloc.closure",
            "alloc.statemachine",
            "alloc.delegate",
            "alloc.enumerator",
        }.ToFrozenSet(StringComparer.Ordinal);

    public static BrowserAnnotatedSourceViewerCatalog Create(
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceInvocationDestination[]?
            invocationDestinations = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            destinationUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceFindingEvidence[]?
            findingEvidence = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            findingEvidenceUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceCallRelationship[]?
            callRelationships = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            callRelationshipsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceCallCycleInspection? callCycles = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            callCyclesUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceSynchronousCompletion[]?
            synchronousCompletions = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            synchronousCompletionsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceAwaitCompletionPath[]?
            awaitCompletionPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            awaitCompletionPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceAllocationExceptionPath[]?
            allocationExceptionPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            allocationExceptionPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected,
        BrowserAnnotatedSourceLocalThrowPathInspection?
            localThrowPaths = null,
        BrowserAnnotatedSourceCapabilityUnavailableReason
            localThrowPathsUnavailableReason =
                BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected)
    {
        ArgumentNullException.ThrowIfNull(document);
        BrowserAnnotatedSourceInvocationDestination[] projectedDestinations =
            invocationDestinations is null
                ? []
                : ValidateInvocationDestinations(document, invocationDestinations);
        if (callRelationships is not null)
            ValidateCallRelationships(document, callRelationships);
        if (callCycles is not null)
        {
            if (callRelationships is null)
            {
                throw new ArgumentException(
                    "Call cycles require projected call relationships.",
                    nameof(callCycles));
            }
            ValidateCallCycles(
                document,
                callRelationships,
                callCycles);
        }
        if (synchronousCompletions is not null)
        {
            if (callRelationships is null)
            {
                throw new ArgumentException(
                    "Synchronous completions require projected call relationships.",
                    nameof(synchronousCompletions));
            }
            ValidateSynchronousCompletions(
                callRelationships,
                synchronousCompletions);
        }
        if (awaitCompletionPaths is not null)
            ValidateAwaitCompletionPaths(document, awaitCompletionPaths);
        if (allocationExceptionPaths is not null)
        {
            ValidateAllocationExceptionPaths(
                document,
                allocationExceptionPaths);
        }
        if (localThrowPaths is not null)
        {
            if (callRelationships is null)
            {
                throw new ArgumentException(
                    "Local throw paths require projected call relationships.",
                    nameof(localThrowPaths));
            }
            ValidateLocalThrowPaths(
                callRelationships,
                localThrowPaths);
        }

        var targetedFacts = new bool[document.Facts.Count];
        foreach (AnnotatedSourceTarget target in document.Targets)
            targetedFacts[target.FactId] = true;

        int[] defaultFindingIds =
        [
            .. document.Facts
                .Where(fact =>
                    targetedFacts[fact.Id]
                        && IsDefaultFindingCategory(fact.Category))
                .Select(fact => fact.Id),
        ];
        BrowserAnnotatedSourceMedium[] supportedMedia =
            document.Nodes.Any(node => node.Medium == SourceLineKind.Il)
                ?
                [
                    BrowserAnnotatedSourceMedium.CSharp,
                    BrowserAnnotatedSourceMedium.Il,
                ]
                : [BrowserAnnotatedSourceMedium.CSharp];
        string[] invocationLikeNodeKinds =
        [
            .. InvocationLikeNodeKinds.Where(kind =>
                document.Nodes.Any(node =>
                    node.Medium == SourceLineKind.CSharp
                        && string.Equals(node.Kind, kind, StringComparison.Ordinal))),
        ];

        return new BrowserAnnotatedSourceViewerCatalog(
            defaultFindingIds,
            supportedMedia,
            invocationLikeNodeKinds,
            findingEvidence is null
                ? findingEvidenceUnavailableReason
                    == BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    ? NotProjected
                    : new BrowserAnnotatedSourceCapabilityAvailability(
                        Available: false,
                        findingEvidenceUnavailableReason)
                : new BrowserAnnotatedSourceCapabilityAvailability(
                    Available: true,
                    UnavailableReason: null),
            invocationDestinations is null
                ? destinationUnavailableReason
                    == BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    ? NotProjected
                    : new BrowserAnnotatedSourceCapabilityAvailability(
                        Available: false,
                        destinationUnavailableReason)
                : new BrowserAnnotatedSourceCapabilityAvailability(
                    Available: true,
                    UnavailableReason: null),
            callRelationships is null
                ? callRelationshipsUnavailableReason
                    == BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected
                    ? NotProjected
                    : new BrowserAnnotatedSourceCapabilityAvailability(
                        Available: false,
                        callRelationshipsUnavailableReason)
                : new BrowserAnnotatedSourceCapabilityAvailability(
                    Available: true,
                    UnavailableReason: null),
            callCycles
                ?? new BrowserAnnotatedSourceCallCycleInspection(
                    Available: false,
                    callCyclesUnavailableReason,
                    IsComplete: false,
                    Limits: [],
                    Findings: []),
            synchronousCompletions is null
                ? new BrowserAnnotatedSourceSynchronousCompletionInspection(
                    Available: false,
                    synchronousCompletionsUnavailableReason,
                    Observations: [])
                : new BrowserAnnotatedSourceSynchronousCompletionInspection(
                    Available: true,
                    UnavailableReason: null,
                    Observations: synchronousCompletions),
            awaitCompletionPaths is null
                ? new BrowserAnnotatedSourceAwaitCompletionPathInspection(
                    Available: false,
                    awaitCompletionPathsUnavailableReason,
                    Observations: [])
                : new BrowserAnnotatedSourceAwaitCompletionPathInspection(
                    Available: true,
                    UnavailableReason: null,
                    Observations: awaitCompletionPaths),
            allocationExceptionPaths is null
                ? new BrowserAnnotatedSourceAllocationExceptionPathInspection(
                    Available: false,
                    allocationExceptionPathsUnavailableReason,
                    Observations: [])
                : new BrowserAnnotatedSourceAllocationExceptionPathInspection(
                    Available: true,
                    UnavailableReason: null,
                    Observations: allocationExceptionPaths),
            localThrowPaths
                ?? new BrowserAnnotatedSourceLocalThrowPathInspection(
                    Available: false,
                    localThrowPathsUnavailableReason,
                    IsComplete: false,
                    Boundaries: [],
                    Limits: null,
                    Receipt: null,
                    Paths: []),
            projectedDestinations);
    }

    private static void ValidateCallRelationships(
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceCallRelationship[] relationships)
    {
        var factIds = new HashSet<int>();
        var physicalOccurrences =
            new HashSet<(Guid ModuleVersionId, int CallerToken, int IlOffset, int OperandToken)>();
        foreach ((BrowserAnnotatedSourceCallRelationship relationship, int index)
            in relationships.Select((relationship, index) =>
                (relationship, index)))
        {
            if (relationship is null)
            {
                throw new ArgumentException(
                    $"Call relationship row {index} is null.",
                    nameof(relationships));
            }
            if (relationship.EdgeRow < 1
                || relationship.FactId < 0
                || relationship.FactId >= document.Facts.Count
                || relationship.ModuleVersionId == Guid.Empty
                || (relationship.CallerToken & 0xFF000000) != 0x06000000
                || relationship.IlOffset < 0
                || relationship.OperandToken <= 0
                || !Enum.IsDefined(relationship.Kind)
                || !physicalOccurrences.Add((
                    relationship.ModuleVersionId,
                    relationship.CallerToken,
                    relationship.IlOffset,
                    relationship.OperandToken))
                || !factIds.Add(relationship.FactId))
            {
                throw new ArgumentException(
                    $"Call relationship row {index} has invalid or duplicate identity.",
                    nameof(relationships));
            }

            AnnotatedSourceFact fact = document.Facts[relationship.FactId];
            if (fact.Descriptor
                    != ResearchFactRegistry.CallRelationshipDescriptorId
                || fact.Origin != AnnotatedSourceFactOrigin.Body
                || fact.SourceOffset != relationship.IlOffset
                || !document.Targets.Any(target =>
                    target.FactId == relationship.FactId))
            {
                throw new ArgumentException(
                    $"Call relationship row {index} does not name one targeted call.edge fact.",
                    nameof(relationships));
            }
            ArgumentNullException.ThrowIfNull(relationship.Target);
        }
    }

    private static void ValidateCallCycles(
        AnnotatedSourceDocument document,
        IReadOnlyList<BrowserAnnotatedSourceCallRelationship> relationships,
        BrowserAnnotatedSourceCallCycleInspection cycles)
    {
        if (!cycles.Available)
        {
            throw new ArgumentException(
                "Projected call cycles must be available.",
                nameof(cycles));
        }

        BrowserAnnotatedSourceCallCycleLimit[] limits =
            cycles.Limits;
        if (limits.Any(limit => !Enum.IsDefined(limit))
            || limits.Distinct().Count() != limits.Length)
        {
            throw new ArgumentException(
                "Call cycle limits must be defined and unique.",
                nameof(cycles));
        }

        BrowserAnnotatedSourceCallCycle[] findings =
            cycles.Findings;
        var findingKeys = new HashSet<string>(
            StringComparer.Ordinal);
        for (int index = 0; index < findings.Length; index++)
        {
            BrowserAnnotatedSourceCallCycle finding =
                findings[index]
                    ?? throw new ArgumentException(
                        $"Call cycle {index} is null.",
                        nameof(cycles));
            int[] edgeRows = finding.EdgeRows;
            int[] factIds = finding.FactIds;
            BrowserCallGraphTarget[] targets =
                finding.Targets;
            if (finding.Ordinal != index
                || string.IsNullOrWhiteSpace(finding.FindingKey)
                || !findingKeys.Add(finding.FindingKey)
                || edgeRows.Length == 0
                || edgeRows.Any(static edgeRow => edgeRow < 1)
                || edgeRows.Distinct().Count() != edgeRows.Length
                || factIds.Length == 0
                || factIds.Distinct().Count() != factIds.Length
                || targets.Length != edgeRows.Length
                || targets.Any(static target => target is null))
            {
                throw new ArgumentException(
                    $"Call cycle {index} has invalid identity or path evidence.",
                    nameof(cycles));
            }

            int[] expectedFactIds =
            [
                .. relationships
                    .Where(relationship =>
                        relationship.EdgeRow == edgeRows[0])
                    .Select(relationship =>
                        relationship.FactId),
            ];
            if (expectedFactIds.Length == 0
                || !expectedFactIds.ToHashSet()
                    .SetEquals(factIds))
            {
                throw new ArgumentException(
                    $"Call cycle {index} is not anchored to every physical occurrence of its first edge.",
                    nameof(cycles));
            }
            foreach (int factId in factIds)
            {
                if (factId < 0
                    || factId >= document.Facts.Count
                    || document.Facts[factId].Descriptor
                        != ResearchFactRegistry
                            .CallRelationshipDescriptorId)
                {
                    throw new ArgumentException(
                        $"Call cycle {index} names an invalid call.edge fact.",
                        nameof(cycles));
                }
            }
        }
    }

    private static void ValidateSynchronousCompletions(
        IReadOnlyList<BrowserAnnotatedSourceCallRelationship> relationships,
        BrowserAnnotatedSourceSynchronousCompletion[] observations)
    {
        HashSet<int> relationshipFactIds =
        [
            .. relationships.Select(static relationship =>
                relationship.FactId),
        ];
        var observedFactIds = new HashSet<int>();
        for (int index = 0; index < observations.Length; index++)
        {
            BrowserAnnotatedSourceSynchronousCompletion observation =
                observations[index]
                    ?? throw new ArgumentException(
                        $"Synchronous completion observation {index} is null.",
                        nameof(observations));
            if (!Enum.IsDefined(observation.Kind)
                || !relationshipFactIds.Contains(observation.FactId)
                || !observedFactIds.Add(observation.FactId))
            {
                throw new ArgumentException(
                    $"Synchronous completion observation {index} has invalid or duplicate relationship evidence.",
                    nameof(observations));
            }
        }

    }

    private static void ValidateAwaitCompletionPaths(
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceAwaitCompletionPath[] observations)
    {
        var observedNodeIds = new HashSet<int>();
        for (int index = 0; index < observations.Length; index++)
        {
            BrowserAnnotatedSourceAwaitCompletionPath observation =
                observations[index]
                    ?? throw new ArgumentException(
                        $"Await completion-path observation {index} is null.",
                        nameof(observations));
            if (observation.NodeId < 0
                || observation.NodeId >= document.Nodes.Count
                || !observedNodeIds.Add(observation.NodeId))
            {
                throw new ArgumentException(
                    $"Await completion-path observation {index} does not name a unique document node.",
                    nameof(observations));
            }

            AnnotatedSourceNode node = document.Nodes[observation.NodeId];
            if (node.Medium != SourceLineKind.CSharp
                || !string.Equals(
                    node.Kind,
                    AnnotatedSourceNodeKinds.AwaitExpression,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Await completion-path observation {index} does not name a C# AwaitExpression node.",
                    nameof(observations));
            }
        }
    }

    private static void ValidateAllocationExceptionPaths(
        AnnotatedSourceDocument document,
        BrowserAnnotatedSourceAllocationExceptionPath[] observations)
    {
        var observedFactIds = new HashSet<int>();
        for (int index = 0; index < observations.Length; index++)
        {
            BrowserAnnotatedSourceAllocationExceptionPath observation =
                observations[index]
                    ?? throw new ArgumentException(
                        $"Allocation exception-path observation {index} is null.",
                        nameof(observations));
            if (!Enum.IsDefined(observation.Kind)
                || observation.FactId < 0
                || observation.FactId >= document.Facts.Count
                || !observedFactIds.Add(observation.FactId))
            {
                throw new ArgumentException(
                    $"Allocation exception-path observation {index} does not name unique typed evidence.",
                    nameof(observations));
            }

            AnnotatedSourceFact fact = document.Facts[observation.FactId];
            if (fact.Origin != AnnotatedSourceFactOrigin.Body
                || !AllocationDescriptorIds.Contains(fact.Descriptor))
            {
                throw new ArgumentException(
                    $"Allocation exception-path observation {index} does not name a body allocation fact.",
                    nameof(observations));
            }
        }
    }

    private static void ValidateLocalThrowPaths(
        IReadOnlyList<BrowserAnnotatedSourceCallRelationship> relationships,
        BrowserAnnotatedSourceLocalThrowPathInspection inspection)
    {
        if (!inspection.Available
            || inspection.Limits is not { } limits
            || inspection.Receipt is not { } receipt)
        {
            throw new ArgumentException(
                "Projected local throw paths must be available with limits and a receipt.",
                nameof(inspection));
        }
        if (limits.MaximumDepth < 0
            || limits.MaximumNodes < 1
            || limits.MaximumEdges < 1
            || limits.MaximumPaths < 1
            || receipt.DestinationSearches < 0
            || receipt.SearchNodes < 0
            || receipt.SearchedEdges < 0
            || receipt.ObservedReachablePairs < 0
            || receipt.ReturnedPaths != inspection.Paths.Length)
        {
            throw new ArgumentException(
                "Local throw path limits or receipt are invalid.",
                nameof(inspection));
        }
        if (inspection.Boundaries.Any(boundary =>
                boundary is null
                || !Enum.IsDefined(boundary.Kind)
                || boundary.Value < 0)
            || inspection.Boundaries
                .Select(static boundary => boundary.Kind)
                .Distinct()
                .Count() != inspection.Boundaries.Length)
        {
            throw new ArgumentException(
                "Local throw path boundaries must be defined and unique.",
                nameof(inspection));
        }

        Dictionary<int, int> edgeByFact = relationships.ToDictionary(
            static relationship => relationship.FactId,
            static relationship => relationship.EdgeRow);
        Dictionary<int, BrowserAnnotatedSourceCallKind> kindByFact =
            relationships.ToDictionary(
                static relationship => relationship.FactId,
                static relationship => relationship.Kind);
        for (int index = 0; index < inspection.Paths.Length; index++)
        {
            BrowserAnnotatedSourceLocalThrowPath path =
                inspection.Paths[index]
                    ?? throw new ArgumentException(
                        $"Local throw path {index} is null.",
                        nameof(inspection));
            if (path.FactIds.Length == 0
                || path.FactIds.Distinct().Count() != path.FactIds.Length
                || path.FactIds.Any(factId =>
                    !edgeByFact.ContainsKey(factId))
                || path.FactIds.Any(factId =>
                    !IsLocalThrowPathCallKind(kindByFact[factId]))
                || path.FactIds
                    .Select(factId => edgeByFact[factId])
                    .Distinct()
                    .Count() != 1
                || path.Targets.Length == 0
                || path.Targets.Length > limits.MaximumDepth
                || path.Targets.Any(static target => target is null)
                || path.TerminalThrows.Length == 0)
            {
                throw new ArgumentException(
                    $"Local throw path {index} has invalid source or member evidence.",
                    nameof(inspection));
            }
            int firstEdgeRow = edgeByFact[path.FactIds[0]];
            int[] expectedFactIds =
            [
                .. relationships
                    .Where(relationship =>
                        relationship.EdgeRow == firstEdgeRow
                        && IsLocalThrowPathCallKind(relationship.Kind))
                    .Select(relationship => relationship.FactId),
            ];
            if (expectedFactIds.Length != path.FactIds.Length
                || expectedFactIds.Any(factId =>
                    !path.FactIds.Contains(factId)))
            {
                throw new ArgumentException(
                    $"Local throw path {index} does not retain every physical first-edge occurrence.",
                    nameof(inspection));
            }
            foreach (
                BrowserAnnotatedSourceLocalThrowSite site
                in path.TerminalThrows)
            {
                if (site is null
                    || string.IsNullOrWhiteSpace(site.ExceptionType)
                    || site.DefinitionModuleVersionId == Guid.Empty
                    || (site.DefinitionToken & 0xFF000000) != 0x02000000
                    || site.ConstructionOffset < 0
                    || site.ConstructorToken <= 0
                    || site.ThrowOffset < 0)
                {
                    throw new ArgumentException(
                        $"Local throw path {index} has invalid terminal throw evidence.",
                        nameof(inspection));
                }
            }

            static bool IsLocalThrowPathCallKind(
                BrowserAnnotatedSourceCallKind kind) =>
                kind is BrowserAnnotatedSourceCallKind.Call
                    or BrowserAnnotatedSourceCallKind.CallVirtual
                    or BrowserAnnotatedSourceCallKind.NewObject;
        }
    }

    private static BrowserAnnotatedSourceInvocationDestination[]
        ValidateInvocationDestinations(
            AnnotatedSourceDocument document,
            BrowserAnnotatedSourceInvocationDestination[] destinations)
    {
        var nodeIds = new HashSet<int>();
        var rows =
            new BrowserAnnotatedSourceInvocationDestination[destinations.Length];
        for (int index = 0; index < destinations.Length; index++)
        {
            BrowserAnnotatedSourceInvocationDestination destination =
                destinations[index]
                ?? throw new ArgumentException(
                    "Invocation destination rows cannot be null.",
                    nameof(destinations));
            if (destination.NodeId < 0
                || destination.NodeId >= document.Nodes.Count)
            {
                throw new ArgumentException(
                    $"Invocation destination node {destination.NodeId} does not exist.",
                    nameof(destinations));
            }
            AnnotatedSourceNode node = document.Nodes[destination.NodeId];
            if (node.Medium != SourceLineKind.CSharp
                || !string.Equals(
                    node.Kind,
                    "InvocationExpression",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Invocation destination node {destination.NodeId} is not a C# invocation.",
                    nameof(destinations));
            }
            if (!nodeIds.Add(destination.NodeId))
            {
                throw new ArgumentException(
                    $"Invocation destination node {destination.NodeId} is duplicated.",
                    nameof(destinations));
            }
            ArgumentNullException.ThrowIfNull(destination.Target);
            rows[index] = destination;
        }
        return rows;
    }

    private static bool IsDefaultFindingCategory(string category) =>
        DefaultFindingCategories.Any(
            defaultCategory =>
                string.Equals(
                    category,
                    defaultCategory.ToString(),
                    StringComparison.Ordinal));
}
