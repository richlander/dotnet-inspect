using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using NuGet.Versioning;
using PackageSource = NuGetFetch.PackageSource;
using ServiceResource = NuGetFetch.ServiceResource;

namespace DotnetInspector.Services;

/// <summary>
/// Fetches NuGet metadata: publish date, downloads, deprecation, vulnerabilities, and package size.
/// Results are cached on disk with a 1-hour TTL.
/// </summary>
public static class PackageMetadataService
{
    private enum SourcePresence
    {
        Present,
        Absent,
        Indeterminate,
    }

    private readonly record struct SourceMetadataResult(
        SourcePresence Presence,
        PackageMetadata? Metadata = null,
        bool Cacheable = true);

    private readonly record struct TextFetchResult(
        string? Content,
        HttpStatusCode? StatusCode)
    {
        public bool IsSuccess => Content is not null;
        public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    }

    private readonly record struct PackageProbeResult(
        SourcePresence Presence,
        long? PackageSize = null);

    private readonly record struct SearchPageResult(
        bool Found,
        int ResultCount,
        long? TotalHits,
        bool DeprecationMetadataAvailable,
        bool Indeterminate = false);

    private readonly record struct SearchFetchResult(
        bool Succeeded,
        bool Found,
        bool DeprecationMetadataAvailable,
        bool Indeterminate = false);

    private readonly record struct VulnerabilityFetchResult(
        bool Succeeded,
        List<PackageVulnerability> Vulnerabilities);

    /// <summary>
    /// Fetches all NuGet metadata for a package: published date, downloads, verified status, deprecation, vulnerabilities.
    /// Results are cached on disk; use <paramref name="forceLatest"/> to bypass the cache.
    /// </summary>
    public static async Task<PackageMetadata> FetchAllMetadataAsync(
        HttpClient client,
        string packageName,
        string version,
        Action<string>? log,
        bool forceLatest = false,
        NuGetSourceOptions? sourceOptions = null,
        HttpClient? untrustedClient = null)
        => await FetchMetadataAsync(
            client,
            packageName,
            version,
            log,
            forceLatest,
            sourceOptions,
            untrustedClient ?? HttpClientFactory.SharedUntrustedFetch).ConfigureAwait(false);

