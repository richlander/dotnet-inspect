// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Based on Microsoft.SymbolStore.SymbolStores.HttpSymbolStore
// Source: https://github.com/dotnet/diagnostics/blob/main/src/Microsoft.SymbolStore/SymbolStores/HttpSymbolStore.cs
// Adapted for AOT compatibility and simplified for dotnet-inspect use case.

using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using DotnetInspector.Core;

namespace DotnetInspector.Packages;

/// <summary>
/// HTTP retry helper with exponential backoff, adapted from Microsoft.SymbolStore.
/// </summary>
public static class HttpRetryHelper
{
    private static readonly HttpRequestOptionsKey<bool> BrowserStreamingResponse =
        new("WebAssemblyEnableStreamingResponse");

    public enum HttpBodyFetchStatus
    {
        Success,
        Unavailable,
        ResponseRejected,
        TooLarge,
    }

    public readonly record struct HttpBodyFetchResult(
        byte[]? Bytes,
        HttpBodyFetchStatus Status);

    public readonly record struct HttpRetryResult(HttpResponseMessage? Response, HttpStatusCode? StatusCode)
    {
        public bool IsSuccess => Response is not null;
        public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    }

    /// <summary>
    /// Default number of retries for transient failures.
    /// </summary>
    public const int DefaultRetryCount = 3;

