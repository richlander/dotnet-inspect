namespace DotnetInspect.Web.Interop.Metadata;

public static class BrowserTypeFindingDiffAnySchema
{
    public const int Version = 1;
}

public sealed record BrowserTypeFindingDiffAnyRequest(
    int SchemaVersion,
    BrowserLibraryFindingDiffAnyRequest Comparison,
    string TypeIdentifier);

public sealed record BrowserFindingDiffAnyMember(
    string? BeforeFingerprint,
    string? AfterFingerprint,
    BrowserFindingDiffAnyState Api,
    BrowserFindingDiffAnyState Body);

public sealed record BrowserTypeFindingDiffAnyValue(
    string TypeIdentifier,
    string TargetAssetId,
    string CurrentAssetId,
    BrowserFindingDiffAnyState Api,
    BrowserFindingDiffAnyState Body,
    BrowserFindingDiffAnyState ResidualApi,
    BrowserFindingDiffAnyState ResidualBody,
    BrowserFindingDiffAnyMember[] Members);

public sealed record BrowserTypeFindingDiffAnyResult(
    int SchemaVersion,
    BrowserTypeFindingDiffAnyRequest? Request,
    BrowserLibraryFindingDiffAnyResultKind Kind,
    BrowserTypeFindingDiffAnyValue? Value,
    string? Error,
    string? Diagnostic,
    string? Reason,
    BrowserLibraryFindingDiffAnyMissingCounterpart? MissingCounterpart = null);
