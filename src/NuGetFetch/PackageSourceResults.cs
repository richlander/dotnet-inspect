using System.Collections;
using System.Net;
using System.Runtime.CompilerServices;
using InertText;

namespace NuGetFetch;

/// <summary>
/// Identifies how a package coordinate was discovered.
/// </summary>
public enum PackageDiscoveryContract
{
    /// <summary>The coordinate was reported by keyword or identity search.</summary>
    KeywordSearch,

    /// <summary>The coordinate was reported by complete version enumeration.</summary>
    CompleteVersionEnumeration,

    /// <summary>The caller supplied an exact coordinate.</summary>
    ExactCoordinate,
}

/// <summary>
/// Source-relative listing state for a package coordinate.
/// </summary>
public enum PackageListingState
{
    /// <summary>The source authoritatively reports the coordinate as listed.</summary>
    Listed,

    /// <summary>The source authoritatively reports the coordinate as unlisted.</summary>
    Unlisted,

    /// <summary>The source has listing semantics, but the state is not known.</summary>
    Unknown,

    /// <summary>The source does not define a listing-state contract.</summary>
    NotApplicable,
}

/// <summary>
/// A validated, normalized NuGet package coordinate.
/// </summary>
public sealed record PackageSourceCoordinate
{
    private PackageSourceCoordinate(string packageId, string version)
    {
        PackageId = packageId;
        Version = version;
    }

    /// <summary>Gets the normalized lowercase package ID.</summary>
    public string PackageId { get; }

    /// <summary>Gets the normalized lowercase NuGet version.</summary>
    public string Version { get; }

    /// <summary>Creates a validated package coordinate.</summary>
    public static PackageSourceCoordinate Create(
        string packageId,
        string version)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        return new PackageSourceCoordinate(
            packageId.ToLowerInvariant(),
            PackageCoordinateValidation.NormalizeVersion(
                version,
                nameof(version)));
    }
}

/// <summary>
/// Opaque caller-owned association with ordinary object reference identity.
/// </summary>
public sealed class PackageSourceAssociation
{
    private PackageSourceAssociation()
    {
    }

    /// <summary>Creates one source-authority association token.</summary>
    public static PackageSourceAssociation Create() => new();
}

/// <summary>
/// Credential-free package-content producer identity.
/// </summary>
public sealed class PackageProducerIdentity
    : IEquatable<PackageProducerIdentity>
{
    internal PackageProducerIdentity(
        object ownerCapability,
        string key,
        InertString display)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Display = display;
    }

    /// <summary>Gets the opaque, versioned producer key.</summary>
    public string Key { get; }

    /// <summary>Gets the inert diagnostic producer display.</summary>
    public InertString Display { get; }

    /// <summary>Gets the canonical NuGet.org producer.</summary>
    public static PackageProducerIdentity NuGetOrg =>
        PackageSourceClientFactory.NuGetOrgProducer;

    /// <inheritdoc/>
    public bool Equals(PackageProducerIdentity? other) =>
        other is not null
        && Key.Equals(other.Key, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PackageProducerIdentity other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Key);

    public static bool operator ==(
        PackageProducerIdentity? left,
        PackageProducerIdentity? right) =>
        EqualityComparer<PackageProducerIdentity>.Default.Equals(left, right);

    public static bool operator !=(
        PackageProducerIdentity? left,
        PackageProducerIdentity? right) =>
        !(left == right);
}

/// <summary>
/// Complete identity of one source-scoped result.
/// </summary>
public sealed class PackageSourceResultIdentity
    : IEquatable<PackageSourceResultIdentity>
{
    internal PackageSourceResultIdentity(
        object ownerCapability,
        PackageProducerIdentity producer,
        PackageSourceAssociation association,
        PackageSourceKind transportKind)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(association);
        Producer = producer;
        Association = association;
        TransportKind = transportKind;
    }

    /// <summary>Gets the package-content producer.</summary>
    public PackageProducerIdentity Producer { get; }

    /// <summary>Gets the exact caller-created association token.</summary>
    public PackageSourceAssociation Association { get; }

    /// <summary>Gets the transport family that produced the result.</summary>
    public PackageSourceKind TransportKind { get; }

    /// <inheritdoc/>
    public bool Equals(PackageSourceResultIdentity? other) =>
        other is not null
        && Producer.Equals(other.Producer)
        && ReferenceEquals(Association, other.Association)
        && TransportKind == other.TransportKind;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PackageSourceResultIdentity other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Producer.Key, StringComparer.Ordinal);
        hash.Add(RuntimeHelpers.GetHashCode(Association));
        hash.Add(TransportKind);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        PackageSourceResultIdentity? left,
        PackageSourceResultIdentity? right) =>
        EqualityComparer<PackageSourceResultIdentity>.Default.Equals(
            left,
            right);

    public static bool operator !=(
        PackageSourceResultIdentity? left,
        PackageSourceResultIdentity? right) =>
        !(left == right);
}

/// <summary>
/// A durable observation that one source reported one package coordinate.
/// </summary>
public sealed class PackageCandidateObservation
{
    private readonly object _issuer;

