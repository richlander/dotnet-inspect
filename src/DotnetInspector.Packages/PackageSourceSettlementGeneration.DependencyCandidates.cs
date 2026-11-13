using NuGetFetch;
using CandidateObservations = DotnetInspector.Packages.PackageSourceDependencyVersionObservations<
    DotnetInspector.Packages.PackageSourceDependencyCandidateEvidence>;

namespace DotnetInspector.Packages;

internal sealed partial class PackageSourceSettlementGeneration
{
    internal async Task<PackageVersionDiscoveryResult> DiscoverDependencyCandidateVersionsAsync(
        string packageId,
        string? constraint,
        IPackageSourceAuthorization sourceAuthorization,
        NuGetOperationContext operation,
        CandidateObservations observations,
        Action<string>? log)
    {
        var participants = new List<CandidateObservations.Observation>();
        var evidence = new List<PackageSourceDependencyCandidateEvidence>();
        PackageSourceAuthorization? authorization = null;
        var listingFailures = new List<PackageAuthorityFailure>();
        IReadOnlyList<int> requiredAuthorities = [];
        int nextListing = 0;
        try
        {
            operation.ThrowIfExpired();
            authorization = sourceAuthorization.AuthorizeSourcesFor(packageId);
            foreach (ConfiguredPackageAuthority authority in authorization.Authorities)
            {
                operation.ThrowIfExpired();
                IPackageSourceClient client = GetClient(authority);
                RequireAuthority(client.Source, authority);
                CandidateObservations.Observation shared = observations.Join(
                    authority, client.Source, packageId,
                    () => PackageSourceDependencyCandidateEvidence.LoadAsync(client, packageId, operation),
                    out bool reused);
                participants.Add(shared);
                log?.Invoke(reused
                    ? $"Reusing dependency identity evidence from {PackageSourceDisplay.ForDiagnostics(authority.Source)}."
                    : $"Fetching dependency identity evidence from {PackageSourceDisplay.ForDiagnostics(authority.Source)}.");
                PackageSourceDependencyCandidateEvidence source =
                    await shared.Work.Value.ConfigureAwait(false);
                operation.ThrowIfExpired();
                evidence.Add(source);
            }

            while (true)
            {
                operation.ThrowIfExpired();
                PackageSourceDependencyCandidateSelection selection =
                    _candidateIssuer.EvaluateDependencyCandidate(
                        packageId, constraint, authorization, evidence, operation);
                if (selection is PackageSourceDependencyCandidateSelection.Complete complete)
                {
                    operation.ThrowIfExpired();
                    if (complete.Discovery.State == PackageVersionDiscoveryState.Authoritative)
                        observations.Retain(participants);
                    return complete.Discovery;
                }

                var required = (PackageSourceDependencyCandidateSelection.NeedListings)selection;
                listingFailures.Clear();
                requiredAuthorities = required.Authorities;
                for (nextListing = 0; nextListing < requiredAuthorities.Count; nextListing++)
                {
                    operation.ThrowIfExpired();
                    int index = requiredAuthorities[nextListing];
                    PackageAuthorityFailure? failure = await evidence[index].AcquireListingAsync(
                        required.Version, authorization.Authorities[index], operation).ConfigureAwait(false);
                    if (failure is not null)
                        listingFailures.Add(failure);
                }
                if (listingFailures.Count > 0)
                {
                    return _candidateIssuer.CreateIncompleteDependencyCandidateDiscovery(
                        packageId, constraint, listingFailures, required.Observations);
                }
            }
        }
        catch (NuGetOperationTimeoutException)
        {
            if (authorization is not null)
            {
                return _candidateIssuer.CreateExpiredDependencyCandidateDiscovery(
                    packageId, constraint, authorization, evidence,
                    [
                        .. listingFailures,
                        .. OperationTimeoutFailures(authorization, evidence.Count, operation),
                        .. requiredAuthorities.Skip(nextListing).Select(index =>
                            OperationTimeoutFailure(authorization.Authorities[index], operation)),
                    ]);
            }
            return _candidateIssuer.CreateIncompleteDependencyCandidateDiscovery(
                packageId, constraint, [VersionDiscoveryOperationTimeoutFailure(operation)]);
        }
        finally
        {
            observations.Release(participants);
        }
    }
}
