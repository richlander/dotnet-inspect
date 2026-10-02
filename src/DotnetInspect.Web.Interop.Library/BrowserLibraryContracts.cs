using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Library;

public sealed record BrowserUploadedLibraryInspection(
    BrowserUploadedLibraryResult Content,
    BrowserLibraryInspectionShare Share,
    BrowserLibraryInspectionDiagnostic[] Diagnostics);

public sealed record BrowserUploadedLibraryResult(
    BrowserUploadedLibraryInspectionOutcome Outcome,
    string DeclaredName,
    string Digest,
    long ByteLength,
    BrowserEmbeddedLibraryProvenance? Provenance,
    BrowserLibraryAssemblyReference? Assembly,
    BrowserUploadedLibrarySurface? Surface,
    BrowserLibraryInspectionFailure[] InspectionFailures,
    BrowserUploadedLibraryFailure? Failure,
    bool IsComplete);

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserUploadedLibraryInspectionOutcome>))]
public enum BrowserUploadedLibraryInspectionOutcome
{
    Available,
    Rejected,
}

public sealed record BrowserEmbeddedLibraryProvenance(
    string ContentRef,
    string Digest,
    string DeclaredName);

public sealed record BrowserLibraryAssemblyReference(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserUploadedLibrarySurface(
    BrowserLibraryAssemblySurface[] Assemblies,
    BrowserLibraryTypeSurface[] Types,
    BrowserLibraryAccessibilityDescriptor[] Accessibility,
    int TotalMembers,
    string[] InspectionErrors,
    string? InspectionError,
    bool IsTruncated);

public sealed record BrowserUploadedLibraryFailure(
    BrowserUploadedLibraryFailureKind Kind,
    string Detail);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserUploadedLibraryFailureKind>))]
public enum BrowserUploadedLibraryFailureKind
{
    InvalidDeclaredName,
    EmptyImage,
    ResourceBudget,
    DescriptorUnavailable,
    NotAssembly,
    InvalidImage,
    UnsupportedMetadataFormat,
    InspectionFailed,
    ProjectionTruncated,
}

public sealed record BrowserLibraryInspectionFailure(
    string Operation,
    int SubjectToken,
    string Mechanism,
    string Kind,
    string Detail,
    BrowserLibraryAssemblyReference? SubjectAssembly,
    BrowserLibraryAssemblyReference? DependencyAssembly);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryInspectionShareKind>))]
public enum BrowserLibraryInspectionShareKind
{
    Available,
    NonProjectable,
}

public sealed record BrowserLibraryInspectionShare(
    BrowserLibraryInspectionShareKind Kind,
    string? FullUrl,
    string? Packet,
    string? Path,
    string? Reason);

public sealed record BrowserLibraryInspectionDiagnostic(
    string Code,
    string Severity,
    string Summary,
    string? Correspondence);

public sealed record BrowserLibraryAccessibilityDescriptor(
    string Id,
    string Label,
    int Order,
    bool IsDefault,
    int Count);

public sealed record BrowserLibraryAssemblySurface(
    string Id,
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken,
    string Asset,
    int PublicTypes,
    int PublicMembers,
    string? PlatformPack);

public sealed record BrowserLibraryTypeSurface(
    string Id,
    string DefinitionId,
    string QueryId,
    string MetadataId,
    string Name,
    string DisplayName,
    string Namespace,
    string Kind,
    string Accessibility,
    string AccessibilityId,
    string Assembly,
    string AssemblyId,
    string AssemblyName,
    int Members,
    string Signature,
    BrowserLibraryMemberSurface[] Api,
    string? PlatformPack);

public sealed record BrowserLibraryMemberSurface(
    string Name,
    string Kind,
    string Signature,
    string Accessibility,
    bool IsStatic,
    bool IsUnsafe,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsExtension,
    bool IsObsolete,
    int GenericArity,
    int? MetadataToken,
    int? DeclarationMetadataToken,
    string? ReturnType,
    BrowserLibraryParameterSurface[] Parameters,
    string? DocumentationId,
    string? Summary,
    string? Returns,
    BrowserLibraryExceptionSurface[] Exceptions,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string AnchorTypeFullName,
    string? DeclaringTypeDefinitionId,
    string GraphSelectorKey,
    BrowserLibraryMemberBodySelector[] BodySelectors);

public sealed record BrowserLibraryMemberBodySelector(
    int Token,
    string MemberName,
    string SelectorKey);

public sealed record BrowserLibraryParameterSurface(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue,
    string? Description);

public sealed record BrowserLibraryExceptionSurface(
    string Type,
    string Description);

