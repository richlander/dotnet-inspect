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
        | JsonWireContractFlow
        | LocalThrows
        | ImplementationProfiles,
}

/// <summary>
/// Materialized IL body evidence for one assembly.
/// </summary>
public sealed class LibraryBodyIndex
{
    internal LibraryBodyIndex(
        string path,
        LibraryBodyModuleIdentity moduleIdentity,
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisFeatures features,
        bool hasFullMethodEvidenceScope,
        LibraryOptimizationAnalysisResult optimization,
        LibraryCallGraphAnalysisResult callGraph,
        LibraryImplementationProfileAnalysisResult
            implementationProfiles)
    {
        Path = path;
        ModuleIdentity = moduleIdentity;
        DeclaredMethods = analysis.Methods.DeclaredMethods;
        Methods = analysis.Methods.Methods;
        ResultSinks = analysis.Methods.ResultSinks;
        FieldStores = analysis.Methods.FieldStores;
        FieldLoads = analysis.Methods.FieldLoads;
        ReturnFlows = analysis.Methods.ReturnFlows;
        Diagnostics = analysis.Diagnostics;
        bool hasFullScope =
            (features
                & LibraryBodyAnalysisFeatures.MethodEvidence) != 0
            && hasFullMethodEvidenceScope;
        _callGraph = callGraph;
        _optimization = optimization;
        _implementationProfileAnalysis = implementationProfiles;
        _allocationOccurrences = analysis.Allocations.Occurrences;
        Features = features;
        HasFullMethodEvidenceScope = hasFullScope;
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
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }
    /// <summary>The normalized producers included in this index.</summary>
    public LibraryBodyAnalysisFeatures Features { get; }
    /// <summary>
    /// True when method evidence covers the full module rather than a
    /// caller-supplied method or type scope. Recoverable body diagnostics are
    /// reported separately through <see cref="Diagnostics"/>.
    /// </summary>
    public bool HasFullMethodEvidenceScope { get; }

    readonly LibraryOptimizationAnalysisResult _optimization;
    readonly LibraryCallGraphAnalysisResult _callGraph;
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

    readonly LibraryImplementationProfileAnalysisResult
        _implementationProfileAnalysis;
    readonly IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> _allocationOccurrences;
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

        return scope is null
            ? _implementationProfileAnalysis.Profiles
            :
            [
                .. _implementationProfileAnalysis.Profiles.Where(
                    profile => scope(profile.Method)),
            ];
    }

    /// <summary>
    /// Exact resolved calls between distinct methods sharing one declaring type
    /// and logical method name.
    /// </summary>
    public ImmutableArray<OverloadCallRelationship>
        OverloadRelationships()
        => _implementationProfileAnalysis.OverloadRelationships;

    /// <summary>Offset-keyed allocation occurrences, grouped by containing method token.</summary>
    public IReadOnlyDictionary<int, ImmutableArray<AllocationOccurrence>> GetAllocationOccurrences() => _allocationOccurrences;

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
        LibraryBodyModuleIdentity? moduleIdentity = null,
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence)
        => LibraryBodyAnalysisExecution.FromEvidence(
            methods,
            unsafeEvidence,
            allocationOccurrences,
            unsafetyOccurrences,
            diagnostics,
            directCalls,
            resultSinks,
            fieldStores,
            fieldLoads,
            returnFlows,
            moduleIdentity,
            features)
        .CompatibilityIndex();

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

}
