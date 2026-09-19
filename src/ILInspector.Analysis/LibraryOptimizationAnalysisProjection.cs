using System.Collections.Immutable;
using Inspector.Findings;

namespace ILInspector.Analysis;

internal sealed class LibraryOptimizationAnalysisProjection
{
    private const int AllocationHotspotThreshold = 16;

    private readonly LibraryBodyAnalysisFeatures _features;
    private readonly ImmutableArray<MethodIdentity> _declaredMethods;
    private readonly ImmutableArray<MethodIdentity> _methods;
    private readonly ImmutableArray<DirectCall> _directCalls;
    private readonly ImmutableArray<DirectCall> _physicalDirectCalls;
    private readonly IReadOnlyDictionary<
        int,
        ImmutableArray<AllocationOccurrence>> _allocationOccurrences;
    private readonly ImmutableArray<OptimizationOpportunity>
        _rawOpportunities;
    private readonly IReadOnlySet<int> _suppressedMethodTokens;
    private readonly IReadOnlySet<int> _scopeExcludedMethodTokens;
    private readonly IReadOnlySet<string> _exceptionTypeNames;
    private readonly string? _moduleName;
    private MethodDefinitionMap? _declaredMethodMap;
    private ImmutableArray<OptimizationOpportunity> _opportunities;
    private ImmutableArray<OptimizationOpportunity> _allocationFanoutOpportunities;
    private IReadOnlyDictionary<int, CallerLoopEvidence>? _directCallerLoops;
    private Dictionary<int, int>? _rootReachByToken;

    internal LibraryOptimizationAnalysisProjection(
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisFeatures features,
        string? moduleName,
        ImmutableArray<DirectCall> physicalDirectCalls)
    {
        _features = features;
        _declaredMethods = analysis.Methods.DeclaredMethods;
        _methods = analysis.Methods.Methods;
        _directCalls = analysis.Methods.DirectCalls;
        _physicalDirectCalls = physicalDirectCalls;
        _allocationOccurrences =
            analysis.Allocations.Occurrences;
        _rawOpportunities =
            analysis.Optimizations.Opportunities;
        _suppressedMethodTokens =
            analysis.Optimizations.SuppressedMethodTokens;
        _scopeExcludedMethodTokens =
            analysis.Optimizations.ScopeExcludedMethodTokens;
        _exceptionTypeNames =
            analysis.Optimizations.ExceptionTypeNames;
        _moduleName = moduleName;
    }

    internal ImmutableArray<OptimizationOpportunity> Opportunities
    {
        get
        {
            if (!ProducesOpportunities)
                return [];
            if (_opportunities.IsDefault)
            {
                Dictionary<int, int> reachByToken = RootReachByToken;
                ImmutableArray<OptimizationOpportunity> raw =
                [
                    .. _rawOpportunities.Select(
                        opportunity =>
                        {
                            int reach = reachByToken.TryGetValue(
                                opportunity.Method.MetadataToken,
                                out int value)
                                    ? value
                                    : opportunity.RootReach;
                            OptimizationOpportunity adjusted =
                                reach != opportunity.RootReach
                                    ? opportunity with { RootReach = reach }
                                    : opportunity;
                            adjusted = MarkAmortizedSetup(adjusted);
                            string confidence =
                                adjusted.ColdPath || adjusted.Amortized
                                    ? "low"
                                    : OptimizationOpportunityAnalysis
                                        .AdjustDelegateConfidenceForReach(
                                            adjusted.Shape,
                                            adjusted.InLoop,
                                            adjusted.Confidence,
                                            reach);
                            adjusted =
                                confidence != adjusted.Confidence
                                    ? adjusted with
                                    {
                                        Confidence = confidence,
                                    }
                                    : adjusted;
                            return OptimizationOpportunityAnalysis
                                .AddFallbackMetadata(adjusted);
                        }),
                ];
                ImmutableArray<OptimizationOpportunity> opportunities =
                    ProducesAllocationOpportunities
                        ?
                        [
                            .. raw,
                            .. AllocationHotspots(
                                    reachByToken,
                                    new HashSet<int>(
                                        _rawOpportunities
                                            .Where(opportunity =>
                                                opportunity.Shape
                                                    != "sync-call-in-async"
                                                && !(opportunity.Shape
                                                        == "async-state-machine"
                                                    && opportunity.Amortized))
                                            .Select(opportunity =>
                                                opportunity.Method.MetadataToken)))
                                .Select(OptimizationOpportunityAnalysis
                                    .AddFallbackMetadata),
                            .. RepeatedScanAnalysis.Collect(
                                    _methods,
                                    _physicalDirectCalls,
                                    _rawOpportunities,
                                    _suppressedMethodTokens,
                                    reachByToken,
                                    DeclaredMethodMap)
                                .Select(OptimizationOpportunityAnalysis
                                    .AddFallbackMetadata),
                        ]
                        : raw;
                _opportunities = AttachCallerLoopEvidence(
                    AttachFindingProvenance(opportunities),
                    DirectCallerLoops);
            }

            return _opportunities;
        }
    }

