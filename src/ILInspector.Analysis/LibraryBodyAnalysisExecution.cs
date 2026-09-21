using System.Collections.Immutable;

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
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

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

/// <summary>Unsafe evidence produced by one library-body Analysis execution.</summary>
public sealed record LibrarySafetyAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    ImmutableArray<UnsafeEvidence> Evidence,
    IReadOnlyDictionary<
        int,
        ImmutableArray<UnsafetyOccurrence>> Occurrences)
{
    /// <summary>Whether unsafe-evidence production participated in this execution.</summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.MethodEvidence);
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
/// Explicit focused results produced by one shared library-body Analysis
/// execution.
/// </summary>
public sealed class LibraryBodyAnalysisExecution
{
    private readonly string? _moduleName;
    private readonly LibraryBodyAnalysisResult _analysis;
    private LibraryBodyIndex? _compatibilityIndex;

    internal LibraryBodyAnalysisExecution(
        string sourceName,
        LibraryBodyModuleIdentity moduleIdentity,
        string? moduleName,
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan)
    {
        _moduleName = moduleName;
        _analysis = analysis;
        Receipt = new(
            sourceName,
            moduleIdentity,
            plan.Features,
            HasFullMethodEvidenceScope(plan),
            analysis.Diagnostics);
        CallGraph = new(
            Receipt,
            _moduleName,
            analysis);
        var generatedFrameworkTypes =
            new GeneratedFrameworkTypeSet(CallGraph);
        Leverage = new(
            Receipt,
            CallGraph,
            generatedFrameworkTypes);
        Safety = new(
            Receipt,
            analysis.Safety.Evidence,
            analysis.Safety.Occurrences);
        Allocations = new(
            Receipt,
            analysis.Allocations);
        ImplementationProfiles =
            CreateImplementationProfileResult(
                Receipt,
                analysis,
                CallGraph,
                generatedFrameworkTypes);
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
        ResourceLifecycle = new(
            Receipt,
            plan.IncludesResourceLifecycle,
            plan.ResourceEffects?.Receipt,
            analysis.ResourceLifecycle?.Methods ?? [],
            analysis.ResourceLifecycle?.Limitations ?? []);
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

    /// <summary>Focused optimization-opportunity result.</summary>
    public LibraryOptimizationAnalysisResult Optimization { get; }

    /// <summary>Focused local call-graph result.</summary>
    public LibraryCallGraphAnalysisResult CallGraph { get; }

    /// <summary>Focused whole-library leverage result.</summary>
    public LibraryLeverageAnalysisResult Leverage { get; }

    /// <summary>Focused root-bound Resource Occurrence result.</summary>
    public LibraryResourceOccurrenceAnalysisResult ResourceOccurrences
    { get; }

    /// <summary>Focused root-bound Resource Lifecycle result.</summary>
    public LibraryResourceLifecycleAnalysisResult ResourceLifecycle
    { get; }

    internal bool HasMaterializedCompatibilityIndex =>
        _compatibilityIndex is not null;

    /// <summary>
    /// Creates the transitional <see cref="LibraryBodyIndex"/> adapter used by
    /// consumers that have not yet migrated to focused results.
    /// </summary>
    public LibraryBodyIndex CompatibilityIndex() =>
        _compatibilityIndex ??= new(
            Receipt.SourceName,
            Receipt.ModuleIdentity,
            _moduleName,
            _analysis,
            Receipt.Features,
            Receipt.HasFullMethodEvidenceScope,
            Optimization,
            CallGraph,
            Leverage);

    private static bool HasFullMethodEvidenceScope(
        LibraryBodyAnalysisPlan plan) =>
        plan.Includes(LibraryBodyAnalysisFeatures.MethodEvidence)
        && !plan.IsScoped;

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
                callGraph.DeclaredMethodMap),
            relationships,
            generatedFrameworkTypes.Types);
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
