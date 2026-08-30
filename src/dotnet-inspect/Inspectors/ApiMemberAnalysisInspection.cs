using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Command-configured, lazily acquired analysis state for one member rendering
/// operation.
/// </summary>
internal sealed class ApiMemberAnalysisInspection
{
    readonly string _assemblyPath;
    readonly IReadOnlyList<ApiMember> _methods;
    readonly ApiOptions? _options;
    readonly IReadOnlyList<string>? _callerScopeAssemblies;
    readonly bool _includeAllocations;
    readonly bool _includeOpportunities;
    readonly bool _includeGraphAllocations;
    readonly bool _includeGraphOpportunities;
    readonly bool _hasCallGraphFieldProjection;
    readonly IReadOnlyList<CallGraphField> _callGraphFields = [];
    readonly IReadOnlySet<int>? _bodyScope;
    readonly Dictionary<
        Analysis.TypeRef,
        Analysis.CallerScopeReachabilityPlan> _plans = [];
    readonly Dictionary<
        Analysis.TypeRef,
        List<MethodBodyInspectionSession>> _directCallerScopes = [];
    MethodBodyInspectionSession? _session;
    ResolvedAssemblyReference? _targetAssembly;
    IReadOnlyList<ResolvedAssemblyReference>? _scopeCandidates;
    List<MethodBodyInspectionSession>? _callerScopes;
    bool _callerScopesResolved;
    List<MethodBodyInspectionSession>? _graphScopes;
    bool _graphScopesResolved;
    List<MethodBodyInspectionSession>? _calleeScopes;
    bool _calleeScopesResolved;
    Analysis.CatalogCallGraphDiagnostics _callGraphDiagnostics =
        Analysis.CatalogCallGraphDiagnostics.Empty;

    internal ApiMemberAnalysisInspection(
        string assemblyPath,
        IReadOnlyList<ApiMember> methods,
        IReadOnlySet<string> requestedSections,
        IReadOnlyList<string>? callerScopeAssemblies,
        ApiOptions? options)
    {
        _assemblyPath = assemblyPath;
        _methods = methods;
        _options = options;
        _callerScopeAssemblies = callerScopeAssemblies;

        (_includeAllocations, _includeOpportunities) =
            ApiAnalysisInspection.AnalysisScopeFor(requestedSections);
        if (requestedSections.Contains(SectionNames.CallGraph)
            && (options?.Fields is { Length: > 0 }
                || options?.Columns is { Length: > 0 }))
        {
            _hasCallGraphFieldProjection = true;
            _callGraphFields = CallGraphFieldSelection.Resolve(
                options?.Fields ?? []);
            _includeGraphAllocations = _callGraphFields.Contains(
                CallGraphField.Allocations);
            _includeGraphOpportunities = _callGraphFields.Contains(
                CallGraphField.AsyncAlternatives);
            if (_includeGraphAllocations)
            {
                _includeAllocations = true;
            }
        }

        bool needsWholeAssemblyBody =
            requestedSections.Contains(SectionNames.Callers)
            || requestedSections.Contains(SectionNames.CallGraph);
        if (!needsWholeAssemblyBody)
        {
            var memberTokens = methods
                .Where(member => member.MetadataToken.HasValue)
                .Select(member => member.MetadataToken!.Value)
                .ToHashSet();
            if (memberTokens.Count > 0
                && memberTokens.Count == methods.Count)
            {
                _bodyScope = memberTokens;
            }
        }
    }

    internal Analysis.LibraryBodyIndex BodyIndex => Session.BodyIndex;

    internal IReadOnlyList<CallGraphField> CallGraphFields =>
        _callGraphFields;

    internal bool HasCallGraphFieldProjection =>
        _hasCallGraphFieldProjection;

    internal bool IncludesCallGraphOpportunities =>
        _includeGraphOpportunities;

    internal IReadOnlyList<Analysis.LibraryBodyIndex>
        CallGraphBodyIndexes
    {
        get
        {
            var indexes = new List<Analysis.LibraryBodyIndex>();
            var seen = new HashSet<Analysis.LibraryBodyIndex>(
                ReferenceEqualityComparer.Instance);
            Add(Session);
            IEnumerable<MethodBodyInspectionSession> callerScopes =
                _graphScopesResolved
                    ? _graphScopes ?? []
                    : _callerScopes ?? [];
            foreach (MethodBodyInspectionSession scope in callerScopes)
            {
                Add(scope);
            }
            foreach (MethodBodyInspectionSession scope in
                _calleeScopes ?? [])
            {
                Add(scope);
            }
            return indexes;

            void Add(MethodBodyInspectionSession session)
            {
                if (seen.Add(session.BodyIndex))
                    indexes.Add(session.BodyIndex);
            }
        }
    }

