using NuGetFetch;

namespace DotnetInspector.Packages;

internal sealed class PackageSourceDependencyCandidateEvidence
{
    private readonly IPackageVersionListingSource? _listingSource;
    private readonly PackageVersionListingSnapshot? _snapshot;
    private readonly PackageVersionResult? _initial;
    private readonly Dictionary<string, PackageCandidateObservation> _known =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _listingGate = new(1, 1);

    private PackageSourceDependencyCandidateEvidence(
        IPackageSourceClient client,
        PackageVersionResult? initial,
        PackageSourceFailure? failure,
        IPackageVersionListingSource? listingSource = null,
        PackageVersionListingSnapshot? snapshot = null)
    {
        Client = client;
        _initial = initial;
        Failure = failure;
        _listingSource = listingSource;
        _snapshot = snapshot;
    }

    internal IPackageSourceClient Client { get; }
    internal PackageSourceFailure? Failure { get; }
    internal bool HasAnyCandidate => _initial is { Candidates.Count: > 0 };
    internal bool HasAuthoritativeListingState =>
        _initial?.HasAuthoritativeListingState == true;

    internal static async Task<PackageSourceDependencyCandidateEvidence> LoadAsync(
        IPackageSourceClient client,
        string packageId,
        NuGetOperationContext operation)
    {
        if (client is IPackageVersionListingSource listingSource)
        {
            PackageSourceOperationResult<PackageVersionListingSnapshot> outcome =
                await listingSource.GetVersionListingSnapshotAsync(
                    packageId, operation.CancellationToken, operation).ConfigureAwait(false);
            return new(client, outcome.Value?.VersionIdentities, outcome.Failure,
                listingSource, outcome.Value);
        }

        PackageSourceOperationResult<PackageVersionResult> full =
            await client.GetVersionsAsync(
                packageId, operation.CancellationToken, operation).ConfigureAwait(false);
        return new(client, full.Value, full.Failure);
    }

    internal PackageCandidateObservation[] GetCandidates()
    {
        lock (_known)
        {
            return _initial is null ? [] :
            [
                .. _initial.Candidates.Select(candidate =>
                    _known.GetValueOrDefault(candidate.Coordinate.Version) ?? candidate),
            ];
        }
    }

    internal async Task<PackageAuthorityFailure?> AcquireListingAsync(
        string version,
        ConfiguredPackageAuthority authority,
        NuGetOperationContext operation)
    {
        try
        {
            await _listingGate.WaitAsync(operation.OperationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (!operation.CancellationToken.IsCancellationRequested)
        {
            operation.ThrowIfExpired();
            throw;
        }

        try
        {
            operation.ThrowIfExpired();
            if (GetCandidates().Any(candidate =>
                candidate.Coordinate.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
                && candidate.ListingState is PackageListingState.Listed or PackageListingState.Unlisted))
            {
                return null;
            }
            if (_listingSource is null || _snapshot is null)
                return MissingListing();

            PackageSourceOperationResult<PackageVersionResult> outcome =
                await _listingSource.GetVersionListingsAsync(
                    _snapshot, [version], operation.CancellationToken, operation).ConfigureAwait(false);
            operation.ThrowIfExpired();
            if (outcome.Failure is { } failure)
                return PackageAuthorityFailureAdapter.DescribeVersionFailure(authority.Source, failure);
            PackageVersionResult result = outcome.Value
                ?? throw new InvalidOperationException("The listing operation returned no outcome.");
            if (!ReferenceEquals(result.Source, Client.Source))
                throw new InvalidOperationException("The listing operation returned another source.");

            lock (_known)
            {
                foreach (PackageCandidateObservation candidate in result.Candidates)
                {
                    if (candidate.ListingState is not (PackageListingState.Listed or PackageListingState.Unlisted))
                        continue;
                    if (_known.TryGetValue(candidate.Coordinate.Version, out PackageCandidateObservation? prior)
                        && prior.ListingState != candidate.ListingState)
                    {
                        return new PackageAuthorityFailure(
                            PackageSourceDisplay.ForDiagnostics(authority.Source),
                            PackageAuthorityFailureKind.InvalidResponse,
                            "The package source reported conflicting version listing states.")
                        {
                            ResultSource = result.Source,
                        };
                    }
                }
                foreach (PackageCandidateObservation candidate in result.Candidates)
                {
                    if (candidate.ListingState is PackageListingState.Listed or PackageListingState.Unlisted)
                        _known[candidate.Coordinate.Version] = candidate;
                }
            }

            return GetCandidates().Any(candidate =>
                candidate.Coordinate.Version.Equals(version, StringComparison.OrdinalIgnoreCase)
                && candidate.ListingState is PackageListingState.Listed or PackageListingState.Unlisted)
                ? null : MissingListing();
        }
        finally
        {
            _listingGate.Release();
        }

        PackageAuthorityFailure MissingListing() => new(
            PackageSourceDisplay.ForDiagnostics(authority.Source),
            PackageAuthorityFailureKind.IncompleteMetadata,
            $"The package source did not provide authoritative listing state for version '{version}'.")
        {
            ResultSource = Client.Source,
        };
    }
}
