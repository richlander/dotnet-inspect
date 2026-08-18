namespace NuGetFetch;

internal sealed class NuGetGalleryPackageSourceClient : IPackageSourceClient
{
    private const string SearchEndpoint =
        "https://azuresearch-usnc.nuget.org/query";
    private const string FlatContainer =
        "https://globalcdn.nuget.org/v3-flatcontainer/";
    private const string PackageEndpoint =
        "https://globalcdn.nuget.org/packages/";
    private const string SymbolEndpoint =
        "https://globalcdn.nuget.org/symbol-packages/";

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
                        hasAuthoritativeListingState: false);
                }

                IReadOnlyList<string> versions = index?.Versions
                    ?? throw new NuGetSourceResponseException(
                        "The NuGet Gallery version response was not a valid version document.");
                return PackageSourceProjection.ProjectVersions(
                    packageId,
                    versions,
                    Identity,
                    PackageDiscoveryContract.CompleteVersionEnumeration,
                    PackageListingState.Unknown,
                    hasAuthoritativeListingState: false,
                    operation);
            },
            cancellationToken).ConfigureAwait(false);
    }

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
