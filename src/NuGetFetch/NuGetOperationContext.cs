using System.Diagnostics;

namespace NuGetFetch;

/// <summary>
/// Carries one monotonic deadline across a composed NuGet operation.
/// </summary>
/// <remarks>
/// Reuse one instance across every source, retry, authentication exchange, and
/// payload read in one public operation. The context must remain alive until
/// any returned payload stream has been consumed or disposed.
/// </remarks>
public sealed class NuGetOperationContext : IDisposable
{
    private readonly CancellationTokenSource _operationCancellation;
    private readonly CancellationToken _operationToken;
    private readonly long _operationStarted;
    private int _disposeState;

    /// <summary>
    /// Creates a context with default request and operation deadlines.
    /// </summary>
    public NuGetOperationContext(
        CancellationToken cancellationToken = default)
        : this(
            NuGetFetchOptions.DefaultRequestTimeout,
            NuGetFetchOptions.DefaultOperationTimeout,
            cancellationToken)
    {
    }

    /// <summary>
    /// Creates a context with configured request and operation deadlines.
    /// </summary>
    public NuGetOperationContext(
        TimeSpan requestTimeout,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default)
    {
        NuGetFetchOptions.ValidateTimeout(
            requestTimeout,
            nameof(requestTimeout));
        NuGetFetchOptions.ValidateTimeout(
            operationTimeout,
            nameof(operationTimeout));
        RequestTimeout = requestTimeout;
        OperationTimeout = operationTimeout;
        CancellationToken = cancellationToken;
        _operationStarted = Stopwatch.GetTimestamp();
        _operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        _operationToken = _operationCancellation.Token;
        _operationCancellation.CancelAfter(OperationTimeout);
    }

    /// <summary>Gets the configured deadline for one request.</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Gets the configured ceiling for the complete operation.</summary>
    public TimeSpan OperationTimeout { get; }

    /// <summary>Gets the caller token carried by this context.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Throws when caller cancellation or the operation ceiling has elapsed.
    /// </summary>
    public void ThrowIfExpired()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
        ThrowIfExpiredForActiveOperation();
    }

    internal void ThrowIfExpiredForActiveOperation()
    {
        if (CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                "NuGet operation was canceled by the caller.",
                CancellationToken);
        }

        if (IsOperationExpired)
        {
            throw new NuGetOperationTimeoutException(
                OperationTimeout,
                new OperationCanceledException(
                    "NuGet operation deadline expired.",
                    _operationToken));
        }
    }

    /// <summary>Cancels outstanding work and releases deadline resources.</summary>
    public void Dispose() => DisposeCore(cancelOutstanding: true);

    internal void Complete() => DisposeCore(cancelOutstanding: false);

    private void DisposeCore(bool cancelOutstanding)
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        try
        {
            if (cancelOutstanding)
                _operationCancellation.Cancel();
        }
        finally
        {
            _operationCancellation.Dispose();
        }
    }

    internal CancellationToken OperationToken
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposeState) != 0,
                this);
            return _operationToken;
        }
    }

    internal bool IsOperationExpired =>
        _operationCancellation.IsCancellationRequested
        || Stopwatch.GetElapsedTime(_operationStarted) >= OperationTimeout;

    internal NuGetOperationDeadline CreateDeadline(
        TimeSpan clientTimeout,
        CancellationToken invocationToken,
        PackageSourceResultIdentity? source = null)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
        _ = ResolveInvocationToken(invocationToken);
        return new NuGetOperationDeadline(
            this,
            clientTimeout,
            source);
    }

    internal CancellationToken ResolveInvocationToken(
        CancellationToken invocationToken)
    {
        if (invocationToken != default
            && invocationToken != CancellationToken)
        {
            throw new ArgumentException(
                "The invocation token must match the operation context's caller token.",
                nameof(invocationToken));
        }

        return CancellationToken;
    }
}