/// <summary>
/// Typed Browser request for one exact Library document
/// (<c>docs/design/library-inspection-document.md#browser-request-lowering</c>).
/// The selector names a Library the Browser already realizes; the plan names
/// the requested document facts.
/// </summary>
public sealed record BrowserLibraryInspectionRequest(
    BrowserLibrarySelector Library,
    BrowserLibraryInspectionPlan Plan);

/// <summary>Exactly one of <see cref="Package"/> or <see cref="Platform"/>, named by <see cref="Kind"/>.</summary>
public sealed record BrowserLibrarySelector(
    BrowserLibrarySelectorKind Kind,
    BrowserPackageLibrarySelector? Package,
    BrowserPlatformLibrarySelector? Platform);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibrarySelectorKind>))]
public enum BrowserLibrarySelectorKind
{
    Package,
    Platform,
}

public sealed record BrowserPackageLibrarySelector(
    string PackageId,
    string Version,
    string TargetFramework,
    string AssemblyId);

public sealed record BrowserPlatformLibrarySelector(
    string TargetFramework,
    string PlatformVersion,
    string AssemblyFileName,
    string Pack);

/// <summary>The Browser subset of <c>LibraryInspectionPlan</c> adopted so far.</summary>
public sealed record BrowserLibraryInspectionPlan(
    bool Enablements);

public sealed record BrowserLibraryDocumentInspection(
    BrowserLibraryDocumentOutcome Outcome,
    string? Detail,
    BrowserLibraryAssemblyReference? Assembly,
    BrowserLibraryEnablements? Enablements,
    BrowserLibraryInspectionDiagnostic[] Diagnostics);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryDocumentOutcome>))]
public enum BrowserLibraryDocumentOutcome
{
    Available,
    Rejected,
    Failed,
    Unavailable,
}

/// <summary>
/// The Enablements fact group. <see cref="Items"/> is empty unless
/// <see cref="Outcome"/> is Available.
/// </summary>
public sealed record BrowserLibraryEnablements(
    BrowserLibraryEnablementsOutcome Outcome,
    BrowserLibraryEnablementsRole? Role,
    string? Failure,
    BrowserLibraryEnablement[] Items);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryEnablementsOutcome>))]
public enum BrowserLibraryEnablementsOutcome
{
    Available,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryEnablementsRole>))]
public enum BrowserLibraryEnablementsRole
{
    [JsonStringEnumMemberName("implementation-assembly")]
    ImplementationAssembly,

    [JsonStringEnumMemberName("api-assembly")]
    ApiAssembly,
}

/// <summary>
/// One enablement fact with its host-neutral label. Badges present only
/// <see cref="BrowserLibraryEnablementKind.Enabled"/> items.
/// </summary>
public sealed record BrowserLibraryEnablement(
    BrowserLibraryEnablementId Id,
    BrowserLibraryEnablementKind Kind,
    string Label,
    BrowserLibraryEnablementUnavailableReason? Reason);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryEnablementId>))]
public enum BrowserLibraryEnablementId
{
    [JsonStringEnumMemberName("aot-compatible")]
    AotCompatible,

    [JsonStringEnumMemberName("runtime-async")]
    RuntimeAsync,

    [JsonStringEnumMemberName("memory-safety-v2")]
    MemorySafetyV2,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryEnablementKind>))]
public enum BrowserLibraryEnablementKind
{
    [JsonStringEnumMemberName("enabled")]
    Enabled,

    [JsonStringEnumMemberName("not-enabled")]
    NotEnabled,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryEnablementUnavailableReason>))]
public enum BrowserLibraryEnablementUnavailableReason
{
    [JsonStringEnumMemberName("reference-assembly")]
    ReferenceAssembly,

    [JsonStringEnumMemberName("undecodable-metadata")]
    UndecodableMetadata,

    [JsonStringEnumMemberName("unrecognized-value")]
    UnrecognizedValue,

    [JsonStringEnumMemberName("conflicting-values")]
    ConflictingValues,

    [JsonStringEnumMemberName("unsupported-memory-safety-rules")]
    UnsupportedMemorySafetyRules,

    [JsonStringEnumMemberName("malformed-memory-safety-rules")]
    MalformedMemorySafetyRules,

    [JsonStringEnumMemberName("conflicting-memory-safety-rules")]
    ConflictingMemorySafetyRules,

    [JsonStringEnumMemberName("memory-safety-metadata-unavailable")]
    MemorySafetyMetadataUnavailable,
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserUploadedLibraryInspection))]
[JsonSerializable(typeof(BrowserLibraryInspectionRequest))]
[JsonSerializable(typeof(BrowserLibraryDocumentInspection))]
internal sealed partial class BrowserLibraryJsonContext : JsonSerializerContext;
