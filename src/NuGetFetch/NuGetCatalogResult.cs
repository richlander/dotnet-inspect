using System.Collections.Immutable;

namespace NuGetFetch;

/// <summary>
/// One explicit UTC NuGet Catalog interval, exclusive at the start and
/// inclusive at the end.
/// </summary>
public sealed record NuGetCatalogRequest
{
    /// <summary>The largest interval accepted by Catalog acquisition.</summary>
    public static TimeSpan MaximumInterval { get; } =
        TimeSpan.FromDays(42);

    public NuGetCatalogRequest(
        DateTimeOffset fromExclusive,
        DateTimeOffset throughInclusive)
    {
        if (fromExclusive.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The Catalog lower bound must be UTC.",
                nameof(fromExclusive));
        }

        if (throughInclusive.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The Catalog upper bound must be UTC.",
                nameof(throughInclusive));
        }

        if (throughInclusive <= fromExclusive)
        {
            throw new ArgumentException(
                "The Catalog upper bound must be later than the lower bound.",
                nameof(throughInclusive));
        }

        if (throughInclusive - fromExclusive > MaximumInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(throughInclusive),
                throughInclusive,
                $"A Catalog interval cannot exceed {MaximumInterval}.");
        }

        FromExclusive = fromExclusive;
        ThroughInclusive = throughInclusive;
    }

    /// <summary>Gets the exclusive UTC lower bound.</summary>
    public DateTimeOffset FromExclusive { get; }

    /// <summary>Gets the inclusive UTC upper bound.</summary>
    public DateTimeOffset ThroughInclusive { get; }
}

/// <summary>The closed source-issued kind of one Catalog event.</summary>
public enum NuGetCatalogEventKind
{
    /// <summary>A <c>nuget:PackageDetails</c> activity observation.</summary>
    Details,

    /// <summary>A <c>nuget:PackageDelete</c> activity observation.</summary>
    Delete,
}

/// <summary>Why a bounded Catalog acquisition completed.</summary>
public enum NuGetCatalogCompletion
{
    /// <summary>The source horizon covers the request and all selected pages were admitted.</summary>
    WindowExhausted,

    /// <summary>All pages through the captured horizon were admitted, but the horizon trails the request.</summary>
    SourceHorizonReached,

    /// <summary>The configured Catalog page bound stopped acquisition.</summary>
    PageLimitReached,

    /// <summary>The configured Catalog HTTP-attempt bound stopped acquisition.</summary>
    RequestLimitReached,

    /// <summary>The configured aggregate decoded-byte bound stopped acquisition.</summary>
    DecodedByteLimitReached,
}

/// <summary>The optional NuGet V3 Catalog capability of a package source.</summary>
public interface INuGetCatalogPackageSourceClient : IPackageSourceClient
{
    /// <summary>
    /// Acquires backpressured page outcomes for one explicit Catalog interval.
    /// </summary>
    IAsyncEnumerable<PackageSourceOperationResult<NuGetCatalogPage>>
        AcquireCatalogAsync(
            NuGetCatalogRequest request,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null);

    /// <summary>
    /// Acquires the source-issued first-receipt timestamp for one Package
    /// Details event.
    /// </summary>
    Task<PackageSourceOperationResult<NuGetCatalogPackageReceipt>>
        GetPackageReceiptAsync(
            NuGetCatalogEvent detailsEvent,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null);
}

/// <summary>One validated activity observation from a Catalog page.</summary>
public sealed class NuGetCatalogEvent
{
    private readonly object _issuer;

    internal NuGetCatalogEvent(
        object ownerCapability,
        object issuer,
        PackageSourceCoordinate coordinate,
        string packageId,
        string version,
        string leafUrl,
        string commitId,
        DateTimeOffset commitTimestamp,
        NuGetCatalogEventKind kind)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(leafUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        if (commitTimestamp.Offset != TimeSpan.Zero)
            throw new ArgumentException("A Catalog event timestamp must be UTC.", nameof(commitTimestamp));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);

