using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// One body index and its acquisition-owner-issued assembly descriptor in a
/// catalog call-graph scope.
/// </summary>
public sealed class CatalogCallGraphParticipant
{
    public CatalogCallGraphParticipant(
        LibraryBodyIndex index,
        ResolvedAssemblyReference assembly)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(assembly);
        Index = index;
        Assembly = assembly;
    }

    public LibraryBodyIndex Index { get; }
    public ResolvedAssemblyReference Assembly { get; }
}

/// <summary>One retained physical call edge in a catalog call-graph scope.</summary>
public sealed class GraphEdgeEvidence
{
    internal GraphEdgeEvidence(
        GraphNodeEvidence caller,
        GraphNodeEvidence callee,
        CallKind kind,
        bool inLoop)
    {
        Caller = caller;
        Callee = callee;
        Kind = kind;
        InLoop = inLoop;
    }

    public GraphNodeEvidence Caller { get; }
    public GraphNodeEvidence Callee { get; }
    public CallKind Kind { get; }
    public bool InLoop { get; }
}

/// <summary>
/// One physical call site whose exact selected definition belongs to a
/// different identity of the graph's primary assembly.
/// </summary>
public sealed class GraphBindingIdentityConflictEvidence
{
    internal GraphBindingIdentityConflictEvidence(
        GraphNodeEvidence callSite,
        AssemblyReferenceIdentity requested,
        AssemblyReferenceIdentity selected,
        AssemblyReferenceIdentity primary)
    {
        CallSite = callSite;
        Requested = requested;
        Selected = selected;
        Primary = primary;
    }

    public GraphNodeEvidence CallSite { get; }
    public AssemblyReferenceIdentity Requested { get; }
    public AssemblyReferenceIdentity Selected { get; }
    public AssemblyReferenceIdentity Primary { get; }
}

/// <summary>
/// Stable counts of graph evidence that could not establish complete catalog
/// correspondence.
/// </summary>
public sealed record CatalogCallGraphDiagnostics(
    int IncompleteNodeCount,
    int IncompleteEdgeCount,
    int BindingIdentityConflictCount)
{
    public static CatalogCallGraphDiagnostics Empty { get; } =
        new(0, 0, 0);

    public bool IsIncomplete =>
        IncompleteNodeCount > 0
        || IncompleteEdgeCount > 0
        || BindingIdentityConflictCount > 0;
}

/// <summary>
/// Catalog-owned identity and storage domain for one fixed assembly group.
/// Signature plans, resolution, physical graph storage, and both traversal
/// directions are built once and reused until the scope is released. The
/// first distinct participant is the primary assembly for identity-conflict
/// diagnostics.
/// </summary>
public sealed class CatalogCallGraphScope : IDisposable
{
    readonly ImmutableArray<CatalogCallGraphParticipant> _participants;
    readonly Dictionary<LibraryBodyIndex, CatalogCallGraphParticipant>
        _participantByIndex =
            new(ReferenceEqualityComparer.Instance);
    readonly IAssemblyBindingPolicy _bindingPolicy;
    readonly TypeResolutionCatalog _catalog;
    ScopeGraph? _graph;
    bool _disposed;

    public CatalogCallGraphScope(
        IAssemblyBindingPolicy bindingPolicy,
        IEnumerable<CatalogCallGraphParticipant> participants,
        TypeResolutionContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(participants);
        _bindingPolicy = bindingPolicy;
        _catalog = new TypeResolutionCatalog(options);

        var builder =
            ImmutableArray.CreateBuilder<CatalogCallGraphParticipant>();
        var registrations = new HashSet<AssemblyAcquisitionRegistration>(
            ReferenceEqualityComparer.Instance);
        var artifacts = new Dictionary<
            (AssemblyReferenceIdentity Identity, Guid ModuleVersionId),
            CatalogCallGraphParticipant>();
        foreach (CatalogCallGraphParticipant participant in participants)
        {
            ArgumentNullException.ThrowIfNull(participant);
            ValidateParticipant(participant);
            var artifact = (
                participant.Assembly.Identity,
                ModuleVersionId(participant.Index));
            if (artifacts.TryGetValue(
                    artifact,
                    out CatalogCallGraphParticipant? canonical))
            {
                if (_participantByIndex.TryGetValue(
                        participant.Index,
                        out CatalogCallGraphParticipant? mapped)
                    && !ReferenceEquals(mapped, canonical))
                {
                    throw new ArgumentException(
                        "A body index cannot describe two physical artifacts.",
                        nameof(participants));
                }

                _participantByIndex.TryAdd(participant.Index, canonical);
                continue;
            }

            if (_participantByIndex.ContainsKey(participant.Index))
            {
                throw new ArgumentException(
                    "A body index cannot describe two physical artifacts.",
                    nameof(participants));
            }
            if (!registrations.Add(participant.Assembly.Registration))
            {
                throw new ArgumentException(
                    "An assembly descriptor may appear only once in a call-graph scope.",
                    nameof(participants));
            }

            artifacts.Add(artifact, participant);
            _participantByIndex.Add(participant.Index, participant);
            builder.Add(participant);
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException(
                "At least one call-graph participant is required.",
                nameof(participants));
        }

        _participants = builder.ToImmutable();
    }

