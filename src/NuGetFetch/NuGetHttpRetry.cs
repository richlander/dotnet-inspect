using System.Net;

namespace NuGetFetch;

internal static class NuGetHttpRetry
{
    private const int MaximumRetries = 3;

    public static async Task<T> RunRequestAsync<T>(
        NuGetOperationDeadline operation,
        Func<CancellationToken, Task<T>> request)
    {
        for (int retry = 0; ; retry++)
        {
            try
            {
                return await operation.RunRequestAsync(request)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (retry < MaximumRetries
                    && IsTransient(exception))
            {
                await DelayAsync(operation, retry).ConfigureAwait(false);
            }
        }
    }

    public static async Task<(Stream Stream, T Metadata)>
        RunStreamingRequestAsync<T>(
            NuGetOperationDeadline operation,
            Func<CancellationToken, Task<(
                Stream Stream,
                IDisposable Owner,
                T Metadata)>> request)
    {
        for (int retry = 0; ; retry++)
        {
            try
            {
                return await operation.RunStreamingRequestAsync(request)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (retry < MaximumRetries
                    && IsTransient(exception))
            {
                await DelayAsync(operation, retry).ConfigureAwait(false);
            }
        }
    }

    private static Task DelayAsync(
        NuGetOperationDeadline operation,
        int retry) =>
        operation.DelayAsync(
            TimeSpan.FromMilliseconds(100 * (1 << retry)));

    private static bool IsTransient(Exception exception) =>
        exception is NuGetRequestTimeoutException
            or NuGetMetadataBodyTimeoutException
        || exception is IOException
            and not NuGetMetadataResponseTooLargeException
            and not NuGetRedirectLimitExceededException
        || exception is HttpRequestException request
            && (request.StatusCode is null
                || request.StatusCode is
                    HttpStatusCode.RequestTimeout
                    or HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout);
}