        _issuer = issuer;
        Coordinate = coordinate;
        PackageId = packageId;
        Version = version;
        LeafUrl = leafUrl;
        CommitId = commitId;
        CommitTimestamp = commitTimestamp;
        Kind = kind;
    }

    public PackageSourceCoordinate Coordinate { get; }
    public string PackageId { get; }
    public string Version { get; }
    public string LeafUrl { get; }
    public string CommitId { get; }
    public DateTimeOffset CommitTimestamp { get; }
    public NuGetCatalogEventKind Kind { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>
/// One immutable Catalog page outcome with cumulative acquisition progress.
/// The terminal marker for an empty interval or a crossed acquisition bound
/// has an empty <see cref="Events"/> collection.
/// </summary>
public sealed class NuGetCatalogPage
{
    private readonly object _issuer;

    internal NuGetCatalogPage(
        object ownerCapability,
        object issuer,
        PackageSourceResultIdentity source,
        NuGetCatalogRequest request,
        ImmutableArray<NuGetCatalogEvent> events,
        DateTimeOffset capturedHorizon,
        int pagesAcquired,
        int httpAttempts,
        long decodedBytes,
        long inWindowEventCount,
        NuGetCatalogCompletion? completion)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (events.IsDefault)
            throw new ArgumentException("Catalog events must be initialized.", nameof(events));
        if (capturedHorizon.Offset != TimeSpan.Zero)
            throw new ArgumentException("The Catalog horizon must be UTC.", nameof(capturedHorizon));
        ArgumentOutOfRangeException.ThrowIfNegative(pagesAcquired);
        ArgumentOutOfRangeException.ThrowIfNegative(httpAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(decodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(inWindowEventCount);
        if (inWindowEventCount < events.Length)
            throw new ArgumentOutOfRangeException(nameof(inWindowEventCount));
        if (completion is { } terminal && !Enum.IsDefined(terminal))
            throw new ArgumentOutOfRangeException(nameof(completion), completion, null);

        _issuer = issuer;
        Source = source;
        Request = request;
        Events = events;
        CapturedHorizon = capturedHorizon;
        PagesAcquired = pagesAcquired;
        HttpAttempts = httpAttempts;
        DecodedBytes = decodedBytes;
        InWindowEventCount = inWindowEventCount;
        Completion = completion;
    }

    public PackageSourceResultIdentity Source { get; }
    public NuGetCatalogRequest Request { get; }
    public ImmutableArray<NuGetCatalogEvent> Events { get; }
    public DateTimeOffset CapturedHorizon { get; }
    public int PagesAcquired { get; }
    public int HttpAttempts { get; }
    public long DecodedBytes { get; }
    public long InWindowEventCount { get; }
    public NuGetCatalogCompletion? Completion { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>The source field that supplied a package-receipt timestamp.</summary>
public enum NuGetCatalogPackageReceiptBasis
{
    /// <summary>The Package Details leaf supplied <c>created</c>.</summary>
    Created,

    /// <summary>
    /// The leaf omitted <c>created</c>, so <c>published</c> supplied the
    /// specification-defined fallback.
    /// </summary>
    PublishedFallback,
}

/// <summary>
/// Source-issued first-receipt evidence for one exact Package Details event.
/// </summary>
public sealed class NuGetCatalogPackageReceipt
{
    private readonly object _issuer;

    internal NuGetCatalogPackageReceipt(
        object ownerCapability,
        object issuer,
        PackageSourceResultIdentity source,
        NuGetCatalogEvent detailsEvent,
        DateTimeOffset receivedAt,
        NuGetCatalogPackageReceiptBasis basis)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(detailsEvent);
        if (receivedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A Catalog package-receipt timestamp must be UTC.",
                nameof(receivedAt));
        }
        if (!Enum.IsDefined(basis))
            throw new ArgumentOutOfRangeException(nameof(basis), basis, null);

        _issuer = issuer;
        Source = source;
        DetailsEvent = detailsEvent;
        ReceivedAt = receivedAt;
        Basis = basis;
    }

    public PackageSourceResultIdentity Source { get; }
    public NuGetCatalogEvent DetailsEvent { get; }
    public DateTimeOffset ReceivedAt { get; }
    public NuGetCatalogPackageReceiptBasis Basis { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

public sealed partial class PackageSourceResultFactory
{
    internal NuGetCatalogEvent CatalogEvent(
        PackageSourceCoordinate coordinate,
        string packageId,
        string version,
        string leafUrl,
        string commitId,
        DateTimeOffset commitTimestamp,
        NuGetCatalogEventKind kind) =>
        new(
            _ownerCapability,
            _issuer,
            coordinate,
            packageId,
            version,
            leafUrl,
            commitId,
            commitTimestamp,
            kind);

    internal NuGetCatalogPage CatalogPage(
        NuGetCatalogRequest request,
        ImmutableArray<NuGetCatalogEvent> events,
        DateTimeOffset capturedHorizon,
        int pagesAcquired,
        int httpAttempts,
        long decodedBytes,
        long inWindowEventCount,
        NuGetCatalogCompletion? completion,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.ThrowIfExpired();
        foreach (NuGetCatalogEvent item in events)
        {
            operation.ThrowIfExpired();
            if (item is null || !item.HasIssuer(_issuer))
                throw ContractViolation();
        }

        var value = new NuGetCatalogPage(
            _ownerCapability,
            _issuer,
            Source,
            request,
            events,
            capturedHorizon,
            pagesAcquired,
            httpAttempts,
            decodedBytes,
            inWindowEventCount,
            completion);
        operation.ThrowIfExpired();
        return value;
    }

    internal void ValidateCatalogDetailsEvent(
        NuGetCatalogEvent detailsEvent)
    {
        ArgumentNullException.ThrowIfNull(detailsEvent);
        if (!detailsEvent.HasIssuer(_issuer))
            throw ContractViolation();
        if (detailsEvent.Kind != NuGetCatalogEventKind.Details)
        {
            throw new ArgumentException(
                "Catalog package-receipt evidence requires a Package Details event.",
                nameof(detailsEvent));
        }
    }

    internal NuGetCatalogPackageReceipt CatalogPackageReceipt(
        NuGetCatalogEvent detailsEvent,
        DateTimeOffset receivedAt,
        NuGetCatalogPackageReceiptBasis basis,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.ThrowIfExpired();
        ValidateCatalogDetailsEvent(detailsEvent);
        var value = new NuGetCatalogPackageReceipt(
            _ownerCapability,
            _issuer,
            Source,
            detailsEvent,
            receivedAt,
            basis);
        operation.ThrowIfExpired();
        return value;
    }

    internal PackageSourceOperationResult<NuGetCatalogPage>
        SucceededCatalog(
            NuGetCatalogPage value,
            NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.ThrowIfExpired();
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
        return Succeeded(value);
    }

    internal PackageSourceOperationResult<NuGetCatalogPage>
        FailedCatalog(PackageSourceFailureKind kind) =>
        Failed<NuGetCatalogPage>(
            PackageSourceCapabilities.Catalog,
            coordinate: null,
            ValidateFailureKind(kind, allowNotFound: false));

    internal PackageSourceOperationResult<NuGetCatalogPackageReceipt>
        SucceededCatalogPackageReceipt(
            NuGetCatalogPackageReceipt value,
            NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.ThrowIfExpired();
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
        ValidateCatalogDetailsEvent(value.DetailsEvent);
        return Succeeded(value);
    }

    internal PackageSourceOperationResult<NuGetCatalogPackageReceipt>
        FailedCatalogPackageReceipt(
            PackageSourceCoordinate coordinate,
            PackageSourceFailureKind kind)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return Failed<NuGetCatalogPackageReceipt>(
            PackageSourceCapabilities.Catalog,
            coordinate,
            ValidateFailureKind(kind, allowNotFound: false));
    }
}
