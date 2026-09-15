namespace ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts;

public static class ClassicAsyncArtifactFixtures
{
    public static async Task RecoverableAsync(
        Task<int> first,
        Task<int> second)
    {
        int left = await first;
        int right = await second;
        GC.KeepAlive((left, right));
    }

    public static async Task RemovedAsync(
        Task<int> first,
        Task<int> second)
    {
        int left = await first;
        int right = await second;
        GC.KeepAlive((left, right));
    }
}

public interface IClassicDefaultArtifactFixture
{
    async Task<int> DefaultAsync(Task<int> value) => await value;
}

sealed class ClassicDefaultArtifactFixture : IClassicDefaultArtifactFixture;

static class Program
{
    static int Main()
    {
        ClassicAsyncArtifactFixtures.RecoverableAsync(
            Task.FromResult(41),
            Task.FromResult(1)).GetAwaiter().GetResult();

        IClassicDefaultArtifactFixture defaultFixture =
            new ClassicDefaultArtifactFixture();
        GC.KeepAlive(
            defaultFixture.DefaultAsync(Task.FromResult(42))
                .GetAwaiter()
                .GetResult());
        return 0;
    }
}
