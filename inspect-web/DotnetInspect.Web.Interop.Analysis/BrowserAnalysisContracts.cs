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
    string? Detail);

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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageIntegrations))]
[JsonSerializable(typeof(BrowserPackageOpportunities))]
[JsonSerializable(typeof(BrowserPackagePerformance))]
[JsonSerializable(typeof(BrowserLibraryMetrics))]
[JsonSerializable(typeof(BrowserAnalysisInspectionEnvelope))]
[JsonSerializable(typeof(BrowserMemberFacts))]
[JsonSerializable(typeof(BrowserCloneCandidateRequest))]
[JsonSerializable(typeof(BrowserCloneCandidateResult))]
[JsonSerializable(typeof(BrowserCompareEntryResult))]
internal sealed partial class BrowserAnalysisJsonContext : JsonSerializerContext;
