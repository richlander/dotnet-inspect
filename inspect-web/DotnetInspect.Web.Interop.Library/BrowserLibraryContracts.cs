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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserUploadedLibraryInspection))]
internal sealed partial class BrowserLibraryJsonContext : JsonSerializerContext;
