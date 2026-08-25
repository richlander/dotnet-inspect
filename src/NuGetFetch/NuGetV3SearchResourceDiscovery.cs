using System.Text.Json;

namespace NuGetFetch;

/// <summary>
/// Discovers compatible NuGet v3 search resources under source-owned bounds.
/// </summary>
/// <remarks>
/// Only the highest supported capability tier is used, with at most four
/// equivalent endpoints retained in service-index order.
/// <c>V3SearchUsesHighestCompatibleResourcesAndFailsOver</c>,
/// <c>V3SearchNormalizesAdvertisedUnicodeEndpoint</c>,
/// <c>V3MalformedAdvertisedSearchIsTypedInvalidResponse</c>, and
/// <c>V3SearchUsesLibraryDeadline</c> gate these properties.
/// </remarks>
internal static class NuGetV3SearchResourceDiscovery
{
    private const int MaxEquivalentSearchEndpoints = 4;

    internal static async Task<IReadOnlyList<string>> GetSearchEndpointsAsync(
        HttpClient client,
        Uri serviceIndex,
        PackageSourceCredential? credential,
        NuGetFetchOptions options,
        NuGetOperationDeadline operation)
    {
        string serviceIndexUrl = NuGetSourceRequest.EndpointUrl(serviceIndex);
        return await NuGetHttpRetry.RunRequestAsync(
            operation,
            async requestToken =>
            {
                using HttpRequestMessage request =
                    NuGetHttpRequest.CreateGetPreservingPathAndQuery(
                        serviceIndexUrl);
                NuGetSourceRequest.ApplyCredential(request, credential);
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                return await NuGetMetadataReader.ReadResponseAsync(
                    response,
                    ReadSearchEndpointsAsync,
                    options,
                    client.Timeout,
                    requestToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyList<string>>
        ReadSearchEndpointsAsync(
            Stream json,
            CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            json,
            new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(
                "resources",
                out JsonElement resources)
            || resources.ValueKind != JsonValueKind.Array)
        {
            throw new NuGetSourceResponseException(
                "The package source service index did not contain a resources array.");
        }

        var matches = new List<(string Endpoint, int Rank)>();
        bool hasMalformedSupportedResource = false;
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resource.ValueKind != JsonValueKind.Object
                || !resource.TryGetProperty(
                    "@type",
                    out JsonElement typeElement))
            {
                continue;
            }

            int resourceRank = -1;
            foreach (string type in ResourceTypes(
                         typeElement,
                         cancellationToken))
            {
                if (TryGetSearchServiceRank(type, out int rank)
                    && rank > resourceRank)
                {
                    resourceRank = rank;
                }
            }

            if (resourceRank < 0)
                continue;

            if (!resource.TryGetProperty(
                    "@id",
                    out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.String
                || idElement.GetString() is not { } declaredEndpoint
                || !NuGetSourceRequest.TryEndpointUrl(
                    declaredEndpoint,
                    out string endpoint)
                || !IsUsableSearchEndpoint(endpoint))
            {
                hasMalformedSupportedResource = true;
                continue;
            }

            matches.Add((endpoint, resourceRank));
        }

        if (matches.Count == 0)
        {
            if (hasMalformedSupportedResource)
            {
                throw new NuGetSourceResponseException(
                    "The package source service index advertised an unusable search endpoint.");
            }

            return [];
        }

        int bestRank = matches.Max(match => match.Rank);
        var selected = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string endpoint, int rank) in matches)
        {
            if (rank == bestRank && seen.Add(endpoint))
            {
                selected.Add(endpoint);
                if (selected.Count == MaxEquivalentSearchEndpoints)
                    break;
            }
        }

        return selected;
    }

    private static IEnumerable<string> ResourceTypes(
        JsonElement typeElement,
        CancellationToken cancellationToken)
    {
        if (typeElement.ValueKind == JsonValueKind.String
            && typeElement.GetString() is { Length: > 0 } type)
        {
            yield return type;
            yield break;
        }

        if (typeElement.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (JsonElement item in typeElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.ValueKind == JsonValueKind.String
                && item.GetString() is { Length: > 0 } itemType)
            {
                yield return itemType;
            }
        }
    }

    private static bool IsUsableSearchEndpoint(string endpoint)
    {
        if (!SearchRequestUri.TryCompose(endpoint, [], out _)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        return parsed.UserInfo.Length == 0;
    }

    private static bool TryGetSearchServiceRank(
        string type,
        out int rank)
    {
        if (type.Equals(
                "SearchQueryService/3.5.0",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 4;
            return true;
        }

        if (type.Equals(
                "SearchQueryService/3.0.0",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 3;
            return true;
        }

        if (type.Equals(
                "SearchQueryService/3.0.0-rc",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 2;
            return true;
        }

        if (type.Equals(
                "SearchQueryService/3.0.0-beta",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 1;
            return true;
        }

        if (type.Equals(
                "SearchQueryService",
                StringComparison.OrdinalIgnoreCase))
        {
            rank = 0;
            return true;
        }

        rank = -1;
        return false;
    }
}
