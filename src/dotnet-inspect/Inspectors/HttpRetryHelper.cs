// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Based on Microsoft.SymbolStore.SymbolStores.HttpSymbolStore
// Source: https://github.com/dotnet/diagnostics/blob/main/src/Microsoft.SymbolStore/SymbolStores/HttpSymbolStore.cs
// Adapted for AOT compatibility and simplified for dotnet-inspect use case.

using System.Net;
using System.Net.Sockets;

namespace DotnetInspector.Inspectors;

/// <summary>
/// HTTP retry helper with exponential backoff, adapted from Microsoft.SymbolStore.
/// </summary>
public static class HttpRetryHelper
{
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
    /// Executes an HTTP GET with retry logic for transient failures.
    /// </summary>
    /// <param name="client">HTTP client to use</param>
    /// <param name="url">URL to fetch</param>
    /// <param name="retryCount">Maximum number of retries (default: 3)</param>
    /// <param name="log">Optional logging callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response if successful, null if failed or not found</returns>
    public static async Task<HttpResponseMessage?> GetWithRetryAsync(
        HttpClient client,
        string url,
        int retryCount = DefaultRetryCount,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        int attempts = 0;

        while (true)
        {
            try
            {
                var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                var statusCode = response.StatusCode;
                response.Dispose();

                // Not found is not retryable
                if (statusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                // Check if retryable
                if (!IsRetryableStatus(statusCode))
                {
                    log?.Invoke($"HTTP {(int)statusCode} (not retryable): {url}");
                    return null;
                }

                log?.Invoke($"HTTP {(int)statusCode} (retryable): {url}");
            }
            catch (HttpRequestException ex)
            {
                var (isRetryable, socketError) = GetSocketError(ex);

                if (!isRetryable)
                {
                    log?.Invoke($"HTTP error (not retryable): {ex.Message}");
                    return null;
                }

                log?.Invoke($"Socket error {socketError} (retryable): {url}");
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                // Timeout - treat as retryable
                log?.Invoke($"Request timeout (retryable): {url}");
            }

            // Check retry limit
            if (attempts++ >= retryCount)
            {
                log?.Invoke($"Max retries ({retryCount}) exceeded: {url}");
                return null;
            }

            // Exponential backoff with jitter
            var delay = GetRetryDelay(attempts);
            log?.Invoke($"Retry #{attempts} after {delay.TotalMilliseconds:F0}ms");
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}