    internal IReadOnlyList<MethodExceptionRegionInfo> ResolveExceptionRegions(
        int methodToken,
        out string? error)
    {
        using var context = PdbContext.Open(_assemblyPath);
        return context.ResolveExceptionRegions(methodToken, out error);
    }

    internal ImmutableArray<CallerEdge> CallerEdges(int methodToken)
    {
        Analysis.CallerResolutionPlan? resolution =
            TryTargetType(methodToken, out Analysis.TypeRef? target)
                ? Plan(target).Resolution
                : null;
        return Session.CallerEdges(
            methodToken,
            DirectCallerScopes(methodToken),
            resolution);
    }

    internal Analysis.CallTreeNode BuildCallTree(int methodToken) =>
        BodyIndex.BuildCallTree(methodToken);

    internal ILInspector.CallGraph.CallGraphProjection BuildCallGraph(
        int methodToken)
    {
        ILInspector.CallGraph.CallGraphProjection projection =
            Session.CallGraph(
                methodToken,
                CallerScopes(
                    includeAllocations: _includeGraphAllocations,
                    includeAsyncSiblingOpportunities:
                        _includeGraphOpportunities,
                    graphScope: true,
                    methodToken),
                CalleeScopes(),
                out Analysis.CatalogCallGraphDiagnostics diagnostics);
        _callGraphDiagnostics = diagnostics;
        return projection;
    }

    internal Analysis.CallTreeNode BuildCallerTree(int methodToken)
    {
        Analysis.CallTreeNode tree = Session.CallerTree(
            methodToken,
            CallerScopes(
                includeAllocations: _includeGraphAllocations,
                includeAsyncSiblingOpportunities:
                    _includeGraphOpportunities,
                graphScope: true,
                methodToken),
            out Analysis.CatalogCallGraphDiagnostics diagnostics);
        _callGraphDiagnostics = diagnostics;
        return tree;
    }

    internal Analysis.CatalogCallGraphDiagnostics CallGraphDiagnostics =>
        _callGraphDiagnostics;

    MethodBodyInspectionSession Session =>
        _session ??= MethodBodyInspectionSession.Open(
            TargetAssembly,
            ApiAnalysisInspection.CreateReferenceResolver(
                _assemblyPath,
                _options),
            _includeAllocations,
            _includeOpportunities,
            _bodyScope,
            includeAsyncSiblingOpportunities:
                _includeGraphOpportunities);

    ResolvedAssemblyReference TargetAssembly =>
        _targetAssembly ??= ResolvedAssemblyReference.CreateFromPath(
            _assemblyPath,
            AssemblyResolutionProvenance.Local("API member target"));

    IReadOnlyList<ResolvedAssemblyReference> ScopeCandidates
    {
        get
        {
            if (_scopeCandidates is not null)
                return _scopeCandidates;

            string targetPath = Path.GetFullPath(_assemblyPath);
            var candidates = new List<ResolvedAssemblyReference>();
            foreach (string path in _callerScopeAssemblies ?? [])
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path);
                }
                catch (Exception ex) when (
                    ex is ArgumentException
                        or NotSupportedException
                        or PathTooLongException)
                {
                    continue;
                }

                if (string.Equals(
                        fullPath,
                        targetPath,
                        StringComparison.Ordinal))
                {
                    candidates.Add(TargetAssembly);
                    continue;
                }

