using System.Runtime.ExceptionServices;

namespace NuGetFetch;

internal sealed class NuGetGalleryPackageSourceClient : IPackageSourceClient
{
    private const string SearchEndpoint =
        "https://azuresearch-usnc.nuget.org/query";
    private const int MaximumSearchSkip = 3000;
    private const string FlatContainer =
        "https://globalcdn.nuget.org/v3-flatcontainer/";
    private const string Registration =
        "https://globalcdn.nuget.org/v3/registration5-gz-semver2/";
    private const string PackageEndpoint =
        "https://globalcdn.nuget.org/packages/";
    private const string SymbolEndpoint =
        "https://globalcdn.nuget.org/symbol-packages/";
    private const int RegistrationPageBatchSize = 8;

    private readonly PackageSourceResultFactory _results;
    private readonly HttpClient _client;
    private readonly NuGetFetchOptions _options;
    private readonly NuGetClient _nuget;
    private readonly SearchService _search;

    public NuGetGalleryPackageSourceClient(
        PackageSourceResultFactory results,
        HttpClient client,
        NuGetFetchOptions options)
    {
        _results = results;
        _client = client;
        _options = NuGetFetchOptions.Validate(options);
        _nuget = new NuGetClient(client, _options);
        _search = new SearchService(
            client,
            SearchEndpoint,
            _options,
            retryTransientRequests: true);
    }

    public PackageSourceResultIdentity Source => _results.Source;
    internal TimeSpan TransportTimeout => _client.Timeout;
    internal TimeSpan RequestTimeout => _options.RequestTimeout;
    internal TimeSpan OperationTimeout => _options.OperationTimeout;
    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.Search
        | PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.Manifest
        | PackageSourceCapabilities.PackagePayload
        | PackageSourceCapabilities.SymbolPayload;

