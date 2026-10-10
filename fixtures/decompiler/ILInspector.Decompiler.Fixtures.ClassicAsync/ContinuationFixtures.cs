#nullable enable
using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

public sealed class ContinuationFixtures
{
    object? _first;
    object? _second;
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
    public Task Work() => Task.CompletedTask;
}
