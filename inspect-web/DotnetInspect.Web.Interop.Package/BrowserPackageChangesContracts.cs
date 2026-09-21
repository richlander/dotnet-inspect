using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Package;

public sealed record BrowserPackageChangesRequest(
    string PackageSetId,
    string? FromExclusive,
    string? ThroughInclusive,
    bool SecurityOnly,
    int MaximumRows);

public sealed record BrowserPackageChangesPackageSetDescriptor(
    string Id,
    string Title,
    string Summary,
    int Order);

public sealed record BrowserPackageChangesPackageSetCatalog(
    int Version,
    BrowserPackageChangesPackageSetDescriptor[] PackageSets);

public sealed record BrowserPackageChangesPackageScope(
    string Kind,
    string? SelectionId,
    string? Prefix,
    string[] PackageIds);

public sealed record BrowserPackageChangesResolvedRequest(
    DateTimeOffset ReferenceTime,
    DateTimeOffset FromExclusive,
    DateTimeOffset ThroughInclusive,
    bool UsedDefaultInterval,
    BrowserPackageChangesPackageScope PackageScope,
    string SecuritySelection,
    int MaximumRows,
    int MaximumCandidateEvents,
    int MaximumReceiptRequests);

public sealed record BrowserPackageChangesSource(
    string ProducerKey,
    string Producer,
    string TransportKind);

public sealed record BrowserPackageChangesCatalogActivity(
    string PackageId,
    string Version,
    string NormalizedPackageId,
    string NormalizedVersion,
    string LeafUrl,
    string CommitId,
    DateTimeOffset CommitTimestamp,
    string CatalogKind,
    string Activity);

public sealed record BrowserPackageChangesAdvisoryReference(
    string GhsaId,
    string? CveId,
    string Severity,
    string AdvisoryUrl,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt);

public sealed record BrowserPackageChangesAdvisoryEvidence(
    string Availability,
    BrowserPackageChangesAdvisoryReference[] Advisories);

public sealed record BrowserPackageChangesPackageReceipt(
    DateTimeOffset ReceivedAt,
    string Basis);

public sealed record BrowserPackageChangesSecurityRelease(
    BrowserPackageChangesPackageReceipt Receipt,
    BrowserPackageChangesAdvisoryReference[] Advisories);

public sealed record BrowserPackageChangesRow(
    BrowserPackageChangesCatalogActivity CatalogActivity,
    BrowserPackageChangesAdvisoryEvidence CurrentAdvisoryContext,
    BrowserPackageChangesAdvisoryEvidence FixedVersionEvidence,
    BrowserPackageChangesPackageReceipt? PackageReceipt,
    string SecurityReleaseStatus,
    BrowserPackageChangesSecurityRelease? SecurityRelease,
    bool IsSecurityRelevant);

public sealed record BrowserPackageChangesPackageSourceFailure(
    int Capability,
    string? PackageId,
    string? Version,
    string Kind,
    string Detail);

public sealed record BrowserPackageChangesReceiptFailure(
    BrowserPackageChangesCatalogActivity CatalogActivity,
    BrowserPackageChangesPackageSourceFailure Failure);

public sealed record BrowserPackageChangesAdvisoryPackage(
    string PackageId,
    string Version,
    BrowserPackageChangesAdvisoryEvidence CurrentAdvisoryContext,
    BrowserPackageChangesAdvisoryEvidence FixedVersionEvidence);

public sealed record BrowserPackageChangesAdvisoryAcquisition(
    string PackageProducerKey,
    string AdvisoryProducer,
    DateTimeOffset ObservedAt,
    int ApiRequests,
    long ResponseBytes,
    bool Complete,
    string[] Failures,
    BrowserPackageChangesAdvisoryPackage[] Packages);

public sealed record BrowserPackageChangesProgress(
    string Phase,
    long Completed,
    long? Total,
    DateTimeOffset? CapturedHorizon,
    int CatalogPagesAcquired,
    int CatalogHttpAttempts,
    long CatalogDecodedBytes);

public sealed record BrowserPackageChangesFailure(
    string Provider,
    BrowserPackageChangesPackageSourceFailure? CatalogFailure,
    string? AdvisoryFailure,
    BrowserPackageChangesReceiptFailure? PackageReceiptFailure);

