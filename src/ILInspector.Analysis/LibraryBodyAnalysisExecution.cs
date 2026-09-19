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

/// <summary>Unsafe evidence produced by one library-body Analysis execution.</summary>
public sealed record LibrarySafetyAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    ImmutableArray<UnsafeEvidence> Evidence)
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
/// Completed optimization opportunities and the supporting classifications
/// produced by one library-body Analysis execution.
/// </summary>
public sealed class LibraryOptimizationAnalysisResult
{
    private readonly LibraryOptimizationAnalysisProjection? _projection;
    private readonly Lazy<ImmutableHashSet<TypeRef>>
        _generatedFrameworkTypes;

    internal LibraryOptimizationAnalysisResult(
        LibraryBodyAnalysisReceipt receipt,
        LibraryOptimizationAnalysisProjection? projection,
        Lazy<ImmutableHashSet<TypeRef>> generatedFrameworkTypes)
    {
        Receipt = receipt;
        _projection = projection;
        _generatedFrameworkTypes = generatedFrameworkTypes;
    }

    /// <summary>
    /// Identity, coverage, and diagnostics for the shared execution.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>
    /// Source and IL opportunities completed with leverage, provenance, and
    /// caller-loop evidence.
    /// </summary>
    public ImmutableArray<OptimizationOpportunity> Opportunities =>
        WasRequested
            ? _projection!.Opportunities
            : [];

    /// <summary>
    /// Opt-in whole-library allocation fanout opportunities.
    /// </summary>
    public ImmutableArray<OptimizationOpportunity>
        AllocationFanoutOpportunities =>
        WasRequested
            ? _projection!.AllocationFanoutOpportunities
            : [];

    /// <summary>
    /// Exact identities classified as generated framework implementation
    /// detail.
    /// </summary>
    public ImmutableHashSet<TypeRef> GeneratedFrameworkTypes =>
        WasRequested
            ? _generatedFrameworkTypes.Value
            : [];

    /// <summary>
    /// Whether full optimization-opportunity production participated in this
    /// execution.
    /// </summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);
}

/// <summary>
/// Explicit focused results produced by one shared library-body Analysis
/// execution.
/// </summary>
public sealed class LibraryBodyAnalysisExecution
{
    private readonly string? _moduleName;
    private readonly LibraryBodyAnalysisResult _analysis;
    private readonly ImmutableArray<DirectCall> _physicalDirectCalls;
    private readonly LibraryOptimizationAnalysisProjection?
        _optimizationProjection;
    private readonly Lazy<ImmutableHashSet<TypeRef>>
        _generatedFrameworkTypes;
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
        _physicalDirectCalls =
        [
            .. analysis.Methods.DirectCalls.Select(static call =>
                call.Caller == call.EvidenceMethod
                    ? call
                    : call with
                    {
                        Caller = call.EvidenceMethod,
                    }),
        ];
        ImmutableArray<DirectCall> physicalDirectCalls =
            _physicalDirectCalls;
        ImmutableArray<MethodIdentity> methods =
            analysis.Methods.Methods;
        _generatedFrameworkTypes = new(() =>
            GeneratedFrameworkTypeAnalysis.Collect(
                    physicalDirectCalls,
                    methods)
                .ToImmutableHashSet());
        if ((Receipt.Features
                & (LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities
                    | LibraryBodyAnalysisFeatures
                        .AsyncSiblingOpportunities)) != 0)
        {
            _optimizationProjection =
                new(
                    analysis,
                    Receipt.Features,
                    _moduleName,
                    _physicalDirectCalls);
        }
        Safety = new(
            Receipt,
            analysis.Safety.Evidence);
        ImplementationProfiles =
            CreateImplementationProfileResult(
                Receipt,
                analysis,
                _moduleName,
                _physicalDirectCalls,
                _generatedFrameworkTypes);
        Optimization = new(
            Receipt,
            Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities)
                ? _optimizationProjection
                : null,
            _generatedFrameworkTypes);
    }

    /// <summary>
    /// Identity, coverage, and diagnostics shared by this execution's focused
    /// results.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>Focused unsafe-evidence result.</summary>
    public LibrarySafetyAnalysisResult Safety { get; }

    /// <summary>Focused implementation-profile result.</summary>
    public LibraryImplementationProfileAnalysisResult
        ImplementationProfiles { get; }

    /// <summary>Focused optimization-opportunity result.</summary>
    public LibraryOptimizationAnalysisResult Optimization { get; }

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
            _physicalDirectCalls,
            _optimizationProjection,
            _generatedFrameworkTypes);

    private static bool HasFullMethodEvidenceScope(
        LibraryBodyAnalysisPlan plan) =>
        plan.Includes(LibraryBodyAnalysisFeatures.MethodEvidence)
        && !plan.IsScoped;

    private static LibraryImplementationProfileAnalysisResult
        CreateImplementationProfileResult(
            LibraryBodyAnalysisReceipt receipt,
            LibraryBodyAnalysisResult analysis,
            string? moduleName,
            ImmutableArray<DirectCall> physicalDirectCalls,
            Lazy<ImmutableHashSet<TypeRef>>
                generatedFrameworkTypes)
    {
        if (!receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.ImplementationProfiles))
        {
            return new(
                receipt,
                [],
                [],
                []);
        }

        MethodDefinitionMap methodMap =
            MethodDefinitionMap.Create(
                analysis.Methods.DeclaredMethods,
                moduleName);
        Dictionary<int, MethodSignals> signals =
            MethodSignalAnalysis.Collect(
                physicalDirectCalls,
                analysis.Safety.Evidence,
                analysis.Methods.BodySignals,
                receipt.Features.HasFlag(
                    LibraryBodyAnalysisFeatures.Allocations)
                    ? analysis.Allocations.Occurrences
                    : null,
                analysis.Methods.InAssemblyTypeIsException,
                analysis.Methods.NonHeapNewObjOperandTokens);
        ImmutableArray<OverloadCallRelationship> relationships =
            MethodImplementationProfileAnalysis
                .CollectOverloadRelationships(
                    analysis.Methods.DeclaredMethods,
                    analysis.Methods.DirectCalls,
                    methodMap);

        return new(
            receipt,
            MethodImplementationProfileAnalysis.Collect(
                analysis.Methods.ImplementationProfiles,
                analysis.Methods.DirectCalls,
                signals,
                relationships,
                methodMap),
            relationships,
            generatedFrameworkTypes.Value);
    }
}