    public AssemblyCatalogId Catalog => _catalog.Id;

    public AssemblyCatalogGenerationId? Generation =>
        _graph?.Generation;

    /// <summary>
    /// Physical nodes whose catalog correspondence could not be projected.
    /// Reading this property builds the shared graph if necessary.
    /// </summary>
    public ImmutableArray<GraphNodeEvidence> IncompleteNodes =>
        Graph.IncompleteNodes;

    /// <summary>
    /// Physical call edges with an incomplete caller or callee projection.
    /// Reading this property builds the shared graph if necessary.
    /// </summary>
    public ImmutableArray<GraphEdgeEvidence> IncompleteEdges =>
        Graph.IncompleteEdges;

    /// <summary>
    /// Exact call-site bindings to a different identity of the primary
    /// assembly. The calls retain their exact identity and are not joined to
    /// the primary assembly.
    /// </summary>
    public ImmutableArray<GraphBindingIdentityConflictEvidence>
        BindingIdentityConflicts => Graph.BindingIdentityConflicts;

    public int StorageNodeCount => Graph.StorageNodeCount;
    public int StorageEdgeCount => Graph.StorageEdgeCount;
    public CatalogCallGraphDiagnostics Diagnostics => Graph.Diagnostics;

    public CallTreeNode BuildCallerTree(
        LibraryBodyIndex root,
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        CatalogCallGraphParticipant participant = Participant(root);
        return Graph.BuildCallerTree(
            participant,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    public CallTreeNode BuildCallTree(
        LibraryBodyIndex root,
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        CatalogCallGraphParticipant participant = Participant(root);
        return Graph.BuildCallTree(
            participant,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    /// <summary>
    /// Detaches one tree from this scope's catalog generation while preserving
    /// physical evidence and safe logical joins.
    /// </summary>
    /// <remarks>
    /// <c>CatalogCallGraphScopeTests</c> gates version-skew separation,
    /// repeated external joins, and independent-acquisition identity.
    /// </remarks>
    public CallTreeNode Detach(CallTreeNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Graph.Detach(root);
    }

    /// <summary>
    /// Releases physical graph storage and its frozen generation. Catalog
    /// acquisition caches remain available for a later rebuild.
    /// </summary>
    public void ReleaseGraph()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _graph?.Dispose();
        _graph = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _graph?.Dispose();
        _graph = null;
        _catalog.Dispose();
    }

    ScopeGraph Graph
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_graph is { } graph
                && graph.Catalog == _catalog.Id)
            {
                return graph;
            }

            _graph?.Dispose();
            return _graph = ScopeGraph.Create(
                _catalog,
                _bindingPolicy,
                _participants);
        }
    }

    CatalogCallGraphParticipant Participant(LibraryBodyIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _participantByIndex.TryGetValue(index, out var participant)
            ? participant
            : throw new ArgumentException(
                "The body index does not belong to this call-graph scope.",
                nameof(index));
    }

    static void ValidateParticipant(
        CatalogCallGraphParticipant participant)
    {
        MethodIdentity? method =
            participant.Index.DeclaredMethods.FirstOrDefault();
        if (method is not null
            && !string.Equals(
                method.AssemblyName,
                participant.Assembly.Identity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The assembly descriptor does not describe the body index.",
                nameof(participant));
        }
    }

    static Guid ModuleVersionId(LibraryBodyIndex index) =>
        index.DeclaredMethods.FirstOrDefault()?.ModuleVersionId
            ?? Guid.Empty;

    sealed class ScopeGraph : IDisposable
    {
        readonly ImmutableArray<StoredDefinition> _definitions;
        readonly ImmutableArray<StoredCallSite> _callSites;
        readonly ImmutableArray<StoredEdge> _edges;
        readonly ImmutableArray<GraphNodeEvidence> _incompleteNodes;
        readonly ImmutableArray<GraphEdgeEvidence> _incompleteEdges;
        readonly ImmutableArray<GraphBindingIdentityConflictEvidence>
            _bindingIdentityConflicts;
        readonly CatalogCallGraphDiagnostics _diagnostics;
        readonly Dictionary<GraphNodeIdentity, ImmutableArray<StoredDefinition>>
            _definitionsByIdentity;
        readonly Dictionary<GraphNodeIdentity, ImmutableArray<StoredEdge>>
            _forward;
        readonly Dictionary<GraphNodeIdentity, ImmutableArray<StoredEdge>>
            _reverse;
        readonly Dictionary<GraphNodeIdentity, int> _incoming;
        readonly Dictionary<(LibraryBodyIndex Index, int Token), StoredDefinition>
            _definitionByLocation;
        readonly TypeResolutionContext _context;

