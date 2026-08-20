namespace NuGetFetch;

internal sealed class NuGetGalleryPackageSourceClient : IPackageSourceClient
{
    private const string SearchEndpoint =
        "https://azuresearch-usnc.nuget.org/query";
    private const string FlatContainer =
        "https://globalcdn.nuget.org/v3-flatcontainer/";
    private const string Registration =
        "https://globalcdn.nuget.org/v3/registration5-gz-semver2/";
    private const string PackageEndpoint =
        "https://globalcdn.nuget.org/packages/";
    private const string SymbolEndpoint =
        "https://globalcdn.nuget.org/symbol-packages/";
    private const int RegistrationPageBatchSize = 8;

    private readonly HttpClient _client;
    private readonly NuGetFetchOptions _options;
    private readonly SearchService _search;

    public NuGetGalleryPackageSourceClient(
        HttpClient client,
        NuGetFetchOptions options)
    {
        _client = client;
        _options = NuGetFetchOptions.Validate(options);
        _search = new SearchService(
            client,
            SearchEndpoint,
            _options,
            retryTransientRequests: true);
    }

    public PackageSourceIdentity Identity => PackageSourceIdentity.NuGetOrg;
    public PackageSourceKind Kind => PackageSourceKind.NuGetGallery;
    internal TimeSpan TransportTimeout => _client.Timeout;
    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.Search
        | PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.PackagePayload
        | PackageSourceCapabilities.SymbolPayload;

