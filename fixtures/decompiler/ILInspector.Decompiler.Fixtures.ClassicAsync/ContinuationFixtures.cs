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
    public Task Work() => Task.CompletedTask;
}
