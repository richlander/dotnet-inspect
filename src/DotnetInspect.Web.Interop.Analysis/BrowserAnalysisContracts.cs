using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Analysis;

/// <summary>
/// The Analysis facade's browser wire contract.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>DotnetInspect.Web.Interop.Analysis</c>. Records that are structurally equal to another
/// facade's are separate module-local contracts by design;
/// <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </remarks>
public sealed record BrowserCompileLibraryAvailability(
    BrowserCompileLibraryStatus Status,
    string? TargetFramework,
    string? Message);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserCompileLibraryStatus>))]
public enum BrowserCompileLibraryStatus
{
    Selected,
    NoCompileAssets,
    NoMatchingTargetFramework,
    EmptyCompileGroup,
    InvalidImplementationAssets,
}

public sealed record BrowserAnalysisInspectionEnvelope(
    JsonElement Content,
    BrowserAnalysisInspectionShare Share,
    BrowserAnalysisInspectionDiagnostic[] Diagnostics);

public sealed record BrowserAnalysisInspectionShare(
    string Kind,
    string? FullUrl,
    string? Packet,
    string? Path,
    string? Reason);

public enum BrowserAnalysisInspectionDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record BrowserAnalysisInspectionDiagnostic(
    string Code,
    BrowserAnalysisInspectionDiagnosticSeverity Severity,
    string Summary,
    string? Correspondence);

