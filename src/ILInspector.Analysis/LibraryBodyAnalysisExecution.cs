using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Common identity, coverage, and diagnostics for focused results produced by
/// one library-body Analysis execution.
/// </summary>
public sealed record LibraryBodyAnalysisReceipt(
    string SourceName,
    LibraryBodyModuleIdentity ModuleIdentity,
    LibraryBodyAnalysisFeatures Features,
    bool HasFullMethodEvidenceScope,
    ImmutableArray<AnalysisDiagnostic> Diagnostics)
{
    /// <summary>
    /// Diagnostic evidence of current physical Analysis-stage participation,
    /// when explicitly requested.
    /// </summary>
    public LibraryBodyAnalysisStageParticipationReceipt?
        StageParticipation
    { get; init; }
}

/// <summary>
/// Why a physical managed body did not issue an implementation profile in one
/// Analysis execution.
/// </summary>
public enum ImplementationProfileUnavailableReason
{
    /// <summary>Implementation-profile production did not participate.</summary>
    NotRequested,
    /// <summary>The body was outside the caller-selected evidence scope.</summary>
    ScopeExcluded,
    /// <summary>Recoverable body Analysis failed before a profile was issued.</summary>
    AnalysisFailed,
    /// <summary>Analysis produced no profile and no narrower reason was available.</summary>
    ProfileUnavailable,
}

/// <summary>
/// Physical managed body that did not issue an implementation profile.
/// </summary>
public sealed record ImplementationProfileUnavailableBody(
    MethodIdentity? EvidenceMethod,
    int MethodToken,
    ImplementationProfileUnavailableReason Reason,
    AnalysisDiagnostic? Diagnostic)
{
    public ImplementationProfileUnavailableBody(
        MethodIdentity evidenceMethod,
        ImplementationProfileUnavailableReason reason,
        AnalysisDiagnostic? diagnostic)
        : this(
            evidenceMethod,
            evidenceMethod.MetadataToken,
            reason,
            diagnostic)
    {
    }
}

/// <summary>
/// Analysis-issued population receipt for implementation-profile evidence.
/// </summary>
public sealed record ImplementationProfilePopulationCoverageReceipt(
    bool WasRequested,
    bool HasFullMethodEvidenceScope,
    ImmutableArray<MethodIdentity> DeclaredMethods,
    ImmutableArray<MethodIdentity> ManagedMethodBodies,
    ImmutableArray<MethodIdentity> ProfiledEvidenceBodies,
    ImmutableArray<ImplementationProfileUnavailableBody> UnavailableBodies,
    ImmutableArray<AnalysisDiagnostic> Diagnostics)
{
    /// <summary>Number of declared method identities in the execution.</summary>
    public int DeclaredMethodCount => DeclaredMethods.Length;

    /// <summary>
    /// Number of physical managed method bodies in the execution, including
    /// token-only failed bodies whose identity could not be decoded.
    /// </summary>
    public int ManagedMethodBodyCount =>
        ManagedMethodBodies.Length
        + UnavailableBodies.Count(static body =>
            body.EvidenceMethod is null);

    /// <summary>Number of physical bodies that issued implementation profiles.</summary>
    public int ProfiledEvidenceBodyCount => ProfiledEvidenceBodies.Length;

    /// <summary>Number of physical bodies that did not issue profiles.</summary>
    public int UnavailableBodyCount => UnavailableBodies.Length;
}

/// <summary>
/// Memory-safety contracts and unsafe evidence produced by one library-body
/// Analysis execution.
/// </summary>
public sealed record LibrarySafetyAnalysisResult
{
    readonly UnsafeModeBreakdown _unsafeModes;

    public LibrarySafetyAnalysisResult(
        LibraryBodyAnalysisReceipt receipt,
        MemorySafetyRulesResult memorySafetyRules,
        UnsafeModeBreakdown unsafeModes,
        ImmutableArray<UnsafeEvidence> evidence,
        IReadOnlyDictionary<
            int,
            ImmutableArray<UnsafetyOccurrence>> occurrences,
        ImmutableArray<UnsafeMemberUse> memberUses,
        UnsafeMemberCensus? memberCensus = null)
    {
        Receipt = receipt;
        MemorySafetyRules = memorySafetyRules;
        _unsafeModes = unsafeModes;
        Evidence = evidence;
        Occurrences = occurrences;
        MemberUses = memberUses;
        _memberCensus = memberCensus;
    }

