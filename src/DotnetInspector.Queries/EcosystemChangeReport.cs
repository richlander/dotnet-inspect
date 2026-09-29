using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>The owner-issued package membership used by one ecosystem report.</summary>
public abstract record EcosystemChangePackageSelection
{
    private protected EcosystemChangePackageSelection()
    {
    }

    /// <summary>
    /// An ordered set of literal package-ID prefixes, optionally identified by
    /// the owner-issued selection (such as an Ecosystem) that recorded them.
    /// </summary>
    public sealed record PackagePrefix : EcosystemChangePackageSelection
    {
        /// <summary>The most prefixes one report may select.</summary>
        public const int MaximumPrefixes = 16;

        public PackagePrefix(PackagePrefixDeclaration prefix)
            : this(selectionId: null, [prefix])
        {
        }

        public PackagePrefix(
            string? selectionId,
            IEnumerable<PackagePrefixDeclaration> prefixes)
        {
            ArgumentNullException.ThrowIfNull(prefixes);
            if (selectionId is not null
                && (selectionId.Length is 0 or > 128
                    || !InertString.IsPermitted(TextPolicy.Field, selectionId)))
            {
                throw new ArgumentException(
                    "A package-prefix selection identity must be a permitted field of at most 128 characters.",
                    nameof(selectionId));
            }

            var builder = ImmutableArray.CreateBuilder<PackagePrefixDeclaration>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackagePrefixDeclaration prefix in prefixes)
            {
                ArgumentNullException.ThrowIfNull(prefix);
                if (!seen.Add(prefix.Prefix))
                {
                    throw new ArgumentException(
                        $"Package prefix '{prefix.Prefix}' is selected more than once.",
                        nameof(prefixes));
                }
                if (builder.Count == MaximumPrefixes)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(prefixes),
                        $"A package-prefix selection cannot exceed {MaximumPrefixes} prefixes.");
                }
                builder.Add(prefix);
            }

            if (builder.Count == 0)
            {
                throw new ArgumentException(
                    "A package-prefix selection must contain at least one prefix.",
                    nameof(prefixes));
            }

            SelectionId = selectionId;
            Prefixes = builder.ToImmutable();
        }

        /// <summary>Gets the owner-issued selection identity, when one recorded the prefixes.</summary>
        public string? SelectionId { get; }

        /// <summary>Gets the literal prefixes in recorded order.</summary>
        public ImmutableArray<PackagePrefixDeclaration> Prefixes { get; }

        internal bool Matches(string packageId)
        {
            foreach (PackagePrefixDeclaration prefix in Prefixes)
            {
                if (packageId.StartsWith(
                        prefix.Prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>Which activity rows one ecosystem report admits.</summary>
public enum EcosystemChangeSecuritySelection
{
    /// <summary>Return activity whether or not security evidence is positive.</summary>
    AllActivity,

    /// <summary>Return only current-affected or evidenced security-release activity.</summary>
    SecurityRelevant,
}

/// <summary>Caller policy for one ecosystem change report.</summary>
public sealed record EcosystemChangeReportRequest
{
    public const int DefaultMaximumRows = 100;
    public const int DefaultMaximumCandidateEvents =
        GitHubNuGetAdvisoryOptions.MaximumCoordinates;
    public const int DefaultMaximumReceiptRequests = 100;

    public EcosystemChangeReportRequest(
        EcosystemChangePackageSelection packageSelection,
        NuGetCatalogRequest? interval = null,
        EcosystemChangeSecuritySelection securitySelection =
            EcosystemChangeSecuritySelection.AllActivity,
        int maximumRows = DefaultMaximumRows,
        int maximumCandidateEvents = DefaultMaximumCandidateEvents,
        int maximumReceiptRequests = DefaultMaximumReceiptRequests)
    {
        ArgumentNullException.ThrowIfNull(packageSelection);
        if (!Enum.IsDefined(securitySelection))
        {
            throw new ArgumentOutOfRangeException(
                nameof(securitySelection),
                securitySelection,
                null);
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumCandidateEvents);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumReceiptRequests);
        if (maximumCandidateEvents
            > GitHubNuGetAdvisoryOptions.MaximumCoordinates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCandidateEvents),
                maximumCandidateEvents,
                $"The candidate bound cannot exceed {GitHubNuGetAdvisoryOptions.MaximumCoordinates}.");
        }
        if (maximumRows > maximumCandidateEvents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRows),
                maximumRows,
                "The result bound cannot exceed the candidate-event bound.");
        }
        if (maximumReceiptRequests > maximumCandidateEvents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumReceiptRequests),
                maximumReceiptRequests,
                "The receipt-request bound cannot exceed the candidate-event bound.");
        }

        PackageSelection = packageSelection;
        Interval = interval;
        SecuritySelection = securitySelection;
        MaximumRows = maximumRows;
        MaximumCandidateEvents = maximumCandidateEvents;
        MaximumReceiptRequests = maximumReceiptRequests;
    }

    public EcosystemChangePackageSelection PackageSelection { get; }
    public NuGetCatalogRequest? Interval { get; }
    public EcosystemChangeSecuritySelection SecuritySelection { get; }
    public int MaximumRows { get; }
    public int MaximumCandidateEvents { get; }
    public int MaximumReceiptRequests { get; }
}