    internal ImmutableArray<OptimizationOpportunity>
        AllocationFanoutOpportunities
    {
        get
        {
            if (!ProducesOpportunities)
                return [];
            if (_allocationFanoutOpportunities.IsDefault)
            {
                Dictionary<int, int> reachByToken = RootReachByToken;
                _allocationFanoutOpportunities =
                    AttachCallerLoopEvidence(
                        AttachFindingProvenance(
                        [
                            .. AllocationFanout.Analyze(
                                    _methods,
                                    ClassifyExactCallTargets(
                                        _physicalDirectCalls,
                                        DeclaredMethodMap,
                                        _methods),
                                    _allocationOccurrences,
                                    _scopeExcludedMethodTokens,
                                    DeclaredMethodMap)
                                .Where(summary =>
                                    !_scopeExcludedMethodTokens
                                        .Contains(
                                            summary.Method.MetadataToken))
                                .Select(summary => new OptimizationOpportunity(
                                    summary.Method,
                                    "allocation-fanout",
                                    $"Known IL-visible impact: direct-sites={summary.DirectSites}, once-paths={summary.OncePaths}, conditional-paths={summary.ConditionalPaths}, repeated-paths={summary.RepeatedPaths}, unknown-paths={summary.UnknownPaths}, cached-sites={summary.CachedSites}, opaque-paths={summary.OpaquePaths}.",
                                    "Inspect the exact allocation findings and call paths; consolidate repeated setup or dispatch object construction when lifecycle measurements show it is unnecessary.",
                                    summary.UnknownPaths == 0
                                        && summary.OpaquePaths == 0
                                        && !summary.Saturated
                                            ? "high"
                                            : "medium",
                                    summary.RepeatedPaths > 0,
                                    ILOffset: null,
                                    "This is a static lower bound over IL-visible allocations. External, virtual, delegate, recursive, and runtime-library allocation effects remain opaque.",
                                    reachByToken.GetValueOrDefault(
                                        summary.Method.MetadataToken))
                                {
                                    CandidateId = null,
                                    Provenance =
                                        PerformanceTriageProvenance.Aggregate,
                                    DirectAllocationSites =
                                        summary.DirectSites,
                                    OnceAllocationPaths =
                                        summary.OncePaths,
                                    ConditionalAllocationPaths =
                                        summary.ConditionalPaths,
                                    RepeatedAllocationPaths =
                                        summary.RepeatedPaths,
                                    UnknownAllocationPaths =
                                        summary.UnknownPaths,
                                    CachedAllocationSites =
                                        summary.CachedSites,
                                    OpaqueCallPaths =
                                        summary.OpaquePaths,
                                    AllocationCountSaturated =
                                        summary.Saturated,
                                }),
                        ]),
                        DirectCallerLoops);
            }

            return _allocationFanoutOpportunities;
        }
    }

    private bool ProducesOpportunities =>
        (_features
            & (LibraryBodyAnalysisFeatures.OptimizationOpportunities
                | LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities)) != 0;

    private bool ProducesAllocationOpportunities =>
        _features.HasFlag(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);

    private IReadOnlyDictionary<int, CallerLoopEvidence> DirectCallerLoops =>
        _directCallerLoops ??= CallerLoopEvidenceAnalysis.FindNearest(
            _methods,
            _physicalDirectCalls,
            maxDepth: 1,
            DeclaredMethodMap);

    private Dictionary<int, int> RootReachByToken
    {
        get
        {
            if (_rootReachByToken is null)
            {
                var reachByToken = new Dictionary<int, int>();
                foreach (MethodLeverage entry in MethodLeverageRanking.Top(
                    _directCalls,
                    _methods,
                    int.MaxValue,
                    scope: null,
                    maxDepth: 64,
                    DeclaredMethodMap))
                {
                    reachByToken[entry.Method.MetadataToken] =
                        entry.RootReach;
                }

                _rootReachByToken = reachByToken;
            }

            return _rootReachByToken;
        }
    }