    readonly UnsafeMemberCensus? _memberCensus;

    /// <summary>Whether this execution produced the unsafe member census.</summary>
    public bool HasMemberCensus => _memberCensus is not null;

    /// <summary>
    /// Unsafe member findings' census: inventory roles folded into declared
    /// members, with exposure and typed limitations.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This execution did not produce the census.
    /// </exception>
    public UnsafeMemberCensus MemberCensus =>
        _memberCensus
            ?? throw new InvalidOperationException(
                "Unsafe member census was not produced by this analysis execution.");

    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>The defining module's normalized memory-safety rules.</summary>
    public MemorySafetyRulesResult MemorySafetyRules { get; }

    /// <summary>
    /// Whole-declaration caller-unsafe-mode counts.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Method evidence was not requested for this execution.
    /// </exception>
    public UnsafeModeBreakdown UnsafeModes =>
        WasRequested
            ? _unsafeModes
            : throw new InvalidOperationException(
                "Unsafe-mode census was not requested for this analysis execution.");

    public ImmutableArray<UnsafeEvidence> Evidence { get; }

    public IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafetyOccurrence>> Occurrences { get; }

    /// <summary>
    /// Positive member inventory roles evaluated under updated language
    /// semantics.
    /// </summary>
    public ImmutableArray<UnsafeMemberUse> MemberUses { get; }

    /// <summary>
    /// Whether unsafe evidence and caller-unsafe-mode census production
    /// participated in this execution.
    /// </summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.MethodEvidence);

    /// <summary>Unsafe evidence grouped by the declared member token it describes.</summary>
    public IReadOnlyDictionary<int, ImmutableArray<UnsafeEvidence>>
        GetEvidenceByMember() =>
        Evidence
            .GroupBy(evidence => evidence.Member.MetadataToken)
            .ToDictionary(
                group => group.Key,
                group => group.ToImmutableArray());
}

/// <summary>
/// Objective implementation profiles and their supporting whole-library
/// classifications.
/// </summary>
public sealed record LibraryImplementationProfileAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    ImplementationProfilePopulationCoverageReceipt Coverage,
    ImmutableArray<MethodImplementationProfile> Profiles,
    ImmutableArray<OverloadCallRelationship> OverloadRelationships,
    ImmutableHashSet<TypeRef> GeneratedFrameworkTypes)
{
    /// <summary>
    /// Whether implementation-profile production participated in this
    /// execution.
    /// </summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.ImplementationProfiles);

    internal LibraryImplementationProfileAnalysisResult
        WithCompatibilityOverloadRelationships(
            LibraryCallGraphAnalysisResult callGraph)
    {
        if (WasRequested || !OverloadRelationships.IsDefaultOrEmpty)
            return this;

        return this with
        {
            OverloadRelationships =
                MethodImplementationProfileAnalysis
                    .CollectOverloadRelationships(
                        callGraph.DeclaredMethods,
                        callGraph.DirectCalls,
                        callGraph.DeclaredMethodMap),
        };
    }
}

/// <summary>
/// Detached terminal-resource facts produced from one resolution and body
/// acquisition generation.
/// </summary>
public sealed record LibraryResourceOccurrenceAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    bool WasRequested,
    ResourceEffectAdmissionReceipt? AdmissionReceipt,
    ImmutableArray<ResourceOccurrenceAnalysisResult> Methods,
    ImmutableArray<ResourceOccurrenceLimitation> Limitations)
{
    public bool IsComplete =>
        WasRequested
        && Limitations.IsEmpty
        && Methods.All(method => method.IsComplete);
}