/// <summary>One request after its UTC reference and interval are frozen.</summary>
public sealed class EcosystemChangeReportPlan
{
    private EcosystemChangeReportPlan(
        EcosystemChangeReportRequest request,
        DateTimeOffset referenceTime,
        NuGetCatalogRequest interval,
        bool usedDefaultInterval)
    {
        Request = request;
        ReferenceTime = referenceTime;
        Interval = interval;
        UsedDefaultInterval = usedDefaultInterval;
    }

    public EcosystemChangeReportRequest Request { get; }
    public DateTimeOffset ReferenceTime { get; }
    public NuGetCatalogRequest Interval { get; }
    public bool UsedDefaultInterval { get; }

    /// <summary>Freezes one request using the supplied clock.</summary>
    public static EcosystemChangeReportPlan Resolve(
        EcosystemChangeReportRequest request,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset referenceTime =
            (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime();
        bool usedDefaultInterval = request.Interval is null;
        NuGetCatalogRequest interval = request.Interval
            ?? new NuGetCatalogRequest(
                referenceTime - NuGetCatalogRequest.MaximumInterval,
                referenceTime);
        return new(
            request,
            referenceTime,
            interval,
            usedDefaultInterval);
    }
}

/// <summary>Security-release evaluation for one activity row.</summary>
public enum EcosystemSecurityReleaseStatus
{
    CheckedNoFixedVersionAssociation,
    FixedVersionEvidenceUnavailable,
    EvidencedInInterval,
    ReceiptOutsideInterval,
    DeleteActivityUnevaluable,
    ReceiptFailure,
    ReceiptLimitReached,
}

/// <summary>One category-qualified advisory result retained on a report row.</summary>
public sealed class EcosystemChangeAdvisoryEvidence
{
    internal EcosystemChangeAdvisoryEvidence(
        GitHubNuGetAdvisoryAvailability availability,
        IReadOnlyList<GitHubNuGetAdvisoryReference> advisories)
    {
        Availability = availability;
        Advisories = ImmutableArray.CreateRange(advisories);
    }

    public GitHubNuGetAdvisoryAvailability Availability { get; }
    public IReadOnlyList<GitHubNuGetAdvisoryReference> Advisories { get; }
}

/// <summary>Positive exact fixed-version and package-receipt evidence.</summary>
public sealed class EcosystemSecurityReleaseEvidence
{
    internal EcosystemSecurityReleaseEvidence(
        NuGetCatalogPackageReceipt receipt,
        IReadOnlyList<GitHubNuGetAdvisoryReference> advisories)
    {
        Receipt = receipt;
        Advisories = ImmutableArray.CreateRange(advisories);
    }