    private static ImmutableArray<OptimizationOpportunity>
        AttachCallerLoopEvidence(
            ImmutableArray<OptimizationOpportunity> opportunities,
            IReadOnlyDictionary<int, CallerLoopEvidence> evidenceByMethod) =>
        [
            .. opportunities.Select(opportunity =>
                evidenceByMethod.TryGetValue(
                    opportunity.Method.MetadataToken,
                    out CallerLoopEvidence? evidence)
                    ? opportunity with { CallerLoop = evidence }
                    : opportunity),
        ];

    private static ImmutableArray<DirectCall> ClassifyExactCallTargets(
        ImmutableArray<DirectCall> calls,
        MethodDefinitionMap declarationMap,
        ImmutableArray<MethodIdentity> methods)
    {
        Dictionary<int, MethodIdentity> methodsByToken =
            methods.ToDictionary(
                static method => method.MetadataToken);
        return
        [
            .. calls.Select(call =>
            {
                int targetToken =
                    declarationMap.Resolve(call);
                bool exact =
                    methodsByToken.TryGetValue(
                        targetToken,
                        out MethodIdentity? target)
                    && CatalogCallGraphScope.IsResolvedExactTarget(
                        call.Kind,
                        target);
                return call with { ExactTarget = exact };
            }),
        ];
    }

    private ImmutableArray<OptimizationOpportunity> AttachFindingProvenance(
        ImmutableArray<OptimizationOpportunity> opportunities)
    {
        var allocationFindings =
            new Dictionary<
                int,
                ImmutableArray<Finding<AllocationOccurrence>>>();
        var callSiteFindings =
            new Dictionary<
                int,
                ImmutableArray<Finding<DirectCall>>>();
        Dictionary<int, ImmutableArray<DirectCall>> physicalCallsByCaller =
            _physicalDirectCalls
                .GroupBy(call => call.Caller.MetadataToken)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToImmutableArray());
        var candidateIds =
            new HashSet<string>(StringComparer.Ordinal);
        var builder =
            ImmutableArray.CreateBuilder<OptimizationOpportunity>(
                opportunities.Length);

