using System.Collections.Concurrent;

using DotnetInspector.Core;
using DotnetInspector.Packages;
using SLF = SourceLinkFetch;

namespace DotnetInspector.Services;

internal enum SourceFetchFailureKind
{
    InvalidUrl,
    Unavailable,
    AttributedOriginChanged,
    ValidationFailed,
}

internal readonly record struct SourceFetchBytesResult(
    byte[]? Bytes,
    SourceFetchFailureKind? Failure = null);

/// <summary>
/// Fetches source files from URLs with persistent disk caching and in-memory caching.
/// </summary>
public class SourceFetcher(HttpClient httpClient)
{
    private readonly ConcurrentDictionary<string, byte[]> _byteMemoryCache = new();
    private readonly HttpClient _httpClient = httpClient;
    private const string ByteCacheCategory = "source-bytes-v2";

    /// <summary>
    /// Fetches exact source bytes and returns them only when they satisfy
    /// <paramref name="validator"/>. Invalid cached bytes are bypassed; invalid network bytes are
    /// neither returned nor cached.
    /// </summary>
    internal async Task<byte[]?> FetchVerifiedSourceBytesAsync(
        string url,
        Func<ReadOnlyMemory<byte>, bool> validator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        return (await FetchSourceBytesCoreAsync(
            url,
            validator,
            cancellationToken).ConfigureAwait(false)).Bytes;
    }

    internal Task<SourceFetchBytesResult> FetchVerifiedSourceBytesResultAsync(
        string url,
        Func<ReadOnlyMemory<byte>, bool> validator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        return FetchSourceBytesCoreAsync(url, validator, cancellationToken);
    }

    private async Task<SourceFetchBytesResult> FetchSourceBytesCoreAsync(
        string url,
        Func<ReadOnlyMemory<byte>, bool> validator,
        CancellationToken cancellationToken)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SourceFetch);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps
                && parsed.Scheme != Uri.UriSchemeHttp))
        {
            return new SourceFetchBytesResult(null, SourceFetchFailureKind.InvalidUrl);
        }

        if (_byteMemoryCache.TryGetValue(url, out var memoryBytes))
        {
            if (validator(memoryBytes))
                return new SourceFetchBytesResult(memoryBytes);

            _byteMemoryCache.TryRemove(url, out _);
        }

        string? encoded = CoreCache.TryGet(
            ByteCacheCategory,
            url,
            extension: "base64");
        if (encoded is not null)
        {
            try
            {
                var cachedBytes = Convert.FromBase64String(encoded);
                if (validator(cachedBytes))
                {
                    _byteMemoryCache[url] = cachedBytes;
                    return new SourceFetchBytesResult(cachedBytes);
                }
            }
            catch (FormatException)
            {
                // A corrupt cache entry is replaced by the network result below.
            }
        }

        try
        {
            using var response = await HttpRetryHelper.GetWithRetryAsync(
                _httpClient,
                url,
                cancellationToken: cancellationToken,
                trafficKind: NetworkTrafficKind.SourceFetch).ConfigureAwait(false);
            if (response is null)
                return new SourceFetchBytesResult(null, SourceFetchFailureKind.Unavailable);

            string? finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri;
            if (finalUrl is null
                || !SLF.SourceLinkProvenance.ValidateFetchOrigin(url, finalUrl).IsAllowed)
            {
                return new SourceFetchBytesResult(
                    null,
                    SourceFetchFailureKind.AttributedOriginChanged);
            }

            const long MaxDownloadSize = 500_000_000;
            if (response.Content.Headers.ContentLength is > MaxDownloadSize)
            {
                throw new InvalidOperationException(
                    $"Download size ({response.Content.Headers.ContentLength / 1_000_000} MB) exceeds limit.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!validator(bytes))
                return new SourceFetchBytesResult(null, SourceFetchFailureKind.ValidationFailed);

            _byteMemoryCache[url] = bytes;
            CoreCache.Set(
                ByteCacheCategory,
                url,
                Convert.ToBase64String(bytes),
                extension: "base64");
            return new SourceFetchBytesResult(bytes);
        }
        catch (HttpRequestException)
        {
            return new SourceFetchBytesResult(null, SourceFetchFailureKind.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SourceFetchBytesResult(null, SourceFetchFailureKind.Unavailable);
        }
    }

    /// <summary>
    /// Extracts a named region from source content.
    /// Returns the content between #region Name and #endregion markers.
    /// </summary>
    public static string? ExtractRegion(string content, string regionName)
    {
        var lines = content.Split('\n');
        List<string> regionLines = [];
        bool inRegion = false;
        int regionDepth = 0;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            
            if (trimmed.StartsWith("#region", StringComparison.Ordinal))
            {
                if (inRegion)
                {
                    // Nested region
                    regionDepth++;
                    regionLines.Add(line);
                }
                else
                {
                    // Check if this is the region we want
                    var name = trimmed.Length > 7 ? trimmed[7..].Trim() : "";
                    if (name.Equals(regionName, StringComparison.OrdinalIgnoreCase))
                    {
                        inRegion = true;
                        regionDepth = 0;
                    }
                }
            }
            else if (trimmed.StartsWith("#endregion", StringComparison.Ordinal))
            {
                if (inRegion)
                {
                    if (regionDepth > 0)
                    {
                        regionDepth--;
                        regionLines.Add(line);
                    }
                    else
                    {
                        // End of our region
                        break;
                    }
                }
            }
            else if (inRegion)
            {
                regionLines.Add(line);
            }
        }

        if (regionLines.Count == 0)
            return null;

        // Trim common leading whitespace
        return TrimCommonIndentation(regionLines);
    }

    private static string TrimCommonIndentation(List<string> lines)
    {
        // Find minimum indentation (ignoring empty lines)
        int minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            int indent = line.TakeWhile(char.IsWhiteSpace).Count();
            minIndent = Math.Min(minIndent, indent);
        }

        if (minIndent == int.MaxValue || minIndent == 0)
            return string.Join('\n', lines).TrimEnd();

        // Remove common indentation
        List<string> result = [];
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                result.Add("");
            else
                result.Add(line[minIndent..]);
        }

        return string.Join('\n', result).TrimEnd();
    }
}
