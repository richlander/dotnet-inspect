namespace InspectWeb.Engine;

/// <summary>
/// The DTO-neutral projections shared by the browser's capability export assemblies.
/// </summary>
/// <remarks>
/// <para>
/// Each export assembly owns its own wire records and its own source-generated serializer
/// vocabulary, so no facade may serialize a type declared here. These models carry the projected
/// product evidence between <c>InspectWeb.Engine.Core</c>'s shared workspace mechanics and the
/// facade that publishes it; a facade maps them to its own transport records.
/// </para>
/// <para>
/// Their shapes intentionally mirror the browser transport so that mapping stays a rename rather
/// than a re-derivation. The projection semantics — identity, ordering, accessibility bucketing,
/// truncation, and failure text — belong to <see cref="BrowserSurfaceProjection"/>.
/// </para>
/// </remarks>
internal sealed record BrowserAccessibilityInfo(
    string Id,
    string Label,
    int Order,
    bool IsDefault,
    int Count);

internal sealed record BrowserAssemblySurfaceInfo(
    string Id,
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken,
    string Asset,
    int PublicTypes,
    int PublicMembers,
    string? PlatformPack);

internal sealed record BrowserTypeSurfaceInfo(
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
    BrowserMemberSurfaceInfo[] Api,
    string? PlatformPack);

internal sealed record BrowserMemberSurfaceInfo(
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
    string? ReturnType,
    BrowserParameterSurfaceInfo[] Parameters,
    string? DocumentationId,
    string? Summary,
    string? Returns,
    BrowserExceptionSurfaceInfo[] Exceptions,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string GraphSelectorKey,
    BrowserMemberBodySelectorInfo[] BodySelectors);

internal sealed record BrowserParameterSurfaceInfo(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue,
    string? Description);

internal sealed record BrowserExceptionSurfaceInfo(
    string Type,
    string Description);

internal sealed record BrowserMemberBodySelectorInfo(
    int Token,
    string MemberName,
    string SelectorKey);

/// <summary>
/// One package or platform workspace's complete browsable surface, before a facade maps it to its
/// own transport records.
/// </summary>
internal sealed record BrowserPackageSurfaceInfo(
    string Package,
    string Version,
    string[] Frameworks,
    string ActiveFramework,
    BrowserPackageIconPayload? Icon,
    string? DefaultAssemblyId,
    BrowserCompileLibraryInfo CompileLibrary,
    BrowserAssemblySurfaceInfo[] Assemblies,
    BrowserTypeSurfaceInfo[] Types,
    BrowserAccessibilityInfo[] Accessibility,
    int TotalMembers,
    IReadOnlyList<BrowserPackageDocumentEntry> Documents,
    string[] InspectionErrors,
    string? InspectionError);
