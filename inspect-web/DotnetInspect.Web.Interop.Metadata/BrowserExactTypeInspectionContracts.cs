using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Metadata;

/// <summary>
/// Resource-free Browser transport for the shared exact-type inspection envelope.
/// </summary>
public sealed record BrowserExactTypeInspectionEnvelope(
    BrowserExactTypeInspectionContent Content,
    BrowserExactTypeShare Share,
    BrowserExactTypeDiagnostic[] Diagnostics);

public sealed record BrowserExactTypeInspectionContent(
    BrowserExactTypeOutcome Kind,
    bool IsComplete,
    BrowserExactTypeRequest Request,
    BrowserExactTypeAvailable? Available,
    BrowserExactTypeName[] Suggestions,
    BrowserExactTypeCandidate[] Candidates,
    BrowserExactTypeFailure[] Failures);

public sealed record BrowserExactTypeRequest(
    int ContextIndex,
    string TypeSelector,
    BrowserExactTypeSurfaceScope Scope,
    BrowserExactTypeSurfaceLimits SurfaceLimits,
    string? AssemblyName,
    BrowserExactTypeAssemblyIdentity? Library,
    string? CompileAssetId);

public sealed record BrowserExactTypeSurfaceLimits(
    int MaxParticipants,
    int MaxTypes,
    int MaxMembers,
    int MaxInspectionFailures,
    int MaxTypeForwarders,
    int MaxMetadataRows,
    int MaxRetainedTextCharacters);

public sealed record BrowserExactTypeAvailable(
    BrowserExactTypeCandidate Candidate,
    bool IsContextUnique,
    BrowserExactTypeSelectedType Type,
    BrowserExactTypeMemberFacts[] MemberFacts,
    BrowserExactTypeFacet[] MemberKindFacets,
    BrowserExactTypeInspectionFailure[] InspectionFailures);

public sealed record BrowserExactTypeMemberFacts(
    int? MetadataToken,
    int? DeclarationMetadataToken,
    bool IsAsync,
    bool? HasMethodBody,
    string[] Attributes);

public sealed record BrowserExactTypeSelectedType(
    BrowserTypeSurface Surface,
    string? BaseType,
    string[] Interfaces,
    string[] DerivedTypes,
    BrowserTypeParameter[] TypeParameters,
    string[] Attributes,
    string? EnumUnderlyingType,
    BrowserTypeComposition? Composition,
    bool IsForwarded);

public sealed record BrowserExactTypeFacet(
    string Id,
    string SingularLabel,
    string PluralLabel,
    int Weight,
    int Count,
    bool IsDefault);

public sealed record BrowserExactTypeCandidate(
    BrowserExactTypeName Definition,
    BrowserExactTypeDefinitionAddress Address,
    BrowserExactTypeLibrarySource Declaration,
    BrowserExactTypeLibrarySource Supplier,
    BrowserExactTypeRealizedSource SupplierSource,
    BrowserExactTypeAssemblyIdentity SupplierAssembly,
    BrowserExactTypeForwardingHop[] ForwardingHops,
    string DeclarationAssetId,
    string SupplierAssetId);

public sealed record BrowserExactTypeName(
    string Namespace,
    string[] Segments);

public sealed record BrowserExactTypeDefinitionAddress(
    string ModuleVersionId,
    int MetadataToken);

public sealed record BrowserExactTypeLibrarySource(
    BrowserExactTypeLibrarySourceKind Kind,
    BrowserExactTypeAssemblyIdentity Library,
    string? PackageId = null,
    string? Version = null,
    string? PlatformFamily = null);

public sealed record BrowserExactTypeRealizedSource(
    BrowserExactTypeRealizedSourceKind Kind,
    string? PackageId = null,
    string? Version = null,
    string? Producer = null,
    string? Framework = null,
    string? RuntimeIdentifier = null,
    string? PlatformFamily = null,
    string? Assembly = null);

public sealed record BrowserExactTypeAssemblyIdentity(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

public sealed record BrowserExactTypeForwardingHop(
    BrowserExactTypeAssemblyIdentity SourceAssembly,
    BrowserExactTypeAssemblyIdentity TargetAssembly,
    BrowserExactTypeResolutionScope Scope);

public sealed record BrowserExactTypeFailure(
    BrowserExactTypeFailureKind Kind,
    BrowserExactTypeAssemblyIdentity? Assembly,
    string? ContextLoadFailure,
    string? CandidateOpenFailure,
    string? PopulationFailure,
    string? SurfaceLimit);

public sealed record BrowserExactTypeInspectionFailure(
    string Operation,
    int SubjectToken,
    BrowserExactTypeInspectionFailureMechanism Mechanism,
    string Kind,
    string Detail,
    BrowserExactTypeAssemblyIdentity? SubjectAssembly,
    BrowserExactTypeAssemblyIdentity? DependencyAssembly,
    int? OwningTypeToken,
    BrowserExactTypeName? OwningTypeDefinition,
    BrowserExactTypeName[] AffectedTypeDefinitions);

public sealed record BrowserExactTypeShare(
    BrowserExactTypeShareKind Kind,
    string? FullUrl,
    string? Packet,
    string? Path,
    string? Reason);

public sealed record BrowserExactTypeDiagnostic(
    string Code,
    BrowserExactTypeDiagnosticSeverity Severity,
    string Summary,
    string? Correspondence);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeOutcome>))]
public enum BrowserExactTypeOutcome
{
    Available,
    NotFound,
    Ambiguous,
    Incomplete,
    Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeSurfaceScope>))]
public enum BrowserExactTypeSurfaceScope
{
    Public,
    IncludeAll,
    PublicWithNonPublicTypes,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeLibrarySourceKind>))]
public enum BrowserExactTypeLibrarySourceKind
{
    Package,
    Platform,
    Project,
    Local,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeRealizedSourceKind>))]
public enum BrowserExactTypeRealizedSourceKind
{
    Package,
    Platform,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeResolutionScope>))]
public enum BrowserExactTypeResolutionScope
{
    Any,
    Platform,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeFailureKind>))]
public enum BrowserExactTypeFailureKind
{
    InvalidRequest,
    DefinitionMismatch,
    ContextUnavailable,
    ContextLoadFailed,
    PopulationUnavailable,
    DeclarationInventoryIncomplete,
    TypeResolutionRejected,
    TypeResolutionUnavailable,
    TypeResolutionAmbiguous,
    ApiSurfaceRejected,
    ApiSurfaceFailed,
    ApiSurfaceIncomplete,
    AsyncClassificationUnavailable,
    ResolvedTypeMissing,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserExactTypeInspectionFailureMechanism>))]
public enum BrowserExactTypeInspectionFailureMechanism
{
    Metadata,
    Relationship,
    Signature,
    TypeSpecification,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeShareKind>))]
public enum BrowserExactTypeShareKind
{
    Available,
    NonProjectable,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserExactTypeDiagnosticSeverity>))]
public enum BrowserExactTypeDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}
