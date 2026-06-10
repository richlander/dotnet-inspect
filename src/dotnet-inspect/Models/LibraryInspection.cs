using System.Text.Json.Serialization;
using DotnetInspector.Metadata;

namespace DotnetInspector.Models;

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiSurface? ApiSurface { get; set; }

    /// <summary>
    /// Extension methods defined in this assembly, grouped by extended type.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExtensionMethodSummary>? ExtensionMethods { get; set; }

    /// <summary>
    /// Public methods with unsafe (pointer) signatures.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClassifiedMethodSummary>? UnsafeMethods { get; set; }

    /// <summary>
    /// Public P/Invoke (DllImport/LibraryImport) methods.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClassifiedMethodSummary>? PInvokeMethods { get; set; }

    /// <summary>
    /// Public async methods, classified as runtime async or classic state-machine async.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AsyncMethodSummary>? AsyncMethods { get; set; }

    /// <summary>
    /// Ecosystem integrations detected from package references and metadata usage.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<IntegrationSummary>? Integrations { get; set; }

    /// <summary>
    /// Metadata evidence of OpenTelemetry packages or .NET diagnostics primitives.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenTelemetrySignal>? OpenTelemetry { get; set; }

    /// <summary>
    /// Manifest resources embedded in this assembly.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ResourceSummary>? Resources { get; set; }

    /// <summary>
    /// File size of the assembly in bytes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long FileSize { get; set; }

    /// <summary>
    /// Assembly-level and module-level custom attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CustomAttributeSummary>? CustomAttributes { get; set; }

    /// <summary>
    /// Metadata audit signals. These are observations, not a trust verdict.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AuditSignal>? AuditSignals { get; set; }

    /// <summary>
    /// Type forwarders defined in this assembly.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TypeForwarderSummary>? TypeForwarders { get; set; }

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

    /// <summary>Whether the assembly contains any public runtime-async methods (impl flag 0x2000).</summary>
    [JsonIgnore]
    public bool HasRuntimeAsync { get; set; }

    /// <summary>Whether the assembly contains any public classic state-machine async methods.</summary>
    [JsonIgnore]
    public bool HasStateMachineAsync { get; set; }

    /// <summary>Whether the assembly has manifest resources.</summary>
    [JsonIgnore]
    public bool HasManifestResources { get; set; }

    /// <summary>Whether the assembly references OpenTelemetry or .NET diagnostics telemetry primitives.</summary>
    [JsonIgnore]
    public bool HasOpenTelemetrySupport { get; set; }

    /// <summary>Whether the assembly has non-well-known custom attributes.</summary>
    [JsonIgnore]
    public bool HasAssemblyAttributes { get; set; }

    /// <summary>Whether the assembly has type forwarders.</summary>
    [JsonIgnore]
    public bool HasExportedTypeForwarders { get; set; }

    /// <summary>
    /// View routing flag: when true, show nested dependency tree instead of flat references.
    /// </summary>
    [JsonIgnore]
    public bool UseDependenciesView { get; set; }
}

/// <summary>
/// Summary of an extension method defined in a library.
/// </summary>
public record class ExtensionMethodSummary
{
    public string MethodName { get; init; } = "";
    public string ExtendedType { get; init; } = "";
    public string ExtensionClass { get; init; } = "";
    public string Kind { get; init; } = "method";
    public int? Overloads { get; init; }
}

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
/// Summary of a dependency age window.
/// </summary>
public record class DependencyAgeSummary(int Count, int MinDays, int MedianDays, int MaxDays);

public record IntegrationSummary(string Integration, int Count, string NextSection);

public record OpenTelemetrySignal(string Area, string Signal, string Value, string Evidence);

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
/// Summary of a manifest resource in a library.
/// </summary>
public record class ResourceSummary
{
    public string Name { get; init; } = "";
    public string Visibility { get; init; } = "";
    public int Size { get; init; }
}

/// <summary>
/// Summary of a custom attribute on the assembly or module.
/// </summary>
public record class CustomAttributeSummary
{
    public string Name { get; init; } = "";
    public string Target { get; init; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; init; }
}

/// <summary>
/// Summary of a type forwarder in a library.
/// </summary>
public record class TypeForwarderSummary
{
    public string TypeName { get; init; } = "";
    public string TargetAssembly { get; init; } = "";
}
