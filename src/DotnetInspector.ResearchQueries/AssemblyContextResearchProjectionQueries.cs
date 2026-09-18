using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>Content-shaped request for one group-scoped Research type projection.</summary>
public sealed record AssemblyContextTypeProjectionRequest(
    string Type,
    bool PublicOnly = false,
    bool Composition = true,
    bool RelationshipGraph = true);

/// <summary>Content-shaped request for one group-scoped Research member projection.</summary>
/// <param name="MethodToken">
/// The exact <c>MethodDef</c> token to project. Supplying it addresses one overload — or one
/// property/event accessor body — without name-and-index guessing, which is what a consumer that
/// already holds a surface's body selectors should do.
/// </param>
/// <param name="AnalysisFeatures">
/// The whole-assembly Analysis features the projection's fact context is built with. The default
/// matches what the Research fact producers observe through.
/// </param>
/// <param name="CallRelationships">
/// Includes exact body-local <c>call.edge</c> Findings in the member census. This requires an
/// exact <paramref name="MethodToken"/> and a source document so every relationship retains its
/// product-issued source targets.
/// </param>
/// <param name="CallCycles">
/// Includes bounded focus-cycle witnesses over the same exact call relationships. Positive
/// witnesses remain valid when the independent cycle census is incomplete.
/// </param>
public sealed record AssemblyContextMemberProjectionRequest(
    string Type,
    string Member,
    int OverloadIndex = 0,
    int? MethodToken = null,
    bool PublicOnly = false,
    bool AnnotatedSource = false,
    bool SourceDocument = false,
    bool FactRows = false,
    bool FindingEvidence = false,
    bool InvocationDestinations = false,
    AnnotationStage AnnotatedStage = AnnotationStage.Raised,
    PrinterOptions? PrinterOptions = null,
    LibraryBodyAnalysisFeatures AnalysisFeatures = LibraryBodyAnalysisFeatures.Default,
    bool CallRelationships = false,
    bool CallCycles = false);

/// <summary>Why a member projection's whole-assembly fact context is narrower than a complete one.</summary>
public enum MemberProjectionContextLimitationKind
{
    /// <summary>
    /// The whole-assembly Analysis index could not be built from the participant's image, so the
    /// projection observed a consistent absence of assembly-scoped facts rather than the facts
    /// themselves.
    /// </summary>
    AssemblyContextUnavailable,
}

/// <summary>
/// A visible narrowing of a member projection's fact context. A projection that could not build
/// the whole-assembly context carries this instead of silently omitting the facts that context
/// produces.
/// </summary>
public sealed record MemberProjectionContextLimitation(
    MemberProjectionContextLimitationKind Kind,
    string Detail);

/// <summary>
/// One Decompiler-issued invocation node joined to one CallGraph-owned typed callee.
/// </summary>
public sealed record AssemblyMemberInvocationDestination(
    int NodeId,
    CallGraphNode Target);

/// <summary>
/// One method-qualified instruction coordinate supporting a caller-side Finding.
/// </summary>
public sealed record AssemblyMemberCalleeEvidenceCoordinate(
    ResearchEvidenceLocation Location,
    CallSiteEvidenceKind Kind);

/// <summary>
/// One exact Finding instance joined to typed evidence about its physical callee.
/// </summary>
public sealed record AssemblyMemberFindingEvidence(
    int FactId,
    FindingInstanceKey InstanceKey,
    MethodIdentity Member,
    ResearchFindingEvidenceState State,
    IReadOnlyList<CallSiteCostEvidenceInput> AggregateInputs,
    IReadOnlyList<AssemblyMemberCalleeEvidenceCoordinate> Coordinates,
    AnnotatedSourceDocument? SourceDocument,
    IReadOnlyList<int> NodeIds,
    string? UnavailableReason);

/// <summary>
/// One physical call relationship joined to its stable graph target.
/// </summary>
public sealed record AssemblyMemberCallRelationship(
    AnnotatedCallGraphOccurrence Occurrence,
    CallGraphNode Target);

/// <summary>
/// Exact source-targeted call occurrences from one depth-one graph projection.
/// </summary>
public sealed record AssemblyMemberCallRelationshipOverlay(
    IReadOnlyList<AssemblyMemberCallRelationship> Relationships);

/// <summary>
/// One focus cycle joined to the physical source facts for its first edge and
/// the typed target reached by every ordered edge.
/// </summary>
public sealed record AssemblyMemberCallCycle(
    FindingKey Key,
    int Ordinal,
    IReadOnlyList<int> EdgeRows,
    IReadOnlyList<int> FactIds,
    IReadOnlyList<CallGraphNode> Targets);

