using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Issues package acquisition candidates from explicit source authorization
/// and owner-issued source results.
/// </summary>
public sealed class PackageAcquisitionCandidateIssuer
{
    private readonly object _issuer = new();

    /// <summary>
    /// Issues one caller-pinned candidate from an already-authorized source set.
    /// </summary>
    public PackageAcquisitionCandidateResult ResolvePinnedCandidate(
        PackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(coordinate);
        if (authorization.Authorities.Count == 0)
        {
            return new PackageAcquisitionCandidateResult(
                PackageAcquisitionCandidateResultState.Denied,
                candidate: null,
                [
                    new PackageAuthorityFailure(
                        InertString.Empty,
                        PackageAuthorityFailureKind.Configuration,
                        authorization.DenialReason
                            ?? $"No configured package source is authorized for '{coordinate.PackageId}'."),
                ]);
        }

        return new PackageAcquisitionCandidateResult(
            PackageAcquisitionCandidateResultState.Resolved,
            PackageAcquisitionCandidate.CreatePinned(
                _issuer,
                coordinate,
                authorization.Authorities),
            failures: []);
    }

    /// <summary>
    /// Records an incomplete caller-pinned authorization operation.
    /// </summary>
    public PackageAcquisitionCandidateResult
        CreateIncompletePinnedCandidate(
            IReadOnlyList<PackageAuthorityFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
        {
            throw new ArgumentException(
                "Incomplete pinned authorization requires failure evidence.",
                nameof(failures));
        }

        return new PackageAcquisitionCandidateResult(
            PackageAcquisitionCandidateResultState.Incomplete,
            candidate: null,
            failures);
    }

    /// <summary>
    /// Aggregates one version-enumeration outcome for every explicitly
    /// authorized authority under the dependency-range discovery contract.
    /// </summary>
    public PackageVersionDiscoveryResult CreateDependencyVersionDiscovery(
        string packageId,
        PackageSourceAuthorization authorization,
        IReadOnlyList<PackageSourceOperationResult<PackageVersionResult>>
            outcomes) =>
        CreateDependencyVersionDiscoveryCore(
            packageId,
            authorization,
            outcomes,
            terminalFailures: [],
            requireEveryAuthority: true);

    /// <summary>
    /// Retains settled source evidence while recording that the shared
    /// operation deadline prevented complete authority consultation.
    /// </summary>
    public PackageVersionDiscoveryResult
        CreateIncompleteDependencyVersionDiscovery(
            string packageId,
            PackageSourceAuthorization authorization,
            IReadOnlyList<PackageSourceOperationResult<PackageVersionResult>>
                completedOutcomes,
            IReadOnlyList<PackageAuthorityFailure> terminalFailures)
    {
        ArgumentNullException.ThrowIfNull(terminalFailures);
        if (terminalFailures.Count == 0)
        {
            throw new ArgumentException(
                "Incomplete dependency discovery requires terminal failure evidence.",
                nameof(terminalFailures));
        }

        return CreateDependencyVersionDiscoveryCore(
            packageId,
            authorization,
            completedOutcomes,
            terminalFailures,
            requireEveryAuthority: false);
    }

    /// <summary>
    /// Prevents publication of an otherwise complete discovery result when
    /// its shared operation expires during package-owned aggregation.
    /// </summary>
    public PackageVersionDiscoveryResult
        CreateIncompleteDependencyVersionDiscovery(
            PackageVersionDiscoveryResult discovery,
            IReadOnlyList<PackageAuthorityFailure> terminalFailures)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(terminalFailures);
        if (!discovery.HasCandidateIssuer(_issuer))
        {
            throw new InvalidOperationException(
                "The discovery result belongs to another candidate issuer.");
        }
        if (terminalFailures.Count == 0)
        {
            throw new ArgumentException(
                "Incomplete dependency discovery requires terminal failure evidence.",
                nameof(terminalFailures));
        }

