using System.Collections.Immutable;
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
            _includeAllocations = true;
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

    internal IReadOnlyList<MethodExceptionRegionInfo> ResolveExceptionRegions(
        int methodToken,
        out string? error)
    {
        using var context = PdbContext.Open(_assemblyPath);
        return context.ResolveExceptionRegions(methodToken, out error);
    }

    internal ImmutableArray<CallerEdge> CallerEdges(int methodToken)
    {
        Analysis.CallerScopeReachabilityPlan plan = Plan(methodToken);
        return Session.CallerEdges(
            methodToken,
            DirectCallerScopes(methodToken),
            plan.Resolution);
    }

    internal Analysis.CallTreeNode BuildCallTree(int methodToken) =>
        BodyIndex.BuildCallTree(methodToken);

    internal Analysis.CallTreeNode BuildCallerTree(int methodToken) =>
        Session.CallerTree(
            methodToken,
            CallerScopes(
                includeAllocations: _includeAllocations,
                methodToken));

    MethodBodyInspectionSession Session =>
        _session ??= MethodBodyInspectionSession.Open(
            TargetAssembly,
            ApiAnalysisInspection.CreateReferenceResolver(
                _assemblyPath,
                _options),
            _includeAllocations,
            _includeOpportunities,
            _bodyScope);

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
            ? CallerScopes(includeAllocations, token.Value)
            : null;
    }

    IReadOnlyList<MethodBodyInspectionSession>? CallerScopes(
        bool includeAllocations,
        int methodToken)
    {
        ref List<MethodBodyInspectionSession>? cached =
            ref includeAllocations
                ? ref _graphScopes
                : ref _callerScopes;
        ref bool resolved =
            ref includeAllocations
                ? ref _graphScopesResolved
                : ref _callerScopesResolved;
        if (resolved)
            return cached;

        resolved = true;
        if (_callerScopeAssemblies is not { Count: > 0 })
            return null;

        Analysis.CallerScopeReachabilityPlan plan = Plan(methodToken);
        List<MethodBodyInspectionSession> opened =
            OpenScopes(plan.GraphCandidates, includeAllocations);
        if (opened.Count == 0
            && !plan.HasRuledOutCandidateNotDefinitelyUnopenable)
        {
            return null;
        }

        cached = opened;
        return cached;
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

        Analysis.CallerScopeReachabilityPlan plan = Plan(methodToken);
        Analysis.TypeRef target = TargetType(methodToken);
        if (_directCallerScopes.TryGetValue(target, out var cached))
            return cached;

        List<MethodBodyInspectionSession> opened =
            OpenScopes(
                plan.DirectCandidates,
                includeAllocations: false);
        _directCallerScopes.Add(target, opened);
        return opened;
    }

    Analysis.CallerScopeReachabilityPlan Plan(int methodToken)
    {
        Analysis.TypeRef target = TargetType(methodToken);
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

    Analysis.TypeRef TargetType(int methodToken)
    {
        Analysis.MethodIdentity? method = Session.BodyIndex.Methods
            .FirstOrDefault(candidate =>
                candidate.MetadataToken == methodToken);
        if (method is null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(methodToken),
                "The target method is not present in the body index.");
        }

        return Analysis.GenericMemberIdentity.OpenDeclaringType(
            method.DeclaringType);
    }

    List<MethodBodyInspectionSession> OpenScopes(
        IReadOnlyList<ResolvedAssemblyReference> candidates,
        bool includeAllocations)
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
                    includeOpportunities: false));
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
                policy.Value.Select(request);
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