    public async Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default)
    {
        return await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.Search,
            async () =>
            {
                using var operation = CreateOperation(cancellationToken);
                IReadOnlyList<SearchResult> results;
                try
                {
                    results = await _search.SearchAsync(
                            query,
                            take,
                            prerelease,
                            auth: null,
                            operation)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    throw new NuGetSourceResponseException(
                        "The NuGet Gallery search response did not satisfy the search contract.",
                        exception);
                }

                return PackageSourceProjection.ProjectSearch(
                    results,
                    Identity,
                    operation);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        string normalizedId = NormalizePackageId(packageId);
        return await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.VersionEnumeration,
            async () =>
            {
                string url =
                    $"{FlatContainer}{EscapeSegment(normalizedId)}/index.json";
                using var operation = CreateOperation(cancellationToken);
                (bool found, VersionIndex? index) =
                    await NuGetHttpRetry.RunRequestAsync(
                        operation,
                        async requestToken =>
                        {
                            using HttpRequestMessage request =
                                NuGetHttpRequest.CreateGet(url);
                            using HttpResponseMessage response =
                                await _client.SendAsync(
                                    request,
                                    HttpCompletionOption.ResponseHeadersRead,
                                    requestToken).ConfigureAwait(false);
                            if (response.StatusCode
                                == System.Net.HttpStatusCode.NotFound)
                            {
                                return (false, null);
                            }

                            response.EnsureSuccessStatusCode();
                            VersionIndex? parsed =
                                await NuGetMetadataReader.ReadResponseAsync(
                                    response,
                                    NuGetApi.DeserializeVersionIndexAsync,
                                    _options,
                                    _client.Timeout,
                                    requestToken).ConfigureAwait(false);
                            return (true, parsed);
                        }).ConfigureAwait(false);

                if (!found)
                {
                    return new PackageVersionResult(
                        [],
                        hasAuthoritativeListingState: true);
                }

                IReadOnlyList<string> versions = index?.Versions
                    ?? throw new NuGetSourceResponseException(
                        "The NuGet Gallery version response was not a valid version document.");
                PackageVersionResult partial =
                    PackageSourceProjection.ProjectVersions(
                    packageId,
                    versions,
                    Identity,
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    PackageListingState.Unknown,
                    hasAuthoritativeListingState: false,
                    operation);
                IReadOnlyDictionary<string, PackageListingState>? listings =
                    await TryGetRegistrationListingsAsync(
                        normalizedId,
                        partial.Candidates,
                        operation,
                        cancellationToken).ConfigureAwait(false);
                if (listings is null)
                    return partial;

                operation.ThrowIfExpired();
                return new PackageVersionResult(
                    [
                        .. partial.Candidates.Select(candidate =>
                            candidate with
                            {
                                ListingState =
                                    listings[candidate.Coordinate.Version],
                            }),
                    ],
                    hasAuthoritativeListingState: true);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, PackageListingState>?>
        TryGetRegistrationListingsAsync(
            string normalizedId,
            IReadOnlyList<PackageCandidateObservation> candidates,
            NuGetOperationDeadline operation,
            CancellationToken callerCancellation)
    {
        string indexUrl =
            $"{Registration}{EscapeSegment(normalizedId)}/index.json";
        try
        {
            NuGetGalleryRegistrationIndex? index =
                await ReadRegistrationDocumentAsync(
                    indexUrl,
                    NuGetGalleryRegistration.DeserializeIndexAsync,
                    operation).ConfigureAwait(false);
            if (index is null)
                return null;

            var listings =
                new Dictionary<string, PackageListingState>(
                    StringComparer.OrdinalIgnoreCase);
            var externalPages = new List<string>();
            foreach (NuGetGalleryRegistrationPage page in index.Pages)
            {
                if (page.Items is { } inlineItems)
                {
                    AddRegistrationListings(
                        inlineItems,
                        listings,
                        operation);
                }
                else
                {
                    externalPages.Add(
                        RebaseRegistrationPage(
                            page.ExternalId!,
                            normalizedId));
                }
            }

            for (int offset = 0;
                 offset < externalPages.Count;
                 offset += RegistrationPageBatchSize)
            {
                Task<IReadOnlyList<NuGetGalleryRegistrationLeaf>?>[] requests =
                [
                    .. externalPages
                        .Skip(offset)
                        .Take(RegistrationPageBatchSize)
                        .Select(pageUrl =>
                            ReadRegistrationDocumentAsync(
                                pageUrl,
                                NuGetGalleryRegistration.DeserializePageAsync,
                                operation)),
                ];
                IReadOnlyList<NuGetGalleryRegistrationLeaf>?[] pages =
                    await Task.WhenAll(requests).ConfigureAwait(false);
                if (pages.Any(page => page is null))
                    return null;
                foreach (IReadOnlyList<NuGetGalleryRegistrationLeaf> page in
                         pages!)
                {
                    AddRegistrationListings(
                        page,
                        listings,
                        operation);
                }
            }

            return candidates.All(candidate =>
                    listings.ContainsKey(
                        candidate.Coordinate.Version))
                ? listings
                : null;
        }
        catch (OperationCanceledException)
            when (callerCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (RegistrationIsUnavailable(exception))
        {
            return null;
        }
    }

    private static void AddRegistrationListings(
        IReadOnlyList<NuGetGalleryRegistrationLeaf> items,
        Dictionary<string, PackageListingState> listings,
        NuGetOperationDeadline operation)
    {
        foreach (NuGetGalleryRegistrationLeaf item in items)
        {
            operation.ThrowIfExpired();
            if (listings.TryGetValue(
                    item.Version,
                    out PackageListingState prior)
                && prior != item.ListingState)
            {
                throw new NuGetSourceResponseException(
                    "The NuGet Gallery registration response reported conflicting listing states.");
            }

            listings[item.Version] = item.ListingState;
        }
    }

    private async Task<T?> ReadRegistrationDocumentAsync<T>(
        string url,
        Func<Stream, CancellationToken, ValueTask<T>> deserialize,
        NuGetOperationDeadline operation)
        where T : class
    {
        return await NuGetHttpRetry.RunRequestAsync(
            operation,
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetHttpRequest.CreateGet(url);
                using HttpResponseMessage response =
                    await _client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken).ConfigureAwait(false);
                if (response.StatusCode
                    == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                return await NuGetMetadataReader.ReadResponseAsync(
                    response,
                    deserialize,
                    _options,
                    _client.Timeout,
                    requestToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static string RebaseRegistrationPage(
        string externalId,
        string normalizedId)
    {
        if (!NuGetHttpRequest.TryCreatePreservingPathAndQuery(
                externalId,
                out _)
            || !Uri.TryCreate(
                externalId,
                UriKind.Absolute,
                out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || parsed.UserInfo.Length > 0
            || parsed.Query.Length > 0
            || parsed.Fragment.Length > 0)
        {
            throw new NuGetSourceResponseException(
                "The NuGet Gallery registration page ID was not an eligible HTTPS path.");
        }

        int schemeSeparator = externalId.IndexOf(
            "://",
            StringComparison.Ordinal);
        int pathStart = externalId.IndexOf(
            '/',
            schemeSeparator + 3);
        string path = pathStart >= 0
            ? externalId[(pathStart + 1)..]
            : "";
        string[] segments = path.Split('/');
        if (segments.Length != 6
            || segments[0] != "v3"
            || segments[1] != "registration5-gz-semver2"
            || !IsCanonicalSegment(segments[2], normalizedId)
            || segments[3] != "page"
            || !TryNormalizeVersionSegment(
                segments[4],
                out string lower)
            || !segments[5].EndsWith(
                ".json",
                StringComparison.Ordinal)
            || !TryNormalizeVersionSegment(
                segments[5][..^5],
                out string upper))
        {
            throw new NuGetSourceResponseException(
                "The NuGet Gallery registration page ID did not match the expected package page path.");
        }

        return $"{Registration}{EscapeSegment(normalizedId)}/page/"
            + $"{EscapeSegment(lower)}/{EscapeSegment(upper)}.json";
    }

    private static bool IsCanonicalSegment(
        string encoded,
        string expected) =>
        Uri.UnescapeDataString(encoded).Equals(
            expected,
            StringComparison.Ordinal)
        && encoded.Equals(
            EscapeSegment(expected),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizeVersionSegment(
        string encoded,
        out string normalized)
    {
        normalized = "";
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(encoded);
            normalized =
                PackageCoordinateValidation.NormalizeVersion(
                    decoded,
                    "version");
        }
        catch (ArgumentException)
        {
            return false;
        }

        return encoded.Equals(
            EscapeSegment(normalized),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool RegistrationIsUnavailable(
        Exception exception) =>
        exception is HttpRequestException
            or IOException
            or TimeoutException
            or OperationCanceledException
            or System.Text.Json.JsonException
            or NuGetSourceResponseException;

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        string fileName =
            EscapeSegment(
                $"{coordinate.PackageId}.{coordinate.Version}.nupkg");
        return await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.PackagePayload,
            async () =>
            {
                (Stream content, long? advertisedLength) =
                    await GetPayloadAsync(
                        $"{PackageEndpoint}{fileName}",
                        cancellationToken).ConfigureAwait(false);
                return new PackageSourcePayload(
                    coordinate,
                    Identity,
                    Kind,
                    PackageSourcePayloadKind.Package,
                    content,
                    advertisedLength);
            },
            cancellationToken,
            coordinate).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        string fileName =
            EscapeSegment(
                $"{coordinate.PackageId}.{coordinate.Version}.snupkg");
        return await PackageSourceOperation.CaptureAsync(
            Identity,
            Kind,
            PackageSourceCapabilities.SymbolPayload,
            async () =>
            {
                (Stream content, long? advertisedLength) =
                    await GetPayloadAsync(
                        $"{SymbolEndpoint}{fileName}",
                        cancellationToken).ConfigureAwait(false);
                return new PackageSourcePayload(
                    coordinate,
                    Identity,
                    Kind,
                    PackageSourcePayloadKind.Symbols,
                    content,
                    advertisedLength);
            },
            cancellationToken,
            coordinate).ConfigureAwait(false);
    }

    private async Task<(Stream Content, long? AdvertisedLength)> GetPayloadAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var operation = CreateOperation(cancellationToken);
        try
        {
            return await NuGetHttpRetry.RunStreamingRequestAsync(
                operation,
                async requestToken =>
                {
                    using HttpRequestMessage request =
                        NuGetHttpRequest.CreateGet(url);
                    HttpResponseMessage response = await _client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken).ConfigureAwait(false);
                    try
                    {
                        response.EnsureSuccessStatusCode();
                        Stream stream = await response.Content
                            .ReadAsStreamAsync(requestToken)
                            .ConfigureAwait(false);
                        return (
                            stream,
                            response,
                            response.Content.Headers.ContentLength);
                    }
                    catch
                    {
                        response.Dispose();
                        throw;
                    }
                }).ConfigureAwait(false);
        }
        catch
        {
            operation.Dispose();
            throw;
        }
    }

    private NuGetOperationDeadline CreateOperation(
        CancellationToken cancellationToken) =>
        new(_options, _client.Timeout, cancellationToken);

    private static string NormalizePackageId(string packageId)
    {
        PackageCoordinateValidation.ValidatePackageId(
            packageId,
            nameof(packageId));
        return packageId.ToLowerInvariant();
    }

    private static string EscapeSegment(string value) =>
        Uri.EscapeDataString(value);

    public void Dispose() => _client.Dispose();
}
