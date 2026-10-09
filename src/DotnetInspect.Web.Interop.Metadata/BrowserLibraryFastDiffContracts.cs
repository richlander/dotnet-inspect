using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Metadata;

public static class BrowserLibraryFastDiffSchema
{
    public const int Version = 1;
}

/// <summary>One exact Library pair: the current compile asset and its target counterpart.</summary>
public sealed record BrowserLibraryFastDiffRequest(
    int SchemaVersion,
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    string TargetFramework,
    string CompileAssetId);

public sealed record BrowserLibraryFastDiffResult(
    int SchemaVersion,
    BrowserLibraryFastDiffRequest? Request,
    BrowserLibraryFastDiffResultKind Kind,
    BrowserLibraryFastDiffValue? Value,
    string? Error,
    string? Diagnostic,
    string? Reason);

/// <summary>
/// The compared implementation assets and every Type whose API or Body is not
/// <c>Unchanged</c>. A compared Type that is absent has both axes unchanged.
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