        foreach (OptimizationOpportunity opportunity in opportunities)
        {
            Finding<AllocationOccurrence>? allocation = null;
            Finding<DirectCall>? callSite = null;
            Finding<DirectCall>? supportingCallSite = null;
            bool attachFinding =
                opportunity.Shape != "generic-parameter-object-box";
            if (attachFinding && opportunity.ILOffset is { } offset)
            {
                int methodToken =
                    opportunity.Method.MetadataToken;
                int evidenceMethodToken =
                    opportunity.EvidenceMethodToken ?? methodToken;
                if (opportunity.Shape != "sync-call-in-async"
                    && _allocationOccurrences.TryGetValue(
                        evidenceMethodToken,
                        out ImmutableArray<AllocationOccurrence>
                            occurrences))
                {
                    if (!allocationFindings.TryGetValue(
                            evidenceMethodToken,
                            out ImmutableArray<
                                Finding<AllocationOccurrence>> findings))
                    {
                        findings = AnalysisFindings.InspectAllocations(
                            occurrences,
                            FindingSubjectFor(
                                DeclaredMethod(evidenceMethodToken)
                                    ?? opportunity.Method));
                        allocationFindings[evidenceMethodToken] =
                            findings;
                    }

                    allocation = SingleFindingAtOffset(
                        findings,
                        offset,
                        static occurrence => occurrence.ILOffset);
                }

                if (allocation is null
                    && physicalCallsByCaller.TryGetValue(
                        evidenceMethodToken,
                        out ImmutableArray<DirectCall> calls))
                {
                    if (!callSiteFindings.TryGetValue(
                            evidenceMethodToken,
                            out ImmutableArray<
                                Finding<DirectCall>> findings))
                    {
                        findings = AnalysisFindings.InspectCallSites(
                            calls,
                            FindingSubjectFor(calls[0].Caller));
                        callSiteFindings[evidenceMethodToken] =
                            findings;
                    }

                    callSite = SingleFindingAtOffset(
                        findings,
                        offset,
                        static call => call.ILOffset);
                }
            }

            if (opportunity.SupportingCallSite is { } supportSite
                && physicalCallsByCaller.TryGetValue(
                    supportSite.EvidenceMethodToken,
                    out ImmutableArray<DirectCall> supportingCalls))
            {
                if (!callSiteFindings.TryGetValue(
                        supportSite.EvidenceMethodToken,
                        out ImmutableArray<Finding<DirectCall>>
                            findings))
                {
                    findings = AnalysisFindings.InspectCallSites(
                        supportingCalls,
                        FindingSubjectFor(
                            supportingCalls[0].Caller));
                    callSiteFindings[
                        supportSite.EvidenceMethodToken] =
                        findings;
                }

                supportingCallSite = SingleFindingAtOffset(
                    findings,
                    supportSite.ILOffset,
                    static call => call.ILOffset);
            }

            string? sourceFinding =
                allocation?.Descriptor.Id
                ?? callSite?.Descriptor.Id
                ?? opportunity.SourceFinding;
            FindingKey? findingKey =
                allocation?.Key ?? callSite?.Key;
            int? ordinal =
                allocation?.Ordinal ?? callSite?.Ordinal;
            int fingerprintLength =
                PerformanceTriageCandidateId.InitialFingerprintLength;
            string candidateId;
            while (true)
            {
                candidateId =
                    PerformanceTriageCandidateId.Create(
                        opportunity,
                        sourceFinding,
                        findingKey,
                        ordinal,
                        fingerprintLength);
                if (candidateIds.Add(candidateId))
                    break;
                if (fingerprintLength
                    == PerformanceTriageCandidateId
                        .MaximumFingerprintLength)
                {
                    throw new InvalidOperationException(
                        $"Duplicate Performance Triage candidate identity '{candidateId}'.");
                }

                fingerprintLength = Math.Min(
                    fingerprintLength + 8,
                    PerformanceTriageCandidateId
                        .MaximumFingerprintLength);
            }

            builder.Add(opportunity with
            {
                CandidateId = candidateId,
                SourceFinding = sourceFinding,
                Operation = allocation is null
                    ? CallOperation(callSite?.Payload)
                    : AllocationOperation(allocation.Payload),
                OperandToken =
                    allocation?.Payload.OperandToken
                    ?? callSite?.Payload.OperandToken,
                SupportingCallSite =
                    opportunity.SupportingCallSite is not
                        { } supportCoordinate
                        ? null
                        : supportCoordinate with
                        {
                            SourceFinding =
                                supportingCallSite
                                    ?.Descriptor.Id,
                            Operation = CallOperation(
                                supportingCallSite
                                    ?.Payload),
                            OperandToken =
                                supportingCallSite
                                    ?.Payload.OperandToken,
                        },
                Provenance =
                    opportunity.Provenance
                        != PerformanceTriageProvenance.Unknown
                        ? opportunity.Provenance
                        : sourceFinding is not null
                            ? PerformanceTriageProvenance.Exact
                            : opportunity.ILOffset is null
                                ? PerformanceTriageProvenance.Aggregate
                                : PerformanceTriageProvenance.Unmatched,
            });
        }