    internal PackageCandidateObservation(
        object ownerCapability,
        object issuer,
        PackageSourceCoordinate coordinate,
        PackageSourceResultIdentity source,
        PackageDiscoveryContract discoveryContract,
        PackageListingState listingState)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(source);
        _issuer = issuer;
        Coordinate = coordinate;
        Source = source;
        DiscoveryContract = discoveryContract;
        ListingState = listingState;
    }

    public PackageSourceCoordinate Coordinate { get; }
    public PackageSourceResultIdentity Source { get; }
    public PackageDiscoveryContract DiscoveryContract { get; }
    public PackageListingState ListingState { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>
/// One package search match and its source provenance.
/// </summary>
public sealed class PackageSearchMatch
{
    private readonly object _issuer;

    internal PackageSearchMatch(
        object ownerCapability,
        object issuer,
        SearchResult metadata,
        PackageCandidateObservation candidate)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(candidate);
        _issuer = issuer;
        Metadata = metadata;
        Candidate = candidate;
    }

    public SearchResult Metadata { get; }
    public PackageCandidateObservation Candidate { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>Why a source-scoped package search is incomplete.</summary>
public enum PackageSearchTruncationReason
{
    None,
    RequestedLimit,
    SourcePageLimit,
    ClientPageLimit,
}

/// <summary>
/// Typed result of one source-scoped package search.
/// </summary>
public sealed class PackageSearchResult
{
    private readonly object _issuer;

    internal PackageSearchResult(
        object ownerCapability,
        object issuer,
        PackageSourceResultIdentity source,
        IReadOnlyList<PackageSearchMatch> matches,
        PackageSearchTruncationReason truncationReason)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(matches);
        _issuer = issuer;
        Source = source;
        Matches = matches;
        TruncationReason = truncationReason;
    }

    public PackageSourceResultIdentity Source { get; }
    public IReadOnlyList<PackageSearchMatch> Matches { get; }
    public PackageSearchTruncationReason TruncationReason { get; }
    public bool Truncated =>
        TruncationReason != PackageSearchTruncationReason.None;

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>
/// Typed result of one source-scoped version enumeration.
/// </summary>
public sealed class PackageVersionResult
{
    private readonly object _issuer;

    internal PackageVersionResult(
        object ownerCapability,
        object issuer,
        PackageSourceResultIdentity source,
        IReadOnlyList<PackageCandidateObservation> candidates,
        bool hasAuthoritativeListingState)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);
        _issuer = issuer;
        Source = source;
        Candidates = candidates;
        HasAuthoritativeListingState = hasAuthoritativeListingState;
    }

    public PackageSourceResultIdentity Source { get; }
    public IReadOnlyList<PackageCandidateObservation> Candidates { get; }
    public bool HasAuthoritativeListingState { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

internal static class PackageSourceProjection
{
    public static PackageVersionResult ProjectVersions(
        PackageSourceResultFactory factory,
        string packageId,
        IReadOnlyList<string> versions,
        PackageDiscoveryContract discoveryContract,
        PackageListingState listingState,
        bool hasAuthoritativeListingState,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(versions);
        var candidates =
            new PackageCandidateObservation[versions.Count];
        for (int i = 0; i < versions.Count; i++)
        {
            operation.ThrowIfExpired();
            if (!PackageCoordinateValidation.IsValidPackageVersion(
                    versions[i]))
            {
                throw new NuGetSourceResponseException(
                    "The package version response contained an invalid package version.");
            }

            candidates[i] = factory.Candidate(
                PackageSourceCoordinate.Create(packageId, versions[i]),
                discoveryContract,
                listingState);
        }

        operation.ThrowIfExpired();
        return factory.Versions(
            candidates,
            hasAuthoritativeListingState,
            operation);
    }

    public static PackageSearchResult ProjectSearch(
        PackageSourceResultFactory factory,
        IReadOnlyList<SearchResult> results,
        NuGetOperationDeadline operation,
        PackageSearchTruncationReason truncationReason =
            PackageSearchTruncationReason.None)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(results);
        var snapshot = new SearchResult[results.Count];
        for (int i = 0; i < results.Count; i++)
        {
            operation.ThrowIfExpired();
            snapshot[i] = results[i];
            operation.ThrowIfExpired();
        }
        operation.ThrowIfExpired();
        return factory.Search(snapshot, truncationReason, operation);
    }
}

/// <summary>
/// Immutable copy-out storage for one bounded package manifest.
/// </summary>
public sealed class PackageSourceManifestContent
{
    private readonly byte[] _content;

    internal PackageSourceManifestContent(
        object ownerCapability,
        ReadOnlyMemory<byte> content)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        _content = content.ToArray();
    }

    public int Length => _content.Length;

    public byte this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_content.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _content[index];
        }
    }

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _content.Length)
        {
            throw new ArgumentException(
                "The destination is shorter than the manifest content.",
                nameof(destination));
        }

        _content.AsSpan().CopyTo(destination);
    }

    public byte[] ToArray()
    {
        var copy = new byte[_content.Length];
        _content.CopyTo(copy, 0);
        return copy;
    }
}

/// <summary>
/// One bounded package manifest fetched directly from a package source.
/// </summary>
public sealed class PackageSourceManifest
{
    private readonly object _issuer;

