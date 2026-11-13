using System.Threading.Tasks;

namespace ILInspector.Decompiler.Fixtures.ClassicAsync;

public sealed class ReceiverAliasFixtures
{
    public Task Work() => Task.CompletedTask;
    public Task<int> Read() => Task.FromResult(3);
    public async Task AwaitReceiver() => await Work();
    public async Task<int> AwaitReceiverResult() => await Read();
}
