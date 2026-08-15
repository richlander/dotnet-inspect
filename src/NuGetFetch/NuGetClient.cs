using System.Collections.Concurrent;
using System.Net.Http.Headers;
using NuGet.Versioning;

namespace NuGetFetch;

/// <summary>
/// Lightweight NuGet V3 API client. Accepts an HttpClient from the caller.
/// </summary>
public class NuGetClient(HttpClient client)
{
    internal const string NuGetOrgFlatContainer = "https://api.nuget.org/v3-flatcontainer/";
    internal const string NuGetOrgServiceIndex = "https://api.nuget.org/v3/index.json";
    internal const string NuGetOrgSearchUrl = "https://azuresearch-usnc.nuget.org/query";

    private readonly NuGetFetchOptions _options = new();
    private readonly ConcurrentDictionary<string, string> _baseAddressCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a NuGet client with configured resource limits and deadlines.
    /// </summary>
    public NuGetClient(HttpClient client, NuGetFetchOptions options)
        : this(client)
    {
        _options = NuGetFetchOptions.Validate(options);
    }

    /// <summary>
    /// Gets all available versions for a package from a NuGet source.
    /// Returns empty list if the package does not exist.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        return await GetVersionsAsync(
            packageId,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        string? sourceUrl,
        PackageSourceCredential? credential,
        NuGetOperationDeadline operation)
    {
        string baseAddress = await ResolveBaseAddressAsync(
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);
        string url = $"{baseAddress}{packageId.ToLowerInvariant()}/index.json";

        try
        {
            return await operation.RunRequestAsync(
                async requestToken =>
                {
                    using HttpRequestMessage request =
                        NuGetMetadataReader.CreateGetRequest(url);
                    ApplyCredential(request, credential);
                    using HttpResponseMessage response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken).ConfigureAwait(false);

                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return [];

                    response.EnsureSuccessStatusCode();
                    VersionIndex? index =
                        await NuGetMetadataReader.ReadResponseAsync(
                            response,
                            NuGetApi.DeserializeVersionIndexAsync,
                            _options,
                            client.Timeout,
                            requestToken).ConfigureAwait(false);
                    return (IReadOnlyList<string>?)index?.Versions ?? [];
                }).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }
    }

    /// <summary>
    /// Gets the latest version for a package. Uses the search API for nuget.org (faster).
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, bool includePrerelease = false, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        return await GetLatestVersionAsync(
            packageId,
            includePrerelease,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);
    }

    private async Task<string?> GetLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        string? sourceUrl,
        PackageSourceCredential? credential,
        NuGetOperationDeadline operation)
    {
        // For nuget.org, use the search API (faster than listing all versions)
        if (sourceUrl is null
            || PackageSource.IsNuGetOrgServiceIndex(sourceUrl))
        {
            return await GetLatestVersionFromSearchAsync(
                packageId,
                includePrerelease,
                operation).ConfigureAwait(false);
        }

        // For other sources, list all versions and pick the latest
        IReadOnlyList<string> versions = await GetVersionsAsync(
            packageId,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);
        return FindLatestVersion(versions, includePrerelease);
    }

    /// <summary>
    /// Gets the latest version across multiple sources. Returns the first match.
    /// </summary>
    public async Task<string?> GetLatestVersionAsync(string packageId, IEnumerable<PackageSource> sources, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        foreach (PackageSource source in sources)
        {
            try
            {
                string? version = await GetLatestVersionAsync(
                    packageId,
                    includePrerelease,
                    source.Url,
                    source.Credential,
                    operation).ConfigureAwait(false);
                if (version is not null)
                {
                    return version;
                }
            }
            catch (HttpRequestException)
            {
                // Try next source
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads a package as a stream. The returned stream must be disposed by the caller,
    /// which will also dispose the underlying HTTP response.
    /// </summary>
    public async Task<Stream> DownloadAsync(string packageId, string version, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        var operation = CreateOperation(cancellationToken);
        try
        {
            string baseAddress = await ResolveBaseAddressAsync(
                sourceUrl,
                credential,
                operation).ConfigureAwait(false);
            string id = packageId.ToLowerInvariant();
            string ver = NormalizeVersion(version);
            string url = $"{baseAddress}{id}/{ver}/{id}.{ver}.nupkg";

            return await operation.RunStreamingRequestAsync(
                async requestToken =>
                {
                    using HttpRequestMessage request =
                        new(HttpMethod.Get, url);
                    ApplyCredential(request, credential);
                    HttpResponseMessage response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken).ConfigureAwait(false);
                    try
                    {
                        response.EnsureSuccessStatusCode();
                        Stream contentStream = await response.Content
                            .ReadAsStreamAsync(requestToken)
                            .ConfigureAwait(false);
                        return (contentStream, response);
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

    /// <summary>
    /// Downloads a package to a file.
    /// </summary>
    public async Task DownloadToFileAsync(string packageId, string version, string destinationPath, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using Stream source = await DownloadAsync(packageId, version, sourceUrl, credential, cancellationToken).ConfigureAwait(false);
        using FileStream dest = File.Create(destinationPath);
        await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the PackageBaseAddress endpoint from a V3 service index.
    /// </summary>
    public async Task<string?> GetPackageBaseAddressAsync(string serviceIndexUrl, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        return await GetPackageBaseAddressAsync(
            serviceIndexUrl,
            credential: null,
            operation).ConfigureAwait(false);
    }

    private async Task<string?> GetPackageBaseAddressAsync(
        string serviceIndexUrl,
        PackageSourceCredential? credential,
        NuGetOperationDeadline operation)
    {
        ServiceIndex? index = await operation.RunRequestAsync(
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetMetadataReader.CreateGetRequest(serviceIndexUrl);
                ApplyCredential(request, credential);
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                return await NuGetMetadataReader.ReadResponseAsync(
                    response,
                    NuGetApi.DeserializeServiceIndexAsync,
                    _options,
                    client.Timeout,
                    requestToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

        string? baseAddress = index?.Resources
            .Where(r => r.Type.StartsWith("PackageBaseAddress", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Id)
            .FirstOrDefault();

        if (baseAddress is null)
        {
            return null;
        }

        return baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/";
    }

    /// <summary>
    /// Resolves versions matching a wildcard pattern (e.g., "11.0.0-preview*").
    /// </summary>
    public async Task<string?> ResolveVersionPatternAsync(string packageId, string pattern, string? sourceUrl = null, PackageSourceCredential? credential = null, CancellationToken cancellationToken = default)
    {
        using var operation = CreateOperation(cancellationToken);
        IReadOnlyList<string> versions = await GetVersionsAsync(
            packageId,
            sourceUrl,
            credential,
            operation).ConfigureAwait(false);

        string prefix = pattern.TrimEnd('*');
        NuGetVersion? best = null;

        foreach (string v in versions)
        {
            if (v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                NuGetVersion.TryParse(v, out NuGetVersion? parsed))
            {
                if (best is null || parsed > best)
                {
                    best = parsed;
                }
            }
        }

        return best?.OriginalVersion;
    }

    private async Task<string?> GetLatestVersionFromSearchAsync(
        string packageId,
        bool includePrerelease,
        NuGetOperationDeadline operation)
    {
        var search = new SearchService(
            client,
            NuGetOrgSearchUrl,
            _options);
        IReadOnlyList<SearchResult> results = await search.SearchAsync(
            $"packageid:{packageId}",
            take: 1,
            prerelease: includePrerelease,
            auth: null,
            operation: operation).ConfigureAwait(false);
        return results.FirstOrDefault()?.Version;
    }

    private async Task<string> ResolveBaseAddressAsync(
        string? sourceUrl,
        PackageSourceCredential? credential,
        NuGetOperationDeadline operation)
    {
        if (sourceUrl is null
            || PackageSource.IsNuGetOrgServiceIndex(sourceUrl))
        {
            return NuGetOrgFlatContainer;
        }

        if (_baseAddressCache.TryGetValue(sourceUrl, out string? cached))
        {
            return cached;
        }

        string baseAddress = await GetPackageBaseAddressAsync(
            sourceUrl,
            credential,
            operation).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not resolve PackageBaseAddress from {sourceUrl}");

        _baseAddressCache.TryAdd(sourceUrl, baseAddress);
        return baseAddress;
    }

    private NuGetOperationDeadline CreateOperation(
        CancellationToken cancellationToken) =>
        new(_options, client.Timeout, cancellationToken);

    private static void ApplyCredential(HttpRequestMessage request, PackageSourceCredential? credential)
    {
        if (credential is not null)
        {
            string encoded = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{credential.Username}:{credential.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
    }

    internal static string? FindLatestVersion(IReadOnlyList<string> versions, bool includePrerelease)
    {
        NuGetVersion? best = null;

        foreach (string v in versions)
        {
            if (NuGetVersion.TryParse(v, out NuGetVersion? parsed))
            {
                if (!includePrerelease && parsed.IsPrerelease)
                {
                    continue;
                }

                if (best is null || parsed > best)
                {
                    best = parsed;
                }
            }
        }

        return best?.OriginalVersion;
    }

    /// <summary>
    /// Normalizes a version string using NuGet versioning rules.
    /// Falls back to lowercasing if the version string can't be parsed.
    /// </summary>
    public static string NormalizeVersion(string version)
    {
        if (NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            return parsed.ToNormalizedString().ToLowerInvariant();
        }

        return version.ToLowerInvariant();
    }
}
