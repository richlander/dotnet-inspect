using NuGetFetch;

namespace DotnetInspector.Packages;

internal abstract record PackageSourceDependencyCandidateSelection
{
    internal sealed record Complete(PackageVersionDiscoveryResult Discovery) :
        PackageSourceDependencyCandidateSelection;

    internal sealed record NeedListings(
        string Version,
        IReadOnlyList<int> Authorities,
        IReadOnlyList<ConfiguredPackageCandidateObservation> Observations) :
        PackageSourceDependencyCandidateSelection;
}

public sealed partial class PackageAcquisitionCandidateIssuer
{
    internal PackageSourceDependencyCandidateSelection EvaluateDependencyCandidate(
        string packageId,
        string? constraint,
        PackageSourceAuthorization authorization,
        IReadOnlyList<PackageSourceDependencyCandidateEvidence> evidence,
        NuGetOperationContext operation)
    {
        var (candidates, failures) = CollectDependencyCandidateObservations(
            packageId, authorization, evidence, operation);
        if (failures.Count > 0)
        {
            return new PackageSourceDependencyCandidateSelection.Complete(
                CreateIncompleteDependencyCandidateDiscovery(
                    packageId, constraint, failures, candidates));
        }

        string? best = PackageDependencyVersionRange.SelectBestSatisfying(
            candidates.Select(candidate => candidate.Observation.Coordinate.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase),
            constraint);
        if (best is null)
        {
            return new PackageSourceDependencyCandidateSelection.Complete(
                new PackageVersionDiscoveryResult(
                    packageId, PackageVersionDiscoveryState.Authoritative, [], [],
                    hasAnyCandidate: evidence.Any(source => source.HasAnyCandidate),
                    contract: PackageVersionDiscoveryContract.DependencyCandidateResolution,
                    candidateIssuer: _issuer,
                    dependencyRangeConstraint: constraint));
        }

        List<ConfiguredPackageCandidateObservation> selected =
        [
            .. candidates.Where(candidate => candidate.Observation.Coordinate.Version.Equals(
                best, StringComparison.OrdinalIgnoreCase)),
        ];
        var needs = new List<int>();
        for (int i = 0; i < evidence.Count; i++)
        {
            if (selected.Any(candidate =>
                ReferenceEquals(candidate.Authority, authorization.Authorities[i])
                && candidate.Observation.Source.TransportKind == PackageSourceKind.NuGetGallery
                && candidate.Observation.ListingState is PackageListingState.Unknown or PackageListingState.NotApplicable))
            {
                needs.Add(i);
            }
        }
        if (needs.Count > 0)
            return new PackageSourceDependencyCandidateSelection.NeedListings(best, needs, candidates);

        IReadOnlyList<InertText.InertString> labels = PackageSourceDisplay.ForVersionListings(
            authorization.Authorities.Select(authority => authority.Source).ToArray());
        var listings = new List<PackageVersionSourceInfo>();
        for (int i = 0; i < authorization.Authorities.Count; i++)
        {
            if (selected.Any(candidate => ReferenceEquals(candidate.Authority, authorization.Authorities[i])))
                listings.Add(new(best, labels[i].ToString(), true));
        }
        operation.ThrowIfExpired();
        return new PackageSourceDependencyCandidateSelection.Complete(
            new PackageVersionDiscoveryResult(
                packageId, PackageVersionDiscoveryState.Authoritative, listings, [],
                hasAnyCandidate: true, selected,
                PackageVersionDiscoveryContract.DependencyCandidateResolution,
                _issuer, constraint));
    }

    private static (
        List<ConfiguredPackageCandidateObservation> Candidates,
        List<PackageAuthorityFailure> Failures) CollectDependencyCandidateObservations(
            string packageId,
            PackageSourceAuthorization authorization,
            IReadOnlyList<PackageSourceDependencyCandidateEvidence> evidence,
            NuGetOperationContext? operation = null)
    {
        var candidates = new List<ConfiguredPackageCandidateObservation>();
        var failures = new List<PackageAuthorityFailure>(authorization.Failures);
        if (authorization.Authorities.Count == 0 && failures.Count == 0)
        {
            failures.Add(new PackageAuthorityFailure(
                InertText.InertString.Empty,
                PackageAuthorityFailureKind.Configuration,
                authorization.DenialReason ?? $"No configured package source is authorized for '{packageId}'."));
        }
        for (int i = 0; i < evidence.Count; i++)
        {
            operation?.ThrowIfExpired();
            ConfiguredPackageAuthority authority = authorization.Authorities[i];
            PackageSourceDependencyCandidateEvidence source = evidence[i];
            RequireAuthority(source.Client.Source, authority);
            if (source.Failure is { } failure)
            {
                RequireAuthority(failure.Source, authority);
                failures.Add(PackageAuthorityFailureAdapter.DescribeVersionFailure(authority.Source, failure));
                continue;
            }
            foreach (PackageCandidateObservation candidate in source.GetCandidates())
            {
                operation?.ThrowIfExpired();
                RequireVersionObservation(packageId, source.Client.Source, candidate);
                bool gallery = source.Client.Source.TransportKind == PackageSourceKind.NuGetGallery;
                if (candidate.ListingState == PackageListingState.Unlisted
                    && (gallery || source.HasAuthoritativeListingState))
                {
                    continue;
                }
                candidates.Add(new(authority, candidate));
            }
        }
        return (candidates, failures);
    }

    internal PackageVersionDiscoveryResult CreateExpiredDependencyCandidateDiscovery(
        string packageId,
        string? constraint,
        PackageSourceAuthorization authorization,
        IReadOnlyList<PackageSourceDependencyCandidateEvidence> evidence,
        IReadOnlyList<PackageAuthorityFailure> terminalFailures)
    {
        var (candidates, failures) = CollectDependencyCandidateObservations(
            packageId, authorization, evidence);
        return new PackageVersionDiscoveryResult(
            packageId, PackageVersionDiscoveryState.Failed, [],
            [.. failures, .. terminalFailures],
            hasAnyCandidate: evidence.Any(source => source.HasAnyCandidate), candidates,
            PackageVersionDiscoveryContract.DependencyCandidateResolution,
            _issuer, constraint);
    }

    internal PackageVersionDiscoveryResult CreateIncompleteDependencyCandidateDiscovery(
        string packageId,
        string? constraint,
        IReadOnlyList<PackageAuthorityFailure> failures,
        IReadOnlyList<ConfiguredPackageCandidateObservation>? candidates = null)
    {
        if (failures.Count == 0)
            throw new ArgumentException("Incomplete candidate discovery requires failure evidence.", nameof(failures));
        return new PackageVersionDiscoveryResult(
            packageId,
            candidates is { Count: > 0 } ? PackageVersionDiscoveryState.Partial : PackageVersionDiscoveryState.Failed,
            [], failures, hasAnyCandidate: candidates is { Count: > 0 }, candidates,
            PackageVersionDiscoveryContract.DependencyCandidateResolution,
            _issuer, constraint);
    }
}
