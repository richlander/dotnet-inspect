using System.Collections.Concurrent;

using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

internal enum SourceFetchFailureKind
{
    InvalidUrl,
    RequestNotAuthorized,
    NotFound,
    Unavailable,
    AttributedOriginUnverified,
    ValidationFailed,
    StorageFailed,
}

internal readonly record struct SourceFetchBytesResult(
    byte[]? Bytes,
    SourceFetchFailureKind? Failure = null);

/// <summary>
/// Fetches checksum-gated source bytes through a host-selected content store.
/// The compatibility constructor uses the process-wide disk cache; content-only
/// hosts can supply <see cref="InMemorySourceContentStore"/>.
/// </summary>
public class SourceFetcher
{
    private readonly ConcurrentDictionary<string, byte[]> _byteMemoryCache = new();
    private readonly HttpClient _httpClient;
    private readonly ISourceContentStore _contentStore;
    private readonly ISourceFetchPolicy? _fetchPolicy;
    private const string ByteCacheCategory = "source-bytes-v2";
    internal const long MaxSourceDownloadSize = 16_000_000;

    public SourceFetcher(HttpClient httpClient)
        : this(httpClient, CoreCacheSourceContentStore.Instance)
    {
    }

    public SourceFetcher(
        HttpClient httpClient,
        ISourceContentStore contentStore,
        ISourceFetchPolicy? fetchPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(contentStore);
        _httpClient = httpClient;
        _contentStore = contentStore;
        _fetchPolicy = fetchPolicy;
    }

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
        if (_fetchPolicy is not null
            && !_fetchPolicy.IsRequestAllowed(parsed))
        {
            return new SourceFetchBytesResult(
                null,
                SourceFetchFailureKind.RequestNotAuthorized);
        }

        if (_byteMemoryCache.TryGetValue(url, out var memoryBytes))
        {
            if (validator(memoryBytes))
                return new SourceFetchBytesResult(memoryBytes);

            _byteMemoryCache.TryRemove(url, out _);
        }

        byte[]? cachedBytes;
        try
        {
            cachedBytes =
                await _contentStore.TryOpenAsync(
                    url,
                    cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (IsContentStoreFailure(ex))
        {
            return new SourceFetchBytesResult(
                null,
                SourceFetchFailureKind.StorageFailed);
        }

        if (cachedBytes is not null)
        {
            if (validator(cachedBytes))
            {
                _byteMemoryCache[url] = cachedBytes;
                return new SourceFetchBytesResult(cachedBytes);
            }
        }

        try
        {
            Action<HttpRequestMessage>? configureRequest =
                _fetchPolicy is null
                    ? null
                    : request => _fetchPolicy.ConfigureRequest(request);
            HttpRetryHelper.HttpBodyFetchResult fetch =
                await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                _httpClient,
                url,
                response => SourceFetchOriginValidator.Validate(
                    url,
                    response.RequestMessage?.RequestUri?.AbsoluteUri,
                    _fetchPolicy?.FinalResponseUriIsReliable
                        ?? !OperatingSystem.IsBrowser()).IsAllowed,
                cancellationToken: cancellationToken,
                trafficKind: NetworkTrafficKind.SourceFetch,
                maxDownloadSize: MaxSourceDownloadSize,
                configureRequest: configureRequest)
                .ConfigureAwait(false);
            if (fetch.Status == HttpRetryHelper.HttpBodyFetchStatus.ResponseRejected)
            {
                return new SourceFetchBytesResult(
                    null,
                    SourceFetchFailureKind.AttributedOriginUnverified);
            }
            if (fetch.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new SourceFetchBytesResult(null, SourceFetchFailureKind.NotFound);
            if (fetch.Bytes is not { } bytes)
                return new SourceFetchBytesResult(null, SourceFetchFailureKind.Unavailable);

            if (!validator(bytes))
                return new SourceFetchBytesResult(null, SourceFetchFailureKind.ValidationFailed);

            try
            {
                await _contentStore.StoreAsync(
                    url,
                    bytes,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (IsContentStoreFailure(ex))
            {
                return new SourceFetchBytesResult(
                    null,
                    SourceFetchFailureKind.StorageFailed);
            }

            _byteMemoryCache[url] = bytes;
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

    static bool IsContentStoreFailure(Exception exception)
        => exception is not (OperationCanceledException
            or OutOfMemoryException
            or StackOverflowException
            or AccessViolationException);

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

    sealed class CoreCacheSourceContentStore
        : ISourceContentStore
    {
        internal static CoreCacheSourceContentStore Instance { get; } =
            new();

        public ValueTask<byte[]?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? encoded = CoreCache.TryGet(
                ByteCacheCategory,
                key,
                extension: "base64");
            if (encoded is null)
                return ValueTask.FromResult<byte[]?>(null);

            try
            {
                return ValueTask.FromResult<byte[]?>(
                    Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                return ValueTask.FromResult<byte[]?>(null);
            }
        }

        public ValueTask StoreAsync(
            string key,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CoreCache.Set(
                ByteCacheCategory,
                key,
                Convert.ToBase64String(content.Span),
                extension: "base64");
            return ValueTask.CompletedTask;
        }
    }
}
