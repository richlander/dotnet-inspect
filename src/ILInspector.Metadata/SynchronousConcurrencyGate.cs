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
    readonly Action? _waitStarted;
    int _available;

    internal SynchronousConcurrencyGate(
        int capacity,
        Action? waitStarted = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _waitStarted = waitStarted;
        _available = capacity;
    }

    internal void Enter(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool waitStarted = false;
            while (_available == 0)
            {
                if (!waitStarted)
                {
                    waitStarted = true;
                    _waitStarted?.Invoke();
                }

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
