using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Detached single-module call evidence and lazy local call-graph derivations
/// produced by one library-body Analysis execution.
/// </summary>
public sealed class LibraryCallGraphAnalysisResult
{
    private readonly string? _moduleName;
    private readonly ImmutableArray<UnsafeEvidence> _unsafeEvidence;
    private readonly IReadOnlyDictionary<int, BodySignals> _bodySignals;
    private readonly IReadOnlyDictionary<
        int,
        ImmutableArray<AllocationOccurrence>> _allocationOccurrences;
    private readonly IReadOnlyDictionary<
        (string Namespace, string Name),
        bool> _inAssemblyTypeIsException;
    private readonly IReadOnlySet<int> _nonHeapNewObjOperandTokens;
    private readonly IReadOnlyDictionary<int, MethodIdentity> _declaredSources;
    private ImmutableArray<DirectCall> _physicalDirectCalls;
    private Dictionary<int, MethodSignals>? _signals;
    private IReadOnlyDictionary<int, ImmutableArray<DirectCall>>?
        _directCallsByCaller;
    private IReadOnlyDictionary<int, ImmutableArray<DirectCall>>?
        _directCallsByEvidenceMethod;
    private MethodDefinitionMap? _methodMap;
    private MethodDefinitionMap? _declaredMethodMap;
    private IReadOnlyDictionary<int, int>? _distinctCallersByCallee;
    private IReadOnlyDictionary<int, ImmutableArray<DirectCall>>?
        _distinctCallerEdgesByCallee;
    private LibraryBodyLocalCallGraph? _rootPathGraph;

    internal LibraryCallGraphAnalysisResult(
        LibraryBodyAnalysisReceipt receipt,
        string? moduleName,
        LibraryBodyAnalysisResult analysis)
    {
        Receipt = receipt;
        _moduleName = moduleName;
        DeclaredMethods = analysis.Methods.DeclaredMethods;
        Methods = analysis.Methods.Methods;
        DirectCalls = analysis.Methods.DirectCalls;
        _unsafeEvidence = analysis.Safety.Evidence;
        _bodySignals = analysis.Methods.BodySignals;
        _allocationOccurrences = analysis.Allocations.Occurrences;
        _inAssemblyTypeIsException =
            analysis.Methods.InAssemblyTypeIsException;
        _nonHeapNewObjOperandTokens =
            analysis.Methods.NonHeapNewObjOperandTokens;
        _declaredSources = analysis.Methods.DeclaredSources;
        OwnershipEvidence = analysis.OwnershipFlow.Methods;
    }

    /// <summary>
    /// Common identity, coverage, and diagnostics for the producing execution.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>Whether method-evidence production participated.</summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.MethodEvidence);

    public LibraryBodyModuleIdentity ModuleIdentity =>
        Receipt.ModuleIdentity;

    public LibraryBodyAnalysisFeatures Features =>
        Receipt.Features;

    public ImmutableArray<AnalysisDiagnostic> Diagnostics =>
        Receipt.Diagnostics;

    public bool HasFullMethodEvidenceScope =>
        Receipt.HasFullMethodEvidenceScope;

    public ImmutableArray<MethodIdentity> DeclaredMethods { get; }

    public ImmutableArray<MethodIdentity> Methods { get; }

    public ImmutableArray<DirectCall> DirectCalls { get; }

    public ImmutableArray<ArrayPoolOwnershipMethodEvidence>
        OwnershipEvidence { get; }

    internal bool HasProjectedPhysicalDirectCalls =>
        !_physicalDirectCalls.IsDefault;

    internal bool HasProjectedMethodSignals =>
        _signals is not null;

    internal ImmutableArray<DirectCall> PhysicalDirectCalls =>
        _physicalDirectCalls.IsDefault
            ? _physicalDirectCalls =
            [
                .. DirectCalls.Select(static call =>
                    call.Caller == call.EvidenceMethod
                        ? call
                        : call with
                        {
                            Caller = call.EvidenceMethod,
                        }),
            ]
            : _physicalDirectCalls;

    internal MethodDefinitionMap DeclaredMethodMap =>
        _declaredMethodMap ??=
            MethodDefinitionMap.Create(
                DeclaredMethods,
                _moduleName);