    internal PackageSourceManifest(
        object ownerCapability,
        object issuer,
        PackageSourceCoordinate coordinate,
        PackageSourceResultIdentity source,
        PackageSourceManifestContent content)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        _issuer = issuer;
        Coordinate = coordinate;
        Source = source;
        Content = content;
    }

    public PackageSourceCoordinate Coordinate { get; }
    public PackageSourceResultIdentity Source { get; }
    public PackageSourceManifestContent Content { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>The kind of payload returned by a package source.</summary>
public enum PackageSourcePayloadKind
{
    /// <summary>A NuGet package archive.</summary>
    Package,

    /// <summary>A NuGet symbol-package archive.</summary>
    Symbols,
}

/// <summary>
/// One source-owned payload stream. The caller owns <see cref="Content"/>.
/// </summary>
public sealed class PackageSourcePayload
{
    private readonly object _issuer;

    internal PackageSourcePayload(
        object ownerCapability,
        object issuer,
        PackageSourceCoordinate coordinate,
        PackageSourceResultIdentity source,
        PackageSourcePayloadKind kind,
        Stream content,
        long? advertisedLength)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        if (advertisedLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(advertisedLength));
        }

        _issuer = issuer;
        Coordinate = coordinate;
        Source = source;
        Kind = kind;
        Content = content;
        AdvertisedLength = advertisedLength;
    }

    public PackageSourceCoordinate Coordinate { get; }
    public PackageSourceResultIdentity Source { get; }
    public PackageSourcePayloadKind Kind { get; }
    public Stream Content { get; }
    public long? AdvertisedLength { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>The expected failure classes produced by source operations.</summary>
public enum PackageSourceFailureKind
{
    Unsupported,
    NotFound,
    AuthenticationRequired,
    Timeout,
    InvalidResponse,
    ResponseRejected,
    Transport,
}

/// <summary>The deadline that caused a package-source stream timeout.</summary>
public enum PackageSourceTimeoutKind
{
    Request,
    MetadataBody,
    Operation,
}

/// <summary>Typed details for a package-source stream timeout.</summary>
public sealed record PackageSourceTimeout(
    PackageSourceTimeoutKind Kind,
    TimeSpan Duration);

/// <summary>
/// A source-scoped failure safe to retain without transport URLs or credentials.
/// </summary>
public sealed class PackageSourceFailure
{
    private readonly object _issuer;

    internal PackageSourceFailure(
        object ownerCapability,
        object issuer,
        PackageSourceResultIdentity source,
        PackageSourceCapabilities capability,
        PackageSourceCoordinate? coordinate,
        PackageSourceFailureKind kind,
        string message)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _issuer = issuer;
        Source = source;
        Capability = capability;
        Coordinate = coordinate;
        Kind = kind;
        Message = message;
    }

    public PackageSourceResultIdentity Source { get; }
    public PackageSourceCapabilities Capability { get; }
    public PackageSourceCoordinate? Coordinate { get; }
    public PackageSourceFailureKind Kind { get; }
    public string Message { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>
/// A source-safe failure raised while consuming an already-returned payload.
/// </summary>
public sealed class PackageSourceStreamException : IOException
{
    internal PackageSourceStreamException(
        PackageSourceResultIdentity source,
        PackageSourceFailureKind kind,
        PackageSourceTimeout? timeout,
        bool cleanupFailed)
        : base(MessageFor(kind, timeout, cleanupFailed))
    {
        ArgumentNullException.ThrowIfNull(source);
        ResultSource = source;
        Kind = kind;
        Timeout = timeout;
        CleanupFailed = cleanupFailed;
    }

    public PackageSourceResultIdentity ResultSource { get; }
    public PackageSourceFailureKind Kind { get; }
    public PackageSourceTimeout? Timeout { get; }
    public bool CleanupFailed { get; }

    private static string MessageFor(
        PackageSourceFailureKind kind,
        PackageSourceTimeout? timeout,
        bool cleanupFailed)
    {
        string message = (kind, timeout) switch
        {
            (PackageSourceFailureKind.Timeout,
                { Kind: PackageSourceTimeoutKind.Request }) =>
                $"NuGet payload request did not complete within {timeout.Duration}.",
            (PackageSourceFailureKind.Timeout,
                { Kind: PackageSourceTimeoutKind.Operation }) =>
                $"NuGet payload operation did not complete within {timeout.Duration}.",
            (PackageSourceFailureKind.Timeout, null)
                when cleanupFailed =>
                "The package source payload cleanup timed out.",
            (PackageSourceFailureKind.Timeout, null) =>
                "The package source payload timed out.",
            (PackageSourceFailureKind.Transport, null)
                when cleanupFailed =>
                "The package source payload cleanup failed.",
            (PackageSourceFailureKind.Transport, null) =>
                "The package source payload transport failed.",
            _ => throw new ArgumentException(
                "A payload stream failure must describe a request timeout, operation timeout, or transport failure.",
                nameof(kind)),
        };
        return cleanupFailed
            && kind == PackageSourceFailureKind.Timeout
            && timeout is not null
            ? $"{message} Payload cleanup also failed."
            : message;
    }
}

/// <summary>
/// The typed success or expected source failure of one source operation.
/// </summary>
public sealed class PackageSourceOperationResult<T>
    where T : class
{
    private readonly object _issuer;

    internal PackageSourceOperationResult(
        object ownerCapability,
        object issuer,
        T? value,
        PackageSourceFailure? failure)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        if (typeof(T) != typeof(PackageSearchResult)
            && typeof(T) != typeof(PackageVersionResult)
            && typeof(T) != typeof(PackageSourceManifest)
            && typeof(T) != typeof(PackageSourcePayload))
        {
            throw new InvalidOperationException(
                "This type is not an owner-controlled package source result.");
        }
        ArgumentNullException.ThrowIfNull(issuer);
        if ((value is null) == (failure is null))
        {
            throw new ArgumentException(
                "A source operation result must contain exactly one value or failure.");
        }

        _issuer = issuer;
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }
    public PackageSourceFailure? Failure { get; }

    internal bool HasIssuer(object issuer) =>
        ReferenceEquals(_issuer, issuer);
}

/// <summary>
/// Issues immutable results for one runtime package source.
/// </summary>
public sealed class PackageSourceResultFactory
{
    private readonly object _ownerCapability;
    private readonly object _issuer = new();

    internal PackageSourceResultFactory(
        object ownerCapability,
        PackageSourceResultIdentity source)
    {
        PackageSourceClientFactory.RequireOwnerCapability(ownerCapability);
        ArgumentNullException.ThrowIfNull(source);
        _ownerCapability = ownerCapability;
        Source = source;
    }

    public PackageSourceResultIdentity Source { get; }

    public PackageCandidateObservation Candidate(
        PackageSourceCoordinate coordinate,
        PackageDiscoveryContract discoveryContract,
        PackageListingState listingState)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ValidateEnum(discoveryContract, nameof(discoveryContract));
        ValidateEnum(listingState, nameof(listingState));
        return new PackageCandidateObservation(
            _ownerCapability,
            _issuer,
            coordinate,
            Source,
            discoveryContract,
            listingState);
    }

    public PackageSearchResult Search(
        IReadOnlyList<SearchResult> results,
        PackageSearchTruncationReason truncationReason =
            PackageSearchTruncationReason.None) =>
        SearchCore(
            results,
            truncationReason,
            operation: null);

    internal PackageSearchResult Search(
        IReadOnlyList<SearchResult> results,
        PackageSearchTruncationReason truncationReason,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return SearchCore(results, truncationReason, operation);
    }

    private PackageSearchResult SearchCore(
        IReadOnlyList<SearchResult> results,
        PackageSearchTruncationReason truncationReason,
        NuGetOperationDeadline? operation)
    {
        ArgumentNullException.ThrowIfNull(results);
        operation?.ThrowIfExpired();
        ValidateEnum(truncationReason, nameof(truncationReason));
        operation?.ThrowIfExpired();
        var matches = new PackageSearchMatch[results.Count];
        operation?.ThrowIfExpired();
        for (int i = 0; i < results.Count; i++)
        {
            operation?.ThrowIfExpired();
            SearchResult metadata = SnapshotSearchResult(
                results[i]
                ?? throw new ArgumentException(
                    "Search results cannot contain null entries.",
                    nameof(results)),
                operation);
            operation?.ThrowIfExpired();
            PackageCandidateObservation candidate = Candidate(
                PackageSourceCoordinate.Create(
                    metadata.Id,
                    metadata.Version),
                PackageDiscoveryContract.KeywordSearch,
                PackageListingState.Listed);
            matches[i] = new PackageSearchMatch(
                _ownerCapability,
                _issuer,
                metadata,
                candidate);
            operation?.ThrowIfExpired();
        }

        var result = new PackageSearchResult(
            _ownerCapability,
            _issuer,
            Source,
            new PackageSourceReadOnlyList<PackageSearchMatch>(matches),
            truncationReason);
        operation?.ThrowIfExpired();
        return result;
    }

    public PackageVersionResult Versions(
        IReadOnlyList<PackageCandidateObservation> candidates,
        bool hasAuthoritativeListingState) =>
        VersionsCore(
            candidates,
            hasAuthoritativeListingState,
            operation: null);

    internal PackageVersionResult Versions(
        IReadOnlyList<PackageCandidateObservation> candidates,
        bool hasAuthoritativeListingState,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return VersionsCore(
            candidates,
            hasAuthoritativeListingState,
            operation);
    }

    private PackageVersionResult VersionsCore(
        IReadOnlyList<PackageCandidateObservation> candidates,
        bool hasAuthoritativeListingState,
        NuGetOperationDeadline? operation)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        operation?.ThrowIfExpired();
        int count = candidates.Count;
        operation?.ThrowIfExpired();
        var snapshot =
            new PackageCandidateObservation[count];
        operation?.ThrowIfExpired();
        for (int i = 0; i < snapshot.Length; i++)
        {
            operation?.ThrowIfExpired();
            PackageCandidateObservation candidate =
                candidates[i]
                ?? throw new ArgumentException(
                    "Version candidates cannot contain null entries.",
                    nameof(candidates));
            operation?.ThrowIfExpired();
            ValidateCandidate(candidate);
            operation?.ThrowIfExpired();
            snapshot[i] = candidate;
        }

        var result = new PackageVersionResult(
            _ownerCapability,
            _issuer,
            Source,
            new PackageSourceReadOnlyList<PackageCandidateObservation>(
                snapshot),
            hasAuthoritativeListingState);
        operation?.ThrowIfExpired();
        return result;
    }

    public PackageSourceManifest Manifest(
        PackageSourceCoordinate coordinate,
        ReadOnlyMemory<byte> content)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        var snapshot = new PackageSourceManifestContent(
            _ownerCapability,
            content);
        return new PackageSourceManifest(
            _ownerCapability,
            _issuer,
            coordinate,
            Source,
            snapshot);
    }

    public PackageSourcePayload Payload(
        PackageSourceCoordinate coordinate,
        PackageSourcePayloadKind kind,
        Stream content,
        long? advertisedLength = null)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ValidateEnum(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(content);
        return new PackageSourcePayload(
            _ownerCapability,
            _issuer,
            coordinate,
            Source,
            kind,
            content,
            advertisedLength);
    }

    public PackageSourceOperationResult<PackageSearchResult> SucceededSearch(
        PackageSearchResult value) =>
        SucceededSearchCore(value, operation: null);

    internal PackageSourceOperationResult<PackageSearchResult> SucceededSearch(
        PackageSearchResult value,
        NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return SucceededSearchCore(value, operation);
    }

    private PackageSourceOperationResult<PackageSearchResult>
        SucceededSearchCore(
            PackageSearchResult value,
            NuGetOperationDeadline? operation)
    {
        operation?.ThrowIfExpired();
        ValidateSearch(value, operation);
        operation?.ThrowIfExpired();
        PackageSourceOperationResult<PackageSearchResult> result =
            Succeeded(value);
        operation?.ThrowIfExpired();
        return result;
    }

    public PackageSourceOperationResult<PackageVersionResult> SucceededVersions(
        PackageVersionResult value) =>
        SucceededVersionsCore(value, operation: null);

    internal PackageSourceOperationResult<PackageVersionResult>
        SucceededVersions(
            PackageVersionResult value,
            NuGetOperationDeadline operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return SucceededVersionsCore(value, operation);
    }

    private PackageSourceOperationResult<PackageVersionResult>
        SucceededVersionsCore(
            PackageVersionResult value,
            NuGetOperationDeadline? operation)
    {
        operation?.ThrowIfExpired();
        ValidateVersions(value, operation);
        operation?.ThrowIfExpired();
        PackageSourceOperationResult<PackageVersionResult> result =
            Succeeded(value);
        operation?.ThrowIfExpired();
        return result;
    }

    public PackageSourceOperationResult<PackageSourceManifest>
        SucceededManifest(
            PackageSourceCoordinate requestedCoordinate,
            PackageSourceManifest value)
    {
        ArgumentNullException.ThrowIfNull(requestedCoordinate);
        ValidateManifest(value, requestedCoordinate);
        return Succeeded(value);
    }

    public PackageSourceOperationResult<PackageSourcePayload> SucceededPackage(
        PackageSourceCoordinate requestedCoordinate,
        PackageSourcePayload value)
    {
        ArgumentNullException.ThrowIfNull(requestedCoordinate);
        ValidatePayload(
            value,
            requestedCoordinate,
            PackageSourcePayloadKind.Package);
        return Succeeded(value);
    }

    public PackageSourceOperationResult<PackageSourcePayload> SucceededSymbols(
        PackageSourceCoordinate requestedCoordinate,
        PackageSourcePayload value)
    {
        ArgumentNullException.ThrowIfNull(requestedCoordinate);
        ValidatePayload(
            value,
            requestedCoordinate,
            PackageSourcePayloadKind.Symbols);
        return Succeeded(value);
    }

    public PackageSourceOperationResult<PackageSearchResult> FailedSearch(
        PackageSourceFailureKind kind) =>
        Failed<PackageSearchResult>(
            PackageSourceCapabilities.Search,
            coordinate: null,
            ValidateFailureKind(kind, allowNotFound: false));

    public PackageSourceOperationResult<PackageVersionResult> FailedVersions(
        PackageSourceFailureKind kind) =>
        Failed<PackageVersionResult>(
            PackageSourceCapabilities.VersionEnumeration,
            coordinate: null,
            ValidateFailureKind(kind, allowNotFound: false));

    public PackageSourceOperationResult<PackageSourceManifest> FailedManifest(
        PackageSourceCoordinate coordinate,
        PackageSourceFailureKind kind)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return Failed<PackageSourceManifest>(
            PackageSourceCapabilities.Manifest,
            coordinate,
            ValidateFailureKind(kind, allowNotFound: true));
    }

    public PackageSourceOperationResult<PackageSourcePayload> FailedPackage(
        PackageSourceCoordinate coordinate,
        PackageSourceFailureKind kind)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return Failed<PackageSourcePayload>(
            PackageSourceCapabilities.PackagePayload,
            coordinate,
            ValidateFailureKind(kind, allowNotFound: true));
    }

    public PackageSourceOperationResult<PackageSourcePayload> FailedSymbols(
        PackageSourceCoordinate coordinate,
        PackageSourceFailureKind kind)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return Failed<PackageSourcePayload>(
            PackageSourceCapabilities.SymbolPayload,
            coordinate,
            ValidateFailureKind(kind, allowNotFound: true));
    }

    internal void ValidateSearchOutcome(
        PackageSourceOperationResult<PackageSearchResult> outcome)
    {
        ValidateOutcome(
            outcome,
            value => ValidateSearch(value, operation: null),
            PackageSourceCapabilities.Search,
            coordinate: null,
            payloadKind: null);
    }

    internal void ValidateVersionsOutcome(
        PackageSourceOperationResult<PackageVersionResult> outcome)
    {
        ValidateOutcome(
            outcome,
            value => ValidateVersions(value, operation: null),
            PackageSourceCapabilities.VersionEnumeration,
            coordinate: null,
            payloadKind: null);
    }

    internal void ValidateManifestOutcome(
        PackageSourceOperationResult<PackageSourceManifest> outcome,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ValidateOutcome(
            outcome,
            value => ValidateManifest(value, coordinate),
            PackageSourceCapabilities.Manifest,
            coordinate,
            payloadKind: null);
    }

    internal void ValidatePackageOutcome(
        PackageSourceOperationResult<PackageSourcePayload> outcome,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ValidateOutcome(
            outcome,
            value => ValidatePayload(
                value,
                coordinate,
                PackageSourcePayloadKind.Package),
            PackageSourceCapabilities.PackagePayload,
            coordinate,
            PackageSourcePayloadKind.Package);
    }

    internal void ValidateSymbolsOutcome(
        PackageSourceOperationResult<PackageSourcePayload> outcome,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ValidateOutcome(
            outcome,
            value => ValidatePayload(
                value,
                coordinate,
                PackageSourcePayloadKind.Symbols),
            PackageSourceCapabilities.SymbolPayload,
            coordinate,
            PackageSourcePayloadKind.Symbols);
    }

    private PackageSourceOperationResult<T> Succeeded<T>(T value)
        where T : class =>
        new(
            _ownerCapability,
            _issuer,
            value,
            failure: null);

    private PackageSourceOperationResult<T> Failed<T>(
        PackageSourceCapabilities capability,
        PackageSourceCoordinate? coordinate,
        PackageSourceFailureKind kind)
        where T : class
    {
        var failure = new PackageSourceFailure(
            _ownerCapability,
            _issuer,
            Source,
            capability,
            coordinate,
            kind,
            MessageFor(kind));
        return new PackageSourceOperationResult<T>(
            _ownerCapability,
            _issuer,
            value: null,
            failure);
    }

    private void ValidateSearch(
        PackageSearchResult value,
        NuGetOperationDeadline? operation)
    {
        ArgumentNullException.ThrowIfNull(value);
        operation?.ThrowIfExpired();
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
        foreach (PackageSearchMatch match in value.Matches)
        {
            operation?.ThrowIfExpired();
            if (match is null
                || !match.HasIssuer(_issuer))
            {
                throw ContractViolation();
            }

            ValidateCandidate(match.Candidate);
        }

        operation?.ThrowIfExpired();
    }

    private void ValidateVersions(
        PackageVersionResult value,
        NuGetOperationDeadline? operation)
    {
        ArgumentNullException.ThrowIfNull(value);
        operation?.ThrowIfExpired();
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
        foreach (PackageCandidateObservation candidate in value.Candidates)
        {
            operation?.ThrowIfExpired();
            ValidateCandidate(candidate);
        }

        operation?.ThrowIfExpired();
    }

    private void ValidateCandidate(PackageCandidateObservation value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
    }

    private void ValidateManifest(
        PackageSourceManifest value,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(value);
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
        if (value.Coordinate != coordinate)
            throw ContractViolation();
    }

    private void ValidatePayload(
        PackageSourcePayload value,
        PackageSourceCoordinate coordinate,
        PackageSourcePayloadKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
        if (value.Coordinate != coordinate || value.Kind != kind)
            throw ContractViolation();
    }

    private void ValidateFailure(
        PackageSourceFailure failure,
        PackageSourceCapabilities capability,
        PackageSourceCoordinate? coordinate)
    {
        RequireSourceAndIssuer(
            failure.Source,
            failure.HasIssuer(_issuer));
        if (failure.Capability != capability
            || failure.Coordinate != coordinate)
        {
            throw ContractViolation();
        }

        _ = ValidateFailureKind(
            failure.Kind,
            allowNotFound: coordinate is not null);
        if (!failure.Message.Equals(
                MessageFor(failure.Kind),
                StringComparison.Ordinal))
        {
            throw ContractViolation();
        }
    }

    private void ValidateOutcome<T>(
        PackageSourceOperationResult<T> outcome,
        Action<T> validateValue,
        PackageSourceCapabilities capability,
        PackageSourceCoordinate? coordinate,
        PackageSourcePayloadKind? payloadKind)
        where T : class
    {
        if (outcome is null)
            throw ContractViolation();
        if (!outcome.HasIssuer(_issuer))
            throw ContractViolation();

        if (outcome.Value is { } value)
        {
            if (outcome.Failure is not null)
                throw ContractViolation();
            validateValue(value);
            if (payloadKind is not null
                && value is PackageSourcePayload payload
                && payload.Kind != payloadKind)
            {
                throw ContractViolation();
            }
            return;
        }

        if (outcome.Failure is not { } failure)
            throw ContractViolation();
        ValidateFailure(failure, capability, coordinate);
    }

    private void RequireSourceAndIssuer(
        PackageSourceResultIdentity source,
        bool hasIssuer)
    {
        if (!hasIssuer
            || !ReferenceEquals(source, Source)
            || source != Source)
        {
            throw ContractViolation();
        }
    }

    private static PackageSourceFailureKind ValidateFailureKind(
        PackageSourceFailureKind kind,
        bool allowNotFound)
    {
        ValidateEnum(kind, nameof(kind));
        if (kind == PackageSourceFailureKind.NotFound
            && !allowNotFound)
        {
            throw new ArgumentException(
                "NotFound is valid only for exact-coordinate operations.",
                nameof(kind));
        }

        return kind;
    }

    private static void ValidateEnum<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, value, null);
    }

    private static SearchResult SnapshotSearchResult(
        SearchResult result,
        NuGetOperationDeadline? operation)
    {
        operation?.ThrowIfExpired();
        IReadOnlyList<SearchVersion>? versions = result.Versions is null
            ? null
            : new PackageSourceReadOnlyList<SearchVersion>(
                SnapshotSearchVersions(result.Versions, operation));
        operation?.ThrowIfExpired();
        IReadOnlyList<string>? owners = result.Owners is null
            ? null
            : new PackageSourceReadOnlyList<string>(
                SnapshotStrings(result.Owners, operation));
        operation?.ThrowIfExpired();
        var snapshot = new SearchResult(
            result.Id,
            result.Version,
            result.Description,
            result.TotalDownloads,
            result.Verified,
            versions,
            owners);
        operation?.ThrowIfExpired();
        return snapshot;
    }

    private static SearchVersion[] SnapshotSearchVersions(
        IReadOnlyList<SearchVersion> versions,
        NuGetOperationDeadline? operation)
    {
        operation?.ThrowIfExpired();
        var snapshot = new SearchVersion[versions.Count];
        operation?.ThrowIfExpired();
        for (int i = 0; i < snapshot.Length; i++)
        {
            operation?.ThrowIfExpired();
            SearchVersion version =
                versions[i]
                ?? throw new ArgumentException(
                    "Search versions cannot contain null entries.",
                    nameof(versions));
            snapshot[i] = new SearchVersion(
                version.Version,
                version.Downloads);
            operation?.ThrowIfExpired();
        }
        return snapshot;
    }

    private static string[] SnapshotStrings(
        IReadOnlyList<string> values,
        NuGetOperationDeadline? operation)
    {
        operation?.ThrowIfExpired();
        var snapshot = new string[values.Count];
        operation?.ThrowIfExpired();
        for (int i = 0; i < snapshot.Length; i++)
        {
            operation?.ThrowIfExpired();
            snapshot[i] =
                values[i]
                ?? throw new ArgumentException(
                    "Search owner collections cannot contain null entries.",
                    nameof(values));
            operation?.ThrowIfExpired();
        }
        return snapshot;
    }

    private static string MessageFor(PackageSourceFailureKind kind) =>
        kind switch
        {
            PackageSourceFailureKind.Unsupported =>
                "The package source does not support this operation.",
            PackageSourceFailureKind.NotFound =>
                "The requested payload was not found at the package source.",
            PackageSourceFailureKind.AuthenticationRequired =>
                "The package source requires or rejected authentication.",
            PackageSourceFailureKind.Timeout =>
                "The package source operation exceeded its configured deadline.",
            PackageSourceFailureKind.InvalidResponse =>
                "The package source returned invalid protocol metadata.",
            PackageSourceFailureKind.ResponseRejected =>
                "The package source response exceeded a configured safety bound.",
            PackageSourceFailureKind.Transport =>
                "The package source transport failed.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static InvalidOperationException ContractViolation() =>
        new("The custom package source returned an outcome that was not issued for the bound operation.");
}

