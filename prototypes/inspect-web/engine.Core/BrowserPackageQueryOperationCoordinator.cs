using System.Runtime.Versioning;

namespace InspectWeb.Engine;

internal abstract record BrowserPackageQueryMatchCreditRequestResult
{
    internal sealed record Granted(int AdditionalMatchCredit)
        : BrowserPackageQueryMatchCreditRequestResult;

    internal sealed record NotActive
        : BrowserPackageQueryMatchCreditRequestResult;
}

/// <summary>
/// Admits package-query operations by page-issued identity and routes
/// feature-owned match credit to the exact active operation.
/// </summary>
[SupportedOSPlatform("browser")]
internal static class BrowserPackageQueryOperationCoordinator
{
    static readonly BrowserManagedOperationBridge Operations = new();
    static readonly object Sync = new();
    static readonly Dictionary<
        BrowserManagedOperationId,
        BrowserPackageQueryMatchCredit> MatchCredits = [];

    internal static Task<
        BrowserManagedOperationResult<TValue, string, string>> RunAsync<TValue, TEvent>(
            BrowserManagedOperationId operationId,
            int initialMatchCredit,
            Action<TEvent>? eventCallback,
            Func<
                BrowserPackageQueryMatchCredit,
                IBrowserManagedOperationEvents<TEvent>,
                CancellationToken,
                Task<TValue>> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Operations.RunAsync<TValue, string, string, TEvent>(
            operationId,
            eventCallback,
            async (token, events) =>
            {
                using var matchCredit =
                    new BrowserPackageQueryMatchCredit(initialMatchCredit);
                lock (Sync)
                {
                    if (!MatchCredits.TryAdd(operationId, matchCredit))
                    {
                        throw new InvalidOperationException(
                            $"Package query '{operationId}' already owns match credit.");
                    }
                }

                try
                {
                    TValue value =
                        await body(matchCredit, events, token).ConfigureAwait(false);
                    return new BrowserManagedOperationBodyResult<
                        TValue,
                        string,
                        string>.Succeeded(value);
                }
                finally
                {
                    lock (Sync)
                    {
                        if (!MatchCredits.TryGetValue(
                                operationId,
                                out BrowserPackageQueryMatchCredit? active)
                            || !ReferenceEquals(active, matchCredit)
                            || !MatchCredits.Remove(operationId))
                        {
                            throw new InvalidOperationException(
                                $"Package query '{operationId}' no longer owns its match credit.");
                        }
                    }
                }
            },
            exception => new(exception.Message, exception.ToString()));
    }

    internal static BrowserManagedCancellationRequestResult RequestCancellation(
        BrowserManagedOperationId operationId,
        BrowserManagedOperationCancelReason reason) =>
        Operations.RequestCancellation(operationId, reason);

    internal static BrowserPackageQueryMatchCreditRequestResult RequestMatches(
        BrowserManagedOperationId operationId,
        int additionalMatchCredit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(additionalMatchCredit);
        BrowserPackageQueryMatchCredit? matchCredit;
        lock (Sync)
            MatchCredits.TryGetValue(operationId, out matchCredit);

        return matchCredit?.TryAdd(additionalMatchCredit) == true
            ? new BrowserPackageQueryMatchCreditRequestResult.Granted(
                additionalMatchCredit)
            : new BrowserPackageQueryMatchCreditRequestResult.NotActive();
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
