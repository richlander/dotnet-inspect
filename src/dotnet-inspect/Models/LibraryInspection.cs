using System.Text.Json.Serialization;
using DotnetInspector.Options;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.Analysis;

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
    /// Whether a SourceLink Integrity pass (GET + checksum verification) was run.
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssemblyInfo? AssemblyInfo { get; set; }

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<UnsafeMemberSummary>? UnsafeMembers { get; set; }

    /// <summary>
    /// Methods ranked by call-graph leverage (distinct direct callers, then outbound
    /// shape). Assembly-wide; populated only when the Top Leverage section is selected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MethodLeverageSummary>? TopLeverage { get; set; }

    /// <summary>
    /// Safe, local optimization opportunities inferred from IL/body evidence.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OptimizationOpportunitySummary>? OptimizationOpportunities { get; set; }

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

    [JsonIgnore]
    public PerformanceTriageOptions PerformanceTriageOptions { get; set; } = PerformanceTriageOptions.Default;

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
            AddFailure(failures, "Extension Methods", ExtensionMemberInspection);
            AddFailure(failures, LibraryIntegrationCatalog.RollupName, EcosystemIntegrationInspection);
            AddFailure(failures, EcosystemIntegrationNames.OpenTelemetry, OpenTelemetryInspection);
            AddFailure(failures, "Resources", ResourceInspection);
            AddFailure(failures, "Custom Attributes", AssemblyAttributeInspection);
            AddFailure(failures, "Type Forwarders", TypeForwarderInspection);
            AddFailure(failures, "Union Types", UnionTypeInspection);
            AddFailure(failures, "Switches", SwitchInspection);
            AddFailure(
                failures,
                DotnetInspector.Sections.SectionNames.ResourceTriage,
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
    public ILOffsetResult? ILOffset { get; set; }

    // Presence flags — populated cheaply from MetadataReader before scanners run.
    // Used by CanRender for fast -s discovery without full scanning.

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

    /// <summary>
    /// View routing flag: when true, show nested dependency tree instead of flat references.
    /// </summary>
    [JsonIgnore]
    public bool UseDependenciesView { get; set; }
}

public class ILOffsetResult
{
    public string? Method { get; init; }
    public string? Token { get; init; }
    public string? ILOffset { get; init; }
    public string? MatchedOffset { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public string? Url { get; init; }
    public ILOffsetMemberContext? MemberContext { get; init; }
    public ILOffsetInstructionContext? InstructionContext { get; init; }
    public List<ILOffsetExceptionContext>? ExceptionContext { get; init; }
    public ILOffsetCallsiteContext? CallsiteContext { get; init; }
    public ILOffsetReturnAddressContext? ReturnAddressContext { get; init; }
    public List<ILOffsetAllocationContext>? AllocationContext { get; init; }
    public List<ILOffsetSafetyContext>? SafetyContext { get; init; }
    public List<ILOffsetCostContext>? CostContext { get; init; }
}

public class ILOffsetMemberContext
{
    public string? Assembly { get; init; }
    public string? Type { get; init; }
    public string? TypeKind { get; init; }
    public string? Member { get; init; }
    public string? Signature { get; init; }
    public string? MemberKind { get; init; }
    public string? Visibility { get; init; }
    public string? Static { get; init; }
    public string? Async { get; init; }
    public string? MetadataToken { get; init; }
    public string? ILOffset { get; init; }
}

public class ILOffsetInstructionContext
{
    public string? ILOffset { get; init; }
    public string? Boundary { get; init; }
    public string? Opcode { get; init; }
    public string? OperandKind { get; init; }
    public string? Operand { get; init; }
    public string? OperandToken { get; init; }
    public string? BranchTargets { get; init; }
    public string? NextOffset { get; init; }
    public int? Length { get; init; }
    public int? Block { get; init; }
    public string? TerminatesBlock { get; init; }
    public string? FallsThrough { get; init; }
}

public class ILOffsetExceptionContext
{
    public int Region { get; init; }
    public string? Context { get; init; }
    public string? Clause { get; init; }
    public string? TryRange { get; init; }
    public string? HandlerRange { get; init; }
    public string? FilterRange { get; init; }
    public string? CaughtType { get; init; }
}

public class ILOffsetCallsiteContext
{
    public string? CallOffset { get; init; }
    public string? Opcode { get; init; }
    public string? CallKind { get; init; }
    public string? Callee { get; init; }
    public string? OperandToken { get; init; }
    public string? ReturnAddress { get; init; }
}

public class ILOffsetReturnAddressContext
{
    public string? ILOffset { get; init; }
    public string? CallOffset { get; init; }
    public string? Opcode { get; init; }
    public string? CallKind { get; init; }
    public string? Callee { get; init; }
    public string? OperandToken { get; init; }
}

public class ILOffsetAllocationContext
{
    public string? ILOffset { get; init; }
    public string? AllocationKind { get; init; }
    public string? AllocatedType { get; init; }
    public string? CountedAsHeap { get; init; }
    public string? Frequency { get; init; }
    public string? Escape { get; init; }
    [JsonPropertyName("escape_kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EscapeKind { get; init; }
    [JsonPropertyName("est_size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EstimatedSizeBytes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SizeTier { get; init; }
    public string? InLoop { get; init; }
    public string? Path { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PathConfidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostDominance { get; init; }
    public string? Evidence { get; init; }
    [JsonPropertyName("multiplicity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Multiplicity { get; init; }
    [JsonPropertyName("churned_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChurnedType { get; init; }
}

public class ILOffsetSafetyContext
{
    public string? ILOffset { get; init; }
    public string? SafetyKind { get; init; }
    public string? Operation { get; init; }
    public string? Requirement { get; init; }
    public string? Evidence { get; init; }
}

public class ILOffsetCostContext
{
    public string? ILOffset { get; init; }
    public string? CostKind { get; init; }
    public string? Operation { get; init; }
    public string? InLoop { get; init; }
    public string? Evidence { get; init; }
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
    public string Evidence { get; init; } = "";
    public string Fix { get; init; } = "";
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
/// A curated exception-path resource lifecycle candidate backed by exact Analysis evidence.
/// </summary>
public record class ResourceTriageSummary
{
    public string Member { get; init; } = "";
    public string Candidate { get; init; } = "";
    public string Finding { get; init; } = "";
    public string Provenance { get; init; } = "";
    public string Resource { get; init; } = "";
    public string Shape { get; init; } = "";
    public string Impact { get; init; } = "";
    public string Actionability { get; init; } = "";
    public string Boundary { get; init; } = "";
    public string AcquireIL { get; init; } = "";
    public string BoundaryIL { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string Direction { get; init; } = "";
    public string Confidence { get; init; } = "";
    public string? Visibility { get; init; }
    public string? Stable { get; init; }
    public string? Selector { get; init; }
}

/// <summary>
/// Summary of a dependency age window.
/// </summary>
public record class DependencyAgeSummary(int Count, int MinDays, int MedianDays, int MaxDays);

public sealed record LibraryIntegrationSummaryJson(string Integration, int Count);

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
