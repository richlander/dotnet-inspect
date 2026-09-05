using System.Runtime.Versioning;

namespace InspectWeb.Engine;

/// <summary>
/// Keeps Browser source acquisition within one aggregate request budget and cancels superseded
/// work. <c>BrowserEngineBoundaryTests.SourceOperations_AreExclusiveAndSuperseding</c> gates this
/// lifetime contract.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserSourceOperationCoordinator
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static SourceOperation? _current;

    internal static ValueTask<BrowserSourceOperationLease> BeginAsync() =>
        BeginAsync(new SourceOperation());

    internal static ValueTask<BrowserSourceOperationLease> BeginAsync(
        CancellationToken cancellationToken,
        Action<BrowserManagedOperationCancelReason> requestCancellation) =>
        BeginAsync(new SourceOperation(cancellationToken, requestCancellation));

    static async ValueTask<BrowserSourceOperationLease> BeginAsync(
        SourceOperation operation)
    {
        SourceOperation? superseded =
            Interlocked.Exchange(ref _current, operation);

        bool entered = false;
        try
        {
            superseded?.Cancel(BrowserManagedOperationCancelReason.Superseded);
            await Gate.WaitAsync(operation.Token);
            entered = true;
            operation.Token.ThrowIfCancellationRequested();
            return new BrowserSourceOperationLease(
                operation.Token,
                () => Complete(operation));
        }
        catch
        {
            if (entered)
                Gate.Release();
            _ = Interlocked.CompareExchange(ref _current, null, operation);
            operation.Dispose();
            throw;
        }
    }

    internal static void CancelCurrent() =>
        Volatile.Read(ref _current)?.Cancel(BrowserManagedOperationCancelReason.User);

    static void Complete(SourceOperation operation)
    {
        Gate.Release();
        _ = Interlocked.CompareExchange(ref _current, null, operation);
        operation.Dispose();
    }

    sealed class SourceOperation : IDisposable
    {
        readonly object _sync = new();
        readonly CancellationTokenSource? _cancellation;
        readonly Action<BrowserManagedOperationCancelReason>? _requestCancellation;
        bool _disposed;

        internal SourceOperation()
        {
            _cancellation = new();
            Token = _cancellation.Token;
        }

        internal SourceOperation(
            CancellationToken token,
            Action<BrowserManagedOperationCancelReason> requestCancellation)
        {
            ArgumentNullException.ThrowIfNull(requestCancellation);
            Token = token;
            _requestCancellation = requestCancellation;
        }

        internal CancellationToken Token { get; }

        internal void Cancel(BrowserManagedOperationCancelReason reason)
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    if (_requestCancellation is { } requestCancellation)
                        requestCancellation(reason);
                    else
                        _cancellation!.Cancel();
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _cancellation?.Dispose();
            }
        }
    }
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserSourceOperationLease : IDisposable
{
    Action? _release;

    internal BrowserSourceOperationLease(
        CancellationToken cancellationToken,
        Action release)
    {
        CancellationToken = cancellationToken;
        _release = release;
    }

    internal CancellationToken CancellationToken { get; }

    public void Dispose() =>
        Interlocked.Exchange(ref _release, null)?.Invoke();
}