    /// <summary>
    /// HTTP status codes that indicate a transient failure worth retrying.
    /// </summary>
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,        // 408
        HttpStatusCode.InternalServerError,   // 500
        HttpStatusCode.BadGateway,            // 502
        HttpStatusCode.ServiceUnavailable,    // 503
        HttpStatusCode.GatewayTimeout,        // 504
    };

    /// <summary>
    /// Socket errors that indicate a transient failure worth retrying.
    /// </summary>
    private static readonly HashSet<SocketError> RetryableSocketErrors = new()
    {
        SocketError.ConnectionReset,
        SocketError.ConnectionAborted,
        SocketError.Shutdown,
        SocketError.TimedOut,
        SocketError.TryAgain,
    };

    /// <summary>
    /// Checks if an HTTP status code is retryable.
    /// </summary>
    public static bool IsRetryableStatus(HttpStatusCode status) => RetryableStatusCodes.Contains(status);

    /// <summary>
    /// Checks if a socket error is retryable.
    /// </summary>
    public static bool IsRetryableSocketError(SocketError error) => RetryableSocketErrors.Contains(error);

    /// <summary>
    /// Extracts socket error from an HttpRequestException, if present.
    /// </summary>
    public static (bool isRetryable, SocketError error) GetSocketError(HttpRequestException ex)
    {
        Exception? innerException = ex.InnerException;
        while (innerException != null)
        {
            if (innerException is SocketException se)
            {
                return (IsRetryableSocketError(se.SocketErrorCode), se.SocketErrorCode);
            }
            innerException = innerException.InnerException;
        }
        return (false, SocketError.Success);
    }

    /// <summary>
    /// Calculates delay for exponential backoff with jitter.
    /// Formula: (2^retryAttempt * 100ms) + random(0-200ms)
    /// </summary>
    public static TimeSpan GetRetryDelay(int retryAttempt)
    {
        var baseDelay = Math.Pow(2, retryAttempt) * 100;
        var jitter = Random.Shared.Next(200);
        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }

    /// <summary>
    /// Core retry loop that handles transient failures with exponential backoff.
    /// </summary>
    private static async Task<HttpRetryResult> ExecuteWithRetryAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> requestFactory,
        string url,
        string methodName,
        int retryCount,
        Action<string>? log,
        CancellationToken cancellationToken,
        NetworkTrafficKind trafficKind)
    {
        int attempts = 0;

        while (true)
        {
            using (NetworkTelemetry.Scope(trafficKind))
            {
                try
                {
                    var response = await requestFactory(cancellationToken).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        return new HttpRetryResult(response, response.StatusCode);
                    }

                    var statusCode = response.StatusCode;
                    response.Dispose();

                    // Not found is not retryable, and is the one status that genuinely means the
                    // package is absent rather than the source being unreadable, so it is not
                    // recorded as a source failure.
                    if (statusCode == HttpStatusCode.NotFound)
                    {
                        return new HttpRetryResult(null, statusCode);
                    }

                    // Check if retryable
                    if (!IsRetryableStatus(statusCode))
                    {
                        log?.Invoke($"HTTP {methodName} {(int)statusCode} (not retryable): {url}");
                        FeedFailureTelemetry.Record(url, statusCode);
                        return new HttpRetryResult(null, statusCode);
                    }

                    log?.Invoke($"HTTP {methodName} {(int)statusCode} (retryable): {url}");
                }
                catch (HttpRequestException ex)
                {
                    var (isRetryable, socketError) = GetSocketError(ex);

                    if (!isRetryable)
                    {
                        log?.Invoke($"HTTP {methodName} error (not retryable): {ex.Message}");
                        FeedFailureTelemetry.Record(url, null);
                        return new HttpRetryResult(null, null);
                    }

                    log?.Invoke($"Socket error {socketError} (retryable): {url}");
                }
                catch (NotSupportedException ex)
                {
                    // Thrown by HttpRequestMessage when the URL scheme is unsupported
                    // (e.g. file:// or a raw local folder path). Treat as non-retryable
                    // so a local folder NuGet source listed in NuGet.Config can't crash
                    // remote queries. Issue #310.
                    log?.Invoke($"HTTP {methodName} unsupported URL (not retryable): {ex.Message}");
                    return new HttpRetryResult(null, null);
                }
                catch (DotnetInspector.Core.OfflineException)
                {
                    log?.Invoke($"Network access is disabled (--offline mode).");
                    return new HttpRetryResult(null, null);
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TaskCanceledException)
                {
                    // Timeout - treat as retryable
                    log?.Invoke($"{methodName} request timeout (retryable): {url}");
                }

                // Check retry limit while the attempt's traffic currency is still active.
                if (attempts++ >= retryCount)
                {
                    log?.Invoke($"Max retries ({retryCount}) exceeded: {url}");
                    FeedFailureTelemetry.Record(url, null);
                    return new HttpRetryResult(null, null);
                }
            }

            // Exponential backoff with jitter
            var delay = GetRetryDelay(attempts);
            log?.Invoke($"Retry #{attempts} after {delay.TotalMilliseconds:F0}ms");
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes an HTTP GET with retry logic for transient failures.
    /// </summary>
    /// <param name="client">HTTP client to use</param>
    /// <param name="url">URL to fetch</param>
    /// <param name="retryCount">Maximum number of retries (default: 3)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="auth">Optional authentication header for authenticated feeds</param>
    /// <returns>Response if successful, null if failed or not found</returns>
    public static async Task<HttpResponseMessage?> GetWithRetryAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown)
    {
        var result = await GetWithRetryResultAsync(
            client,
            url,
            retryCount,
            log,
            cancellationToken,
            auth,
            trafficKind).ConfigureAwait(false);
        return result.Response;
    }

    /// <summary>
    /// Executes an HTTP GET with retry logic and returns status information for non-success responses.
    /// </summary>
    public static Task<HttpRetryResult> GetWithRetryResultAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown,
        RangeHeaderValue? range = null)
    {
        return ExecuteWithRetryAsync(
            ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (auth != null)
                    request.Headers.Authorization = auth;
                request.Headers.Range = range;
                return range is null
                    ? client.SendAsync(request, ct)
                    : client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);
            },
            url,
            "GET",
            retryCount,
            log,
            cancellationToken,
            trafficKind);
    }

    /// <summary>
    /// Executes an HTTP GET with header-first response validation, then reads a bounded response
    /// body under the untrusted-fetch timeout. Transient failures while reading the body are
    /// retried with the request.
    /// </summary>
    public static async Task<HttpBodyFetchResult> GetBytesAfterHeadersWithRetryAsync(
        HttpClient client,
        string url,
        Func<HttpResponseMessage, bool> responseValidator,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown,
        long maxDownloadSize = 500_000_000)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(responseValidator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDownloadSize);

        int attempts = 0;

        while (true)
        {
            bool readingBody = false;
            using (NetworkTelemetry.Scope(trafficKind))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                TimeSpan requestTimeout = client.Timeout == Timeout.InfiniteTimeSpan
                    || client.Timeout > HttpClientFactoryOptions.BaselineTimeout
                        ? HttpClientFactoryOptions.BaselineTimeout
                        : client.Timeout;
                timeout.CancelAfter(requestTimeout);

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (auth != null)
                        request.Headers.Authorization = auth;
                    request.Options.Set(BrowserStreamingResponse, true);

                    using var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        if (!responseValidator(response))
                        {
                            return new HttpBodyFetchResult(
                                null,
                                HttpBodyFetchStatus.ResponseRejected);
                        }

                        readingBody = true;
                        byte[] bytes = await ReadBoundedBodyAsync(
                            response.Content,
                            maxDownloadSize,
                            timeout.Token).ConfigureAwait(false);
                        return new HttpBodyFetchResult(
                            bytes,
                            HttpBodyFetchStatus.Success);
                    }

                    HttpStatusCode statusCode = response.StatusCode;
                    if (statusCode == HttpStatusCode.NotFound)
                    {
                        return new HttpBodyFetchResult(
                            null,
                            HttpBodyFetchStatus.Unavailable);
                    }

                    if (!IsRetryableStatus(statusCode))
                    {
                        log?.Invoke($"HTTP GET {(int)statusCode} (not retryable): {url}");
                        FeedFailureTelemetry.Record(url, statusCode);
                        return new HttpBodyFetchResult(
                            null,
                            HttpBodyFetchStatus.Unavailable);
                    }

                    log?.Invoke($"HTTP GET {(int)statusCode} (retryable): {url}");
                }
                catch (ResponseBodyTooLargeException)
                {
                    return new HttpBodyFetchResult(
                        null,
                        HttpBodyFetchStatus.TooLarge);
                }
                catch (HttpRequestException ex)
                {
                    var (isRetryable, socketError) = GetSocketError(ex);
                    if (!readingBody && !isRetryable)
                    {
                        log?.Invoke($"HTTP GET error (not retryable): {ex.Message}");
                        FeedFailureTelemetry.Record(url, null);
                        return new HttpBodyFetchResult(
                            null,
                            HttpBodyFetchStatus.Unavailable);
                    }

                    log?.Invoke(readingBody
                        ? $"HTTP GET body failed (retryable): {url}"
                        : $"Socket error {socketError} (retryable): {url}");
                }
                catch (IOException) when (readingBody)
                {
                    log?.Invoke($"HTTP GET body failed (retryable): {url}");
                }
                catch (NotSupportedException ex)
                {
                    log?.Invoke($"HTTP GET unsupported URL (not retryable): {ex.Message}");
                    return new HttpBodyFetchResult(
                        null,
                        HttpBodyFetchStatus.Unavailable);
                }
                catch (OfflineException)
                {
                    log?.Invoke("Network access is disabled (--offline mode).");
                    return new HttpBodyFetchResult(
                        null,
                        HttpBodyFetchStatus.Unavailable);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    log?.Invoke("GET request or body timeout (retryable).");
                }

                if (attempts++ >= retryCount)
                {
                    log?.Invoke($"Max retries ({retryCount}) exceeded: {url}");
                    FeedFailureTelemetry.Record(url, null);
                    return new HttpBodyFetchResult(
                        null,
                        HttpBodyFetchStatus.Unavailable);
                }
            }

            TimeSpan delay = GetRetryDelay(attempts);
            log?.Invoke($"Retry #{attempts} after {delay.TotalMilliseconds:F0}ms");
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpContent content,
        long maxDownloadSize,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maxDownloadSize)
        {
            throw new ResponseBodyTooLargeException();
        }

        await using Stream source = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        long total = 0;
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                total += read;
                if (total > maxDownloadSize)
                    throw new ResponseBodyTooLargeException();

                destination.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return destination.ToArray();
    }

    private sealed class ResponseBodyTooLargeException : InvalidOperationException;

    /// <summary>
    /// Executes an HTTP GET and returns the response body as a string, with retry logic.
    /// </summary>
    /// <param name="client">HTTP client to use</param>
    /// <param name="url">URL to fetch</param>
    /// <param name="retryCount">Maximum number of retries (default: 3)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="auth">Optional authentication header for authenticated feeds</param>
    /// <returns>Response body as string, or null if failed</returns>
    public static async Task<string?> GetStringWithRetryAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown)
    {
        using var response = await GetWithRetryAsync(client, url, retryCount, log, cancellationToken, auth, trafficKind).ConfigureAwait(false);
        if (response == null)
            return null;

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an HTTP GET and returns the response body as a byte array, with retry logic.
    /// </summary>
    /// <param name="client">HTTP client to use</param>
    /// <param name="url">URL to fetch</param>
    /// <param name="retryCount">Maximum number of retries (default: 3)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="auth">Optional authentication header for authenticated feeds</param>
    /// <returns>Response body as byte array, or null if failed</returns>
    public static async Task<byte[]?> GetBytesWithRetryAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown)
    {
        using var response = await GetWithRetryAsync(client, url, retryCount, log, cancellationToken, auth, trafficKind).ConfigureAwait(false);
        if (response == null)
            return null;

        const long MaxDownloadSize = 500_000_000; // 500 MB

        if (response.Content.Headers.ContentLength is > MaxDownloadSize)
            throw new InvalidOperationException(
                $"Download size ({response.Content.Headers.ContentLength / 1_000_000} MB) exceeds limit.");

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an HTTP GET and streams the response body directly to <paramref name="destinationPath"/>,
    /// with retry on the initial request. Uses <see cref="HttpCompletionOption.ResponseHeadersRead"/> so
    /// the payload is never fully buffered in memory — important for large packages. Returns true on
    /// success, false if the request ultimately failed. Throws if the advertised size exceeds the cap.
    /// Note: a failure that occurs mid-body (after headers) is not retried.
    /// </summary>
    public static async Task<bool> DownloadToFileWithRetryAsync(
        HttpClient client,
        string url,
        string destinationPath,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown)
    {
        var result = await ExecuteWithRetryAsync(
            ct =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (auth != null)
                    request.Headers.Authorization = auth;
                return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            },
            url,
            "GET",
            retryCount,
            log,
            cancellationToken,
            trafficKind).ConfigureAwait(false);

        using var response = result.Response;
        if (response == null)
            return false;

        const long MaxDownloadSize = 500_000_000; // 500 MB
        if (response.Content.Headers.ContentLength is > MaxDownloadSize)
            throw new InvalidOperationException(
                $"Download size ({response.Content.Headers.ContentLength / 1_000_000} MB) exceeds limit.");

        await using var fs = File.Create(destinationPath);
        await response.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Executes an HTTP HEAD request with retry logic.
    /// </summary>
    /// <param name="client">HTTP client to use</param>
    /// <param name="url">URL to check</param>
    /// <param name="retryCount">Maximum number of retries (default: 3)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response if successful, null if failed or not found</returns>
    public static async Task<HttpResponseMessage?> HeadWithRetryAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown)
    {
        var result = await HeadWithRetryResultAsync(client, url, retryCount, log, cancellationToken, trafficKind).ConfigureAwait(false);
        return result.Response;
    }

    /// <summary>
    /// Executes an HTTP HEAD request with retry logic and returns status information for non-success responses.
    /// </summary>
    public static Task<HttpRetryResult> HeadWithRetryResultAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown)
    {
        return ExecuteWithRetryAsync(
            async ct =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                return await client.SendAsync(request, ct).ConfigureAwait(false);
            },
            url,
            "HEAD",
            retryCount,
            log,
            cancellationToken,
            trafficKind);
    }
}
