using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Models;

internal static class LibraryInspectionDisplay
{
    /// <summary>
    /// Resolves the compact display version using priority:
    /// PlatformVersion, InformationalVersion prefix, AssemblyVersion, FileVersion.
    /// </summary>
    public static string ResolveVersion(LibraryInspection inspection)
    {
        if (!string.IsNullOrEmpty(inspection.PlatformVersion))
            return inspection.PlatformVersion;

        if (inspection.AssemblyInfo is { } info)
        {
            if (!string.IsNullOrEmpty(info.InformationalVersion))
            {
                var ver = info.InformationalVersion;
                var plusIndex = ver.IndexOf('+');
                if (plusIndex > 0)
                    ver = ver[..plusIndex];
                var dashIndex = ver.IndexOf('-');
                var versionPart = dashIndex > 0 ? ver[..dashIndex] : ver;
                if (versionPart.Split('.').All(p => int.TryParse(p, out _)))
                    return dashIndex > 0 ? ver : versionPart;
            }

            if (!string.IsNullOrEmpty(info.AssemblyVersion))
                return info.AssemblyVersion;

            if (!string.IsNullOrEmpty(info.FileVersion))
                return info.FileVersion;
        }

        return "";
    }
}

public class LibraryInspection
{
    [JsonIgnore]
    internal IntegrationQueryOptions IntegrationQuery { get; init; } = IntegrationQueryOptions.Default;

    [JsonIgnore]
    internal IReadOnlyList<AssemblyReferenceIdentity>? AssemblyReferenceIdentities { get; set; }

    [JsonIgnore]
    public AssemblyIntegrationsEntry? AssemblyIntegrationsEntry { get; set; }

    [JsonIgnore]
    public AssemblyIntegrationOpportunitiesEntry?
        AssemblyIntegrationOpportunitiesEntry
    {
        get;
        set
        {
            field = value;
            _inspectionFailuresInitialized = false;
            _inspectionFailures = null;
        }
    }

    [JsonIgnore]
    public string? Tfm { get; set; }

    public string FileName { get; set; } = "";

    public string FileType { get; set; } = "";

    /// <summary>
    /// PDB format: "Portable PDB", "Windows PDB", or null if none.
    /// </summary>
    public string? PdbFormat { get; set; }

    /// <summary>
    /// Where the PDB is located: "Embedded", "Standalone", or null if unknown.
    /// </summary>
    public string? PdbLocation { get; set; }

    public string? PdbPath { get; set; }

    public bool HasEmbeddedPdb { get; set; }

    public bool HasReproducibleFlag { get; set; }

    public bool? HasNormalizedPaths { get; set; }

    public bool HasSourceLink { get; set; }

    /// <summary>
    /// Explanation for why SourceLink is unavailable (e.g., "Distro build (ReadyToRun)").
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLinkUnavailableReason { get; set; }

    public bool IsDeterministic { get; set; }

    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Indicates that a Windows PDB was detected (not supported by this tool).
    /// </summary>
    public bool WindowsPdbDetected { get; set; }

    /// <summary>
    /// The server the PDB was retrieved from (e.g., "nuget.org", "msdl.microsoft.com"), or null if local/embedded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SymbolServer { get; set; }

    /// <summary>
    /// Inferred builder of the assembly based on symbol availability and SourceLink.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Builder { get; set; }

    /// <summary>
    /// Where the assembly was resolved from: "Platform (runtime)", "NuGet", or null for local files.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    /// <summary>
    /// Platform/package version (e.g., "10.0.1"), distinct from the PE assembly version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlatformVersion { get; set; }

    /// <summary>
    /// Whether a platform assembly is a facade-only assembly.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsFacadeAssembly { get; set; }

    /// <summary>
    /// Typed classification evidence for platform assemblies. Presentation
    /// continues to project the compatible nullable facade field.
    /// </summary>
    [JsonIgnore]
    public AssemblySurfaceClassificationOutcome? SurfaceClassification { get; set; }

    /// <summary>
    /// Finding projection of <see cref="SurfaceClassification"/> for composed
    /// inspection and failure reporting.
    /// </summary>
    [JsonIgnore]
    public FindingInspection<AssemblySurfaceClassification>?
        SurfaceClassificationInspection { get; set; }

    /// <summary>
    /// File last modified timestamp.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Publisher identity from NuGet package author signature (CN).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; set; }

    /// <summary>
    /// Whether the package publisher signature was cryptographically verified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PublisherVerified { get; set; }

    /// <summary>
    /// Whether the package repository signature was cryptographically verified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RepositoryVerified { get; set; }

    /// <summary>
    /// Status message when signature verification was skipped or failed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SignatureStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLinkJson { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SourceLinkMapInspection? SourceLinkMap { get; set; }

    public List<string>? NonNormalizedPaths { get; set; }

    /// <summary>
    /// Total number of source documents in the PDB.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int TotalSourceFiles { get; set; }

    /// <summary>
    /// Number of source files accessible via SourceLink (HTTP 200).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AccessibleSourceFiles { get; set; }

    /// <summary>
    /// Number of source files embedded in the PDB.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EmbeddedSourceFiles { get; set; }

    /// <summary>
    /// Source files that are neither accessible via SourceLink nor embedded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? MissingSourceFiles { get; set; }

    /// <summary>
    /// Whether all source files are accessible (via SourceLink or embedded).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllSourcesAccessible { get; set; }

    /// <summary>
    /// Whether a SourceLink: Integrity pass (GET + checksum verification) was run.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SourceIntegrityChecked { get; set; }

    /// <summary>
    /// Number of source documents whose content hash matched the PDB checksum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SourceIntegrityVerified { get; set; }

    /// <summary>
    /// Number of source documents whose content hash did NOT match the PDB checksum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SourceIntegrityMismatched { get; set; }

    /// <summary>
    /// Number of source documents whose content hash matched the PDB checksum after normalizing line endings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SourceIntegrityLineEndingNormalized { get; set; }

