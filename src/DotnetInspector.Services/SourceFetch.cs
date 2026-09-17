using DotnetInspector.Cache;
using System.Collections.Concurrent;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

internal enum SourceError
{
    InvalidUrl,
    RequestNotAuthorized,
    NotFound,
    Unavailable,
    ValidationFailed,
    StorageFailed,
}

internal abstract record FetchSourceResult
{
    private protected FetchSourceResult()
    {
    }

    internal sealed record Success(byte[] Content) : FetchSourceResult;

    internal sealed record Failure(SourceError Error) : FetchSourceResult;
}

/// <summary>
/// Fetches caller-validated source bytes through a host-selected content store.
/// The compatibility constructor uses the process-wide disk cache; content-only
/// hosts can supply <see cref="InMemorySourceContentStore"/>.
/// </summary>
public class SourceFetch
{
    private readonly ConcurrentDictionary<string, byte[]> _byteMemoryCache = new();
    private readonly HttpClient _httpClient;
    private readonly ISourceContentStore _contentStore;
    private readonly ISourceFetchPolicy? _fetchPolicy;
    private const string ByteCacheCategory = "source-bytes-v2";
    internal const long MaxSourceDownloadSize = 16_000_000;

    public SourceFetch(HttpClient httpClient)
        : this(httpClient, PersistentCacheSourceContentStore.Instance)
    {
    }

    public SourceFetch(
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
    internal Task<FetchSourceResult> FetchVerifiedSourceBytesAsync(
        string url,
        Func<ReadOnlyMemory<byte>, bool> validator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        return FetchSourceBytesCoreAsync(url, validator, cancellationToken);
    }

    private async Task<FetchSourceResult> FetchSourceBytesCoreAsync(
        string url,
        Func<ReadOnlyMemory<byte>, bool> validator,
        CancellationToken cancellationToken)
    {
        using var trafficScope = NetworkTelemetry.Scope(NetworkTrafficKind.SourceFetch);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps
                && parsed.Scheme != Uri.UriSchemeHttp))
        {
            return new FetchSourceResult.Failure(SourceError.InvalidUrl);
        }
        if (_fetchPolicy is not null)
        {
            bool allowed = _fetchPolicy.IsRequestAllowed(parsed);
            cancellationToken.ThrowIfCancellationRequested();
            if (!allowed)
            {
                return new FetchSourceResult.Failure(
                    SourceError.RequestNotAuthorized);
            }
        }

        if (_byteMemoryCache.TryGetValue(url, out var memoryBytes))
        {
            bool valid = validator(memoryBytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (valid)
                return new FetchSourceResult.Success(memoryBytes);

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
            return new FetchSourceResult.Failure(
                SourceError.StorageFailed);
        }

        if (cachedBytes is not null)
        {
            bool valid = validator(cachedBytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (valid)
            {
                _byteMemoryCache[url] = cachedBytes;
                return new FetchSourceResult.Success(cachedBytes);
            }
        }

        Action<HttpRequestMessage>? configureRequest =
            _fetchPolicy is null
                ? null
                : request => _fetchPolicy.ConfigureRequest(request);
        HttpRetryHelper.HttpBodyFetchResult fetch =
            await HttpRetryHelper.GetBytesAfterHeadersWithRetryAsync(
                _httpClient,
                url,
                static _ => true,
                cancellationToken: cancellationToken,
                trafficKind: NetworkTrafficKind.SourceFetch,
                maxDownloadSize: MaxSourceDownloadSize,
                configureRequest: configureRequest)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (fetch.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new FetchSourceResult.Failure(SourceError.NotFound);
        if (fetch.Bytes is not { } bytes)
            return new FetchSourceResult.Failure(SourceError.Unavailable);

        bool networkBytesValid = validator(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (!networkBytesValid)
            return new FetchSourceResult.Failure(
                SourceError.ValidationFailed);

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
            return new FetchSourceResult.Failure(
                SourceError.StorageFailed);
        }

        _byteMemoryCache[url] = bytes;
        return new FetchSourceResult.Success(bytes);
    }

    static bool IsContentStoreFailure(Exception exception)
        => exception is not (OperationCanceledException
            or OutOfMemoryException
            or StackOverflowException
            or AccessViolationException);

    sealed class PersistentCacheSourceContentStore
        : ISourceContentStore
    {
        internal static PersistentCacheSourceContentStore Instance { get; } =
            new();

        public ValueTask<byte[]?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? encoded = PersistentCache.TryGet(
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
            PersistentCache.Set(
                ByteCacheCategory,
                key,
                Convert.ToBase64String(content.Span),
                extension: "base64");
            return ValueTask.CompletedTask;
        }
    }
}