                if (ResolvedAssemblyReference.TryCreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Local("caller scope"),
                    out ResolvedAssemblyReference? candidate))
                {
                    candidates.Add(candidate);
                }
            }

            _scopeCandidates = candidates;
            return candidates;
        }
    }

    internal IReadOnlyList<MethodBodyInspectionSession>? CallerScopes(
        bool includeAllocations)
    {
        int? token = _methods
            .FirstOrDefault(method => method.MetadataToken.HasValue)
            ?.MetadataToken;
        token ??= Session.BodyIndex.Methods
            .FirstOrDefault()?.MetadataToken;
        return token.HasValue
            ? CallerScopes(
                includeAllocations,
                includeAsyncSiblingOpportunities:
                    includeAllocations
                        && _includeGraphOpportunities,
                graphScope: includeAllocations,
                token.Value)
            : null;
    }

    IReadOnlyList<MethodBodyInspectionSession>? CallerScopes(
        bool includeAllocations,
        bool includeAsyncSiblingOpportunities,
        bool graphScope,
        int methodToken)
    {
        ref List<MethodBodyInspectionSession>? cached =
            ref graphScope
                ? ref _graphScopes
                : ref _callerScopes;
        ref bool resolved =
            ref graphScope
                ? ref _graphScopesResolved
                : ref _callerScopesResolved;
        if (resolved)
            return cached;

        resolved = true;
        if (_callerScopeAssemblies is not { Count: > 0 })
            return null;

        if (!TryTargetType(methodToken, out Analysis.TypeRef? target))
        {
            List<MethodBodyInspectionSession> unfiltered =
                OpenScopes(
                    ScopeCandidates,
                    includeAllocations,
                    includeAsyncSiblingOpportunities);
            if (unfiltered.Count == 0)
                return null;

            cached = unfiltered;
            return cached;
        }

        Analysis.CallerScopeReachabilityPlan plan = Plan(target);
        List<MethodBodyInspectionSession> opened =
            OpenScopes(
                plan.GraphCandidates,
                includeAllocations,
                includeAsyncSiblingOpportunities);
        if (opened.Count == 0
            && !plan.HasRuledOutCandidateNotDefinitelyUnopenable)
        {
            return null;
        }

        cached = opened;
        return cached;
    }

    internal IReadOnlyList<MethodBodyInspectionSession>? CalleeScopes()
    {
        if (_calleeScopesResolved)
            return _calleeScopes;

        _calleeScopesResolved = true;
        if (_callerScopeAssemblies is not { Count: > 0 })
            return null;

        List<MethodBodyInspectionSession> opened =
            OpenScopes(
                ForwardScopeCandidates(),
                _includeGraphAllocations,
                _includeGraphOpportunities);
        if (opened.Count == 0)
            return null;

        _calleeScopes = opened;
        return _calleeScopes;
    }

    IReadOnlyList<ResolvedAssemblyReference> ForwardScopeCandidates()
    {
        var byName = ScopeCandidates
            .GroupBy(
                candidate => candidate.Identity.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<AssemblyAcquisitionRegistration>(
            ReferenceEqualityComparer.Instance);
        var pending = new Queue<ResolvedAssemblyReference>();

        if (!TryReferenceNames(
                TargetAssembly,
                out IReadOnlyList<string>? references))
        {
            return ScopeCandidates;
        }

        Enqueue(references);
        while (pending.TryDequeue(out ResolvedAssemblyReference? candidate))
        {
            if (!selected.Add(candidate.Registration))
                continue;

            if (!TryReferenceNames(
                    candidate,
                    out IReadOnlyList<string>? candidateReferences))
            {
                return ScopeCandidates;
            }

            Enqueue(candidateReferences);
        }

        return
        [
            .. ScopeCandidates.Where(candidate =>
                selected.Contains(candidate.Registration)),
        ];

        void Enqueue(IReadOnlyList<string> names)
        {
            foreach (string name in names)
            {
                if (!byName.TryGetValue(
                        name,
                        out ResolvedAssemblyReference[]? matches))
                {
                    continue;
                }

                foreach (ResolvedAssemblyReference match in matches)
                    pending.Enqueue(match);
            }
        }
    }

    static bool TryReferenceNames(
        ResolvedAssemblyReference assembly,
        [NotNullWhen(true)]
        out IReadOnlyList<string>? references)
    {
        if (assembly.Path is null)
        {
            references = null;
            return false;
        }

        try
        {
            AssemblyIdentityNames names =
                AssemblyIdentityScanner.Scan(assembly.Path);
            references = names.ReferenceNames;
            return names.ReferencesComplete;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or ArgumentException
                or NotSupportedException)
        {
            references = null;
            return false;
        }
    }

    internal IReadOnlyList<MethodBodyInspectionSession>? DirectCallerScopes(
        int methodToken)
    {
        if (_callerScopeAssemblies is not { Count: > 0 })
            return null;

        if (_graphScopesResolved && _graphScopes is not null)
            return _graphScopes;
        if (_callerScopesResolved && _callerScopes is not null)
            return _callerScopes;

        if (!TryTargetType(methodToken, out Analysis.TypeRef? target))
        {
            return CallerScopes(
                includeAllocations: false,
                includeAsyncSiblingOpportunities: false,
                graphScope: false,
                methodToken);
        }

        Analysis.CallerScopeReachabilityPlan plan = Plan(target);
        if (_directCallerScopes.TryGetValue(target, out var cached))
            return cached;

        List<MethodBodyInspectionSession> opened =
            OpenScopes(
                plan.DirectCandidates,
                includeAllocations: false);
        _directCallerScopes.Add(target, opened);
        return opened;
    }

    Analysis.CallerScopeReachabilityPlan Plan(Analysis.TypeRef target)
    {
        if (_plans.TryGetValue(target, out var plan))
            return plan;

        var bindingPolicy = new CallerBindingPolicy(
            TargetAssembly,
            ScopeCandidates,
            _options);
        plan = Analysis.CallerScopeReachabilityPlan.Create(
            bindingPolicy,
            TargetAssembly,
            target,
            ScopeCandidates);
        _plans.Add(target, plan);
        return plan;
    }

    bool TryTargetType(
        int methodToken,
        [NotNullWhen(true)]
        out Analysis.TypeRef? target)
    {
        Analysis.MethodIdentity? method = Session.BodyIndex.DeclaredMethods
            .FirstOrDefault(candidate =>
                candidate.MetadataToken == methodToken);
        if (method is null)
        {
            target = null;
            return false;
        }

        Analysis.TypeRef openDeclaringType =
            Analysis.GenericMemberIdentity.OpenDeclaringType(
            method.DeclaringType);
        if (openDeclaringType.Resolution is null)
        {
            target = null;
            return false;
        }

        target = openDeclaringType;
        return true;
    }

    List<MethodBodyInspectionSession> OpenScopes(
        IReadOnlyList<ResolvedAssemblyReference> candidates,
        bool includeAllocations,
        bool includeAsyncSiblingOpportunities = false)
    {
        var opened = new List<MethodBodyInspectionSession>();
        foreach (ResolvedAssemblyReference candidate in candidates)
        {
            if (candidate.Path is null)
                continue;

            try
            {
                opened.Add(MethodBodyInspectionSession.Open(
                    candidate,
                    ApiAnalysisInspection.CreateReferenceResolver(
                        candidate.Path,
                        _options),
                    includeAllocations,
                    includeOpportunities: false,
                    includeAsyncSiblingOpportunities:
                        includeAsyncSiblingOpportunities));
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException)
            {
                // Caller scope is best-effort; unreadable assemblies cannot
                // contribute body edges.
            }
        }

        return opened;
    }

    sealed class CallerBindingPolicy : IAssemblyBindingPolicy
    {
        readonly object _gate = new();
        readonly ApiOptions? _options;
        readonly string _targetPath;
        readonly Lazy<IAssemblyBindingPolicy> _default;
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            Lazy<IAssemblyBindingPolicy>> _byOrigin =
                new(ReferenceEqualityComparer.Instance);

        internal CallerBindingPolicy(
            ResolvedAssemblyReference target,
            IReadOnlyList<ResolvedAssemblyReference> candidates,
            ApiOptions? options)
        {
            _options = options;
            _targetPath = target.Path
                ?? throw new ArgumentException(
                    "Caller binding requires a path-backed target.",
                    nameof(target));
            Register(target);
            _default = _byOrigin[target.Registration];
            foreach (ResolvedAssemblyReference candidate in candidates)
                Register(candidate);
        }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            Lazy<IAssemblyBindingPolicy> policy = _default;
            if (request.Origin
                is AssemblyBindingOrigin.RequestingAssembly origin)
            {
                lock (_gate)
                {
                    if (_byOrigin.TryGetValue(
                            origin.Registration,
                            out Lazy<IAssemblyBindingPolicy>? originPolicy))
                    {
                        policy = originPolicy;
                    }
                }
            }

            AssemblyBindingSelection selection =
                AssemblyBindingSelection.ValidateForRequest(
                    request,
                    policy.Value.Select(request));
            switch (selection)
            {
                case AssemblyBindingSelection.Selected selected:
                    Register(selected.Assembly);
                    break;
                case AssemblyBindingSelection.Ambiguous ambiguous:
                    foreach (ResolvedAssemblyReference assembly
                        in ambiguous.Assemblies)
                    {
                        Register(assembly);
                    }
                    break;
            }

            return selection;
        }

        IAssemblyBindingPolicy PolicyFor(
            ResolvedAssemblyReference assembly) =>
            new AssemblyReferenceBindingPolicy(
                ApiAnalysisInspection.CreateReferenceResolver(
                    assembly.Path ?? _targetPath,
                    _options));

        void Register(ResolvedAssemblyReference assembly)
        {
            lock (_gate)
            {
                _byOrigin.TryAdd(
                    assembly.Registration,
                    new Lazy<IAssemblyBindingPolicy>(
                        () => PolicyFor(assembly),
                        LazyThreadSafetyMode.ExecutionAndPublication));
            }
        }
    }
}