/// <summary>
/// Bounded focus-cycle Findings plus the independent completeness state of the
/// operation that produced them.
/// </summary>
public sealed record AssemblyMemberCallCycleInspection(
    IReadOnlyList<AssemblyMemberCallCycle> Findings,
    AnnotatedCallGraphCycleLimit Limits)
{
    public bool IsComplete => Limits == AnnotatedCallGraphCycleLimit.None;
}

/// <summary>One participant's member projection and any narrowing of its fact context.</summary>
public sealed record AssemblyMemberProjection(
    ResearchViews.MemberProjectionResult Projection,
    MemberProjectionContextLimitation? ContextLimitation,
    IReadOnlyList<AssemblyMemberFindingEvidence>? FindingEvidence,
    IReadOnlyList<AssemblyMemberInvocationDestination> InvocationDestinations,
    AssemblyMemberCallRelationshipOverlay? CallRelationships = null,
    AssemblyMemberCallCycleInspection? CallCycles = null);

/// <summary>
/// Projects the Research type view from participants of one binding-consistent assembly context
/// group, without a filesystem path.
/// </summary>
/// <remarks>
/// The query owns the <see cref="MetadataSource"/> it opens over the group's immutable image
/// snapshot, and resolves that source's assembly references through the participant's own binding
/// policy rather than by guessing names. Consumers receive a typed per-participant outcome.
/// Pathless projection and binding-consistent resolution are gated by
/// <c>AssemblyContextResearchProjectionQueryTests</c>.
/// </remarks>
public static class AssemblyContextTypeProjectionQuery
{
    public static InspectionQuery<
        AssemblyContextResult<ResearchViews.TypeProjectionResult>> Definition { get; } =
        new("Assembly context type projection", InspectionCost.Unbounded);

    public static AssemblyContextResult<ResearchViews.TypeProjectionResult> Execute(
        AssemblyContextGroup group,
        AssemblyContextTypeProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        return AssemblyContextQueryExecutor.ExecuteOverSnapshots(
            group,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    public static AssemblyContextEntry<ResearchViews.TypeProjectionResult> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextTypeProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    static ResearchViews.TypeProjectionResult Project(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        AssemblyContextTypeProjectionRequest request)
    {
        AssemblyContextAnalysisSource.BindingPolicyResolver resolver =
            AssemblyContextResearchSource.Resolver(group, subject);
        using MetadataSource source =
            AssemblyContextResearchSource.Open(
                group,
                subject,
                snapshot,
                resolver);
        ResearchViews.TypeProjectionResult result = ResearchViews.ProjectType(
            new ResearchViews.TypeProjectionRequest(
                source,
                request.Type,
                request.PublicOnly,
                request.Composition,
                request.RelationshipGraph));
        resolver.ValidateForPublication();
        return result;
    }
}

/// <summary>
/// Projects the Research member view — including the portable
/// <see cref="AnnotatedSourceDocument"/> — from participants of one binding-consistent assembly
/// context group, without a filesystem path.
/// </summary>
/// <remarks>
/// The query owns both lifetimes the projection needs: the <see cref="MetadataSource"/> over the
/// group's immutable image snapshot, and the whole-assembly <see cref="LibraryBodyIndex"/> the
/// Research fact producers observe through. Path-keyed Analysis resolution cannot reach a
/// snapshot, so the context is supplied explicitly; when it cannot be built the result carries a
/// <see cref="MemberProjectionContextLimitation"/> rather than a fact-free projection that reads
/// as complete. Gated by <c>AssemblyContextResearchProjectionQueryTests</c>.
/// </remarks>
public static class AssemblyContextMemberProjectionQuery
{
    public static InspectionQuery<
        AssemblyContextResult<AssemblyMemberProjection>> Definition { get; } =
        new("Assembly context member projection", InspectionCost.Unbounded);

    public static AssemblyContextResult<AssemblyMemberProjection> Execute(
        AssemblyContextGroup group,
        AssemblyContextMemberProjectionRequest request)
    {
        Validate(request);
        return AssemblyContextQueryExecutor.ExecuteOverSnapshots(
            group,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    public static AssemblyContextEntry<AssemblyMemberProjection> ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextMemberProjectionRequest request)
    {
        Validate(request);
        return AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) => Project(group, subject, snapshot, request));
    }

    static void Validate(AssemblyContextMemberProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Member);
        ArgumentOutOfRangeException.ThrowIfNegative(request.OverloadIndex);
        if (request.InvocationDestinations && !request.SourceDocument)
        {
            throw new ArgumentException(
                "Invocation destinations require a source document.",
                nameof(request));
        }
        if (request.CallRelationships
            && (!request.SourceDocument || request.MethodToken is null))
        {
            throw new ArgumentException(
                "Call relationships require a source document and an exact MethodDef token.",
                nameof(request));
        }
        if (request.CallCycles && !request.CallRelationships)
        {
            throw new ArgumentException(
                "Call cycles require exact call relationships.",
                nameof(request));
        }
        if (request.FindingEvidence
            && (!request.SourceDocument || !request.FactRows))
        {
            throw new ArgumentException(
                "Finding evidence requires Facts rows and a source document.",
                nameof(request));
        }
    }