    public NuGetCatalogPackageReceipt Receipt { get; }
    public IReadOnlyList<GitHubNuGetAdvisoryReference> Advisories { get; }
}

/// <summary>The report-owned meaning of one Catalog activity observation.</summary>
public enum EcosystemChangeActivityKind
{
    SnapshotObserved,
    DeletionObserved,
}

/// <summary>One selected Catalog activity observation and its security overlay.</summary>
public sealed class EcosystemChangeReportRow
{
    internal EcosystemChangeReportRow(
        PackageSourceResultIdentity source,
        NuGetCatalogEvent catalogEvent,
        EcosystemChangeAdvisoryEvidence currentAdvisoryContext,
        EcosystemChangeAdvisoryEvidence fixedVersionEvidence,
        NuGetCatalogPackageReceipt? packageReceipt,
        EcosystemSecurityReleaseStatus securityReleaseStatus,
        EcosystemSecurityReleaseEvidence? securityRelease)
    {
        Source = source;
        CatalogEvent = catalogEvent;
        Activity = catalogEvent.Kind switch
        {
            NuGetCatalogEventKind.Details =>
                EcosystemChangeActivityKind.SnapshotObserved,
            NuGetCatalogEventKind.Delete =>
                EcosystemChangeActivityKind.DeletionObserved,
            _ => throw new InvalidOperationException(
                $"Unsupported Catalog event kind {catalogEvent.Kind}."),
        };
        PackageId = new InertString(
            TextPolicy.Field,
            catalogEvent.PackageId,
            PackageCoordinateResolver.MaxPackageIdLength);
        Version = new InertString(
            TextPolicy.Field,
            catalogEvent.Version,
            256);
        CurrentAdvisoryContext = currentAdvisoryContext;
        FixedVersionEvidence = fixedVersionEvidence;
        PackageReceipt = packageReceipt;
        SecurityReleaseStatus = securityReleaseStatus;
        SecurityRelease = securityRelease;
    }

    public PackageSourceResultIdentity Source { get; }
    public NuGetCatalogEvent CatalogEvent { get; }
    public EcosystemChangeActivityKind Activity { get; }
    public InertString PackageId { get; }
    public InertString Version { get; }
    public EcosystemChangeAdvisoryEvidence CurrentAdvisoryContext { get; }
    public EcosystemChangeAdvisoryEvidence FixedVersionEvidence { get; }
    public NuGetCatalogPackageReceipt? PackageReceipt { get; }
    public EcosystemSecurityReleaseStatus SecurityReleaseStatus { get; }
    public EcosystemSecurityReleaseEvidence? SecurityRelease { get; }

    public bool IsSecurityRelevant =>
        CurrentAdvisoryContext.Advisories.Count > 0
        || SecurityRelease is not null;
}

/// <summary>A Package Details receipt lookup that did not produce evidence.</summary>
public sealed record EcosystemChangeReceiptFailure(
    NuGetCatalogEvent CatalogEvent,
    PackageSourceFailure Failure);

/// <summary>Overall terminal state after preserving detailed provider outcomes.</summary>
public enum EcosystemChangeReportCompletionKind
{
    Complete,
    ResultLimitReached,
    Partial,
    Failed,
}

/// <summary>Resource-free terminal coverage and work accounting.</summary>
public sealed class EcosystemChangeReportSummary
{
    internal EcosystemChangeReportSummary(
        EcosystemChangeReportPlan plan,
        PackageSourceResultIdentity source,
        DateTimeOffset? capturedHorizon,
        NuGetCatalogCompletion? catalogCompletion,
        PackageSourceFailure? catalogFailure,
        int catalogPagesAcquired,
        int catalogHttpAttempts,
        long catalogDecodedBytes,
        long catalogInWindowEventCount,
        long matchingEventCount,
        int retainedEventCount,
        GitHubNuGetAdvisoryAcquisition advisoryEvidence,
        int receiptCandidates,
        int receiptRequests,
        int receiptSuccesses,
        ImmutableArray<EcosystemChangeReceiptFailure> receiptFailures,
        bool receiptLimitReached,
        int currentContextUnevaluableRows,
        int securityReleaseUnevaluableRows,
        int eligibleRowCount,
        int returnedRowCount,
        bool resultLimitReached,
        EcosystemChangeReportCompletionKind completion)
    {
        Plan = plan;
        Source = source;
        CapturedHorizon = capturedHorizon;
        CatalogCompletion = catalogCompletion;
        CatalogFailure = catalogFailure;
        CatalogPagesAcquired = catalogPagesAcquired;
        CatalogHttpAttempts = catalogHttpAttempts;
        CatalogDecodedBytes = catalogDecodedBytes;
        CatalogInWindowEventCount = catalogInWindowEventCount;
        MatchingEventCount = matchingEventCount;
        RetainedEventCount = retainedEventCount;
        AdvisoryEvidence = advisoryEvidence;
        ReceiptCandidates = receiptCandidates;
        ReceiptRequests = receiptRequests;
        ReceiptSuccesses = receiptSuccesses;
        ReceiptFailures = receiptFailures;
        ReceiptLimitReached = receiptLimitReached;
        CurrentContextUnevaluableRows = currentContextUnevaluableRows;
        SecurityReleaseUnevaluableRows = securityReleaseUnevaluableRows;
        EligibleRowCount = eligibleRowCount;
        ReturnedRowCount = returnedRowCount;
        ResultLimitReached = resultLimitReached;
        Completion = completion;
    }

