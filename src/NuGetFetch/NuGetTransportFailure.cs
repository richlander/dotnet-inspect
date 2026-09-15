namespace NuGetFetch;

internal static class NuGetTransportFailure
{
    public static TimeoutException? GetTimeout(Exception exception) =>
        exception switch
        {
            TimeoutException timeout => timeout,
            OperationCanceledException
            {
                InnerException: TimeoutException timeout,
            } => timeout,
            _ => null,
        };

    public static bool IsTimeout(Exception exception) =>
        GetTimeout(exception) is not null;
}