    /// <summary>
    /// Number of source documents that could not be fetched or had no usable checksum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SourceIntegrityUnverifiable { get; set; }

    /// <summary>
    /// File paths whose content hash did not match the recorded PDB checksum.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? SourceIntegrityMismatches { get; set; }

    private SourceAvailabilityResult? _sourceAvailabilityQueryResult;

    [JsonIgnore]
    public SourceAvailabilityResult? SourceAvailabilityQueryResult
    {
        get => _sourceAvailabilityQueryResult;
        set
        {
            _sourceAvailabilityQueryResult = value;
            ResetFindingProjectionCaches();
        }
    }

    private SourceIntegrityResult? _sourceIntegrityQueryResult;

    [JsonIgnore]
    public SourceIntegrityResult? SourceIntegrityQueryResult
    {
        get => _sourceIntegrityQueryResult;
        set
        {
            _sourceIntegrityQueryResult = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssemblyInfo? AssemblyInfo { get; set; }

    [JsonIgnore]
    internal List<AssemblyReferenceNode>? IdentifierConfusionReferenceClosure
    {
        get;
        set;
    }

    [JsonIgnore]
    internal IdentifierConfusionAuditFailureKind? IdentifierConfusionFailure
    {
        get;
        set;
    }

    [JsonIgnore]
    internal IdentifierConfusionAuditFailureKind? AssemblyReferenceFailureKind
    {
        get;
        set;
    }

    private FindingInspection<AssemblyReference>? _assemblyReferenceInspection;

    [JsonIgnore]
    public FindingInspection<AssemblyReference>? AssemblyReferenceInspection
    {
        get => _assemblyReferenceInspection;
        set
        {
            _assemblyReferenceInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    private FindingInspection<SourceDocumentObservation>? _sourceDocumentInspection;
    private FindingInspection<CompilationOptionInfo>? _compilationOptionInspection;
    private FindingInspection<CompilationReferenceInfo>? _compilationReferenceInspection;

    [JsonIgnore]
    public FindingInspection<SourceDocumentObservation>? SourceDocumentInspection
    {
        get => _sourceDocumentInspection;
        set
        {
            _sourceDocumentInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore]
    public FindingInspection<CompilationOptionInfo>? CompilationOptionInspection
    {
        get => _compilationOptionInspection;
        set
        {
            _compilationOptionInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore]
    public FindingInspection<CompilationReferenceInfo>? CompilationReferenceInspection
    {
        get => _compilationReferenceInspection;
        set
        {
            _compilationReferenceInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiSurface? ApiSurface { get; set; }

    private FindingInspection<ExtensionMemberObservation>? _extensionMemberInspection;
    private IReadOnlyList<ExtensionMethodInfo>? _extensionMemberDisplayOrder;
    private bool _extensionMethodsInitialized;
    private List<LibraryExtensionMethodJson>? _extensionMethods;

    [JsonIgnore]
    public FindingInspection<ExtensionMemberObservation>? ExtensionMemberInspection =>
        _extensionMemberInspection;

    internal void SetExtensionMemberInspection(
        FindingInspection<ExtensionMemberObservation> inspection,
        IReadOnlyList<ExtensionMethodInfo>? displayOrder)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        _extensionMemberInspection = inspection;
        _extensionMemberDisplayOrder = displayOrder?.ToArray();
        ResetFindingProjectionCaches();
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryExtensionMethodJson>? ExtensionMethods =>
        GetOrCreate(
            ref _extensionMethodsInitialized,
            ref _extensionMethods,
            ProjectExtensionMethods);

    private List<ClassifiedMethodSummary>? _unsafeMethods;

    /// <summary>
    /// Presentation rows for public methods with unsafe (pointer) signatures.
    /// Classification semantics come from <see cref="ClassifiedMethodInspection"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClassifiedMethodSummary>? UnsafeMethods
    {
        get => ClassifiedMethodInspection.Failure() is null ? _unsafeMethods : null;
        set => _unsafeMethods = value;
    }

    /// <summary>
    /// Members with unsafe signature or body-level unsafe evidence.
    /// P/Invoke-only methods are excluded and remain in P/Invoke Methods.
    /// </summary>
    private List<UnsafeMemberSummary>? _unsafeMembers;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UnsafeMemberSummary>? UnsafeMembers
    {
        get => UnsafeEvidenceInspection.Failure() is null ? _unsafeMembers : null;
        set => _unsafeMembers = value;
    }

    private FindingInspection<UnsafeEvidence>? _unsafeEvidenceInspection;

    /// <summary>Typed unsafe declaration and body evidence with path-scoped provenance.</summary>
    [JsonIgnore]
    public FindingInspection<UnsafeEvidence>? UnsafeEvidenceInspection
    {
        get => _unsafeEvidenceInspection;
        set
        {
            _unsafeEvidenceInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    /// <summary>
    /// Result of the bounded discovery probe when the complete unsafe-evidence census was not
    /// requested.
    /// </summary>
    [JsonIgnore]
    public bool? UnsafeEvidencePresent { get; set; }

    /// <summary>Failure from the bounded discovery probe, kept separate from the full census.</summary>
    [JsonIgnore]
    public Exception? UnsafeEvidencePresenceError { get; set; }

    /// <summary>Per-method failures that made the unsafe-evidence census incomplete.</summary>
    [JsonIgnore]
    public ImmutableArray<AnalysisDiagnostic> UnsafeEvidenceDiagnostics { get; set; } = [];

    private TopLeverageResult? _topLeverageQueryResult;

    /// <summary>Typed whole-assembly call-graph leverage evidence.</summary>
    [JsonIgnore]
    public TopLeverageResult? TopLeverageQueryResult
    {
        get => _topLeverageQueryResult;
        set
        {
            _topLeverageQueryResult = value;
            ResetFindingProjectionCaches();
        }
    }

    /// <summary>CLI-owned member coordinates joined to typed leverage evidence.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>?
        TopLeverageDrillMap { get; set; }

    /// <summary>
    /// Compatibility projection of methods ranked by call-graph leverage. Assembly-wide;
    /// populated only when the Top Leverage section is selected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MethodLeverageSummary>? TopLeverage { get; set; }

    /// <summary>
    /// Safe, local optimization opportunities inferred from IL/body evidence. Internal backing
    /// for the kind-scoped performance sections and the nested <see cref="Performance"/> JSON
    /// projection; not serialized directly (see <see cref="Performance"/>).
    /// </summary>
    [JsonIgnore]
    public List<OptimizationOpportunitySummary>? OptimizationOpportunities { get; set; }

    private OptimizationOpportunitiesResult?
        _optimizationOpportunitiesQueryResult;

    /// <summary>Typed whole-assembly optimization evidence.</summary>
    [JsonIgnore]
    public OptimizationOpportunitiesResult?
        OptimizationOpportunitiesQueryResult
    {
        get => _optimizationOpportunitiesQueryResult;
        set
        {
            _optimizationOpportunitiesQueryResult = value;
            ResetFindingProjectionCaches();
        }
    }

    /// <summary>
    /// CLI-filtered and ranked typed opportunities retained through presentation.
    /// </summary>
    [JsonIgnore]
    public ImmutableArray<OptimizationOpportunity>
        PerformanceTriageOpportunities { get; set; } = [];

    /// <summary>
    /// Nested performance projection: the optimization opportunities bucketed by kind, mirroring
    /// the kind-scoped sections and the il-offset nested model. Null (absent) when the scan did
    /// not run; each kind array is absent when it has no findings.
    /// </summary>
    [JsonPropertyName("performance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PerformanceProjection? Performance =>
        PerformanceProjection.FromOpportunities(OptimizationOpportunities);

    private ResourceTriageResult? _resourceTriageQueryResult;

    /// <summary>Typed whole-assembly resource lifecycle evidence and assessments.</summary>
    [JsonIgnore]
    public ResourceTriageResult? ResourceTriageQueryResult
    {
        get => _resourceTriageQueryResult;
        set
        {
            _resourceTriageQueryResult = value;
            ResetFindingProjectionCaches();
        }
    }

    private FindingInspection<ResourceLifecycleOccurrence>?
        _resourceLifecycleInspection;

    [JsonIgnore]
    public FindingInspection<ResourceLifecycleOccurrence>?
        ResourceLifecycleInspection
    {
        get => _resourceLifecycleInspection;
        set
        {
            _resourceLifecycleInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ResourceTriageSummary>? ResourceTriage { get; set; }

    /// <summary>CLI-filtered typed assessments retained through presentation.</summary>
    [JsonIgnore]
    public ImmutableArray<ResourceTriageAssessment>
        ResourceTriageAssessments { get; set; } = [];

    /// <summary>CLI-owned member coordinates joined to typed resource triage evidence.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<int, (string? Stable, string Visibility, string Selector)>?
        ResourceTriageDrillMap { get; set; }

    [JsonIgnore]
    public PerformanceTriageOptions PerformanceTriageOptions { get; set; } = PerformanceTriageOptions.Default;

    /// <summary>
    /// Typed result of the selected Body Shapes query.
    /// </summary>
    private BodyShapesResult? _bodyShapesQueryResult;

    [JsonIgnore]
    public BodyShapesResult? BodyShapesQueryResult
    {
        get => _bodyShapesQueryResult;
        set
        {
            _bodyShapesQueryResult = value;
            ResetFindingProjectionCaches();
        }
    }

    /// <summary>
    /// Compatibility projection of exact rendered C# syntax matches.
    /// </summary>
    [JsonIgnore]
    public BodyShapeSearchResult? BodyShapeSearchResult { get; set; }

    [JsonIgnore]
    public BodyShapeSearchResult? EffectiveBodyShapeSearchResult =>
        BodyShapesQueryResult switch
        {
            BodyShapesResult.Available available => available.Search,
            null => BodyShapeSearchResult,
            _ => null,
        };

    [JsonPropertyName("body_shapes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BodyShapeJsonMatch>? BodyShapes =>
        EffectiveBodyShapeSearchResult?.Matches?
            .Select(BodyShapeJsonMatch.FromMatch)
            .ToList();

    [JsonIgnore]
    public BodyKindQueryOptions BodyKindQueryOptions { get; set; } = BodyKindQueryOptions.Default;

    private List<ClassifiedMethodSummary>? _pInvokeMethods;

    /// <summary>
    /// Presentation rows for public P/Invoke (DllImport/LibraryImport) methods.
    /// Classification semantics come from <see cref="ClassifiedMethodInspection"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClassifiedMethodSummary>? PInvokeMethods
    {
        get => ClassifiedMethodInspection.Failure() is null ? _pInvokeMethods : null;
        set => _pInvokeMethods = value;
    }

    private List<AsyncMethodSummary>? _asyncMethods;

    /// <summary>
    /// Presentation rows for public runtime or classic state-machine async methods.
    /// Classification semantics come from <see cref="ClassifiedMethodInspection"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AsyncMethodSummary>? AsyncMethods
    {
        get => ClassifiedMethodInspection.Failure() is null ? _asyncMethods : null;
        set => _asyncMethods = value;
    }

    private FindingInspection<ClassifiedMethodObservation>? _classifiedMethodInspection;

    [JsonIgnore]
    public FindingInspection<ClassifiedMethodObservation>? ClassifiedMethodInspection
    {
        get => _classifiedMethodInspection;
        set
        {
            _classifiedMethodInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore]
    public int UnsafeMethodCount =>
        CountClassifiedMethods(MethodClassification.Unsafe, _unsafeMethods?.Count ?? 0);

    [JsonIgnore]
    public int PInvokeMethodCount =>
        CountClassifiedMethods(MethodClassification.PInvoke, _pInvokeMethods?.Count ?? 0);

    [JsonIgnore]
    public int AsyncMethodCount
    {
        get
        {
            if (ClassifiedMethodInspection is null)
                return _asyncMethods?.Count ?? 0;

            return ClassifiedMethodInspection.PayloadsForRendering().Count(
                static method => method.Classification is MethodClassification.RuntimeAsync
                    or MethodClassification.StateMachineAsync);
        }
    }

    private FindingInspection<EcosystemIntegrationSignalInfo>? _ecosystemIntegrationInspection;
    private FindingInspection<OpenTelemetrySignalInfo>? _openTelemetryInspection;
    private FindingInspection<ManifestResourceInfo>? _resourceInspection;
    private FindingInspection<AssemblyAttributeInfo>? _assemblyAttributeInspection;
    private IReadOnlyList<AssemblyAttributeInfo>? _assemblyAttributeJsonOrder;
    private FindingInspection<TypeForwarderInfo>? _typeForwarderInspection;
    private FindingInspection<UnionTypeInfo>? _unionTypeInspection;
    private FindingInspection<SwitchInfo>? _switchInspection;

    [JsonIgnore]
    public FindingInspection<EcosystemIntegrationSignalInfo>? EcosystemIntegrationInspection
    {
        get => _ecosystemIntegrationInspection;
        set
        {
            _ecosystemIntegrationInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore]
    public FindingInspection<OpenTelemetrySignalInfo>? OpenTelemetryInspection
    {
        get => _openTelemetryInspection;
        set
        {
            _openTelemetryInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore]
    public FindingInspection<ManifestResourceInfo>? ResourceInspection
    {
        get => _resourceInspection;
        set
        {
            _resourceInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    /// <summary>
    /// Typed result of the metadata-image query backing the <c>@Metadata</c> lens.
    ///
    /// This is deliberately the *cheap* half of the lens. It is what the per-table sections'
    /// <c>CanRender</c> consults, so a table with no rows never renders an empty section, and it
    /// backs the <c>Metadata: Image</c> section. The expensive half — actually projecting rows —
    /// happens at render time for the selected tables only, so selecting one table never pays to
    /// project the other sixteen.
    ///
    /// Null means the query did not run. <see cref="MetadataImageResult.NoMetadata"/> and
    /// <see cref="MetadataImageResult.Failed"/> remain distinct so absence and acquisition failure
    /// cannot collapse into the same empty rendering.
    /// </summary>
    [JsonIgnore]
    public MetadataImageResult? MetadataImageResult
    {
        get;
        set
        {
            field = value;
            _inspectionFailuresInitialized = false;
            _inspectionFailures = null;
        }
    }

    /// <summary>The available metadata overview, or null when the query did not produce one.</summary>
    [JsonIgnore]
    public MetadataImageOverview? MetadataOverview =>
        MetadataImageResult is MetadataImageResult.Available available
            ? available.Overview
            : null;

    /// <summary>
    /// The path the metadata lens re-opens to project rows at render time. Captured from the
    /// query adapter rather than recovered from <see cref="FileName"/>, which is a display name and
    /// not always a resolvable path (extracted package assemblies resolve elsewhere).
    /// </summary>
    [JsonIgnore]
    public string? MetadataAssemblyPath { get; set; }

    /// <summary>
    /// The heap value <c>--heap</c> named, or null when no heap coordinate was given.
    ///
    /// This is the carrier that makes the coordinate-scoped heap section exist: like
    /// <see cref="ILOffset"/>, the section is applicable exactly when this is non-null, so a
    /// section that has nothing to show is never listed or rendered.
    /// </summary>
    [JsonIgnore]
    public MetadataHeapLookup? MetadataHeap { get; set; }

    [JsonIgnore]
    public FindingInspection<AssemblyAttributeInfo>? AssemblyAttributeInspection =>
        _assemblyAttributeInspection;

    internal void SetAssemblyAttributeInspection(
        FindingInspection<AssemblyAttributeInfo> inspection,
        IReadOnlyList<AssemblyAttributeInfo>? jsonOrder)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        _assemblyAttributeInspection = inspection;
        _assemblyAttributeJsonOrder = jsonOrder?.ToArray();
        ResetFindingProjectionCaches();
    }

    [JsonIgnore]
    public FindingInspection<TypeForwarderInfo>? TypeForwarderInspection
    {
        get => _typeForwarderInspection;
        set
        {
            _typeForwarderInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore]
    public FindingInspection<UnionTypeInfo>? UnionTypeInspection
    {
        get => _unionTypeInspection;
        set
        {
            _unionTypeInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    [JsonIgnore]
    public FindingInspection<SwitchInfo>? SwitchInspection
    {
        get => _switchInspection;
        set
        {
            _switchInspection = value;
            ResetFindingProjectionCaches();
        }
    }

    private bool _inspectionFailuresInitialized;
    private List<LibraryInspectionFailureJson>? _inspectionFailures;
    private bool _integrationsInitialized;
    private List<LibraryIntegrationSummaryJson>? _integrations;
    private Dictionary<LibraryIntegrationDescriptor, List<LibraryIntegrationSignalJson>?>? _integrationSignals;
    private bool _resourcesInitialized;
    private List<LibraryResourceJson>? _resources;
    private bool _customAttributesInitialized;
    private List<LibraryCustomAttributeJson>? _customAttributes;
    private bool _typeForwardersInitialized;
    private List<TypeForwarderInfo>? _typeForwarders;
    private bool _unionTypesInitialized;
    private List<UnionTypeInfo>? _unionTypes;
    private bool _switchesInitialized;
    private List<SwitchInfo>? _switches;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryInspectionFailureJson>? InspectionFailures =>
        GetOrCreate(
            ref _inspectionFailuresInitialized,
            ref _inspectionFailures,
            () =>
            {
            List<LibraryInspectionFailureJson> failures = [];
            AddFailure(failures, "References", AssemblyReferenceInspection);
            AddFailure(failures, "Source Documents", SourceDocumentInspection);
            AddFailure(failures, "Compilation Options", CompilationOptionInspection);
            AddFailure(failures, "Compilation References", CompilationReferenceInspection);
            AddFailure(failures, "Classified Methods", ClassifiedMethodInspection);
            AddFailure(failures, SectionNames.UnsafeMembers, UnsafeEvidenceInspection);
            if (TopLeverageQueryResult is TopLeverageResult.Failed leverageFailure)
            {
                failures.Add(new LibraryInspectionFailureJson(
                    SectionNames.TopLeverage,
                    TopLeverageQuery.Definition.Name,
                    leverageFailure.Error.Message));
            }
            if (OptimizationOpportunitiesQueryResult
                is OptimizationOpportunitiesResult.Failed optimizationFailure)
            {
                failures.Add(new LibraryInspectionFailureJson(
                    SectionNames.PerformanceTriage,
                    OptimizationOpportunitiesQuery.Definition.Name,
                    optimizationFailure.Error.Message));
                if (BodyKindQueryOptions.HasFilter
                    && PerformanceTriageOptions.HasCandidateFilters)
                {
                    failures.Add(new LibraryInspectionFailureJson(
                        SectionNames.BodyShapes,
                        OptimizationOpportunitiesQuery.Definition.Name,
                        optimizationFailure.Error.Message));
                }
            }
            if (BodyShapesQueryResult is BodyShapesResult.Failed bodyShapesFailure)
            {
                failures.Add(new LibraryInspectionFailureJson(
                    SectionNames.BodyShapes,
                    BodyShapesQuery.Definition.Name,
                    bodyShapesFailure.Error.Message));
            }
            AddFailure(failures, "Extension Methods", ExtensionMemberInspection);
            AddFailure(failures, LibraryIntegrationCatalog.RollupName, EcosystemIntegrationInspection);
            AddFailure(failures, EcosystemIntegrationNames.OpenTelemetry, OpenTelemetryInspection);
            AddFailure(failures, "Resources", ResourceInspection);
            AddFailure(failures, "Custom Attributes", AssemblyAttributeInspection);
            AddFailure(failures, "Type Forwarders", TypeForwarderInspection);
            AddFailure(failures, "Union Types", UnionTypeInspection);
            AddFailure(failures, "Switches", SwitchInspection);
            switch (AssemblyIntegrationOpportunitiesEntry)
            {
                case AssemblyIntegrationOpportunitiesEntry.Rejected rejected:
                    failures.Add(new LibraryInspectionFailureJson(
                        IntegrationSectionNames.Opportunities,
                        AssemblyContextIntegrationOpportunitiesQuery
                            .Definition.Name,
                        $"{rejected.Failure.Kind}: {rejected.Failure.Detail}"));
                    break;

                case AssemblyIntegrationOpportunitiesEntry.Failed failed:
                    failures.Add(new LibraryInspectionFailureJson(
                        IntegrationSectionNames.Opportunities,
                        AssemblyContextIntegrationOpportunitiesQuery
                            .Definition.Name,
                        failed.Error.Message));
                    break;
            }
            if (MetadataImageResult is MetadataImageResult.Failed metadataFailure)
            {
                failures.Add(new LibraryInspectionFailureJson(
                    MetadataSectionNames.Image,
                    MetadataImageQuery.Definition.Name,
                    metadataFailure.Error.Message));
            }
            if (SourceAvailabilityQueryResult is SourceAvailabilityResult.Failed availabilityFailure)
            {
                failures.Add(new LibraryInspectionFailureJson(
                    DotnetInspector.Sections.SectionNames.SourceLinkAvailability,
                    SourceAvailabilityQuery.Definition.Name,
                    availabilityFailure.Reason));
            }
            if (SourceIntegrityQueryResult is SourceIntegrityResult.Failed integrityFailure)
            {
                failures.Add(new LibraryInspectionFailureJson(
                    DotnetInspector.Sections.SectionNames.SourceLinkIntegrity,
                    SourceIntegrityQuery.Definition.Name,
                    integrityFailure.Reason));
            }
            AddFailure(
                failures,
                DotnetInspector.Sections.SectionNames.ArrayPoolEscapes,
                ResourceLifecycleInspection);
            return NullIfEmpty(failures);
            });

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSummaryJson>? Integrations =>
        GetOrCreate(
            ref _integrationsInitialized,
            ref _integrations,
            () => NullIfEmpty(
                LibraryIntegrationCatalog.All
                    .Select(descriptor =>
                    {
                        var signals = descriptor.GetSignals(this);
                        return new LibraryIntegrationSummaryJson(
                            descriptor.Name,
                            descriptor.CountRenderedRows(signals));
                    })
                    .Where(summary => summary.Count > 0)
                    .ToList()));

    /// <summary>
    /// Potential integration categories suggested by package-owned API shapes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<IntegrationOpportunityInfo>? IntegrationOpportunities { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? AspNetCore => IntegrationSignals(LibraryIntegrationCatalog.AspNetCore);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? Aspire => IntegrationSignals(LibraryIntegrationCatalog.Aspire);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? AI => IntegrationSignals(LibraryIntegrationCatalog.AI);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? Authentication => IntegrationSignals(LibraryIntegrationCatalog.Authentication);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? Configuration => IntegrationSignals(LibraryIntegrationCatalog.Configuration);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? DependencyInjection => IntegrationSignals(LibraryIntegrationCatalog.DependencyInjection);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? Logging => IntegrationSignals(LibraryIntegrationCatalog.Logging);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? OpenTelemetry => IntegrationSignals(LibraryIntegrationCatalog.OpenTelemetry);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? Options => IntegrationSignals(LibraryIntegrationCatalog.Options);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? Hosting => IntegrationSignals(LibraryIntegrationCatalog.Hosting);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? HealthChecks => IntegrationSignals(LibraryIntegrationCatalog.HealthChecks);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? HttpClient => IntegrationSignals(LibraryIntegrationCatalog.HttpClient);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryIntegrationSignalJson>? OpenApi => IntegrationSignals(LibraryIntegrationCatalog.OpenAPI);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryResourceJson>? Resources =>
        GetOrCreate(
            ref _resourcesInitialized,
            ref _resources,
            () => NullIfEmpty(
                ResourceInspection.PayloadsForRendering()
                    .OrderBy(resource => resource.Name)
                    .Select(resource => new LibraryResourceJson(
                        resource.Name,
                        resource.IsPublic ? "public" : "private",
                        resource.Size))
                    .ToList()));

    /// <summary>
    /// File size of the assembly in bytes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long FileSize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LibraryCustomAttributeJson>? CustomAttributes =>
        GetOrCreate(
            ref _customAttributesInitialized,
            ref _customAttributes,
            () => NullIfEmpty(
                (_assemblyAttributeJsonOrder
                    ?? AssemblyAttributeInspection.PayloadsForRendering())
                    .Select(attribute => new LibraryCustomAttributeJson
                    {
                        Name = attribute.Name,
                        Target = attribute.Target,
                        Value = attribute.Value,
                    })
                    .ToList()));

    /// <summary>
    /// Metadata audit signals. These are observations, not a trust verdict.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AuditSignal>? AuditSignals { get; set; }

    /// <summary>
    /// Assembly-derived audit metadata retained from the typed query result.
    ///
    /// Audit signals are recomputed after the source-audit and integrity passes fold in evidence
    /// those passes produce. Recomputing them used to reopen the assembly each time — up to four
    /// opens per run, and a window in which a retargeted path could mix two assemblies into one
    /// Signals section. Only the model-derived half actually changes, so the assembly-derived half
    /// is captured once here and reused. Never serialized; it is an intermediate, not output.
    /// </summary>
    [JsonIgnore]
    public AssemblyAuditMetadata? AuditMetadata { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TypeForwarderInfo>? TypeForwarders =>
        GetOrCreate(
            ref _typeForwardersInitialized,
            ref _typeForwarders,
            () => NullIfEmpty(TypeForwarderInspection.PayloadsForRendering().ToList()));

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UnionTypeInfo>? UnionTypes =>
        GetOrCreate(
            ref _unionTypesInitialized,
            ref _unionTypes,
            () => NullIfEmpty(UnionTypeInspection.PayloadsForRendering().ToList()));

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SwitchInfo>? Switches =>
        GetOrCreate(
            ref _switchesInitialized,
            ref _switches,
            () => NullIfEmpty(SwitchInspection.PayloadsForRendering().ToList()));

    /// <summary>
    /// SourceLink URL rows for public types in this assembly. Populated only
    /// when the Source Files section is selected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SourceFileInfo>? SourceFiles { get; set; }

    /// <summary>
    /// MethodDef token + IL offset source resolution result. Populated only when
    /// an IL coordinate section is selected with a token+offset parameter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ILOffsetProjection? ILOffset { get; set; }

    // Presence flags — populated cheaply from MetadataReader before queries run.
    // Used by CanRender for fast -s discovery without full production.

    /// <summary>Whether the assembly contains any static classes with [Extension] attribute.</summary>
    [JsonIgnore]
    public bool HasExtensionTypes { get; set; }

    /// <summary>Whether the assembly contains any methods with PInvokeImpl flag.</summary>
    [JsonIgnore]
    public bool HasPInvokeImports { get; set; }

    /// <summary>Whether the assembly contains any methods with unsafe (pointer) signatures.</summary>
    [JsonIgnore]
    public bool HasUnsafeCode { get; set; }

    /// <summary>Set when guarded decoding prevents unsafe-code presence from being determined.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SignatureDecodeStatus? UnsafeSignatureDecodeStatus { get; set; }

    /// <summary>True when the assembly has at least one method with an IL body (not a pure ref/abstract assembly).</summary>
    [JsonIgnore]
    public bool HasMethodBodies { get; set; }

    /// <summary>Whether the assembly contains any public runtime-async methods (impl flag 0x2000).</summary>
    [JsonIgnore]
    public bool HasRuntimeAsync { get; set; }

    /// <summary>Whether the assembly contains any public classic state-machine async methods.</summary>
    [JsonIgnore]
    public bool HasStateMachineAsync { get; set; }

    /// <summary>Whether the assembly has manifest resources.</summary>
    [JsonIgnore]
    public bool HasManifestResources { get; set; }

    /// <summary>Whether the assembly contains ASP.NET Core middleware/endpoint primitives.</summary>
    [JsonIgnore]
    public bool HasAspNetCoreSupport { get; set; }

    /// <summary>Whether the assembly contains Aspire resource primitives.</summary>
    [JsonIgnore]
    public bool HasAspireSupport { get; set; }

    /// <summary>Whether the assembly references OpenTelemetry or .NET diagnostics telemetry primitives.</summary>
    [JsonIgnore]
    public bool HasOpenTelemetrySupport { get; set; }

    /// <summary>Whether the assembly references Microsoft.Extensions.AI primitives.</summary>
    [JsonIgnore]
    public bool HasAISupport { get; set; }

    /// <summary>Whether the assembly contains authentication/authorization primitives.</summary>
    [JsonIgnore]
    public bool HasAuthenticationSupport { get; set; }

    /// <summary>Whether the assembly contains configuration primitives.</summary>
    [JsonIgnore]
    public bool HasConfigurationSupport { get; set; }

    /// <summary>Whether the assembly references dependency injection primitives.</summary>
    [JsonIgnore]
    public bool HasDependencyInjectionSupport { get; set; }

    /// <summary>Whether the assembly references logging primitives.</summary>
    [JsonIgnore]
    public bool HasLoggingSupport { get; set; }

    /// <summary>Whether the assembly references options-pattern primitives.</summary>
    [JsonIgnore]
    public bool HasOptionsSupport { get; set; }

    /// <summary>Whether the assembly references hosting primitives.</summary>
    [JsonIgnore]
    public bool HasHostingSupport { get; set; }

    /// <summary>Whether the assembly references health-check primitives.</summary>
    [JsonIgnore]
    public bool HasHealthChecksSupport { get; set; }

    /// <summary>Whether the assembly references HttpClientFactory primitives.</summary>
    [JsonIgnore]
    public bool HasHttpClientSupport { get; set; }

    /// <summary>Whether the assembly contains OpenAPI/Swagger primitives.</summary>
    [JsonIgnore]
    public bool HasOpenApiSupport { get; set; }

    private List<LibraryIntegrationSignalJson>? IntegrationSignals(
        LibraryIntegrationDescriptor descriptor)
    {
        _integrationSignals ??= [];
        if (_integrationSignals.TryGetValue(descriptor, out var cached))
            return cached;

        var signals = NullIfEmpty(
            descriptor.GetSignals(this)
                .Select(signal => new LibraryIntegrationSignalJson(
                    signal.Kind,
                    signal.Name,
                    signal.Shape))
                .ToList());
        _integrationSignals.Add(descriptor, signals);
        return signals;
    }

    private static List<T>? NullIfEmpty<T>(List<T> values)
        => values.Count > 0 ? values : null;

    private static List<T>? GetOrCreate<T>(
        ref bool initialized,
        ref List<T>? value,
        Func<List<T>?> factory)
    {
        if (!initialized)
        {
            value = factory();
            initialized = true;
        }

        return value;
    }

    private void ResetFindingProjectionCaches()
    {
        _extensionMethodsInitialized = false;
        _extensionMethods = null;
        _inspectionFailuresInitialized = false;
        _inspectionFailures = null;
        _integrationsInitialized = false;
        _integrations = null;
        _integrationSignals = null;
        _resourcesInitialized = false;
        _resources = null;
        _customAttributesInitialized = false;
        _customAttributes = null;
        _typeForwardersInitialized = false;
        _typeForwarders = null;
        _unionTypesInitialized = false;
        _unionTypes = null;
        _switchesInitialized = false;
        _switches = null;
    }

    private int CountClassifiedMethods(MethodClassification classification, int fallback)
        => ClassifiedMethodInspection is null
            ? fallback
            : ClassifiedMethodInspection.PayloadsForRendering().Count(
                method => method.Classification == classification);

    private List<LibraryExtensionMethodJson>? ProjectExtensionMethods()
    {
        if (_extensionMemberDisplayOrder is null)
            return null;

        var anchors = ExtensionMemberInspection.PayloadsForRendering()
            .Select(static observation => observation.Anchor)
            .ToHashSet();
        var rows = _extensionMemberDisplayOrder
            .Where(member => member.Anchor is { } anchor && anchors.Contains(anchor))
            .GroupBy(member => (
                member.MethodName,
                member.Kind,
                member.ExtensionClass,
                member.ExtendedType))
            .Select(group =>
            {
                int count = group.Count();
                return new LibraryExtensionMethodJson(
                    group.Key.MethodName,
                    group.Key.ExtendedType,
                    group.Key.ExtensionClass,
                    group.Key.Kind,
                    count > 1 ? count : null);
            })
            .OrderBy(member => member.ExtendedType)
            .ThenBy(member => member.MethodName)
            .ToList();
        return NullIfEmpty(rows);
    }

    private static void AddFailure<T>(
        List<LibraryInspectionFailureJson> failures,
        string section,
        FindingInspection<T>? inspection)
        where T : notnull
    {
        if (inspection.Failure() is { } failure)
        {
            failures.Add(new LibraryInspectionFailureJson(
                section,
                failure.Descriptor.Title,
                failure.Reason));
        }
    }

    /// <summary>Number of integration categories discovered by the presence scan.</summary>
    [JsonIgnore]
    public int IntegrationCount { get; set; }

    /// <summary>Whether the assembly has non-well-known custom attributes.</summary>
    [JsonIgnore]
    public bool HasAssemblyAttributes { get; set; }

    /// <summary>Whether the assembly has type forwarders.</summary>
    [JsonIgnore]
    public bool HasExportedTypeForwarders { get; set; }

    /// <summary>Whether the assembly has types annotated with UnionAttribute.</summary>
    [JsonIgnore]
    public bool HasUnionTypes { get; set; }

    /// <summary>Whether the assembly has feature, compatibility, or runtime switches.</summary>
    [JsonIgnore]
    public bool HasSwitches { get; set; }

    /// <summary>Number of feature, compatibility, or runtime switches discovered by the presence scan.</summary>
    [JsonIgnore]
    public int SwitchCount { get; set; }

}

public sealed record SourceFileInfo(string Type, string? Url);

/// <summary>
/// JSON projection of extension members defined in a library.
/// </summary>
public sealed record LibraryExtensionMethodJson(
    string MethodName,
    string ExtendedType,
    string ExtensionClass,
    string Kind,
    int? Overloads);

/// <summary>
/// Summary of a classified method (unsafe or P/Invoke).
/// </summary>
public record class ClassifiedMethodSummary
{
    public string MethodName { get; init; } = "";
    public string DeclaringType { get; init; } = "";
    public string Signature { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModuleName { get; init; }
}

/// <summary>
/// Summary of a member with unsafe signature or body evidence.
/// </summary>
public record class UnsafeMemberSummary
{
    public string Member { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Kind { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IL { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; init; }
}

/// <summary>
/// A method ranked by call-graph leverage: distinct direct callers (fanin), outbound
/// call sites (fanout), longest intra-assembly call chain (depth), and in-loop call sites.
/// </summary>
public record class MethodLeverageSummary
{
    public string Member { get; init; } = "";
    public int Callers { get; init; }
    public int RootReach { get; init; }
    public int Fanout { get; init; }
    public int Depth { get; init; }
    public int LoopCalls { get; init; }
    public bool Generated { get; init; }
    public string? Visibility { get; init; }
    public string? Stable { get; init; }
    public string? Selector { get; init; }
}

/// <summary>
/// Summary of a safe, local optimization opportunity inferred from IL evidence.
/// </summary>
public record class OptimizationOpportunitySummary
{
    public string Member { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Assembly { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ModuleVersionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MethodToken { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Candidate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Finding { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Provenance { get; init; }
    public int RootReach { get; init; }
    public string Shape { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EvidenceMethod { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupportingFinding { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupportingOperation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupportingToken { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupportingEvidenceMethod { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SupportingIL { get; init; }
    public string Evidence { get; init; } = "";
    public string Fix { get; init; } = "";
    public string Priority { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string Loop { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallerLoop { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CallerLoopDepth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallerLoopWitness { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Allocation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PathConfidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostDominance { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IL { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Weight { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DirectSites { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OncePaths { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ConditionalPaths { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RepeatedPaths { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? UnknownPaths { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CachedSites { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OpaquePaths { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Saturated { get; init; }
}

/// <summary>
/// Nested performance projection: the optimization opportunities bucketed by kind, one array per
/// kind-scoped section. Mirrors the il-offset nested model — each kind is absent (null) when it
/// has no findings, so a consumer selecting the <c>@Performance</c> group receives exactly the
/// kinds that were found.
/// </summary>
public sealed class PerformanceProjection
{
    [JsonPropertyName("boxing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? Boxing { get; set; }

    [JsonPropertyName("arrays")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? Arrays { get; set; }

    [JsonPropertyName("closures_and_delegates")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? ClosuresAndDelegates { get; set; }

    [JsonPropertyName("enumerators")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? Enumerators { get; set; }

    [JsonPropertyName("loop_hot_paths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? LoopHotPaths { get; set; }

    [JsonPropertyName("allocation_hotspots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? AllocationHotspots { get; set; }

    [JsonPropertyName("async")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? Async { get; set; }

    [JsonPropertyName("other")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? Other { get; set; }

    /// <summary>
    /// Buckets a flat opportunity list into the nested projection. Returns null (absent) when the
    /// list is null or empty. Preserves the scanner's pre-ordering within each bucket.
    /// </summary>
    public static PerformanceProjection? FromOpportunities(
        List<OptimizationOpportunitySummary>? opportunities)
    {
        if (opportunities is not { Count: > 0 })
        {
            return null;
        }

        var projection = new PerformanceProjection();
        foreach (var opportunity in opportunities)
        {
            var bucket = PerformanceKinds.SectionForShape(opportunity.Shape) switch
            {
                SectionNames.PerformanceBoxing => projection.Boxing ??= [],
                SectionNames.PerformanceArrays => projection.Arrays ??= [],
                SectionNames.PerformanceClosures => projection.ClosuresAndDelegates ??= [],
                SectionNames.PerformanceEnumerators => projection.Enumerators ??= [],
                SectionNames.PerformanceLoops => projection.LoopHotPaths ??= [],
                SectionNames.PerformanceHotspots => projection.AllocationHotspots ??= [],
                SectionNames.PerformanceAsync => projection.Async ??= [],
                _ => projection.Other ??= [],
            };
            bucket.Add(opportunity);
        }

        return projection;
    }
}

/// <summary>
/// A curated exception-path resource lifecycle candidate backed by exact Analysis evidence.
/// </summary>
public record class ResourceTriageSummary
{
    public required string Member { get; init; }
    public required string Candidate { get; init; }
    public required string Finding { get; init; }
    public required string Provenance { get; init; }
    public required string Resource { get; init; }
    public required string Shape { get; init; }
    public required string Impact { get; init; }
    public required string Actionability { get; init; }
    public required int AcquireOffset { get; init; }
    public required List<ResourceBoundarySummary> Boundaries { get; init; }
    public required string Evidence { get; init; }
    public required string Direction { get; init; }
    public required string Confidence { get; init; }
    public string? Visibility { get; init; }
    public string? Stable { get; init; }
    public string? Selector { get; init; }
}

public sealed record ResourceBoundarySummary(
    string Operation,
    int ILOffset);

/// <summary>
/// Summary of a dependency age window.
/// </summary>
public record class DependencyAgeSummary(int Count, int MinDays, int MedianDays, int MaxDays);

public sealed record LibraryIntegrationSummaryJson(string Integration, int Count);

public sealed record VersionJson(string Version);

public sealed record VersionListingJson(string Version, string Listing);

/// <summary>
/// One version of a package as carried by one feed. A version present on two feeds
/// produces two of these.
/// </summary>
public sealed record VersionFeedJson(
    string Version,
    string Feed,
    bool Listed);

public sealed record LibraryIntegrationSignalJson(
    string Kind,
    string Name,
    string Shape = IntegrationSignalShape.Type);

public sealed record LibraryInspectionFailureJson(
    string Section,
    string Finding,
    string Reason);

public sealed record LibraryResourceJson(
    string Name,
    string Visibility,
    int Size);

public sealed record class LibraryCustomAttributeJson
{
    public string Name { get; init; } = "";
    public string Target { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; init; }
}

/// <summary>
/// Summary of an async method, including whether it is runtime async or classic
/// state-machine async.
/// </summary>
public record class AsyncMethodSummary
{
    /// <summary>Kind value for runtime async ("async v2").</summary>
    public const string RuntimeKind = "Runtime";

    /// <summary>Kind value for classic compiler state-machine async ("async v1").</summary>
    public const string StateMachineKind = "State machine";

    public string MethodName { get; init; } = "";
    public string DeclaringType { get; init; } = "";
    public string Signature { get; init; } = "";

    /// <summary>"Runtime" for runtime async, "State machine" for classic compiler async.</summary>
    public string Kind { get; init; } = "";
}

/// <summary>
/// One heap value read by coordinate: what was asked for, and what was found there.
///
/// The coordinate travels with the value because a heap value carries no identity of its own — a
/// string is just a string — so rendering it without the heap and address it came from would give
/// a reader nothing to check or follow up on.
/// </summary>
/// <param name="Heap">The heap the coordinate named.</param>
/// <param name="Address">
/// The address the coordinate named: a byte offset, or a 1-based index for the GUID heap.
/// </param>
/// <param name="Value">
/// What was read there. A <see cref="MetadataValue.Malformed"/> when the address is out of range
/// or the value did not decode; never silently replaced by an empty value.
/// </param>
public sealed record MetadataHeapLookup(HeapKind Heap, int Address, MetadataValue Value);