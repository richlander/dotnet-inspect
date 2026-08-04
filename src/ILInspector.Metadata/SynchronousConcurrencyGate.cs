namespace ILInspector.Metadata;

/// <summary>
/// Bounds concurrent work for synchronous metadata APIs.
/// </summary>
internal sealed class SynchronousConcurrencyGate
{
    static readonly TimeSpan CancellationPollInterval =
        TimeSpan.FromMilliseconds(50);

    readonly object _gate = new();
    readonly int _capacity;
    int _available;

    internal SynchronousConcurrencyGate(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _available = capacity;
    }

    internal void Enter(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (_available == 0)
            {
                // SemaphoreSlim.Wait throws on single-threaded Browser/Wasm
                // even when entry is uncontended. A single-threaded host cannot
                // reach this contention-only monitor wait.
                Monitor.Wait(_gate, CancellationPollInterval);
                cancellationToken.ThrowIfCancellationRequested();
            }

            _available--;
        }
    }

    internal void Exit()
    {
        lock (_gate)
        {
            if (_available == _capacity)
                throw new SemaphoreFullException();

            _available++;
            Monitor.Pulse(_gate);
        }
    }
}