    static AssemblyMemberProjection Project(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        AssemblyContextMemberProjectionRequest request)
    {
        AssemblyContextAnalysisSource.BindingPolicyResolver resolver =
            AssemblyContextResearchSource.Resolver(group, subject);
        LibraryBodyIndex? index = null;
        MemberProjectionContextLimitation? limitation = null;
        try
        {
            index = LibraryBodyIndex.OpenFromPrefetchedImage(
                AssemblyContextResearchSource.Name(subject),
                snapshot.Content,
                request.AnalysisFeatures,
                resolver);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException
                or NotSupportedException)
        {
            limitation = new MemberProjectionContextLimitation(
                MemberProjectionContextLimitationKind.AssemblyContextUnavailable,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            using MetadataSource source =
                AssemblyContextResearchSource.Open(
                    group,
                    subject,
                    snapshot,
                    resolver);
            ResearchAssemblyContext? assembly =
                index is null ? null : ResearchAssemblyContext.Create(index);
            CallRelationshipProjection? callRelationships =
                index is not null
                    && request.MethodToken is int requestedMethodToken
                    && request.CallRelationships
                    ? ProjectCallRelationships(
                        index,
                        requestedMethodToken,
                        request.CallCycles)
                    : null;
            if (request.CallRelationships
                && index is not null
                && callRelationships is null)
            {
                throw new InvalidOperationException(
                    "Call relationship projection produced no callee topology.");
            }
            ResearchViews.MemberProjectionResult projection =
                ResearchViews.ProjectMember(
                    new ResearchViews.MemberProjectionRequest(
                        source,
                        request.Type,
                        request.Member,
                        request.OverloadIndex,
                        request.PublicOnly,
                        request.AnnotatedSource,
                        CostOverlay: false,
                        SemanticsOverlay: false,
                        request.FactRows,
                        request.AnnotatedStage,
                        Registry: request.CallRelationships
                                && callRelationships is not null
                            ? ResearchFactRegistry
                                .MemberCensusWithCallRelationships
                            : null,
                        request.MethodToken,
                        request.PrinterOptions,
                        CaretFocus: null,
                        request.SourceDocument,
                        assembly,
                        CallSites: request.CallRelationships
                                && callRelationships is not null
                            ? callRelationships.Calls
                                .Select(call => call.Call)
                                .ToArray()
                            : null));
            IReadOnlyList<AssemblyMemberFindingEvidence>? findingEvidence =
                request.FindingEvidence
                    ? assembly is null
                        ? null
                        : ProjectFindingEvidence(
                            source,
                            projection,
                            assembly,
                            request.PrinterOptions)
                    : null;
            IReadOnlyList<AssemblyMemberInvocationDestination> destinations = [];
            if (request.InvocationDestinations
                && index is not null
                && projection.SourceDocument is { } document
                && projection.SelectedMethodToken is { } methodToken)
            {
                CallRelationshipProjection? destinationRelationships =
                    callRelationships
                    ?? ProjectCallRelationships(
                        index,
                        methodToken,
                        includeCycles: false);
                if (destinationRelationships is not null)
                {
                    destinations = ProjectInvocationDestinations(
                        destinationRelationships,
                        document);
                }
            }
            AssemblyMemberCallRelationshipOverlay? relationshipOverlay =
                request.CallRelationships
                    && callRelationships is not null
                    && projection.SourceDocument is { } relationshipDocument
                    ? ProjectCallRelationshipOverlay(
                        callRelationships,
                        relationshipDocument)
                    : null;
            AssemblyMemberCallCycleInspection? cycleInspection =
                request.CallCycles
                    && callRelationships is not null
                    && relationshipOverlay is not null
                    ? ProjectCallCycles(
                        callRelationships,
                        relationshipOverlay)
                    : null;
            var result = new AssemblyMemberProjection(
                projection,
                limitation,
                findingEvidence,
                destinations,
                relationshipOverlay,
                cycleInspection);
            resolver.ValidateForPublication();
            return result;
        }
        finally
        {
            // The index holds derived call-graph maps for the whole assembly. It is not
            // disposable, so hand that memory back explicitly before the query returns rather
            // than leaving it to a browser's collector.
            index?.ReleaseCallGraphCaches();
        }
    }

    static IReadOnlyList<AssemblyMemberFindingEvidence> ProjectFindingEvidence(
        MetadataSource source,
        ResearchViews.MemberProjectionResult projection,
        ResearchAssemblyContext assembly,
        PrinterOptions? printerOptions)
    {
        if (projection.Facts is not { } facts
            || projection.SourceDocumentFactIdentities is not { } identities)
        {
            throw new InvalidOperationException(
                "Callee evidence requires one Finding census projected through Facts and Annotated Source.");
        }

        Dictionary<FindingInstanceKey, int> factIdsByInstance =
            identities.ToDictionary(
                identity => identity.InstanceKey,
                identity => identity.FactId);
        var calleeProjections =
            new Dictionary<MethodIdentity, CalleeSourceProjection>();
        var result = new List<AssemblyMemberFindingEvidence>();
        foreach (ResearchViews.FactRow fact in facts)
        {
            if (fact.Id is not (
                    "cost.callee"
                    or "semantics.callee"
                    or "safety.callee")
                || fact.InstanceKey is not { } instanceKey
                || fact.Evidence is not { } evidence)
            {
                continue;
            }
            if (!factIdsByInstance.TryGetValue(instanceKey, out int factId))
            {
                throw new InvalidOperationException(
                    $"Callee evidence Finding instance {instanceKey} has no Annotated Source fact identity.");
            }
            if (fact.Id == "cost.callee")
            {
                ValidateMethodEvidence(fact.Id, evidence);
                result.Add(new AssemblyMemberFindingEvidence(
                    factId,
                    instanceKey,
                    evidence.Subject,
                    evidence.State,
                    evidence.AggregateInputs,
                    Coordinates: [],
                    SourceDocument: null,
                    NodeIds: [],
                    UnavailableReason: null));
                continue;
            }
            if (evidence.State == ResearchFindingEvidenceState.Method)
            {
                throw new InvalidOperationException(
                    $"Instruction-level Finding '{fact.Id}' carried method-only evidence.");
            }
            if (!evidence.AggregateInputs.IsDefaultOrEmpty)
            {
                throw new InvalidOperationException(
                    $"Instruction-level Finding '{fact.Id}' carried aggregate method evidence.");
            }

            IReadOnlyList<AssemblyMemberCalleeEvidenceCoordinate> coordinates =
                EvidenceCoordinates(fact.Id, evidence, assembly);
            if (evidence.State == ResearchFindingEvidenceState.InstructionUnavailable)
            {
                result.Add(new AssemblyMemberFindingEvidence(
                    factId,
                    instanceKey,
                    evidence.Subject,
                    evidence.State,
                    evidence.AggregateInputs,
                    coordinates,
                    SourceDocument: null,
                    NodeIds: [],
                    "Research reported no instruction coordinates for this callee evidence."));
                continue;
            }

            CalleeSourceProjection callee = ProjectCalleeSource(
                source,
                assembly,
                evidence.Subject,
                printerOptions,
                calleeProjections);
            if (callee.Document is null)
            {
                result.Add(new AssemblyMemberFindingEvidence(
                    factId,
                    instanceKey,
                    evidence.Subject,
                    evidence.State,
                    evidence.AggregateInputs,
                    coordinates,
                    SourceDocument: null,
                    NodeIds: [],
                    callee.Failure
                        ?? "The callee source document was unavailable."));
                continue;
            }

            (int[] NodeIds, string? Failure) correspondence =
                FindEvidenceNodes(callee.Document, coordinates);
            result.Add(new AssemblyMemberFindingEvidence(
                factId,
                instanceKey,
                evidence.Subject,
                evidence.State,
                evidence.AggregateInputs,
                coordinates,
                callee.Document,
                correspondence.Failure is null
                    ? correspondence.NodeIds
                    : [],
                correspondence.Failure));
        }
        return result;
    }

    static void ValidateMethodEvidence(
        string descriptor,
        ResearchFindingEvidence evidence)
    {
        if (evidence.State != ResearchFindingEvidenceState.Method
            || evidence.Locations.Length != 1
            || evidence.AggregateInputs.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                $"Method-level Finding '{descriptor}' carried incomplete aggregate evidence.");
        }

        ResearchEvidenceLocation location = evidence.Locations[0];
        if (location.Admit(evidence.Subject)
            is ResearchEvidenceLocationAdmission.Rejected rejected)
        {
            throw new InvalidOperationException(
                $"Callee evidence location for '{descriptor}' names "
                    + $"'{rejected.Location.Method}' instead of "
                    + $"'{rejected.ExpectedMethod}'.");
        }
        if (location.ILOffset is not null)
        {
            throw new InvalidOperationException(
                $"Method-level Finding '{descriptor}' carried an instruction location.");
        }
    }

