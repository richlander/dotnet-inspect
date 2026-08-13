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
using System.Text;
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

    /// <summary>
    /// Outcome of streaming a response body to a local file.
    /// </summary>
    public enum DownloadToFileResult
    {
        /// <summary>The destination contains the complete response body.</summary>
        Succeeded,

        /// <summary>The source gave no usable answer (missing, transport failure).</summary>
        Unavailable,

        /// <summary>The source answered with a payload above the configured cap.</summary>
        RejectedPayload,
    }

    public readonly record struct HttpBodyFetchResult(
        byte[]? Bytes,
        HttpBodyFetchStatus Status,
        HttpStatusCode? StatusCode = null);

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
    /// Largest advertised response body accepted by a downloading helper.
    /// </summary>
    private const long MaxDownloadSize = 500_000_000; // 500 MB

    /// <summary>
    /// Default ceiling for text/JSON feed documents fetched via
    /// <see cref="GetStringWithRetryAsync"/> (service index, version lists,
    /// registration pages, search). Hostile feeds must not be able to force a
    /// multi‑GiB string allocation on discovery paths that precede package
    /// download admission.
    /// </summary>
    public const long DefaultMaxTextResponseBytes = 16 * 1024 * 1024;

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
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        HttpCompletionOption completionOption,
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
            Uri? effectiveRequestUri = null;
            string? redactedUrl = null;
            string RedactedUrl() =>
                redactedUrl ??= (effectiveRequestUri is { } effectiveUri
                    ? NetworkRequestObservation.RedactSensitiveUrl(effectiveUri)
                    : NetworkRequestObservation.RedactSensitiveUrlText(url)).ToString();
            void RecordFailure(HttpStatusCode? status)
            {
                if (effectiveRequestUri is { } effectiveUri)
                    FeedFailureTelemetry.Record(effectiveUri, status);
                else
                    FeedFailureTelemetry.Record(url, status);
            }
            void CaptureEffectiveRequestUri(Uri? uri)
            {
                if (uri is not null)
                {
                    effectiveRequestUri = uri;
                    redactedUrl = null;
                }
            }

            using (NetworkTelemetry.Scope(trafficKind))
            {
                HttpRequestMessage? request = null;
                try
                {
                    request = requestFactory();
                    CaptureEffectiveRequestUri(
                        ResolveInitialRequestUri(client, request.RequestUri));
                    Task<HttpResponseMessage> sendTask =
                        completionOption == HttpCompletionOption.ResponseContentRead
                            ? client.SendAsync(request, cancellationToken)
                            : client.SendAsync(request, completionOption, cancellationToken);
                    var response = await sendTask.ConfigureAwait(false);
                    CaptureEffectiveRequestUri(response.RequestMessage?.RequestUri ?? request.RequestUri);

                    if (response.IsSuccessStatusCode)
                    {
                        if (!ReferenceEquals(response.RequestMessage, request))
                            request.Dispose();
                        request = null;
                        return new HttpRetryResult(response, response.StatusCode);
                    }

                    var statusCode = response.StatusCode;
                    HttpRequestMessage? responseRequest = response.RequestMessage;
                    response.Dispose();
                    if (!ReferenceEquals(responseRequest, request))
                        responseRequest?.Dispose();

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
                        log?.Invoke($"HTTP {methodName} {(int)statusCode} (not retryable): {RedactedUrl()}");
                        RecordFailure(statusCode);
                        return new HttpRetryResult(null, statusCode);
                    }

                    log?.Invoke($"HTTP {methodName} {(int)statusCode} (retryable): {RedactedUrl()}");
                }
                catch (HttpRequestException ex)
                {
                    var (isRetryable, socketError) = GetSocketError(ex);

                    if (!isRetryable)
                    {
                        string errorKind = socketError != SocketError.Success
                            ? socketError.ToString()
                            : ex.HttpRequestError.ToString();
                        log?.Invoke($"HTTP {methodName} error {errorKind} (not retryable): {RedactedUrl()}");
                        RecordFailure(null);
                        return new HttpRetryResult(null, null);
                    }

                    log?.Invoke($"Socket error {socketError} (retryable): {RedactedUrl()}");
                }
                catch (NotSupportedException)
                {
                    // Thrown by HttpRequestMessage when the URL scheme is unsupported
                    // (e.g. file:// or a raw local folder path). Treat as non-retryable
                    // so a local folder NuGet source listed in NuGet.Config can't crash
                    // remote queries. Issue #310.
                    log?.Invoke($"HTTP {methodName} unsupported URL (not retryable)");
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
                    log?.Invoke($"{methodName} request timeout (retryable): {RedactedUrl()}");
                }
                finally
                {
                    request?.Dispose();
                }

                // Check retry limit while the attempt's traffic currency is still active.
                if (attempts++ >= retryCount)
                {
                    log?.Invoke($"Max retries ({retryCount}) exceeded: {RedactedUrl()}");
                    RecordFailure(null);
                    return new HttpRetryResult(null, null);
                }
            }

            // Exponential backoff with jitter
            var delay = GetRetryDelay(attempts);
            log?.Invoke($"Retry #{attempts} after {delay.TotalMilliseconds:F0}ms");
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Uri? ResolveInitialRequestUri(HttpClient client, Uri? requestUri)
    {
        if (requestUri is null || requestUri.IsAbsoluteUri || client.BaseAddress is null)
            return requestUri;

        return Uri.TryCreate(client.BaseAddress, requestUri, out Uri? resolved)
            ? resolved
            : requestUri;
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
            client,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (auth != null)
                    request.Headers.Authorization = auth;
                request.Headers.Range = range;
                return request;
            },
            range is null
                ? HttpCompletionOption.ResponseContentRead
                : HttpCompletionOption.ResponseHeadersRead,
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
                                HttpBodyFetchStatus.ResponseRejected,
                                response.StatusCode);
                        }

                        readingBody = true;
                        byte[] bytes = await ReadBoundedBodyAsync(
                            response.Content,
                            maxDownloadSize,
                            timeout.Token).ConfigureAwait(false);
                        return new HttpBodyFetchResult(
                            bytes,
                            HttpBodyFetchStatus.Success,
                            response.StatusCode);
                    }

                    HttpStatusCode statusCode = response.StatusCode;
                    if (statusCode == HttpStatusCode.NotFound)
                    {
                        return new HttpBodyFetchResult(
                            null,
                            HttpBodyFetchStatus.Unavailable,
                            statusCode);
                    }

                    if (!IsRetryableStatus(statusCode))
                    {
                        log?.Invoke($"HTTP GET {(int)statusCode} (not retryable).");
                        FeedFailureTelemetry.Record(url, statusCode);
                        return new HttpBodyFetchResult(
                            null,
                            HttpBodyFetchStatus.Unavailable,
                            statusCode);
                    }

                    log?.Invoke($"HTTP GET {(int)statusCode} (retryable).");
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
                        string errorKind = socketError != SocketError.Success
                            ? socketError.ToString()
                            : ex.HttpRequestError.ToString();
                        log?.Invoke($"HTTP GET error {errorKind} (not retryable).");
                        FeedFailureTelemetry.Record(url, null);
                        return new HttpBodyFetchResult(
                            null,
                            HttpBodyFetchStatus.Unavailable);
                    }

                    log?.Invoke(readingBody
                        ? "HTTP GET body failed (retryable)."
                        : $"Socket error {socketError} (retryable).");
                }
                catch (IOException) when (readingBody)
                {
                    log?.Invoke("HTTP GET body failed (retryable).");
                }
                catch (NotSupportedException)
                {
                    log?.Invoke("HTTP GET unsupported URL (not retryable).");
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
                    log?.Invoke($"Max retries ({retryCount}) exceeded.");
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
    /// Executes an HTTP GET and returns the response body as a string, with
    /// retry logic and a hard body ceiling
    /// (<see cref="DefaultMaxTextResponseBytes"/> unless overridden).
    /// Oversized bodies return null (fail closed) rather than materializing an
    /// unbounded string.
    /// </summary>
    /// <param name="client">HTTP client to use</param>
    /// <param name="url">URL to fetch</param>
    /// <param name="retryCount">Maximum number of retries (default: 3)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="auth">Optional authentication header for authenticated feeds</param>
    /// <param name="trafficKind">Telemetry classification for the request</param>
    /// <param name="maxDownloadSize">
    /// Maximum accepted body bytes. Defaults to
    /// <see cref="DefaultMaxTextResponseBytes"/>.
    /// </param>
    /// <returns>Response body as string, or null if failed or oversize</returns>
    public static async Task<string?> GetStringWithRetryAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown,
        long maxDownloadSize = DefaultMaxTextResponseBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDownloadSize);

        // Headers-first via the shared retry loop so relative-URL resolution,
        // redacted failure logs, and FeedFailureTelemetry stay identical to
        // other package GETs — then bound the body before decoding text.
        HttpRetryResult result = await ExecuteWithRetryAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (auth != null)
                    request.Headers.Authorization = auth;
                return request;
            },
            HttpCompletionOption.ResponseHeadersRead,
            url,
            "GET",
            retryCount,
            log,
            cancellationToken,
            trafficKind).ConfigureAwait(false);

        using HttpResponseMessage? response = result.Response;
        if (response is null)
            return null;

        // Body work sits outside ExecuteWithRetryAsync's NetworkTelemetry.Scope.
        // Re-enter the caller trafficKind so FeedFailureTelemetry.Record stamps
        // PackageVersionList / PackageManifest / etc., not Unknown or an outer
        // ambient scope.
        using (NetworkTelemetry.Scope(trafficKind))
        {
            // Oversize / body timeout must mark FeedFailureTelemetry so callers that
            // distinguish Absent vs Failure via failure deltas (version index) do not
            // treat a hostile oversize 200 as a clean 404.
            void RecordBodyFailure()
            {
                Uri? effective = response.RequestMessage?.RequestUri;
                if (effective is not null)
                    FeedFailureTelemetry.Record(effective, response.StatusCode);
                else
                    FeedFailureTelemetry.Record(url, response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is long advertised
                && advertised > maxDownloadSize)
            {
                log?.Invoke($"HTTP GET body exceeded {maxDownloadSize} byte cap.");
                RecordBodyFailure();
                return null;
            }

            // Headers-first means HttpClient.Timeout no longer covers the body.
            // Match GetBytesAfterHeadersWithRetryAsync: linked CTS + CancelAfter so
            // a stalled feed cannot hang service-index / version-list discovery.
            TimeSpan requestTimeout = client.Timeout == Timeout.InfiniteTimeSpan
                || client.Timeout > HttpClientFactoryOptions.BaselineTimeout
                    ? HttpClientFactoryOptions.BaselineTimeout
                    : client.Timeout;
            using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            bodyTimeout.CancelAfter(requestTimeout);

            try
            {
                byte[] bytes = await ReadBoundedBodyAsync(
                    response.Content,
                    maxDownloadSize,
                    bodyTimeout.Token).ConfigureAwait(false);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (ResponseBodyTooLargeException)
            {
                log?.Invoke($"HTTP GET body exceeded {maxDownloadSize} byte cap.");
                RecordBodyFailure();
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                log?.Invoke("GET body timeout.");
                RecordBodyFailure();
                return null;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                // Mid-body transport failure after headers must not escape as an
                // unhandled fault: discovery loops classify sources via null +
                // FeedFailureTelemetry deltas, not exceptions.
                log?.Invoke("GET body failed.");
                RecordBodyFailure();
                return null;
            }
        }
    }

    /// <summary>
    /// Executes an HTTP GET with retry on the initial request and returns the
    /// successful response with its body unread, so a caller can stream the
    /// payload without buffering it. Uses
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/>. The caller owns
    /// the returned response and must dispose it. Returns null if the request
    /// ultimately failed. Returns null if the advertised size exceeds
    /// <paramref name="maxAdvertisedContentLength"/> so a multi-source caller
    /// can fail over instead of aborting the remaining producers.
    /// Note: a failure that occurs mid-body (after headers) is not retried.
    /// </summary>
    /// <param name="maxAdvertisedContentLength">
    /// The advertised body size above which this helper returns null. A caller
    /// that bounds the payload itself — and must keep an oversized payload a
    /// typed failure rather than an exception — raises this so the decision
    /// stays where the typed result is produced. The advertised length is a
    /// claim by the remote, so raising this never removes a bound from a caller
    /// that counts the bytes it actually received.
    /// </param>
    public static async Task<HttpResponseMessage?> GetStreamedWithRetryAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown,
        long maxAdvertisedContentLength = MaxDownloadSize)
    {
        var result = await ExecuteWithRetryAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (auth != null)
                    request.Headers.Authorization = auth;
                return request;
            },
            HttpCompletionOption.ResponseHeadersRead,
            url,
            "GET",
            retryCount,
            log,
            cancellationToken,
            trafficKind).ConfigureAwait(false);

        var response = result.Response;
        if (response == null)
            return null;

        if (response.Content.Headers.ContentLength > maxAdvertisedContentLength)
        {
            // Oversized advertised length is a failed source answer. Returning
            // null keeps source failover intact; throwing would abort the loop
            // that is still trying every other authorized producer.
            long advertised = response.Content.Headers.ContentLength.Value;
            response.Dispose();
            log?.Invoke(
                $"Download size ({advertised / 1_000_000} MB) exceeds limit.");
            return null;
        }

        return response;
    }

    /// <summary>
    /// Executes an HTTP GET and streams the response body directly to <paramref name="destinationPath"/>,
    /// with retry on the initial request. Uses <see cref="HttpCompletionOption.ResponseHeadersRead"/> so
    /// the payload is never fully buffered in memory — important for large packages. Returns true on
    /// <see cref="DownloadToFileResult.Succeeded"/> on success,
    /// <see cref="DownloadToFileResult.Unavailable"/> when the source gave no
    /// usable answer, or <see cref="DownloadToFileResult.RejectedPayload"/> when
    /// the source advertised or streamed a body above the cap.
    /// Bytes actually received are counted against the same cap, so a chunked
    /// or under-reported response cannot grow the destination without bound.
    /// The body read is bounded by the same request timeout used for headers so
    /// a stalled source cannot pin the caller past failover.
    /// Note: a failure that occurs mid-body (after headers) is not retried.
    /// </summary>
    /// <remarks>
    /// Gated by
    /// <c>HttpRetryHelperTests.DownloadToFile_BoundsActualBytesWhenLengthIsMissingOrUnderReported</c>
    /// and
    /// <c>HttpRetryHelperTests.DownloadToFile_RejectsAdvertisedOversizeAsTypedResult</c>.
    /// </remarks>
    public static async Task<DownloadToFileResult> DownloadToFileWithRetryAsync(
        HttpClient client,
        string url,
        string destinationPath,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        AuthenticationHeaderValue? auth = null,
        NetworkTrafficKind trafficKind = NetworkTrafficKind.Unknown,
        long maxDownloadedBytes = MaxDownloadSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDownloadedBytes);

        // Keep advertised-oversize as a typed RejectedPayload here rather than
        // collapsing it into GetStreamedWithRetryAsync's null (Unavailable).
        using var response = await GetStreamedWithRetryAsync(
            client,
            url,
            retryCount,
            log,
            cancellationToken,
            auth,
            trafficKind,
            maxAdvertisedContentLength: long.MaxValue)
            .ConfigureAwait(false);
        if (response == null)
            return DownloadToFileResult.Unavailable;

        if (response.Content.Headers.ContentLength > maxDownloadedBytes)
        {
            long advertised = response.Content.Headers.ContentLength.Value;
            log?.Invoke(
                $"Download size ({advertised / 1_000_000} MB) exceeds limit.");
            return DownloadToFileResult.RejectedPayload;
        }

        bool destinationCreated = false;
        bool completed = false;
        try
        {
            using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            TimeSpan requestTimeout =
                client.Timeout == Timeout.InfiniteTimeSpan
                || client.Timeout > HttpClientFactoryOptions.BaselineTimeout
                    ? HttpClientFactoryOptions.BaselineTimeout
                    : client.Timeout;
            bodyTimeout.CancelAfter(requestTimeout);

            await using Stream source = await response.Content
                .ReadAsStreamAsync(bodyTimeout.Token)
                .ConfigureAwait(false);
            await using FileStream destination = File.Create(destinationPath);
            destinationCreated = true;
            byte[] buffer = new byte[81920];
            long received = 0;
            while (true)
            {
                int read = await source.ReadAsync(
                        buffer,
                        bodyTimeout.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;

                if (read > maxDownloadedBytes - received)
                {
                    log?.Invoke(
                        $"Download size exceeds the {maxDownloadedBytes}-byte limit.");
                    return DownloadToFileResult.RejectedPayload;
                }

                await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        bodyTimeout.Token)
                    .ConfigureAwait(false);
                received += read;
            }

            completed = true;
            return DownloadToFileResult.Succeeded;
        }
        finally
        {
            if (destinationCreated && !completed)
            {
                try
                {
                    File.Delete(destinationPath);
                }
                catch (IOException)
                {
                    // Prefer the download failure over a cleanup failure.
                }
                catch (UnauthorizedAccessException)
                {
                    // Prefer the download failure over a cleanup failure.
                }
            }
        }
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
            client,
            () => new HttpRequestMessage(HttpMethod.Head, url),
            HttpCompletionOption.ResponseContentRead,
            url,
            "HEAD",
            retryCount,
            log,
            cancellationToken,
            trafficKind);
    }
}
