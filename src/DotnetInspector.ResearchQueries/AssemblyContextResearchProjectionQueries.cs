using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
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
/// <param name="SynchronousCompletions">
/// Includes framework-authenticated synchronous task-completion observations
/// joined to their exact physical <c>call.edge</c> Findings.
/// </param>
/// <param name="AwaitCompletionPaths">
/// Includes Decompiler-proven inline and suspension/resume paths for each
/// reconstructed classic <c>await</c>.
/// </param>
/// <param name="AllocationExceptionPaths">
/// Includes Analysis-classified thrown-value and exception-handler paths for
/// exact allocation Findings.
/// </param>
/// <param name="LocalThrowPaths">
/// Includes bounded same-module direct-call paths from the selected MethodDef
/// to methods containing Analysis-proven local throws. This requires exact
/// call relationships and local-throw Analysis.
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
    bool CallCycles = false,
    bool SynchronousCompletions = false,
    bool AwaitCompletionPaths = false,
    bool AllocationExceptionPaths = false,
    bool LocalThrowPaths = false);

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

/// <summary>
/// One exact physical relationship whose framework member synchronously
/// observes task completion.
/// </summary>
public sealed record AssemblyMemberSynchronousCompletion(
    int FactId,
    SynchronousCompletionKind Kind);

/// <summary>
/// One Decompiler-issued classic <c>await</c> node whose inline and
/// suspension/resume paths were proven before reconstruction.
/// </summary>
public sealed record AssemblyMemberAwaitCompletionPath(int NodeId);

/// <summary>
/// One exact allocation Finding Analysis placed on exception-related control
/// flow.
/// </summary>
public sealed record AssemblyMemberAllocationExceptionPath(
    int FactId,
    AllocationExceptionPathKind Kind);

public enum AssemblyMemberLocalThrowPathBoundaryKind
{
    AnalysisIncomplete,
    TraversalBoundary,
    PartialMethodEvidenceScope,
    UnresolvedLocalCalls,
    UnattributedGeneratedBodies,
    DepthLimit,
    NodeBudget,
    EdgeBudget,
    PathBudget,
    IncompleteLocalThrowEvidence,
    IncompleteCorrespondence,
}

public sealed record AssemblyMemberLocalThrowPathBoundary(
    AssemblyMemberLocalThrowPathBoundaryKind Kind,
    int Value);

public sealed record AssemblyMemberLocalThrowSite(
    ILInspector.Analysis.TypeRef ExceptionType,
    MetadataTypeDefinitionAddress Definition,
    int ConstructionOffset,
    int ConstructorToken,
    int ThrowOffset);

public sealed record AssemblyMemberLocalThrowPath(
    IReadOnlyList<int> FactIds,
    IReadOnlyList<CallGraphNode> Targets,
    IReadOnlyList<AssemblyMemberLocalThrowSite> TerminalThrows);

public sealed record AssemblyMemberLocalThrowPathInspection(
    IReadOnlyList<AssemblyMemberLocalThrowPath> Paths,
    IReadOnlyList<AssemblyMemberLocalThrowPathBoundary> Boundaries,
    LibraryBodyRootPathLimits Limits,
    LibraryBodyRootPathReceipt Receipt)
{
    public bool IsComplete => Boundaries.Count == 0;
}