    static IReadOnlyList<AssemblyMemberCalleeEvidenceCoordinate> EvidenceCoordinates(
        string descriptor,
        ResearchFindingEvidence evidence,
        ResearchAssemblyContext assembly)
    {
        var coordinates =
            new List<AssemblyMemberCalleeEvidenceCoordinate>(
                evidence.Locations.Length);
        foreach (ResearchEvidenceLocation location in evidence.Locations)
        {
            if (location.Admit(evidence.Subject)
                is ResearchEvidenceLocationAdmission.Rejected rejected)
            {
                throw new InvalidOperationException(
                    $"Callee evidence location for '{descriptor}' names "
                        + $"'{rejected.Location.Method}' instead of "
                        + $"'{rejected.ExpectedMethod}'.");
            }
            if (location.ILOffset is not int)
            {
                throw new InvalidOperationException(
                    $"Instruction-level Finding '{descriptor}' carried a method-only location.");
            }

            CallSiteEvidenceKind kind = descriptor switch
            {
                "semantics.callee" =>
                    CallSiteEvidenceKind.ExceptionConstruction,
                "safety.callee" => SafetyEvidenceKind(
                    assembly,
                    evidence.Subject,
                    location),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor,
                    "Unsupported callee evidence descriptor."),
            };
            coordinates.Add(new AssemblyMemberCalleeEvidenceCoordinate(
                location,
                kind));
        }
        return coordinates;
    }

    static CallSiteEvidenceKind SafetyEvidenceKind(
        ResearchAssemblyContext assembly,
        MethodIdentity subject,
        ResearchEvidenceLocation location)
    {
        CallSiteEvidenceKind[] kinds =
        [
            .. assembly.UnsafeEvidenceByToken
                .GetValueOrDefault(subject.MetadataToken, [])
                .Where(item =>
                    item.ILOffset is int offset
                    && ResearchEvidenceLocation.ForInstruction(
                        item.Member,
                        offset) == location)
                .Select(SafetyEvidenceKind)
                .Where(static kind => kind is not null)
                .Select(static kind => kind!.Value)
                .Distinct(),
        ];
        return kinds.Length == 1
            ? kinds[0]
            : throw new InvalidOperationException(
                $"Safety evidence at IL_{location.ILOffset:X4} in "
                    + $"'{subject}' mapped to {kinds.Length} supported evidence kinds.");
    }

    static CallSiteEvidenceKind? SafetyEvidenceKind(UnsafeEvidence evidence) =>
        (evidence.Detail, evidence.Kind) switch
        {
            ("localloc", "opcode") => CallSiteEvidenceKind.Localloc,
            (_, "calli") => CallSiteEvidenceKind.Calli,
            _ => null,
        };

    static CalleeSourceProjection ProjectCalleeSource(
        MetadataSource source,
        ResearchAssemblyContext assembly,
        MethodIdentity callee,
        PrinterOptions? printerOptions,
        IDictionary<MethodIdentity, CalleeSourceProjection> cache)
    {
        if (cache.TryGetValue(callee, out CalleeSourceProjection? existing))
            return existing;

        ResearchViews.MemberProjectionResult projected =
            ResearchViews.ProjectMember(
                new ResearchViews.MemberProjectionRequest(
                    source,
                    callee.DeclaringType.ToQualifiedDisplayString(),
                    callee.Name,
                    MethodToken: callee.MetadataToken,
                    PrinterOptions: printerOptions,
                    SourceDocument: true,
                    Assembly: assembly));
        var created = new CalleeSourceProjection(
            projected.SourceDocument,
            projected.SourceDocumentFailure?.Diagnostics.Count > 0
                ? string.Join(
                    "; ",
                    projected.SourceDocumentFailure.Diagnostics.Select(
                        diagnostic => diagnostic.ToString()))
                : projected.SourceDocument is null
                    ? "The callee source document was unavailable."
                    : null);
        cache.Add(callee, created);
        return created;
    }

    internal static (int[] NodeIds, string? Failure) FindEvidenceNodes(
        AnnotatedSourceDocument document,
        IReadOnlyList<AssemblyMemberCalleeEvidenceCoordinate> coordinates)
    {
        var nodeIds = new List<int>();
        foreach (AssemblyMemberCalleeEvidenceCoordinate coordinate in coordinates)
        {
            int ilOffset = coordinate.Location.ILOffset
                ?? throw new InvalidOperationException(
                    "Callee evidence correspondence requires an instruction offset.");
            string expectedKind = EvidenceNodeKind(coordinate.Kind);
            AnnotatedSourceNode[] matches =
            [
                .. document.Nodes.Where(node =>
                    node.Medium == SourceLineKind.CSharp
                    && string.Equals(
                        node.Kind,
                        expectedKind,
                        StringComparison.Ordinal)
                    && node.Provenance?.IlOffsets.Contains(ilOffset) == true),
            ];
            if (matches.Length != 1)
            {
                return (
                    [],
                    $"Callee evidence at IL_{ilOffset:X4} matched "
                        + $"{matches.Length} product-issued {expectedKind} nodes.");
            }
            nodeIds.Add(matches[0].Id);
        }
        return ([.. nodeIds.Distinct().Order()], null);
    }

    static string EvidenceNodeKind(CallSiteEvidenceKind kind) =>
        kind switch
        {
            CallSiteEvidenceKind.ExceptionConstruction =>
                "ObjectCreationExpression",
            CallSiteEvidenceKind.Localloc => "StackAllocationExpression",
            CallSiteEvidenceKind.Calli => "IndirectInvocationExpression",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    sealed record CalleeSourceProjection(
        AnnotatedSourceDocument? Document,
        string? Failure);

    sealed record CallRelationshipProjection(
        CallGraphProjection Graph,
        ImmutableArray<AnnotatedCallGraphMappedCall> Calls,
        AnnotatedCallGraphCycleInspection? Cycles);

    static CallRelationshipProjection? ProjectCallRelationships(
        LibraryBodyIndex index,
        int callerToken,
        bool includeCycles)
    {
        index.GetDirectCallsByEvidenceMethod()
            .TryGetValue(callerToken, out ImmutableArray<DirectCall> callArray);
        DirectCall[] calls = callArray.IsDefault ? [] : [.. callArray];
        CallTreeNode? exactCalleeRoot = index.BuildCallTree(
            callerToken,
            maxDepth: 1,
            maxNodes: calls.Length == int.MaxValue
                ? int.MaxValue
                : calls.Length + 1);
        if (exactCalleeRoot is null)
            return null;

        CallGraphProjection graph;
        AnnotatedCallGraphCycleInspection? cycles = null;
        if (includeCycles)
        {
            const int CycleDepth = 3;
            const int CycleMaxNodes = 25;
            CallTreeNode boundedCalleeRoot = index.BuildCallTree(
                callerToken,
                CycleDepth,
                CycleMaxNodes);
            CallTreeNode calleeRoot = PreserveExactFocusNeighborhood(
                exactCalleeRoot,
                boundedCalleeRoot);
            CallTreeNode callerRoot = index.BuildCallerTree(
                callerToken,
                CycleDepth,
                CycleMaxNodes);
            var graphView = new MemberCallGraphView(
                CallGraphTier.Callers,
                calleeRoot,
                callerRoot)
            {
                FocusModuleVersionId =
                    index.ModuleIdentity.ModuleVersionId,
                FocusMethodToken = callerToken,
                FocusCallSites = [.. calls],
            };
            graph = CallGraphProjection.Create(
                graphView.CallerRoot,
                graphView.CalleeRoot);
            cycles = CallGraphCycleFindings.Inspect(
                graphView,
                graph);
        }
        else
        {
            graph = CallGraphProjection.FromCallees(
                exactCalleeRoot);
        }
        (
            ImmutableArray<AnnotatedCallGraphMappedCall> mapped,
            string? failure) =
            AnnotatedCallGraphOccurrenceProjection.MapCalls(
                graph,
                calls,
                omitNotProjected: false);
        if (failure is not null)
            throw new InvalidOperationException(failure);
        return new(graph, mapped, cycles);
    }

    static CallTreeNode PreserveExactFocusNeighborhood(
        CallTreeNode exactRoot,
        CallTreeNode boundedRoot)
    {
        Dictionary<GraphNodeIdentity, CallTreeNode> boundedChildren =
            boundedRoot.Children.ToDictionary(
                static child =>
                    child.GraphEvidence?.Identity
                        ?? GraphNodeIdentity.FromMember(
                            child.Member));
        if (boundedChildren.Count == exactRoot.Children.Length
            && exactRoot.Children.All(child =>
                boundedChildren.ContainsKey(
                    child.GraphEvidence?.Identity
                        ?? GraphNodeIdentity.FromMember(
                            child.Member))))
        {
            return boundedRoot;
        }

        ImmutableArray<CallTreeNode> children =
        [
            .. exactRoot.Children.Select(child =>
            {
                GraphNodeIdentity identity =
                    child.GraphEvidence?.Identity
                        ?? GraphNodeIdentity.FromMember(
                            child.Member);
                return boundedChildren.GetValueOrDefault(
                    identity,
                    child);
            }),
        ];
        return boundedRoot with
        {
            Status = exactRoot.Status,
            Children = children,
        };
    }

    static IReadOnlyList<AssemblyMemberInvocationDestination> ProjectInvocationDestinations(
        CallRelationshipProjection relationships,
        AnnotatedSourceDocument document)
    {
        return
        [
            .. relationships.Calls
                .Select(mapped => (
                    Node: InnermostInvocationNodeAtOffset(
                        document,
                        mapped.Call.ILOffset),
                    Target: FindCallee(
                        relationships.Graph,
                        mapped.Call)))
                .Where(pair => pair.Node is not null && pair.Target is not null)
                .Select(pair => (Node: pair.Node!, Target: pair.Target!))
                .GroupBy(pair => pair.Node.Id)
                .Select(group => new
                {
                    NodeId = group.Key,
                    Targets = DistinctInvocationTargets(
                        group.Select(pair => pair.Target)),
                })
                .Where(group => group.Targets.Length == 1)
                .Select(group => new AssemblyMemberInvocationDestination(
                    group.NodeId,
                    group.Targets[0])),
        ];
    }

    static AssemblyMemberCallRelationshipOverlay
        ProjectCallRelationshipOverlay(
            CallRelationshipProjection relationships,
            AnnotatedSourceDocument document)
    {
        (
            ImmutableArray<AnnotatedCallGraphOccurrence> occurrences,
            string? failure) =
            AnnotatedCallGraphOccurrenceProjection.MapFacts(
                relationships.Calls,
                document);
        if (failure is not null)
            throw new InvalidOperationException(failure);

        var rows =
            new AssemblyMemberCallRelationship[occurrences.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            DirectCall call = relationships.Calls[index].Call;
            if (relationships.Graph.FindFocusCalleeTarget(
                    call,
                    out CallGraphNode target)
                != CallGraphRowMatch.Found)
            {
                throw new InvalidOperationException(
                    $"Call site IL_{call.ILOffset:X4} has no stable graph target.");
            }
            rows[index] = new(occurrences[index], target);
        }
        return new(rows);
    }

    static AssemblyMemberCallCycleInspection ProjectCallCycles(
        CallRelationshipProjection relationships,
        AssemblyMemberCallRelationshipOverlay relationshipOverlay)
    {
        AnnotatedCallGraphCycleInspection cycles =
            relationships.Cycles
                ?? throw new InvalidOperationException(
                    "Call cycle projection produced no cycle inspection.");
        Dictionary<int, CallGraphRow> graphRows =
            relationships.Graph.Rows.ToDictionary(
                static row => row.Number);
        var findings =
            new AssemblyMemberCallCycle[cycles.Findings.Length];
        for (int index = 0; index < findings.Length; index++)
        {
            Finding<CallGraphCycleWitness> finding =
                cycles.Findings[index];
            int ordinal = finding.Ordinal
                ?? throw new InvalidOperationException(
                    "A call cycle Finding carries no ordinal.");
            int firstEdgeRow =
                finding.Payload.EdgeRows[0];
            int[] factIds =
            [
                .. relationshipOverlay.Relationships
                    .Where(relationship =>
                        relationship.Occurrence.EdgeRow
                            == firstEdgeRow)
                    .Select(relationship =>
                        relationship.Occurrence.FactId),
            ];
            if (factIds.Length == 0)
            {
                throw new InvalidOperationException(
                    "A focus cycle does not begin at one source-targeted call relationship.");
            }
            CallGraphNode[] targets =
            [
                .. finding.Payload.EdgeRows.Select(edgeRow =>
                {
                    if (!graphRows.TryGetValue(
                            edgeRow,
                            out CallGraphRow row))
                    {
                        throw new InvalidOperationException(
                            $"A call cycle names missing edge row {edgeRow}.");
                    }
                    return relationships.Graph.Nodes[
                        row.Edge.To];
                }),
            ];
            findings[index] = new AssemblyMemberCallCycle(
                finding.Key,
                ordinal,
                finding.Payload.EdgeRows,
                factIds,
                targets);
        }
        return new(findings, cycles.Limits);
    }

    static CallGraphNode? FindCallee(
        CallGraphProjection graph,
        DirectCall call) =>
        graph.FindFocusCalleeTarget(call, out CallGraphNode target)
            == CallGraphRowMatch.Found
            ? target
            : null;

    static CallGraphNode[] DistinctInvocationTargets(
        IEnumerable<CallGraphNode> targets)
    {
        var distinct = new List<CallGraphNode>();
        foreach (CallGraphNode target in targets)
        {
            if (!distinct.Any(candidate =>
                    SameInvocationTarget(candidate, target)))
            {
                distinct.Add(target);
            }
        }
        return [.. distinct];
    }

    static bool SameInvocationTarget(
        CallGraphNode first,
        CallGraphNode second) =>
        first.Identity == second.Identity
        && Equivalent(
            first.DefinitionAssemblyIdentity,
            second.DefinitionAssemblyIdentity)
        && Equivalent(
            first.ResolutionAssemblyIdentity,
            second.ResolutionAssemblyIdentity)
        && Equivalent(
            first.OccurrenceAssemblyIdentity,
            second.OccurrenceAssemblyIdentity);

    static bool Equivalent(
        AssemblyReferenceIdentity? first,
        AssemblyReferenceIdentity? second) =>
        first is null
            ? second is null
            : second is not null && first.IsEquivalentTo(second);

    static AnnotatedSourceNode? InnermostInvocationNodeAtOffset(
        AnnotatedSourceDocument document,
        int ilOffset)
    {
        AnnotatedSourceNode[] containing =
        [
            .. document.Nodes.Where(node =>
                node.Medium == SourceLineKind.CSharp
                && node.Provenance?.IlOffsets.Contains(ilOffset) == true),
        ];
        AnnotatedSourceNode[] innermost =
        [
            .. containing.Where(candidate =>
                !containing.Any(other =>
                    other.Id != candidate.Id
                    && SpansContain(candidate.Spans, other.Spans))),
        ];
        return innermost.Length == 1
            && innermost[0].Kind == "InvocationExpression"
            ? innermost[0]
            : null;
    }

    static bool SpansContain(
        IReadOnlyList<AnnotatedSourceSpan> outer,
        IReadOnlyList<AnnotatedSourceSpan> inner) =>
        inner.All(innerSpan =>
            outer.Any(outerSpan =>
                innerSpan.Start >= outerSpan.Start
                && (long)innerSpan.Start + innerSpan.Length
                    <= (long)outerSpan.Start + outerSpan.Length));
}

