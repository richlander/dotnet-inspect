using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.ControlFlow;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Selects the Analysis producers that participate in one assembly body
/// acquisition.
/// </summary>
[Flags]
public enum LibraryBodyAnalysisFeatures
{
    /// <summary>Acquire no body-analysis evidence.</summary>
    None = 0,
    /// <summary>Produce calls, unsafe evidence, and method/body signals.</summary>
    MethodEvidence = 1 << 0,
    /// <summary>Produce allocation occurrences; implies <see cref="MethodEvidence"/>.</summary>
    Allocations = 1 << 1,
    /// <summary>
    /// Produce optimization opportunities; implies <see cref="Allocations"/>.
    /// </summary>
    OptimizationOpportunities = 1 << 2,
    /// <summary>Produce the whole-assembly ArrayPool lifecycle census.</summary>
    LeakTriage = 1 << 3,
    /// <summary>
    /// Produce compact body-scoped ArrayPool ownership-flow summaries.
    /// </summary>
    OwnershipFlow = 1 << 4,
    /// <summary>
    /// Produce sync-call-in-async opportunities; implies
    /// <see cref="MethodEvidence"/>.
    /// </summary>
    AsyncSiblingOpportunities = 1 << 5,
    /// <summary>
    /// Produce call argument provenance and return-sink value flow required to
    /// authenticate source-generated System.Text.Json wire contracts. A scoped
    /// body census withholds async state-machine field provenance whose
    /// validity depends on the absence of writes in other bodies.
    /// </summary>
    JsonWireContractFlow = 1 << 6,
    /// <summary>The body-analysis features used by the general index.</summary>
    Default = MethodEvidence
        | Allocations
        | OptimizationOpportunities
        | AsyncSiblingOpportunities,
    /// <summary>All available body-analysis producers.</summary>
    All = Default | LeakTriage | OwnershipFlow | JsonWireContractFlow,
}

/// <summary>
/// Materialized IL body evidence for one assembly.
/// <para>
/// Derived single-assembly call-graph maps are populated lazily on first use
/// and then retained, so an instance is not safe for concurrent use without
/// external synchronization — the same as the evidence accessors that already
/// cached this way. Use <see cref="ReleaseCallGraphCaches"/> to hand that
/// memory back. Cross-assembly graph storage belongs to
/// <see cref="CatalogCallGraphScope"/>.
/// </para>
/// </summary>
public sealed class LibraryBodyIndex
{
    LibraryBodyIndex(
        string path,
        LibraryBodyModuleIdentity moduleIdentity,
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisFeatures features)
    {
        Path = path;
        ModuleIdentity = moduleIdentity;
        DeclaredMethods = analysis.Methods.DeclaredMethods;
        Methods = analysis.Methods.Methods;
        DirectCalls = analysis.Methods.DirectCalls;
        ResultSinks = analysis.Methods.ResultSinks;
        FieldStores = analysis.Methods.FieldStores;
        FieldLoads = analysis.Methods.FieldLoads;
        ReturnFlows = analysis.Methods.ReturnFlows;
        _physicalDirectCalls =
        [
            .. DirectCalls.Select(static call =>
                call.Caller == call.EvidenceMethod
                    ? call
                    : call with
                    {
                        Caller = call.EvidenceMethod,
                    }),
        ];
        UnsafeEvidence = analysis.Safety.Evidence;
        Diagnostics = analysis.Diagnostics;
        _rawOpportunities = analysis.Optimizations.Opportunities;
        _opportunitiesComputed =
            (features
                & (LibraryBodyAnalysisFeatures.OptimizationOpportunities
                    | LibraryBodyAnalysisFeatures
                        .AsyncSiblingOpportunities)) != 0;
        _allocationOpportunitiesComputed =
            (features
                & LibraryBodyAnalysisFeatures.OptimizationOpportunities) != 0;
        _unsafeLeverageMethods = analysis.Safety.LeverageMethods;
        MemorySafetyRulesEnabled = analysis.Safety.UpdatedRulesEnabled;
        UnsafeModes = analysis.Safety.Modes;
        _bodySignals = analysis.Methods.BodySignals;
        _allocationOccurrences = analysis.Allocations.Occurrences;
        _unsafetyOccurrences = analysis.Safety.Occurrences;
        _inAssemblyTypeIsException =
            analysis.Methods.InAssemblyTypeIsException;
        _suppressedOpportunityTokens =
            analysis.Optimizations.SuppressedMethodTokens;
        _scopeExcludedOpportunityTokens =
            analysis.Optimizations.ScopeExcludedMethodTokens;
        _exceptionTypeNames = analysis.Optimizations.ExceptionTypeNames;
        _nonHeapNewObjOperandTokens =
            analysis.Methods.NonHeapNewObjOperandTokens;
        Features = features;
        _leakTriage = analysis.Resources.LeakTriage;
        ArrayPoolOwnership = analysis.OwnershipFlow.Methods;
        _declaredSources = analysis.Methods.DeclaredSources;
    }

    public string Path { get; }
    /// <summary>
    /// Exact image-derived identity for the module that produced this index.
    /// This remains available when no body producer or method is selected.
    /// </summary>
    public LibraryBodyModuleIdentity ModuleIdentity { get; }
    /// <summary>
    /// Every decoded method identity, including abstract and extern members,
    /// when <see cref="LibraryBodyAnalysisFeatures.MethodEvidence"/> is enabled.
    /// </summary>
    public ImmutableArray<MethodIdentity> DeclaredMethods { get; }
    /// <summary>
    /// Method identities whose definitions carry IL bodies, when
    /// <see cref="LibraryBodyAnalysisFeatures.MethodEvidence"/> is enabled.
    /// </summary>
    public ImmutableArray<MethodIdentity> Methods { get; }
    /// <summary>
    /// Direct call sites attributed to their declared source caller when the
    /// existing async or lifted-body resolver recognizes a synthesized body.
    /// <see cref="DirectCall.EvidenceMethod"/> retains the physical IL-body
    /// coordinate. <c>DirectCalls_AttributeAsyncCallSitesToSourceMethod</c> and
    /// <c>DirectCalls_AttributeLiftedBodiesButNotIterators</c> gate this
    /// contract and its iterator non-action boundary.
    /// </summary>
    public ImmutableArray<DirectCall> DirectCalls { get; }
    /// <summary>
    /// Conservative physical return and single-argument call sinks, with
    /// reaching-definition-backed direct-call provenance for their values,
    /// when <see cref="LibraryBodyAnalysisFeatures.JsonWireContractFlow"/> is
    /// requested.
    /// </summary>
    public ImmutableArray<MethodResultSink> ResultSinks { get; }

    /// <summary>
    /// Every physical <c>stsfld</c>/<c>stfld</c> site with the resolved
    /// provenance of the value it stores, when
    /// <see cref="LibraryBodyAnalysisFeatures.JsonWireContractFlow"/> is
    /// requested. Unproven stores are present with an unresolved value so a
    /// consumer asking "is this the only write to this field?" fails closed.
    /// </summary>
    public ImmutableArray<FieldStoreFact> FieldStores { get; }

    /// <summary>
    /// Every physical <c>ldsfld</c>/<c>ldfld</c>/<c>ldsflda</c>/<c>ldflda</c>
    /// site, with the receiver argument Analysis proved for an instance access
    /// and whether the field address escapes, when
    /// <see cref="LibraryBodyAnalysisFeatures.JsonWireContractFlow"/> is
    /// requested. The read/address counterpart of <see cref="FieldStores"/>,
    /// needed where a cached read never reaches a resolvable stack slot or an
    /// indirect write must invalidate stable provenance.
    /// </summary>
    public ImmutableArray<FieldLoadFact> FieldLoads { get; }

