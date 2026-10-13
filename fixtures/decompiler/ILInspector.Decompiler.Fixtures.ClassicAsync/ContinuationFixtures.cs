#nullable enable
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

public sealed class ContinuationFixtures
{
    object? _first;
    object? _second;
    System.Memory<byte> _memory;
    public System.Memory<byte> Memory => _memory;
    public object? First => _first;
    public object? Second => _second;
    void Cleanup() { _first = new object(); _second = new object(); }
    public async Task AwaitReceiverCleanup()
    {
        await Work();
        Cleanup();
        _first = null;
        _second = null;
    }
    public async Task GuardedReceiverCleanup(bool enabled)
    {
        if (!enabled) return;
        await Work();
        Cleanup();
        _first = null;
        _second = null;
    }
    public async Task GuardedAwait(bool enabled)
    {
        if (!enabled) return;
        await Work();
    }
    public async Task InvertedGuardedAwait(bool disabled)
    {
        if (disabled) return;
        await Work();
    }
    public async Task CompoundGuardedAwait(bool first, bool second)
    {
        if (!first && !second) return;
        await Work();
    }
    public async Task ReferenceGuardedAwait()
    {
        if (_first is null && _second is null) return;
        await Work();
    }
    public async ValueTask CompoundReceiverCleanup()
    {
        if (_first is null && _second is null) return;
        await WorkToken(default).ConfigureAwait(false);
        Cleanup();
        _first = null;
        _second = null;
    }
    public Task WorkToken(System.Threading.CancellationToken token) => Task.CompletedTask;
    public async Task AwaitFieldReset()
    {
        await Work();
        _memory = default;
        Cleanup();
    }
    public async ValueTask GuardedAwaitFieldReset(bool enabled)
    {
        if (!enabled) return;
        await Work();
        _memory = default;
        Cleanup();
    }
    public Task Work() => Task.CompletedTask;
}
