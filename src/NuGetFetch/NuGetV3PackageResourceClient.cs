using System.Collections.Concurrent;
using NuGet.Versioning;

namespace NuGetFetch;

/// <summary>
/// Owns NuGet v3 <c>PackageBaseAddress</c> discovery and source-relative
/// version, manifest, and package requests.
/// </summary>
/// <remarks>
/// <c>CanonicalV3VersionAndPackageDiscoverDeclaredBaseAddress</c> gates
/// service-index discovery without the legacy NuGet.org shortcut,
/// <c>V3VersionManifestAndPackageDoNotSendCredentialCrossOrigin</c> gates credential
/// scope, and
/// <c>DefaultV3TransportBlocksPrivateCrossOriginVersionAndPackageResources</c>
/// gates the destination policy on both resource operations.
/// </remarks>
internal sealed class NuGetV3PackageResourceClient(HttpClient client)
{
    internal const string NuGetOrgFlatContainer =
        "https://api.nuget.org/v3-flatcontainer/";
    internal const string NuGetOrgServiceIndex =
        "https://api.nuget.org/v3/index.json";

    private readonly ConcurrentDictionary<string, string> _baseAddressCache =
        new(StringComparer.Ordinal);

    internal async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        string serviceIndexUrl,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation,
        bool useNuGetOrgShortcut)
    {
        string baseAddress = await ResolveBaseAddressAsync(
            serviceIndexUrl,
            credential,
            options,
            operation,
            useNuGetOrgShortcut).ConfigureAwait(false);
        string normalizedId = packageId.ToLowerInvariant();
        string url = AppendBaseAddressPath(
            baseAddress,
            $"{Uri.EscapeDataString(normalizedId)}/index.json");
        PackageSourceCredential? endpointCredential =
            NuGetSourceRequest.CredentialForEndpoint(
                serviceIndexUrl,
                url,
                credential);

        try
        {
            return await operation.RunRequestAsync(
                async requestToken =>
                {
                    using HttpRequestMessage request =
                        NuGetHttpRequest.CreateGetPreservingPathAndQuery(url);
                    NuGetSourceRequest.ApplyCredential(
                        request,
                        endpointCredential);
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
                            options,
                            operation.RequestTimeout,
                            requestToken).ConfigureAwait(false);
                    return (IReadOnlyList<string>?)index?.Versions
                        ?? throw new NuGetSourceResponseException(
                            "The package version response was not a valid version document.");
                }).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }
    }

    internal async Task<(Stream Content, long? AdvertisedLength)> GetPackageAsync(
        string packageId,
        string version,
        string serviceIndexUrl,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation,
        bool useNuGetOrgShortcut)
    {
        string baseAddress = await ResolveBaseAddressAsync(
            serviceIndexUrl,
            credential,
            options,
            operation,
            useNuGetOrgShortcut).ConfigureAwait(false);
        string id = packageId.ToLowerInvariant();
        string normalizedVersion = NormalizeVersion(version);
        string url = AppendBaseAddressPath(
            baseAddress,
            $"{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(normalizedVersion)}/"
            + $"{Uri.EscapeDataString($"{id}.{normalizedVersion}.nupkg")}");
        PackageSourceCredential? endpointCredential =
            NuGetSourceRequest.CredentialForEndpoint(
                serviceIndexUrl,
                url,
                credential);

        return await operation.RunStreamingRequestAsync(
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetHttpRequest.CreateGetPreservingPathAndQuery(url);
                NuGetSourceRequest.ApplyCredential(
                    request,
                    endpointCredential);
                HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);
                try
                {
                    response.EnsureSuccessStatusCode();
                    Stream content = await response.Content
                        .ReadAsStreamAsync(requestToken)
                        .ConfigureAwait(false);
                    return (
                        content,
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

    internal async Task<ReadOnlyMemory<byte>> GetManifestAsync(
        string packageId,
        string version,
        string serviceIndexUrl,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation,
        bool useNuGetOrgShortcut)
    {
        string baseAddress = await ResolveBaseAddressAsync(
            serviceIndexUrl,
            credential,
            options,
            operation,
            useNuGetOrgShortcut).ConfigureAwait(false);
        return await GetManifestFromBaseAddressAsync(
            packageId,
            version,
            baseAddress,
            serviceIndexUrl,
            credential,
            options,
            operation).ConfigureAwait(false);
    }

    internal async Task<ReadOnlyMemory<byte>> GetManifestFromBaseAddressAsync(
        string packageId,
        string version,
        string baseAddress,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation) =>
        await GetManifestFromBaseAddressAsync(
            packageId,
            version,
            NormalizeBaseAddress(baseAddress),
            serviceIndexUrl: null,
            credential: null,
            options,
            operation).ConfigureAwait(false);

    private async Task<ReadOnlyMemory<byte>> GetManifestFromBaseAddressAsync(
        string packageId,
        string version,
        string baseAddress,
        string? serviceIndexUrl,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation)
    {
        string id = packageId.ToLowerInvariant();
        string normalizedVersion = NormalizeVersion(version);
        string url = AppendBaseAddressPath(
            baseAddress,
            $"{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(normalizedVersion)}/"
            + $"{Uri.EscapeDataString($"{id}.nuspec")}");
        PackageSourceCredential? endpointCredential =
            NuGetSourceRequest.CredentialForEndpoint(
                serviceIndexUrl,
                url,
                credential);

        return await NuGetHttpRetry.RunRequestAsync(
            operation,
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetHttpRequest.CreateGetPreservingPathAndQuery(url);
                NuGetSourceRequest.ApplyCredential(
                    request,
                    endpointCredential);
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await NuGetMetadataReader.ReadResponseAsync(
                    response,
                    ReadManifestBytesAsync,
                    options with
                    {
                        MaxMetadataResponseBytes =
                            options.MaxManifestResponseBytes,
                    },
                    operation.RequestTimeout,
                    requestToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static async ValueTask<ReadOnlyMemory<byte>> ReadManifestBytesAsync(
        Stream manifest,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await manifest.CopyToAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
        return buffer.ToArray();
    }

    internal async Task<string?> GetPackageBaseAddressAsync(
        string serviceIndexUrl,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation)
    {
        if (!NuGetSourceRequest.TryEndpointUrl(
                serviceIndexUrl,
                out string normalizedServiceIndexUrl))
        {
            throw new NuGetSourceResponseException(
                "The package source service-index endpoint is unusable.");
        }

        ServiceIndex? index;
        try
        {
            index = await operation.RunRequestAsync(
                async requestToken =>
                {
                    using HttpRequestMessage request =
                        NuGetHttpRequest
                            .CreateGetPreservingPathAndQuery(
                                normalizedServiceIndexUrl);
                    NuGetSourceRequest.ApplyCredential(
                        request,
                        credential);
                    using HttpResponseMessage response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    return await NuGetMetadataReader.ReadResponseAsync(
                        response,
                        NuGetApi.DeserializeServiceIndexAsync,
                        options,
                        operation.RequestTimeout,
                        requestToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode
                == System.Net.HttpStatusCode.NotFound)
        {
            throw new NuGetSourceResponseException(
                "The package source service index was not found.",
                exception);
        }

        string? baseAddress = index?.Resources
            .Where(resource => resource.Type.StartsWith(
                "PackageBaseAddress",
                StringComparison.OrdinalIgnoreCase))
            .Select(resource => resource.Id)
            .FirstOrDefault();

        return baseAddress is null
            ? null
            : NormalizeBaseAddress(baseAddress);
    }

    private async Task<string> ResolveBaseAddressAsync(
        string serviceIndexUrl,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation,
        bool useNuGetOrgShortcut)
    {
        if (useNuGetOrgShortcut
            && PackageSource.IsNuGetOrgServiceIndex(serviceIndexUrl))
        {
            return NuGetOrgFlatContainer;
        }

        bool cacheableSource =
            credential is null
            && Uri.TryCreate(
                serviceIndexUrl,
                UriKind.Absolute,
                out Uri? source)
            && source.Query.Length == 0
            && source.Fragment.Length == 0;
        if (cacheableSource
            && _baseAddressCache.TryGetValue(
                serviceIndexUrl,
                out string? cached))
        {
            return cached;
        }

        string baseAddress = await GetPackageBaseAddressAsync(
            serviceIndexUrl,
            credential,
            options,
            operation).ConfigureAwait(false)
            ?? throw new NuGetSourceResponseException(
                "The source service index did not advertise PackageBaseAddress.");

        if (cacheableSource
            && Uri.TryCreate(
                baseAddress,
                UriKind.Absolute,
                out Uri? resource)
            && resource.Query.Length == 0
            && resource.Fragment.Length == 0)
        {
            _baseAddressCache.TryAdd(serviceIndexUrl, baseAddress);
        }

        return baseAddress;
    }

    private static string NormalizeBaseAddress(string baseAddress)
    {
        if (!NuGetHttpRequest.HasValidRawText(
                baseAddress,
                allowNonAscii: true)
            || !Uri.TryCreate(baseAddress, UriKind.Absolute, out Uri? endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp
                && endpoint.Scheme != Uri.UriSchemeHttps)
            || endpoint.UserInfo.Length > 0
            || endpoint.Fragment.Length > 0)
        {
            throw new NuGetSourceResponseException(
                "The source service index advertised an unusable PackageBaseAddress.");
        }

        string escaped;
        try
        {
            escaped = NuGetSourceRequest.EndpointUrl(endpoint);
        }
        catch (NuGetSourceResponseException exception)
        {
            throw new NuGetSourceResponseException(
                "The source service index advertised an unusable PackageBaseAddress.",
                exception);
        }

        int queryStart = escaped.IndexOf('?', StringComparison.Ordinal);
        string pathAndOrigin = queryStart >= 0
            ? escaped[..queryStart]
            : escaped;
        string query = queryStart >= 0
            ? escaped[queryStart..]
            : "";
        string normalized = pathAndOrigin.EndsWith("/", StringComparison.Ordinal)
            ? pathAndOrigin + query
            : $"{pathAndOrigin}/{query}";
        if (!NuGetHttpRequest.TryCreatePreservingPathAndQuery(
                normalized,
                out _))
        {
            throw new NuGetSourceResponseException(
                "The source service index advertised an unusable PackageBaseAddress.");
        }

        return normalized;
    }

    private static string AppendBaseAddressPath(
        string baseAddress,
        string relativePath)
    {
        int queryStart = baseAddress.IndexOf('?', StringComparison.Ordinal);
        return queryStart >= 0
            ? $"{baseAddress[..queryStart]}{relativePath}{baseAddress[queryStart..]}"
            : baseAddress + relativePath;
    }

    internal static string NormalizeVersion(string version)
    {
        if (NuGetVersion.TryParse(version, out NuGetVersion? parsed))
        {
            return parsed.ToNormalizedString().ToLowerInvariant();
        }

        return version.ToLowerInvariant();
    }
}