        return new PackageVersionDiscoveryResult(
            PackageVersionDiscoveryState.Failed,
            discovery.SourceListings,
            [.. discovery.Failures, .. terminalFailures],
            discovery.HasAnyCandidate,
            discovery.Candidates,
            discovery.Contract,
            _issuer);
    }

    private PackageVersionDiscoveryResult
        CreateDependencyVersionDiscoveryCore(
            string packageId,
            PackageSourceAuthorization authorization,
            IReadOnlyList<PackageSourceOperationResult<PackageVersionResult>>
                outcomes,
            IReadOnlyList<PackageAuthorityFailure> terminalFailures,
            bool requireEveryAuthority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (outcomes.Count > authorization.Authorities.Count
            || (requireEveryAuthority
                && authorization.Authorities.Count != outcomes.Count))
        {
            throw new ArgumentException(
                "Dependency version discovery requires one source outcome for every authorized authority.",
                nameof(outcomes));
        }

        if (authorization.Authorities.Count == 0)
        {
            IReadOnlyList<PackageAuthorityFailure> emptyFailures =
                terminalFailures.Count == 0
                    ? [
                        new PackageAuthorityFailure(
                            InertString.Empty,
                            PackageAuthorityFailureKind.Configuration,
                            authorization.DenialReason
                                ?? $"No configured package source is authorized for '{packageId}'."),
                    ]
                    : terminalFailures;
            return new PackageVersionDiscoveryResult(
                PackageVersionDiscoveryState.Failed,
                [],
                emptyFailures,
                hasAnyCandidate: false,
                contract:
                    PackageVersionDiscoveryContract
                        .DependencyRangeResolution,
                candidateIssuer: _issuer);
        }

        IReadOnlyList<InertString> labels =
            PackageSourceDisplay.ForVersionListings(
                authorization.Authorities.Select(
                    authority => authority.Source).ToArray());
        var listings = new List<PackageVersionSourceInfo>();
        var candidates =
            new List<ConfiguredPackageCandidateObservation>();
        var failures = new List<PackageAuthorityFailure>(
            terminalFailures);
        bool hasAnyCandidate = false;
        for (int index = 0; index < outcomes.Count; index++)
        {
            ConfiguredPackageAuthority authority =
                authorization.Authorities[index];
            PackageSourceOperationResult<PackageVersionResult> outcome =
                outcomes[index];
            if (outcome.Failure is { } failure)
            {
                RequireAuthority(failure.Source, authority);
                failures.Add(
                    DesktopPackageSourceComposition.DescribeFailure(
                        authority.Source,
                        failure));
                continue;
            }

            PackageVersionResult result = outcome.Value
                ?? throw new InvalidOperationException(
                    "The package source version operation returned neither a value nor a failure.");
            RequireAuthority(result.Source, authority);
            bool incompleteGalleryListingState =
                result.Source.TransportKind
                    == PackageSourceKind.NuGetGallery
                && !result.HasAuthoritativeListingState;

            foreach (PackageCandidateObservation candidate in
                     result.Candidates)
            {
                if (!ReferenceEquals(candidate.Source, result.Source)
                    || !candidate.Coordinate.PackageId.Equals(
                        packageId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The package source returned version evidence for another authority or package.");
                }
                if (candidate.DiscoveryContract
                    != PackageDiscoveryContract
                        .CompleteVersionEnumeration)
                {
                    throw new InvalidOperationException(
                        "Dependency version discovery requires complete version-enumeration observations.");
                }

                incompleteGalleryListingState |=
                    result.Source.TransportKind
                        == PackageSourceKind.NuGetGallery
                    && candidate.ListingState is
                        PackageListingState.Unknown
                        or PackageListingState.NotApplicable;
                hasAnyCandidate = true;
                bool listed = !result.HasAuthoritativeListingState
                    || candidate.ListingState
                        != PackageListingState.Unlisted;
                if (!listed)
                    continue;

                candidates.Add(new(authority, candidate));
                listings.Add(new PackageVersionSourceInfo(
                    candidate.Coordinate.Version,
                    labels[index].ToString(),
                    listed));
            }

            if (incompleteGalleryListingState)
            {
                failures.Add(new PackageAuthorityFailure(
                    PackageSourceDisplay.ForDiagnostics(authority.Source),
                    PackageAuthorityFailureKind.IncompleteMetadata,
                    $"Package source {PackageSourceDisplay.ForDiagnostics(authority.Source)} did not provide authoritative version listing state."));
            }
        }

        List<PackageVersionSourceInfo> orderedListings =
        [
            .. listings
                .OrderByDescending(
                    listing => NuGetVersion.Parse(listing.Version)),
        ];
        PackageVersionDiscoveryState state = !requireEveryAuthority
            ? PackageVersionDiscoveryState.Failed
            : failures.Count switch
        {
            0 => PackageVersionDiscoveryState.Authoritative,
            _ when orderedListings.Count > 0 =>
                PackageVersionDiscoveryState.Partial,
            _ => PackageVersionDiscoveryState.Failed,
        };
        return new PackageVersionDiscoveryResult(
            state,
            orderedListings,
            failures,
            hasAnyCandidate,
            candidates,
            PackageVersionDiscoveryContract.DependencyRangeResolution,
            _issuer);
    }

    private static void RequireAuthority(
        PackageSourceResultIdentity result,
        ConfiguredPackageAuthority authority)
    {
        if (!ReferenceEquals(
                result.Association,
                authority.Association))
        {
            throw new InvalidOperationException(
                "The package source result belongs to another configured authority.");
        }
    }
}
