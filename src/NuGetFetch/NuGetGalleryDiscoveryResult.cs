using System.Collections.Immutable;

namespace NuGetFetch;

/// <summary>The Gallery-specific metadata discovery capability.</summary>
public interface INuGetGalleryPackageSourceClient : IPackageSourceClient
{
    Task<PackageSourceOperationResult<NuGetGalleryDiscoveryResult>> DiscoverAsync(
        NuGetGalleryDiscoveryRequest request,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null);
}

/// <summary>One admitted search-metadata observation, without manifest facts.</summary>
public sealed class NuGetGalleryDiscoveryMatch
{
    internal NuGetGalleryDiscoveryMatch(
        PackageCandidateObservation candidate,
        string packageId,
        string version,
        long? totalDownloads,
        bool? verified,
        ImmutableArray<string> owners,
        string? description)
    {
        Candidate = candidate;
        PackageId = packageId;
        Version = version;
        TotalDownloads = totalDownloads;
        Verified = verified;
        Owners = owners;
        Description = description;
    }

    public PackageCandidateObservation Candidate { get; }
    public string PackageId { get; }
    public string Version { get; }
    public long? TotalDownloads { get; }
    public bool? Verified { get; }
    public ImmutableArray<string> Owners { get; }
    public string? Description { get; }
}

/// <summary>
/// One fully admitted finite Gallery response. Success does not establish
/// exhaustion of the searchable population.
/// </summary>
public sealed class NuGetGalleryDiscoveryResult
{
    private readonly object _issuer;

    internal NuGetGalleryDiscoveryResult(
        object issuer,
        PackageSourceResultIdentity source,
        NuGetGalleryDiscoveryRequest request,
        ImmutableArray<NuGetGalleryDiscoveryMatch> matches,
        long? estimatedTotalHits)
    {
        _issuer = issuer;
        Source = source;
        Request = request;
        Matches = matches;
        EstimatedTotalHits = estimatedTotalHits;
    }

    public PackageSourceResultIdentity Source { get; }
    public NuGetGalleryDiscoveryRequest Request { get; }
    public ImmutableArray<NuGetGalleryDiscoveryMatch> Matches { get; }
    public long? EstimatedTotalHits { get; }

    internal bool HasIssuer(object issuer) => ReferenceEquals(_issuer, issuer);
}

public sealed partial class PackageSourceResultFactory
{
    internal NuGetGalleryDiscoveryResult GalleryDiscovery(
        NuGetGalleryDiscoveryRequest request,
        ImmutableArray<NuGetGalleryDiscoveryMatch> matches,
        long? estimatedTotalHits,
        NuGetOperationDeadline operation)
    {
        operation.ThrowIfExpired();
        foreach (NuGetGalleryDiscoveryMatch match in matches)
        {
            operation.ThrowIfExpired();
            ValidateCandidate(match.Candidate);
        }

        return new NuGetGalleryDiscoveryResult(
            _issuer, Source, request, matches, estimatedTotalHits);
    }

    internal PackageSourceOperationResult<NuGetGalleryDiscoveryResult>
        SucceededGalleryDiscovery(
            NuGetGalleryDiscoveryResult value,
            NuGetOperationDeadline operation)
    {
        operation.ThrowIfExpired();
        RequireSourceAndIssuer(value.Source, value.HasIssuer(_issuer));
        return Succeeded(value);
    }

    internal PackageSourceOperationResult<NuGetGalleryDiscoveryResult>
        FailedGalleryDiscovery(PackageSourceFailureKind kind) =>
        Failed<NuGetGalleryDiscoveryResult>(
            PackageSourceCapabilities.Search,
            coordinate: null,
            ValidateFailureKind(kind, allowNotFound: false));
}
