using System.Globalization;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Presentation;

/// <summary>Markout document for one completed ecosystem report.</summary>
[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class EcosystemChangeReportView
{
    private EcosystemChangeReportView()
    {
    }

    [MarkoutIgnore, JsonIgnore]
    public string Title { get; private init; } = "";

    [MarkoutIgnore, JsonIgnore]
    public string Description { get; private init; } =
        "Package activity with current advisory context and evidenced security releases.";

    public string Interval { get; private init; } = "";

    [MarkoutPropertyName("Reference time")]
    public string ReferenceTime { get; private init; } = "";

    [MarkoutPropertyName("Interval basis")]
    public string IntervalBasis { get; private init; } = "";

    [MarkoutPropertyName("Security selection")]
    public string SecuritySelection { get; private init; } = "";
    public string Source { get; private init; } = "";

    [MarkoutPropertyName("Source horizon")]
    public string SourceHorizon { get; private init; } = "";

    [MarkoutPropertyName("Advisory observed")]
    public string AdvisoryObserved { get; private init; } = "";

    public string Completion { get; private init; } = "";
    public string Coverage { get; private init; } = "";

    [MarkoutSection(Name = "Package scope")]
    public List<EcosystemChangePackageScopeRowView> PackageScope
        { get; private init; } = [];

    [MarkoutSection(
        Name = "Activity",
        EmptyText = "No matching package activity was returned.")]
    public List<EcosystemChangeActivityRowView> Activity
        { get; private init; } = [];

    [MarkoutSection(
        Name = "Advisory evidence",
        EmptyText = "No positive advisory references were returned.")]
    public List<EcosystemChangeAdvisoryRowView> AdvisoryEvidence
        { get; private init; } = [];

    [MarkoutSection(Name = "Work")]
    public List<EcosystemChangeWorkRowView> Work
        { get; private init; } = [];

    [MarkoutSection(
        Name = "Failures",
        EmptyText = "No provider failures were reported.")]
    public List<EcosystemChangeFailureRowView> Failures
        { get; private init; } = [];

    public static EcosystemChangeReportView Create(
        EcosystemChangeReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EcosystemChangeReportRequestPresentation request = document.Request;
        EcosystemChangeReportSummaryPresentation summary = document.Summary;
        string selection = request.PackageScope.Kind switch
        {
            EcosystemChangePackageScopeKind.PackageSet =>
                request.PackageScope.SelectionId!,
            EcosystemChangePackageScopeKind.PackagePrefix =>
                request.PackageScope.Prefix + "*",
            _ => throw new InvalidOperationException(
                "Unknown package-scope kind."),
        };

        return new EcosystemChangeReportView
        {
            Title = selection,
            Interval =
                $"({Time(request.FromExclusive)}, "
                + $"{Time(request.ThroughInclusive)}]",
            ReferenceTime = Time(request.ReferenceTime),
            IntervalBasis = request.UsedDefaultInterval
                ? "Default 42 days"
                : "Explicit",
            SecuritySelection = SecuritySelectionText(
                request.SecuritySelection),
            Source =
                $"{document.Source.Producer} "
                + $"({document.Source.TransportKind})",
            SourceHorizon = summary.CapturedHorizon is { } horizon
                ? Time(horizon)
                : "Unavailable",
            AdvisoryObserved = Time(summary.AdvisoryEvidence.ObservedAt),
            Completion = CompletionText(summary.Completion),
            Coverage = DescribeCoverage(summary),
            PackageScope = PackageScopeRows(request.PackageScope),
            Activity = [.. document.Rows.Select(ActivityRow)],
            AdvisoryEvidence =
                [.. document.Rows.SelectMany(AdvisoryRows)],
            Work = WorkRows(request, summary),
            Failures = [.. document.Failures.Select(FailureRow)],
        };
    }

    static List<EcosystemChangePackageScopeRowView> PackageScopeRows(
        EcosystemChangePackageScopePresentation scope) =>
        scope.Kind switch
        {
            EcosystemChangePackageScopeKind.PackageSet =>
                [.. scope.PackageIds.Select(package =>
                    new EcosystemChangePackageScopeRowView(
                        "Exact package", package))],
            EcosystemChangePackageScopeKind.PackagePrefix =>
                [new EcosystemChangePackageScopeRowView(
                    "Literal prefix", scope.Prefix!)],
            _ => throw new InvalidOperationException(
                "Unknown package-scope kind."),
        };

    static EcosystemChangeActivityRowView ActivityRow(
        EcosystemChangeReportRowPresentation row)
    {
        EcosystemChangeCatalogActivityPresentation activity =
            row.CatalogActivity;
        return new(
            Time(activity.CommitTimestamp),
            activity.PackageId,
            activity.Version,
            ActivityText(activity.Activity),
            AdvisorySummary(row.CurrentAdvisoryContext),
            AdvisorySummary(row.FixedVersionEvidence),
            SecurityReleaseText(row.SecurityReleaseStatus),
            row.PackageReceipt is { } receipt
                ? Time(receipt.ReceivedAt)
                : null,
            row.PackageReceipt?.Basis.ToString());
    }

    static IEnumerable<EcosystemChangeAdvisoryRowView> AdvisoryRows(
        EcosystemChangeReportRowPresentation row)
    {
        foreach (EcosystemChangeAdvisoryReferencePresentation advisory
            in row.CurrentAdvisoryContext.Advisories)
        {
            yield return AdvisoryRow(
                row,
                "Current advisory context",
                advisory);
        }
        foreach (EcosystemChangeAdvisoryReferencePresentation advisory
            in row.FixedVersionEvidence.Advisories)
        {
            yield return AdvisoryRow(
                row,
                "Exact first patched",
                advisory);
        }
        if (row.SecurityRelease is { } release)
        {
            foreach (EcosystemChangeAdvisoryReferencePresentation advisory
                in release.Advisories)
            {
                yield return AdvisoryRow(
                    row,
                    "Security release",
                    advisory);
            }
        }
    }

    static EcosystemChangeAdvisoryRowView AdvisoryRow(
        EcosystemChangeReportRowPresentation row,
        string category,
        EcosystemChangeAdvisoryReferencePresentation advisory) =>
        new(
            row.CatalogActivity.PackageId,
            row.CatalogActivity.Version,
            category,
            advisory.GhsaId,
            advisory.CveId,
            advisory.Severity.ToString(),
            Time(advisory.PublishedAt),
            Time(advisory.UpdatedAt),
            advisory.AdvisoryUrl);

    static List<EcosystemChangeWorkRowView> WorkRows(
        EcosystemChangeReportRequestPresentation request,
        EcosystemChangeReportSummaryPresentation summary) =>
        [
            new("Catalog pages", summary.CatalogPagesAcquired.ToString(
                CultureInfo.InvariantCulture)),
            new("Catalog HTTP attempts", summary.CatalogHttpAttempts.ToString(
                CultureInfo.InvariantCulture)),
            new("Catalog decoded bytes", summary.CatalogDecodedBytes.ToString(
                CultureInfo.InvariantCulture)),
            new("Catalog in-window events", summary.CatalogInWindowEventCount.ToString(
                CultureInfo.InvariantCulture)),
            new("Scope matches", summary.MatchingEventCount.ToString(
                CultureInfo.InvariantCulture)),
            new("Retained events", summary.RetainedEventCount.ToString(
                CultureInfo.InvariantCulture)),
            new("Candidate event limit", request.MaximumCandidateEvents.ToString(
                CultureInfo.InvariantCulture)),
            new("Candidate limit reached", YesNo(summary.CandidateLimitReached)),
            new("Advisory API requests", summary.AdvisoryEvidence.ApiRequests.ToString(
                CultureInfo.InvariantCulture)),
            new("Advisory response bytes", summary.AdvisoryEvidence.ResponseBytes.ToString(
                CultureInfo.InvariantCulture)),
            new("Advisory coverage complete", YesNo(summary.AdvisoryEvidence.Complete)),
            new("Receipt candidates", summary.ReceiptCandidates.ToString(
                CultureInfo.InvariantCulture)),
            new("Receipt requests", summary.ReceiptRequests.ToString(
                CultureInfo.InvariantCulture)),
            new("Receipt successes", summary.ReceiptSuccesses.ToString(
                CultureInfo.InvariantCulture)),
            new("Receipt request limit", request.MaximumReceiptRequests.ToString(
                CultureInfo.InvariantCulture)),
            new("Receipt limit reached", YesNo(summary.ReceiptLimitReached)),
            new("Current-context unevaluable", summary.CurrentContextUnevaluableRows.ToString(
                CultureInfo.InvariantCulture)),
            new("Security-release unevaluable", summary.SecurityReleaseUnevaluableRows.ToString(
                CultureInfo.InvariantCulture)),
            new("Eligible rows", summary.EligibleRowCount.ToString(
                CultureInfo.InvariantCulture)),
            new("Returned rows", summary.ReturnedRowCount.ToString(
                CultureInfo.InvariantCulture)),
            new("Result row limit", request.MaximumRows.ToString(
                CultureInfo.InvariantCulture)),
            new("Result limit reached", YesNo(summary.ResultLimitReached)),
        ];

    static EcosystemChangeFailureRowView FailureRow(
        EcosystemChangeReportFailurePresentation failure) =>
        failure.Provider switch
        {
            EcosystemChangeFailureProvider.Catalog =>
                new(
                    "Catalog",
                    Subject(failure.CatalogFailure!),
                    failure.CatalogFailure!.Kind.ToString(),
                    failure.CatalogFailure.Detail),
            EcosystemChangeFailureProvider.Advisory =>
                new(
                    "Advisory",
                    "GitHub reviewed advisories",
                    failure.AdvisoryFailure!.Value.ToString(),
                    null),
            EcosystemChangeFailureProvider.PackageReceipt =>
                new(
                    "Package receipt",
                    Coordinate(
                        failure.PackageReceiptFailure!.CatalogActivity),
                    failure.PackageReceiptFailure.Failure.Kind.ToString(),
                    failure.PackageReceiptFailure.Failure.Detail),
            _ => throw new InvalidOperationException(
                "Unknown ecosystem report failure provider."),
        };

    static string AdvisorySummary(
        EcosystemChangeAdvisoryEvidencePresentation evidence)
    {
        string references = string.Join(
            ", ",
            evidence.Advisories.Select(static item => item.GhsaId));
        return references.Length == 0
            ? evidence.Availability.ToString()
            : $"{evidence.Availability}: {references}";
    }

    static string Subject(
        EcosystemChangePackageSourceFailurePresentation failure) =>
        failure.PackageId is null || failure.Version is null
            ? failure.Capability.ToString()
            : $"{failure.PackageId}@{failure.Version}";

    static string Coordinate(
        EcosystemChangeCatalogActivityPresentation activity) =>
        $"{activity.PackageId}@{activity.Version}";

    static string DescribeCoverage(
        EcosystemChangeReportSummaryPresentation summary)
    {
        var reasons = new List<string>();
        if (summary.CatalogFailure is not null)
            reasons.Add("Catalog failed");
        else if (summary.CatalogCompletion is { } catalog
            && catalog != NuGetFetch.NuGetCatalogCompletion.WindowExhausted)
        {
            reasons.Add($"Catalog: {catalog}");
        }
        if (summary.CandidateLimitReached)
            reasons.Add("candidate limit reached");
        if (!summary.AdvisoryEvidence.Complete)
            reasons.Add("advisory evidence incomplete");
        if (!summary.ReceiptFailures.IsEmpty)
            reasons.Add("receipt failures");
        if (summary.ReceiptLimitReached)
            reasons.Add("receipt limit reached");
        if (summary.ResultLimitReached)
            reasons.Add("result limit reached");
        return reasons.Count == 0
            ? "Complete"
            : string.Join("; ", reasons);
    }

    static string Time(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    static string ActivityText(
        EcosystemChangeActivityKind activity) =>
        activity switch
        {
            EcosystemChangeActivityKind.SnapshotObserved =>
                "Snapshot observed",
            EcosystemChangeActivityKind.DeletionObserved =>
                "Deletion observed",
            _ => throw new InvalidOperationException(
                "Unknown ecosystem activity kind."),
        };

    static string SecurityReleaseText(
        EcosystemSecurityReleaseStatus status) =>
        status switch
        {
            EcosystemSecurityReleaseStatus
                .CheckedNoFixedVersionAssociation =>
                "No exact fixed-version association",
            EcosystemSecurityReleaseStatus
                .FixedVersionEvidenceUnavailable =>
                "Fixed-version evidence unavailable",
            EcosystemSecurityReleaseStatus.EvidencedInInterval =>
                "Evidenced in interval",
            EcosystemSecurityReleaseStatus.ReceiptOutsideInterval =>
                "Receipt outside interval",
            EcosystemSecurityReleaseStatus.DeleteActivityUnevaluable =>
                "Delete activity unevaluable",
            EcosystemSecurityReleaseStatus.ReceiptFailure =>
                "Receipt failed",
            EcosystemSecurityReleaseStatus.ReceiptLimitReached =>
                "Receipt limit reached",
            _ => throw new InvalidOperationException(
                "Unknown security-release status."),
        };

    static string SecuritySelectionText(
        EcosystemChangeSecuritySelection selection) =>
        selection switch
        {
            EcosystemChangeSecuritySelection.AllActivity =>
                "All activity",
            EcosystemChangeSecuritySelection.SecurityRelevant =>
                "Security relevant",
            _ => throw new InvalidOperationException(
                "Unknown security selection."),
        };

    static string CompletionText(
        EcosystemChangeReportCompletionKind completion) =>
        completion switch
        {
            EcosystemChangeReportCompletionKind.Complete => "Complete",
            EcosystemChangeReportCompletionKind.ResultLimitReached =>
                "Result limit reached",
            EcosystemChangeReportCompletionKind.Partial => "Partial",
            EcosystemChangeReportCompletionKind.Failed => "Failed",
            _ => throw new InvalidOperationException(
                "Unknown ecosystem report completion."),
        };

    static string YesNo(bool value) => value ? "Yes" : "No";
}

[MarkoutSerializable]
public sealed record EcosystemChangePackageScopeRowView(
    string Kind,
    string Value);

[MarkoutSerializable]
public sealed record EcosystemChangeActivityRowView(
    string Observed,
    string Package,
    string Version,
    string Activity,
    [property: MarkoutPropertyName("Current advisory")] string CurrentAdvisory,
    [property: MarkoutPropertyName("Fixed version")] string FixedVersion,
    [property: MarkoutPropertyName("Security release")] string SecurityRelease,
    string? Receipt,
    [property: MarkoutPropertyName("Receipt basis")] string? ReceiptBasis);

[MarkoutSerializable]
public sealed record EcosystemChangeAdvisoryRowView(
    string Package,
    string Version,
    string Category,
    string Ghsa,
    string? Cve,
    string Severity,
    string Published,
    string Updated,
    string Url);

[MarkoutSerializable]
public sealed record EcosystemChangeWorkRowView(
    string Metric,
    string Value);

[MarkoutSerializable]
public sealed record EcosystemChangeFailureRowView(
    string Provider,
    string Subject,
    string Kind,
    string? Detail);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(EcosystemChangeReportView))]
[MarkoutContext(typeof(EcosystemChangePackageScopeRowView))]
[MarkoutContext(typeof(EcosystemChangeActivityRowView))]
[MarkoutContext(typeof(EcosystemChangeAdvisoryRowView))]
[MarkoutContext(typeof(EcosystemChangeWorkRowView))]
[MarkoutContext(typeof(EcosystemChangeFailureRowView))]
public partial class EcosystemChangeReportViewContext
    : MarkoutSerializerContext;