        ScopeGraph(
            TypeResolutionContext context,
            ImmutableArray<CatalogCallGraphParticipant> participants,
            ImmutableArray<StoredDefinition> definitions,
            ImmutableArray<StoredCallSite> callSites,
            ImmutableArray<StoredEdge> edges)
        {
            _context = context;
            Catalog = context.Catalog;
            Generation = context.Generation;
            _definitions = definitions;
            _callSites = callSites;
            _edges = edges;
            _incompleteNodes =
            [
                .. definitions
                    .Select(definition => definition.Evidence)
                    .Concat(callSites.Select(callSite => callSite.Evidence))
                    .Where(evidence =>
                        evidence.Kind == GraphCorrespondenceKind.Incomplete),
            ];
            _incompleteEdges =
            [
                .. edges
                    .Where(edge =>
                        edge.Caller.Evidence.Kind
                            == GraphCorrespondenceKind.Incomplete
                        || edge.Callee.Evidence.Kind
                            == GraphCorrespondenceKind.Incomplete)
                    .Select(edge => new GraphEdgeEvidence(
                        edge.Caller.Evidence,
                        edge.Callee.Evidence,
                        edge.Call.Kind,
                        edge.Call.InLoop)),
            ];
            _bindingIdentityConflicts = FindBindingIdentityConflicts(
                participants,
                definitions,
                callSites);
            _diagnostics = new(
                _incompleteNodes.Length,
                _incompleteEdges.Length,
                _bindingIdentityConflicts.Length);
            _definitionByLocation = definitions.ToDictionary(
                definition =>
                    (definition.Participant.Index, definition.Method.MetadataToken));
            _definitionsByIdentity = definitions
                .GroupBy(definition => definition.Evidence.Identity)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(
                            definition => definition.Participant.Assembly.Path,
                            StringComparer.Ordinal)
                        .ThenBy(
                            definition => definition.Method.MetadataToken)
                        .ToImmutableArray());
            _forward = edges
                .GroupBy(edge => edge.Caller.Evidence.Identity)
                .ToDictionary(
                    group => group.Key,
                    group => OrderForwardEdges(group).ToImmutableArray());
            _reverse = edges
                .GroupBy(edge => edge.Callee.Evidence.Identity)
                .ToDictionary(
                    group => group.Key,
                    group => OrderReverseEdges(group).ToImmutableArray());
            _incoming = edges
                .GroupBy(edge => edge.Callee.Evidence.Identity)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(edge => edge.Caller.Evidence.Identity)
                        .Distinct()
                        .Count());
        }

        internal AssemblyCatalogId Catalog { get; }
        internal AssemblyCatalogGenerationId Generation { get; }
        internal int StorageNodeCount =>
            _definitions.Length + _callSites.Length;
        internal int StorageEdgeCount => _edges.Length;
        internal CatalogCallGraphDiagnostics Diagnostics => _diagnostics;
        internal ImmutableArray<GraphNodeEvidence> IncompleteNodes =>
            _incompleteNodes;
        internal ImmutableArray<GraphEdgeEvidence> IncompleteEdges =>
            _incompleteEdges;
        internal ImmutableArray<GraphBindingIdentityConflictEvidence>
            BindingIdentityConflicts => _bindingIdentityConflicts;

        static ImmutableArray<GraphBindingIdentityConflictEvidence>
            FindBindingIdentityConflicts(
                ImmutableArray<CatalogCallGraphParticipant> participants,
                ImmutableArray<StoredDefinition> definitions,
                ImmutableArray<StoredCallSite> callSites)
        {
            AssemblyReferenceIdentity primary =
                participants[0].Assembly.Identity;
            Dictionary<
                GraphNodeIdentity,
                ImmutableArray<AssemblyReferenceIdentity>>
                skewedDefinitions = definitions
                    .Where(definition =>
                        string.Equals(
                            definition.Participant.Assembly.Identity.Name,
                            primary.Name,
                            StringComparison.OrdinalIgnoreCase)
                        && definition.Participant.Assembly.Identity
                            != primary)
                    .GroupBy(definition => definition.Evidence.Identity)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .Select(definition =>
                                definition.Participant.Assembly.Identity)
                            .Distinct()
                            .ToImmutableArray());
            if (skewedDefinitions.Count == 0)
                return [];

            var conflicts = ImmutableArray.CreateBuilder<
                GraphBindingIdentityConflictEvidence>();
            foreach (StoredCallSite callSite in callSites)
            {
                if (callSite.Call.Callee.DeclaringType.Resolution?.Origin
                        is not TypeReferenceOrigin.AssemblyReference requested
                    || !string.Equals(
                        requested.Assembly.Name,
                        primary.Name,
                        StringComparison.OrdinalIgnoreCase)
                    || !skewedDefinitions.TryGetValue(
                        callSite.Evidence.Identity,
                        out ImmutableArray<AssemblyReferenceIdentity>
                            selectedIdentities))
                {
                    continue;
                }

                foreach (AssemblyReferenceIdentity selected
                    in selectedIdentities)
                {
                    if (selected != requested.Assembly)
                        continue;

                    conflicts.Add(
                        new GraphBindingIdentityConflictEvidence(
                            callSite.Evidence,
                            requested.Assembly,
                            selected,
                            primary));
                }
            }

            return conflicts.ToImmutable();
        }

        internal static ScopeGraph Create(
            TypeResolutionCatalog catalog,
            IAssemblyBindingPolicy bindingPolicy,
            ImmutableArray<CatalogCallGraphParticipant> participants)
        {
            var plans = new Dictionary<PlanKey, PlanEntry>();
            var definitions =
                ImmutableArray.CreateBuilder<PendingDefinition>();
            var callSites =
                ImmutableArray.CreateBuilder<PendingCallSite>();
            var definitionLocations = new Dictionary<
                (LibraryBodyIndex Index, int Token),
                PendingDefinition>();

            foreach (CatalogCallGraphParticipant participant
                in participants)
            {
                HashSet<int> bodyTokens =
                [
                    .. participant.Index.Methods.Select(
                        method => method.MetadataToken),
                ];
                Dictionary<int, AnalysisDiagnostic> diagnosticsByToken =
                    participant.Index.Diagnostics
                        .GroupBy(diagnostic => diagnostic.MethodToken)
                        .ToDictionary(
                            group => group.Key,
                            group => group.First());
                foreach (MethodIdentity method
                    in participant.Index.DeclaredMethods)
                {
                    MemberRef member =
                        CallTreeMember.FromDefinition(method);
                    var storage = GraphNodeStorageKey.Definition(
                        participant.Assembly,
                        method.ModuleVersionId,
                        method.MetadataToken);
                    PlanEntry plan = GetOrAddPlan(
                        plans,
                        participant.Assembly,
                        member,
                        () => CatalogMemberCorrespondencePlan.Create(
                            participant.Assembly,
                            method));
                    var pending = new PendingDefinition(
                        participant,
                        method,
                        member,
                        storage,
                        plan,
                        bodyTokens.Contains(method.MetadataToken),
                        diagnosticsByToken.GetValueOrDefault(
                            method.MetadataToken));
                    definitions.Add(pending);
                    definitionLocations.Add(
                        (participant.Index, method.MetadataToken),
                        pending);
                }

                foreach (DirectCall call in participant.Index.DirectCalls)
                {
                    var storage = GraphNodeStorageKey.CallSite(
                        participant.Assembly,
                        call.EvidenceMethod.ModuleVersionId,
                        call);
                    PlanEntry plan = GetOrAddPlan(
                        plans,
                        participant.Assembly,
                        call.Callee,
                        () => CatalogMemberCorrespondencePlan.Create(
                            participant.Assembly,
                            call.Callee));
                    callSites.Add(
                        new PendingCallSite(
                            participant,
                            call,
                            storage,
                            plan));
                }
            }

            TypeResolutionRequest[] requests = plans.Values
                .SelectMany(plan => plan.Plan.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance)
                .ToArray();
            TypeResolutionContext context = catalog.CreateContext(
                bindingPolicy,
                participants.Select(participant => participant.Assembly),
                requests);
            try
            {
                foreach (PlanEntry plan in plans.Values)
                    plan.Projection = plan.Plan.Project(context);

                var storedDefinitions =
                    ImmutableArray.CreateBuilder<StoredDefinition>(
                        definitions.Count);
                var storedDefinitionByPending =
                    new Dictionary<PendingDefinition, StoredDefinition>(
                        ReferenceEqualityComparer.Instance);
                foreach (PendingDefinition definition in definitions)
                {
                    var stored = new StoredDefinition(
                        definition.Participant,
                        definition.Method,
                        definition.Member,
                        Evidence(
                            definition.Storage,
                            definition.Plan.Projection!),
                        definition.HasBody,
                        definition.Diagnostic);
                    storedDefinitions.Add(stored);
                    storedDefinitionByPending.Add(definition, stored);
                }

                var storedCallSites =
                    ImmutableArray.CreateBuilder<StoredCallSite>(
                        callSites.Count);
                var storedEdges =
                    ImmutableArray.CreateBuilder<StoredEdge>(
                        callSites.Count);
                foreach (PendingCallSite callSite in callSites)
                {
                    var stored = new StoredCallSite(
                        callSite.Participant,
                        callSite.Call,
                        Evidence(
                            callSite.Storage,
                            callSite.Plan.Projection!));
                    storedCallSites.Add(stored);
                    if (definitionLocations.TryGetValue(
                            (
                                callSite.Participant.Index,
                                callSite.Call.Caller.MetadataToken),
                            out PendingDefinition? caller))
                    {
                        storedEdges.Add(
                            new StoredEdge(
                                storedDefinitionByPending[caller],
                                stored,
                                callSite.Call));
                    }
                }

                return new ScopeGraph(
                    context,
                    participants,
                    storedDefinitions.ToImmutable(),
                    storedCallSites.ToImmutable(),
                    storedEdges.ToImmutable());
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        internal CallTreeNode BuildCallerTree(
            CatalogCallGraphParticipant root,
            int rootMethodToken,
            int maxDepth,
            int maxNodes)
        {
            (MemberRef Member, GraphNodeEvidence Evidence) =
                Root(root, rootMethodToken);
            string targetAssembly = root.Assembly.Identity.Name;
            int budget = Math.Max(1, maxNodes);
            int created = 1;
            var expanded = new HashSet<GraphNodeIdentity>();

            CallTreeNode Build(
                MemberRef member,
                GraphNodeEvidence evidence,
                string assembly,
                MethodSignals signals,
                int depth,
                bool inLoop,
                ImmutableArray<DirectCall> parentEdgeCallSites = default,
                GraphNodeStorageKey? parentEdgeCallerDefinition = null)
            {
                GraphNodeIdentity identity = evidence.Identity;
                bool external = !string.Equals(
                    assembly,
                    targetAssembly,
                    StringComparison.Ordinal);
                string? source = external ? assembly : null;
                string? classification = depth == 0
                    ? "target"
                    : member.Name is "Main" or "<Main>$"
                        ? "entrypoint"
                        : null;
                string? loopHint = inLoop ? "loop call" : null;

                if (!_reverse.TryGetValue(identity, out var rawEdges))
                {
                    var leafStatus = depth > 0
                        && evidence.Kind
                            == GraphCorrespondenceKind.Incomplete
                            ? CallTreeStatus.External
                            : CallTreeStatus.Leaf;
                    return Node(
                        member,
                        kind: null,
                        leafStatus,
                        [],
                        new CallTreePerf(
                            0,
                            0,
                            1,
                            inLoop,
                            loopHint,
                            classification,
                            signals,
                            source),
                        evidence,
                        parentEdgeCallSites:
                            parentEdgeCallSites,
                        parentEdgeCallerDefinition:
                            parentEdgeCallerDefinition);
                }

                var edges = rawEdges
                    .GroupBy(edge => edge.Caller.Evidence.Identity)
                    .Select(group =>
                        (
                            Edge: group.FirstOrDefault(
                                edge => edge.Call.InLoop,
                                group.First()),
                            Calls: group
                                .Select(edge => edge.Call)
                                .ToImmutableArray()))
                    .ToImmutableArray();
                int fanin = edges.Length;
                if (depth >= maxDepth)
                {
                    return Node(
                        member,
                        null,
                        CallTreeStatus.DepthLimited,
                        [],
                        new CallTreePerf(
                            0,
                            fanin,
                            1,
                            inLoop,
                            loopHint,
                            classification,
                            signals,
                            source),
                        evidence,
                        parentEdgeCallSites:
                            parentEdgeCallSites,
                        parentEdgeCallerDefinition:
                            parentEdgeCallerDefinition);
                }
                if (!expanded.Add(identity))
                {
                    return Node(
                        member,
                        null,
                        CallTreeStatus.AlreadyShown,
                        [],
                        new CallTreePerf(
                            0,
                            fanin,
                            1,
                            inLoop,
                            loopHint,
                            classification,
                            signals,
                            source),
                        evidence,
                        parentEdgeCallSites:
                            parentEdgeCallSites,
                        parentEdgeCallerDefinition:
                            parentEdgeCallerDefinition);
                }

                var children =
                    ImmutableArray.CreateBuilder<CallTreeNode>();
                bool truncated = false;
                foreach (var edgeGroup in edges)
                {
                    if (created >= budget)
                    {
                        truncated = true;
                        break;
                    }
                    created++;
                    StoredEdge edge = edgeGroup.Edge;
                    children.Add(
                        Build(
                            edge.Caller.Member,
                            edge.Caller.Evidence,
                            edge.Caller.Participant.Assembly.Identity.Name,
                            edge.Caller.Signals,
                            depth + 1,
                            edge.Call.InLoop,
                            edgeGroup.Calls,
                            edge.Caller.Evidence.Storage));
                }

                CallTreeStatus status = truncated
                    ? CallTreeStatus.Truncated
                    : children.Count == 0
                        ? CallTreeStatus.Leaf
                        : CallTreeStatus.Expanded;
                int treeDepth = children.Count == 0
                    ? 1
                    : 1 + children.Max(
                        child => child.Perf?.MaxDepth ?? 1);
                return Node(
                    member,
                    null,
                    status,
                    children.ToImmutable(),
                    new CallTreePerf(
                        0,
                        fanin,
                        treeDepth,
                        inLoop,
                        loopHint,
                        classification,
                        signals,
                        source),
                    evidence,
                    parentEdgeCallSites:
                        parentEdgeCallSites,
                    parentEdgeCallerDefinition:
                        parentEdgeCallerDefinition);
            }

            StoredDefinition? definition = DefinitionFor(
                Evidence.Identity);
            return Build(
                Member,
                Evidence,
                root.Assembly.Identity.Name,
                definition?.Signals ?? MethodSignals.None,
                depth: 0,
                inLoop: false);
        }

        internal CallTreeNode BuildCallTree(
            CatalogCallGraphParticipant root,
            int rootMethodToken,
            int maxDepth,
            int maxNodes)
        {
            (MemberRef Member, GraphNodeEvidence Evidence) =
                Root(root, rootMethodToken);
            string targetAssembly = root.Assembly.Identity.Name;
            int budget = Math.Max(1, maxNodes);
            int created = 1;
            var expanded = new HashSet<GraphNodeIdentity>();

            CallTreeNode Build(
                MemberRef member,
                GraphNodeEvidence evidence,
                CallKind? kind,
                int depth,
                bool inLoop,
                bool hasVirtualDispatchOccurrence,
                ImmutableArray<DirectCall> parentEdgeCallSites = default,
                GraphNodeStorageKey? parentEdgeCallerDefinition = null)
            {
                GraphNodeIdentity identity = evidence.Identity;
                StoredDefinition? definition = DefinitionFor(identity);
                if (definition is not null
                    && evidence.Storage.Kind
                        == GraphNodeStorageKind.CallSite)
                {
                    evidence = evidence.WithDefinitionStorage(
                        definition.Evidence.Storage);
                }
                string assembly =
                    definition?.Participant.Assembly.Identity.Name
                    ?? member.DeclaringType.Assembly;
                MethodSignals signals =
                    definition?.Signals ?? MethodSignals.None;
                bool external = assembly.Length > 0
                    && !string.Equals(
                        assembly,
                        targetAssembly,
                        StringComparison.Ordinal);
                string? source = external ? assembly : null;
                string? loopHint = inLoop ? "loop" : null;
                int fanin = _incoming.GetValueOrDefault(identity);
                bool hasUnresolvedDispatch =
                    hasVirtualDispatchOccurrence
                    && definition?.Method.IsVirtualDispatchOpen == true;

                if (!_forward.TryGetValue(identity, out var rawEdges))
                {
                    CallTreeStatus leafStatus = depth > 0
                        && definition is null
                            ? CallTreeStatus.External
                            : definition?.Diagnostic is not null
                                ? CallTreeStatus.AnalysisIncomplete
                                : definition is { HasBody: false }
                                    ? CallTreeStatus.Bodiless
                                    : CallTreeStatus.Leaf;
                    return Node(
                        member,
                        kind,
                        leafStatus,
                        [],
                        new CallTreePerf(
                            0,
                            fanin,
                            1,
                            inLoop,
                            loopHint,
                            null,
                            signals,
                            source),
                        evidence,
                        definition?.Diagnostic,
                        hasUnresolvedDispatch,
                        parentEdgeCallSites,
                        parentEdgeCallerDefinition);
                }

                int fanout = rawEdges.Length;
                if (depth >= maxDepth)
                {
                    return Node(
                        member,
                        kind,
                        CallTreeStatus.DepthLimited,
                        [],
                        new CallTreePerf(
                            fanout,
                            fanin,
                            1,
                            inLoop,
                            loopHint,
                            null,
                            signals,
                            source),
                        evidence,
                        definition?.Diagnostic,
                        hasUnresolvedDispatch,
                        parentEdgeCallSites,
                        parentEdgeCallerDefinition);
                }
                if (!expanded.Add(identity))
                {
                    return Node(
                        member,
                        kind,
                        CallTreeStatus.AlreadyShown,
                        [],
                        new CallTreePerf(
                            fanout,
                            fanin,
                            1,
                            inLoop,
                            loopHint,
                            null,
                            signals,
                            source),
                        evidence,
                        definition?.Diagnostic,
                        hasUnresolvedDispatch,
                        parentEdgeCallSites,
                        parentEdgeCallerDefinition);
                }

                var edges = rawEdges
                    .GroupBy(edge => edge.Callee.Evidence.Identity)
                    .Select(group =>
                        (
                            Edge: group.FirstOrDefault(
                                edge => edge.Call.InLoop,
                                group.First()),
                            Calls: group
                                .Select(edge => edge.Call)
                                .ToImmutableArray(),
                            HasVirtualDispatch:
                                group.Any(edge =>
                                    edge.Call.Kind
                                        is CallKind.CallVirtual
                                            or CallKind.LoadVirtualFunction)))
                    .ToImmutableArray();
                var children =
                    ImmutableArray.CreateBuilder<CallTreeNode>();
                bool truncated = false;
                foreach (var edgeGroup in edges)
                {
                    if (created >= budget)
                    {
                        truncated = true;
                        break;
                    }
                    created++;
                    StoredEdge edge = edgeGroup.Edge;
                    children.Add(
                        Build(
                            edge.Callee.Call.Callee,
                            edge.Callee.Evidence,
                            edge.Call.Kind,
                            depth + 1,
                            edge.Call.InLoop,
                            edgeGroup.HasVirtualDispatch,
                            edgeGroup.Calls,
                            edge.Caller.Evidence.Storage));
                }

                CallTreeStatus status = truncated
                    ? CallTreeStatus.Truncated
                    : definition?.Diagnostic is not null
                        ? CallTreeStatus.AnalysisIncomplete
                        : children.Count == 0
                            ? CallTreeStatus.Leaf
                            : CallTreeStatus.Expanded;
                int treeDepth = children.Count == 0
                    ? 1
                    : 1 + children.Max(
                        child => child.Perf?.MaxDepth ?? 1);
                return Node(
                    member,
                    kind,
                    status,
                    children.ToImmutable(),
                    new CallTreePerf(
                        fanout,
                        fanin,
                        treeDepth,
                        inLoop,
                        loopHint,
                        null,
                        signals,
                        source),
                    evidence,
                    definition?.Diagnostic,
                    hasUnresolvedDispatch,
                    parentEdgeCallSites,
                    parentEdgeCallerDefinition);
            }

            return Build(
                Member,
                Evidence,
                kind: null,
                depth: 0,
                inLoop: false,
                hasVirtualDispatchOccurrence: false);
        }

        public void Dispose() => _context.Dispose();

        (MemberRef Member, GraphNodeEvidence Evidence) Root(
            CatalogCallGraphParticipant root,
            int rootMethodToken)
        {
            if (_definitionByLocation.TryGetValue(
                    (root.Index, rootMethodToken),
                    out StoredDefinition? definition))
            {
                return (definition.Member, definition.Evidence);
            }

            StoredCallSite? callSite = _callSites.FirstOrDefault(
                candidate =>
                    ReferenceEquals(candidate.Participant.Index, root.Index)
                    && candidate.Call.CalleeDefinitionToken
                        == rootMethodToken
                    && candidate.Call.Callee.Kind
                        != MemberKind.Unsupported);
            if (callSite is not null)
                return (callSite.Call.Callee, callSite.Evidence);

            var member = MemberRef.Unsupported(
                $"method token 0x{rootMethodToken:X8}");
            var storage = GraphNodeStorageKey.Definition(
                root.Assembly,
                root.Index.DeclaredMethods.FirstOrDefault()
                    ?.ModuleVersionId ?? Guid.Empty,
                rootMethodToken);
            return (
                member,
                new GraphNodeEvidence(
                    storage,
                    GraphNodeIdentity.FromStorage(storage),
                    correspondence: null));
        }

        StoredDefinition? DefinitionFor(GraphNodeIdentity identity) =>
            _definitionsByIdentity.TryGetValue(
                identity,
                out ImmutableArray<StoredDefinition> definitions)
                ? definitions[0]
                : null;

        internal CallTreeNode Detach(CallTreeNode root)
        {
            var detached = new Dictionary<
                GraphNodeIdentity,
                GraphNodeIdentity>();

            GraphNodeIdentity DetachIdentity(
                GraphNodeEvidence evidence,
                bool isRoot)
            {
                if (detached.TryGetValue(
                        evidence.Identity,
                        out GraphNodeIdentity? existing))
                {
                    return existing;
                }

                GraphNodeIdentity identity;
                if (isRoot
                    && evidence.Storage.Kind
                        == GraphNodeStorageKind.Definition)
                {
                    identity = GraphNodeIdentity.FromArtifactMember(
                        evidence.Storage);
                }
                else if (_definitionsByIdentity.TryGetValue(
                        evidence.Identity,
                        out ImmutableArray<StoredDefinition> definitions)
                    && definitions.Length == 1
                    && (evidence.Storage.Kind
                            == GraphNodeStorageKind.Definition
                        || evidence.Kind
                            == GraphCorrespondenceKind.Exact))
                {
                    identity = GraphNodeIdentity.FromArtifactMember(
                        definitions[0].Evidence.Storage);
                }
                else if (evidence.Kind
                    == GraphCorrespondenceKind.Incomplete)
                {
                    identity = GraphNodeIdentity.FromStorage(
                        evidence.Storage);
                }
                else
                {
                    identity =
                        GraphNodeIdentity.CreateDocumentLocal();
                }

                detached.Add(evidence.Identity, identity);
                return identity;
            }

            GraphNodeEvidence? DetachEvidence(
                GraphNodeEvidence? evidence,
                bool isRoot)
            {
                if (evidence is null)
                    return null;

                return new GraphNodeEvidence(
                    evidence.Storage,
                    DetachIdentity(evidence, isRoot),
                    correspondence: null,
                    definitionStorage:
                        evidence.DefinitionStorage);
            }

            CallTreeNode DetachNode(
                CallTreeNode node,
                bool isRoot = false) =>
                node with
                {
                    GraphEvidence = DetachEvidence(
                        node.GraphEvidence,
                        isRoot),
                    Children =
                    [
                        .. node.Children.Select(
                            child => DetachNode(child)),
                    ],
                };

            return DetachNode(root, isRoot: true);
        }

        static GraphNodeEvidence Evidence(
            GraphNodeStorageKey storage,
            CatalogMemberJoinProjection projection) =>
            new(
                storage,
                projection is CatalogMemberJoinProjection.Issued issued
                    ? GraphNodeIdentity.FromCorrespondence(issued.Key)
                    : GraphNodeIdentity.FromStorage(storage),
                projection);

        static PlanEntry GetOrAddPlan(
            Dictionary<PlanKey, PlanEntry> plans,
            ResolvedAssemblyReference source,
            MemberRef member,
            Func<CatalogMemberCorrespondencePlan> create)
        {
            var key = new PlanKey(
                source.Registration,
                GraphNodeIdentity.FromMember(member));
            if (plans.TryGetValue(key, out PlanEntry? existing))
                return existing;

            var entry = new PlanEntry(create());
            plans.Add(key, entry);
            return entry;
        }

        static CallTreeNode Node(
            MemberRef member,
            CallKind? kind,
            CallTreeStatus status,
            ImmutableArray<CallTreeNode> children,
            CallTreePerf perf,
            GraphNodeEvidence evidence,
            AnalysisDiagnostic? diagnostic = null,
            bool hasUnresolvedDispatch = false,
            ImmutableArray<DirectCall> parentEdgeCallSites = default,
            GraphNodeStorageKey? parentEdgeCallerDefinition = null) =>
            new(member, kind, status, children, perf)
            {
                GraphEvidence = evidence,
                Diagnostic = diagnostic,
                HasUnresolvedDispatch =
                    hasUnresolvedDispatch,
                ParentEdgeCallSites = parentEdgeCallSites.IsDefault
                    ? []
                    : parentEdgeCallSites,
                ParentEdgeCallerDefinition =
                    parentEdgeCallerDefinition,
            };

        static IOrderedEnumerable<StoredEdge> OrderForwardEdges(
            IEnumerable<StoredEdge> edges) =>
            edges
                .OrderBy(
                    edge => edge.Callee.Call.Callee.DeclaringType.Assembly,
                    StringComparer.Ordinal)
                .ThenBy(
                    edge => edge.Callee.Call.Callee
                        .ToQualifiedDisplayString(),
                    StringComparer.Ordinal)
                .ThenBy(
                    edge => edge.Callee.Call.Callee.ParameterTypes.Length)
                .ThenBy(
                    edge => edge.Callee.Evidence.Storage.ModuleVersionId)
                .ThenBy(
                    edge => edge.Callee.Evidence.Storage.MethodToken)
                .ThenBy(
                    edge => edge.Callee.Evidence.Storage.ILOffset);

        static IOrderedEnumerable<StoredEdge> OrderReverseEdges(
            IEnumerable<StoredEdge> edges) =>
            CallTreeOrdering.OrderCallers(
                    edges,
                    edge => edge.Caller.Participant.Assembly.Identity.Name,
                    edge => edge.Caller.Member.ToQualifiedDisplayString(),
                    edge => edge.Caller.Member.ParameterTypes.Length,
                    edge => edge.Caller.Evidence.Storage.ModuleVersionId,
                    edge => edge.Caller.Evidence.Storage.MethodToken,
                    edge => edge.Callee.Evidence.Storage.ILOffset);

        sealed class PlanEntry(
            CatalogMemberCorrespondencePlan plan)
        {
            internal CatalogMemberCorrespondencePlan Plan { get; } =
                plan;
            internal CatalogMemberJoinProjection? Projection { get; set; }
        }

        readonly record struct PlanKey(
            AssemblyAcquisitionRegistration Source,
            GraphNodeIdentity Member);

        sealed record PendingDefinition(
            CatalogCallGraphParticipant Participant,
            MethodIdentity Method,
            MemberRef Member,
            GraphNodeStorageKey Storage,
            PlanEntry Plan,
            bool HasBody,
            AnalysisDiagnostic? Diagnostic);

        sealed record PendingCallSite(
            CatalogCallGraphParticipant Participant,
            DirectCall Call,
            GraphNodeStorageKey Storage,
            PlanEntry Plan);

        sealed class StoredDefinition
        {
            internal StoredDefinition(
                CatalogCallGraphParticipant participant,
                MethodIdentity method,
                MemberRef member,
                GraphNodeEvidence evidence,
                bool hasBody,
                AnalysisDiagnostic? diagnostic)
            {
                Participant = participant;
                Method = method;
                Member = member;
                Evidence = evidence;
                HasBody = hasBody;
                Diagnostic = diagnostic;
                Signals = participant.Index.GetMethodSignals()
                    .GetValueOrDefault(
                        method.MetadataToken,
                        MethodSignals.None);
            }

            internal CatalogCallGraphParticipant Participant { get; }
            internal MethodIdentity Method { get; }
            internal MemberRef Member { get; }
            internal GraphNodeEvidence Evidence { get; }
            internal bool HasBody { get; }
            internal AnalysisDiagnostic? Diagnostic { get; }
            internal MethodSignals Signals { get; }
        }

        sealed record StoredCallSite(
            CatalogCallGraphParticipant Participant,
            DirectCall Call,
            GraphNodeEvidence Evidence);

        sealed record StoredEdge(
            StoredDefinition Caller,
            StoredCallSite Callee,
            DirectCall Call);
    }
}
