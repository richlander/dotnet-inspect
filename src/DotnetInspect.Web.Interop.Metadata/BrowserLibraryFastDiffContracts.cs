using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Metadata;

public static class BrowserLibraryFastDiffSchema
{
    public const int Version = 2;
}

/// <summary>
/// One exact Library pair: the current compile asset and its target
/// counterpart, and the axes to decide.
/// </summary>
public sealed record BrowserLibraryFastDiffRequest(
    int SchemaVersion,
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    string TargetFramework,
    string CompileAssetId,
    BrowserFastDiffAxes Axes);

public sealed record BrowserLibraryFastDiffResult(
    int SchemaVersion,
    BrowserLibraryFastDiffRequest? Request,
    BrowserLibraryFastDiffResultKind Kind,
    BrowserLibraryFastDiffValue? Value,
    string? Error,
    string? Diagnostic,
    string? Reason);

/// <summary>
/// The compared implementation assets and every Type with an axis that is
/// <c>Changed</c> or <c>Indeterminate</c>. A compared Type that is absent has
/// every requested axis unchanged.
/// </summary>
public sealed record BrowserLibraryFastDiffValue(
    string TargetAssetId,
    string CurrentAssetId,
    int ComparedTypeCount,
    BrowserFastDiffType[] Types);

/// <param name="Identifier">
/// The escaped Type identity navigation uses as <c>definitionId</c>.
/// </param>
public sealed record BrowserFastDiffType(
    string Identifier,
    string FullName,
    BrowserFastDiffState Api,
    BrowserFastDiffState Body);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserFastDiffState>))]
public enum BrowserFastDiffState
{
    Unchanged,
    Changed,
    Indeterminate,
    NotCompared,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserFastDiffAxes>))]
public enum BrowserFastDiffAxes
{
    ApiAndBody,
    Api,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserLibraryFastDiffResultKind>))]
public enum BrowserLibraryFastDiffResultKind
{
    Succeeded,
    Rejected,
    Failed,
    Canceled,
}

public sealed record BrowserLibraryFastDiffCancellation(
    BrowserLibraryApiDiffCancellationKind Kind,
    string? Reason);
