using System.Text.Json.Serialization;

namespace InspectWeb.Engine.SourceFacade;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserSourceComparisonResultKind>))]
public enum BrowserSourceComparisonResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

public sealed record BrowserSourceComparisonRequest(
    string PackageId,
    string BeforeVersion,
    string AfterVersion,
    string Framework,
    string Assembly,
    string TypeIdentity,
    string MemberName,
    string SelectorKey,
    int MetadataToken);

public sealed record BrowserSourceComparisonResult(
    int Version,
    BrowserSourceComparisonResultKind Kind,
    BrowserSourceComparison? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason);

public sealed record BrowserSourceComparison(
    BrowserSourceComparisonRequest Request,
    string Status,
    bool IsExact,
    BrowserSourceComparisonEndpoint Before,
    BrowserSourceComparisonEndpoint After,
    BrowserSourceComparisonLine[] Lines,
    string? Failure);

public sealed record BrowserSourceComparisonEndpoint(
    string PackageId,
    string Version,
    string Framework,
    string Assembly,
    string AssetPath,
    string? ModuleVersionId,
    string AssemblyIdentity,
    string? MemberIdentity,
    int? MetadataToken,
    string State,
    string? Detail,
    string? Text,
    string? SourceUrl,
    string? RepositoryUrl,
    string? Revision);

public sealed record BrowserSourceComparisonLine(
    string Kind,
    string Difference,
    int? BeforeLine,
    string? BeforeText,
    int? AfterLine,
    string? AfterText);
