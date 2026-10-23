using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Metadata;

public static class BrowserLibraryFindingDiffAnySchema
{
    public const int Version = 2;
}

/// <summary>
/// One exact Library pair: the current compile asset and its target
/// counterpart, and the axes to decide.
/// </summary>
public sealed record BrowserLibraryFindingDiffAnyRequest(
    int SchemaVersion,
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    string TargetFramework,
    string CompileAssetId,
    BrowserFindingDiffAnyAxes Axes);

public sealed record BrowserLibraryFindingDiffAnyResult(
    int SchemaVersion,
    BrowserLibraryFindingDiffAnyRequest? Request,
    BrowserLibraryFindingDiffAnyResultKind Kind,
    BrowserLibraryFindingDiffAnyValue? Value,
    string? Error,
    string? Diagnostic,
    string? Reason);

/// <summary>
/// The compared implementation assets and every Type with an axis that is
/// <c>Changed</c> or <c>Indeterminate</c>. A compared Type that is absent has
/// every requested axis unchanged.
/// </summary>
public sealed record BrowserLibraryFindingDiffAnyValue(
    string TargetAssetId,
    string CurrentAssetId,
    int ComparedTypeCount,
    BrowserFindingDiffAnyType[] Types);

/// <param name="Identifier">
/// The escaped Type identity navigation uses as <c>definitionId</c>.
/// </param>
public sealed record BrowserFindingDiffAnyType(
    string Identifier,
    string FullName,
    BrowserFindingDiffAnyState Api,
    BrowserFindingDiffAnyState Body);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserFindingDiffAnyState>))]
public enum BrowserFindingDiffAnyState
{
    Unchanged,
    Changed,
    Indeterminate,
    NotCompared,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserFindingDiffAnyAxes>))]
public enum BrowserFindingDiffAnyAxes
{
    ApiAndBody,
    Api,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryFindingDiffAnyResultKind>))]
public enum BrowserLibraryFindingDiffAnyResultKind
{
    Succeeded,
    Rejected,
    Failed,
    Canceled,
}

public sealed record BrowserLibraryFindingDiffAnyCancellation(
    BrowserLibraryApiDiffCancellationKind Kind,
    string? Reason);
