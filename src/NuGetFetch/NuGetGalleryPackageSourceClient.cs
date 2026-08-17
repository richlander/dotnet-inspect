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
            _options);
    }

    public PackageSourceIdentity Identity => PackageSourceIdentity.NuGetOrg;
    public PackageSourceKind Kind => PackageSourceKind.NuGetGallery;
    public PackageSourceCapabilities Capabilities =>
        PackageSourceCapabilities.Search
        | PackageSourceCapabilities.VersionEnumeration
        | PackageSourceCapabilities.PackagePayload
        | PackageSourceCapabilities.SymbolPayload;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int take = 20,
        bool prerelease = false,
        CancellationToken cancellationToken = default)
    {
        return await _search.SearchAsync(
            query,
            take,
            prerelease,
            auth: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        string normalizedId = NormalizePackageId(packageId);
        string url =
            $"{FlatContainer}{EscapeSegment(normalizedId)}/index.json";
        using var operation = CreateOperation(cancellationToken);
        (bool found, VersionIndex? index) = await operation.RunRequestAsync(
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetHttpRequest.CreateGet(url);
                using HttpResponseMessage response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return (false, null);

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
            return [];

        IReadOnlyList<string> versions = index?.Versions
            ?? throw new InvalidOperationException(
                "The NuGet Gallery version response was not a valid version document.");
        foreach (string version in versions)
        {
            operation.ThrowIfExpired();
            if (!PackageCoordinateValidation.IsValidPackageVersion(version))
            {
                throw new InvalidOperationException(
                    "The NuGet Gallery version response contained an invalid package version.");
            }
        }

        return versions;
    }

    public async Task<Stream> GetPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        (string normalizedId, string normalizedVersion) =
            NormalizeCoordinate(packageId, version);
        string fileName =
            EscapeSegment($"{normalizedId}.{normalizedVersion}.nupkg");
        return await GetPayloadAsync(
            $"{PackageEndpoint}{fileName}",
            returnNullOnNotFound: false,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The NuGet Gallery package response was unexpectedly absent.");
    }

    public async Task<Stream?> TryGetSymbolsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        (string normalizedId, string normalizedVersion) =
            NormalizeCoordinate(packageId, version);
        string fileName =
            EscapeSegment($"{normalizedId}.{normalizedVersion}.snupkg");
        return await GetPayloadAsync(
            $"{SymbolEndpoint}{fileName}",
            returnNullOnNotFound: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Stream?> GetPayloadAsync(
        string url,
        bool returnNullOnNotFound,
        CancellationToken cancellationToken)
    {
        var operation = CreateOperation(cancellationToken);
        try
        {
            return await operation.RunStreamingRequestAsync(
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
                        if (returnNullOnNotFound
                            && response.StatusCode
                                == System.Net.HttpStatusCode.NotFound)
                        {
                            requestToken.ThrowIfCancellationRequested();
                            operation.ThrowIfExpired();
                            throw new GalleryPayloadNotFoundException();
                        }

                        response.EnsureSuccessStatusCode();
                        Stream stream = await response.Content
                            .ReadAsStreamAsync(requestToken)
                            .ConfigureAwait(false);
                        return (stream, response);
                    }
                    catch
                    {
                        response.Dispose();
                        throw;
                    }
                }).ConfigureAwait(false);
        }
        catch (GalleryPayloadNotFoundException)
            when (returnNullOnNotFound)
        {
            operation.Dispose();
            return null;
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

    private static (string Id, string Version) NormalizeCoordinate(
        string packageId,
        string version) =>
        (
            NormalizePackageId(packageId),
            PackageCoordinateValidation.NormalizeVersion(
                version,
                nameof(version))
        );

    private static string EscapeSegment(string value) =>
        Uri.EscapeDataString(value);

    public void Dispose() => _client.Dispose();

    private sealed class GalleryPayloadNotFoundException : Exception
    {
    }
}