    public async Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        return await PackageSourceOperation.CaptureSearchAsync(
            _results,
            async () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(
                        cancellationToken,
                        operationContext);
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
                    when (exception.GetType()
                        == typeof(InvalidOperationException))
                {
                    throw new NuGetSourceResponseException(
                        "The NuGet Gallery search response did not satisfy the search contract.",
                        exception);
                }

                return PackageSourceProjection.ProjectSearch(
                    _results,
                    results,
                    operation,
                    results.Count == take
                        ? PackageSearchTruncationReason.RequestedLimit
                        : PackageSearchTruncationReason.None);
            },
            cancellationToken,
            operationContext: operationContext).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(
        string prefix,
        int take = 100,
        bool prerelease = false,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        return await PackageSourceOperation.CaptureSearchAsync(
            _results,
            async () =>
            {
                using NuGetOperationDeadline operation =
                    CreateOperation(
                        cancellationToken,
                        operationContext);
                PrefixSearchResult result;
                try
                {
                    result = await _search.SearchByPrefixWithStateAsync(
                            prefix,
                            take,
                            prerelease,
                            auth: null,
                            maximumSkip: MaximumSearchSkip,
                            operation)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                    when (exception.GetType()
                        == typeof(InvalidOperationException))
                {
                    throw new NuGetSourceResponseException(
                        "The NuGet Gallery prefix-search response did not satisfy the search contract.",
                        exception);
                }

                return PackageSourceProjection.ProjectSearch(
                    _results,
                    result.Matches,
                    operation,
                    result.Completion switch
                    {
                        PrefixSearchCompletion.Complete =>
                            PackageSearchTruncationReason.None,
                        PrefixSearchCompletion.TakeReached =>
                            PackageSearchTruncationReason.RequestedLimit,
                        PrefixSearchCompletion.SourcePageLimitReached =>
                            PackageSearchTruncationReason.SourcePageLimit,
                        PrefixSearchCompletion.ClientPageLimitReached =>
                            PackageSearchTruncationReason.ClientPageLimit,
                        _ => throw new InvalidOperationException(
                            "Unknown prefix-search completion state."),
                    });
            },
            cancellationToken,
            operationContext: operationContext).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        cancellationToken = operationContext?.ResolveInvocationToken(
            cancellationToken) ?? cancellationToken;
        string normalizedId = NormalizePackageId(packageId);
        return await PackageSourceOperation.CaptureVersionsAsync(
            _results,
            async () =>
            {
                string url =
                    $"{FlatContainer}{EscapeSegment(normalizedId)}/index.json";
                using NuGetOperationDeadline operation =
                    CreateOperation(
                        cancellationToken,
                        operationContext);
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
                                    operation.RequestTimeout,
                                    requestToken).ConfigureAwait(false);
                            return (true, parsed);
                        }).ConfigureAwait(false);

                if (!found)
                {
                    return _results.Versions(
                        [],
                        hasAuthoritativeListingState: true,
                        operation);
                }

                IReadOnlyList<string> versions = index?.Versions
                    ?? throw new NuGetSourceResponseException(
                        "The NuGet Gallery version response was not a valid version document.");
                PackageVersionResult partial =
                    PackageSourceProjection.ProjectVersions(
                    _results,
                    packageId,
                    versions,
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

                return ApplyRegistrationListingsOrPartial(
                    _results,
                    partial,
                    listings,
                    operation,
                    cancellationToken);
            },
            cancellationToken,
            operationContext: operationContext).ConfigureAwait(false);
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
            var candidateVersions = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (PackageCandidateObservation candidate in candidates)
            {
                operation.ThrowIfExpired();
                candidateVersions.Add(candidate.Coordinate.Version);
            }

            var budget =
                new NuGetGalleryRegistrationBudget(
                    candidateVersions.Count,
                    _options.MaxRegistrationMetadataBytes);
            NuGetGalleryRegistrationIndex? index =
                await ReadRegistrationDocumentAsync(
                    indexUrl,
                    async (json, cancellationToken) =>
                    {
                        using Stream limitedJson =
                            budget.LimitBytes(json);
                        return await NuGetGalleryRegistration
                            .DeserializeIndexAsync(
                                limitedJson,
                                candidateVersions,
                                budget,
                                operation,
                                cancellationToken).ConfigureAwait(false);
                    },
                    operation).ConfigureAwait(false);
            if (index is null)
                return null;

            var listings =
                new Dictionary<string, PackageListingState>(
                    candidates.Count,
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
                var materializationBudget =
                    new NuGetGalleryRegistrationByteBudget(
                        _options.MaxRegistrationPageBatchBytes);
                Task<MemoryStream?>[] requests =
                [
                    .. externalPages
                        .Skip(offset)
                        .Take(RegistrationPageBatchSize)
                        .Select(pageUrl =>
                            ReadRegistrationPageAsync(
                                pageUrl,
                                budget,
                                materializationBudget,
                                operation)),
                ];
                MemoryStream?[] pages;
                try
                {
                    pages = await Task.WhenAll(requests)
                        .ConfigureAwait(false);
                }
                catch
                {
                    foreach (Task<MemoryStream?> request in requests)
                    {
                        if (request.IsCompletedSuccessfully)
                            request.Result?.Dispose();
                    }

                    ThrowPreferredRegistrationFailure(
                        requests,
                        operation,
                        callerCancellation);
                    throw;
                }

                try
                {
                    if (pages.Any(page => page is null))
                        return null;

                    foreach (MemoryStream page in pages!)
                    {
                        IReadOnlyDictionary<string, PackageListingState>
                            pageListings = await operation.RunRequestAsync(
                                cancellationToken =>
                                    NuGetGalleryRegistration
                                        .DeserializePageAsync(
                                            page,
                                            candidateVersions,
                                            budget,
                                            operation,
                                            cancellationToken).AsTask())
                                .ConfigureAwait(false);
                        AddRegistrationListings(
                            pageListings,
                            listings,
                            operation);
                    }
                }
                finally
                {
                    foreach (MemoryStream? page in pages)
                        page?.Dispose();
                }
            }

            foreach (PackageCandidateObservation candidate in candidates)
            {
                operation.ThrowIfExpired();
                if (!listings.ContainsKey(
                        candidate.Coordinate.Version))
                {
                    return null;
                }
            }

            operation.ThrowIfExpired();
            return listings;
        }
        catch (OperationCanceledException)
            when (callerCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (RegistrationIsUnavailable(exception))
        {
            callerCancellation.ThrowIfCancellationRequested();
            return null;
        }
    }

    internal static PackageVersionResult
        ApplyRegistrationListingsOrPartial(
            PackageSourceResultFactory results,
            PackageVersionResult partial,
            IReadOnlyDictionary<string, PackageListingState> listings,
            NuGetOperationDeadline operation,
            CancellationToken callerCancellation)
    {
        ArgumentNullException.ThrowIfNull(results);
        try
        {
            var candidates =
                new PackageCandidateObservation[partial.Candidates.Count];
            for (int i = 0; i < candidates.Length; i++)
            {
                operation.ThrowIfExpired();
                PackageCandidateObservation candidate =
                    partial.Candidates[i];
                candidates[i] = results.Candidate(
                    candidate.Coordinate,
                    candidate.DiscoveryContract,
                    listings[candidate.Coordinate.Version]);
            }

            operation.ThrowIfExpired();
            return results.Versions(
                candidates,
                hasAuthoritativeListingState: true,
                operation);
        }
        catch (OperationCanceledException)
            when (callerCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (RegistrationIsUnavailable(exception))
        {
            callerCancellation.ThrowIfCancellationRequested();
            return partial;
        }
    }

    private static void AddRegistrationListings(
        IReadOnlyDictionary<string, PackageListingState> items,
        Dictionary<string, PackageListingState> listings,
        NuGetOperationDeadline operation)
    {
        foreach ((string version, PackageListingState listingState) in items)
        {
            operation.ThrowIfExpired();
            if (listings.TryGetValue(
                    version,
                    out PackageListingState prior)
                && prior != listingState)
            {
                throw new NuGetSourceResponseException(
                    "The NuGet Gallery registration response reported conflicting listing states.");
            }

            listings[version] = listingState;
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
                    operation.RequestTimeout,
                    requestToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private async Task<MemoryStream?> ReadRegistrationPageAsync(
        string url,
        NuGetGalleryRegistrationBudget aggregateBudget,
        NuGetGalleryRegistrationByteBudget materializationBudget,
        NuGetOperationDeadline operation)
    {
        NuGetGalleryRegistrationByteBudget.Materialization?
            materialization = await NuGetHttpRetry.RunRequestAsync(
            operation,
            async requestToken =>
            {
                NuGetGalleryRegistrationByteBudget.Materialization?
                    result = null;
                bool hasResult = false;
                try
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
                    result = await NuGetMetadataReader.ReadResponseAsync(
                        response,
                        async (json, cancellationToken) =>
                        {
                            using Stream aggregateLimitedJson =
                                aggregateBudget.LimitBytes(json);
                            return await materializationBudget
                                .MaterializeAsync(
                                    aggregateLimitedJson,
                                    cancellationToken).ConfigureAwait(false);
                        },
                        _options,
                        operation.RequestTimeout,
                        requestToken).ConfigureAwait(false);
                    hasResult = true;
                    return result;
                }
                catch
                {
                    if (hasResult)
                        NuGetRejectedResult.RejectIfOwned(result);
                    throw;
                }
            }).ConfigureAwait(false);
        if (materialization is null)
            return null;

        using (materialization)
            return materialization.Commit();
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
            or InvalidDataException
            or OperationCanceledException
            or System.Text.Json.JsonException
            or NuGetSourceResponseException;

    private static void ThrowPreferredRegistrationFailure(
        IEnumerable<Task<MemoryStream?>> requests,
        NuGetOperationDeadline operation,
        CancellationToken callerCancellation)
    {
        callerCancellation.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();

        Exception[] failures = requests
            .SelectMany(RegistrationFailures)
            .ToArray();
        Exception? timeout = failures
            .FirstOrDefault(exception =>
                exception is NuGetRequestTimeoutException
                    or NuGetMetadataBodyTimeoutException
                    or NuGetOperationTimeoutException);
        timeout ??= failures.FirstOrDefault(
            exception => exception is TimeoutException);
        if (timeout is not null)
            ExceptionDispatchInfo.Capture(timeout).Throw();
    }

    private static IEnumerable<Exception> RegistrationFailures(
        Task<MemoryStream?> request)
    {
        if (request.Exception is not null)
            return request.Exception.Flatten().InnerExceptions;

        if (!request.IsCanceled)
            return [];

        try
        {
            _ = request.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException exception)
        {
            return NuGetTransportFailure.GetTimeout(exception) is
                { } timeout
                ? [timeout]
                : [];
        }

        return [];
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        string fileName =
            EscapeSegment(
                $"{coordinate.PackageId}.{coordinate.Version}.nupkg");
        return await PackageSourceOperation.CapturePackageAsync(
            _results,
            coordinate,
            async () =>
            {
                (Stream content, long? advertisedLength) =
                    await GetPayloadAsync(
                        $"{PackageEndpoint}{fileName}",
                        cancellationToken,
                        operationContext).ConfigureAwait(false);
                return _results.Payload(
                    coordinate,
                    PackageSourcePayloadKind.Package,
                    content,
                    advertisedLength);
            },
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        return await PackageSourceOperation.CaptureManifestAsync(
            _results,
            coordinate,
            async () => _results.Manifest(
                coordinate,
                await GetManifestAsync(
                    coordinate,
                    cancellationToken,
                    operationContext).ConfigureAwait(false)),
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    public async Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        string fileName =
            EscapeSegment(
                $"{coordinate.PackageId}.{coordinate.Version}.snupkg");
        return await PackageSourceOperation.CaptureSymbolsAsync(
            _results,
            coordinate,
            async () =>
            {
                (Stream content, long? advertisedLength) =
                    await GetPayloadAsync(
                        $"{SymbolEndpoint}{fileName}",
                        cancellationToken,
                        operationContext).ConfigureAwait(false);
                return _results.Payload(
                    coordinate,
                    PackageSourcePayloadKind.Symbols,
                    content,
                    advertisedLength);
            },
            cancellationToken,
            operationContext).ConfigureAwait(false);
    }

    private async Task<(Stream Content, long? AdvertisedLength)> GetPayloadAsync(
        string url,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
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
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext) =>
        operationContext is null
            ? new NuGetOperationDeadline(
                _options,
                _client.Timeout,
                cancellationToken,
                Source)
            : operationContext.CreateDeadline(
                _client.Timeout,
                cancellationToken,
                Source);

    private async Task<ReadOnlyMemory<byte>> GetManifestAsync(
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken,
        NuGetOperationContext? operationContext)
    {
        using NuGetOperationDeadline operation =
            CreateOperation(cancellationToken, operationContext);
        return await _nuget.GetManifestFromBaseAddressAsync(
            coordinate.PackageId,
            coordinate.Version,
            FlatContainer,
            operation).ConfigureAwait(false);
    }

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
