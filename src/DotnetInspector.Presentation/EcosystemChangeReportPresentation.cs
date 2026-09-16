using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Presentation;

/// <summary>The portable kind of package selection used by an ecosystem report.</summary>
public enum EcosystemChangePackageScopeKind
{
    PackageSet,
    PackagePrefix,
}

/// <summary>Portable package-selection evidence for one ecosystem report.</summary>
public sealed record EcosystemChangePackageScopePresentation(
    EcosystemChangePackageScopeKind Kind,
    string? SelectionId,
    string? Prefix,
    ImmutableArray<string> PackageIds);

/// <summary>Portable resolved request metadata for one ecosystem report.</summary>
public sealed record EcosystemChangeReportRequestPresentation(
    DateTimeOffset ReferenceTime,
    DateTimeOffset FromExclusive,
    DateTimeOffset ThroughInclusive,
    bool UsedDefaultInterval,
    EcosystemChangePackageScopePresentation PackageScope,
    EcosystemChangeSecuritySelection SecuritySelection,
    int MaximumRows,
    int MaximumCandidateEvents,
    int MaximumReceiptRequests);

/// <summary>Portable producer and transport identity for one report source.</summary>
public sealed record EcosystemChangeReportSourcePresentation(
    string ProducerKey,
    string Producer,
    PackageSourceKind TransportKind);

/// <summary>Portable Catalog activity identity and time basis.</summary>
public sealed record EcosystemChangeCatalogActivityPresentation(
    string PackageId,
    string Version,
    string NormalizedPackageId,
    string NormalizedVersion,
    string LeafUrl,
    string CommitId,
    DateTimeOffset CommitTimestamp,
    NuGetCatalogEventKind CatalogKind,
    EcosystemChangeActivityKind Activity);