internal static class PackageSourceOperation
{
    public static Task<PackageSourceOperationResult<PackageSearchResult>>
        CaptureSearchAsync(
            PackageSourceResultFactory factory,
            Func<Task<PackageSearchResult>> operation,
            CancellationToken cancellationToken,
            NuGetOperationContext? operationContext = null,
            NuGetOperationDeadline? operationDeadline = null) =>
        CaptureAsync(
            operation,
            value => operationDeadline is null
                ? factory.SucceededSearch(value)
                : factory.SucceededSearch(value, operationDeadline),
            factory.FailedSearch,
            allowNotFound: false,
            cancellationToken,
            operationContext);

    public static Task<PackageSourceOperationResult<PackageVersionResult>>
        CaptureVersionsAsync(
            PackageSourceResultFactory factory,
            Func<Task<PackageVersionResult>> operation,
            CancellationToken cancellationToken,
            NuGetOperationContext? operationContext = null,
            NuGetOperationDeadline? operationDeadline = null) =>
        CaptureAsync(
            operation,
            value => operationDeadline is null
                ? factory.SucceededVersions(value)
                : factory.SucceededVersions(value, operationDeadline),
            factory.FailedVersions,
            allowNotFound: false,
            cancellationToken,
            operationContext);

    public static Task<PackageSourceOperationResult<PackageSourceManifest>>
        CaptureManifestAsync(
            PackageSourceResultFactory factory,
            PackageSourceCoordinate coordinate,
            Func<Task<PackageSourceManifest>> operation,
            CancellationToken cancellationToken,
            NuGetOperationContext? operationContext = null) =>
        CaptureAsync(
            operation,
            value => factory.SucceededManifest(coordinate, value),
            kind => factory.FailedManifest(coordinate, kind),
            allowNotFound: true,
            cancellationToken,
            operationContext);

