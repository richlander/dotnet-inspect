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

    internal static async ValueTask<BrowserSourceOperationLease> BeginAsync()
    {
        var operation = new SourceOperation();
        SourceOperation? superseded =
            Interlocked.Exchange(ref _current, operation);
        superseded?.Cancel();

        bool entered = false;
        try
        {
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
        Volatile.Read(ref _current)?.Cancel();

    static void Complete(SourceOperation operation)
    {
        Gate.Release();
        _ = Interlocked.CompareExchange(ref _current, null, operation);
        operation.Dispose();
    }

    sealed class SourceOperation : IDisposable
    {
        readonly object _sync = new();
        readonly CancellationTokenSource _cancellation = new();
        bool _disposed;

        internal CancellationToken Token => _cancellation.Token;

        internal void Cancel()
        {
            lock (_sync)
            {
                if (!_disposed)
                    _cancellation.Cancel();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _cancellation.Dispose();
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
