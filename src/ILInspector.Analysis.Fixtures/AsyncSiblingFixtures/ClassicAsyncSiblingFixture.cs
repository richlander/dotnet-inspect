namespace ILInspector.Analysis.ClassicAsyncFixtures;

public static class ClassicAsyncSiblingFixture
{
    public static int ReadValue(int value)
        => value;

    public static Task<int> ReadValueAsync(
        int value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }

    public static async Task<int> CallsSyncSiblingFromAsync(
        int value)
    {
        await Task.Yield();
        return ReadValue(value);
    }
}

public sealed class ClassicGenericSelfSiblingFixture<T>
{
    public int Read(T value) => 0;

    public async Task<int> ReadAsync(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public static class ClassicGenericMethodSelfSiblingFixture
{
    public static T Read<T>(T value) => value;

    public static async Task<T> ReadAsync<T>(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public sealed class ClassicStateMachineCollision
{
    public int Read(int value) => value;

    public Task<int> ReadAsync(string value)
        => Task.FromResult(value.Length);

    public async Task<int> AnalyzeAsync(int value)
    {
        await Task.Yield();
        return Read(value);
    }
}

public sealed class ClassicStateMachineCollision<T>
{
    public int Read(T value) => 0;

    public Task<int> ReadAsync(T value)
        => Task.FromResult(Read(value));

    public async Task<int> AnalyzeAsync(T value)
    {
        await Task.Yield();
        return Read(value);
    }
}