/// <summary>Portable GitHub-reviewed advisory identity and document times.</summary>
public sealed record EcosystemChangeAdvisoryReferencePresentation(
    string GhsaId,
    string? CveId,
    GitHubNuGetAdvisorySeverity Severity,
    string AdvisoryUrl,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Portable availability and references for one advisory category.</summary>
public sealed record EcosystemChangeAdvisoryEvidencePresentation(
    GitHubNuGetAdvisoryAvailability Availability,
    ImmutableArray<EcosystemChangeAdvisoryReferencePresentation> Advisories);

/// <summary>Portable source-issued package-receipt evidence.</summary>
public sealed record EcosystemChangePackageReceiptPresentation(
    DateTimeOffset ReceivedAt,
    NuGetCatalogPackageReceiptBasis Basis);

/// <summary>Portable positive security-release evidence.</summary>
public sealed record EcosystemChangeSecurityReleasePresentation(
    EcosystemChangePackageReceiptPresentation Receipt,
    ImmutableArray<EcosystemChangeAdvisoryReferencePresentation> Advisories);

/// <summary>One portable ecosystem activity row and its security overlay.</summary>
public sealed record EcosystemChangeReportRowPresentation(
    EcosystemChangeCatalogActivityPresentation CatalogActivity,
    EcosystemChangeAdvisoryEvidencePresentation CurrentAdvisoryContext,
    EcosystemChangeAdvisoryEvidencePresentation FixedVersionEvidence,
    EcosystemChangePackageReceiptPresentation? PackageReceipt,
    EcosystemSecurityReleaseStatus SecurityReleaseStatus,
    EcosystemChangeSecurityReleasePresentation? SecurityRelease,
    bool IsSecurityRelevant);

/// <summary>Portable package-source failure details.</summary>
public sealed record EcosystemChangePackageSourceFailurePresentation(
    PackageSourceCapabilities Capability,
    string? PackageId,
    string? Version,
    PackageSourceFailureKind Kind,
    string Detail);

/// <summary>Portable receipt failure associated with one Catalog activity.</summary>
public sealed record EcosystemChangeReceiptFailurePresentation(
    EcosystemChangeCatalogActivityPresentation CatalogActivity,
    EcosystemChangePackageSourceFailurePresentation Failure);

/// <summary>Portable category-qualified advisory evidence for one coordinate.</summary>
public sealed record EcosystemChangeAdvisoryPackagePresentation(
    string PackageId,
    string Version,
    EcosystemChangeAdvisoryEvidencePresentation CurrentAdvisoryContext,
    EcosystemChangeAdvisoryEvidencePresentation FixedVersionEvidence);

/// <summary>Portable bounded advisory-acquisition evidence.</summary>
public sealed record EcosystemChangeAdvisoryAcquisitionPresentation(
    string PackageProducerKey,
    string AdvisoryProducer,
    DateTimeOffset ObservedAt,
    int ApiRequests,
    long ResponseBytes,
    bool Complete,
    ImmutableArray<GitHubNuGetAdvisoryFailureKind> Failures,
    ImmutableArray<EcosystemChangeAdvisoryPackagePresentation> Packages);

/// <summary>Portable bounded-work progress from the query event stream.</summary>
public sealed record EcosystemChangeReportProgressPresentation(
    EcosystemChangeReportProgressPhase Phase,
    long Completed,
    long? Total,
    DateTimeOffset? CapturedHorizon,
    int CatalogPagesAcquired,
    int CatalogHttpAttempts,
    long CatalogDecodedBytes);

/// <summary>Provider that issued one query-stream failure event.</summary>
public enum EcosystemChangeFailureProvider
{
    Catalog,
    Advisory,
    PackageReceipt,
}

/// <summary>One portable failure event from the query event stream.</summary>
public sealed record EcosystemChangeReportFailurePresentation(
    EcosystemChangeFailureProvider Provider,
    EcosystemChangePackageSourceFailurePresentation? CatalogFailure,
    GitHubNuGetAdvisoryFailureKind? AdvisoryFailure,
    EcosystemChangeReceiptFailurePresentation? PackageReceiptFailure);

/// <summary>Portable terminal coverage and work accounting.</summary>
public sealed record EcosystemChangeReportSummaryPresentation(
    DateTimeOffset? CapturedHorizon,
    NuGetCatalogCompletion? CatalogCompletion,
    EcosystemChangePackageSourceFailurePresentation? CatalogFailure,
    int CatalogPagesAcquired,
    int CatalogHttpAttempts,
    long CatalogDecodedBytes,
    long CatalogInWindowEventCount,
    long MatchingEventCount,
    int RetainedEventCount,
    bool CandidateLimitReached,
    EcosystemChangeAdvisoryAcquisitionPresentation AdvisoryEvidence,
    int ReceiptCandidates,
    int ReceiptRequests,
    int ReceiptSuccesses,
    ImmutableArray<EcosystemChangeReceiptFailurePresentation> ReceiptFailures,
    bool ReceiptLimitReached,
    int CurrentContextUnevaluableRows,
    int SecurityReleaseUnevaluableRows,
    int EligibleRowCount,
    int ReturnedRowCount,
    bool ResultLimitReached,
    EcosystemChangeReportCompletionKind Completion);

/// <summary>
/// One complete, resource-free ecosystem report attempt shared by structured
/// serialization and Markout presentation.
/// </summary>
public sealed record EcosystemChangeReportDocument(
    int SchemaVersion,
    EcosystemChangeReportRequestPresentation Request,
    EcosystemChangeReportSourcePresentation Source,
    ImmutableArray<EcosystemChangeReportProgressPresentation> Progress,
    ImmutableArray<EcosystemChangeReportRowPresentation> Rows,
    ImmutableArray<EcosystemChangeReportFailurePresentation> Failures,
    EcosystemChangeReportSummaryPresentation Summary)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>Collects and projects a closed ecosystem-report event stream.</summary>
public static class EcosystemChangeReportPresentation
{
    /// <summary>Collects one asynchronous query attempt into a portable document.</summary>
    public static async Task<EcosystemChangeReportDocument> CollectAsync(
        IAsyncEnumerable<EcosystemChangeReportEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        cancellationToken.ThrowIfCancellationRequested();
        var collected = new List<EcosystemChangeReportEvent>();
        await foreach (EcosystemChangeReportEvent item
            in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            collected.Add(item);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Create(collected);
    }

    /// <summary>Projects one already-collected query attempt.</summary>
    public static EcosystemChangeReportDocument Create(
        IEnumerable<EcosystemChangeReportEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var progress =
            ImmutableArray.CreateBuilder<EcosystemChangeReportProgressPresentation>();
        var rows =
            ImmutableArray.CreateBuilder<EcosystemChangeReportRow>();
        var failures =
            ImmutableArray.CreateBuilder<EcosystemChangeReportEvent.Failure>();
        EcosystemChangeReportSummary? summary = null;

        foreach (EcosystemChangeReportEvent item in events)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (summary is not null)
            {
                throw new InvalidOperationException(
                    "An ecosystem report event appeared after terminal completion.");
            }

            switch (item)
            {
                case EcosystemChangeReportEvent.Progress value:
                    progress.Add(Project(value.Value));
                    break;
                case EcosystemChangeReportEvent.Row value:
                    rows.Add(value.Value);
                    break;
                case EcosystemChangeReportEvent.Failure.Catalog value:
                    failures.Add(value);
                    break;
                case EcosystemChangeReportEvent.Failure.Advisory value:
                    failures.Add(value);
                    break;
                case EcosystemChangeReportEvent.Failure.PackageReceipt value:
                    failures.Add(value);
                    break;
                case EcosystemChangeReportEvent.Completed value:
                    summary = value.Summary;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown ecosystem report event kind.");
            }
        }

        if (summary is null)
        {
            throw new InvalidOperationException(
                "An ecosystem report attempt ended without terminal completion.");
        }
        if (summary.ReturnedRowCount != rows.Count)
        {
            throw new InvalidOperationException(
                "The ecosystem report summary does not describe the returned row population.");
        }
        if (rows.Any(row => row.Source != summary.Source))
        {
            throw new InvalidOperationException(
                "An ecosystem report row does not belong to the terminal source identity.");
        }
        ValidateFailureCorrespondence(failures, summary);

        return new EcosystemChangeReportDocument(
            EcosystemChangeReportDocument.CurrentSchemaVersion,
            Project(summary.Plan),
            Project(summary.Source),
            progress.ToImmutable(),
            [.. rows.Select(Project)],
            [.. failures.Select(Project)],
            Project(summary));
    }

    static void ValidateFailureCorrespondence(
        IEnumerable<EcosystemChangeReportEvent.Failure> failures,
        EcosystemChangeReportSummary summary)
    {
        EcosystemChangeReportEvent.Failure[] streamFailures =
            [.. failures];
        EcosystemChangeReportEvent.Failure.Catalog[] catalogFailures =
            [.. streamFailures.OfType<
                EcosystemChangeReportEvent.Failure.Catalog>()];
        if (summary.CatalogFailure is null)
        {
            if (catalogFailures.Length != 0)
                throw FailureMismatch();
        }
        else
        {
            if (summary.CatalogFailure.Source != summary.Source
                || catalogFailures.Length != 1
                || !ReferenceEquals(
                    catalogFailures[0].Value,
                    summary.CatalogFailure))
            {
                throw FailureMismatch();
            }
        }

        GitHubNuGetAdvisoryFailureKind[] advisoryFailures =
            [.. streamFailures
                .OfType<EcosystemChangeReportEvent.Failure.Advisory>()
                .Select(static failure => failure.Kind)];
        if (!advisoryFailures.SequenceEqual(
                summary.AdvisoryEvidence.Failures))
        {
            throw FailureMismatch();
        }

        EcosystemChangeReceiptFailure[] receiptFailures =
            [.. streamFailures
                .OfType<
                    EcosystemChangeReportEvent.Failure.PackageReceipt>()
                .Select(static failure => failure.Value)];
        if (!receiptFailures.SequenceEqual(summary.ReceiptFailures)
            || receiptFailures.Any(failure =>
                failure.Failure.Source != summary.Source)
            || summary.ReceiptFailures.Any(failure =>
                failure.Failure.Source != summary.Source))
        {
            throw FailureMismatch();
        }

        static InvalidOperationException FailureMismatch() =>
            new(
                "The ecosystem report failure events do not match terminal failure accounting.");
    }

    static EcosystemChangeReportRequestPresentation Project(
        EcosystemChangeReportPlan plan)
    {
        EcosystemChangeReportRequest request = plan.Request;
        return new(
            plan.ReferenceTime,
            plan.Interval.FromExclusive,
            plan.Interval.ThroughInclusive,
            plan.UsedDefaultInterval,
            Project(request.PackageSelection),
            request.SecuritySelection,
            request.MaximumRows,
            request.MaximumCandidateEvents,
            request.MaximumReceiptRequests);
    }

    static EcosystemChangePackageScopePresentation Project(
        EcosystemChangePackageSelection selection) =>
        selection switch
        {
            EcosystemChangePackageSelection.PackageSet value =>
                new(
                    EcosystemChangePackageScopeKind.PackageSet,
                    Inert(value.SelectionId),
                    Prefix: null,
                    [.. value.PackageIds.Select(Inert)]),
            EcosystemChangePackageSelection.PackagePrefix value =>
                new(
                    EcosystemChangePackageScopeKind.PackagePrefix,
                    SelectionId: null,
                    Inert(value.Prefix.Prefix),
                    PackageIds: []),
            _ => throw new InvalidOperationException(
                "Unknown ecosystem package-selection kind."),
        };

    static EcosystemChangeReportSourcePresentation Project(
        PackageSourceResultIdentity source) =>
        new(
            source.Producer.Key,
            source.Producer.Display.ToString(),
            source.TransportKind);

    static EcosystemChangeCatalogActivityPresentation ProjectActivity(
        EcosystemChangeReportRow row) =>
        new(
            row.PackageId.ToString(),
            row.Version.ToString(),
            row.CatalogEvent.Coordinate.PackageId,
            row.CatalogEvent.Coordinate.Version,
            Inert(row.CatalogEvent.LeafUrl),
            Inert(row.CatalogEvent.CommitId),
            row.CatalogEvent.CommitTimestamp,
            row.CatalogEvent.Kind,
            row.Activity);

    static EcosystemChangeCatalogActivityPresentation Project(
        NuGetCatalogEvent value) =>
        new(
            Inert(value.PackageId),
            Inert(value.Version),
            value.Coordinate.PackageId,
            value.Coordinate.Version,
            Inert(value.LeafUrl),
            Inert(value.CommitId),
            value.CommitTimestamp,
            value.Kind,
            value.Kind switch
            {
                NuGetCatalogEventKind.Details =>
                    EcosystemChangeActivityKind.SnapshotObserved,
                NuGetCatalogEventKind.Delete =>
                    EcosystemChangeActivityKind.DeletionObserved,
                _ => throw new InvalidOperationException(
                    "Unknown Catalog event kind."),
            });

    static EcosystemChangeAdvisoryReferencePresentation Project(
        GitHubNuGetAdvisoryReference value) =>
        new(
            Inert(value.GhsaId),
            value.CveId is null ? null : Inert(value.CveId),
            value.Severity,
            Inert(value.AdvisoryUrl.AbsoluteUri),
            value.PublishedAt,
            value.UpdatedAt);

    static EcosystemChangeAdvisoryEvidencePresentation Project(
        EcosystemChangeAdvisoryEvidence value) =>
        new(
            value.Availability,
            [.. value.Advisories.Select(Project)]);

    static EcosystemChangeAdvisoryEvidencePresentation Project(
        GitHubNuGetAdvisoryAvailability availability,
        IReadOnlyList<GitHubNuGetAdvisoryReference> advisories) =>
        new(availability, [.. advisories.Select(Project)]);

    static EcosystemChangePackageReceiptPresentation Project(
        NuGetCatalogPackageReceipt value) =>
        new(value.ReceivedAt, value.Basis);

    static EcosystemChangeSecurityReleasePresentation Project(
        EcosystemSecurityReleaseEvidence value) =>
        new(
            Project(value.Receipt),
            [.. value.Advisories.Select(Project)]);

    internal static EcosystemChangeReportRowPresentation Project(
        EcosystemChangeReportRow value) =>
        new(
            ProjectActivity(value),
            Project(value.CurrentAdvisoryContext),
            Project(value.FixedVersionEvidence),
            value.PackageReceipt is null
                ? null
                : Project(value.PackageReceipt),
            value.SecurityReleaseStatus,
            value.SecurityRelease is null
                ? null
                : Project(value.SecurityRelease),
            value.IsSecurityRelevant);

    static EcosystemChangePackageSourceFailurePresentation Project(
        PackageSourceFailure value) =>
        new(
            value.Capability,
            value.Coordinate?.PackageId,
            value.Coordinate?.Version,
            value.Kind,
            Inert(value.Message));

    static EcosystemChangeReceiptFailurePresentation Project(
        EcosystemChangeReceiptFailure value) =>
        new(Project(value.CatalogEvent), Project(value.Failure));

    static EcosystemChangeAdvisoryAcquisitionPresentation Project(
        GitHubNuGetAdvisoryAcquisition value) =>
        new(
            value.PackageProducer.Key,
            GitHubNuGetAdvisoryAcquisition.AdvisoryProducer.AbsoluteUri,
            value.ObservedAt,
            value.ApiRequests,
            value.ResponseBytes,
            value.Complete,
            [.. value.Failures],
            [.. value.Packages.Select(package =>
                new EcosystemChangeAdvisoryPackagePresentation(
                    package.Coordinate.PackageId,
                    package.Coordinate.Version,
                    Project(
                        package.CurrentContextAvailability,
                        package.CurrentAdvisories),
                    Project(
                        package.FixedVersionAvailability,
                        package.FixedVersionAdvisories)))]);

    internal static EcosystemChangeReportProgressPresentation Project(
        EcosystemChangeReportProgress value) =>
        new(
            value.Phase,
            value.Completed,
            value.Total,
            value.CapturedHorizon,
            value.CatalogPagesAcquired,
            value.CatalogHttpAttempts,
            value.CatalogDecodedBytes);

    internal static EcosystemChangeReportFailurePresentation Project(
        EcosystemChangeReportEvent.Failure value) =>
        value switch
        {
            EcosystemChangeReportEvent.Failure.Catalog failure =>
                new(
                    EcosystemChangeFailureProvider.Catalog,
                    Project(failure.Value),
                    AdvisoryFailure: null,
                    PackageReceiptFailure: null),
            EcosystemChangeReportEvent.Failure.Advisory failure =>
                new(
                    EcosystemChangeFailureProvider.Advisory,
                    CatalogFailure: null,
                    failure.Kind,
                    PackageReceiptFailure: null),
            EcosystemChangeReportEvent.Failure.PackageReceipt failure =>
                new(
                    EcosystemChangeFailureProvider.PackageReceipt,
                    CatalogFailure: null,
                    AdvisoryFailure: null,
                    Project(failure.Value)),
            _ => throw new InvalidOperationException(
                "Unknown ecosystem report failure kind."),
        };

    static EcosystemChangeReportSummaryPresentation Project(
        EcosystemChangeReportSummary value) =>
        new(
            value.CapturedHorizon,
            value.CatalogCompletion,
            value.CatalogFailure is null
                ? null
                : Project(value.CatalogFailure),
            value.CatalogPagesAcquired,
            value.CatalogHttpAttempts,
            value.CatalogDecodedBytes,
            value.CatalogInWindowEventCount,
            value.MatchingEventCount,
            value.RetainedEventCount,
            value.CandidateLimitReached,
            Project(value.AdvisoryEvidence),
            value.ReceiptCandidates,
            value.ReceiptRequests,
            value.ReceiptSuccesses,
            [.. value.ReceiptFailures.Select(Project)],
            value.ReceiptLimitReached,
            value.CurrentContextUnevaluableRows,
            value.SecurityReleaseUnevaluableRows,
            value.EligibleRowCount,
            value.ReturnedRowCount,
            value.ResultLimitReached,
            value.Completion);

    static string Inert(string value) =>
        new InertString(TextPolicy.Field, value).ToString();
}