        return builder.MoveToImmutable();
    }

    private static FindingSubject FindingSubjectFor(
        MethodIdentity method) =>
        new(
            $"method:0x{method.MetadataToken:X8}",
            $"{method.DeclaringType.ToQualifiedDisplayString()}::{method.Name}");

    private static Finding<T>? SingleFindingAtOffset<T>(
        ImmutableArray<Finding<T>> findings,
        int offset,
        Func<T, int> getOffset)
        where T : notnull
    {
        Finding<T>? result = null;
        foreach (Finding<T> finding in findings)
        {
            if (getOffset(finding.Payload) != offset)
                continue;
            if (result is not null)
            {
                throw new InvalidOperationException(
                    $"Finding census '{finding.Descriptor.Id}' contains multiple occurrences at IL_{offset:X4}.");
            }

            result = finding;
        }

        return result;
    }

    private static string AllocationOperation(
        AllocationOccurrence occurrence) =>
        occurrence.Source switch
        {
            AllocationFactSource.Newobj => "newobj",
            AllocationFactSource.Newarr => "newarr",
            AllocationFactSource.Box => "box",
            AllocationFactSource.GetEnumeratorCall =>
                "call.get-enumerator",
            _ => occurrence.Source.ToString().ToLowerInvariant(),
        };

    private static string? CallOperation(DirectCall? call) =>
        call is null
            ? null
            : string.IsNullOrWhiteSpace(call.Opcode)
                ? call.Kind.ToString().ToLowerInvariant()
                : call.Opcode;

    private bool IsExceptionConstruction(TypeRef type)
    {
        TypeRef definition =
            type.Kind == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        if (_exceptionTypeNames.Contains(
                definition.ToQualifiedDisplayString()))
        {
            return true;
        }

        if (_methods.Length > 0
            && definition.Assembly
                == _methods[0].AssemblyName)
        {
            return false;
        }

        return definition.Name.EndsWith(
            "Exception",
            StringComparison.Ordinal);
    }

    private OptimizationOpportunity MarkAmortizedSetup(
        OptimizationOpportunity opportunity)
    {
        if (opportunity.Amortized)
            return opportunity;
        if (opportunity.Method.Name is not (".ctor" or ".cctor"))
            return opportunity;
        if (opportunity.Method.Name == ".ctor"
            && ConstructorIsInvokedInLoop(opportunity.Method))
        {
            return opportunity;
        }

        return opportunity with
        {
            Amortized = true,
            SafeFixDirection =
                "This allocation is in constructor/type-initializer setup, not a steady-state per-call path. Optimize only if profiles show this setup is hot or repeated unexpectedly.",
            Caveat =
                "Amortized setup path: constructor/type-initializer allocations are usually once per instance/type, not per steady-state operation.",
        };
    }

    private bool ConstructorIsInvokedInLoop(
        MethodIdentity constructor) =>
        _directCalls.Any(call =>
            call.Kind == CallKind.NewObject
            && call.InLoop
            && DeclaredMethodMap.Resolve(call)
                == constructor.MetadataToken);

    private MethodDefinitionMap DeclaredMethodMap =>
        _declaredMethodMap ??=
            MethodDefinitionMap.Create(
                _declaredMethods,
                _moduleName);

    private IEnumerable<OptimizationOpportunity> AllocationHotspots(
        Dictionary<int, int> reachByToken,
        IReadOnlySet<int> methodsWithSpecificShape)
    {
        var methodByToken =
            new Dictionary<int, MethodIdentity>(
                _methods.Length);
        foreach (MethodIdentity method in _methods)
            methodByToken[method.MetadataToken] = method;

        var steadyAllocations = new Dictionary<int, int>();
        var steadyAllocationLoop = new HashSet<int>();
        foreach (var (token, occurrences)
            in _allocationOccurrences)
        {
            foreach (AllocationOccurrence occurrence in occurrences)
            {
                if (!occurrence.CountsAsHeapAllocation)
                    continue;
                if (occurrence.Escape == AllocationEscape.ThrowPath)
                    continue;
                if (occurrence.Kind == AllocationKind.Object
                    && occurrence.AllocatedType is { } type
                    && IsExceptionConstruction(type))
                {
                    continue;
                }

                steadyAllocations[token] =
                    steadyAllocations.GetValueOrDefault(token) + 1;
                if (occurrence.Multiplicity
                    == AllocationMultiplicity.Loop)
                {
                    steadyAllocationLoop.Add(token);
                }
            }
        }

        foreach ((int token, MethodIdentity method) in methodByToken)
        {
            if (_suppressedMethodTokens.Contains(token))
            {
                continue;
            }

            if (methodsWithSpecificShape.Contains(token))
                continue;
            bool inLoop = steadyAllocationLoop.Contains(token);
            if (!inLoop)
                continue;
            int allocations =
                steadyAllocations.GetValueOrDefault(token);
            if (allocations < AllocationHotspotThreshold)
                continue;

            yield return new(
                method,
                "allocation-hotspot",
                $"{allocations} heap allocations in a loop (newobj/newarr/box)",
                "Many allocations in one loop are often reducible: pool or cache reused objects, use spans/stackalloc for transient buffers, and avoid intermediate collections on hot paths.",
                "medium",
                true,
                null,
                "Aggregate loop-allocation density (excludes exception construction); some may be intrinsic object construction. Review the loop body for reducible temporaries.",
                reachByToken.GetValueOrDefault(token));
        }
    }

    private MethodIdentity? DeclaredMethod(int metadataToken)
    {
        int low = 0;
        int high =
            _declaredMethods.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            MethodIdentity candidate =
                _declaredMethods[middle];
            if (candidate.MetadataToken == metadataToken)
                return candidate;
            if (candidate.MetadataToken < metadataToken)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return null;
    }
}