    public static Task<PackageSourceOperationResult<PackageSourcePayload>>
        CapturePackageAsync(
            PackageSourceResultFactory factory,
            PackageSourceCoordinate coordinate,
            Func<Task<PackageSourcePayload>> operation,
            CancellationToken cancellationToken,
            NuGetOperationContext? operationContext = null) =>
        CaptureAsync(
            operation,
            value => factory.SucceededPackage(coordinate, value),
            kind => factory.FailedPackage(coordinate, kind),
            allowNotFound: true,
            cancellationToken,
            operationContext);

    public static Task<PackageSourceOperationResult<PackageSourcePayload>>
        CaptureSymbolsAsync(
            PackageSourceResultFactory factory,
            PackageSourceCoordinate coordinate,
            Func<Task<PackageSourcePayload>> operation,
            CancellationToken cancellationToken,
            NuGetOperationContext? operationContext = null) =>
        CaptureAsync(
            operation,
            value => factory.SucceededSymbols(coordinate, value),
            kind => factory.FailedSymbols(coordinate, kind),
            allowNotFound: true,
            cancellationToken,
            operationContext);

    private static async Task<PackageSourceOperationResult<T>> CaptureAsync<T>(
        Func<Task<T>> operation,
        Func<T, PackageSourceOperationResult<T>> succeeded,
        Func<PackageSourceFailureKind, PackageSourceOperationResult<T>> failed,
        bool allowNotFound,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
        where T : class
    {
        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        try
        {
            return succeeded(await operation().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (TryClassify(
                exception,
                allowNotFound,
                out PackageSourceFailureKind kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return failed(kind);
        }
    }

    private static bool TryClassify(
        Exception exception,
        bool allowNotFound,
        out PackageSourceFailureKind kind)
    {
        kind = exception switch
        {
            NuGetRequestTimeoutException
                or NuGetMetadataBodyTimeoutException
                or NuGetOperationTimeoutException =>
                PackageSourceFailureKind.Timeout,
            _ when NuGetTransportFailure.IsTimeout(exception) =>
                PackageSourceFailureKind.Timeout,
            NuGetMetadataResponseTooLargeException =>
                PackageSourceFailureKind.ResponseRejected,
            NuGetRedirectLimitExceededException
                or NuGetRegistrationResourceLimitExceededException =>
                PackageSourceFailureKind.ResponseRejected,
            NuGetSourceCapabilityUnavailableException =>
                PackageSourceFailureKind.Unsupported,
            NuGetSourceResponseException
                or System.Text.Json.JsonException
                or InvalidDataException =>
                PackageSourceFailureKind.InvalidResponse,
            HttpRequestException
            {
                StatusCode: HttpStatusCode.NotFound,
            } when allowNotFound =>
                PackageSourceFailureKind.NotFound,
            HttpRequestException
            {
                StatusCode: HttpStatusCode.NotFound,
            } =>
                PackageSourceFailureKind.InvalidResponse,
            HttpRequestException
            {
                StatusCode:
                        HttpStatusCode.Unauthorized
                        or HttpStatusCode.Forbidden,
            } =>
                PackageSourceFailureKind.AuthenticationRequired,
            HttpRequestException =>
                PackageSourceFailureKind.Transport,
            OperationCanceledException =>
                PackageSourceFailureKind.Transport,
            IOException =>
                PackageSourceFailureKind.Transport,
            _ => default,
        };

        return exception is
            TimeoutException
            or NuGetMetadataResponseTooLargeException
            or NuGetRedirectLimitExceededException
            or NuGetRegistrationResourceLimitExceededException
            or NuGetSourceCapabilityUnavailableException
            or NuGetSourceResponseException
            or System.Text.Json.JsonException
            or InvalidDataException
            or HttpRequestException
            or OperationCanceledException
            or IOException;
    }
}

file sealed class PackageSourceReadOnlyList<T>(
    T[] items)
    : IReadOnlyList<T>
{
    private readonly T[] _items = items;

    public int Count => _items.Length;
    public T this[int index] => _items[index];

    public IEnumerator<T> GetEnumerator() =>
        ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class NuGetSourceCapabilityUnavailableException()
    : InvalidOperationException(
        "The package source does not advertise the requested capability.");

internal sealed class NuGetSourceResponseException : InvalidOperationException
{
    internal NuGetSourceResponseException(string message)
        : base(message)
    {
    }

    internal NuGetSourceResponseException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
