namespace NuGetFetch;

/// <summary>
/// Thrown when one NuGet HTTP request exceeds its configured deadline.
/// </summary>
public sealed class NuGetRequestTimeoutException : TimeoutException
{
    public NuGetRequestTimeoutException(
        TimeSpan timeout,
        OperationCanceledException innerException)
        : base($"NuGet request did not complete within {timeout}.", innerException)
    {
        Timeout = timeout;
    }

    /// <summary>
    /// Gets the configured request deadline.
    /// </summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
/// Thrown when one logical NuGet operation exceeds its configured ceiling.
/// </summary>
public sealed class NuGetOperationTimeoutException : TimeoutException
{
    public NuGetOperationTimeoutException(
        TimeSpan timeout,
        OperationCanceledException innerException)
        : base($"NuGet operation did not complete within {timeout}.", innerException)
    {
        Timeout = timeout;
    }

    /// <summary>
    /// Gets the configured operation ceiling.
    /// </summary>
    public TimeSpan Timeout { get; }
}