    private static async Task<PackageMetadata> FetchMetadataAsync(
        HttpClient client,
        string packageName,
        string version,
        Action<string>? log,
        bool forceLatest,
        NuGetSourceOptions? sourceOptions,
        HttpClient untrustedClient)
    {
        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = NormalizeVersion(version);
        IReadOnlyList<PackageSource> sources =
            ResolveMetadataSources(sourceOptions, packageName, log);

        foreach (PackageSource source in sources)
        {
            string cacheKey = MetadataCacheKey(
                source,
                normalizedName,
                normalizedVersion);
            if (!forceLatest)
            {
                MetadataFieldCache.Entry? fromCache;
                using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageMetadata))
                {
                    fromCache = MetadataFieldCache.TryGetEntry(cacheKey);
                }
                if (fromCache is { IsAbsent: true })
                {
                    continue;
                }
                if (fromCache is { } cached)
                {
                    log?.Invoke($"Using cached metadata from {source.Name}");
                    return cached.Metadata;
                }
            }

            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? sourceUri)
                || sourceUri.Scheme is not ("http" or "https"))
            {
                log?.Invoke(
                    $"Skipping non-HTTP NuGet metadata source "
                    + $"'{source.Name}': {source.Url}");
                return new PackageMetadata();
            }

            HttpClient sourceClient =
                ReferenceEquals(client, HttpClientFactory.Shared)
                    ? HttpClientFactory.GetPackageSourceClient(source.Url)
                    : client;
            SourceMetadataResult result = await FetchAllMetadataFromSourceAsync(
                sourceClient,
                source,
                normalizedName,
                normalizedVersion,
                log,
                untrustedClient).ConfigureAwait(false);
            if (result.Presence == SourcePresence.Indeterminate)
            {
                log?.Invoke(
                    $"Metadata from higher-precedence source '{source.Name}' is unavailable; "
                    + "lower sources were not consulted.");
                return new PackageMetadata();
            }
            if (result.Presence == SourcePresence.Absent)
            {
                using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageMetadata))
                {
                    MetadataFieldCache.SetAbsent(cacheKey);
                }
                continue;
            }

            PackageMetadata metadata = result.Metadata ?? new PackageMetadata();
            if (result.Cacheable)
            {
                using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageMetadata))
                {
                    MetadataFieldCache.Set(cacheKey, metadata);
                }
            }
            return metadata;
        }

        return new PackageMetadata();
    }

    private static IReadOnlyList<PackageSource> ResolveMetadataSources(
        NuGetSourceOptions? sourceOptions,
        string packageId,
        Action<string>? log)
    {
        try
        {
            List<PackageSource> mapped =
                NuGetSourceResolver.ResolveSourcesForPackage(
                    sourceOptions,
                    packageId);
            return NuGetSourceResolver.ResolveAuthorizedSources(
                sourceOptions,
                mapped);
        }
        catch (PackageSourceMappingException ex)
            when (ex.Failure is
                PackageSourceMappingFailure.NoPattern
                or PackageSourceMappingFailure.InactiveSource)
        {
            log?.Invoke(ex.Message);
            return [];
        }
    }

    private static string MetadataCacheKey(
        PackageSource source,
        string normalizedName,
        string normalizedVersion) =>
        "v7-full-"
        + $"{NuGetCache.GetSourceKey(source.Url)}-"
        + $"{normalizedName}@{normalizedVersion}";

    private static string NormalizeVersion(string version) =>
        NuGetVersion.TryParse(version, out NuGetVersion? parsed)
            ? parsed.ToNormalizedString().ToLowerInvariant()
            : version.ToLowerInvariant();

    private static async Task<SourceMetadataResult> FetchAllMetadataFromSourceAsync(
        HttpClient client,
        PackageSource source,
        string normalizedName,
        string version,
        Action<string>? log,
        HttpClient untrustedClient)
    {
        IReadOnlyList<ServiceResource>? resources =
            await PackageExtractor.GetServiceIndexResourcesAsync(
                client,
                source,
                log).ConfigureAwait(false);
        if (resources is null)
        {
            return new SourceMetadataResult(SourcePresence.Indeterminate);
        }

        var metadata = new PackageMetadata();

        List<ServiceResource> registrationResources =
            PackageExtractor.GetCompatibleServiceResources(
                resources,
                "RegistrationsBaseUrl");
        List<ServiceResource> packageBaseAddresses =
            PackageExtractor.GetCompatibleServiceResources(
                resources,
                "PackageBaseAddress");
        List<ServiceResource> searchQueryServices =
            PackageExtractor.GetCompatibleSearchServiceResources(resources);
        List<ServiceResource> vulnerabilityInfos =
            PackageExtractor.GetCompatibleServiceResources(
                resources,
                "VulnerabilityInfo");
        metadata.DeprecationMetadataSupported =
            registrationResources.Count > 0
            || searchQueryServices.Count > 0;
        metadata.DeprecationMetadataAvailable =
            !metadata.DeprecationMetadataSupported;

        bool found = false;
        bool sawExistenceEndpoint = false;
        bool sawIndeterminate = false;
        foreach (ServiceResource registration in registrationResources)
        {
            sawExistenceEndpoint = true;
            try
            {
                SourceMetadataResult registrationResult =
                    await FetchRegistrationMetadataAsync(
                        client,
                        untrustedClient,
                        source,
                        registration,
                        normalizedName,
                        version,
                        log).ConfigureAwait(false);
                if (registrationResult.Metadata is { } registrationMetadata)
                {
                    metadata = registrationMetadata;
                    found = true;
                    break;
                }
                sawIndeterminate |=
                    registrationResult.Presence == SourcePresence.Indeterminate;
            }
            catch (Exception ex) when (ex is not NetworkPolicyException
                && ex is (JsonException
                    or InvalidOperationException
                    or UriFormatException))
            {
                log?.Invoke(
                    $"Invalid registration metadata from {source.Name} ({ex.GetType().Name}).");
                sawIndeterminate = true;
            }
        }

        if (packageBaseAddresses.Count > 0)
        {
            sawExistenceEndpoint = true;
            foreach (ServiceResource packageBaseAddress in packageBaseAddresses)
            {
                PackageProbeResult probe = await ProbePackageAsync(
                    client,
                    source,
                    packageBaseAddress,
                    normalizedName,
                    version,
                    log,
                    untrustedClient).ConfigureAwait(false);
                if (probe.Presence == SourcePresence.Present)
                {
                    found = true;
                    metadata.PackageSize = probe.PackageSize;
                    break;
                }
                if (probe.Presence == SourcePresence.Indeterminate)
                {
                    sawIndeterminate = true;
                }
            }
        }

        if (!found)
        {
            return sawExistenceEndpoint && !sawIndeterminate
                ? new SourceMetadataResult(SourcePresence.Absent)
                : new SourceMetadataResult(SourcePresence.Indeterminate);
        }

        bool searchDataAvailable = searchQueryServices.Count == 0;
        if (searchQueryServices.Count > 0)
        {
            foreach (ServiceResource searchQueryService in searchQueryServices)
            {
                try
                {
                    SearchFetchResult searchResult =
                        await FetchSearchMetadataAsync(
                                client,
                                source,
                                searchQueryService,
                                normalizedName,
                                version,
                                metadata,
                                log,
                                untrustedClient)
                            .ConfigureAwait(false);
                    if (searchResult.Succeeded)
                    {
                        searchDataAvailable = true;
                        if (!metadata.DeprecationMetadataAvailable)
                            sawIndeterminate |= searchResult.Indeterminate;
                        if (searchResult.DeprecationMetadataAvailable)
                            metadata.DeprecationMetadataAvailable = true;
                        break;
                    }
                }
                catch (Exception ex) when (ex is not NetworkPolicyException)
                {
                    log?.Invoke(
                        $"Error fetching search metadata from "
                        + NetworkRequestObservation.RedactSensitiveUrlText(
                            searchQueryService.Id)
                        + $" ({ex.GetType().Name})");
                }
            }
        }

        bool vulnerabilityDataAvailable = vulnerabilityInfos.Count == 0;
        List<PackageVulnerability> partialVulnerabilities = [];
        if (vulnerabilityInfos.Count > 0)
        {
            foreach (ServiceResource vulnerabilityInfo in vulnerabilityInfos)
            {
                try
                {
                    VulnerabilityFetchResult result =
                        await GetPackageVulnerabilitiesAsync(
                            client,
                            source,
                            vulnerabilityInfo.Id,
                            normalizedName,
                            version,
                            log,
                            untrustedClient).ConfigureAwait(false);
                    if (!result.Succeeded)
                    {
                        if (result.Vulnerabilities.Count
                            > partialVulnerabilities.Count)
                        {
                            partialVulnerabilities =
                                result.Vulnerabilities;
                        }
                        continue;
                    }

                    vulnerabilityDataAvailable = true;
                    metadata.Vulnerabilities ??= [];
                    metadata.Vulnerabilities.AddRange(
                        result.Vulnerabilities);
                    break;
                }
                catch (Exception ex) when (ex is not NetworkPolicyException)
                {
                    log?.Invoke(
                        $"Error fetching vulnerability data from "
                        + $"{vulnerabilityInfo.Id}: {ex.Message}");
                }
            }
        }

        if (!vulnerabilityDataAvailable
            && partialVulnerabilities.Count > 0)
        {
            metadata.Vulnerabilities = partialVulnerabilities;
        }

        return new SourceMetadataResult(
            SourcePresence.Present,
            metadata,
            Cacheable: !sawIndeterminate
                && searchDataAvailable
                && vulnerabilityDataAvailable);
    }

    private static async Task<TextFetchResult> FetchTextAsync(
        HttpClient client,
        HttpClient untrustedClient,
        PackageSource source,
        string url,
        NetworkTrafficKind trafficKind,
        Action<string>? log,
        bool preservePathAndQuery = false)
    {
        HttpClient endpointClient = NuGetCredentialScope.IsSameOrigin(
            source.Url,
            url)
                ? client
                : untrustedClient;
        // Bound like GetStringWithRetryAsync: hostile registration/search/index
        // documents must not force an unbounded string allocation. Preserve
        // StatusCode so 404 stays Present/Absent (not Indeterminate).
        HttpRetryHelper.HttpBodyFetchResult body =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                endpointClient,
                url,
                static _ => true,
                log: log,
                auth: NuGetCredentialScope.AuthFor(source, url, log),
                trafficKind: trafficKind,
                maxDownloadSize: HttpRetryHelper.DefaultMaxTextResponseBytes,
                preservePathAndQuery: preservePathAndQuery)
                .ConfigureAwait(false);
        if (body.Status != HttpRetryHelper.HttpBodyFetchStatus.Success
            || body.Bytes is null)
        {
            return new TextFetchResult(null, body.StatusCode);
        }

        return new TextFetchResult(
            System.Text.Encoding.UTF8.GetString(body.Bytes),
            body.StatusCode ?? HttpStatusCode.OK);
    }

    private static async Task<PackageProbeResult> ProbePackageAsync(
        HttpClient client,
        PackageSource source,
        ServiceResource packageBaseAddress,
        string normalizedName,
        string version,
        Action<string>? log,
        HttpClient untrustedClient)
    {
        string nupkgUrl = AppendPath(
            packageBaseAddress.Id,
            normalizedName,
            version,
            $"{normalizedName}.{version}.nupkg");
        log?.Invoke($"Fetching package size from {source.Name}: {nupkgUrl}");

        AuthenticationHeaderValue? auth =
            NuGetCredentialScope.AuthFor(source, nupkgUrl, log);
        HttpRetryHelper.HttpRetryResult sizeResult =
            await HttpRetryHelper.GetWithRetryResultAsync(
                NuGetCredentialScope.IsSameOrigin(source.Url, nupkgUrl)
                    ? client
                    : untrustedClient,
                nupkgUrl,
                log: log,
                trafficKind: NetworkTrafficKind.PackageSizeProbe,
                auth: auth,
                range: new RangeHeaderValue(0, 0)).ConfigureAwait(false);
        using HttpResponseMessage? response = sizeResult.Response;
        if (response is null)
        {
            return new PackageProbeResult(
                sizeResult.IsNotFound
                    ? SourcePresence.Absent
                    : SourcePresence.Indeterminate);
        }

        if (response.StatusCode == HttpStatusCode.OK
            && string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "text/html",
                StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke(
                $"Package endpoint returned HTML instead of package content: "
                + $"{nupkgUrl}");
            return new PackageProbeResult(SourcePresence.Indeterminate);
        }

        long? contentLength = response.StatusCode
            == HttpStatusCode.PartialContent
                ? response.Content.Headers.ContentRange?.Length
                : response.Content.Headers.ContentLength;
        return new PackageProbeResult(
            SourcePresence.Present,
            contentLength);
    }

    private static async Task<SearchFetchResult> FetchSearchMetadataAsync(
        HttpClient client,
        PackageSource source,
        ServiceResource searchQueryService,
        string normalizedName,
        string version,
        PackageMetadata metadata,
        Action<string>? log,
        HttpClient untrustedClient)
    {
        const int PageSize = 20;
        const int MaxSearchResults = 1000;
        int examined = 0;

        while (examined < MaxSearchResults)
        {
            if (!NuGetFetch.SearchRequestUri.TryCompose(
                    searchQueryService.Id,
                    [
                        ("q", normalizedName),
                        ("skip", examined.ToString(CultureInfo.InvariantCulture)),
                        ("take", PageSize.ToString(CultureInfo.InvariantCulture)),
                        ("prerelease", "true"),
                        ("semVerLevel", "2.0.0"),
                    ],
                    out string searchUrl))
            {
                log?.Invoke(
                    $"The search endpoint for {source.Name} is not a usable absolute HTTP or HTTPS URL.");
                return new SearchFetchResult(
                    Succeeded: false,
                    Found: false,
                    DeprecationMetadataAvailable: false);
            }

            log?.Invoke(
                $"Fetching search metadata from {source.Name}: "
                + NetworkRequestObservation.RedactSensitiveUrlText(searchUrl));

            TextFetchResult searchResult = await FetchTextAsync(
                client,
                untrustedClient,
                source,
                searchUrl,
                NetworkTrafficKind.PackageMetadata,
                log,
                preservePathAndQuery: true).ConfigureAwait(false);
            if (!searchResult.IsSuccess)
                return new SearchFetchResult(
                    Succeeded: false,
                    Found: false,
                    DeprecationMetadataAvailable: false);

            SearchPageResult page = ApplySearchMetadata(
                searchResult.Content!,
                normalizedName,
                version,
                metadata,
                log);
            if (page.Found)
                return new SearchFetchResult(
                    Succeeded: true,
                    Found: true,
                    page.DeprecationMetadataAvailable,
                    page.Indeterminate);
            if (page.ResultCount == 0)
                return new SearchFetchResult(
                    Succeeded: true,
                    Found: false,
                    DeprecationMetadataAvailable: false);

            examined += page.ResultCount;
            if (page.TotalHits is > 0 && examined >= page.TotalHits)
                return new SearchFetchResult(
                    Succeeded: true,
                    Found: false,
                    DeprecationMetadataAvailable: false);
        }

        return new SearchFetchResult(
            Succeeded: true,
            Found: false,
            DeprecationMetadataAvailable: false);
    }

    private static async Task<SourceMetadataResult> FetchRegistrationMetadataAsync(
        HttpClient client,
        HttpClient untrustedClient,
        PackageSource source,
        ServiceResource registration,
        string normalizedName,
        string version,
        Action<string>? log)
    {
        const int MaximumPageCount = 128;
        string indexUrl = AppendPath(registration.Id, normalizedName, "index.json");
        log?.Invoke(
            "Fetching registration index: "
            + NetworkRequestObservation.RedactSensitiveUrlText(indexUrl));
        TextFetchResult indexResult = await FetchTextAsync(
            client, untrustedClient, source, indexUrl,
            NetworkTrafficKind.PackageMetadata, log).ConfigureAwait(false);
        if (!indexResult.IsSuccess)
        {
            return new SourceMetadataResult(
                indexResult.IsNotFound
                    ? SourcePresence.Absent
                    : SourcePresence.Indeterminate);
        }

        using var index = HardenedJson.Parse(indexResult.Content!);
        JsonElement pages = GetRegistrationItems(index.RootElement);
        if (pages.GetArrayLength() > MaximumPageCount)
            throw new JsonException("Registration index exceeded the 128-page limit.");
        if (!NuGetVersion.TryParse(version, out NuGetVersion? requestedVersion))
            throw new JsonException("Registration lookup requires an exact NuGet version.");

        foreach (JsonElement page in pages.EnumerateArray())
        {
            NuGetVersion lower = ReadRegistrationVersion(page, "lower");
            NuGetVersion upper = ReadRegistrationVersion(page, "upper");
            if (VersionComparer.VersionRelease.Compare(lower, upper) > 0)
                throw new JsonException("Registration page bounds are reversed.");
            if (VersionComparer.VersionRelease.Compare(requestedVersion, lower) < 0
                || VersionComparer.VersionRelease.Compare(requestedVersion, upper) > 0)
                continue;

            PackageMetadata? metadata;
            if (page.TryGetProperty("items", out _))
            {
                metadata = FindRegistrationMetadata(
                    page, normalizedName, version, log);
            }
            else
            {
                if (!page.TryGetProperty("@id", out JsonElement pageId)
                    || pageId.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(pageId.GetString()))
                    throw new JsonException("Registration page has no link.");

                string pageUrl = ResolveReference(indexUrl, pageId.GetString()!);
                if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri? uri)
                    || uri.Scheme is not ("http" or "https"))
                    throw new JsonException("Registration page link must use HTTP or HTTPS.");

                log?.Invoke(
                    "Fetching registration page: "
                    + NetworkRequestObservation.RedactSensitiveUrlText(pageUrl));
                TextFetchResult pageResult = await FetchTextAsync(
                    client, untrustedClient, source, pageUrl,
                    NetworkTrafficKind.PackageMetadata, log,
                    preservePathAndQuery: true).ConfigureAwait(false);
                if (!pageResult.IsSuccess)
                {
                    // A broken advertised link does not prove version absence.
                    log?.Invoke("The selected registration page is unavailable.");
                    return new SourceMetadataResult(SourcePresence.Indeterminate);
                }

                using var pageDocument = HardenedJson.Parse(pageResult.Content!);
                metadata = FindRegistrationMetadata(
                    pageDocument.RootElement, normalizedName, version, log);
            }
            if (metadata is not null)
                return new SourceMetadataResult(SourcePresence.Present, metadata);
        }

        return new SourceMetadataResult(SourcePresence.Absent);
    }

    private static JsonElement GetRegistrationItems(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty("items", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
            throw new JsonException("Registration document has no items array.");
        return items;
    }

    private static NuGetVersion ReadRegistrationVersion(
        JsonElement element,
        string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || !NuGetVersion.TryParse(value.GetString(), out NuGetVersion? parsed))
            throw new JsonException("Registration metadata has an invalid version.");
        return parsed;
    }

    private static PackageMetadata? FindRegistrationMetadata(
        JsonElement page,
        string normalizedName,
        string version,
        Action<string>? log)
    {
        foreach (JsonElement leaf in GetRegistrationItems(page).EnumerateArray())
        {
            if (leaf.ValueKind != JsonValueKind.Object
                || !leaf.TryGetProperty("catalogEntry", out JsonElement catalog)
                || catalog.ValueKind != JsonValueKind.Object)
                throw new JsonException("Registration leaf has no embedded catalog entry.");
            NuGetVersion catalogVersion = ReadRegistrationVersion(catalog, "version");
            if (!catalog.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String
                || !string.Equals(
                    id.GetString(),
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase))
                throw new JsonException("Registration metadata has a different package identity.");
            if (!VersionsEqual(catalogVersion.ToNormalizedString(), version))
                continue;

            var metadata = new PackageMetadata
            {
                DeprecationMetadataSupported = true,
                DeprecationMetadataAvailable = true,
            };
            ApplyCatalogElement(catalog, metadata, log);
            return metadata;
        }
        return null;
    }

    private static void ApplyCatalogElement(
        JsonElement root,
        PackageMetadata metadata,
        Action<string>? log)
    {
        ApplyListingState(root, metadata, log);

        if (root.TryGetProperty("published", out JsonElement publishedElement)
            && DateTimeOffset.TryParse(
                publishedElement.GetString(),
                out DateTimeOffset published))
        {
            metadata.Published ??= published;
        }

        if (root.TryGetProperty("deprecation", out JsonElement deprecationElement)
            && deprecationElement.ValueKind == JsonValueKind.Object)
        {
            metadata.Deprecation = ParseDeprecation(deprecationElement);
            log?.Invoke($"Deprecation: {metadata.Deprecation.Summary}");
        }
    }

    private static void ApplyListingState(
        JsonElement root,
        PackageMetadata metadata,
        Action<string>? log)
    {
        if (!root.TryGetProperty("listed", out JsonElement listedElement)
            || listedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        metadata.Listed = listedElement.GetBoolean();
        log?.Invoke($"Listed: {metadata.Listed.Value}");
    }

    private static SearchPageResult ApplySearchMetadata(
        string json,
        string normalizedName,
        string version,
        PackageMetadata metadata,
        Action<string>? log)
    {
        using var doc = HardenedJson.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "Search metadata did not contain a data array.");
        }

        int resultCount = data.GetArrayLength();
        long? totalHits =
            doc.RootElement.TryGetProperty(
                "totalHits",
                out JsonElement totalHitsElement)
            && TryReadInt64(totalHitsElement, out long parsedTotalHits)
                ? parsedTotalHits
                : null;
        JsonElement? package = data
            .EnumerateArray()
            .FirstOrDefault(item =>
                item.TryGetProperty("id", out JsonElement id)
                && string.Equals(
                    id.GetString(),
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase));
        if (package is not JsonElement pkg
            || pkg.ValueKind != JsonValueKind.Object)
        {
            return new SearchPageResult(
                Found: false,
                resultCount,
                totalHits,
                DeprecationMetadataAvailable: false);
        }

        bool deprecationMetadataAvailable =
            pkg.TryGetProperty(
                "version",
                out JsonElement packageVersion)
            && VersionsEqual(packageVersion.GetString(), version);

        if (pkg.TryGetProperty("totalDownloads", out JsonElement downloads)
            && TryReadInt64(downloads, out long totalDownloads))
        {
            metadata.TotalDownloads = totalDownloads;
        }

        if (pkg.TryGetProperty("versions", out JsonElement versions)
            && versions.ValueKind == JsonValueKind.Array)
        {
            metadata.VersionCount = versions.GetArrayLength();

            foreach (JsonElement item in versions.EnumerateArray())
            {
                if (item.TryGetProperty("version", out JsonElement versionElement)
                    && VersionsEqual(versionElement.GetString(), version)
                    && item.TryGetProperty(
                        "downloads",
                        out JsonElement versionDownloads)
                    && TryReadInt64(
                        versionDownloads,
                        out long downloadCount))
                {
                    metadata.VersionDownloads = downloadCount;
                    break;
                }
            }
        }

        if (pkg.TryGetProperty("verified", out JsonElement verified)
            && verified.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            metadata.IsVerified = verified.GetBoolean();
        }

        if (pkg.TryGetProperty("owners", out JsonElement owners))
        {
            metadata.Owners = owners.ValueKind switch
            {
                JsonValueKind.Array =>
                [
                    .. owners
                        .EnumerateArray()
                        .Select(owner => owner.GetString())
                        .Where(owner => !string.IsNullOrWhiteSpace(owner))
                        .Cast<string>(),
                ],
                JsonValueKind.String =>
                [
                    .. (owners.GetString() ?? "")
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries
                                | StringSplitOptions.TrimEntries),
                ],
                _ => metadata.Owners,
            };
        }

        if (deprecationMetadataAvailable
            && metadata.Deprecation is null
            && pkg.TryGetProperty(
                "deprecation",
                out JsonElement deprecationElement)
            && deprecationElement.ValueKind == JsonValueKind.Object)
        {
            metadata.Deprecation = ParseDeprecation(deprecationElement);
            log?.Invoke($"Deprecation: {metadata.Deprecation.Summary}");
        }

        return new SearchPageResult(
            Found: true,
            resultCount,
            totalHits,
            deprecationMetadataAvailable,
            Indeterminate: !deprecationMetadataAvailable);
    }

    private static bool TryReadInt64(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt64(out value);
        }

        value = default;
        return element.ValueKind == JsonValueKind.String
            && long.TryParse(element.GetString(), out value);
    }

    private static bool VersionsEqual(string? left, string right) =>
        NuGetVersion.TryParse(left, out NuGetVersion? parsedLeft)
        && NuGetVersion.TryParse(right, out NuGetVersion? parsedRight)
            ? parsedLeft == parsedRight
            : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string AppendPath(string baseUrl, params string[] segments)
    {
        var builder = new UriBuilder(baseUrl);
        string suffix = string.Join(
            '/',
            segments.Select(Uri.EscapeDataString));
        builder.Path = $"{builder.Path.TrimEnd('/')}/{suffix}";
        return builder.Uri.AbsoluteUri;
    }

    // Keep escaping intact for callers that preserve the advertised request target.
    private static string ResolveReference(string baseUrl, string reference) =>
        new Uri(new Uri(baseUrl, UriKind.Absolute), reference).OriginalString;

    internal static PackageDeprecation ParseDeprecation(JsonElement deprecationElement)
    {
        var deprecation = new PackageDeprecation();

        if (deprecationElement.TryGetProperty("reasons", out var reasons))
        {
            deprecation.Reasons = reasons.EnumerateArray()
                .Select(r => r.GetString())
                .Where(r => r != null)
                .Cast<string>()
                .ToList();
        }
        if (deprecationElement.TryGetProperty("message", out var message))
        {
            deprecation.Message = message.GetString();
        }
        if (deprecationElement.TryGetProperty("alternatePackage", out var altPkg) &&
            altPkg.TryGetProperty("id", out var altId))
        {
            deprecation.AlternatePackageId = altId.GetString();
        }

        return deprecation;
    }

    private static async Task<VulnerabilityFetchResult> GetPackageVulnerabilitiesAsync(
        HttpClient client,
        PackageSource source,
        string indexUrl,
        string packageName,
        string version,
        Action<string>? log,
        HttpClient untrustedClient)
    {
        List<PackageVulnerability> result = [];

        if (!NuGet.Versioning.NuGetVersion.TryParse(version, out var packageVersion))
        {
            log?.Invoke($"Could not parse version: {version}");
            return new VulnerabilityFetchResult(
                Succeeded: false,
                result);
        }

        TextFetchResult indexResult = await FetchTextAsync(
            client,
            untrustedClient,
            source,
            indexUrl,
            NetworkTrafficKind.VulnerabilityData,
            log).ConfigureAwait(false);
        if (!indexResult.IsSuccess)
        {
            return new VulnerabilityFetchResult(
                Succeeded: false,
                result);
        }

        using var indexDoc = HardenedJson.Parse(indexResult.Content!);
        bool allPagesSucceeded = true;
        if (indexDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new VulnerabilityFetchResult(
                Succeeded: false,
                result);
        }

        List<string> pages = [];
        foreach (JsonElement entry in indexDoc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("@id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString()))
            {
                pages.Add(id.GetString()!);
            }
            else
            {
                allPagesSucceeded = false;
            }
        }

        foreach (string pageUrl in pages)
        {
            string resolvedPageUrl;
            try
            {
                resolvedPageUrl = ResolveReference(indexUrl, pageUrl);
            }
            catch (UriFormatException ex)
            {
                log?.Invoke(
                    $"Invalid vulnerability page reference from "
                    + $"{source.Name}: {ex.Message}");
                allPagesSucceeded = false;
                continue;
            }

            log?.Invoke($"Fetching vulnerability page: {resolvedPageUrl}");
            TextFetchResult pageResult = await FetchTextAsync(
                client,
                untrustedClient,
                source,
                resolvedPageUrl,
                NetworkTrafficKind.VulnerabilityData,
                log).ConfigureAwait(false);
            if (!pageResult.IsSuccess)
            {
                allPagesSucceeded = false;
                continue;
            }

            try
            {
                using var pageDoc = HardenedJson.Parse(pageResult.Content!);

                if (pageDoc.RootElement.TryGetProperty(
                        packageName,
                        out JsonElement vulnArray))
                {
                    if (vulnArray.ValueKind != JsonValueKind.Array)
                    {
                        allPagesSucceeded = false;
                        continue;
                    }

                    foreach (JsonElement vuln in vulnArray.EnumerateArray())
                    {
                        if (vuln.ValueKind != JsonValueKind.Object)
                        {
                            allPagesSucceeded = false;
                            continue;
                        }

                        string? versionsRange =
                            vuln.TryGetProperty("versions", out JsonElement range)
                            && range.ValueKind == JsonValueKind.String
                                ? range.GetString()
                                : null;
                        long severityValue = default;
                        bool hasSeverity =
                            vuln.TryGetProperty(
                                "severity",
                                out JsonElement severity)
                            && TryReadInt64(
                                severity,
                                out severityValue)
                            && severityValue is >= 0 and <= 3;
                        string? advisoryUrl =
                            vuln.TryGetProperty("url", out JsonElement advisory)
                            && advisory.ValueKind == JsonValueKind.String
                                ? advisory.GetString()
                                : null;

                        if (string.IsNullOrWhiteSpace(versionsRange)
                            || !hasSeverity
                            || string.IsNullOrWhiteSpace(advisoryUrl)
                            || !NuGet.Versioning.VersionRange.TryParse(
                                versionsRange,
                                out NuGet.Versioning.VersionRange? affectedVersions))
                        {
                            allPagesSucceeded = false;
                            continue;
                        }

                        if (!affectedVersions.Satisfies(packageVersion))
                        {
                            continue;
                        }

                        var vulnerability = new PackageVulnerability
                        {
                            Severity = SeverityToString((int)severityValue),
                            AdvisoryUrl = advisoryUrl,
                        };

                        if (advisoryUrl.Contains(
                                "github.com/advisories/GHSA-",
                                StringComparison.Ordinal))
                        {
                            string? ghsaId = ExtractGhsaId(advisoryUrl);
                            if (ghsaId is not null)
                            {
                                vulnerability.GhsaId = ghsaId;
                                await EnrichFromGitHubAdvisoryAsync(
                                    client,
                                    vulnerability,
                                    ghsaId,
                                    log).ConfigureAwait(false);
                            }
                        }

                        result.Add(vulnerability);
                    }
                }
            }
            catch (Exception ex) when (ex is
                JsonException
                or InvalidOperationException)
            {
                log?.Invoke(
                    $"Invalid vulnerability page from "
                    + $"{source.Name}: {ex.Message}");
                allPagesSucceeded = false;
            }
        }

        return new VulnerabilityFetchResult(
            allPagesSucceeded,
            result);
    }

    private static string? ExtractGhsaId(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}");
        return match.Success ? match.Value : null;
    }

    private static async Task EnrichFromGitHubAdvisoryAsync(HttpClient client, PackageVulnerability vuln, string ghsaId, Action<string>? log)
    {
        try
        {
            string apiUrl = $"https://api.github.com/advisories/{ghsaId}";
            log?.Invoke($"Fetching GitHub advisory: {InertText.UrlRedaction.ForDiagnostics(apiUrl)}");

            // Bound like discovery JSON GETs — advisory documents must not force
            // an unbounded ReadAsStringAsync allocation.
            string? json = await HttpRetryHelper.GetStringWithRetryAsync(
                client,
                apiUrl,
                log: log,
                trafficKind: NetworkTrafficKind.AdvisoryData,
                maxDownloadSize: HttpRetryHelper.DefaultMaxTextResponseBytes,
                configureRequest: static request =>
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "dotnet-inspect");
                    request.Headers.TryAddWithoutValidation(
                        "Accept",
                        "application/vnd.github+json");
                }).ConfigureAwait(false);
            if (json is null)
            {
                log?.Invoke("GitHub advisory fetch failed or exceeded size cap.");
                return;
            }

            using var doc = HardenedJson.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("cve_id", out var cveId) && cveId.ValueKind == JsonValueKind.String)
                vuln.CveId = cveId.GetString();

            if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String)
                vuln.Summary = summary.GetString();

            if (root.TryGetProperty("severity", out var severity) && severity.ValueKind == JsonValueKind.String)
            {
                var sev = severity.GetString();
                if (!string.IsNullOrEmpty(sev))
                    vuln.Severity = char.ToUpper(sev[0]) + sev[1..].ToLower();
            }
        }
        catch (Exception ex) when (ex is not NetworkPolicyException)
        {
            log?.Invoke($"Error fetching GitHub advisory: {ex.Message}");
        }
    }

    internal static bool IsVersionInRange(NuGet.Versioning.NuGetVersion version, string rangeString)
    {
        if (NuGet.Versioning.VersionRange.TryParse(rangeString, out var range))
        {
            return range.Satisfies(version);
        }
        return false;
    }

    internal static string SeverityToString(int severity)
    {
        return severity switch
        {
            0 => "Low",
            1 => "Moderate",
            2 => "High",
            3 => "Critical",
            _ => "Unknown"
        };
    }
}