    /// <summary>
    /// The union of proven producers each non-void body can return, when
    /// <see cref="LibraryBodyAnalysisFeatures.JsonWireContractFlow"/> is
    /// requested. Present with an unresolved value whenever any reachable
    /// return went unproven, so a consumer asking "can this method return
    /// anything else?" fails closed.
    /// </summary>
    public ImmutableArray<MethodReturnFlow> ReturnFlows { get; }
    readonly ImmutableArray<DirectCall> _physicalDirectCalls;
    public ImmutableArray<UnsafeEvidence> UnsafeEvidence { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
    /// <summary>The normalized producers included in this index.</summary>
    public LibraryBodyAnalysisFeatures Features { get; }

    /// <summary>
    /// Compact per-method ArrayPool ownership summaries produced during the
    /// body walk. No IL or control-flow state is retained.
    /// </summary>
    public ImmutableArray<ArrayPoolOwnershipMethodEvidence>
        ArrayPoolOwnership { get; }

    readonly LeakTriageResult? _leakTriage;

    /// <summary>
    /// Gets the whole-assembly lifecycle census produced when
    /// <see cref="LibraryBodyAnalysisFeatures.LeakTriage"/> was requested.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The Leak Triage producer was not requested.
    /// </exception>
    public LeakTriageResult LeakTriage
        => _leakTriage
            ?? throw new InvalidOperationException(
                "Leak Triage was not requested for this body index.");

    readonly ImmutableArray<OptimizationOpportunity> _rawOpportunities;
    readonly bool _opportunitiesComputed;
    readonly bool _allocationOpportunitiesComputed;
    readonly ImmutableArray<MethodIdentity> _unsafeLeverageMethods;
    ImmutableArray<OptimizationOpportunity> _opportunities;
    ImmutableArray<OptimizationOpportunity> _allocationFanoutOpportunities;
    IReadOnlyDictionary<int, CallerLoopEvidence>? _directCallerLoops;
    Dictionary<int, int>? _rootReachByToken;
    IReadOnlyDictionary<int, ImmutableArray<DirectCall>>? _directCallsByCaller;
    IReadOnlyDictionary<int, ImmutableArray<DirectCall>>?
        _directCallsByEvidenceMethod;
    IReadOnlyDictionary<int, ImmutableArray<UnsafeEvidence>>? _unsafeEvidenceByMember;
    MethodDefinitionMap? _methodMap;
    IReadOnlyDictionary<int, int>? _distinctCallersByCallee;
    IReadOnlyDictionary<int, ImmutableArray<DirectCall>>? _distinctCallerEdgesByCallee;
    /// <summary>
    /// Drops the maps that back the single-assembly call-tree builders: the
    /// definition map, distinct-caller counts and edges, and direct-call
    /// grouping.
    /// <para>
    /// For a consumer under a hard memory ceiling that is done asking call-graph questions. This
    /// deliberately does <em>not</em> drop the evidence-domain caches — method signals, caller-loop
    /// evidence, root-reach roll-ups, unsafe-evidence grouping, generated-framework type sets, and
    /// the optimization-opportunity arrays — which serve other producers and together retain well
    /// under a megabyte. Cross-assembly storage is released through
    /// <see cref="CatalogCallGraphScope.ReleaseGraph"/>. Everything rebuilds
    /// on next use, so this only trades time for memory.
    /// </para>
    /// <para>
    /// <c>ReleaseMethods_DropExactlyTheCachesTheyDocument</c> derives this type's cache fields by
    /// reflection and fails if one is added, or moved across that boundary, without updating it.
    /// </para>
    /// </summary>
    public void ReleaseCallGraphCaches()
    {
        _methodMap = null;
        _distinctCallersByCallee = null;
        _distinctCallerEdgesByCallee = null;
        _directCallsByCaller = null;
        _directCallsByEvidenceMethod = null;
    }

    /// <summary>
    /// Source/IL optimization opportunities, each enriched with the containing method's
    /// <see cref="MethodLeverage.RootReach"/> so callers can prioritize the intersection
    /// of high-leverage methods and actionable rewrite shapes. Computed once on first
    /// access (the leverage join walks the whole-assembly call graph).
    /// </summary>
    public ImmutableArray<OptimizationOpportunity> OptimizationOpportunities
    {
        get
        {
            if (!_opportunitiesComputed)
                return ImmutableArray<OptimizationOpportunity>.Empty;
            if (_opportunities.IsDefault)
            {
                var reachByToken = RootReachByToken;
                ImmutableArray<OptimizationOpportunity> raw =
                [
                    .. _rawOpportunities.Select(opportunity =>
                    {
                        int reach = reachByToken.TryGetValue(
                            opportunity.Method.MetadataToken,
                            out int r)
                                ? r
                                : opportunity.RootReach;
                        var adjusted =
                            reach != opportunity.RootReach
                                ? opportunity with { RootReach = reach }
                                : opportunity;
                        adjusted = MarkAmortizedSetup(adjusted);
                        var confidence = IsLowFrequencyOpportunity(adjusted)
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
                    _allocationOpportunitiesComputed
                        ?
                        [
                            .. raw,
                            .. AllocationHotspots(
                                    reachByToken,
                                    new HashSet<int>(
                                        _rawOpportunities
                                            .Where(o =>
                                                o.Shape
                                                    != "sync-call-in-async"
                                                && !(o.Shape
                                                        == "async-state-machine"
                                                    && o.Amortized))
                                            .Select(o =>
                                                o.Method.MetadataToken)))
                                .Select(OptimizationOpportunityAnalysis
                                    .AddFallbackMetadata),
                            .. RepeatedScanAnalysis.Collect(
                                    Methods,
                                    _physicalDirectCalls,
                                    _rawOpportunities,
                                    _suppressedOpportunityTokens,
                                    reachByToken)
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

    static ImmutableArray<OptimizationOpportunity> AttachCallerLoopEvidence(
        ImmutableArray<OptimizationOpportunity> opportunities,
        IReadOnlyDictionary<int, CallerLoopEvidence> evidenceByMethod)
        => [.. opportunities.Select(opportunity =>
            evidenceByMethod.TryGetValue(opportunity.Method.MetadataToken, out var evidence)
                ? opportunity with { CallerLoop = evidence }
                : opportunity)];

    /// <summary>
    /// Opt-in allocation fanout rows. Each row carries a sound IL-visible lower bound through
    /// exact intra-assembly call targets; uncertain, recursive, virtual, and external calls are
    /// counted as opaque rather than assigned invented targets.
    /// </summary>
    public ImmutableArray<OptimizationOpportunity> AllocationFanoutOpportunities
    {
        get
        {
            if (!_opportunitiesComputed)
                return ImmutableArray<OptimizationOpportunity>.Empty;
            if (_allocationFanoutOpportunities.IsDefault)
            {
                var reachByToken = RootReachByToken;

                _allocationFanoutOpportunities = AttachCallerLoopEvidence(AttachFindingProvenance(
                [
                    .. AllocationFanout.Analyze(
                            Methods,
                            ClassifyExactCallTargets(
                                Path,
                                _physicalDirectCalls,
                                Methods),
                            _allocationOccurrences,
                            _scopeExcludedOpportunityTokens)
                        .Where(summary =>
                            !_scopeExcludedOpportunityTokens
                                .Contains(
                                    summary.Method.MetadataToken))
                        .Select(summary => new OptimizationOpportunity(
                            summary.Method,
                            "allocation-fanout",
                            $"Known IL-visible impact: direct-sites={summary.DirectSites}, once-paths={summary.OncePaths}, conditional-paths={summary.ConditionalPaths}, repeated-paths={summary.RepeatedPaths}, unknown-paths={summary.UnknownPaths}, cached-sites={summary.CachedSites}, opaque-paths={summary.OpaquePaths}.",
                            "Inspect the exact allocation findings and call paths; consolidate repeated setup or dispatch object construction when lifecycle measurements show it is unnecessary.",
                            summary.UnknownPaths == 0 && summary.OpaquePaths == 0 && !summary.Saturated ? "high" : "medium",
                            summary.RepeatedPaths > 0,
                            ILOffset: null,
                            "This is a static lower bound over IL-visible allocations. External, virtual, delegate, recursive, and runtime-library allocation effects remain opaque.",
                            reachByToken.GetValueOrDefault(summary.Method.MetadataToken))
                        {
                            CandidateId = null,
                            Provenance = PerformanceTriageProvenance.Aggregate,
                            DirectAllocationSites = summary.DirectSites,
                            OnceAllocationPaths = summary.OncePaths,
                            ConditionalAllocationPaths = summary.ConditionalPaths,
                            RepeatedAllocationPaths = summary.RepeatedPaths,
                            UnknownAllocationPaths = summary.UnknownPaths,
                            CachedAllocationSites = summary.CachedSites,
                            OpaqueCallPaths = summary.OpaquePaths,
                            AllocationCountSaturated = summary.Saturated,
                        }),
                ]), DirectCallerLoops);
            }
            return _allocationFanoutOpportunities;
        }
    }

    IReadOnlyDictionary<int, CallerLoopEvidence> DirectCallerLoops
        => _directCallerLoops ??= CallerLoopEvidenceAnalysis.FindNearest(
            Methods,
            _physicalDirectCalls,
            maxDepth: 1);

    Dictionary<int, int> RootReachByToken
    {
        get
        {
            if (_rootReachByToken is null)
            {
                var reachByToken = new Dictionary<int, int>();
                foreach (var entry in TopLeverage(int.MaxValue))
                    reachByToken[entry.Method.MetadataToken] = entry.RootReach;
                _rootReachByToken = reachByToken;
            }
            return _rootReachByToken;
        }
    }

    static ImmutableArray<DirectCall> ClassifyExactCallTargets(
        string path,
        ImmutableArray<DirectCall> calls,
        ImmutableArray<MethodIdentity> methods)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen);
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        var methodMap = MethodDefinitionMap.Create(methods);
        return
        [
            .. calls.Select(call =>
            {
                int targetToken = methodMap.Resolve(call);
                bool exact = targetToken != 0 && call.Kind switch
                {
                    CallKind.Call or CallKind.NewObject => true,
                    CallKind.CallVirtual => IsExactVirtualTarget(reader, targetToken),
                    _ => false,
                };
                return call with { ExactTarget = exact };
            }),
        ];
    }

    static bool IsExactVirtualTarget(MetadataReader reader, int methodToken)
    {
        var handle = MetadataTokens.EntityHandle(methodToken);
        if (handle.Kind != HandleKind.MethodDefinition)
            return false;
        var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
        if ((method.Attributes & MethodAttributes.Virtual) == 0
            || (method.Attributes & MethodAttributes.Final) != 0)
        {
            return true;
        }

        var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
        return (declaringType.Attributes & TypeAttributes.Sealed) != 0;
    }

    ImmutableArray<OptimizationOpportunity> AttachFindingProvenance(
        ImmutableArray<OptimizationOpportunity> opportunities)
    {
        var allocationFindings = new Dictionary<int, ImmutableArray<Finding<AllocationOccurrence>>>();
        var callSiteFindings = new Dictionary<int, ImmutableArray<Finding<DirectCall>>>();
        var physicalCallsByCaller = _physicalDirectCalls
            .GroupBy(call => call.Caller.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray());
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<OptimizationOpportunity>(opportunities.Length);

        foreach (var opportunity in opportunities)
        {
            Finding<AllocationOccurrence>? allocation = null;
            Finding<DirectCall>? callSite = null;
            Finding<DirectCall>? supportingCallSite = null;
            bool attachFinding =
                opportunity.Shape != "generic-parameter-object-box";
            if (attachFinding && opportunity.ILOffset is { } offset)
            {
                int methodToken = opportunity.Method.MetadataToken;
                int evidenceMethodToken =
                    opportunity.EvidenceMethodToken ?? methodToken;
                if (opportunity.Shape != "sync-call-in-async"
                    && _allocationOccurrences.TryGetValue(
                        evidenceMethodToken,
                        out var occurrences))
                {
                    if (!allocationFindings.TryGetValue(
                            evidenceMethodToken,
                            out var findings))
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

                // newobj and GetEnumerator calls can appear in both censuses. Their triage
                // shapes describe the allocation, so the allocation Finding owns provenance.
                if (allocation is null
                    && physicalCallsByCaller.TryGetValue(
                        evidenceMethodToken,
                        out var calls))
                {
                    if (!callSiteFindings.TryGetValue(
                            evidenceMethodToken,
                            out var findings))
                    {
                        findings = AnalysisFindings.InspectCallSites(
                            calls,
                            FindingSubjectFor(calls[0].Caller));
                        callSiteFindings[evidenceMethodToken] = findings;
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
                    out var supportingCalls))
            {
                if (!callSiteFindings.TryGetValue(
                        supportSite.EvidenceMethodToken,
                        out var findings))
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

            string? sourceFinding = allocation?.Descriptor.Id ?? callSite?.Descriptor.Id ?? opportunity.SourceFinding;
            FindingKey? findingKey = allocation?.Key ?? callSite?.Key;
            int? ordinal = allocation?.Ordinal ?? callSite?.Ordinal;
            int fingerprintLength = PerformanceTriageCandidateId.InitialFingerprintLength;
            string candidateId;
            while (true)
            {
                candidateId = PerformanceTriageCandidateId.Create(
                    opportunity,
                    sourceFinding,
                    findingKey,
                    ordinal,
                    fingerprintLength);
                if (candidateIds.Add(candidateId))
                    break;
                if (fingerprintLength == PerformanceTriageCandidateId.MaximumFingerprintLength)
                {
                    throw new InvalidOperationException(
                        $"Duplicate Performance Triage candidate identity '{candidateId}'.");
                }
                fingerprintLength = Math.Min(
                    fingerprintLength + 8,
                    PerformanceTriageCandidateId.MaximumFingerprintLength);
            }

            builder.Add(opportunity with
            {
                CandidateId = candidateId,
                SourceFinding = sourceFinding,
                Operation = allocation is null
                    ? CallOperation(callSite?.Payload)
                    : AllocationOperation(allocation.Payload),
                OperandToken = allocation?.Payload.OperandToken ?? callSite?.Payload.OperandToken,
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
                Provenance = opportunity.Provenance != PerformanceTriageProvenance.Unknown
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

    static FindingSubject FindingSubjectFor(MethodIdentity method)
        => new(
            $"method:0x{method.MetadataToken:X8}",
            $"{method.DeclaringType.ToQualifiedDisplayString()}::{method.Name}");

    static Finding<T>? SingleFindingAtOffset<T>(
        ImmutableArray<Finding<T>> findings,
        int offset,
        Func<T, int> getOffset)
        where T : notnull
    {
        Finding<T>? result = null;
        foreach (var finding in findings)
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

    static string AllocationOperation(AllocationOccurrence occurrence)
        => occurrence.Source switch
        {
            AllocationFactSource.Newobj => "newobj",
            AllocationFactSource.Newarr => "newarr",
            AllocationFactSource.Box => "box",
            AllocationFactSource.GetEnumeratorCall => "call.get-enumerator",
            _ => occurrence.Source.ToString().ToLowerInvariant(),
        };

    static string? CallOperation(DirectCall? call)
        => call is null
            ? null
            : string.IsNullOrWhiteSpace(call.Opcode)
                ? call.Kind.ToString().ToLowerInvariant()
                : call.Opcode;

    bool IsExceptionConstruction(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (_exceptionTypeNames.Contains(definition.ToQualifiedDisplayString()))
            return true;
        if (Methods.Length > 0
            && definition.Assembly == Methods[0].AssemblyName)
            return false;
        return definition.Name.EndsWith("Exception", StringComparison.Ordinal);
    }

    // A method that allocates densely is a real perf signal only when the allocations are
    // both REPEATED (in a loop) and not already pinpointed by a specific shape — otherwise
    // the count is dominated by intrinsic, non-reducible construction (e.g. a serializer
    // building its output model), which floods the section on allocation-heavy assemblies.
    // So allocation-hotspot fires only for a method that (1) is not already covered by a
    // specific-shape row, (2) allocates inside a loop, and (3) clears a high density bar.
    // Exception-construction allocations are excluded (throw-path only), and source-/
    // compiler-generated methods are suppressed just as for shaped opportunities.
    const int AllocationHotspotThreshold = 16;

    // A non-loop delegate is allocated once per call, so it is low-value in a cold method —
    // but on a high-reach (widely-reached, hot) method it is a real per-call heap allocation
    // worth surfacing. Lift such rows from "low" to "medium" so genuinely hot escaping
    // delegates are not buried among the cold one-shots. Threshold chosen against real
    // assemblies (on Aspire.Dashboard this promotes ~19 of 293 non-loop delegate rows).
    public const int DelegateHotRootReach =
        OptimizationOpportunityAnalysis.DelegateHotRootReach;

    // Adjust a delegate row's confidence once its method's RootReach is known: a cold-looking
    // (low) non-loop delegate on a high-reach method becomes medium. Loop delegates (already
    // high) and non-delegate shapes are unchanged. Pure for testability.
    public static string AdjustDelegateConfidenceForReach(string shape, bool inLoop, string confidence, int rootReach)
        => OptimizationOpportunityAnalysis
            .AdjustDelegateConfidenceForReach(
                shape,
                inLoop,
                confidence,
                rootReach);

    static bool IsLowFrequencyOpportunity(OptimizationOpportunity opportunity)
        => opportunity.ColdPath || opportunity.Amortized;

    OptimizationOpportunity MarkAmortizedSetup(OptimizationOpportunity opportunity)
    {
        if (opportunity.Amortized)
            return opportunity;
        if (opportunity.Method.Name is not (".ctor" or ".cctor"))
            return opportunity;
        // Type initializers are exact amortized setup: one execution per type.
        // Instance constructors are less certain, so demote only when this assembly
        // does not itself instantiate the constructor from a loop. That preserves a
        // known-hot transient-constructor signal while still lowering setup-only rows
        // such as DI/SignalR constructors that are not loop-invoked in their assembly.
        if (opportunity.Method.Name == ".ctor" && ConstructorIsInvokedInLoop(opportunity.Method))
            return opportunity;

        return opportunity with
        {
            Amortized = true,
            SafeFixDirection = "This allocation is in constructor/type-initializer setup, not a steady-state per-call path. Optimize only if profiles show this setup is hot or repeated unexpectedly.",
            Caveat = "Amortized setup path: constructor/type-initializer allocations are usually once per instance/type, not per steady-state operation.",
        };
    }

    bool ConstructorIsInvokedInLoop(MethodIdentity constructor)
        => DirectCalls.Any(call =>
            call.Kind == CallKind.NewObject
            && call.InLoop
            && call.CalleeDefinitionToken == constructor.MetadataToken);

    IEnumerable<OptimizationOpportunity> AllocationHotspots(Dictionary<int, int> reachByToken, IReadOnlySet<int> methodsWithSpecificShape)
    {
        var methodByToken = new Dictionary<int, MethodIdentity>(Methods.Length);
        foreach (var method in Methods)
            methodByToken[method.MetadataToken] = method;

        // Per-method steady-state allocation occurrences and whether any such
        // allocation is on a loop back-edge.
        var steadyAllocations = new Dictionary<int, int>();
        var steadyAllocationLoop = new HashSet<int>();
        foreach (var (token, occurrences) in _allocationOccurrences)
        {
            foreach (var occurrence in occurrences)
            {
                if (!occurrence.CountsAsHeapAllocation)
                    continue;
                if (occurrence.Escape == AllocationEscape.ThrowPath)
                    continue;
                if (occurrence.Kind == AllocationKind.Object
                    && occurrence.AllocatedType is { } type
                    && IsExceptionConstruction(type))
                    continue;
                steadyAllocations[token] = steadyAllocations.GetValueOrDefault(token) + 1;
                // Only allocations that genuinely iterate (semantic multiplicity) make a
                // method a loop hotspot — a return/throw early-exit inside a loop runs once.
                if (occurrence.Multiplicity == AllocationMultiplicity.Loop)
                    steadyAllocationLoop.Add(token);
            }
        }

        foreach (var (token, method) in methodByToken)
        {
            if (_suppressedOpportunityTokens.Contains(token))
                continue;
            // Dedup: a method already pinpointed by a specific shape (delegate/box/array/…)
            // doesn't also need a vague aggregate-density row; that only double-counts and
            // drowns the actionable specific rows.
            if (methodsWithSpecificShape.Contains(token))
                continue;
            // Only loop allocations are repeated; a once-per-call dense method is usually
            // intrinsic construction (e.g. building an output object), not reducible waste.
            bool inLoop = steadyAllocationLoop.Contains(token);
            if (!inLoop)
                continue;
            int allocations = steadyAllocations.GetValueOrDefault(token);
            if (allocations < AllocationHotspotThreshold)
                continue;
            yield return new OptimizationOpportunity(
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

    // A membership/search LINQ terminal on System.Linq.Enumerable: one that walks the
    // sequence to answer a lookup/membership question and whose canonical fix is an
    // indexed lookup (HashSet/Dictionary). Lazy operators (Where/Select/OrderBy) are
    // excluded — they do not enumerate at the call site — as are materializers
    // (ToArray/ToList), which have a different fix shape.
    //
    // Only the predicate/value overloads do real O(n) work. The parameterless positional
    // and aggregate overloads (First(), Single(), Count(), Any()) are O(1) — a positional
    // read, or the ICollection.Count fast path — so they are NOT scans and must not be
    // flagged. Every scanning overload takes the source plus a predicate/value, so it has
    // at least two parameters in Enumerable's static signature; gate on that arity.
    public static bool IsLinqMembershipScan(
        MemberRef member,
        out string operation)
        => RepeatedScanAnalysis.IsLinqMembershipScan(
            member,
            out operation);

    static bool IsLinqMaterializer(
        MemberRef member,
        out string operation)
        => RepeatedScanAnalysis.IsLinqMaterializer(
            member,
            out operation);

    // System.String.Concat — the lowering of the `+` / `+=` string operators (and of simple
    // interpolations like `$"{a}-{b}"`). Each call allocates a fresh string. Inside a loop,
    // when the result is stored back into one of its own inputs, it is the StringBuilder
    // anti-pattern: `s += …` repeatedly copies the growing accumulator (O(n^2)).
    public static bool IsStringConcat(MemberRef member)
        => RepeatedScanAnalysis.IsStringConcat(member);

    // A GetEnumerator call that returns a reference-type enumerator — i.e. iterating the
    // sequence allocates an enumerator object on the heap. `foreach` over a concrete type with a
    // struct enumerator (List<T>.Enumerator, …) returns it by value and allocates nothing; only
    // a foreach over an interface (IEnumerable/IEnumerable<T>) binds to GetEnumerator returning
    // the framework IEnumerator/IEnumerator<T> interface, whose implementation is a heap object.
    // The return type is matched by trusted-framework identity (#1708), not namespace+name, so a
    // user type that merely reuses the IEnumerator namespace and name is not mistaken for it.
    public static bool IsInterfaceEnumeratorAllocation(MemberRef member)
        => RepeatedScanAnalysis.IsInterfaceEnumeratorAllocation(member);

    // A lazy/deferred Enumerable operator (Where/Select/…): it returns an iterator without
    // enumerating at the call site. A helper that returns such a query is itself a deferred
    // linear scan — the scan runs when the caller enumerates the result.
    static bool IsLinqLazyProducer(
        MemberRef member,
        out string operation)
        => RepeatedScanAnalysis.IsLinqLazyProducer(
            member,
            out operation);

    /// <summary>
    /// Whether the module opted into the updated memory-safety rules via
    /// <c>MemorySafetyRulesAttribute</c> (Roslyn's <c>UseUpdatedMemorySafetyRules</c>).
    /// When false, every requires-unsafe member is <see cref="CallerUnsafeMode.Implicit"/>.
    /// </summary>
    public bool MemorySafetyRulesEnabled { get; }

    /// <summary>Per-<see cref="CallerUnsafeMode"/> method counts across the whole assembly.</summary>
    public UnsafeModeBreakdown UnsafeModes { get; }

    Dictionary<int, MethodSignals>? _signals;
    readonly IReadOnlyDictionary<int, MethodIdentity> _declaredSources;
    readonly IReadOnlyDictionary<int, BodySignals> _bodySignals;
    readonly IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> _allocationOccurrences;
    readonly IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> _unsafetyOccurrences;
    readonly IReadOnlyDictionary<(string Namespace, string Name), bool> _inAssemblyTypeIsException;
    readonly IReadOnlySet<int> _suppressedOpportunityTokens;
    readonly IReadOnlySet<int>
        _scopeExcludedOpportunityTokens;
    readonly IReadOnlySet<string> _exceptionTypeNames;
    readonly IReadOnlySet<int> _nonHeapNewObjOperandTokens;

    /// <summary>
    /// Per-method analysis signals (allocations, copies, unsafe, reflection,
    /// throw/catch/finally, evidence offsets), keyed by metadata token. Computed once
    /// from the call index and the body-scan signals, reused by the call-graph builders.
    /// </summary>
    Dictionary<int, MethodSignals> Signals =>
        _signals ??= MethodSignalAnalysis.Collect(
            _physicalDirectCalls,
            UnsafeEvidence,
            _bodySignals,
            Features.HasFlag(LibraryBodyAnalysisFeatures.Allocations)
                ? _allocationOccurrences
                : null,
            _inAssemblyTypeIsException,
            _nonHeapNewObjOperandTokens);

    /// <summary>
    /// Returns per-method body/call signals keyed by metadata token.
    /// </summary>
    public IReadOnlyDictionary<int, MethodSignals> GetMethodSignals() => Signals;

    /// <summary>Offset-keyed allocation occurrences, grouped by containing method token.</summary>
    public IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> GetAllocationOccurrences() => _allocationOccurrences;

    public IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> GetUnsafetyOccurrences() => _unsafetyOccurrences;

    public IReadOnlyDictionary<int, ImmutableArray<DirectCall>> GetDirectCallsByCaller()
        => _directCallsByCaller ??= DirectCalls
            .GroupBy(call => call.Caller.MetadataToken)
            .ToDictionary(group => group.Key, group => group.ToImmutableArray());

    /// <summary>
    /// Direct call sites grouped by the physical method body that owns their
    /// IL coordinates. The calls retain their declared <see cref="DirectCall.Caller"/>.
    /// </summary>
    public IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        GetDirectCallsByEvidenceMethod()
        => _directCallsByEvidenceMethod ??= DirectCalls
            .GroupBy(call => call.EvidenceMethod.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray());

    /// <summary>
    /// Maps a compiler-generated body — an async state-machine <c>MoveNext</c>,
    /// or a lifted local-function/lambda method — to an authenticated declared
    /// source identity. An unscoped index may return the immediate lifted
    /// source when the ultimate owner is unresolved; scoped indexes fail closed.
    /// Returns null when <paramref name="caller"/> is not such a body.
    /// <c>ResolveDeclaredMethod_MapsClassicAsyncMoveNextToSource</c> and
    /// <c>OptimizationOpportunities_UnresolvedLiftedSourceFailsClosedAcrossScopes</c>
    /// gate this contract.
    /// </summary>
    /// <remarks>
    /// <see cref="DirectCalls"/>, <see cref="FindCalls"/>, and
    /// <see cref="GetDirectCallsByCaller"/> already expose the declared caller.
    /// Pass <see cref="DirectCall.EvidenceMethod"/> when a consumer also needs
    /// to resolve the physical body explicitly.
    /// </remarks>
    public MethodIdentity? ResolveDeclaredMethod(MethodIdentity caller)
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
    /// Token/signature resolution over this assembly's defined methods. Depends only on
    /// <see cref="Methods"/>, so it is built once instead of per call-tree request: a single
    /// progressive render asks for several trees over the same index.
    /// </summary>
    MethodDefinitionMap MethodMap => _methodMap ??= MethodDefinitionMap.Create(Methods);

    readonly record struct LocalCalleeKey(
        int DefinitionToken,
        GraphNodeIdentity? StructuralIdentity);

    MethodIdentity? DeclaredMethod(int metadataToken)
    {
        // The builder merges declarations in metadata order, so token lookup
        // needs no retained per-index cache.
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

    /// <summary>
    /// Distinct callers per callee definition token, over the whole assembly.
    ///
    /// Fan-in is the <em>true</em> inbound degree, not the degree of the drawn subgraph: a bounded
    /// tree visits at most <c>maxNodes</c> nodes, but the annotation means "how many members depend
    /// on this one", so it must count callers the tree never expanded. That is why this is a
    /// whole-graph quantity and cannot be narrowed to the visited set — it is cached per index
    /// instead, so the cost is paid once rather than on every request.
    /// </summary>
    IReadOnlyDictionary<int, int> DistinctCallersByCallee()
        => _distinctCallersByCallee ??= DirectCalls
            .GroupBy(call => MethodMap.Resolve(call))
            .Where(group => group.Key != 0)
            .ToDictionary(
                group => group.Key,
                group => group.Select(call => call.Caller.MetadataToken).Distinct().Count(),
                EqualityComparer<int>.Default);

    /// <summary>
    /// Inbound call edges per callee definition token, collapsed to one edge per distinct caller
    /// method and preserving an in-loop call site when the same caller has one.
    ///
    /// Keyed by <see cref="MethodDefinitionMap.Resolve"/>, which is root-independent. The caller
    /// tree's own resolution additionally maps any edge whose callee definition token equals the
    /// requested root onto that root, which matters only when the root is bodiless
    /// (abstract/interface/extern) and therefore absent from <see cref="Methods"/>; for a root with
    /// a body <c>Resolve</c> already returns that same token. <see cref="BuildCallerTree(int, int,
    /// int)"/> therefore uses this cache only for rooted-in-a-body requests and builds its own map
    /// for the bodiless case, rather than caching a map that silently depends on the root.
    /// </summary>
    IReadOnlyDictionary<int, ImmutableArray<DirectCall>> DistinctCallerEdgesByCallee()
        => _distinctCallerEdgesByCallee ??= DirectCalls
            .GroupBy(call => MethodMap.Resolve(call))
            .Where(group => group.Key != 0)
            .ToDictionary(
                group => group.Key,
                group => CallTreeOrdering.OrderCallers(
                        group,
                        call => call.Caller.AssemblyName,
                        call => CallTreeMember.ToQualifiedDisplayString(
                            call.Caller),
                        call => call.Caller.ParameterTypes.Length,
                        call => call.Caller.ModuleVersionId,
                        call => call.Caller.MetadataToken,
                        call => call.ILOffset)
                    .GroupBy(call => call.Caller.MetadataToken)
                    .Select(callerGroup => callerGroup.FirstOrDefault(call => call.InLoop) ?? callerGroup.First())
                    .ToImmutableArray(),
                EqualityComparer<int>.Default);

    public IReadOnlyDictionary<int, ImmutableArray<UnsafeEvidence>> GetUnsafeEvidenceByMember()
        => _unsafeEvidenceByMember ??= UnsafeEvidence
            .GroupBy(evidence => evidence.Member.MetadataToken)
            .ToDictionary(group => group.Key, group => group.ToImmutableArray());

    IReadOnlySet<TypeRef>? _generatedFrameworkTypes;

    /// <summary>
    /// Exact <see cref="TypeRef"/> identities of types recognized as protobuf/gRPC
    /// generated implementation detail, detected structurally (no attributes are
    /// emitted on this code). Keys are definition identities, not qualified display
    /// strings: namespace <c>N.A</c> plus root <c>B</c> is distinct from namespace
    /// <c>N</c> plus nested <c>A+B</c>. A type qualifies when
    /// any of its methods bootstraps protobuf generated infrastructure — calling
    /// <c>Google.Protobuf.Reflection.FileDescriptor.FromGeneratedCode</c>, constructing
    /// <c>Google.Protobuf.Reflection.GeneratedClrTypeInfo</c>, or constructing the
    /// per-message <c>Google.Protobuf.MessageParser&lt;T&gt;</c> — where the bootstrap type
    /// comes from the real <c>Google.Protobuf</c> assembly (a user assembly can declare
    /// <c>Google.Protobuf.*</c> lookalikes, so namespace/name alone is not sufficient,
    /// #1580) — or is a gRPC stub that both
    /// declares infrastructure members whose names are codegen-only (<c>__ServiceName</c>,
    /// <c>__Helper_*</c>, <c>__Marshaller_*</c>, <c>__Method_*</c>) <em>and</em> calls into
    /// <c>Grpc.Core</c> (the binding/marshalling APIs a generated stub uses). A generated
    /// member name alone is not sufficient — an ordinary user type can declare a
    /// <c>__Helper_*</c> method — so the structural <c>Grpc.Core</c> tie is required to avoid
    /// classifying user lookalikes as generated. gRPC binding calls
    /// (<c>ServerServiceDefinition</c>/<c>Marshallers</c>) are still not a signal on their own,
    /// since hand-written registration uses them without the generated members. These signals
    /// appear in generated protobuf/gRPC code, so perf triage can mark them in Top Leverage and
    /// suppress them from Performance Triage like other generated detail.
    /// </summary>
    public IReadOnlySet<TypeRef> GeneratedFrameworkTypes
        => _generatedFrameworkTypes ??=
            GeneratedFrameworkTypeAnalysis.Collect(
                _physicalDirectCalls,
                Methods);

    /// <summary>
    /// True when <paramref name="type"/> is in
    /// <see cref="GeneratedFrameworkTypes"/> or is a metadata nested type of one.
    /// </summary>
    public bool IsGeneratedFrameworkType(TypeRef type)
        => IsGeneratedFrameworkType(GeneratedFrameworkTypes, type);

    /// <summary>
    /// True when <paramref name="type"/> is a classified generated-framework type
    /// or a metadata nested type of one. Prefers decoder segment structure over
    /// flattened <c>+</c> names; does not parse qualified display text.
    /// </summary>
    public static bool IsGeneratedFrameworkType(
        IReadOnlySet<TypeRef> generatedFrameworkTypes,
        TypeRef type)
        => GeneratedFrameworkTypeAnalysis.Contains(generatedFrameworkTypes, type);

    public static LibraryBodyIndex Open(string path)
        => Open(path, resolver: null);

    internal static LibraryBodyIndex FromEvidence(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>>? allocationOccurrences = null,
        IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>>? unsafetyOccurrences = null,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default,
        ImmutableArray<DirectCall> directCalls = default,
        ImmutableArray<MethodResultSink> resultSinks = default,
        ImmutableArray<FieldStoreFact> fieldStores = default,
        ImmutableArray<FieldLoadFact> fieldLoads = default,
        ImmutableArray<MethodReturnFlow> returnFlows = default,
        LibraryBodyModuleIdentity? moduleIdentity = null)
    {
        moduleIdentity ??= SyntheticEvidenceIdentity(methods);
        ValidateSyntheticEvidenceIdentity(moduleIdentity, methods);
        return new(
            path: "",
            moduleIdentity,
            analysis: new(
                Methods: new(
                    DeclaredMethods: methods,
                    Methods: methods,
                    DirectCalls: directCalls.IsDefault ? [] : directCalls,
                    ResultSinks: resultSinks.IsDefault ? [] : resultSinks,
                    FieldStores: fieldStores.IsDefault ? [] : fieldStores,
                    FieldLoads: fieldLoads.IsDefault ? [] : fieldLoads,
                    ReturnFlows: returnFlows.IsDefault ? [] : returnFlows,
                    BodySignals: new Dictionary<int, BodySignals>(),
                    InAssemblyTypeIsException:
                        new Dictionary<(string Namespace, string Name), bool>(),
                    NonHeapNewObjOperandTokens: new HashSet<int>(),
                    DeclaredSources: new Dictionary<int, MethodIdentity>()),
                Safety: new(
                    Evidence: unsafeEvidence,
                    LeverageMethods: [],
                    UpdatedRulesEnabled: false,
                    Modes: new UnsafeModeBreakdown(
                        methods.Count(method =>
                            method.CallerUnsafeMode == CallerUnsafeMode.None),
                        methods.Count(method =>
                            method.CallerUnsafeMode
                                == CallerUnsafeMode.Implicit),
                        methods.Count(method =>
                            method.CallerUnsafeMode
                                == CallerUnsafeMode.Explicit)),
                    Occurrences: unsafetyOccurrences
                        ?? new Dictionary<
                            int,
                            ImmutableArray<UnsafetyOccurrence>>()),
                Allocations: new(
                    allocationOccurrences
                        ?? new Dictionary<
                            int,
                            ImmutableArray<AllocationOccurrence>>()),
                Optimizations: new(
                    Opportunities: [],
                    SuppressedMethodTokens: new HashSet<int>(),
                    ScopeExcludedMethodTokens:
                        new HashSet<int>(),
                    ExceptionTypeNames:
                        new HashSet<string>(StringComparer.Ordinal)),
                OwnershipFlow: new(Methods: []),
                Resources: new(LeakTriage: null),
                Diagnostics: diagnostics.IsDefault ? [] : diagnostics),
            features: LibraryBodyAnalysisFeatures.MethodEvidence
                | (allocationOccurrences is null
                    ? LibraryBodyAnalysisFeatures.None
                    : LibraryBodyAnalysisFeatures.Allocations));
    }

    public static LibraryBodyIndex Open(string path, IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true, bool includeOpportunities = true, IReadOnlySet<int>? bodyScope = null, Func<TypeRef, bool>? bodyTypeScope = null)
    {
        var features = LibraryBodyAnalysisFeatures.MethodEvidence;
        if (includeAllocations)
            features |= LibraryBodyAnalysisFeatures.Allocations;
        if (includeOpportunities)
            features |= LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        return Open(path, features, resolver, bodyScope, bodyTypeScope);
    }

    public static LibraryBodyIndex Open(
        string path,
        LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        LibraryBodyAnalysisPlan plan =
            LibraryBodyAnalysisPlan.Create(
                features,
                bodyScope,
                bodyTypeScope);

        if (resolver is not null
            && UsesReferenceResolution(plan))
        {
            LibraryBodyRootSnapshot? rootSnapshot =
                AcquireRootSnapshot(path);
            if (rootSnapshot is not null)
            {
                using var imageReader =
                    new PEReader(rootSnapshot.Snapshot.Content);
                return BuildFromReader(
                    path,
                    imageReader,
                    plan,
                    resolver,
                    rootSnapshot);
            }
        }

        // Full (unscoped) builds decode every method body in parallel; prefetch the entire image
        // so concurrent GetMethodBody reads are served from an immutable in-memory block rather
        // than seeking a shared FileStream (which is not safe for concurrent reads). Scoped builds
        // decode only a handful of bodies sequentially, so they keep the lazy default.
        var streamOptions = !plan.IsScoped
            ? PEStreamOptions.PrefetchEntireImage
            : PEStreamOptions.Default;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(
            stream,
            streamOptions | PEStreamOptions.LeaveOpen);
        return BuildFromReader(
            path,
            peReader,
            plan,
            resolver,
            rootSnapshot: null);
    }

    /// <summary>
    /// Builds an index over caller-provided immutable PE image content without
    /// reopening the target file.
    /// </summary>
    /// <remarks>
    /// <c>LibraryBodyIndex_ConsumesCallerOwnedPrefetchedImage</c> gates shared
    /// image consumption, and
    /// <c>LibraryBodyIndex_PrefetchedImageHonorsBodyScope</c> gates scoped
    /// decoding through this entry point.
    /// </remarks>
    public static LibraryBodyIndex OpenFromPrefetchedImage(
        string path,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (image.IsDefaultOrEmpty)
            throw new ArgumentException("A prefetched PE image is required.", nameof(image));
        LibraryBodyAnalysisPlan plan =
            LibraryBodyAnalysisPlan.Create(
                features,
                bodyScope,
                bodyTypeScope);

        using var peReader = new PEReader(image);
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        LibraryBodyRootSnapshot? rootSnapshot =
            resolver is not null
                && reader.IsAssembly
                && UsesReferenceResolution(plan)
                ? CreateRootSnapshot(path, reader, image)
                : null;
        return BuildFromReader(
            path,
            peReader,
            plan,
            resolver,
            rootSnapshot);
    }

    /// <summary>
    /// Determines whether an opened metadata context contains any unsafe
    /// declaration or body evidence, stopping after the first finding instead
    /// of materializing a whole-assembly body index or PE image.
    /// </summary>
    /// <remarks>
    /// Gates:
    /// <c>Discover_UnsafeMembers_UsesPresenceProbeWithoutExecutingFullQuery</c> and
    /// <c>UnsafeEvidencePresenceQuery_ConsumesBorrowedNonPrefetchedContext</c>.
    /// </remarks>
    public static bool HasUnsafeEvidence(
        string path,
        PdbContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);

        return context.InspectImage(
            peReader => HasUnsafeEvidence(
                path,
                peReader));
    }

    /// <summary>
    /// Determines whether an immutable PE image contains unsafe evidence.
    /// Prefer the context overload when an owning metadata context is already
    /// open.
    /// </summary>
    public static bool HasUnsafeEvidence(
        string path,
        ImmutableArray<byte> image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A PE image is required.",
                nameof(image));
        }

        using var peReader = new PEReader(image);
        return HasUnsafeEvidence(path, peReader);
    }

    static bool HasUnsafeEvidence(
        string path,
        PEReader peReader)
    {
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return false;

        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            MetadataFormatAdmission.GetMetadataReader(peReader),
            peReader);
        return builder.HasUnsafeEvidence();
    }

    static LibraryBodyIndex BuildFromReader(
        string path,
        PEReader peReader,
        LibraryBodyAnalysisPlan plan,
        IAssemblyReferenceResolver? resolver,
        LibraryBodyRootSnapshot? rootSnapshot)
    {
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            throw new BadImageFormatException($"No managed metadata: {path}");
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        LibraryBodyModuleIdentity moduleIdentity =
            LibraryBodyModuleIdentity.FromImage(reader);
        IAssemblyReferenceResolver? analysisResolver =
            plan.Includes(
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities)
            || plan.Includes(
                LibraryBodyAnalysisFeatures
                    .AsyncSiblingOpportunities)
            || plan.Includes(
                LibraryBodyAnalysisFeatures
                    .OwnershipFlow)
                ? resolver
                : null;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            analysisResolver,
            analysisResolver is null
                ? null
                : rootSnapshot);
        LibraryBodyAnalysisResult analysis =
            builder.Build(plan);
        return new LibraryBodyIndex(
            path,
            moduleIdentity,
            analysis,
            plan.Features);
    }

    static void ValidateSyntheticEvidenceIdentity(
        LibraryBodyModuleIdentity moduleIdentity,
        ImmutableArray<MethodIdentity> methods)
    {
        foreach (MethodIdentity method in methods)
        {
            if (moduleIdentity.AssemblyIdentity is not { } assembly
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    assembly.Name,
                    method.AssemblyName)
                || moduleIdentity.ModuleVersionId
                    != method.ModuleVersionId)
            {
                throw new ArgumentException(
                    "Synthetic method evidence does not match the supplied "
                    + "module identity.",
                    nameof(methods));
            }
        }
    }

    static LibraryBodyModuleIdentity SyntheticEvidenceIdentity(
        ImmutableArray<MethodIdentity> methods)
    {
        if (methods.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An empty synthetic index requires an explicit module identity.",
                nameof(methods));
        }

        MethodIdentity first = methods[0];
        return new LibraryBodyModuleIdentity(
            new AssemblyReferenceIdentity(
                first.AssemblyName,
                Version: null,
                Culture: null,
                PublicKeyToken: null),
            first.ModuleVersionId);
    }

    static bool UsesReferenceResolution(
        LibraryBodyAnalysisPlan plan) =>
        plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities)
        || plan.Includes(
            LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities)
        || plan.Includes(
            LibraryBodyAnalysisFeatures.OwnershipFlow);

    static LibraryBodyRootSnapshot? AcquireRootSnapshot(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        AssemblyReferenceIdentity identity;
        DateTime lastWriteTimeUtc;
        using (FileStream stream = File.OpenRead(fullPath))
        using (var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen))
        {
            if (!MetadataFormatAdmission.AdmitImage(peReader))
            {
                throw new BadImageFormatException(
                    $"No managed metadata: {path}");
            }

            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(peReader);
            if (!reader.IsAssembly)
                return null;
            identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader);
            lastWriteTimeUtc =
                File.GetLastWriteTimeUtc(stream.SafeFileHandle);
        }

        var assembly = ResolvedAssemblyReference.Create(
            identity,
            fullPath,
            () => File.OpenRead(fullPath),
            AssemblyResolutionProvenance.Local(
                "LibraryBodyIndex"),
            lastWriteTimeUtc);
        AssemblyImageSnapshotResult result =
            AssemblyImageSnapshot.Open(
                assembly,
                length => length
                    <= AssemblyImageSnapshot
                        .DefaultMaxRetainedImageBytes,
                _ => { });
        return result switch
        {
            AssemblyImageSnapshotResult.Ready ready =>
                new LibraryBodyRootSnapshot(
                    assembly,
                    ready.Snapshot),
            AssemblyImageSnapshotResult.Rejected rejected =>
                throw RootSnapshotFailure(path, rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown root-image acquisition result."),
        };
    }

    static LibraryBodyRootSnapshot CreateRootSnapshot(
        string path,
        MetadataReader reader,
        ImmutableArray<byte> image)
    {
        if (image.Length
            > AssemblyImageSnapshot.DefaultMaxRetainedImageBytes)
        {
            throw new InvalidOperationException(
                "The root assembly exceeds the retained-image budget.");
        }

        byte[] bytes = ImmutableCollectionsMarshal.AsArray(image)!;
        var assembly = ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
            System.IO.Path.GetFullPath(path),
            () => new MemoryStream(bytes, writable: false),
            AssemblyResolutionProvenance.Local(
                "LibraryBodyIndex"));
        AssemblyImageSnapshotResult result =
            AssemblyImageSnapshot.FromRetainedContent(
                assembly,
                image);
        return result switch
        {
            AssemblyImageSnapshotResult.Ready ready =>
                new LibraryBodyRootSnapshot(
                    assembly,
                    ready.Snapshot),
            AssemblyImageSnapshotResult.Rejected rejected =>
                throw RootSnapshotFailure(path, rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown root-image acquisition result."),
        };
    }

    static Exception RootSnapshotFailure(
        string path,
        CandidateOpenFailure failure)
    {
        if (failure.MetadataRootReason is { } reason)
            return new MalformedMetadataRootException(reason);

        return failure.Kind switch
        {
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                new UnsupportedMetadataFormatException(),
            CandidateOpenFailureKind.InvalidImage =>
                new BadImageFormatException(
                    $"{failure.Detail} Path: {path}"),
            CandidateOpenFailureKind.Unreadable =>
                new IOException(
                    $"{failure.Detail} Path: {path}"),
            CandidateOpenFailureKind.ResourceBudget =>
                new InvalidOperationException(
                    $"{failure.Detail} Path: {path}"),
            _ => new InvalidOperationException(
                $"Unknown root-image failure for {path}."),
        };
    }

    public ImmutableArray<DirectCall> FindCalls(MemberPattern pattern)
        => [.. DirectCalls.Where(call => pattern.Matches(call.Callee))];

    /// <summary>
    /// The most-leveraged requires-unsafe methods, ranked by distinct direct
    /// callers — the highest-value targets for `unsafe` marking, since marking
    /// them propagates the requirement to the most callers.
    /// </summary>
    public ImmutableArray<UnsafeMethodLeverage> TopUnsafeLeverage(int count = 6)
        => UnsafeLeverage.Top(
            _physicalDirectCalls,
            _unsafeLeverageMethods,
            count);

    /// <summary>
    /// The most-leveraged methods in this assembly, ranked by distinct direct
    /// callers. <paramref name="scope"/> optionally restricts which methods are
    /// ranked (for example, members declared on one selected type) while fanin
    /// is still measured across every caller in the assembly.
    /// </summary>
    public ImmutableArray<MethodLeverage> TopLeverage(int count = 25, Func<MethodIdentity, bool>? scope = null)
        => MethodLeverageRanking.Top(
            DirectCalls,
            Methods,
            count,
            scope);

    /// <summary>
    /// Distinct callee types touched by calls from methods in <paramref name="callerScope"/>.
    /// Callee declaring types are reduced to their open definitions so generic instantiations
    /// stay bounded and same-type generic self-calls are excluded.
    /// </summary>
    public ImmutableArray<CalledTypeSummary> CalledTypes(Func<MethodIdentity, bool> callerScope)
    {
        ArgumentNullException.ThrowIfNull(callerScope);

        return
        [
            .. DirectCalls
                .Where(call => callerScope(call.Caller))
                .Where(call => call.Callee.Kind != MemberKind.Unsupported)
                .Where(call => !IsObjectConstructor(call.Callee))
                .Select(call => new
                {
                    Call = call,
                    CalledType = GenericMemberIdentity.OpenDeclaringType(call.Callee.DeclaringType),
                    CallerType = GenericMemberIdentity.OpenDeclaringType(call.Caller.DeclaringType),
                    CalleeKey = GraphNodeIdentity.FromMember(call.Callee),
                })
                .Where(item => !item.CalledType.Equals(item.CallerType))
                .GroupBy(item => item.CalledType)
                .Select(group =>
                {
                    var type = group.Key;
                    return new CalledTypeSummary(
                        type,
                        FormatCalledTypeAssembly(type.Assembly),
                        Calls: group.Count(),
                        Members: group.Select(item => item.CalleeKey).Distinct().Count(),
                        CallKinds: [.. group
                            .Select(item => item.Call.Kind)
                            .Distinct()
                            .OrderBy(kind => kind)]);
                })
                .OrderByDescending(summary => summary.Calls)
                .ThenBy(summary => summary.Type.ToQualifiedDisplayString(), StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Requires-unsafe methods whose signature carries no pointer — the unsafe
    /// obligation is visible only via the attribute / <c>unsafe</c> modifier,
    /// hidden from a caller reading the parameter and return types.
    /// </summary>
    public ImmutableArray<OpaqueUnsafeMethod> OpaqueUnsafeMethods()
        => OpaqueUnsafe.Collect(Methods);

    /// <summary>
    /// Requires-unsafe methods whose body shows no directly-visible unsafe
    /// operation — an absence claim (never "safe"): a pointer local optimized
    /// away in Release erases the trace of a real dereference.
    /// </summary>
    public ImmutableArray<HollowUnsafeMethod> HollowUnsafeMethods()
        => HollowUnsafe.Collect(Methods, UnsafeEvidence);

    /// <summary>
    /// Builds a bounded outbound (callee) call tree rooted at the method identified by
    /// <paramref name="rootMethodToken"/>. Expansion stays within this assembly: callees that
    /// resolve to another assembly are recorded as <see cref="CallTreeStatus.External"/> leaves.
    /// A method is expanded at most once across the whole tree; later references (shared callees
    /// or cycles) are recorded as <see cref="CallTreeStatus.AlreadyShown"/> leaves. Expansion stops
    /// at <paramref name="maxDepth"/> levels and once <paramref name="maxNodes"/> total nodes exist.
    /// </summary>
    public CallTreeNode BuildCallTree(int rootMethodToken, int maxDepth = 3, int maxNodes = 25)
    {
        var root = DeclaredMethods.FirstOrDefault(
            method => method.MetadataToken == rootMethodToken);
        var rootMember = root is { } identity
            ? CallTreeMember.FromDefinition(identity)
            : MemberRef.Unsupported($"method token 0x{rootMethodToken:X8}");

        var callsByCaller = GetDirectCallsByCaller();

        var methodMap = MethodMap;
        var diagnosticsByToken = Diagnostics
            .GroupBy(diagnostic => diagnostic.MethodToken)
            .ToDictionary(group => group.Key, group => group.First());

        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();

        int ResolveCallee(DirectCall call)
            => DeclaredMethod(call.CalleeDefinitionToken)
                    ?.MetadataToken
                ?? methodMap.Resolve(call);

        // Fan-in counts distinct callers, not call sites: it is a leverage cue ("how many
        // members depend on this one"), and the reverse graph draws one edge per distinct
        // caller, so the annotation has to agree with the picture it annotates. Cached on the
        // index because it is a whole-graph quantity that every request would otherwise rebuild.
        var incomingCounts = DistinctCallersByCallee();

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
            var sig = token != 0 ? Signals.GetValueOrDefault(token, MethodSignals.None) : MethodSignals.None;
            diagnosticsByToken.TryGetValue(token, out AnalysisDiagnostic? diagnostic);
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
                    HasUnresolvedDispatch =
                        hasUnresolvedDispatch,
                    ParentEdgeCallSites = parentEdgeCallSites.IsDefault
                        ? []
                        : parentEdgeCallSites,
                };

            if (token == 0 || !callsByCaller.TryGetValue(token, out var edges))
            {
                var leafStatus = token == 0 && depth > 0
                    ? CallTreeStatus.External
                    : diagnostic is not null
                        ? CallTreeStatus.AnalysisIncomplete
                        : definition is not null
                            && !methodMap.ContainsToken(token)
                            ? CallTreeStatus.Bodiless
                            : CallTreeStatus.Leaf;
                return Node(
                    leafStatus,
                    [],
                    new CallTreePerf(0, incomingCounts.TryGetValue(token, out var incoming) ? incoming : 0, 1, inLoop, inLoop ? "loop" : null, null, sig));
            }

            // True outbound degree (call sites), independent of how far the bounded
            // tree expanded. CallTreeStatus separately conveys why expansion stopped,
            // so depth-limited/already-shown/truncated nodes still report their real
            // fan-out instead of reading like leaves.
            var fanout = edges.Length;
            if (depth >= maxDepth)
            {
                return Node(
                    CallTreeStatus.DepthLimited,
                    [],
                    new CallTreePerf(fanout, incomingCounts.TryGetValue(token, out var incomingDepth) ? incomingDepth : 0, 1, inLoop, inLoop ? "loop" : null, null, sig));
            }

            if (!expanded.Add(token))
            {
                return Node(
                    CallTreeStatus.AlreadyShown,
                    [],
                    new CallTreePerf(fanout, incomingCounts.TryGetValue(token, out var incomingShown) ? incomingShown : 0, 1, inLoop, inLoop ? "loop" : null, null, sig));
            }

            var collapsedEdges = edges
                .Select(edge =>
                    (
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
                .Select(group =>
                    (
                        Item: group.FirstOrDefault(
                            item => item.Edge.InLoop,
                            group.First()),
                        Calls: group
                            .Select(item => item.Edge)
                            .ToImmutableArray(),
                        HasVirtualDispatch:
                            group.Any(item =>
                                item.Edge.Kind
                                    is CallKind.CallVirtual
                                        or CallKind.LoadVirtualFunction)))
                .ToImmutableArray();
            var children = ImmutableArray.CreateBuilder<CallTreeNode>();
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

            var status = truncated
                ? CallTreeStatus.Truncated
                : diagnostic is not null
                    ? CallTreeStatus.AnalysisIncomplete
                    : children.Count == 0 ? CallTreeStatus.Leaf : CallTreeStatus.Expanded;
            var maxTreeDepth = children.Count == 0 ? 1 : 1 + children.Max(child => child.Perf?.MaxDepth ?? 1);
            var fanin = incomingCounts.TryGetValue(token, out var count) ? count : 0;
            return Node(
                status,
                children.ToImmutable(),
                new CallTreePerf(fanout, fanin, maxTreeDepth, inLoop, inLoop ? "loop" : null, null, sig));
        }

        return Build(rootMember, null, rootMethodToken, 0);
    }

    /// <summary>
    /// Builds a bounded reverse (caller) tree rooted at the method identified by
    /// <paramref name="rootMethodToken"/>. Nodes are the immediate callers of the
    /// selected method and their callers transitively, capped by depth and node budget.
    /// </summary>
    public CallTreeNode BuildCallerTree(int rootMethodToken, int maxDepth = 3, int maxNodes = 25)
    {
        var root = DeclaredMethods.FirstOrDefault(
            method => method.MetadataToken == rootMethodToken);
        // DeclaredMethods supplies the label even when the selected method has no body of its
        // own. For a token with no local declaration, recover the label from an inbound edge so
        // the graph can still name the member instead of printing a bare token.
        var rootMember = root is { } identity
            ? CallTreeMember.FromDefinition(identity)
            : DirectCalls.FirstOrDefault(call => call.CalleeDefinitionToken == rootMethodToken
                && call.Callee.Kind != MemberKind.Unsupported) is { Callee: { } resolvedCallee }
                ? resolvedCallee
                : MemberRef.Unsupported($"method token 0x{rootMethodToken:X8}");

        var methodMap = MethodMap;

        int ResolveCalleeToken(DirectCall call)
        {
            // Direct callvirt/call edges to the selected method reference it by its own
            // MethodDef token (peeled from a MethodSpec for generic-method calls). Accept that
            // even when the selected method has no body of its own (abstract/interface/extern)
            // and so is absent from Methods, so a Caller Graph rooted at a bodiless member still
            // surfaces its real inbound callers.
            if (call.CalleeDefinitionToken == rootMethodToken)
                return rootMethodToken;
            if (methodMap.ContainsToken(call.CalleeDefinitionToken))
                return call.CalleeDefinitionToken;
            return methodMap.Resolve(call);
        }

        // Group inbound call edges by callee, then collapse to one edge per distinct caller
        // method (the section reports callers, not call sites). Preserve the in-loop signal:
        // if any call site from a caller hits the target inside a loop, keep that edge so the
        // loop annotation survives deduplication.
        //
        // When the root has a body, ResolveCalleeToken agrees with MethodDefinitionMap.Resolve
        // for every edge — Resolve returns CalleeDefinitionToken whenever that token is a defined
        // method — so the shared per-index cache applies. A bodiless root is the one case where
        // the grouping genuinely depends on the root, because edges naming it would otherwise be
        // resolved onto some other defined method; that case builds its own map.
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>> reverseEdges =
            methodMap.ContainsToken(rootMethodToken)
                ? DistinctCallerEdgesByCallee()
                : DirectCalls
                    .GroupBy(call => ResolveCalleeToken(call))
                    .Where(group => group.Key != 0)
                    .ToDictionary(
                        group => group.Key,
                        group => CallTreeOrdering.OrderCallers(
                                group,
                                call => call.Caller.AssemblyName,
                                call => CallTreeMember.ToQualifiedDisplayString(
                                    call.Caller),
                                call => call.Caller.ParameterTypes.Length,
                                call => call.Caller.ModuleVersionId,
                                call => call.Caller.MetadataToken,
                                call => call.ILOffset)
                            .GroupBy(call => call.Caller.MetadataToken)
                            .Select(callerGroup => callerGroup.FirstOrDefault(call => call.InLoop) ?? callerGroup.First())
                            .ToImmutableArray(),
                        EqualityComparer<int>.Default);

        int budget = Math.Max(1, maxNodes);
        int created = 1;
        var expanded = new HashSet<int>();

        CallTreeNode Build(
            MemberRef member,
            int token,
            int depth,
            bool inLoop,
            ImmutableArray<DirectCall> parentEdgeCallSites = default)
        {
            // Reverse-graph semantics: the selected member is the target/sink, and the
            // entry points are the far callers — not the tree root. Label accordingly so
            // the target is not mistaken for the source of leverage.
            var classification = depth == 0
                ? "target"
                : member.Name is "Main" or "<Main>$" ? "entrypoint" : null;
            // A caller node's loop flag is an edge property: this caller invokes the node
            // toward the target inside a loop (not "this method is loop-heavy").
            var loopHint = inLoop ? "loop call" : null;
            var sig = token != 0 ? Signals.GetValueOrDefault(token, MethodSignals.None) : MethodSignals.None;

            CallTreeNode Node(
                CallTreeStatus status,
                ImmutableArray<CallTreeNode> children,
                CallTreePerf perf) =>
                new(member, null, status, children, perf)
                {
                    ParentEdgeCallSites = parentEdgeCallSites.IsDefault
                        ? []
                        : parentEdgeCallSites,
                };

            if (token == 0 || !reverseEdges.TryGetValue(token, out var edges))
            {
                var leafStatus = token == 0 && depth > 0 ? CallTreeStatus.External : CallTreeStatus.Leaf;
                return Node(
                    leafStatus,
                    [],
                    new CallTreePerf(0, 0, 1, inLoop, loopHint, classification, sig));
            }

            var fanin = edges.Length;
            if (depth >= maxDepth)
                return Node(
                    CallTreeStatus.DepthLimited,
                    [],
                    new CallTreePerf(0, fanin, 1, inLoop, loopHint, classification, sig));

            if (!expanded.Add(token))
                return Node(
                    CallTreeStatus.AlreadyShown,
                    [],
                    new CallTreePerf(0, fanin, 1, inLoop, loopHint, classification, sig));

            var children = ImmutableArray.CreateBuilder<CallTreeNode>();
            bool truncated = false;
            foreach (var edge in edges)
            {
                if (created >= budget)
                {
                    truncated = true;
                    break;
                }
                created++;
                var caller = edge.Caller;
                ImmutableArray<DirectCall> callSites =
                [
                    .. GetDirectCallsByCaller()[
                            caller.MetadataToken]
                        .Where(call =>
                            ResolveCalleeToken(call) == token)
                        .OrderBy(call => call.ILOffset)
                        .ThenBy(call => call.OperandToken),
                ];
                children.Add(Build(
                    CallTreeMember.FromDefinition(caller),
                    caller.MetadataToken,
                    depth + 1,
                    edge.InLoop,
                    callSites));
            }

            var nodeStatus = truncated
                ? CallTreeStatus.Truncated
                : children.Count == 0 ? CallTreeStatus.Leaf : CallTreeStatus.Expanded;
            var maxTreeDepth = children.Count == 0 ? 1 : 1 + children.Max(child => child.Perf?.MaxDepth ?? 1);
            return Node(
                nodeStatus,
                children.ToImmutable(),
                new CallTreePerf(0, fanin, maxTreeDepth, inLoop, loopHint, classification, sig));
        }

        return Build(rootMember, rootMethodToken, 0, false);
    }

    /// <summary>
    /// Builds a bounded reverse tree through one catalog-owned assembly-group
    /// graph. The scope owns graph storage and correspondence work so caller
    /// and callee views reuse one acquisition.
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
    /// graph. The scope owns graph storage and correspondence work so caller
    /// and callee views reuse one acquisition.
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

    static string FormatCalledTypeAssembly(string assembly)
        => string.IsNullOrEmpty(assembly) || assembly == TypeRef.CoreLibrary ? "" : assembly;

    static bool IsObjectConstructor(MemberRef member)
        => member is
        {
            Name: ".ctor",
            DeclaringType.Kind: TypeRefKind.Definition,
            DeclaringType.Assembly: TypeRef.CoreLibrary,
            DeclaringType.Namespace: "System",
            DeclaringType.Name: "Object"
        };
}