/// <summary>
/// Detached interprocedural ownership summaries produced from one Resource
/// Occurrence and body-analysis generation.
/// </summary>
public sealed record LibraryResourceOwnershipAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    bool WasRequested,
    ResourceEffectAdmissionReceipt? AdmissionReceipt,
    ImmutableArray<ResourceOwnershipMethodSummary> Methods,
    ImmutableArray<ResourceOccurrenceLimitation> Limitations)
{
    public bool IsComplete =>
        WasRequested
        && Limitations.IsEmpty
        && Methods.All(method =>
            method.IsComplete
            && method.Acquisitions.All(
                static flow => flow.IsComplete)
            && method.Parameters.All(
                static flow => flow.IsComplete));
}

/// <summary>
/// Explicit focused results produced by one shared library-body Analysis
/// execution.
/// </summary>
public sealed class LibraryBodyAnalysisExecution
{
    private readonly string? _moduleName;
    private readonly LibraryBodyAnalysisResult _analysis;

    internal LibraryBodyAnalysisExecution(
        string sourceName,
        LibraryBodyModuleIdentity moduleIdentity,
        string? moduleName,
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan,
        LibraryBodyAnalysisStageParticipationReceipt?
            stageParticipation = null)
    {
        _moduleName = moduleName;
        _analysis = analysis;
        Receipt = new(
            sourceName,
            moduleIdentity,
            plan.Features,
            HasFullMethodEvidenceScope(plan),
            analysis.Diagnostics)
        {
            StageParticipation = stageParticipation,
        };
        CallGraph = new(
            Receipt,
            _moduleName,
            analysis);
        LocalThrows = new(
            Receipt,
            analysis.Methods.LocalThrows);
        JsonWireContracts = new(
            Receipt,
            CallGraph,
            analysis);
        var generatedFrameworkTypes =
            new GeneratedFrameworkTypeSet(CallGraph);
        Leverage = new(
            Receipt,
            CallGraph,
            generatedFrameworkTypes,
            analysis.Safety.LeverageMethods);
        Safety = new(
            Receipt,
            analysis.Safety.Rules,
            analysis.Safety.Modes,
            analysis.Safety.Evidence,
            analysis.Safety.Occurrences,
            analysis.Safety.MemberUses,
            analysis.Safety.MemberCensus);
        Allocations = new(
            Receipt,
            analysis.Allocations);
        ImplementationProfiles =
            CreateImplementationProfileResult(
                Receipt,
                analysis,
                CallGraph,
                generatedFrameworkTypes);
        ImplementationMetrics =
            CreateImplementationMetricResult(
                Receipt,
                analysis,
                CallGraph,
                ImplementationProfiles,
                plan);
        Optimization = new(
            Receipt,
            analysis,
            CallGraph,
            generatedFrameworkTypes);
        ResourceOccurrences = new(
            Receipt,
            plan.IncludesResourceOccurrences,
            plan.ResourceEffects?.Receipt,
            analysis.ResourceOccurrences?.Methods ?? [],
            analysis.ResourceOccurrences?.Limitations ?? []);
        ResourceOwnership = new(
            Receipt,
            plan.IncludesResourceOccurrences,
            plan.ResourceEffects?.Receipt,
            analysis.ResourceOwnership?.Methods ?? [],
            analysis.ResourceOwnership?.Limitations ?? []);
        ResourceLifecycle = new(
            Receipt,
            plan.IncludesResourceLifecycle,
            plan.ResourceEffects?.Receipt,
            analysis.ResourceLifecycle?.Methods ?? [],
            analysis.ResourceLifecycle?.Limitations ?? []);
    }