    public EcosystemChangeReportPlan Plan { get; }
    public PackageSourceResultIdentity Source { get; }
    public DateTimeOffset? CapturedHorizon { get; }
    public NuGetCatalogCompletion? CatalogCompletion { get; }
    public PackageSourceFailure? CatalogFailure { get; }
    public int CatalogPagesAcquired { get; }
    public int CatalogHttpAttempts { get; }
    public long CatalogDecodedBytes { get; }
    public long CatalogInWindowEventCount { get; }
    public long MatchingEventCount { get; }
    public int RetainedEventCount { get; }
    public bool CandidateLimitReached =>
        MatchingEventCount > RetainedEventCount;
    public GitHubNuGetAdvisoryAcquisition AdvisoryEvidence { get; }
    public int ReceiptCandidates { get; }
    public int ReceiptRequests { get; }
    public int ReceiptSuccesses { get; }
    public IReadOnlyList<EcosystemChangeReceiptFailure> ReceiptFailures { get; }
    public bool ReceiptLimitReached { get; }
    public int CurrentContextUnevaluableRows { get; }
    public int SecurityReleaseUnevaluableRows { get; }
    public int EligibleRowCount { get; }
    public int ReturnedRowCount { get; }
    public bool ResultLimitReached { get; }
    public EcosystemChangeReportCompletionKind Completion { get; }
}

public enum EcosystemChangeReportProgressPhase
{
    Catalog,
    Advisory,
    PackageReceipt,
}

/// <summary>One bounded-work progress observation.</summary>
public sealed record EcosystemChangeReportProgress(
    EcosystemChangeReportProgressPhase Phase,
    long Completed,
    long? Total,
    DateTimeOffset? CapturedHorizon = null,
    int CatalogPagesAcquired = 0,
    int CatalogHttpAttempts = 0,
    long CatalogDecodedBytes = 0);

/// <summary>Closed event stream for one ecosystem report attempt.</summary>
public abstract record EcosystemChangeReportEvent
{
    private protected EcosystemChangeReportEvent()
    {
    }

    public sealed record Progress(EcosystemChangeReportProgress Value)
        : EcosystemChangeReportEvent;

    public sealed record Row(EcosystemChangeReportRow Value)
        : EcosystemChangeReportEvent;

    public abstract record Failure : EcosystemChangeReportEvent
    {
        private protected Failure()
        {
        }

        public sealed record Catalog(PackageSourceFailure Value) : Failure;

        public sealed record Advisory(GitHubNuGetAdvisoryFailureKind Kind)
            : Failure;

        public sealed record PackageReceipt(EcosystemChangeReceiptFailure Value)
            : Failure;
    }

    public sealed record Completed(EcosystemChangeReportSummary Summary)
        : EcosystemChangeReportEvent;
}
