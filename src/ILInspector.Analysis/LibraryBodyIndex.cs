using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using ILInspector.ControlFlow;
using Inspector.Findings;
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
    /// <summary>
    /// Produce typed physical local-throw sites and explicit body coverage;
    /// implies <see cref="MethodEvidence"/>.
    /// </summary>
    LocalThrows = 1 << 7,
    /// <summary>
    /// Produce objective per-physical-body implementation profiles; implies
    /// <see cref="MethodEvidence"/>.
    /// </summary>
    ImplementationProfiles = 1 << 8,
    /// <summary>The body-analysis features used by the general index.</summary>
    Default = MethodEvidence
        | Allocations
        | OptimizationOpportunities
        | AsyncSiblingOpportunities,
    /// <summary>All available body-analysis producers.</summary>
    All = Default
        | LeakTriage
        | OwnershipFlow
        | JsonWireContractFlow
        | LocalThrows
        | ImplementationProfiles,
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
    internal LibraryBodyIndex(
        string path,
        LibraryBodyModuleIdentity moduleIdentity,
        string? moduleName,
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisFeatures features,
        bool hasFullMethodEvidenceScope,
        LibraryOptimizationAnalysisResult? optimization = null,
        LibraryCallGraphAnalysisResult? callGraph = null,
        LibraryLeverageAnalysisResult? leverage = null)
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
        _localThrows = analysis.Methods.LocalThrows;
        UnsafeEvidence = analysis.Safety.Evidence;
        Diagnostics = analysis.Diagnostics;
        bool hasFullScope =
            (features
                & LibraryBodyAnalysisFeatures.MethodEvidence) != 0
            && hasFullMethodEvidenceScope;
        var receipt = new LibraryBodyAnalysisReceipt(
            path,
            moduleIdentity,
            features,
            hasFullScope,
            analysis.Diagnostics);
        _callGraph = callGraph
            ?? new(
                receipt,
                moduleName,
                analysis);
        GeneratedFrameworkTypeSet? generatedFrameworkTypes = null;
        _leverage = leverage
            ?? new(
                receipt,
                _callGraph,
                generatedFrameworkTypes ??=
                    new GeneratedFrameworkTypeSet(_callGraph));
        _optimization = optimization
            ?? new(
                receipt,
                analysis,
                _callGraph,
                generatedFrameworkTypes ??=
                    new GeneratedFrameworkTypeSet(_callGraph));
        _unsafeLeverageMethods = analysis.Safety.LeverageMethods;
        MemorySafetyRules = analysis.Safety.Rules;
        UnsafeModes = analysis.Safety.Modes;
        _implementationProfiles =
            analysis.Methods.ImplementationProfiles;
        _allocationOccurrences = analysis.Allocations.Occurrences;
        _unsafetyOccurrences = analysis.Safety.Occurrences;
        Features = features;
        HasFullMethodEvidenceScope = hasFullScope;
        _leakTriage = analysis.Resources.LeakTriage;
        ArrayPoolOwnership = analysis.OwnershipFlow.Methods;
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
    /// when call value flow is requested.
    /// </summary>
    public ImmutableArray<MethodResultSink> ResultSinks { get; }

    /// <summary>
    /// Every physical <c>stsfld</c>/<c>stfld</c> site with the resolved
    /// provenance of the value it stores, when call value flow is requested.
    /// Unproven stores are present with an unresolved value so a
    /// consumer asking "is this the only write to this field?" fails closed.
    /// </summary>
    public ImmutableArray<FieldStoreFact> FieldStores { get; }

    /// <summary>
    /// Every physical <c>ldsfld</c>/<c>ldfld</c>/<c>ldsflda</c>/<c>ldflda</c>
    /// site, with the receiver argument Analysis proved for an instance access
    /// and whether the field address escapes, when call value flow is
    /// requested. The read/address counterpart of <see cref="FieldStores"/>,
    /// needed where a cached read never reaches a resolvable stack slot or an
    /// indirect write must invalidate stable provenance.
    /// </summary>
    public ImmutableArray<FieldLoadFact> FieldLoads { get; }

    /// <summary>
    /// The union of proven producers each non-void body can return, when
    /// call value flow is requested. Present with an unresolved value whenever any reachable
    /// return went unproven, so a consumer asking "can this method return
    /// anything else?" fails closed.
    /// </summary>
    public ImmutableArray<MethodReturnFlow> ReturnFlows { get; }
    readonly ImmutableArray<MethodLocalThrowEvidence> _localThrows;

    /// <summary>
    /// Physical local-throw evidence, including unresolved sites and unavailable
    /// bodies. No kickoff or enclosing-source attribution is applied.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The local-throw producer was not requested.
    /// </exception>
    public ImmutableArray<MethodLocalThrowEvidence> LocalThrows
        => (Features & LibraryBodyAnalysisFeatures.LocalThrows) != 0
            ? _localThrows
            : throw new InvalidOperationException(
                "Local throws were not requested for this body index.");

    public ImmutableArray<UnsafeEvidence> UnsafeEvidence { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
    /// <summary>The normalized producers included in this index.</summary>
    public LibraryBodyAnalysisFeatures Features { get; }
    /// <summary>
    /// True when method evidence covers the full module rather than a
    /// caller-supplied method or type scope. Recoverable body diagnostics are
    /// reported separately through <see cref="Diagnostics"/>.
    /// </summary>
    public bool HasFullMethodEvidenceScope { get; }

    /// <summary>
    /// Compact per-method ArrayPool ownership summaries produced during the
    /// body walk. No IL or control-flow state is retained.
    /// </summary>
    public ImmutableArray<ArrayPoolOwnershipMethodEvidence>
        ArrayPoolOwnership
    { get; }

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

    readonly LibraryOptimizationAnalysisResult _optimization;
    readonly LibraryCallGraphAnalysisResult _callGraph;
    readonly LibraryLeverageAnalysisResult _leverage;

    /// <summary>
    /// Focused call-graph result backing the compatibility members.
    /// </summary>
    public LibraryCallGraphAnalysisResult CallGraphAnalysis =>
        _callGraph;

    /// <summary>
    /// Focused leverage result backing the compatibility member.
    /// </summary>
    public LibraryLeverageAnalysisResult LeverageAnalysis =>
        _leverage;

    readonly ImmutableArray<MethodIdentity> _unsafeLeverageMethods;
    IReadOnlyDictionary<int, ImmutableArray<UnsafeEvidence>>? _unsafeEvidenceByMember;
    /// <summary>
    /// Drops the maps that back the single-assembly call-tree builders: the
    /// definition map, distinct-caller counts and edges, and direct-call
    /// grouping. It also drops implementation-profile projections derived
    /// from those call relationships.
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
        _callGraph.ReleaseCaches();
        _overloadRelationships = default;
        _projectedImplementationProfiles = default;
    }

    /// <summary>
    /// Source/IL optimization opportunities, each enriched with the containing method's
    /// <see cref="MethodLeverage.RootReach"/> so callers can prioritize the intersection
    /// of high-leverage methods and actionable rewrite shapes. Computed once on first
    /// access (the leverage join walks the whole-assembly call graph).
    /// </summary>
    public ImmutableArray<OptimizationOpportunity> OptimizationOpportunities
        => _optimization.Opportunities;

    /// <summary>
    /// Opt-in allocation fanout rows. Each row carries a sound IL-visible lower bound through
    /// exact intra-assembly call targets; uncertain, recursive, virtual, and external calls are
    /// counted as opaque rather than assigned invented targets.
    /// </summary>
    public ImmutableArray<OptimizationOpportunity>
        AllocationFanoutOpportunities
        => _optimization.AllocationFanoutOpportunities;

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
    /// The defining module's normalized memory-safety rules result.
    /// </summary>
    public MemorySafetyRulesResult MemorySafetyRules { get; }

    /// <summary>
    /// Whether the normalized module result selects the recognized updated
    /// rules. False covers every other state; callers that need the distinction
    /// consume <see cref="MemorySafetyRules"/>.
    /// </summary>
    public bool MemorySafetyRulesEnabled =>
        MemorySafetyRules is MemorySafetyRulesResult.Available
        {
            State: MemorySafetyRulesState.Updated,
        };

    /// <summary>Per-<see cref="CallerUnsafeMode"/> method counts across the whole assembly.</summary>
    public UnsafeModeBreakdown UnsafeModes { get; }

    readonly ImmutableArray<MethodBodyImplementationMetrics>
        _implementationProfiles;
    ImmutableArray<MethodImplementationProfile>
        _projectedImplementationProfiles;
    ImmutableArray<OverloadCallRelationship>
        _overloadRelationships;
    readonly IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> _allocationOccurrences;
    readonly IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> _unsafetyOccurrences;

    /// <summary>
    /// Per-method analysis signals (allocations, copies, unsafe, reflection,
    /// throw/catch/finally, evidence offsets), keyed by metadata token. Computed once
    /// from the call index and the body-scan signals, reused by the call-graph builders.
    /// </summary>
    IReadOnlyDictionary<int, MethodSignals> Signals =>
        _callGraph.MethodSignals;

    /// <summary>
    /// Returns per-method body/call signals keyed by metadata token.
    /// </summary>
    public IReadOnlyDictionary<int, MethodSignals> GetMethodSignals() => Signals;

    /// <summary>
    /// Objective implementation measurements, ordered by instruction count as
    /// a body-size baseline. This order is not a universal complexity score.
    /// </summary>
    public ImmutableArray<MethodImplementationProfile>
        ImplementationProfiles(
            Func<MethodIdentity, bool>? scope = null)
    {
        if (!Features.HasFlag(
                LibraryBodyAnalysisFeatures.ImplementationProfiles))
        {
            throw new InvalidOperationException(
                "Implementation profiles were not requested for this body index.");
        }

        if (_projectedImplementationProfiles.IsDefault)
        {
            _projectedImplementationProfiles =
                MethodImplementationProfileAnalysis.Collect(
                    _implementationProfiles,
                    DirectCalls,
                    Signals,
                    OverloadRelationships(),
                    DeclaredMethodMap);
        }

        return scope is null
            ? _projectedImplementationProfiles
            :
            [
                .. _projectedImplementationProfiles.Where(
                    profile => scope(profile.Method)),
            ];
    }

    /// <summary>
    /// Exact resolved calls between distinct methods sharing one declaring type
    /// and logical method name.
    /// </summary>
    public ImmutableArray<OverloadCallRelationship>
        OverloadRelationships()
    {
        if (_overloadRelationships.IsDefault)
        {
            _overloadRelationships = MethodImplementationProfileAnalysis
                .CollectOverloadRelationships(
                    DeclaredMethods,
                    DirectCalls,
                    DeclaredMethodMap);
        }
        return _overloadRelationships;
    }

    /// <summary>Offset-keyed allocation occurrences, grouped by containing method token.</summary>
    public IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> GetAllocationOccurrences() => _allocationOccurrences;

    public IReadOnlyDictionary<int, ImmutableArray<UnsafetyOccurrence>> GetUnsafetyOccurrences() => _unsafetyOccurrences;

    public IReadOnlyDictionary<int, ImmutableArray<DirectCall>> GetDirectCallsByCaller()
        => _callGraph.DirectCallsByCaller;

    /// <summary>
    /// Direct call sites grouped by the physical method body that owns their
    /// IL coordinates. The calls retain their declared <see cref="DirectCall.Caller"/>.
    /// </summary>
    public IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        GetDirectCallsByEvidenceMethod()
        => _callGraph.DirectCallsByEvidenceMethod;

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
        => _callGraph.ResolveDeclaredMethod(caller);

    /// <summary>
    /// Membership map for definitions with analyzable bodies. Correspondence
    /// always resolves through <see cref="DeclaredMethodMap"/> first.
    /// </summary>
    internal MethodDefinitionMap DeclaredMethodMap =>
        _callGraph.DeclaredMethodMap;

    internal LibraryBodyLocalCallGraph RootPathGraph()
        => _callGraph.RootPathGraph();

    public IReadOnlyDictionary<int, ImmutableArray<UnsafeEvidence>> GetUnsafeEvidenceByMember()
        => _unsafeEvidenceByMember ??= UnsafeEvidence
            .GroupBy(evidence => evidence.Member.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray());

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
        => _optimization.GeneratedFrameworkTypes;

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
        => LibraryBodyAnalysisService.AnalyzePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                LibraryBodyAnalysisFeatures.Default));

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
            moduleName: null,
            analysis: new(
                Methods: new(
                    DeclaredMethods: methods,
                    Methods: methods,
                    FailedMethodBodies: [],
                    DirectCalls: directCalls.IsDefault ? [] : directCalls,
                    ResultSinks: resultSinks.IsDefault ? [] : resultSinks,
                    FieldStores: fieldStores.IsDefault ? [] : fieldStores,
                    FieldLoads: fieldLoads.IsDefault ? [] : fieldLoads,
                    ReturnFlows: returnFlows.IsDefault ? [] : returnFlows,
                    BodySignals: new Dictionary<int, BodySignals>(),
                    ImplementationMetrics: [],
                    ImplementationMetricDiagnostics: [],
                    ImplementationProfiles: [],
                    InAssemblyTypeIsException:
                        new Dictionary<(string Namespace, string Name), bool>(),
                    NonHeapNewObjOperandTokens: new HashSet<int>(),
                    DeclaredSources: new Dictionary<int, MethodIdentity>(),
                    LocalThrows: []),
                Safety: new(
                    Evidence: unsafeEvidence,
                    LeverageMethods: [],
                    Rules: new MemorySafetyRulesResult.Available(
                        MemorySafetyRulesState.Legacy,
                        []),
                    Modes: new UnsafeModeBreakdown(
                        methods.Count(method =>
                            method.CallerUnsafeMode == CallerUnsafeMode.None),
                        methods.Count(method =>
                            method.CallerUnsafeMode
                                == CallerUnsafeMode.Implicit),
                        methods.Count(method =>
                            method.CallerUnsafeMode
                                == CallerUnsafeMode.Explicit),
                        methods.Count(method =>
                            method.CallerUnsafeMode
                                == CallerUnsafeMode.Unavailable)),
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
                    StringMaterializations: [],
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
                    : LibraryBodyAnalysisFeatures.Allocations),
            hasFullMethodEvidenceScope: true);
    }

    public static LibraryBodyIndex Open(string path, IAssemblyReferenceResolver? resolver = null,
        bool includeAllocations = true, bool includeOpportunities = true, IReadOnlySet<int>? bodyScope = null, Func<TypeRef, bool>? bodyTypeScope = null)
    {
        var features = LibraryBodyAnalysisFeatures.MethodEvidence;
        if (includeAllocations)
            features |= LibraryBodyAnalysisFeatures.Allocations;
        if (includeOpportunities)
            features |= LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        return LibraryBodyAnalysisService.AnalyzePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                features,
                bodyScope,
                bodyTypeScope),
            resolver);
    }

    public static LibraryBodyIndex Open(
        string path,
        LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null)
    {
        return LibraryBodyAnalysisService.AnalyzePath(
            path,
            LibraryBodyAnalysisRequest.Create(
                features,
                bodyScope,
                bodyTypeScope),
            resolver);
    }

    /// <summary>
    /// Compatibility facade for immutable-image Analysis execution. New
    /// consumers should use <see cref="LibraryBodyAnalysisService"/>.
    /// </summary>
    /// <remarks>
    /// <c>LibraryBodyAnalysisService_ConsumesImageWithoutReopeningSourceName</c>
    /// gates shared image consumption, and
    /// <c>LibraryBodyAnalysisService_ImageRequestHonorsBodyScope</c> gates
    /// scoped decoding through the service.
    /// </remarks>
    public static LibraryBodyIndex OpenFromPrefetchedImage(
        string path,
        ImmutableArray<byte> image,
        LibraryBodyAnalysisFeatures features,
        IAssemblyReferenceResolver? resolver = null,
        IReadOnlySet<int>? bodyScope = null,
        Func<TypeRef, bool>? bodyTypeScope = null)
    {
        return LibraryBodyAnalysisService.AnalyzeImage(
            path,
            image,
            LibraryBodyAnalysisRequest.Create(
                features,
                bodyScope,
                bodyTypeScope),
            resolver);
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
        if (!peReader.HasMetadata)
            return false;

        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            peReader.GetMetadataReader(),
            peReader);
        return builder.HasUnsafeEvidence();
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

    public ImmutableArray<DirectCall> FindCalls(MemberPattern pattern)
        => [.. DirectCalls.Where(call => pattern.Matches(call.Callee))];

    /// <summary>
    /// The most-leveraged requires-unsafe methods, ranked by distinct direct
    /// callers — the highest-value targets for `unsafe` marking, since marking
    /// them propagates the requirement to the most callers.
    /// </summary>
    public ImmutableArray<UnsafeMethodLeverage> TopUnsafeLeverage(int count = 6)
        => UnsafeLeverage.Top(
            _callGraph.PhysicalDirectCalls,
            _unsafeLeverageMethods,
            count,
            DeclaredMethodMap);

    /// <summary>
    /// The most-leveraged methods in this assembly, ranked by distinct direct
    /// callers. <paramref name="scope"/> optionally restricts which methods are
    /// ranked (for example, members declared on one selected type) while fanin
    /// is still measured across every caller in the assembly.
    /// </summary>
    public ImmutableArray<MethodLeverage> TopLeverage(int count = 25, Func<MethodIdentity, bool>? scope = null)
        => _leverage.Top(count, scope);

    /// <summary>
    /// Distinct callee types touched by calls from methods in <paramref name="callerScope"/>.
    /// Callee declaring types are reduced to their open definitions so generic instantiations
    /// stay bounded and same-type generic self-calls are excluded.
    /// </summary>
    public ImmutableArray<CalledTypeSummary> CalledTypes(Func<MethodIdentity, bool> callerScope)
        => _callGraph.CalledTypes(callerScope);

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
    /// Builds a bounded outbound call tree from the focused call-graph result.
    /// </summary>
    public CallTreeNode BuildCallTree(
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25) =>
        _callGraph.BuildCallTree(
            rootMethodToken,
            maxDepth,
            maxNodes);

    /// <summary>
    /// Builds a bounded reverse call tree from the focused call-graph result.
    /// </summary>
    public CallTreeNode BuildCallerTree(
        int rootMethodToken,
        int maxDepth = 3,
        int maxNodes = 25) =>
        _callGraph.BuildCallerTree(
            rootMethodToken,
            maxDepth,
            maxNodes);

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
            _callGraph,
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
            _callGraph,
            rootMethodToken,
            maxDepth,
            maxNodes);
    }
}