    internal static LibraryBodyAnalysisExecution FromEvidence(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<UnsafeEvidence> unsafeEvidence,
        IReadOnlyDictionary<
            int,
            ImmutableArray<AllocationOccurrence>>?
            allocationOccurrences = null,
        IReadOnlyDictionary<
            int,
            ImmutableArray<UnsafetyOccurrence>>?
            unsafetyOccurrences = null,
        ImmutableArray<AnalysisDiagnostic> diagnostics = default,
        ImmutableArray<DirectCall> directCalls = default,
        ImmutableArray<MethodResultSink> resultSinks = default,
        ImmutableArray<FieldStoreFact> fieldStores = default,
        ImmutableArray<FieldLoadFact> fieldLoads = default,
        ImmutableArray<MethodReturnFlow> returnFlows = default,
        LibraryBodyModuleIdentity? moduleIdentity = null,
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence)
    {
        moduleIdentity ??= SyntheticEvidenceIdentity(methods);
        ValidateSyntheticEvidenceIdentity(moduleIdentity, methods);
        features |= allocationOccurrences is null
            ? LibraryBodyAnalysisFeatures.None
            : LibraryBodyAnalysisFeatures.Allocations;
        var analysis = new LibraryBodyAnalysisResult(
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
                    new Dictionary<
                        (string Namespace, string Name),
                        bool>(),
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
                        method.CallerUnsafeMode
                            == CallerUnsafeMode.None),
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
                        ImmutableArray<UnsafetyOccurrence>>(),
                MemberUses: []),
            Allocations: new(
                allocationOccurrences
                    ?? new Dictionary<
                        int,
                        ImmutableArray<AllocationOccurrence>>()),
            Optimizations: new(
                Opportunities: [],
                StringMaterializations: [],
                SuppressedMethodTokens: new HashSet<int>(),
                ScopeExcludedMethodTokens: new HashSet<int>(),
                ExceptionTypeNames:
                    new HashSet<string>(StringComparer.Ordinal)),
            Diagnostics: diagnostics.IsDefault ? [] : diagnostics);

        return new(
            sourceName: "",
            moduleIdentity,
            moduleName: null,
            analysis,
            LibraryBodyAnalysisPlan.Create(
                features,
                methodScope: null,
                typeScope: null));
    }

    /// <summary>
    /// Identity, coverage, and diagnostics shared by this execution's focused
    /// results.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>Focused unsafe-evidence result.</summary>
    public LibrarySafetyAnalysisResult Safety { get; }

    /// <summary>Focused allocation-occurrence result.</summary>
    public LibraryAllocationAnalysisResult Allocations { get; }

    /// <summary>Focused implementation-profile result.</summary>
    public LibraryImplementationProfileAnalysisResult
        ImplementationProfiles
    { get; }

    internal LibraryImplementationMetricAnalysisResult
        ImplementationMetrics
    { get; }

    /// <summary>Focused optimization-opportunity result.</summary>
    public LibraryOptimizationAnalysisResult Optimization { get; }

    /// <summary>Focused local call-graph result.</summary>
    public LibraryCallGraphAnalysisResult CallGraph { get; }

    /// <summary>Focused physical local-throw evidence.</summary>
    public LibraryLocalThrowAnalysisResult LocalThrows { get; }

    /// <summary>Focused JSON wire-contract call and value-flow evidence.</summary>
    public LibraryJsonWireContractAnalysisResult JsonWireContracts { get; }

    /// <summary>Focused whole-library leverage result.</summary>
    public LibraryLeverageAnalysisResult Leverage { get; }

    /// <summary>Focused root-bound Resource Occurrence result.</summary>
    public LibraryResourceOccurrenceAnalysisResult ResourceOccurrences
    { get; }

    /// <summary>Focused interprocedural Resource Ownership summary.</summary>
    public LibraryResourceOwnershipAnalysisResult ResourceOwnership
    { get; }

    /// <summary>Focused root-bound Resource Lifecycle result.</summary>
    public LibraryResourceLifecycleAnalysisResult ResourceLifecycle
    { get; }

    internal ImplementationMetricWorkBudgetSnapshot?
        ImplementationMetricWork =>
        _analysis.ImplementationMetricWork;

    private static bool HasFullMethodEvidenceScope(
        LibraryBodyAnalysisPlan plan) =>
        plan.Includes(LibraryBodyAnalysisFeatures.MethodEvidence)
        && !plan.IsScoped;

    static LibraryImplementationMetricAnalysisResult
        CreateImplementationMetricResult(
            LibraryBodyAnalysisReceipt receipt,
            LibraryBodyAnalysisResult analysis,
            LibraryCallGraphAnalysisResult callGraph,
            LibraryImplementationProfileAnalysisResult
                implementationProfiles,
            LibraryBodyAnalysisPlan plan)
    {
        ImmutableArray<AnalysisDiagnostic> metricDiagnostics =
            AnalysisDiagnosticAggregation.MergeInMetadataOrder(
                analysis.Diagnostics,
                analysis.Methods
                    .ImplementationMetricDiagnostics);
        if (plan.ImplementationMetrics is not { } metricPlan)
        {
            return new(
                receipt,
                WasRequested: false,
                Participation: null,
                analysis.Methods.DeclaredMethods,
                analysis.Methods.Methods,
                analysis.Methods.FailedMethodBodies,
                [],
                SiblingRelationships: null,
                metricDiagnostics);
        }

        ImmutableArray<MethodImplementationMetricEvidence> bodies =
            analysis.Methods.ImplementationMetrics;
        if (metricPlan.IncludesDirectCallMetric)
        {
            bodies = PublishDirectCallMetrics(
                bodies,
                callGraph,
                metricDiagnostics);
        }
        ImplementationMetricSiblingRelationships?
            siblingRelationships = null;
        ImmutableArray<ImplementationMetricStageParticipation>
            actualStages =
                analysis.ImplementationMetricParticipation
                    ?.Stages ?? [];
        if (metricPlan.RequestedMetrics.HasFlag(
                ImplementationMetricKind
                    .SiblingOverloadRelationships))
        {
            siblingRelationships =
                PublishSiblingRelationships(
                    bodies,
                    callGraph,
                    implementationProfiles,
                    metricDiagnostics);
            if (!bodies.IsEmpty)
            {
                actualStages =
                    AddSiblingRelationshipParticipation(
                        actualStages,
                        bodies,
                        plan.RequestedFeatures
                            & LibraryBodyAnalysisFeatures
                                .ImplementationProfiles);
            }
        }

        return new(
            receipt,
            WasRequested: true,
            new(
                metricPlan.RequestedMetrics,
                metricPlan.RequiredFacts,
                metricPlan.WorkStages,
                metricPlan.UsesFocusedExecution
                    && plan.RequestedFeatures
                        == LibraryBodyAnalysisFeatures.None,
                actualStages,
                analysis.ImplementationMetricParticipation
                    ?.InstructionSourceWork,
                analysis.ImplementationMetricWork),
            analysis.Methods.DeclaredMethods,
            analysis.Methods.Methods,
            analysis.Methods.FailedMethodBodies,
            bodies,
            siblingRelationships,
            metricDiagnostics);
    }

    static ImplementationMetricSiblingRelationships
        PublishSiblingRelationships(
            ImmutableArray<MethodImplementationMetricEvidence> bodies,
            LibraryCallGraphAnalysisResult callGraph,
            LibraryImplementationProfileAnalysisResult
                implementationProfiles,
            ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        var relationshipDiagnostics =
            diagnostics.ToBuilder();
        HashSet<int> diagnosedTokens =
        [
            .. diagnostics.Select(static diagnostic =>
                diagnostic.MethodToken),
        ];
        foreach (MethodImplementationMetricEvidence body in bodies)
        {
            if (body.DirectCallCollectionAttempted
                && body.DirectCallCollectionComplete)
            {
                continue;
            }
            if (!diagnosedTokens.Add(
                    body.EvidenceMethod.MetadataToken))
            {
                continue;
            }

            relationshipDiagnostics.Add(
                new(
                    body.EvidenceMethod.MetadataToken,
                    body.EvidenceMethod.Name,
                    body.DirectCalls?.IncompleteReason
                        ?? "Direct-call collection did not complete.",
                    SourceMethodToken:
                        body.Method.MetadataToken,
                    DeclaringType:
                        body.EvidenceMethod.DeclaringType,
                    SourceDeclaringType:
                        body.Method.DeclaringType));
        }

        return new(
            implementationProfiles.WasRequested
                && CanReuseSiblingRelationships(
                    bodies,
                    implementationProfiles
                        .OverloadRelationships)
                ? implementationProfiles
                    .OverloadRelationships
                : MethodImplementationProfileAnalysis
                    .CollectOverloadRelationships(
                        callGraph.DeclaredMethods,
                        SelectMetricDirectCalls(
                            bodies,
                            callGraph.DirectCalls),
                        callGraph.DeclaredMethodMap),
            relationshipDiagnostics.ToImmutable());
    }

    static bool CanReuseSiblingRelationships(
        ImmutableArray<MethodImplementationMetricEvidence> bodies,
        ImmutableArray<OverloadCallRelationship> relationships)
    {
        foreach (OverloadCallRelationship relationship
            in relationships)
        {
            bool admitted = false;
            foreach (MethodImplementationMetricEvidence body
                in bodies)
            {
                if (body.EvidenceMethod.MetadataToken
                    != relationship.EvidenceMethod.MetadataToken)
                {
                    continue;
                }

                admitted = true;
                break;
            }
            if (!admitted)
                return false;
        }

        return true;
    }

    static ImmutableArray<DirectCall> SelectMetricDirectCalls(
        ImmutableArray<MethodImplementationMetricEvidence> bodies,
        ImmutableArray<DirectCall> directCalls)
    {
        if (bodies.IsEmpty || directCalls.IsEmpty)
            return [];

        HashSet<int> evidenceTokens =
        [
            .. bodies.Select(static body =>
                body.EvidenceMethod.MetadataToken),
        ];
        return
        [
            .. directCalls.Where(call =>
                evidenceTokens.Contains(
                    call.EvidenceMethod.MetadataToken)),
        ];
    }

    static ImmutableArray<ImplementationMetricStageParticipation>
        AddSiblingRelationshipParticipation(
            ImmutableArray<ImplementationMetricStageParticipation>
                stages,
            ImmutableArray<MethodImplementationMetricEvidence> bodies,
            LibraryBodyAnalysisFeatures featureCauses)
    {
        int completedBodies = bodies.Count(static body =>
            body.DirectCallCollectionAttempted
            && body.DirectCallCollectionComplete);
        return
        [
            .. stages
                .Append(
                    new(
                        ImplementationMetricWorkStage
                            .SiblingRelationshipProjection,
                        ImplementationMetricKind
                            .SiblingOverloadRelationships,
                        featureCauses,
                        bodies.Length,
                        completedBodies,
                        bodies.Length - completedBodies))
                .OrderBy(static stage => stage.Stage),
        ];
    }

    static ImmutableArray<MethodImplementationMetricEvidence>
        PublishDirectCallMetrics(
            ImmutableArray<MethodImplementationMetricEvidence> bodies,
            LibraryCallGraphAnalysisResult callGraph,
            ImmutableArray<AnalysisDiagnostic> diagnostics)
    {
        Dictionary<int, DirectCall[]> callsByEvidenceMethod =
            callGraph.DirectCalls
                .GroupBy(static call =>
                    call.EvidenceMethod.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.ToArray());
        Dictionary<int, AnalysisDiagnostic> diagnosticsByToken =
            diagnostics
                .GroupBy(static diagnostic =>
                    diagnostic.MethodToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First());

        return
        [
            .. bodies.Select(body =>
            {
                if (!body.DirectCallCollectionAttempted)
                    return body;

                callsByEvidenceMethod.TryGetValue(
                    body.EvidenceMethod.MetadataToken,
                    out DirectCall[]? calls);
                calls ??= [];
                string? incompleteReason = null;
                if (!body.DirectCallCollectionComplete)
                {
                    incompleteReason =
                        diagnosticsByToken.TryGetValue(
                            body.EvidenceMethod.MetadataToken,
                            out AnalysisDiagnostic? diagnostic)
                            ? diagnostic.Message
                            : "Direct-call collection did not complete.";
                }

                return body with
                {
                    DirectCalls =
                        MethodImplementationProfileAnalysis
                            .MeasureDirectCalls(
                                body.DirectCallCount?.Count
                                    ?? 0,
                                calls,
                                callGraph.DeclaredMethodMap,
                                incompleteReason),
                };
            }),
        ];
    }

    private static LibraryImplementationProfileAnalysisResult
        CreateImplementationProfileResult(
            LibraryBodyAnalysisReceipt receipt,
            LibraryBodyAnalysisResult analysis,
            LibraryCallGraphAnalysisResult callGraph,
            GeneratedFrameworkTypeSet generatedFrameworkTypes)
    {
        if (!receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.ImplementationProfiles))
        {
            return new(
                receipt,
                CreateImplementationProfileCoverage(
                    receipt,
                    analysis,
                    wasRequested: false),
                [],
                [],
                []);
        }

        ImmutableArray<OverloadCallRelationship> relationships =
            MethodImplementationProfileAnalysis
                .CollectOverloadRelationships(
                    callGraph.DeclaredMethods,
                    callGraph.DirectCalls,
                    callGraph.DeclaredMethodMap);

        return new(
            receipt,
            CreateImplementationProfileCoverage(
                receipt,
                analysis,
                wasRequested: true),
            MethodImplementationProfileAnalysis.Collect(
                analysis.Methods.ImplementationProfiles,
                callGraph.DirectCalls,
                callGraph.MethodSignals,
                relationships,
                callGraph.DeclaredMethodMap,
                analysis.Methods.ImplementationMetrics
                    .Where(static body =>
                        body.DirectCallCount is not null)
                    .ToDictionary(
                        static body =>
                            body.EvidenceMethod.MetadataToken,
                        static body =>
                            body.DirectCallCount!.Count)),
            relationships,
            generatedFrameworkTypes.Types);
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

    private static ImplementationProfilePopulationCoverageReceipt
        CreateImplementationProfileCoverage(
            LibraryBodyAnalysisReceipt receipt,
            LibraryBodyAnalysisResult analysis,
            bool wasRequested)
    {
        if (!wasRequested)
        {
            return new(
                WasRequested: false,
                receipt.HasFullMethodEvidenceScope,
                analysis.Methods.DeclaredMethods,
                analysis.Methods.Methods,
                [],
                [],
                analysis.Diagnostics);
        }

        ImmutableArray<MethodIdentity> profiledBodies =
        [
            .. analysis.Methods.ImplementationProfiles
                .Select(static profile => profile.EvidenceMethod),
        ];
        HashSet<int> profiledTokens =
        [
            .. profiledBodies.Select(static method => method.MetadataToken),
        ];
        Dictionary<int, AnalysisDiagnostic> diagnosticsByToken =
            analysis.Diagnostics
                .GroupBy(static diagnostic => diagnostic.MethodToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First());

        ImmutableArray<ImplementationProfileUnavailableBody>
            unavailableBodies =
        [
            .. analysis.Methods.Methods
                .Where(method => !profiledTokens.Contains(
                    method.MetadataToken))
                .Select(method =>
                {
                    diagnosticsByToken.TryGetValue(
                        method.MetadataToken,
                        out AnalysisDiagnostic? diagnostic);
                    return new ImplementationProfileUnavailableBody(
                        method,
                        UnavailableReason(
                            wasRequested,
                            receipt.HasFullMethodEvidenceScope,
                            diagnostic),
                        diagnostic);
                }),
            .. analysis.Methods.FailedMethodBodies
                .Select(body => new ImplementationProfileUnavailableBody(
                    null,
                    body.MethodToken,
                    ImplementationProfileUnavailableReason.AnalysisFailed,
                    body.Diagnostic)),
        ];

        return new(
            wasRequested,
            receipt.HasFullMethodEvidenceScope,
            analysis.Methods.DeclaredMethods,
            analysis.Methods.Methods,
            profiledBodies,
            unavailableBodies,
            analysis.Diagnostics);
    }

    private static ImplementationProfileUnavailableReason
        UnavailableReason(
            bool wasRequested,
            bool hasFullMethodEvidenceScope,
            AnalysisDiagnostic? diagnostic)
    {
        if (!wasRequested)
            return ImplementationProfileUnavailableReason.NotRequested;
        if (diagnostic is not null)
            return ImplementationProfileUnavailableReason.AnalysisFailed;
        if (!hasFullMethodEvidenceScope)
            return ImplementationProfileUnavailableReason.ScopeExcluded;
        return ImplementationProfileUnavailableReason.ProfileUnavailable;
    }
}
