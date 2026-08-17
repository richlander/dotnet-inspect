using System.Net;
using System.Text.Json;

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
/// A durable observation that one producer reported one package coordinate.
/// </summary>
public sealed record PackageCandidateObservation(
    PackageSourceCoordinate Coordinate,
    PackageSourceIdentity Producer,
    PackageDiscoveryContract DiscoveryContract,
    PackageListingState ListingState);

/// <summary>
/// One package search match and its source provenance.
/// </summary>
public sealed record PackageSearchMatch(
    SearchResult Metadata,
    PackageCandidateObservation Candidate);

/// <summary>
/// Typed result of one source-scoped package search.
/// </summary>
public sealed record PackageSearchResult(
    IReadOnlyList<PackageSearchMatch> Matches);

/// <summary>
/// Typed result of one source-scoped version enumeration.
/// </summary>
public sealed record PackageVersionResult
{
    /// <summary>Creates one source-scoped version result.</summary>
    public PackageVersionResult(
        IReadOnlyList<PackageCandidateObservation> candidates,
        bool hasAuthoritativeListingState)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates = candidates;
        HasAuthoritativeListingState =
            hasAuthoritativeListingState;
    }

    /// <summary>Gets the source-reported package candidates.</summary>
    public IReadOnlyList<PackageCandidateObservation> Candidates { get; }

    /// <summary>
    /// Gets whether the result has authoritative listing information,
    /// including when the candidate set is empty.
    /// </summary>
    public bool HasAuthoritativeListingState { get; }
}

internal static class PackageSourceProjection
{
    public static PackageVersionResult ProjectVersions(
        string packageId,
        IReadOnlyList<string> versions,
        PackageSourceIdentity producer,
        PackageDiscoveryContract discoveryContract,
        PackageListingState listingState,
        bool hasAuthoritativeListingState,
        NuGetOperationDeadline operation)
    {
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

            candidates[i] = new PackageCandidateObservation(
                PackageSourceCoordinate.Create(packageId, versions[i]),
                producer,
                discoveryContract,
                listingState);
        }

        operation.ThrowIfExpired();
        return new PackageVersionResult(
            candidates,
            hasAuthoritativeListingState);
    }

    public static PackageSearchResult ProjectSearch(
        IReadOnlyList<SearchResult> results,
        PackageSourceIdentity producer,
        NuGetOperationDeadline operation)
    {
        var matches = new PackageSearchMatch[results.Count];
        for (int i = 0; i < results.Count; i++)
        {
            operation.ThrowIfExpired();
            SearchResult result = results[i];
            matches[i] = new PackageSearchMatch(
                result,
                new PackageCandidateObservation(
                    PackageSourceCoordinate.Create(
                        result.Id,
                        result.Version),
                    producer,
                    PackageDiscoveryContract.KeywordSearch,
                    PackageListingState.Listed));
        }

        operation.ThrowIfExpired();
        return new PackageSearchResult(matches);
    }
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
/// Failures while consuming an already-returned stream remain bounded transport
/// or timeout exceptions because a result has already been returned.
/// </summary>
public sealed record PackageSourcePayload(
    PackageSourceCoordinate Coordinate,
    PackageSourceIdentity Producer,
    PackageSourceKind TransportKind,
    PackageSourcePayloadKind Kind,
    Stream Content);

/// <summary>The expected failure classes produced by source operations.</summary>
public enum PackageSourceFailureKind
{
    /// <summary>The source does not advertise the requested capability.</summary>
    Unsupported,

    /// <summary>The requested exact payload is absent from the source.</summary>
    NotFound,

    /// <summary>The source rejected or requires authentication.</summary>
    AuthenticationRequired,

    /// <summary>A library-owned request or operation deadline expired.</summary>
    Timeout,

    /// <summary>The source returned malformed or incomplete protocol metadata.</summary>
    InvalidResponse,

    /// <summary>The source response exceeded a configured safety bound.</summary>
    ResponseRejected,

    /// <summary>The source could not be reached or completed by the transport.</summary>
    Transport,
}

/// <summary>
/// A source-scoped failure safe to retain without transport URLs or credentials.
/// </summary>
public sealed record PackageSourceFailure(
    PackageSourceIdentity Producer,
    PackageSourceKind TransportKind,
    PackageSourceCapabilities Capability,
    PackageSourceCoordinate? Coordinate,
    PackageSourceFailureKind Kind,
    string Message);

/// <summary>
/// The typed success or expected source failure of one source operation.
/// </summary>
public abstract record PackageSourceOperationResult<T>
{
    private protected PackageSourceOperationResult()
    {
    }

    /// <summary>The operation completed successfully.</summary>
    public sealed record Succeeded(T Value)
        : PackageSourceOperationResult<T>;

    /// <summary>The source operation failed in an expected, classified way.</summary>
    public sealed record Failed(PackageSourceFailure Failure)
        : PackageSourceOperationResult<T>;
}

internal static class PackageSourceOperation
{
    public static async Task<PackageSourceOperationResult<T>> CaptureAsync<T>(
        PackageSourceIdentity producer,
        PackageSourceKind transportKind,
        PackageSourceCapabilities capability,
        Func<Task<T>> operation,
        CancellationToken cancellationToken,
        PackageSourceCoordinate? coordinate = null)
    {
        try
        {
            return new PackageSourceOperationResult<T>.Succeeded(
                await operation().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (TryClassify(
                exception,
                capability,
                coordinate,
                out PackageSourceFailureKind kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PackageSourceOperationResult<T>.Failed(
                new PackageSourceFailure(
                    producer,
                    transportKind,
                    capability,
                    coordinate,
                    kind,
                    MessageFor(kind)));
        }
    }

    public static PackageSourceOperationResult<T> Unsupported<T>(
        PackageSourceIdentity producer,
        PackageSourceKind transportKind,
        PackageSourceCapabilities capability,
        PackageSourceCoordinate? coordinate = null) =>
        new PackageSourceOperationResult<T>.Failed(
            new PackageSourceFailure(
                producer,
                transportKind,
                capability,
                coordinate,
                PackageSourceFailureKind.Unsupported,
                MessageFor(PackageSourceFailureKind.Unsupported)));

    private static bool TryClassify(
        Exception exception,
        PackageSourceCapabilities capability,
        PackageSourceCoordinate? coordinate,
        out PackageSourceFailureKind kind)
    {
        kind = exception switch
        {
            NuGetRequestTimeoutException
                or NuGetOperationTimeoutException
                or NuGetMetadataBodyTimeoutException =>
                PackageSourceFailureKind.Timeout,
            NuGetMetadataResponseTooLargeException =>
                PackageSourceFailureKind.ResponseRejected,
            NuGetRedirectLimitExceededException =>
                PackageSourceFailureKind.ResponseRejected,
            NuGetSourceResponseException
                or JsonException =>
                PackageSourceFailureKind.InvalidResponse,
            HttpRequestException
            {
                StatusCode: HttpStatusCode.NotFound,
            } when coordinate is not null
                    && capability is
                        PackageSourceCapabilities.PackagePayload
                        or PackageSourceCapabilities.SymbolPayload =>
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
            IOException =>
                PackageSourceFailureKind.Transport,
            _ => default,
        };

        return exception is
            NuGetRequestTimeoutException
            or NuGetOperationTimeoutException
            or NuGetMetadataBodyTimeoutException
            or NuGetMetadataResponseTooLargeException
            or NuGetRedirectLimitExceededException
            or NuGetSourceResponseException
            or JsonException
            or HttpRequestException
            or IOException;
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
}

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
