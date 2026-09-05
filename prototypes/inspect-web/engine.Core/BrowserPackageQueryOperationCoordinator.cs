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

    internal static async ValueTask<BrowserPackageQueryOperationLease> BeginAsync(
        int initialMatchCredit)
    {
        var operation = new PackageQueryOperation(initialMatchCredit);
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
                operation.MatchCredit,
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

    internal static bool RequestCurrentMatches(int additionalMatchCredit) =>
        Volatile.Read(ref _current)
            ?.MatchCredit.TryAdd(additionalMatchCredit) == true;

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

        internal PackageQueryOperation(int initialMatchCredit)
        {
            MatchCredit = new BrowserPackageQueryMatchCredit(initialMatchCredit);
        }

        internal CancellationToken Token => _cancellation.Token;
        internal BrowserPackageQueryMatchCredit MatchCredit { get; }

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
                MatchCredit.Dispose();
            }
        }
    }
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserPackageQueryMatchCredit : IDisposable
{
    readonly object _sync = new();
    readonly SemaphoreSlim _available;
    bool _disposed;

    internal BrowserPackageQueryMatchCredit(int initialMatchCredit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialMatchCredit);
        _available = new SemaphoreSlim(initialMatchCredit, int.MaxValue);
    }

    internal ValueTask WaitAsync(CancellationToken cancellationToken) =>
        new(_available.WaitAsync(cancellationToken));

    internal bool TryAdd(int additionalMatchCredit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(additionalMatchCredit);
        lock (_sync)
        {
            if (_disposed)
                return false;
            _available.Release(additionalMatchCredit);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _available.Dispose();
        }
    }
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserPackageQueryOperationLease : IDisposable
{
    Action? _release;

    internal BrowserPackageQueryOperationLease(
        CancellationToken cancellationToken,
        BrowserPackageQueryMatchCredit matchCredit,
        Action release)
    {
        CancellationToken = cancellationToken;
        MatchCredit = matchCredit;
        _release = release;
    }

    internal CancellationToken CancellationToken { get; }
    internal BrowserPackageQueryMatchCredit MatchCredit { get; }

    public void Dispose() =>
        Interlocked.Exchange(ref _release, null)?.Invoke();
}