/// <summary>
/// Opens the Research inputs for one group participant from workspace-owned content: a
/// <see cref="MetadataSource"/> over the retained immutable image, and reference resolution that
/// answers from the participant's own binding policy.
/// </summary>
internal static class AssemblyContextResearchSource
{
    internal static MetadataSource Open(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        AssemblyContextAnalysisSource.BindingPolicyResolver resolver)
        => MetadataSource.OpenWithoutSymbols(
            snapshot.RetainAssemblyReference(Participant(group, subject).Assembly),
            (IAssemblyReferenceResolver)resolver);

    /// <summary>
    /// The name Analysis and the decompiler label this assembly by. It is a label, not a file:
    /// a participant acquired from content has no path.
    /// </summary>
    internal static string Name(AssemblyContextSubject subject)
        => AssemblyContextAnalysisSource.Name(subject);

    internal static AssemblyContextAnalysisSource.BindingPolicyResolver Resolver(
        AssemblyContextGroup group,
        AssemblyContextSubject subject)
        => AssemblyContextAnalysisSource.Resolver(group, subject);

    static AssemblyContextParticipant Participant(
        AssemblyContextGroup group,
        AssemblyContextSubject subject)
        => group.Participants.Single(
            candidate => ReferenceEquals(
                candidate.Assembly.Registration,
                subject.Registration));
}
