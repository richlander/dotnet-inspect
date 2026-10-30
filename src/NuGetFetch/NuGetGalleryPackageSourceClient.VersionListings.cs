namespace NuGetFetch;

internal sealed partial class NuGetGalleryPackageSourceClient
{
    public async Task<PackageSourceOperationResult<PackageVersionListingSnapshot>>
        GetVersionListingSnapshotAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        string normalizedId = NormalizePackageId(packageId);
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await PackageSourceOperation.CaptureVersionListingSnapshotAsync(
            _results,
            async () =>
            {
                PackageVersionResult identities =
                    await ReadVersionIdentitiesAsync(
                        normalizedId, operation).ConfigureAwait(false);
                var candidateVersions = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (PackageCandidateObservation candidate in identities.Candidates)
                {
                    operation.ThrowIfExpired();
                    candidateVersions.Add(candidate.Coordinate.Version);
                }

                var budget = new NuGetGalleryRegistrationBudget(
                    candidateVersions.Count,
                    _options.MaxRegistrationMetadataBytes);
                NuGetGalleryRegistrationIndex? index = null;
                if (!identities.HasAuthoritativeListingState)
                {
                    try
                    {
                        index = await ReadRegistrationIndexAsync(
                            $"{Registration}{EscapeSegment(normalizedId)}/index.json",
                            candidateVersions,
                            budget,
                            operation).ConfigureAwait(false);
                        if (index is not null)
                        {
                            var listings = new Dictionary<string, PackageListingState>(
                                StringComparer.OrdinalIgnoreCase);
                            foreach (NuGetGalleryRegistrationPage page in index.Pages)
                            {
                                if (page.Items is { } items)
                                    AddRegistrationListings(items, listings, operation);
                            }
                            identities = ApplyKnownRegistrationListings(
                                identities, listings, operation);
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                        when (RegistrationIsUnavailable(exception))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        index = null;
                    }
                }

                return new PackageVersionListingSnapshot(
                    normalizedId, identities, candidateVersions, index, budget);
            },
            cancellationToken,
            operationContext,
            operation).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageVersionResult>>
        GetVersionListingsAsync(
            PackageVersionListingSnapshot snapshot,
            IReadOnlyList<string> versions,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(versions);
        if (!ReferenceEquals(snapshot.Source, Source))
        {
            throw new ArgumentException(
                "The version snapshot belongs to another package source.",
                nameof(snapshot));
        }

        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await PackageSourceOperation.CaptureVersionsAsync(
            _results,
            async () =>
            {
                var requested = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string version in versions)
                {
                    operation.ThrowIfExpired();
                    string normalized = PackageSourceCoordinate.Create(
                        snapshot.PackageId, version).Version;
                    if (!snapshot.CandidateVersions.Contains(normalized))
                    {
                        throw new ArgumentException(
                            "Listing requests must name versions reported by the snapshot.",
                            nameof(versions));
                    }
                    requested.Add(normalized);
                }

                if (requested.Count == 0
                    || snapshot.VersionIdentities.HasAuthoritativeListingState)
                {
                    return snapshot.VersionIdentities;
                }

                IReadOnlyDictionary<string, PackageListingState>? listings =
                    await TryGetRegistrationListingsAsync(
                        snapshot.PackageId,
                        snapshot.VersionIdentities.Candidates,
                        operation,
                        cancellationToken,
                        snapshot,
                        requested).ConfigureAwait(false);
                return listings is null
                    ? snapshot.VersionIdentities
                    : ApplyKnownRegistrationListings(
                        snapshot.VersionIdentities, listings, operation);
            },
            cancellationToken,
            operationContext: operationContext,
            operationDeadline: operation).ConfigureAwait(false);
    }

    private async Task<PackageVersionResult> ReadVersionIdentitiesAsync(
        string normalizedId,
        NuGetOperationDeadline operation)
    {
        string url = $"{FlatContainer}{EscapeSegment(normalizedId)}/index.json";
        (bool found, VersionIndex? index) =
            await NuGetHttpRetry.RunRequestAsync(
                operation,
                async requestToken =>
                {
                    using HttpRequestMessage request = NuGetHttpRequest.CreateGet(url);
                    using HttpResponseMessage response = await _client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken).ConfigureAwait(false);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return (false, null);

                    response.EnsureSuccessStatusCode();
                    VersionIndex? parsed = await NuGetMetadataReader.ReadResponseAsync(
                        response,
                        NuGetApi.DeserializeVersionIndexAsync,
                        _options,
                        operation.RequestTimeout,
                        requestToken).ConfigureAwait(false);
                    return (true, parsed);
                }).ConfigureAwait(false);
        if (!found)
            return _results.Versions([], hasAuthoritativeListingState: true, operation);

        return PackageSourceProjection.ProjectVersions(
            _results,
            normalizedId,
            index?.Versions ?? throw new NuGetSourceResponseException(
                "The NuGet Gallery version response was not a valid version document."),
            PackageDiscoveryContract.CompleteVersionEnumeration,
            PackageListingState.Unknown,
            hasAuthoritativeListingState: false,
            operation);
    }

    private Task<NuGetGalleryRegistrationIndex?> ReadRegistrationIndexAsync(
        string url,
        IReadOnlySet<string> candidateVersions,
        NuGetGalleryRegistrationBudget budget,
        NuGetOperationDeadline operation) =>
        ReadRegistrationDocumentAsync(
            url,
            async (json, cancellationToken) =>
            {
                using Stream limitedJson = budget.LimitBytes(json);
                return await NuGetGalleryRegistration.DeserializeIndexAsync(
                    limitedJson, candidateVersions, budget, operation,
                    cancellationToken).ConfigureAwait(false);
            },
            operation);

    private PackageVersionResult ApplyKnownRegistrationListings(
        PackageVersionResult identities,
        IReadOnlyDictionary<string, PackageListingState> listings,
        NuGetOperationDeadline operation)
    {
        var candidates = new PackageCandidateObservation[identities.Candidates.Count];
        bool complete = true;
        for (int i = 0; i < candidates.Length; i++)
        {
            operation.ThrowIfExpired();
            PackageCandidateObservation candidate = identities.Candidates[i];
            candidates[i] = listings.TryGetValue(
                candidate.Coordinate.Version, out PackageListingState listing)
                ? _results.Candidate(
                    candidate.Coordinate, candidate.DiscoveryContract, listing)
                : candidate;
            complete &= candidates[i].ListingState != PackageListingState.Unknown;
        }
        return _results.Versions(candidates, complete, operation);
    }
}