/// <summary>One participant's member projection and any narrowing of its fact context.</summary>
public sealed record AssemblyMemberProjection(
    MemberProjectionResult Projection,
    MemberProjectionContextLimitation? ContextLimitation,
    IReadOnlyList<AssemblyMemberFindingEvidence>? FindingEvidence,
    IReadOnlyList<AssemblyMemberInvocationDestination> InvocationDestinations,
    AssemblyMemberCallRelationshipOverlay? CallRelationships = null,
    AssemblyMemberCallCycleInspection? CallCycles = null,
    IReadOnlyList<AssemblyMemberSynchronousCompletion>?
        SynchronousCompletions = null,
    IReadOnlyList<AssemblyMemberAwaitCompletionPath>?
        AwaitCompletionPaths = null,
    IReadOnlyList<AssemblyMemberAllocationExceptionPath>?
        AllocationExceptionPaths = null,
    AssemblyMemberLocalThrowPathInspection?
        LocalThrowPaths = null);

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
        if (request.SynchronousCompletions
            && !request.CallRelationships)
        {
            throw new ArgumentException(
                "Synchronous completions require exact call relationships.",
                nameof(request));
        }
        if (request.AwaitCompletionPaths
            && (!request.SourceDocument || request.MethodToken is null))
        {
            throw new ArgumentException(
                "Await completion paths require a source document and an exact MethodDef token.",
                nameof(request));
        }
        if (request.AllocationExceptionPaths
            && (!request.SourceDocument
                || !request.AnalysisFeatures.HasFlag(
                    LibraryBodyAnalysisFeatures.Allocations)))
        {
            throw new ArgumentException(
                "Allocation exception paths require a source document and allocation analysis.",
                nameof(request));
        }
        if (request.LocalThrowPaths
            && (!request.CallRelationships
                || !request.AnalysisFeatures.HasFlag(
                    LibraryBodyAnalysisFeatures.LocalThrows)))
        {
            throw new ArgumentException(
                "Local throw paths require exact call relationships and local-throw analysis.",
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
        LibraryBodyAnalysisExecution? execution = null;
        LibraryBodyIndex? index = null;
        MemberProjectionContextLimitation? limitation = null;
        try
        {
            execution = LibraryBodyAnalysisService.ExecuteImage(
                AssemblyContextResearchSource.Name(subject),
                snapshot.Content,
                LibraryBodyAnalysisRequest.Create(
                    request.AnalysisFeatures),
                resolver);
            index = execution.CompatibilityIndex();
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
            MemberProjectionAnalysisInput? analysis =
                execution is null
                    ? null
                    : new(
                        execution.Allocations,
                        execution.Safety,
                        execution.CallGraph,
                        execution.Leverage);
            CallRelationshipProjection? callRelationships =
                index is not null
                    && request.MethodToken is int requestedMethodToken
                    && request.CallRelationships
                    ? ProjectCallRelationships(
                        index,
                        requestedMethodToken,
                        request.CallCycles,
                        request.LocalThrowPaths)
                    : null;
            if (request.CallRelationships
                && index is not null
                && callRelationships is null)
            {
                throw new InvalidOperationException(
                    "Call relationship projection produced no callee topology.");
            }
            MemberProjectionResult projection =
                MemberProjectionProducer.Produce(
                    new MemberProjectionRequest(
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
                        Analysis: analysis,
                        CallSites: request.CallRelationships
                                && callRelationships is not null
                            ? callRelationships.Calls
                                .Select(call => call.Call)
                                .ToArray()
                            : null));
            IReadOnlyList<AssemblyMemberFindingEvidence>? findingEvidence =
                request.FindingEvidence
                    ? assembly is null || analysis is null
                        ? null
                        : ProjectFindingEvidence(
                            source,
                            projection,
                            assembly,
                            analysis,
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
                        includeCycles: false,
                        includeExtendedCallees: false);
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
            IReadOnlyList<AssemblyMemberSynchronousCompletion>?
                synchronousCompletions =
                    request.SynchronousCompletions
                        && callRelationships is not null
                        && relationshipOverlay is not null
                        ? ProjectSynchronousCompletions(
                            callRelationships,
                            relationshipOverlay)
                        : null;
            IReadOnlyList<AssemblyMemberAwaitCompletionPath>?
                awaitCompletionPaths =
                    request.AwaitCompletionPaths
                        && projection.SourceDocument is { } awaitDocument
                        ? ProjectAwaitCompletionPaths(
                            projection,
                            awaitDocument)
                        : null;
            IReadOnlyList<AssemblyMemberAllocationExceptionPath>?
                allocationExceptionPaths =
                    request.AllocationExceptionPaths
                        && assembly is not null
                        && projection.SourceDocument is not null
                        ? ProjectAllocationExceptionPaths(
                            projection)
                        : null;
            AssemblyMemberLocalThrowPathInspection? localThrowPaths =
                request.LocalThrowPaths
                    && index is not null
                    && request.MethodToken is int localThrowRootToken
                    && callRelationships is not null
                    && relationshipOverlay is not null
                    ? ProjectLocalThrowPaths(
                        index,
                        localThrowRootToken,
                        callRelationships,
                        relationshipOverlay)
                    : null;
            var result = new AssemblyMemberProjection(
                projection,
                limitation,
                findingEvidence,
                destinations,
                relationshipOverlay,
                cycleInspection,
                synchronousCompletions,
                awaitCompletionPaths,
                allocationExceptionPaths,
                localThrowPaths);
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
        MemberProjectionResult projection,
        ResearchAssemblyContext assembly,
        MemberProjectionAnalysisInput analysis,
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
        foreach (FactRow fact in facts)
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
                analysis,
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
        MemberProjectionAnalysisInput analysis,
        MethodIdentity callee,
        PrinterOptions? printerOptions,
        IDictionary<MethodIdentity, CalleeSourceProjection> cache)
    {
        if (cache.TryGetValue(callee, out CalleeSourceProjection? existing))
            return existing;

        MemberProjectionResult projected =
            MemberProjectionProducer.Produce(
                new MemberProjectionRequest(
                    source,
                    callee.DeclaringType.ToQualifiedDisplayString(),
                    callee.Name,
                    MethodToken: callee.MetadataToken,
                    PrinterOptions: printerOptions,
                    SourceDocument: true,
                    Analysis: analysis));
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

    const int ExtendedCalleeDepth = 3;
    const int ExtendedCalleeNodes = 25;

    static CallRelationshipProjection? ProjectCallRelationships(
        LibraryBodyIndex index,
        int callerToken,
        bool includeCycles,
        bool includeExtendedCallees)
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
        if (includeCycles || includeExtendedCallees)
        {
            CallTreeNode boundedCalleeRoot = index.BuildCallTree(
                callerToken,
                ExtendedCalleeDepth,
                ExtendedCalleeNodes);
            CallTreeNode calleeRoot = PreserveExactFocusNeighborhood(
                exactCalleeRoot,
                boundedCalleeRoot);
            if (includeCycles)
            {
                CallTreeNode callerRoot = index.BuildCallerTree(
                    callerToken,
                    ExtendedCalleeDepth,
                    ExtendedCalleeNodes);
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
                graph = CallGraphProjection.FromCallees(calleeRoot);
            }
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
        var findings = new List<AssemblyMemberCallCycle>(
            cycles.Findings.Length);
        AnnotatedCallGraphCycleLimit limits = cycles.Limits;
        foreach (Finding<CallGraphCycleWitness> finding in cycles.Findings)
        {
            _ = finding.Ordinal
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
                limits |=
                    AnnotatedCallGraphCycleLimit
                        .IncompleteCorrespondence;
                continue;
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
            findings.Add(new AssemblyMemberCallCycle(
                finding.Key,
                findings.Count,
                finding.Payload.EdgeRows,
                factIds,
                targets));
        }
        return new(findings, limits);
    }

    static AssemblyMemberLocalThrowPathInspection ProjectLocalThrowPaths(
        LibraryBodyIndex index,
        int rootToken,
        CallRelationshipProjection relationships,
        AssemblyMemberCallRelationshipOverlay relationshipOverlay)
    {
        var limits = new LibraryBodyRootPathLimits(
            MaximumDepth: ExtendedCalleeDepth,
            MaximumNodes: ExtendedCalleeNodes,
            MaximumEdges: 100,
            MaximumPaths: 25);
        MethodIdentity root = index.DeclaredMethods.Single(
            method => method.MetadataToken == rootToken);
        Dictionary<int, MethodIdentity> methodsByToken =
            index.DeclaredMethods.ToDictionary(
                static method => method.MetadataToken);
        HashSet<int> outboundNodeIds =
            OutboundNodeIds(
                relationships.Graph,
                ExtendedCalleeDepth);
        bool IsProjectedLocalMethod(int methodToken) =>
            methodsByToken.TryGetValue(
                methodToken,
                out MethodIdentity? method)
            && relationships.Graph.FindNode(
                    method,
                    out CallGraphNode node)
                == CallGraphNodeMatch.Found
            && outboundNodeIds.Contains(node.Id);
        Dictionary<int, ImmutableArray<LocalThrowSite>> knownThrows =
            index.LocalThrows
                .OfType<MethodLocalThrowEvidence.Inspected>()
                .Select(evidence => (
                    evidence.MethodToken,
                    Sites: evidence.Sites
                        .Where(site =>
                            site.Kind == LocalThrowInstructionKind.Throw
                            && site.Type is LocalThrowTypeEvidence.Known)
                        .ToImmutableArray()))
                .Where(entry =>
                    !entry.Sites.IsEmpty
                    && IsProjectedLocalMethod(entry.MethodToken))
                .ToDictionary(
                    static entry => entry.MethodToken,
                    static entry => entry.Sites);
        int incompleteThrowEvidence = index.LocalThrows.Count(evidence =>
            IsProjectedLocalMethod(evidence.MethodToken)
            && (evidence switch
                {
                    MethodLocalThrowEvidence.Inspected inspected =>
                        !inspected.IsComplete,
                    MethodLocalThrowEvidence.Unavailable unavailable =>
                        unavailable.Reason
                            is not LocalThrowUnavailableReason.NoManagedBody,
                    _ => true,
                }));

        MetadataMethodAddress rootAddress = MethodAddress(root);
        ImmutableArray<MetadataMethodAddress> destinations =
        [
            .. knownThrows.Keys
                .Where(token => token != rootToken)
                .Order()
                .Select(token => MethodAddress(
                    methodsByToken[token])),
        ];
        IReadOnlyList<LibraryBodyRootPathWitness> witnesses;
        LibraryBodyRootPathReceipt receipt;
        List<AssemblyMemberLocalThrowPathBoundary> boundaries;
        if (destinations.IsEmpty)
        {
            witnesses = [];
            receipt = new LibraryBodyRootPathReceipt(
                RequestedRoots: 1,
                RequestedDestinations: 0,
                DestinationSearches: 0,
                SearchNodes: 0,
                SearchedEdges: 0,
                ObservedReachablePairs: 0,
                ReturnedPaths: 0);
            boundaries = [];
            if (!index.HasFullMethodEvidenceScope)
            {
                boundaries.Add(
                    new AssemblyMemberLocalThrowPathBoundary(
                        AssemblyMemberLocalThrowPathBoundaryKind
                            .PartialMethodEvidenceScope,
                        1));
            }
            if (!index.Diagnostics.IsEmpty)
            {
                boundaries.Add(
                    new AssemblyMemberLocalThrowPathBoundary(
                        AssemblyMemberLocalThrowPathBoundaryKind
                            .AnalysisIncomplete,
                        index.Diagnostics.Length));
            }
        }
        else
        {
            LibraryBodyRootPathResult search =
                LibraryBodyRootPathAnalysis.FindShortestPaths(
                index.CallGraphAnalysis,
                [rootAddress],
                destinations,
                limits);
            witnesses = search.Witnesses;
            receipt = search.Receipt;
            boundaries =
                search.Boundaries
                    .Select(ProjectLocalThrowPathBoundary)
                    .ToList();
        }
        Dictionary<
            (Guid ModuleVersionId, int CallerToken, int ILOffset, int OperandToken),
            int> factsByCall =
            relationshipOverlay.Relationships.ToDictionary(
                static relationship => (
                    relationship.Occurrence.ModuleVersionId,
                    relationship.Occurrence.CallerToken,
                    relationship.Occurrence.ILOffset,
                    relationship.Occurrence.OperandToken),
                static relationship =>
                    relationship.Occurrence.FactId);

        var paths = new List<AssemblyMemberLocalThrowPath>();
        if (relationships.Graph.HasUnexploredTraversalBoundary)
        {
            boundaries.Add(
                new AssemblyMemberLocalThrowPathBoundary(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .TraversalBoundary,
                    1));
        }
        if (relationships.Graph.HasAnalysisFailureBoundary
            && boundaries.All(boundary =>
                boundary.Kind
                    != AssemblyMemberLocalThrowPathBoundaryKind
                        .AnalysisIncomplete))
        {
            boundaries.Add(
                new AssemblyMemberLocalThrowPathBoundary(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .AnalysisIncomplete,
                    1));
        }
        if (incompleteThrowEvidence > 0)
        {
            boundaries.Add(
                new AssemblyMemberLocalThrowPathBoundary(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .IncompleteLocalThrowEvidence,
                    incompleteThrowEvidence));
        }
        int incompleteCorrespondence = 0;
        foreach (LibraryBodyRootPathWitness witness in witnesses)
        {
            if (!knownThrows.TryGetValue(
                    witness.Destination.MetadataToken,
                    out ImmutableArray<LocalThrowSite> terminalSites))
            {
                continue;
            }

            LibraryBodyRootPathStep first = witness.Steps[0];
            int[] factIds =
            [
                .. first.CallSites
                    .Select(call => factsByCall.GetValueOrDefault((
                        call.EvidenceMethod.ModuleVersionId,
                        call.EvidenceMethod.MetadataToken,
                        call.ILOffset,
                        call.OperandToken), -1))
                    .Where(static factId => factId >= 0)
                    .Distinct()
                    .Order(),
            ];
            incompleteCorrespondence +=
                first.CallSites.Length - factIds.Length;
            if (factIds.Length == 0)
            {
                continue;
            }

            var targets = new List<CallGraphNode>(witness.Steps.Length);
            bool completeTargets = true;
            foreach (LibraryBodyRootPathStep step in witness.Steps)
            {
                if (relationships.Graph.FindNode(
                        step.Callee,
                        out CallGraphNode target)
                    != CallGraphNodeMatch.Found)
                {
                    completeTargets = false;
                    incompleteCorrespondence++;
                    break;
                }
                targets.Add(target);
            }
            if (!completeTargets)
                continue;

            paths.Add(
                new AssemblyMemberLocalThrowPath(
                    factIds,
                    targets,
                    [
                        .. terminalSites.Select(site =>
                        {
                            var known =
                                (LocalThrowTypeEvidence.Known)site.Type;
                            return new AssemblyMemberLocalThrowSite(
                                known.ExceptionType,
                                known.Definition,
                                known.ConstructionOffset,
                                known.ConstructorToken,
                                site.ILOffset);
                        }),
                    ]));
        }
        if (incompleteCorrespondence > 0)
        {
            boundaries.Add(
                new AssemblyMemberLocalThrowPathBoundary(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .IncompleteCorrespondence,
                    incompleteCorrespondence));
        }

        return new(
            paths,
            boundaries,
            limits,
            receipt with { ReturnedPaths = paths.Count });
    }

    static HashSet<int> OutboundNodeIds(
        CallGraphProjection graph,
        int maximumDepth)
    {
        HashSet<int> result = [graph.Focus.Id];
        HashSet<int> frontier = [graph.Focus.Id];
        for (int depth = 0;
            depth < maximumDepth && frontier.Count > 0;
            depth++)
        {
            HashSet<int> next =
            [
                .. graph.Rows
                    .Where(row => frontier.Contains(row.Edge.From))
                    .Select(static row => row.Edge.To)
                    .Where(result.Add),
            ];
            frontier = next;
        }
        return result;
    }

    static AssemblyMemberLocalThrowPathBoundary
        ProjectLocalThrowPathBoundary(
            LibraryBodyRootPathBoundary boundary) =>
        boundary switch
        {
            LibraryBodyRootPathBoundary.AnalysisIncomplete value =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .AnalysisIncomplete,
                    value.DiagnosticCount),
            LibraryBodyRootPathBoundary.PartialMethodEvidenceScope =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .PartialMethodEvidenceScope,
                    1),
            LibraryBodyRootPathBoundary.UnresolvedLocalCalls value =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .UnresolvedLocalCalls,
                    value.Count),
            LibraryBodyRootPathBoundary.UnattributedGeneratedBodies value =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind
                        .UnattributedGeneratedBodies,
                    value.Count),
            LibraryBodyRootPathBoundary.DepthLimit value =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind.DepthLimit,
                    value.MaximumDepth),
            LibraryBodyRootPathBoundary.NodeBudget value =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind.NodeBudget,
                    value.MaximumNodes),
            LibraryBodyRootPathBoundary.EdgeBudget value =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind.EdgeBudget,
                    value.MaximumEdges),
            LibraryBodyRootPathBoundary.PathBudget value =>
                new(
                    AssemblyMemberLocalThrowPathBoundaryKind.PathBudget,
                    value.MaximumPaths),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary)),
        };

    static MetadataMethodAddress MethodAddress(MethodIdentity method) =>
        new(
            method.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(
                method.MetadataToken & 0x00FFFFFF));

    static IReadOnlyList<AssemblyMemberSynchronousCompletion>
        ProjectSynchronousCompletions(
            CallRelationshipProjection relationships,
            AssemblyMemberCallRelationshipOverlay relationshipOverlay)
    {
        if (relationships.Calls.Length
            != relationshipOverlay.Relationships.Count)
        {
            throw new InvalidOperationException(
                "Synchronous completion projection requires one relationship for every physical call.");
        }

        var factIdsByCall =
            new Dictionary<(Guid ModuleVersionId, int CallerToken, int ILOffset, int OperandToken), int>();
        for (int index = 0; index < relationships.Calls.Length; index++)
        {
            DirectCall call = relationships.Calls[index].Call;
            factIdsByCall.Add(
                (
                    call.EvidenceMethod.ModuleVersionId,
                    call.EvidenceMethod.MetadataToken,
                    call.ILOffset,
                    call.OperandToken),
                relationshipOverlay.Relationships[index]
                    .Occurrence.FactId);
        }
        return
        [
            .. SynchronousCompletionAnalysis.Inspect(
                    relationships.Calls.Select(static call =>
                        call.Call))
                .Select(observation =>
                {
                    DirectCall call = observation.Call;
                    return new AssemblyMemberSynchronousCompletion(
                        factIdsByCall[(
                            call.EvidenceMethod.ModuleVersionId,
                            call.EvidenceMethod.MetadataToken,
                            call.ILOffset,
                            call.OperandToken)],
                        observation.Kind);
                }),
        ];
    }

    static IReadOnlyList<AssemblyMemberAwaitCompletionPath>
        ProjectAwaitCompletionPaths(
            MemberProjectionResult projection,
            AnnotatedSourceDocument document)
    {
        return
        [
            .. (projection.AwaitCompletionPathNodeIds ?? [])
                .Select(nodeId =>
                {
                    AnnotatedSourceNode node = document.Nodes[nodeId];
                    if (node.Medium != SourceLineKind.CSharp
                        || !string.Equals(
                            node.Kind,
                            AnnotatedSourceNodeKinds.AwaitExpression,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Classic await completion path names non-await node {nodeId}.");
                    }
                    return new AssemblyMemberAwaitCompletionPath(nodeId);
                }),
        ];
    }

    static IReadOnlyList<AssemblyMemberAllocationExceptionPath>
        ProjectAllocationExceptionPaths(
            MemberProjectionResult projection)
        =>
        [
            .. (projection.AllocationExceptionPaths ?? [])
                .Select(static path =>
                    new AssemblyMemberAllocationExceptionPath(
                        path.FactId,
                        path.Kind)),
        ];

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