public sealed record BrowserPackageChangesSummary(
    DateTimeOffset? CapturedHorizon,
    string? CatalogCompletion,
    BrowserPackageChangesPackageSourceFailure? CatalogFailure,
    int CatalogPagesAcquired,
    int CatalogHttpAttempts,
    long CatalogDecodedBytes,
    long CatalogInWindowEventCount,
    long MatchingEventCount,
    int RetainedEventCount,
    bool CandidateLimitReached,
    BrowserPackageChangesAdvisoryAcquisition AdvisoryEvidence,
    int ReceiptCandidates,
    int ReceiptRequests,
    int ReceiptSuccesses,
    BrowserPackageChangesReceiptFailure[] ReceiptFailures,
    bool ReceiptLimitReached,
    int CurrentContextUnevaluableRows,
    int SecurityReleaseUnevaluableRows,
    int EligibleRowCount,
    int ReturnedRowCount,
    bool ResultLimitReached,
    string Completion);

public sealed record BrowserPackageChangesDocument(
    int SchemaVersion,
    BrowserPackageChangesResolvedRequest Request,
    BrowserPackageChangesSource Source,
    BrowserPackageChangesProgress[] Progress,
    BrowserPackageChangesRow[] Rows,
    BrowserPackageChangesFailure[] Failures,
    BrowserPackageChangesSummary Summary);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageChangesEventKind>))]
public enum BrowserPackageChangesEventKind
{
    Progress,
    Row,
    Failure,
}

public sealed record BrowserPackageChangesEvent(
    BrowserPackageChangesEventKind Kind,
    BrowserPackageChangesProgress? Progress,
    BrowserPackageChangesRow? Row,
    BrowserPackageChangesFailure? Failure);

public sealed record BrowserPackageChangesInspection(
    string ResourcePath,
    BrowserInspectionContentKind ContentKind,
    BrowserPackageChangesDocument Content,
    BrowserInspectionPortableProjection PortableProjection,
    BrowserInspectionDiagnostic[] Diagnostics);

[JsonConverter(typeof(JsonStringEnumConverter<BrowserPackageChangesResultKind>))]
public enum BrowserPackageChangesResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserPackageChangesOperationFailureKind>))]
public enum BrowserPackageChangesOperationFailureKind
{
    Expected,
    Unexpected,
}

public sealed record BrowserPackageChangesResult(
    int Version,
    BrowserPackageChangesResultKind Kind,
    BrowserPackageChangesInspection? Inspection,
    BrowserPackageChangesOperationFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserPackageChangesResult From(
        BrowserManagedOperationResult<
            BrowserPackageChangesInspection,
            string,
            string> result) =>
        result switch
        {
            BrowserManagedOperationResult<
                BrowserPackageChangesInspection,
                string,
                string>.Succeeded succeeded =>
                new(
                    1,
                    BrowserPackageChangesResultKind.Succeeded,
                    succeeded.Value,
                    null,
                    null,
                    null,
                    null),
            BrowserManagedOperationResult<
                BrowserPackageChangesInspection,
                string,
                string>.Failed failed =>
                new(
                    1,
                    BrowserPackageChangesResultKind.Failed,
                    null,
                    failed.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected =>
                            BrowserPackageChangesOperationFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected =>
                            BrowserPackageChangesOperationFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(nameof(result)),
                    },
                    failed.Error,
                    failed.Diagnostic,
                    null),
            BrowserManagedOperationResult<
                BrowserPackageChangesInspection,
                string,
                string>.Canceled canceled =>
                new(
                    1,
                    BrowserPackageChangesResultKind.Canceled,
                    null,
                    null,
                    null,
                    null,
                    BrowserManagedOperationCancelReasons.Format(canceled.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

[JsonConverter(
    typeof(JsonStringEnumConverter<BrowserPackageChangesCancellationKind>))]
public enum BrowserPackageChangesCancellationKind
{
    Requested,
    AlreadyRequested,
    NotActive,
}

public sealed record BrowserPackageChangesCancellation(
    BrowserPackageChangesCancellationKind Kind,
    string? Reason)
{
    internal static BrowserPackageChangesCancellation From(
        BrowserManagedCancellationRequestResult result) =>
        result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new(
                    BrowserPackageChangesCancellationKind.Requested,
                    BrowserManagedOperationCancelReasons.Format(requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested requested =>
                new(
                    BrowserPackageChangesCancellationKind.AlreadyRequested,
                    BrowserManagedOperationCancelReasons.Format(requested.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new(BrowserPackageChangesCancellationKind.NotActive, null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

[JsonSerializable(typeof(BrowserPackageChangesRequest))]
[JsonSerializable(typeof(BrowserPackageChangesPackageSetCatalog))]
[JsonSerializable(typeof(BrowserPackageChangesEvent))]
[JsonSerializable(typeof(BrowserPackageChangesResult))]
[JsonSerializable(typeof(BrowserPackageChangesCancellation))]
internal sealed partial class BrowserPackageJsonContext;
