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
    LibraryBodyAnalysisFeatures AnalysisFeatures = LibraryBodyAnalysisFeatures.Default);

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
/// One exact Finding instance joined to its physical callee document and source nodes.
/// </summary>
public sealed record AssemblyMemberFindingEvidence(
    int FactId,
    FindingInstanceKey InstanceKey,
    MethodIdentity Member,
    IReadOnlyList<AssemblyMemberCalleeEvidenceCoordinate> Coordinates,
    AnnotatedSourceDocument? SourceDocument,
    IReadOnlyList<int> NodeIds,
    string? UnavailableReason);

/// <summary>One participant's member projection and any narrowing of its fact context.</summary>
public sealed record AssemblyMemberProjection(
    ResearchViews.MemberProjectionResult Projection,
    MemberProjectionContextLimitation? ContextLimitation,
    IReadOnlyList<AssemblyMemberFindingEvidence>? FindingEvidence,
    IReadOnlyList<AssemblyMemberInvocationDestination> InvocationDestinations);

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
                        Registry: null,
                        request.MethodToken,
                        request.PrinterOptions,
                        CaretFocus: null,
                        request.SourceDocument,
                        assembly));
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
            IReadOnlyList<AssemblyMemberInvocationDestination> destinations =
                request.InvocationDestinations
                    && index is not null
                    && projection.SourceDocument is { } document
                    && projection.SelectedMethodToken is { } methodToken
                    ? ProjectInvocationDestinations(index, methodToken, document)
                    : [];
            var result = new AssemblyMemberProjection(
                projection,
                limitation,
                findingEvidence,
                destinations);
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
            if (fact.Id is not ("semantics.callee" or "safety.callee")
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
            if (evidence.State == ResearchFindingEvidenceState.Method)
            {
                throw new InvalidOperationException(
                    $"Instruction-level Finding '{fact.Id}' carried method-only evidence.");
            }

            IReadOnlyList<AssemblyMemberCalleeEvidenceCoordinate> coordinates =
                EvidenceCoordinates(fact.Id, evidence, assembly);
            if (evidence.State == ResearchFindingEvidenceState.InstructionUnavailable)
            {
                result.Add(new AssemblyMemberFindingEvidence(
                    factId,
                    instanceKey,
                    evidence.Subject,
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
                coordinates,
                callee.Document,
                correspondence.Failure is null
                    ? correspondence.NodeIds
                    : [],
                correspondence.Failure));
        }
        return result;
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

    static IReadOnlyList<AssemblyMemberInvocationDestination> ProjectInvocationDestinations(
        LibraryBodyIndex index,
        int callerToken,
        AnnotatedSourceDocument document)
    {
        index.GetDirectCallsByEvidenceMethod()
            .TryGetValue(callerToken, out ImmutableArray<DirectCall> callArray);
        DirectCall[] calls = callArray.IsDefault ? [] : [.. callArray];
        if (calls.Length == 0)
            return [];

        CallTreeNode? calleeRoot = index.BuildCallTree(
            callerToken,
            maxDepth: 1,
            maxNodes: calls.Length == int.MaxValue
                ? int.MaxValue
                : calls.Length + 1);
        if (calleeRoot is null)
            return [];

        CallGraphProjection graph = CallGraphProjection.FromCallees(calleeRoot);
        return
        [
            .. calls
                .Select(call => (
                    Node: InnermostInvocationNodeAtOffset(document, call.ILOffset),
                    Target: FindCallee(graph, call)))
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