public sealed record BrowserImplementationProfiles(
    int SchemaVersion,
    string Outcome,
    BrowserImplementationProfileSubject? Subject,
    BrowserImplementationProfileContent? Content,
    BrowserImplementationProfileFailure? Failure,
    BrowserAnalysisInspectionShare? Share,
    BrowserAnalysisInspectionDiagnostic[] Diagnostics,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserImplementationProfileSubject(
    BrowserAnalysisAssemblyIdentity Identity,
    string? ModuleVersionId,
    BrowserImplementationProfileProvenance Provenance);

public sealed record BrowserAnalysisAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserImplementationProfileProvenance(
    string Kind,
    string? PackageId,
    string? PackageVersion,
    string? Framework,
    string? FrameworkVersion,
    string? RuntimeIdentifier,
    string? AssetPath,
    string? ResolverSource,
    string? Project,
    string? ContentRef,
    string? Digest,
    string? DeclaredName);

public sealed record BrowserImplementationProfileContent(
    BrowserImplementationProfilePublicMember[] Members,
    BrowserImplementationProfileMethod[] Methods,
    BrowserImplementationProfile[] Profiles,
    BrowserImplementationProfileCoverage Coverage,
    BrowserImplementationProfileRelationship[] OverloadRelationships,
    string[] GeneratedFrameworkTypes,
    BrowserImplementationProfileAnalysisDiagnostic[] AnalysisDiagnostics,
    BrowserImplementationProfileApiSurfaceFailure[] ApiSurfaceInspectionFailures);

public sealed record BrowserImplementationProfileMethod(
    string Key,
    string AssemblyName,
    string ModuleVersionId,
    string DeclaringType,
    string Name,
    string[] ParameterTypes,
    string ReturnType,
    int MetadataToken,
    bool IsStatic,
    bool IsExtension,
    string CallerUnsafeMode,
    int GenericArity,
    string[] GenericParameterNames,
    string Display);

public sealed record BrowserImplementationProfile(
    string MethodKey,
    string EvidenceMethodKey,
    int ILBytes,
    int InstructionCount,
    int DistinctOpcodeCount,
    int BasicBlockCount,
    int BranchCount,
    int ConditionalBranchCount,
    int SwitchCount,
    int SwitchTargetCount,
    int NormalFlowCyclomaticComplexity,
    int LoopCount,
    int CatchCount,
    int FilterCount,
    int FinallyCount,
    int FaultCount,
    int LocalCount,
    int DirectCallCount,
    int DistinctCalleeCount,
    int AllocationCount,
    int ThrowCount,
    bool Async,
    bool Unsafe,
    int ReflectionCallCount,
    int IncomingOverloadCallerCount,
    int OutgoingOverloadTargetCount,
    bool IsComplete,
    string[] IncompleteReasons,
    BrowserImplementationProfilePublicMember[] PublicMembers);

public sealed record BrowserImplementationProfilePublicMember(
    string TypeDefinitionId,
    string Member,
    string StableSelector,
    int[] BodyTokens);

public sealed record BrowserImplementationProfileCoverage(
    bool WasRequested,
    bool HasFullMethodEvidenceScope,
    string[] DeclaredMethodKeys,
    string[] ManagedMethodBodyKeys,
    string[] ProfiledEvidenceBodyKeys,
    BrowserImplementationProfileUnavailableBody[] UnavailableBodies,
    BrowserImplementationProfileAnalysisDiagnostic[] Diagnostics);

public sealed record BrowserImplementationProfileUnavailableBody(
    string? EvidenceMethodKey,
    int MethodToken,
    string Reason,
    BrowserImplementationProfileAnalysisDiagnostic? Diagnostic);

public sealed record BrowserImplementationProfileRelationship(
    string CallerKey,
    string CalleeKey,
    string EvidenceMethodKey,
    int ILOffset,
    string Kind);

public sealed record BrowserImplementationProfileAnalysisDiagnostic(
    int MethodToken,
    string Method,
    string Message,
    int? SourceMethodToken,
    string? DeclaringType,
    string? SourceDeclaringType);

public sealed record BrowserImplementationProfileApiSurfaceFailure(
    string Operation,
    int SubjectToken,
    string Mechanism,
    string Kind,
    string Detail,
    BrowserAnalysisAssemblyIdentity? SubjectAssembly,
    BrowserAnalysisAssemblyIdentity? DependencyAssembly);

public sealed record BrowserImplementationProfileFailure(
    string Kind,
    string Detail,
    string? MetadataRootReason);

/// <summary>
/// Ecosystem integration evidence for one workspace, carried exactly as
/// <c>AssemblyContextIntegrationsQuery</c> produced it: one group per package/version/framework,
/// one entry per participant, and each participant's own signals grouped by the integration name
/// the scanner assigned. Grouping is presentation; no signal, category, or count is composed here.
/// </summary>
public sealed record BrowserPackageIntegrations(
    string Package,
    string Version,
    string Framework,
    BrowserIntegrationCategory[] Categories,
    int TotalSignals,
    bool IsComplete,
    string? InspectionError,
    BrowserCompileLibraryAvailability CompileLibrary)
{
    public BrowserAnalysisInspectionEnvelope? Inspection { get; init; }
}

public sealed record BrowserIntegrationCategory(
    string Integration,
    BrowserIntegrationSignal[] Signals);

public sealed record BrowserIntegrationSignal(string Kind, string Name, string Shape);

/// <summary>
/// Integration opportunities for one package workspace, composed by
/// <c>AssemblyContextIntegrationOpportunitiesQuery</c> from its typed Integrations prerequisite.
/// The host groups and deduplicates rows for display; it does not infer opportunity evidence.
/// </summary>
public sealed record BrowserPackageOpportunities(
    string Package,
    string Version,
    string ActiveFramework,
    BrowserOpportunityCategory[] Categories,
    int TotalOpportunities,
    bool IsComplete,
    string? InspectionError,
    BrowserCompileLibraryAvailability CompileLibrary)
{
    public BrowserAnalysisInspectionEnvelope? Inspection { get; init; }
}

public sealed record BrowserOpportunityCategory(
    string Integration,
    BrowserOpportunityItem[] Items);

public sealed record BrowserOpportunityItem(
    string Api,
    string IntegrationType,
    string LookFor,
    string? SourceDefinitionId,
    string SourceAssembly,
    string SourceAssemblyVersion,
    string? SourceAssemblyCulture,
    string? SourceAssemblyPublicKeyToken);

public sealed record BrowserPackagePerformance(
    BrowserPerformanceMember[] Members,
    string? InspectionError,
    int NonPublicOpportunities,
    int TotalOpportunities,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserLibraryMetrics(
    string Outcome,
    string? MethodologyVersion,
    BrowserLibraryMetricsPopulation? Population,
    BrowserLibraryMetricsDistribution[] Distributions,
    BrowserLibraryMetricsBooleanDisposition? AsyncStateMachinePresence,
    BrowserLibraryMetricsType[] TypeSummaries,
    BrowserLibraryMetricsRelationship[] EntangledRelationships,
    string[] Diagnostics,
    string? Failure,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserLibraryMetricsPopulation(
    int PhysicalEvidenceBodyCount,
    int ProfiledPhysicalEvidenceBodyCount,
    int LogicalOwnerCount,
    int CompleteProfileCount,
    int IncompleteProfileCount);

public sealed record BrowserLibraryMetricsDistribution(
    string Metric,
    int CompleteBodyCount,
    int? Minimum,
    int? P50,
    int? P90,
    int? P95,
    int? P99,
    int? Maximum);

public sealed record BrowserLibraryMetricsBooleanDisposition(
    string Name,
    int CompleteBodyCount,
    int PresentCount,
    int AbsentCount);

public sealed record BrowserLibraryMetricsType(
    string TypeKey,
    string TypeDisplay,
    string Namespace,
    string Name,
    int BodyCount,
    int InstructionCount,
    int ComplexityTotal,
    int LoopCount,
    int DirectCallCount,
    int AllocationCount);

public sealed record BrowserLibraryMetricsRelationship(
    string SourceTypeKey,
    string SourceTypeDisplay,
    string TargetTypeKey,
    string TargetTypeDisplay,
    int CallSiteCount,
    int SourceDegree,
    int TargetDegree);

public sealed record BrowserLibraryStructuralSalience(
    int SchemaVersion,
    string Outcome,
    string? MethodologyVersion,
    string? EvidenceMode,
    BrowserLibraryNamespaceLeverageIndex? NamespaceIndex,
    BrowserLibraryTypeLeverageShard[] TypeLeverageShards,
    string? Failure,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserLibraryNamespaceLeverageIndex(
    string Disposition,
    BrowserLibrarySignatureUseCoverage Coverage,
    BrowserLibraryNamespaceLeverageRow[] Namespaces,
    string[] Diagnostics);

public sealed record BrowserLibraryNamespaceLeverageRow(
    string Namespace,
    int TypeCount,
    int ExternalIncomingSourceTypeCount,
    bool TopLeverage);

public sealed record BrowserLibraryTypeLeverageShard(
    string Namespace,
    string Disposition,
    BrowserLibrarySignatureUseCoverage Coverage,
    BrowserLibraryTypeLeverageRow[] Types,
    string[] SeaLevelOrder,
    string[] MountainPeakOrder,
    string[] Diagnostics);

public sealed record BrowserLibrarySignatureUseCoverage(
    int Considered,
    int Examined,
    int Unavailable,
    int Limited);

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserLibraryStructuralTypePole>))]
public enum BrowserLibraryStructuralTypePole
{
    SeaLevel,
    MountainPeak,
}

public sealed record BrowserLibraryTypeLeverageRow(
    string TypeDefinitionId,
    string TypeDisplay,
    bool DesignationEligible,
    int SignatureIncomingDegree,
    int SignatureOutgoingDegree,
    string Role,
    BrowserLibraryStructuralTypePole? Pole);

public sealed record BrowserPerformanceMember(
    string Assembly,
    string TypeId,
    string MemberName,
    string StableSelector,
    int[] BodyTokens,
    int OpportunityCount,
    int InLoopCount,
    string[] Shapes,
    string Confidence);

public sealed record BrowserMemberFacts(
    int MetadataToken,
    BrowserMethodSignals Signals,
    BrowserAllocationFact[] Allocations,
    BrowserCallFact[] Calls,
    BrowserSafetyFact[] Safety,
    BrowserExceptionRegion[] ExceptionRegions,
    BrowserPerformanceOpportunity[] PerformanceOpportunities,
    string[] Diagnostics);

public sealed record BrowserMethodSignals(
    int Allocations,
    int Copies,
    bool Unsafe,
    int Reflection,
    int Throws,
    int Catches,
    int Finallys,
    bool AllocatesInLoop,
    string[] EvidenceOffsets,
    string[] ExceptionTypes);

public sealed record BrowserAllocationLifetimeUse(
    int ILOffset,
    string Kind);

public sealed record BrowserAllocationLifetimeLimitation(
    string Kind,
    int? ILOffset,
    string? Operation);

public sealed record BrowserAllocationFact(
    string Kind,
    string? Type,
    string Offset,
    bool CountedAsHeap,
    string Frequency,
    string Multiplicity,
    string Path,
    string Escape,
    bool InLoop,
    int? EstimatedSizeBytes,
    string? Detail,
    BrowserAllocationLifetimeUse[] LifetimeUses,
    BrowserAllocationLifetimeLimitation[] LifetimeLimitations);

public sealed record BrowserCallFact(
    string Callee,
    string Offset,
    string Opcode,
    string Kind,
    string Multiplicity,
    bool InLoop);

public sealed record BrowserSafetyFact(
    string Kind,
    string? Offset,
    string Operation,
    string Requirement,
    string Evidence);

public sealed record BrowserExceptionRegion(
    int Region,
    string Clause,
    string TryRange,
    string HandlerRange,
    string? FilterRange,
    string? CaughtType);

public sealed record BrowserPerformanceOpportunity(
    string Shape,
    string Evidence,
    string Fix,
    string Confidence,
    string? Offset,
    bool InLoop,
    string? Caveat,
    string? Finding,
    string Provenance);

public sealed record BrowserTypeImplementationHeat(
    int SchemaVersion,
    string Outcome,
    BrowserImplementationProfileSubject? Subject,
    BrowserTypeImplementationHeatContent? Content,
    BrowserImplementationProfileFailure? Failure,
    BrowserAnalysisInspectionShare? Share,
    BrowserAnalysisInspectionDiagnostic[] Diagnostics,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserTypeMethodLeverage(
    int SchemaVersion,
    string Outcome,
    BrowserImplementationProfileSubject? Subject,
    BrowserTypeMethodLeverageContent? Content,
    BrowserImplementationProfileFailure? Failure,
    BrowserAnalysisInspectionShare? Share,
    BrowserAnalysisInspectionDiagnostic[] Diagnostics,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserTypeMethodLeverageContent(
    string TypeDefinitionId,
    int MethodCount,
    int WinnerCount,
    BrowserTypeMethodLeverageRank? WinningRank,
    BrowserTypeMethodLeverageWinner[] AnchoredWinners,
    BrowserImplementationProfileAnalysisDiagnostic[] AnalysisDiagnostics,
    BrowserImplementationProfileApiSurfaceFailure[]
        ApiSurfaceInspectionFailures);

public sealed record BrowserTypeMethodLeverageRank(
    int DirectCallerCount,
    int RootReach,
    int Fanout,
    int LoopCallCount,
    int MaxDepth);

public sealed record BrowserTypeMethodLeverageWinner(
    string TypeDefinitionId,
    string StableSelector,
    int[] MethodTokens);

public sealed record BrowserTypeImplementationHeatContent(
    string TypeDefinitionId,
    BrowserImplementationHeatFamily[] Families,
    BrowserImplementationProfileAnalysisDiagnostic[] AnalysisDiagnostics,
    BrowserImplementationProfileApiSurfaceFailure[] ApiSurfaceInspectionFailures);

public sealed record BrowserImplementationHeatFamily(
    string Member,
    BrowserImplementationHeatRosterMember[] Roster,
    BrowserImplementationHeatMethod[] Methods,
    BrowserImplementationHeatRelationship[] Relationships,
    BrowserImplementationProfileUnavailableBody[] UnavailableBodies,
    BrowserImplementationProfileAnalysisDiagnostic[] AnalysisDiagnostics);

public sealed record BrowserImplementationHeatRosterMember(
    string TypeDefinitionId,
    string StableSelector,
    int MetadataToken);

public sealed record BrowserImplementationHeatMethod(
    int MetadataToken,
    bool IsRosterMember,
    bool HasBody,
    int? Size,
    bool IsTrivial,
    bool IsComplete);

public sealed record BrowserImplementationHeatRelationship(
    int CallerToken,
    int CalleeToken);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageIntegrations))]
[JsonSerializable(typeof(BrowserPackageOpportunities))]
[JsonSerializable(typeof(BrowserPackagePerformance))]
[JsonSerializable(typeof(BrowserLibraryMetrics))]
[JsonSerializable(typeof(BrowserLibraryStructuralSalience))]
[JsonSerializable(typeof(BrowserImplementationProfiles))]
[JsonSerializable(typeof(BrowserTypeImplementationHeat))]
[JsonSerializable(typeof(BrowserTypeMethodLeverage))]
[JsonSerializable(typeof(BrowserAnalysisInspectionEnvelope))]
[JsonSerializable(typeof(BrowserMemberFacts))]
[JsonSerializable(typeof(BrowserCloneCandidateRequest))]
[JsonSerializable(typeof(BrowserCloneCandidateResult))]
[JsonSerializable(typeof(BrowserCompareEntryResult))]
internal sealed partial class BrowserAnalysisJsonContext : JsonSerializerContext;