    public IReadOnlyDictionary<int, MethodSignals> MethodSignals =>
        _signals ??= MethodSignalAnalysis.Collect(
            PhysicalDirectCalls,
            _unsafeEvidence,
            _bodySignals,
            Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.Allocations)
                ? _allocationOccurrences
                : null,
            _inAssemblyTypeIsException,
            _nonHeapNewObjOperandTokens);

    internal IReadOnlyDictionary<int, MethodSignals>
        GetMethodSignals() => MethodSignals;

    /// <summary>Direct call sites grouped by their declared caller token.</summary>
    public IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        DirectCallsByCaller =>
        _directCallsByCaller ??= DirectCalls
            .GroupBy(call => call.Caller.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray());

    /// <summary>
    /// Direct call sites grouped by the physical method body that owns their
    /// IL coordinates. Calls retain their declared <see cref="DirectCall.Caller"/>.
    /// </summary>
    public IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        DirectCallsByEvidenceMethod =>
        _directCallsByEvidenceMethod ??= DirectCalls
            .GroupBy(call => call.EvidenceMethod.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray());

    internal LibraryBodyLocalCallGraph RootPathGraph() =>
        _rootPathGraph ??=
            LibraryBodyRootPathAnalysis.BuildLocalGraph(this);

    internal MethodIdentity? ResolveDeclaredMethod(
        MethodIdentity caller)
    {
        if (_declaredSources.TryGetValue(
                caller.MetadataToken,
                out MethodIdentity? source)
            && source.MetadataToken != caller.MetadataToken)
        {
            return source;
        }

        return null;
    }

    /// <summary>
    /// Releases lazy single-module graph maps while retaining projected
    /// physical-call and method-signal evidence used by other producers.
    /// Subsequent graph access rebuilds from the retained detached evidence.
    /// </summary>
    public void ReleaseCaches()
    {
        _methodMap = null;
        _declaredMethodMap = null;
        _distinctCallersByCallee = null;
        _distinctCallerEdgesByCallee = null;
        _rootPathGraph = null;
        _directCallsByCaller = null;
        _directCallsByEvidenceMethod = null;
    }

    /// <summary>
    /// Builds a bounded outbound tree rooted at one MethodDef token.
    /// </summary>
    public CallTreeNode BuildCallTree(
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        EnsureRequested();

        MethodIdentity? root = DeclaredMethod(rootMethodToken);
        MemberRef rootMember = root is { } identity
            ? CallTreeMember.FromDefinition(identity)
            : MemberRef.Unsupported(
                $"method token 0x{rootMethodToken:X8}");

        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            callsByCaller = DirectCallsByCaller;
        MethodDefinitionMap bodyMap =
            _methodMap ??=
                MethodDefinitionMap.Create(
                    Methods,
                    _moduleName);
        MethodDefinitionMap declarationMap = DeclaredMethodMap;
        Dictionary<int, AnalysisDiagnostic> diagnosticsByToken =
            Receipt.Diagnostics
                .GroupBy(diagnostic => diagnostic.MethodToken)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());
        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();
        IReadOnlyDictionary<int, int> incomingCounts =
            DistinctCallersByCallee();

        int ResolveCallee(DirectCall call) =>
            declarationMap.Resolve(call);

        CallTreeNode Build(
            MemberRef member,
            CallKind? kind,
            int token,
            int depth,
            bool inLoop = false,
            bool hasVirtualDispatchOccurrence = false,
            ImmutableArray<DirectCall> parentEdgeCallSites = default)
        {
            MethodIdentity? definition =
                token == 0 ? null : DeclaredMethod(token);
            MethodSignals signals = token != 0
                ? MethodSignals.GetValueOrDefault(
                    token,
                    Analysis.MethodSignals.None)
                : Analysis.MethodSignals.None;
            diagnosticsByToken.TryGetValue(
                token,
                out AnalysisDiagnostic? diagnostic);
            bool hasUnresolvedDispatch =
                hasVirtualDispatchOccurrence
                && definition?.IsVirtualDispatchOpen == true;

            CallTreeNode Node(
                CallTreeStatus status,
                ImmutableArray<CallTreeNode> children,
                CallTreePerf perf) =>
                new(member, kind, status, children, perf)
                {
                    Diagnostic = diagnostic,
                    HasUnresolvedDispatch = hasUnresolvedDispatch,
                    ParentEdgeCallSites =
                        parentEdgeCallSites.IsDefault
                            ? []
                            : parentEdgeCallSites,
                };

            if (token == 0
                || !callsByCaller.TryGetValue(
                    token,
                    out ImmutableArray<DirectCall> edges))
            {
                CallTreeStatus leafStatus =
                    token == 0 && depth > 0
                        ? CallTreeStatus.External
                        : diagnostic is not null
                            ? CallTreeStatus.AnalysisIncomplete
                            : definition is not null
                                && !bodyMap.ContainsToken(token)
                                ? CallTreeStatus.Bodiless
                                : CallTreeStatus.Leaf;
                return Node(
                    leafStatus,
                    [],
                    new(
                        0,
                        incomingCounts.GetValueOrDefault(token),
                        1,
                        inLoop,
                        inLoop ? "loop" : null,
                        null,
                        signals));
            }

            int fanout = edges.Length;
            if (depth >= maxDepth)
            {
                return Node(
                    CallTreeStatus.DepthLimited,
                    [],
                    new(
                        fanout,
                        incomingCounts.GetValueOrDefault(token),
                        1,
                        inLoop,
                        inLoop ? "loop" : null,
                        null,
                        signals));
            }

            if (!expanded.Add(token))
            {
                return Node(
                    CallTreeStatus.AlreadyShown,
                    [],
                    new(
                        fanout,
                        incomingCounts.GetValueOrDefault(token),
                        1,
                        inLoop,
                        inLoop ? "loop" : null,
                        null,
                        signals));
            }

            ImmutableArray<(
                (DirectCall Edge, int Token) Item,
                ImmutableArray<DirectCall> Calls,
                bool HasVirtualDispatch)> collapsedEdges =
            [
                .. edges
                    .Select(edge => (
                        Edge: edge,
                        Token: ResolveCallee(edge)))
                    .GroupBy(item =>
                        item.Token != 0
                            ? new LocalCalleeKey(
                                item.Token,
                                null)
                            : new LocalCalleeKey(
                                0,
                                GraphNodeIdentity.FromMember(
                                    item.Edge.Callee)))
                    .Select(group => (
                        Item: group.FirstOrDefault(
                            item => item.Edge.InLoop,
                            group.First()),
                        Calls: group
                            .Select(item => item.Edge)
                            .ToImmutableArray(),
                        HasVirtualDispatch:
                            group.Any(item =>
                                item.Edge.Kind is
                                    CallKind.CallVirtual
                                    or CallKind
                                        .LoadVirtualFunction))),
            ];
            var children =
                ImmutableArray.CreateBuilder<CallTreeNode>();
            bool truncated = false;
            foreach (var edgeGroup in collapsedEdges)
            {
                if (created >= budget)
                {
                    truncated = true;
                    break;
                }

                created++;
                DirectCall edge = edgeGroup.Item.Edge;
                children.Add(
                    Build(
                        edge.Callee,
                        edge.Kind,
                        edgeGroup.Item.Token,
                        depth + 1,
                        edge.InLoop,
                        edgeGroup.HasVirtualDispatch,
                        edgeGroup.Calls));
            }

            CallTreeStatus status = truncated
                ? CallTreeStatus.Truncated
                : diagnostic is not null
                    ? CallTreeStatus.AnalysisIncomplete
                    : children.Count == 0
                        ? CallTreeStatus.Leaf
                        : CallTreeStatus.Expanded;
            int maxTreeDepth = children.Count == 0
                ? 1
                : 1 + children.Max(
                    child => child.Perf?.MaxDepth ?? 1);
            return Node(
                status,
                children.ToImmutable(),
                new(
                    fanout,
                    incomingCounts.GetValueOrDefault(token),
                    maxTreeDepth,
                    inLoop,
                    inLoop ? "loop" : null,
                    null,
                    signals));
        }

        return Build(
            rootMember,
            null,
            rootMethodToken,
            0);
    }

    /// <summary>
    /// Builds a bounded reverse tree rooted at one MethodDef token.
    /// </summary>
    public CallTreeNode BuildCallerTree(
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        EnsureRequested();

        MethodIdentity? root = DeclaredMethod(rootMethodToken);
        MemberRef rootMember = root is { } identity
            ? CallTreeMember.FromDefinition(identity)
            : DirectCalls.FirstOrDefault(call =>
                    call.CalleeDefinitionToken == rootMethodToken
                    && call.Callee.Kind != MemberKind.Unsupported)
                is { Callee: { } resolvedCallee }
                ? resolvedCallee
                : MemberRef.Unsupported(
                    $"method token 0x{rootMethodToken:X8}");
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
            reverseEdges = DistinctCallerEdgesByCallee();
        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();

        int ResolveCalleeToken(DirectCall call) =>
            DeclaredMethodMap.Resolve(call);

        CallTreeNode Build(
            MemberRef member,
            int token,
            int depth,
            bool inLoop,
            ImmutableArray<DirectCall> parentEdgeCallSites = default)
        {
            string? classification = depth == 0
                ? "target"
                : member.Name is "Main" or "<Main>$"
                    ? "entrypoint"
                    : null;
            string? loopHint = inLoop ? "loop call" : null;
            MethodSignals signals = token != 0
                ? MethodSignals.GetValueOrDefault(
                    token,
                    Analysis.MethodSignals.None)
                : Analysis.MethodSignals.None;

            CallTreeNode Node(
                CallTreeStatus status,
                ImmutableArray<CallTreeNode> children,
                CallTreePerf perf) =>
                new(member, null, status, children, perf)
                {
                    ParentEdgeCallSites =
                        parentEdgeCallSites.IsDefault
                            ? []
                            : parentEdgeCallSites,
                };

            if (token == 0
                || !reverseEdges.TryGetValue(
                    token,
                    out ImmutableArray<DirectCall> edges))
            {
                CallTreeStatus leafStatus =
                    token == 0 && depth > 0
                        ? CallTreeStatus.External
                        : CallTreeStatus.Leaf;
                return Node(
                    leafStatus,
                    [],
                    new(
                        0,
                        0,
                        1,
                        inLoop,
                        loopHint,
                        classification,
                        signals));
            }

            int fanin = edges.Length;
            if (depth >= maxDepth)
            {
                return Node(
                    CallTreeStatus.DepthLimited,
                    [],
                    new(
                        0,
                        fanin,
                        1,
                        inLoop,
                        loopHint,
                        classification,
                        signals));
            }

            if (!expanded.Add(token))
            {
                return Node(
                    CallTreeStatus.AlreadyShown,
                    [],
                    new(
                        0,
                        fanin,
                        1,
                        inLoop,
                        loopHint,
                        classification,
                        signals));
            }

            var children =
                ImmutableArray.CreateBuilder<CallTreeNode>();
            bool truncated = false;
            foreach (DirectCall edge in edges)
            {
                if (created >= budget)
                {
                    truncated = true;
                    break;
                }

                created++;
                MethodIdentity caller = edge.Caller;
                ImmutableArray<DirectCall> callSites =
                [
                    .. DirectCallsByCaller[caller.MetadataToken]
                        .Where(call =>
                            ResolveCalleeToken(call) == token)
                        .OrderBy(call => call.ILOffset)
                        .ThenBy(call => call.OperandToken),
                ];
                children.Add(
                    Build(
                        CallTreeMember.FromDefinition(caller),
                        caller.MetadataToken,
                        depth + 1,
                        edge.InLoop,
                        callSites));
            }

            CallTreeStatus status = truncated
                ? CallTreeStatus.Truncated
                : children.Count == 0
                    ? CallTreeStatus.Leaf
                    : CallTreeStatus.Expanded;
            int maxTreeDepth = children.Count == 0
                ? 1
                : 1 + children.Max(
                    child => child.Perf?.MaxDepth ?? 1);
            return Node(
                status,
                children.ToImmutable(),
                new(
                    0,
                    fanin,
                    maxTreeDepth,
                    inLoop,
                    loopHint,
                    classification,
                    signals));
        }

        return Build(
            rootMember,
            rootMethodToken,
            0,
            false);
    }

    /// <summary>
    /// Builds a bounded reverse tree through one catalog-owned assembly-group
    /// graph.
    /// </summary>
    public CallTreeNode BuildCallerTree(
        int rootMethodToken,
        CatalogCallGraphScope scope,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.BuildCallerTree(
            this,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    /// <summary>
    /// Builds a bounded forward tree through one catalog-owned assembly-group
    /// graph.
    /// </summary>
    public CallTreeNode BuildCallTree(
        int rootMethodToken,
        CatalogCallGraphScope scope,
        int maxDepth = 3,
        int maxNodes = 25)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.BuildCallTree(
            this,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }

    private IReadOnlyDictionary<int, int>
        DistinctCallersByCallee() =>
        _distinctCallersByCallee ??= DirectCalls
            .GroupBy(call => DeclaredMethodMap.Resolve(call))
            .Where(group => group.Key != 0)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(call => call.Caller.MetadataToken)
                    .Distinct()
                    .Count());

    private IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        DistinctCallerEdgesByCallee() =>
        _distinctCallerEdgesByCallee ??= DirectCalls
            .GroupBy(call => DeclaredMethodMap.Resolve(call))
            .Where(group => group.Key != 0)
            .ToDictionary(
                group => group.Key,
                group => CallTreeOrdering.OrderCallers(
                        group,
                        call => call.Caller.AssemblyName,
                        call => CallTreeMember
                            .ToQualifiedDisplayString(
                                call.Caller),
                        call => call.Caller.ParameterTypes.Length,
                        call => call.Caller.ModuleVersionId,
                        call => call.Caller.MetadataToken,
                        call => call.ILOffset)
                    .GroupBy(
                        call => call.Caller.MetadataToken)
                    .Select(callerGroup =>
                        callerGroup.FirstOrDefault(
                            call => call.InLoop)
                        ?? callerGroup.First())
                    .ToImmutableArray());

    private MethodIdentity? DeclaredMethod(int metadataToken)
    {
        int low = 0;
        int high = DeclaredMethods.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            MethodIdentity candidate = DeclaredMethods[middle];
            if (candidate.MetadataToken == metadataToken)
                return candidate;
            if (candidate.MetadataToken < metadataToken)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return null;
    }

    internal void EnsureRequested()
    {
        if (!WasRequested)
        {
            throw new InvalidOperationException(
                "Call-graph evidence was not requested for this Analysis "
                + "execution.");
        }
    }

    private readonly record struct LocalCalleeKey(
        int DefinitionToken,
        GraphNodeIdentity? StructuralIdentity);
}
