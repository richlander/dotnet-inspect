using System.Runtime.Versioning;

namespace InspectWeb.Engine;

/// <summary>
/// Serializes Browser package-query streams and cancels work superseded by a newer request.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageQueryOperationCoordinator
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static PackageQueryOperation? _current;

    internal static async ValueTask<BrowserPackageQueryOperationLease> BeginAsync()
    {
        var operation = new PackageQueryOperation();
        PackageQueryOperation? superseded =
            Interlocked.Exchange(ref _current, operation);
        superseded?.Cancel();

        bool entered = false;
        try
        {
            await Gate.WaitAsync(operation.Token);
            entered = true;
            operation.Token.ThrowIfCancellationRequested();
            return new BrowserPackageQueryOperationLease(
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

    static void Complete(PackageQueryOperation operation)
    {
        Gate.Release();
        _ = Interlocked.CompareExchange(ref _current, null, operation);
        operation.Dispose();
    }

    sealed class PackageQueryOperation : IDisposable
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
internal sealed class BrowserPackageQueryOperationLease : IDisposable
{
    Action? _release;

    internal BrowserPackageQueryOperationLease(
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
