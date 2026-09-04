using System.Text.Json.Serialization;

namespace InspectWeb.Engine.MetadataFacade;

/// <summary>
/// The metadata facade's browser wire contract.
/// </summary>
/// <remarks>
/// Every record here is declared and source-generated inside
/// <c>InspectWeb.Engine.MetadataExports</c>. Records that are structurally equal to another
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

/// <summary>
/// One type's metadata projection, adapted from <c>ResearchViews.TypeProjectionResult</c> — the
/// presentation-neutral seam the CLI consumes — so the browser never reimplements type-fact
/// composition.
/// </summary>
public sealed record BrowserTypeMetadata(
    string FullName,
    string? Namespace,
    string Name,
    string Kind,
    string[] Modifiers,
    string? Accessibility,
    string? Assembly,
    string? BaseType,
    string[] Interfaces,
    string[] DerivedTypes,
    BrowserTypeParameter[] TypeParameters,
    string[] Attributes,
    string? EnumUnderlyingType,
    BrowserTypeComposition? Composition,
    BrowserTypeGraphNode[] GraphNodes,
    BrowserTypeGraphEdge[] GraphEdges,
    string[] InspectionFailures);

public sealed record BrowserTypeParameter(string Name, string? Variance, string[] Constraints);

public sealed record BrowserTypeComposition(
    int Methods,
    int Properties,
    int Fields,
    int Events,
    int Constructors,
    int Operators,
    int ExplicitInterfaceImplementations,
    int ExtensionMethods,
    int Static,
    int Unsafe,
    int Async,
    int Virtual,
    int Abstract,
    int Override,
    int Extension,
    int Obsolete,
    int Total);

public sealed record BrowserTypeGraphNode(string Id, string DisplayName, string Role);

public sealed record BrowserTypeGraphEdge(string FromId, string ToId, string Kind);

public sealed record BrowserPackageMetadata(
    BrowserAssemblyMetadata[] Assemblies,
    string? InspectionError,
    BrowserCompileLibraryAvailability CompileLibrary);

public sealed record BrowserAssemblyMetadata(
    string Assembly,
    string MetadataVersion,
    bool MetadataVersionTruncated,
    string Kind,
    bool IsAssembly,
    int MetadataSize,
    int ProjectedTableTotal,
    BrowserMetadataHeap[] Heaps,
    BrowserMetadataTable[] Tables,
    BrowserMetadataHeaders Headers);

public sealed record BrowserMetadataHeap(
    string Name,
    int SizeInBytes,
    int MaxAddress,
    string Addressing);

public sealed record BrowserMetadataTable(
    int Index,
    string Name,
    int RowCount,
    bool IsProjected);

public sealed record BrowserMetadataHeaders(
    string Machine,
    bool IsPE32Plus,
    string Subsystem,
    string? CorFlags,
    int? MajorRuntimeVersion,
    int? MinorRuntimeVersion,
    int? EntryPointToken);

public sealed record BrowserMetadataWindow(
    string Assembly,
    int Index,
    string Name,
    int RowCount,
    int StartRowId,
    BrowserMetadataColumn[] Columns,
    BrowserMetadataRow[] Rows,
    bool Truncated,
    string? Error);

public sealed record BrowserMetadataColumn(
    string Name,
    string Kind,
    int[] CandidateTargets);

public sealed record BrowserMetadataRow(
    int RowId,
    int Token,
    BrowserMetadataCell[] Cells);

public sealed record BrowserMetadataCell(
    string Kind,
    long? Raw = null,
    string? Display = null,
    string? Decoded = null,
    string? Heap = null,
    string? Text = null,
    string? Preview = null,
    int? Offset = null,
    int? Length = null,
    bool? Truncated = null,
    int? TargetTable = null,
    int? TargetRowId = null,
    int? StartRowId = null,
    int? EndRowId = null,
    int? Count = null,
    int? Token = null,
    string? Detail = null);

public sealed record BrowserHeapListing(
    string Assembly,
    string Heap,
    string StreamName,
    string Coverage,
    BrowserHeapEntry[] Entries,
    bool RowsTruncated,
    bool EntriesTruncated,
    string? Error);

public sealed record BrowserHeapEntry(
    int Offset,
    BrowserMetadataCell Value,
    int ReferenceCount);

/// <summary>
/// The owning API member and exact physical body selected by a graph query.
/// <c>MemberFacts_DistinguishesSurfaceAndBodyTokenResolution</c> gates this provenance.
/// </summary>
public sealed record BrowserGraphMemberSurface(
    BrowserTypeSurface Type,
    BrowserMemberBodySelector SelectedBody);

/// <summary>
/// One type row projected for a graph target. See the package facade's declaration for the
/// identity rules these fields carry; this facade owns its own copy of the transport.
/// </summary>
public sealed record BrowserTypeSurface(
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
    BrowserMemberSurface[] Api,
    string? PlatformPack);

public sealed record BrowserMemberSurface(
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
    BrowserParameterSurface[] Parameters,
    string? DocumentationId,
    string? Summary,
    string? Returns,
    BrowserExceptionSurface[] Exceptions,
    string StableSelector,
    string AnchorDigest,
    string CanonicalSignature,
    string GraphSelectorKey,
    BrowserMemberBodySelector[] BodySelectors);

public sealed record BrowserMemberBodySelector(
    int Token,
    string MemberName,
    string SelectorKey);

public sealed record BrowserParameterSurface(
    string Name,
    string Type,
    string? Modifier,
    bool HasDefault,
    string? DefaultValue,
    string? Description);

public sealed record BrowserExceptionSurface(
    string Type,
    string Description);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPackageMetadata))]
[JsonSerializable(typeof(BrowserMetadataWindow))]
[JsonSerializable(typeof(BrowserHeapListing))]
[JsonSerializable(typeof(BrowserTypeMetadata))]
[JsonSerializable(typeof(BrowserGraphMemberSurface))]
internal sealed partial class BrowserMetadataJsonContext : JsonSerializerContext;
